using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;

/// <summary>
/// Fixes equipping runtime-cloned custom grenades.
///
/// Gear select calls <c>Player.SpawnGear_Server(slot, allGearIndex, ...)</c> and NGO
/// instantiates <c>Global.AllGear[allGearIndex]</c> as a network prefab. A runtime
/// catalog clone is not a valid registered NetworkObject prefab → NRE.
///
/// Strategy:
///  1. Prefix remaps our catalog AllGear index → base Incendiary index (ref param).
///  2. Postfix stamps Friend identity onto the live instance (with retry if async).
///  3. GearSelectionWindow only null-guards OnOpen/OnClose — does NOT double-spawn.
/// </summary>
internal static class SpawnGearHooks
{
    [ThreadStatic]
    private static bool RemappingCustomSpawn;

    [ThreadStatic]
    private static int RemappedSlot;

    public static void Apply(Harmony harmony)
    {
        Type playerType = typeof(Player);

        MethodInfo target = AccessTools.Method(playerType, "SpawnGear_Server",
            new[] { typeof(int), typeof(int), typeof(bool), typeof(bool), typeof(int), typeof(int) });

        if (target == null)
        {
            target = AccessTools.Method(playerType, "SpawnGear_Server",
                new[] { typeof(int), typeof(int), typeof(bool), typeof(bool) });
        }

        if (target == null)
        {
            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(playerType))
            {
                if (m.Name == "SpawnGear_Server")
                {
                    target = m;
                    break;
                }
            }
        }

        if (target == null)
        {
            FriendinaBoxPlugin.Logger?.LogError("[FriendinaBox] Could not find Player.SpawnGear_Server to patch.");
            return;
        }

        MethodInfo prefix = AccessTools.Method(typeof(SpawnGearHooks), nameof(SpawnGearServerPrefix_6));
        ParameterInfo[] ps = target.GetParameters();
        if (ps.Length == 4)
            prefix = AccessTools.Method(typeof(SpawnGearHooks), nameof(SpawnGearServerPrefix_4));

        harmony.Patch(target,
            prefix: new HarmonyMethod(prefix),
            postfix: new HarmonyMethod(typeof(SpawnGearHooks), nameof(SpawnGearServerPostfix)));

        FriendinaBoxPlugin.Logger?.LogInfo(
            $"[FriendinaBox] Patched {target.DeclaringType.FullName}.{target.Name} " +
            $"({ps.Length} params) with {prefix.Name}.");
    }

    private static void SpawnGearServerPrefix_6(
        Player __instance,
        int slot,
        ref int allGearIndex,
        bool equip,
        bool despawn,
        int skinID,
        int skinSeed)
    {
        TryRemap(slot, ref allGearIndex);
    }

    private static void SpawnGearServerPrefix_4(
        Player __instance,
        int slot,
        ref int allGearIndex,
        bool equip,
        bool despawn)
    {
        TryRemap(slot, ref allGearIndex);
    }

    private static void TryRemap(int slot, ref int allGearIndex)
    {
        RemappingCustomSpawn = false;

        if (Global.Instance?.AllGear == null)
            return;

        if (allGearIndex < 0 || allGearIndex >= Global.Instance.AllGear.Length)
            return;

        IUpgradable requested = Global.Instance.AllGear[allGearIndex];
        if (!IsOurCatalogGear(requested))
            return;

        int baseIndex = ResolveBaseIndex();
        if (baseIndex < 0)
        {
            FriendinaBoxPlugin.Logger?.LogError(
                "[FriendinaBox] Cannot remap spawn — base Incendiary AllGear index unknown. " +
                "Aborting custom equip to avoid NRE.");
            return;
        }

        FriendinaBoxPlugin.Logger?.LogInfo(
            $"[FriendinaBox] Remap SpawnGear slot={slot} index {allGearIndex} → base {baseIndex} " +
            $"(api={requested.Info?.APIName}).");

        allGearIndex = baseIndex;
        RemappingCustomSpawn = true;
        RemappedSlot = slot;
    }

    private static int ResolveBaseIndex()
    {
        if (Global.Instance?.AllGear == null)
            return -1;

        int baseIndex = GrenadeRegistration.BaseAllGearIndex;
        if (baseIndex >= 0 && baseIndex < Global.Instance.AllGear.Length)
        {
            IUpgradable at = Global.Instance.AllGear[baseIndex];
            if (at != null && !IsOurCatalogGear(at))
                return baseIndex;
        }

        if (GrenadeRegistration.BaseGrenadePrefab != null)
        {
            int idx = Array.IndexOf(Global.Instance.AllGear, (IUpgradable)GrenadeRegistration.BaseGrenadePrefab);
            if (idx >= 0)
            {
                GrenadeRegistration.SetBaseAllGearIndex(idx);
                return idx;
            }
        }

        if (GrenadeRegistration.BaseNetworkPrefab != null)
        {
            for (int i = 0; i < Global.Instance.AllGear.Length; i++)
            {
                if (Global.Instance.AllGear[i] is Component c &&
                    c.gameObject == GrenadeRegistration.BaseNetworkPrefab &&
                    !IsOurCatalogGear(Global.Instance.AllGear[i]))
                {
                    GrenadeRegistration.SetBaseAllGearIndex(i);
                    return i;
                }
            }
        }

        for (int i = 0; i < Global.Instance.AllGear.Length; i++)
        {
            if (Global.Instance.AllGear[i] is GrenadeGear && !IsOurCatalogGear(Global.Instance.AllGear[i]))
            {
                GrenadeRegistration.SetBaseAllGearIndex(i);
                return i;
            }
        }

        return -1;
    }

    private static void SpawnGearServerPostfix(Player __instance)
    {
        if (!RemappingCustomSpawn)
            return;

        RemappingCustomSpawn = false;
        int slot = RemappedSlot;

        try
        {
            if (!TryStampCustomIdentityOnSlot(__instance, slot))
            {
                // NGO spawn can finish after this postfix — retry briefly.
                FriendinaBoxPlugin plugin = FriendinaBoxPlugin.Instance;
                if (plugin != null)
                    plugin.StartCoroutine(StampWhenReady(__instance, slot));
            }
        }
        catch (Exception ex)
        {
            FriendinaBoxPlugin.Logger?.LogError($"[FriendinaBox] Post-spawn stamp failed: {ex}");
        }
    }

    private static IEnumerator StampWhenReady(Player player, int slot)
    {
        // Wait a few frames for Gear[slot] to be assigned after network spawn.
        for (int attempt = 0; attempt < 30; attempt++)
        {
            yield return null;
            if (player == null)
                yield break;

            try
            {
                if (TryStampCustomIdentityOnSlot(player, slot))
                    yield break;
            }
            catch (Exception ex)
            {
                FriendinaBoxPlugin.Logger?.LogWarning(
                    $"[FriendinaBox] Deferred stamp attempt {attempt}: {ex.Message}");
            }
        }

        FriendinaBoxPlugin.Logger?.LogWarning(
            $"[FriendinaBox] Deferred stamp gave up on Gear[{slot}] after retries.");
    }

    /// <returns>True if live gear was found and stamped.</returns>
    private static bool TryStampCustomIdentityOnSlot(Player player, int slot)
    {
        if (player?.Gear == null || slot < 0 || slot >= player.Gear.Length)
            return false;

        IGear live = player.Gear[slot];
        if (live == null)
            return false;

        bool alreadyIdentity =
            live.Info != null && live.Info.APIName == FriendinaBoxPlugin.GearApiName &&
            live.gameObject != null && live.gameObject.GetComponent<FriendinaBoxBehaviour>() != null;

        IUpgradable catalog = FriendinaBoxPlugin.CustomGrenadePrefab
            ?? GrenadeRegistration.CatalogGear
            ?? FindCatalogInAllGear();

        if (catalog == null)
        {
            FriendinaBoxPlugin.Logger?.LogWarning("[FriendinaBox] Catalog gear missing during stamp.");
            return false;
        }

        if (!alreadyIdentity)
        {
            live.Prefab = catalog;

            if (catalog.Info != null)
                TryAssignInfo(live, catalog.Info);

            if (live.gameObject != null)
            {
                FriendinaBoxBehaviour behaviour = live.gameObject.GetComponent<FriendinaBoxBehaviour>();
                if (behaviour == null)
                    behaviour = live.gameObject.AddComponent<FriendinaBoxBehaviour>();

                FriendinaBoxBehaviour templateBehaviour = null;
                if (catalog is Component cc)
                    templateBehaviour = cc.GetComponent<FriendinaBoxBehaviour>();

                behaviour.InitializeAsPrefab(
                    templateBehaviour != null ? templateBehaviour.Description : FriendinaBoxPlugin.GearDescription);

                if (templateBehaviour != null)
                    behaviour.CopySnapshotFrom(templateBehaviour);
            }
        }
        else
        {
            // Ensure Prefab pointer is catalog even if Info was already stamped.
            live.Prefab = catalog;
        }

        // Setup() already ran ApplyUpgrades() while Prefab was still Incendiary.
        // Re-apply now that Prefab/Info point at Friend so equipped upgrades + HUD bind correctly.
        try
        {
            live.ApplyUpgrades();

            if (FriendinaBoxBehaviour.TryGet(live, out FriendinaBoxBehaviour b))
                FriendCombatHooks.EnsureBound(live, b.GrenadeData);
        }
        catch (Exception ex)
        {
            FriendinaBoxPlugin.Logger?.LogWarning(
                $"[FriendinaBox] Post-stamp ApplyUpgrades failed on Gear[{slot}]: {ex.Message}");
        }

        FriendinaBoxPlugin.Logger?.LogInfo(
            $"[FriendinaBox] Stamped custom identity on Gear[{slot}] ({FriendinaBoxPlugin.GearApiName}) + reapplied upgrades.");
        return true;
    }



    private static IUpgradable FindCatalogInAllGear()
    {
        if (Global.Instance?.AllGear == null)
            return null;
        for (int i = 0; i < Global.Instance.AllGear.Length; i++)
        {
            IUpgradable g = Global.Instance.AllGear[i];
            if (IsOurCatalogGear(g))
                return g;
        }
        return null;
    }

    internal static bool IsOurCatalogGear(IUpgradable gear)
    {
        if (gear == null)
            return false;

        if (gear == FriendinaBoxPlugin.CustomGrenadePrefab || gear == GrenadeRegistration.CatalogGear)
            return true;

        if (gear.Info != null && gear.Info.APIName == FriendinaBoxPlugin.GearApiName)
            return true;

        if (gear.Info != null && gear.Info.ID == FriendinaBoxPlugin.GearId)
            return true;

        return false;
    }

    private static void TryAssignInfo(IGear live, GearInfo info)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type t = live.GetType();
        while (t != null)
        {
            FieldInfo f = t.GetField("<Info>k__BackingField", flags) ?? t.GetField("Info", flags);
            if (f != null)
            {
                f.SetValue(live, info);
                return;
            }
            PropertyInfo p = t.GetProperty("Info", flags);
            if (p != null && p.CanWrite)
            {
                p.SetValue(live, info);
                return;
            }
            t = t.BaseType;
        }
    }
}

/// <summary>
/// GearSlot.Update only animates the "new upgrades" badge color via Global.GetRarity.
/// That path can NRE during boot / custom gear inject; swallow so the menu stays usable.
/// </summary>
[HarmonyPatch(typeof(GearSlot), "Update")]
internal static class GearSlotUpdateHook
{
    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception)
    {
        if (__exception is NullReferenceException)
            return null;
        return __exception;
    }
}

/// <summary>
/// Null-safe gear menu open/close. Does NOT spawn gear — vanilla OnCloseCallback +
/// SpawnGearHooks remap/stamp handle equip. Double-spawning here caused first-equip failure.
/// </summary>
[HarmonyPatch(typeof(GearSelectionWindow))]
internal static class GearSelectionWindowHooks
{
    [HarmonyPatch("OnOpen")]
    [HarmonyFinalizer]
    private static Exception OnOpenFinalizer(Exception __exception)
    {
        if (__exception is NullReferenceException)
        {
            FriendinaBoxPlugin.Logger?.LogError(
                "[FriendinaBox] GearSelectionWindow.OnOpen NRE (null Gear slot after failed spawn).\n" +
                __exception);
            return null;
        }
        return __exception;
    }

    [HarmonyPatch("OnCloseCallback")]
    [HarmonyPrefix]
    private static void OnCloseCallbackPrefix()
    {
        // Ensure base index is known before vanilla IndexOf + SpawnGear_ServerRpc runs.
        // Prefix remap on SpawnGear_Server handles catalog → Incendiary.
        if (GrenadeRegistration.BaseAllGearIndex < 0 && Global.Instance?.AllGear != null)
        {
            for (int g = 0; g < Global.Instance.AllGear.Length; g++)
            {
                if (Global.Instance.AllGear[g] is GrenadeGear &&
                    !SpawnGearHooks.IsOurCatalogGear(Global.Instance.AllGear[g]))
                {
                    GrenadeRegistration.SetBaseAllGearIndex(g);
                    break;
                }
            }
        }
    }

    [HarmonyPatch("OnCloseCallback")]
    [HarmonyFinalizer]
    private static Exception OnCloseFinalizer(Exception __exception)
    {
        if (__exception is NullReferenceException)
        {
            FriendinaBoxPlugin.Logger?.LogError(
                "[FriendinaBox] GearSelectionWindow.OnCloseCallback NRE (null Gear[i] after failed spawn).\n" +
                __exception);
            return null;
        }
        return __exception;
    }
}
