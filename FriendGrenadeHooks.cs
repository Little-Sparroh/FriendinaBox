using System;
using HarmonyLib;
using UnityEngine;

/// <summary>
/// Turns Friend in a Box throws into proximity mines instead of instant explosions.
///
/// Vanilla: bounce → fuse → GrenadeBullet.Detonate AOE.
/// Friend: on Detonate, suppress vanilla blast and spawn <see cref="FriendDeployable"/>.
/// </summary>
[HarmonyPatch(typeof(GrenadeBullet), "Detonate")]
internal static class FriendGrenadeHooks
{
    [HarmonyPrefix]
    private static bool Prefix(GrenadeBullet __instance)
    {
        try
        {
            if (__instance == null)
                return true;

            IDamageSource parent = __instance.ParentSource;
            if (parent is not IGear gear)
                return true;

            if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
                return true;

            // Only the local owner arms mines (sandbox / local-player authority).
            if (gear is Throwable throwable)
            {
                if (throwable.Player == null || !throwable.Player.IsLocalPlayer)
                    return false; // suppress remote/vanilla path for our gear on non-owners
            }

            Vector3 pos = __instance.transform != null
                ? __instance.transform.position
                : default;
            TryReadBulletPosition(__instance, ref pos);

            ref FriendinaBoxBehaviour.Data data = ref behaviour.GrenadeData;

            if (gear is not IWeapon weapon)
                return true;

            float radiusMult = Mathf.Max(0.01f, data.explosionRadiusMultiplier);
            float explosionRadius = weapon.GunData.hitForce * radiusMult;
            var damage = new DamageData(
                weapon.GunData.damage,
                weapon.GunData.damageEffect,
                weapon.GunData.damageEffectAmount,
                weapon.GunData.damageFlags | DamageFlags.AOE);

            float shake = 12f;
            float selfFx = 1f;
            if (gear is GrenadeGear grenadeGear)
            {
                shake = grenadeGear.ExplosionShake;
                selfFx = grenadeGear.SelfEffectMultiplier;
            }

            // Squad Drop: one throw arms maxConcurrentDeploys at once, spread so they don't overlap.
            int count = Mathf.Max(1, data.maxConcurrentDeploys);
            // Keep tracker cap in sync before multi-register.
            if (count > FriendDeployTracker.MaxConcurrentDeploys)
                FriendDeployTracker.MaxConcurrentDeploys = count;

            for (int i = 0; i < count; i++)
            {
                Vector3 spawnPos = pos + GetSquadSpreadOffset(i, count);
                FriendDeployable.Spawn(
                    spawnPos,
                    gear,
                    data,
                    explosionRadius,
                    damage,
                    shake,
                    selfFx);
            }

            // Stop the projectile without running vanilla AOE.

            try
            {
                __instance.Kill();
            }
            catch
            {
                // Kill may already be mid-teardown; ignore.
            }

            return false; // skip vanilla Detonate
        }
        catch (Exception ex)
        {
            FriendinaBoxPlugin.Logger?.LogError($"[FriendinaBox] Detonate prefix failed: {ex}");
            return true;
        }
    }

    private static void TryReadBulletPosition(GrenadeBullet bullet, ref Vector3 pos)
    {
        var field = AccessTools.Field(bullet.GetType(), "positionNext")
            ?? AccessTools.Field(typeof(SimpleProjectileBullet), "positionNext");
        if (field != null && field.GetValue(bullet) is Vector3 v)
            pos = v;
    }

    /// <summary>
    /// Horizontal ring offsets so multi-deploy mines/turrets don't stack on the land point.
    /// Index 0 sits on impact; others fan out evenly.
    /// </summary>
    private static Vector3 GetSquadSpreadOffset(int index, int count)
    {
        if (count <= 1 || index <= 0)
            return Vector3.zero;

        // Slightly wider ring as squad grows.
        float radius = 1.6f + 0.35f * (count - 2);
        radius = Mathf.Clamp(radius, 1.4f, 3.2f);

        // Even angles starting from a slight offset so pair doesn't sit on a line through origin only.
        float angle = (index / (float)count) * Mathf.PI * 2f + 0.35f;
        float jitter = (index * 0.17f) % 0.25f;
        float r = radius + jitter;

        return new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
    }
}

