using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Pigeon.Movement;


/// <summary>
/// Ouroboros filters upgrades with <see cref="Upgrade.UpgradeFlags.Coop"/> when solo:
///
///   if (coopFlag && GameManager.players.Count == 1) exclude;
///
/// While Friend in a Box is equipped, treat the player as "not solo" for that check
/// so multiplayer-only upgrades can enter the temp mission pool.
/// </summary>
[HarmonyPatch]
internal static class CoopUnlockHook
{
    private static MethodBase TargetMethod()
    {
        // private int FilterUpgrades(UpgradeFilterParams filter, ref Random rand, int direction = 0)
        MethodInfo method = AccessTools.Method(typeof(PlayerData), "FilterUpgrades");
        if (method == null)
            FriendinaBoxPlugin.Logger?.LogError("[FriendinaBox] Could not find PlayerData.FilterUpgrades.");
        else
            FriendinaBoxPlugin.Logger?.LogInfo("[FriendinaBox] Patching PlayerData.FilterUpgrades for Coop unlock.");
        return method;
    }

    /// <summary>
    /// Replaces the solo coop gate so equipped Friend counts as a second "player".
    /// Matches IL that loads GameManager.players, calls get_Count, and compares to 1
    /// in the Coop-flag branch — we inject a call that returns an effective count.
    /// </summary>
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo playersCountGetter = AccessTools.PropertyGetter(typeof(List<Player>), "Count");
        FieldInfo playersField = AccessTools.Field(typeof(GameManager), "players");
        MethodInfo effectiveCount = AccessTools.Method(typeof(CoopUnlockHook), nameof(GetEffectivePlayerCount));

        if (playersCountGetter == null || playersField == null || effectiveCount == null)
        {
            FriendinaBoxPlugin.Logger?.LogError(
                "[FriendinaBox] CoopUnlockHook transpiler missing members — coop unlock disabled.");
            return instructions;
        }

        var list = new List<CodeInstruction>(instructions);
        int patches = 0;

        for (int i = 0; i < list.Count - 1; i++)
        {
            // pattern: ldsfld GameManager.players / callvirt get_Count
            if (list[i].opcode == OpCodes.Ldsfld &&
                Equals(list[i].operand, playersField) &&
                list[i + 1].opcode == OpCodes.Callvirt &&
                Equals(list[i + 1].operand, playersCountGetter))
            {
                // Replace get_Count with our effective count helper (still consumes the list).
                list[i + 1] = new CodeInstruction(OpCodes.Call, effectiveCount);
                patches++;
            }
        }

        FriendinaBoxPlugin.Logger?.LogInfo(
            $"[FriendinaBox] CoopUnlockHook transpiler applied {patches} player-count replacement(s).");

        return list;
    }

    /// <summary>
    /// Returns GameManager.players.Count, but at least 2 when Friend is equipped
    /// so the solo Coop filter does not exclude multiplayer upgrades.
    /// </summary>
    public static int GetEffectivePlayerCount(List<Player> players)
    {
        int count = players?.Count ?? 0;
        if (count <= 1 && FriendinaBoxBehaviour.IsEquippedOnLocalPlayer())
            return 2;
        return count;
    }
}
