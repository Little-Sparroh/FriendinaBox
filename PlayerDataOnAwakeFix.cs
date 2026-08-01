using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

/// <summary>
/// Vanilla PlayerData.OnAwake has a bug when cleaning null mod upgrades:
///
///   for (int l = 0; l < invalidUpgrades.Count; l++)
///       if (match) invalidUpgrades.RemoveAt(num2);  // num2 indexes datum.Value, not invalidUpgrades!
///
/// That throws ArgumentOutOfRangeException and aborts boot when modded save data exists.
/// This transpiler rewrites RemoveAt(num2) → RemoveAt(l) in that loop.
/// </summary>
[HarmonyPatch(typeof(PlayerData), nameof(PlayerData.OnAwake))]
internal static class PlayerDataOnAwakeFix
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        MethodInfo listRemoveAt = AccessTools.Method(typeof(List<>).MakeGenericType(AccessTools.Inner(typeof(PlayerData), "UpgradeInfoBackup")
            ?? FindUpgradeInfoBackupType()
            ?? typeof(object)), "RemoveAt", new[] { typeof(int) });

        // Fallback: match any List.RemoveAt callvirt with ldloc that looks like the bug pattern.
        // Safer approach: replace the specific wrong RemoveAt argument.
        //
        // Pattern near the bug (IL roughly):
        //   ldloc.s invalidUpgrades list (or ldfld)
        //   ldloc.s num2   <-- WRONG index
        //   callvirt List.RemoveAt
        //
        // We look for RemoveAt where the index load is the outer loop variable used for
        // datum.Value (typically a higher local) while a nearby local `l` is the inner index.
        //
        // Practical approach used here: finalizer on OnAwake already softens remaining issues;
        // this transpiler tries to fix the known RemoveAt(num2) → use the inner loop local.
        //
        // Because local indices vary by compiler, we also rely on the Prefix gear-register
        // timing fix. For the RemoveAt bug we use a Harmony finalizer + a targeted fix via
        // matching consecutive ldloc + RemoveAt after a blt inner loop on `l`.

        int patches = 0;

        // Find: callvirt RemoveAt preceded by ldloc.* where we can swap to the previous ldloc
        // that was used as the inner for-loop index. Heuristic: when we see
        //   ldarg.0 / ldsfld Instance
        //   ldfld invalidUpgrades
        //   ldloc X   (num2)
        //   callvirt RemoveAt
        // and earlier in the same basic block there was
        //   ldloc Y   (l)
        // used with invalidUpgrades.Count comparison,
        // replace ldloc X with ldloc Y.
        //
        // Simpler reliable fix: Harmony Finalizer swallows ArgumentOutOfRangeException from OnAwake
        // is already planned. For transpiler, patch every
        //   ldfld invalidUpgrades / ldloc / RemoveAt
        // to use the most recent "inner loop index" local stored when we see:
        //   ldfld invalidUpgrades / callvirt get_Count / ldloc / blt

        int lastInnerLoopLocal = -1;
        FieldInfo invalidField = AccessTools.Field(typeof(PlayerData), "invalidUpgrades");
        MethodInfo removeAt = null;

        for (int i = 0; i < list.Count; i++)
        {
            // Track inner loop: invalidUpgrades.Count compared to a local
            if (invalidField != null &&
                i + 3 < list.Count &&
                list[i].LoadsField(invalidField) &&
                list[i + 1].opcode == OpCodes.Callvirt &&
                list[i + 1].operand is MethodInfo mi &&
                mi.Name == "get_Count")
            {
                // next is often ldloc for loop variable
                if (IsLdloc(list[i + 2], out int localIndex))
                    lastInnerLoopLocal = localIndex;
            }

            // Detect RemoveAt on a list
            if (list[i].opcode == OpCodes.Callvirt &&
                list[i].operand is MethodInfo remove &&
                remove.Name == "RemoveAt" &&
                remove.GetParameters().Length == 1)
            {
                removeAt = remove;
                // Previous instruction should be the index
                if (i >= 1 && IsLdloc(list[i - 1], out int indexLocal) &&
                    lastInnerLoopLocal >= 0 &&
                    indexLocal != lastInnerLoopLocal &&
                    i >= 2)
                {
                    // Check that the list being modified is invalidUpgrades (a few ops before)
                    bool isInvalidList = false;
                    for (int b = i - 2; b >= Math.Max(0, i - 6); b--)
                    {
                        if (list[b].LoadsField(invalidField))
                        {
                            isInvalidList = true;
                            break;
                        }
                    }

                    if (isInvalidList)
                    {
                        list[i - 1] = Ldloc(lastInnerLoopLocal);
                        patches++;
                        FriendinaBoxPlugin.Logger?.LogInfo(
                            $"[FriendinaBox] OnAwake fix: RemoveAt local {indexLocal} → {lastInnerLoopLocal}.");
                    }
                }
            }
        }

        if (patches == 0)
        {
            FriendinaBoxPlugin.Logger?.LogWarning(
                "[FriendinaBox] OnAwake RemoveAt transpiler found 0 sites — relying on finalizer guard.");
        }
        else
        {
            FriendinaBoxPlugin.Logger?.LogInfo(
                $"[FriendinaBox] OnAwake RemoveAt transpiler patched {patches} site(s).");
        }

        return list;
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception)
    {
        // Soften residual vanilla cleanup crashes so the game can finish booting.
        if (__exception is ArgumentOutOfRangeException or NullReferenceException)
        {
            FriendinaBoxPlugin.Logger?.LogError(
                "[FriendinaBox] Swallowed PlayerData.OnAwake exception so boot can continue:\n" +
                __exception);
            return null;
        }

        return __exception;
    }

    private static Type FindUpgradeInfoBackupType()
    {
        foreach (Type t in typeof(PlayerData).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (t.Name.Contains("UpgradeInfoBackup"))
                return t;
        }

        return null;
    }

    private static bool IsLdloc(CodeInstruction ins, out int localIndex)
    {
        localIndex = -1;
        if (ins.opcode == OpCodes.Ldloc_0) { localIndex = 0; return true; }
        if (ins.opcode == OpCodes.Ldloc_1) { localIndex = 1; return true; }
        if (ins.opcode == OpCodes.Ldloc_2) { localIndex = 2; return true; }
        if (ins.opcode == OpCodes.Ldloc_3) { localIndex = 3; return true; }
        if (ins.opcode == OpCodes.Ldloc_S || ins.opcode == OpCodes.Ldloc)
        {
            if (ins.operand is LocalBuilder lb)
            {
                localIndex = lb.LocalIndex;
                return true;
            }

            if (ins.operand is byte b)
            {
                localIndex = b;
                return true;
            }

            if (ins.operand is int i)
            {
                localIndex = i;
                return true;
            }
        }

        return false;
    }

    private static CodeInstruction Ldloc(int localIndex)
    {
        switch (localIndex)
        {
            case 0: return new CodeInstruction(OpCodes.Ldloc_0);
            case 1: return new CodeInstruction(OpCodes.Ldloc_1);
            case 2: return new CodeInstruction(OpCodes.Ldloc_2);
            case 3: return new CodeInstruction(OpCodes.Ldloc_3);
            default:
                if (localIndex <= 255)
                    return new CodeInstruction(OpCodes.Ldloc_S, (byte)localIndex);
                return new CodeInstruction(OpCodes.Ldloc, localIndex);
        }
    }
}
