using System;
using BepInEx.Logging;
using UnityEngine;

/// <summary>
/// Helpers for creating and registering custom upgrades at runtime.
/// Wraps the official <see cref="PlayerData.CreateUpgrade"/> / <see cref="PlayerData.CustomUpgradeParams"/> API.
/// </summary>
public static class UpgradeRegistration
{
    public static bool TryCreateGunUpgrade(
        string modGuid,
        string gearApiName,
        int upgradeId,
        string name,
        string description,
        Rarity rarity,
        UpgradeProperty[] properties,
        HexMap pattern,
        Upgrade.UpgradeFlags flags,
        Sprite icon,
        ManualLogSource log,
        out Upgrade upgrade,
        int priority = 0,
        Upgrade.CollectionSource collectionSource = Upgrade.CollectionSource.WorldPool)
    {
        upgrade = null;

        IUpgradable gear = null;
        try
        {
            // FindGear can NRE if PlayerData tables are mid-init — never let that escape.
            gear = PlayerData.FindGear(gearApiName);
        }
        catch (Exception ex)
        {
            log?.LogWarning($"[UpgradeRegistration] FindGear(\"{gearApiName}\") threw: {ex.Message}");
            return false;
        }

        if (gear == null)
        {
            log?.LogError($"[UpgradeRegistration] FindGear(\"{gearApiName}\") returned null. Register gear first.");
            return false;
        }

        return TryCreateGunUpgrade(
            modGuid, gear, upgradeId, name, description, rarity,
            properties, pattern, flags, icon, log, out upgrade, priority, collectionSource);
    }

    /// <summary>
    /// Preferred path when the catalog gear reference is already known (avoids FindGear).
    /// </summary>
    public static bool TryCreateGunUpgrade(
        string modGuid,
        IUpgradable gear,
        int upgradeId,
        string name,
        string description,
        Rarity rarity,
        UpgradeProperty[] properties,
        HexMap pattern,
        Upgrade.UpgradeFlags flags,
        Sprite icon,
        ManualLogSource log,
        out Upgrade upgrade,
        int priority = 0,
        Upgrade.CollectionSource collectionSource = Upgrade.CollectionSource.WorldPool)
    {
        upgrade = null;

        if (string.IsNullOrEmpty(modGuid))
        {
            log?.LogError("[UpgradeRegistration] modGuid is null/empty.");
            return false;
        }

        if (gear == null)
        {
            log?.LogError("[UpgradeRegistration] gear is null.");
            return false;
        }

        if (properties == null || properties.Length == 0)
        {
            log?.LogError("[UpgradeRegistration] properties array is null/empty.");
            return false;
        }

        PlayerData.CustomUpgradeParams upgradeParams = PlayerData.CustomUpgradeParams.Create(
            gear,
            upgradeId,
            name,
            description,
            rarity,
            icon);

        upgradeParams.flags = flags;
        upgradeParams.priority = priority;
        upgradeParams.collectionSource = collectionSource;
        upgradeParams.upgradeType = Upgrade.Type.Normal;
        upgradeParams.useDefaultUnlockCost = true;

        if (pattern != null)
            upgradeParams.pattern = pattern;

        try
        {
            upgrade = PlayerData.CreateUpgrade(modGuid, upgradeParams, properties);
        }
        catch (Exception ex)
        {
            log?.LogError($"[UpgradeRegistration] CreateUpgrade threw: {ex}");
            upgrade = null;
            return false;
        }

        if (upgrade == null)
        {
            log?.LogError("[UpgradeRegistration] CreateUpgrade returned null.");
            return false;
        }

        if (pattern != null)
            upgrade.SetPattern(pattern);

        log?.LogInfo($"[UpgradeRegistration] Created '{name}' id={upgradeId} on {gear.Info?.APIName ?? "?"}.");
        return true;
    }

    /// <summary>3-cell diagonal — radius / general mine upgrades.</summary>
    public static HexMap CreateRadiusPattern()
    {
        HexMap map = new HexMap(3, 3);
        map[0, 0].enabled = true;
        map[1, 0].enabled = true;
        map[1, 0].connections = HexMap.Direction.SouthEast | HexMap.Direction.NorthWest;
        map[2, 1].enabled = true;
        return map;
    }

    /// <summary>2-cell bar — duration / cooldown tradeoffs.</summary>
    public static HexMap CreateBarPattern()
    {
        HexMap map = new HexMap(2, 2);
        map[0, 0].enabled = true;
        map[1, 0].enabled = true;
        map[0, 0].connections = HexMap.Direction.SouthEast;
        map[1, 0].connections = HexMap.Direction.NorthWest;
        return map;
    }

    /// <summary>Single cell — simple flag / expire effects.</summary>
    public static HexMap CreateSinglePattern()
    {
        HexMap map = new HexMap(1, 1);
        map[0, 0].enabled = true;
        return map;
    }

    public static UpgradeInstance GrantTestInstance(
        IUpgradable gear,
        Upgrade upgrade,
        bool unlock = true,
        bool quietUnlock = true,
        ManualLogSource log = null)
    {
        if (gear == null || upgrade == null)
        {
            log?.LogError("[UpgradeRegistration] GrantTestInstance: gear or upgrade is null.");
            return null;
        }

        UpgradeInstance instance = PlayerData.CollectInstance(gear, upgrade, PlayerData.UnlockFlags.Hidden);
        if (instance == null)
        {
            log?.LogError("[UpgradeRegistration] CollectInstance returned null.");
            return null;
        }

        if (unlock)
            instance.Unlock(quietUnlock);

        log?.LogInfo($"[UpgradeRegistration] Granted test instance of '{upgrade.Name}' (id={instance.InstanceID}).");
        return instance;
    }
}
