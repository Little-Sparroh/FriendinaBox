using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

/// <summary>
/// Friend in a Box — custom deployable grenade for Mycopunk.
///
/// Phase 1:
///  - Clones the vanilla Incendiary Grenade prefab at runtime
///  - Registers as equippable throwable gear
///  - Lands as a proximity mine (duration + detect radius) instead of instant boom
///  - While equipped, unlocks Ouroboros UpgradeFlags.Coop drops in solo
///  - Mine-path + mode converter upgrades
///  - Turret/mortar fire real RailBullet / MortarBullet
/// </summary>
[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
[MycoMod(null, ModFlags.IsSandbox)]
public class FriendinaBoxPlugin : BaseUnityPlugin
{
    public const string PluginGUID = "sparroh.friendinabox";
    public const string PluginName = "FriendinaBox";
    public const string PluginVersion = "1.0.0";

    /// <summary>Stable numeric GearInfo.ID — high range to avoid vanilla / other mods.</summary>
    public const int GearId = 92100;

    /// <summary>Value of GearInfo.APIName — used by PlayerData.FindGear.</summary>
    public const string GearApiName = "friend_in_a_box";

    public const string GearDisplayName = "Friend in a Box";
    public const string GearDescription =
        "Deployable ally grenade. Lands as a proximity mine. While equipped, enables multiplayer-only upgrades in Ouroboros.";

    // Upgrade ids — keep unique per mod.
    public const int UpgradeWiderNetId = 92101;
    public const int UpgradeLongWatchId = 92102;
    public const int UpgradeQuickDeployId = 92103;
    public const int UpgradeLingeringGiftId = 92104;
    public const int UpgradePartingBoostId = 92105;
    public const int UpgradeTurretModeId = 92106;
    public const int UpgradeMortarModeId = 92107;
    public const int UpgradeDroneModeId = 92108;
    public const int UpgradeSquadDropId = 92109;
    public const int UpgradeSympatheticLinkId = 92110;
    public const int UpgradeFieldRechargeId = 92111;
    public const int UpgradeOvertimeId = 92112;
    public const int UpgradePaintedTargetsId = 92113;
    public const int UpgradeScuttleChargeId = 92114;
    public const int UpgradeReactiveShellId = 92115;
    public const int UpgradeCalibratedLinkId = 92116;
    public const int UpgradeDesignatedTargetId = 92117;
    public const int UpgradeHiveKinId = 92118;




    internal static new ManualLogSource Logger;
    internal static FriendinaBoxPlugin Instance;

    /// <summary>Registered prefab / gear instance (null until registration succeeds).</summary>
    public static IUpgradable CustomGrenadePrefab;

    private Harmony _harmony;
    private bool _gearRegistered;
    private bool _upgradesRegistered;

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;

        _harmony = new Harmony(PluginGUID);
        // Gear must exist BEFORE PlayerData.OnAwake walks AllGear / fires upgrade callbacks.
        _harmony.PatchAll(typeof(PlayerDataOnAwakePrefix));
        _harmony.PatchAll(typeof(PlayerDataOnAwakeFix));
        _harmony.PatchAll(typeof(GlobalLoadHook));
        _harmony.PatchAll(typeof(FriendGrenadeHooks));
        _harmony.PatchAll(typeof(CoopUnlockHook));
        _harmony.PatchAll(typeof(GearSelectionWindowHooks));
        _harmony.PatchAll(typeof(GearSlotUpdateHook));
        _harmony.PatchAll(typeof(FriendPlayerSpawnHook));
        _harmony.PatchAll(typeof(SwarmFriendFireHooks));
        SpawnGearHooks.Apply(_harmony);




        // Preferred upgrade path: when upgrade tables are ready (or immediately if already ready).
        PlayerData.AddRegisterUpgradesCallback(RegisterUpgrades);

        // In case Global already loaded before this plugin awoke.
        TryRegisterGear("Awake");

        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
    }

    private void OnDestroy()
    {
        FriendDeployTracker.Clear();
        _harmony?.UnpatchSelf();
        _harmony = null;
        Instance = null;
    }

    /// <summary>Called after PlayerData.OnAwake so GearData + CreateUpgrade are safe.</summary>
    internal void OnPlayerDataReady()
    {
        try
        {
            GrenadeRegistration.EnsurePlayerDataEntry(autoUnlock: true, Logger);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[FriendinaBox] EnsurePlayerDataEntry: {ex.Message}");
        }

        // Gear may already be in AllGear from the prefix; still ensure flag + upgrades.
        if (!_gearRegistered)
            TryRegisterGear("PlayerData ready");
        else if (CustomGrenadePrefab == null)
            CustomGrenadePrefab = GrenadeRegistration.CatalogGear;

        RegisterUpgrades();
    }

    internal void TryRegisterGear(string reason)
    {
        if (_gearRegistered)
        {
            // Still allow upgrade pass if gear exists but upgrades were deferred.
            if (!_upgradesRegistered && (CustomGrenadePrefab != null || GrenadeRegistration.CatalogGear != null))
                RegisterUpgrades();
            return;
        }


        if (Global.Instance == null || Global.Instance.AllGear == null || Global.Instance.AllGear.Length == 0)
        {
            Logger.LogDebug($"[FriendinaBox] Global.AllGear not ready yet ({reason}).");
            return;
        }

        try
        {
            if (!GrenadeRegistration.TryCreateAndRegister(
                    modGuid: PluginGUID,
                    gearId: GearId,
                    apiName: GearApiName,
                    displayName: GearDisplayName,
                    description: GearDescription,
                    baseTypeName: "IncendiaryGrenade",
                    autoUnlock: true,
                    log: Logger,
                    out CustomGrenadePrefab))
            {
                return;
            }

            _gearRegistered = true;
            Logger.LogInfo(
                $"[FriendinaBox] Registered gear '{GearDisplayName}' " +
                $"(api={GearApiName}, id={GearId}) via {reason}.");

            // Resolve Cycler rail + enemy mortar bullet prefabs while AllGear is hot.
            try
            {
                FriendBulletCache.EnsureCached();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[FriendinaBox] Bullet cache failed (non-fatal): {ex.Message}");
            }

            // Upgrades may have been deferred until gear existed.
            RegisterUpgrades();
        }
        catch (Exception ex)
        {
            Logger.LogError($"[FriendinaBox] Gear registration failed: {ex}");
        }
    }

    private void RegisterUpgrades()
    {
        if (_upgradesRegistered)
            return;

        try
        {
            // Prefer our catalog reference — never call FindGear during early boot
            // (PlayerData tables may be mid-init and FindGear can NRE).
            IUpgradable gear = CustomGrenadePrefab ?? GrenadeRegistration.CatalogGear;
            if (gear == null)
            {
                Logger.LogDebug("[FriendinaBox] Deferring upgrades until gear is registered.");
                return;
            }

            // PlayerData must be far enough along that CreateUpgrade works.
            if (PlayerData.Instance == null)
            {
                Logger.LogDebug("[FriendinaBox] Deferring upgrades until PlayerData.Instance exists.");
                return;
            }

            bool ok = true;

            ok &= CreateUpgrade(gear, UpgradeWiderNetId, "Wider Net",
                "Increases mine detection radius and explosion radius.",
                Rarity.Standard, new UpgradeProperty[] { new FriendWiderNetProperty() },
                UpgradeRegistration.CreateRadiusPattern(), Upgrade.UpgradeFlags.CanStack);

            ok &= CreateUpgrade(gear, UpgradeLongWatchId, "Long Watch",
                "Mine lasts longer, but throw cooldown is slower.",
                Rarity.Standard, new UpgradeProperty[] { new FriendLongWatchProperty() },
                UpgradeRegistration.CreateBarPattern(), Upgrade.UpgradeFlags.CanStack);

            ok &= CreateUpgrade(gear, UpgradeQuickDeployId, "Quick Deploy",
                "Throw cooldown is faster, but mine duration is shorter.",
                Rarity.Standard, new UpgradeProperty[] { new FriendQuickDeployProperty() },
                UpgradeRegistration.CreateBarPattern(), Upgrade.UpgradeFlags.CanStack);

            ok &= CreateUpgrade(gear, UpgradeLingeringGiftId, "Lingering Gift",
                "When the mine expires without detonating, leave an acid puddle.",
                Rarity.Rare, new UpgradeProperty[] { new FriendLingeringGiftProperty() },
                UpgradeRegistration.CreateSinglePattern(), Upgrade.UpgradeFlags.None);

            ok &= CreateUpgrade(gear, UpgradePartingBoostId, "Parting Boost",
                "When the mine expires without detonating, gain a burst of move speed.",
                Rarity.Rare, new UpgradeProperty[] { new FriendPartingBoostProperty() },
                UpgradeRegistration.CreateSinglePattern(), Upgrade.UpgradeFlags.None);

            ok &= CreateUpgrade(gear, UpgradeTurretModeId, "Sentry Kit",
                "Deploy as a stationary auto-turret instead of a mine. (Green box)",
                Rarity.Exotic, new UpgradeProperty[] { new FriendTurretModeProperty() },
                UpgradeRegistration.CreateSinglePattern(), Upgrade.UpgradeFlags.None);

            ok &= CreateUpgrade(gear, UpgradeMortarModeId, "Lobber Kit",
                "Deploy as a mortar that lobs AoE at range. (Orange box)",
                Rarity.Exotic, new UpgradeProperty[] { new FriendMortarModeProperty() },
                UpgradeRegistration.CreateSinglePattern(), Upgrade.UpgradeFlags.None);

            ok &= CreateUpgrade(gear, UpgradeDroneModeId, "Buddy Protocol",
                "Deploy as a drone that hovers near you. Suicide dives by default; combines with Sentry/Lobber. (Cyan box)",
                Rarity.Exotic, new UpgradeProperty[] { new FriendDroneModeProperty() },
                UpgradeRegistration.CreateSinglePattern(), Upgrade.UpgradeFlags.None);

            ok &= CreateUpgrade(gear, UpgradeSquadDropId, "Squad Drop",
                "Each throw deploys multiple Friends in a spread. Stackable.",
                Rarity.Rare, new UpgradeProperty[] { new FriendSquadDropProperty() },
                UpgradeRegistration.CreateBarPattern(), Upgrade.UpgradeFlags.CanStack);


            ok &= CreateUpgrade(gear, UpgradeSympatheticLinkId, "Sympathetic Link",
                "A portion of Friend damage returns as blue overhealth (does not fill base HP first).",
                Rarity.Standard, new UpgradeProperty[] { new FriendSympatheticLinkProperty() },
                UpgradeRegistration.CreateSinglePattern(), Upgrade.UpgradeFlags.CanStack);

            ok &= CreateUpgrade(gear, UpgradeFieldRechargeId, "Field Recharge",
                "Friend kills refund grenade charge.",
                Rarity.Standard, new UpgradeProperty[] { new FriendFieldRechargeProperty() },
                UpgradeRegistration.CreateSinglePattern(), Upgrade.UpgradeFlags.CanStack);

            ok &= CreateUpgrade(gear, UpgradeOvertimeId, "Overtime",
                "Friend kills extend the active deploy duration.",
                Rarity.Standard, new UpgradeProperty[] { new FriendOvertimeProperty() },
                UpgradeRegistration.CreateSinglePattern(), Upgrade.UpgradeFlags.CanStack);

            ok &= CreateUpgrade(gear, UpgradePaintedTargetsId, "Painted Targets",
                "Enemies hit by Friend are marked and take bonus damage briefly.",
                Rarity.Rare, new UpgradeProperty[] { new FriendPaintedTargetsProperty() },
                UpgradeRegistration.CreateRadiusPattern(), Upgrade.UpgradeFlags.CanStack);

            ok &= CreateUpgrade(gear, UpgradeScuttleChargeId, "Scuttle Charge",
                "Shoot your deployable to detonate it early. Power scales with remaining duration.",
                Rarity.Exotic, new UpgradeProperty[] { new FriendScuttleChargeProperty() },
                UpgradeRegistration.CreateSinglePattern(), Upgrade.UpgradeFlags.None);

            ok &= CreateUpgrade(gear, UpgradeReactiveShellId, "Reactive Shell",
                "When you take damage, gain blue overhealth. Does not top up base HP first.",
                Rarity.Rare, new UpgradeProperty[] { new FriendReactiveShellProperty() },
                UpgradeRegistration.CreateSinglePattern(), Upgrade.UpgradeFlags.CanStack);

            ok &= CreateUpgrade(gear, UpgradeCalibratedLinkId, "Calibrated Link",
                "Turret/mortar/drone fire blends toward your primary weapon's damage, effect, fire rate, and bullets per shot.",
                Rarity.Exotic, new UpgradeProperty[] { new FriendCalibratedLinkProperty() },
                UpgradeRegistration.CreateBarPattern(), Upgrade.UpgradeFlags.CanStack);


            ok &= CreateUpgrade(gear, UpgradeDesignatedTargetId, "Designated Target",
                "After you damage an enemy, Friend focuses that target while it stays in range.",
                Rarity.Rare, new UpgradeProperty[] { new FriendDesignatedTargetProperty() },
                UpgradeRegistration.CreateSinglePattern(), Upgrade.UpgradeFlags.None);

            ok &= CreateUpgrade(gear, UpgradeHiveKinId, "Hive Kin",
                "Your Friend deployables (mine, turret, mortar, drone) count as Swarm Launcher allies for Breeding Season. Extra pellets spawn from them while firing.",
                Rarity.Exotic, new UpgradeProperty[] { new FriendHiveKinProperty() },
                UpgradeRegistration.CreateSinglePattern(), Upgrade.UpgradeFlags.None);


            if (!ok)
            {
                Logger.LogWarning("[FriendinaBox] One or more upgrades failed to register.");
                return;
            }

            _upgradesRegistered = true;
            Logger.LogInfo(
                $"[FriendinaBox] Registered 18 upgrades on '{GearApiName}' " +
                $"(ids={UpgradeWiderNetId}-{UpgradeHiveKinId}).");



        }
        catch (Exception ex)
        {
            Logger.LogError($"[FriendinaBox] Upgrade registration failed: {ex}");
        }
    }

    private bool CreateUpgrade(
        IUpgradable gear,
        int id,
        string name,
        string description,
        Rarity rarity,
        UpgradeProperty[] properties,
        HexMap pattern,
        Upgrade.UpgradeFlags flags)
    {
        return UpgradeRegistration.TryCreateGunUpgrade(
            PluginGUID,
            gear,
            id,
            name,
            description,
            rarity,
            properties,
            pattern,
            flags,
            null,
            Logger,
            out _);
    }
}

/// <summary>
/// Inject Friend gear into AllGear before PlayerData.OnAwake enumerates gear / fires upgrade callbacks.
/// After OnAwake, ensure GearData + upgrades (PlayerData tables are ready).
/// </summary>
[HarmonyPatch(typeof(PlayerData), nameof(PlayerData.OnAwake))]
internal static class PlayerDataOnAwakePrefix
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        FriendinaBoxPlugin.Instance?.TryRegisterGear("PlayerData.OnAwake prefix");
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            FriendinaBoxPlugin.Instance?.OnPlayerDataReady();
        }
        catch (Exception ex)
        {
            FriendinaBoxPlugin.Logger?.LogWarning($"[FriendinaBox] OnAwake postfix: {ex.Message}");
        }
    }
}



/// <summary>
/// Registers custom gear immediately after vanilla Global resources initialize
/// (backup path if OnAwake already ran, or gear was missed).
/// </summary>
[HarmonyPatch(typeof(Global), nameof(Global.LoadInstance))]
internal static class GlobalLoadHook
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        FriendinaBoxPlugin.Instance?.TryRegisterGear("Global.LoadInstance");
    }
}
