using System.Reflection;
using UnityEngine;

/// <summary>
/// Resolves vanilla bullet prefabs for Friend turret / mortar fire.
/// RailBullet comes from Cycler (CartridgeSMG); MortarBullet from enemy mortar assets.
/// </summary>
public static class FriendBulletCache
{
    private static bool _attempted;
    private static GameObject _railPrefab;
    private static GameObject _mortarPrefab;

    public static GameObject RailPrefab
    {
        get
        {
            EnsureCached();
            return _railPrefab;
        }
    }

    public static GameObject MortarPrefab
    {
        get
        {
            EnsureCached();
            return _mortarPrefab;
        }
    }

    public static void EnsureCached()
    {
        if (_attempted)
            return;
        _attempted = true;

        TryCacheRailFromCycler();
        TryCacheMortar();

        if (_railPrefab != null)
            FriendinaBoxPlugin.Logger?.LogInfo(
                $"[FriendinaBox] Cached RailBullet prefab '{_railPrefab.name}'.");
        else
            FriendinaBoxPlugin.Logger?.LogWarning(
                "[FriendinaBox] Could not cache RailBullet — turret fire will use explosion fallback.");

        if (_mortarPrefab != null)
            FriendinaBoxPlugin.Logger?.LogInfo(
                $"[FriendinaBox] Cached MortarBullet prefab '{_mortarPrefab.name}'.");
        else
            FriendinaBoxPlugin.Logger?.LogWarning(
                "[FriendinaBox] Could not cache MortarBullet — mortar fire will use explosion fallback.");
    }

    private static void TryCacheRailFromCycler()
    {
        // Prefer live AllGear CartridgeSMG (Cycler).
        if (Global.Instance?.AllGear != null)
        {
            for (int i = 0; i < Global.Instance.AllGear.Length; i++)
            {
                if (Global.Instance.AllGear[i] is CartridgeSMG smg)
                {
                    if (TryReadGunBulletPrefab(smg, out GameObject go) && go != null)
                    {
                        // Prefer actual RailBullet component if present.
                        if (go.GetComponent<RailBullet>() != null || go.GetComponent<IBullet>() != null)
                        {
                            _railPrefab = go;
                            return;
                        }
                    }
                }
            }

            // Fallback: any Gun whose bullet is RailBullet.
            for (int i = 0; i < Global.Instance.AllGear.Length; i++)
            {
                if (Global.Instance.AllGear[i] is Gun gun &&
                    TryReadGunBulletPrefab(gun, out GameObject go) &&
                    go != null &&
                    go.GetComponent<RailBullet>() != null)
                {
                    _railPrefab = go;
                    return;
                }
            }
        }

        // Last resort: any loaded RailBullet asset (prefab instance in memory).
        RailBullet[] rails = Resources.FindObjectsOfTypeAll<RailBullet>();
        for (int i = 0; i < rails.Length; i++)
        {
            RailBullet rb = rails[i];
            if (rb == null)
                continue;
            GameObject go = rb.gameObject;
            // Prefer inactive prefab assets over scene instances.
            if (go.scene.IsValid() && go.scene.isLoaded && go.activeInHierarchy)
                continue;
            _railPrefab = go;
            return;
        }

        if (rails.Length > 0 && rails[0] != null)
            _railPrefab = rails[0].gameObject;
    }

    private static void TryCacheMortar()
    {
        MortarBullet[] mortars = Resources.FindObjectsOfTypeAll<MortarBullet>();
        for (int i = 0; i < mortars.Length; i++)
        {
            MortarBullet mb = mortars[i];
            if (mb == null)
                continue;
            GameObject go = mb.gameObject;
            if (go.scene.IsValid() && go.scene.isLoaded && go.activeInHierarchy)
                continue;
            _mortarPrefab = go;
            return;
        }

        if (mortars.Length > 0 && mortars[0] != null)
        {
            _mortarPrefab = mortars[0].gameObject;
            return;
        }

        // Fallback: any RocketSalvoBullet (mortar extends this).
        RocketSalvoBullet[] rockets = Resources.FindObjectsOfTypeAll<RocketSalvoBullet>();
        for (int i = 0; i < rockets.Length; i++)
        {
            RocketSalvoBullet r = rockets[i];
            if (r == null || r is MortarBullet)
                continue;
            // Prefer types named like mortar if any non-mortar rockets only.
        }

        // Enemy arm tips often hold mortar prefabs.
        if (Global.Instance?.AllGear == null)
            return;

        ProjectileGunArmTip[] tips = Resources.FindObjectsOfTypeAll<ProjectileGunArmTip>();
        for (int i = 0; i < tips.Length; i++)
        {
            if (TryReadArmTipBulletPrefab(tips[i], out GameObject go) &&
                go != null &&
                go.GetComponent<MortarBullet>() != null)
            {
                _mortarPrefab = go;
                return;
            }
        }
    }

    private static bool TryReadGunBulletPrefab(Gun gun, out GameObject prefab)
    {
        prefab = null;
        if (gun == null)
            return false;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Serialized GameObject field on Gun.
        FieldInfo goField = typeof(Gun).GetField("_bulletPrefab", flags);
        if (goField?.GetValue(gun) is GameObject go && go != null)
        {
            prefab = go;
            return true;
        }

        // Cached IBullet reference.
        FieldInfo bulletField = typeof(Gun).GetField("bulletPrefab", flags)
            ?? typeof(Gun).GetField("defaultBulletPrefab", flags);
        if (bulletField?.GetValue(gun) is IBullet bullet && bullet is Component c && c != null)
        {
            // Prefer the prefab asset if this is a live instance — still usable with SimplePool.Get.
            prefab = c.gameObject;
            return true;
        }

        // Component on same hierarchy (some builds).
        RailBullet rail = gun.GetComponentInChildren<RailBullet>(true);
        if (rail != null)
        {
            prefab = rail.gameObject;
            return true;
        }

        return false;
    }

    private static bool TryReadArmTipBulletPrefab(ProjectileGunArmTip tip, out GameObject prefab)
    {
        prefab = null;
        if (tip == null)
            return false;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        // GunArmTip.bulletPrefab
        FieldInfo field = typeof(GunArmTip).GetField("bulletPrefab", flags)
            ?? tip.GetType().GetField("bulletPrefab", flags);
        if (field?.GetValue(tip) is GameObject go && go != null)
        {
            prefab = go;
            return true;
        }

        return false;
    }
}
