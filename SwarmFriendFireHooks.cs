using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;

/// <summary>
/// Hive Kin: active Friend deployables (mine/turret/mortar/drone) count as
/// Breeding Season (FriendFire) allies for Swarm Launcher.
/// Extra pellets spawn from deploy positions while firing.
/// </summary>

[HarmonyPatch(typeof(SwarmGun))]
internal static class SwarmFriendFireHooks
{
    private static FieldInfo _friendBulletCounterField;
    private static FieldInfo _fireDataField;
    private static MethodInfo _startFireCustom;
    private static MethodInfo _finishFireCustom;
    private static FieldInfo _bulletRotationField;
    private static bool _resolved;
    private static bool _resolveFailed;

    private static readonly List<FriendDeployable> DroneBuffer = new List<FriendDeployable>(8);

    private static void EnsureResolved()
    {
        if (_resolved || _resolveFailed)
            return;

        try
        {
            _friendBulletCounterField = AccessTools.Field(typeof(SwarmGun), "friendBulletCounter");
            _fireDataField = AccessTools.Field(typeof(Gun), "fireData");
            _startFireCustom = AccessTools.Method(
                typeof(SwarmGun),
                "StartFireCustomSwarmBullet",
                new[] { typeof(int), typeof(Vector3), typeof(Vector3), typeof(BulletData).MakeByRefType() });
            _finishFireCustom = AccessTools.Method(
                typeof(SwarmGun),
                "FinishFireCustomSwarmBullet",
                new[] { typeof(int), typeof(BulletData).MakeByRefType(), typeof(GearUpgradeFlags) });

            if (_fireDataField != null)
            {
                Type fireDataType = _fireDataField.FieldType;
                _bulletRotationField = AccessTools.Field(fireDataType, "bulletRotation");
            }

            if (_friendBulletCounterField == null || _startFireCustom == null || _finishFireCustom == null)
            {
                // Fallback: looser method lookup
                _startFireCustom ??= AccessTools.Method(typeof(SwarmGun), "StartFireCustomSwarmBullet");
                _finishFireCustom ??= AccessTools.Method(typeof(SwarmGun), "FinishFireCustomSwarmBullet");
            }

            if (_friendBulletCounterField == null || _startFireCustom == null || _finishFireCustom == null)
            {
                FriendinaBoxPlugin.Logger?.LogWarning(
                    "[FriendinaBox] Hive Kin: could not resolve SwarmGun FriendFire internals " +
                    $"(counter={_friendBulletCounterField != null}, start={_startFireCustom != null}, finish={_finishFireCustom != null}).");
                _resolveFailed = true;
                return;
            }

            _resolved = true;
            FriendinaBoxPlugin.Logger?.LogInfo("[FriendinaBox] Hive Kin SwarmGun hooks resolved.");
        }
        catch (Exception ex)
        {
            FriendinaBoxPlugin.Logger?.LogWarning($"[FriendinaBox] Hive Kin resolve failed: {ex.Message}");
            _resolveFailed = true;
        }
    }

    /// <summary>
    /// True if this OnFire call will trigger a FriendFire spawn (interval met).
    /// </summary>
    [HarmonyPatch("OnFire")]
    [HarmonyPrefix]
    private static void OnFirePrefix(SwarmGun __instance, out bool __state)
    {
        __state = false;
        if (__instance == null)
            return;

        try
        {
            ref SwarmGun.Data data = ref __instance.SwarmData;
            if (data.friendFireRadius <= 0f)
                return;

            if (!HiveKinActive())
                return;

            EnsureResolved();
            if (!_resolved)
                return;

            int counter = (int)_friendBulletCounterField.GetValue(__instance);
            int interval = Mathf.Max(1, data.friendFireInterval);
            // Vanilla does counter++ then checks >= interval.
            __state = (counter + 1) >= interval;
        }
        catch
        {
            __state = false;
        }
    }

    [HarmonyPatch("OnFire")]
    [HarmonyPostfix]
    private static void OnFirePostfix(SwarmGun __instance, int numBullets, bool __state)
    {
        if (!__state || __instance == null || !__instance.IsOwner)
            return;

        try
        {
            if (!HiveKinActive())
                return;

            EnsureResolved();
            if (!_resolved)
                return;

            ref SwarmGun.Data data = ref __instance.SwarmData;
            float radius = data.friendFireRadius;
            if (radius <= 0f)
                return;

            Player owner = __instance.Player;
            if (owner == null)
                return;

            Vector3 ownerPos = owner.InterpolatedPosition;
            FriendDeployTracker.GetAlliesInRadius(ownerPos, radius, DroneBuffer);
            if (DroneBuffer.Count == 0)
                return;

            Vector3 bulletEuler = GetFireEuler(__instance, owner);
            GearUpgradeFlags flags = __instance.UpgradeFlags;

            for (int i = 0; i < DroneBuffer.Count; i++)
            {
                FriendDeployable deploy = DroneBuffer[i];
                if (deploy == null)
                    continue;

                // Match vanilla ally height (~+1.5) for ground forms; drones already hover.
                Vector3 spawnPos = deploy.transform.position;
                bool isDrone = (deploy.Mode & FriendDeployMode.Drone) != 0;
                spawnPos.y += isDrone ? 0.35f : 1.5f;


                int shotIndex = numBullets + 100 + i;
                BulletData bulletData = default;

                object[] startArgs = { shotIndex, spawnPos, bulletEuler, bulletData };
                _startFireCustom.Invoke(__instance, startArgs);
                bulletData = (BulletData)startArgs[3];

                if (data.friendFireDamageMult > 0f)
                    bulletData.damage *= data.friendFireDamageMult;

                object[] finishArgs = { shotIndex, bulletData, flags };
                _finishFireCustom.Invoke(__instance, finishArgs);
            }
        }
        catch (Exception ex)
        {
            FriendinaBoxPlugin.Logger?.LogWarning($"[FriendinaBox] Hive Kin OnFire failed: {ex.Message}");
        }
    }

    [HarmonyPatch("OnActiveUpdate")]
    [HarmonyPostfix]
    private static void OnActiveUpdatePostfix(SwarmGun __instance)
    {
        if (__instance == null || !__instance.IsOwner)
            return;

        try
        {
            ref SwarmGun.Data data = ref __instance.SwarmData;
            if (data.friendFireRadius <= 0f)
                return;
            if (!HiveKinActive())
                return;

            Player owner = __instance.Player;
            if (owner == null)
                return;

            FriendDeployTracker.GetAlliesInRadius(owner.InterpolatedPosition, data.friendFireRadius, DroneBuffer);
            int friendAllies = DroneBuffer.Count;
            if (friendAllies <= 0)
                return;

            // Count real player allies the same way vanilla does.
            int playerAllies = 0;
            float r2 = data.friendFireRadius * data.friendFireRadius;
            Vector3 pos = owner.InterpolatedPosition;
            for (int j = 0; j < GameManager.players.Count; j++)
            {
                Player p = GameManager.players[j];
                if (p == null || p.IsLocalPlayer)
                    continue;
                if ((p.InterpolatedPosition - pos).sqrMagnitude >= r2)
                    continue;
                if (p.Gear != null && p.Gear.Length > 1 &&
                    (p.Gear[0] is SwarmGun || p.Gear[1] is SwarmGun))
                    playerAllies++;
            }

            int total = playerAllies + friendAllies;

            if (total > 0)
            {
                Sprite icon = UpgradeProperty_SwarmGun_FriendFire.Icon;
                if (icon != null)
                {
                    owner.UpdateStackDisplay(
                        typeof(UpgradeProperty_SwarmGun_FriendFire),
                        TextBlocks.GetString("FriendFire", 0),
                        icon,
                        total,
                        0.1f);
                }
            }
        }
        catch
        {
            // never break swarm update
        }
    }

    private static bool HiveKinActive()
    {
        if (!FriendinaBoxBehaviour.TryGetEquipped(out FriendinaBoxBehaviour b, out _))
            return false;
        return b.GrenadeData.countsAsSwarmAlly;
    }

    private static Vector3 GetFireEuler(SwarmGun gun, Player owner)
    {
        try
        {
            if (_fireDataField != null && _bulletRotationField != null)
            {
                object fireData = _fireDataField.GetValue(gun);
                if (fireData != null)
                {
                    object rotObj = _bulletRotationField.GetValue(fireData);
                    if (rotObj is Quaternion q)
                        return q.eulerAngles;
                }
            }
        }
        catch
        {
            // fall through
        }

        if (owner != null)
            return owner.transform.eulerAngles;
        return Vector3.zero;
    }
}
