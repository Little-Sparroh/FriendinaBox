using System;
using System.Collections.Generic;
using Pigeon.Math;
using UnityEngine;


/// <summary>
/// Friend in a Box upgrade properties — mutate <see cref="FriendinaBoxBehaviour.Data"/>
/// and shared grenade stats (GunData / CooldownData).
/// </summary>

/// <summary>Increased detection radius + explosion radius on mine.</summary>
[Serializable]
public class FriendWiderNetProperty : UpgradeProperty
{
    public global::Range<float> radiusBonus = new global::Range<float>(0.2f, 0.35f);

    public override IEnumerator<StatData> GetStatData(
        Pigeon.Math.Random rand,
        IUpgradable gear,
        UpgradeInstance upgrade)
    {
        yield return StatData.Create(
            "Mine radius:",
            radiusBonus,
            upgrade,
            ref rand,
            OverrideType.Multiply,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
    }

    public override void Apply(IGear gear, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            return;

        float bonus = radiusBonus.GetValue(ref rand, upgrade, default(BoostParams));
        float mult = 1f + bonus;
        behaviour.GrenadeData.explosionRadiusMultiplier *= mult;
        behaviour.GrenadeData.detectRadiusMultiplier *= mult;

        if (gear is IWeapon weapon)
            weapon.GunData.hitForce *= mult;
    }

    public override void Remove(IGear gear, IGear prefab, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            behaviour.RestoreFromPrefab();

        if (gear is IWeapon weapon && prefab is IWeapon prefabWeapon)
            weapon.GunData.hitForce = prefabWeapon.GunData.hitForce;
    }
}

/// <summary>Duration increased, cooldown increased.</summary>
[Serializable]
public class FriendLongWatchProperty : UpgradeProperty
{
    public global::Range<float> durationBonus = new global::Range<float>(0.35f, 0.5f);
    public global::Range<float> cooldownPenalty = new global::Range<float>(0.2f, 0.3f);

    public override IEnumerator<StatData> GetStatData(
        Pigeon.Math.Random rand,
        IUpgradable gear,
        UpgradeInstance upgrade)
    {
        yield return StatData.Create(
            "Mine duration:",
            durationBonus,
            upgrade,
            ref rand,
            OverrideType.Multiply,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
        yield return StatData.Create(
            "Cooldown:",
            cooldownPenalty,
            upgrade,
            ref rand,
            OverrideType.Multiply,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
    }

    public override void Apply(IGear gear, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            return;

        float dur = durationBonus.GetValue(ref rand, upgrade, default(BoostParams));
        float cd = cooldownPenalty.GetValue(ref rand, upgrade, default(BoostParams));
        behaviour.GrenadeData.deployDuration *= (1f + dur);

        if (gear is Throwable throwable)
            throwable.CooldownData.rechargeDuration *= (1f + cd);
    }

    public override void Remove(IGear gear, IGear prefab, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            behaviour.RestoreFromPrefab();

        if (gear is Throwable live && prefab is Throwable prefabThrowable)
            live.CooldownData.rechargeDuration = prefabThrowable.CooldownData.rechargeDuration;
    }
}

/// <summary>Cooldown decreased, duration decreased.</summary>
[Serializable]
public class FriendQuickDeployProperty : UpgradeProperty
{
    public global::Range<float> cooldownBonus = new global::Range<float>(0.2f, 0.3f);
    public global::Range<float> durationPenalty = new global::Range<float>(0.15f, 0.25f);

    public override IEnumerator<StatData> GetStatData(
        Pigeon.Math.Random rand,
        IUpgradable gear,
        UpgradeInstance upgrade)
    {
        yield return StatData.Create(
            "Cooldown:",
            cooldownBonus,
            upgrade,
            ref rand,
            OverrideType.Multiply,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
        yield return StatData.Create(
            "Mine duration:",
            durationPenalty,
            upgrade,
            ref rand,
            OverrideType.Multiply,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
    }

    public override void Apply(IGear gear, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            return;

        float cd = cooldownBonus.GetValue(ref rand, upgrade, default(BoostParams));
        float dur = durationPenalty.GetValue(ref rand, upgrade, default(BoostParams));
        behaviour.GrenadeData.deployDuration *= Mathf.Max(0.25f, 1f - dur);

        if (gear is Throwable throwable)
            throwable.CooldownData.rechargeDuration *= Mathf.Max(0.25f, 1f - cd);
    }

    public override void Remove(IGear gear, IGear prefab, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            behaviour.RestoreFromPrefab();

        if (gear is Throwable live && prefab is Throwable prefabThrowable)
            live.CooldownData.rechargeDuration = prefabThrowable.CooldownData.rechargeDuration;
    }
}

/// <summary>If duration ends, create an acid puddle at the mine location.</summary>
[Serializable]
public class FriendLingeringGiftProperty : UpgradeProperty
{
    public global::Range<float> puddleDuration = new global::Range<float>(4f, 6f);

    public override IEnumerator<StatData> GetStatData(
        Pigeon.Math.Random rand,
        IUpgradable gear,
        UpgradeInstance upgrade)
    {
        yield return StatData.Create(
            "Expire acid puddle:",
            puddleDuration,
            upgrade,
            ref rand,
            OverrideType.Add,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
    }

    public override void Apply(IGear gear, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            return;

        float duration = puddleDuration.GetValue(ref rand, upgrade, default(BoostParams));
        behaviour.GrenadeData.acidPuddleOnExpireDuration =
            Mathf.Max(behaviour.GrenadeData.acidPuddleOnExpireDuration, duration);
    }

    public override void Remove(IGear gear, IGear prefab, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            behaviour.RestoreFromPrefab();
    }
}

/// <summary>If duration ends, player gains bonus move speed.</summary>
[Serializable]
public class FriendPartingBoostProperty : UpgradeProperty
{
    public global::Range<float> speedBonus = new global::Range<float>(0.2f, 0.3f);
    public global::Range<float> buffDuration = new global::Range<float>(4f, 6f);

    public override IEnumerator<StatData> GetStatData(
        Pigeon.Math.Random rand,
        IUpgradable gear,
        UpgradeInstance upgrade)
    {
        yield return StatData.Create(
            "Expire move speed:",
            speedBonus,
            upgrade,
            ref rand,
            OverrideType.Multiply,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
        yield return StatData.Create(
            "Boost duration:",
            buffDuration,
            upgrade,
            ref rand,
            OverrideType.Add,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
    }

    public override void Apply(IGear gear, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            return;

        float speed = speedBonus.GetValue(ref rand, upgrade, default(BoostParams));
        float duration = buffDuration.GetValue(ref rand, upgrade, default(BoostParams));
        behaviour.GrenadeData.expireMoveSpeedBonus =
            Mathf.Max(behaviour.GrenadeData.expireMoveSpeedBonus, speed);
        behaviour.GrenadeData.expireMoveSpeedDuration =
            Mathf.Max(behaviour.GrenadeData.expireMoveSpeedDuration, duration);
    }

    public override void Remove(IGear gear, IGear prefab, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            behaviour.RestoreFromPrefab();
    }
}

// ---------------------------------------------------------------------------
// Mode converters — set deployMode flags (can combine for drone hybrids)
// ---------------------------------------------------------------------------

/// <summary>Convert deployable into a stationary auto-turret.</summary>
[Serializable]
public class FriendTurretModeProperty : UpgradeProperty
{
    public override IEnumerator<StatData> GetStatData(
        Pigeon.Math.Random rand,
        IUpgradable gear,
        UpgradeInstance upgrade)
    {
        yield return StatData.Create(
            "Deploy mode:",
            new global::Range<int>(1, 1),
            upgrade,
            ref rand,
            OverrideType.Add,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
    }

    public override void Apply(IGear gear, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            return;
        behaviour.GrenadeData.deployMode |= FriendDeployMode.Turret;
    }

    public override void Remove(IGear gear, IGear prefab, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            behaviour.RestoreFromPrefab();
    }
}

/// <summary>Convert deployable into a mortar (periodic lobbed AoE).</summary>
[Serializable]
public class FriendMortarModeProperty : UpgradeProperty
{
    public override IEnumerator<StatData> GetStatData(
        Pigeon.Math.Random rand,
        IUpgradable gear,
        UpgradeInstance upgrade)
    {
        yield return StatData.Create(
            "Deploy mode:",
            new global::Range<int>(1, 1),
            upgrade,
            ref rand,
            OverrideType.Add,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
    }

    public override void Apply(IGear gear, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            return;
        behaviour.GrenadeData.deployMode |= FriendDeployMode.Mortar;
    }

    public override void Remove(IGear gear, IGear prefab, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            behaviour.RestoreFromPrefab();
    }
}

/// <summary>
/// Convert deployable into a drone that hovers near the player.
/// Default: suicide dive. With Turret/Mortar flags, inherits those instead of diving.
/// </summary>
[Serializable]
public class FriendDroneModeProperty : UpgradeProperty
{
    public override IEnumerator<StatData> GetStatData(
        Pigeon.Math.Random rand,
        IUpgradable gear,
        UpgradeInstance upgrade)
    {
        yield return StatData.Create(
            "Deploy mode:",
            new global::Range<int>(1, 1),
            upgrade,
            ref rand,
            OverrideType.Add,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
    }

    public override void Apply(IGear gear, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            return;
        behaviour.GrenadeData.deployMode |= FriendDeployMode.Drone;
    }

    public override void Remove(IGear gear, IGear prefab, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            behaviour.RestoreFromPrefab();
    }
}

// ---------------------------------------------------------------------------
// Combat / field upgrades
// ---------------------------------------------------------------------------

/// <summary>+1 deploy per throw (stackable); multi-spawn lands in a spread.</summary>
[Serializable]
public class FriendSquadDropProperty : UpgradeProperty
{
    public global::Range<int> extraDeploys = new global::Range<int>(1, 1);

    public override IEnumerator<StatData> GetStatData(
        Pigeon.Math.Random rand,
        IUpgradable gear,
        UpgradeInstance upgrade)
    {
        yield return StatData.Create(
            "Deploys per throw:",
            extraDeploys,
            upgrade,
            ref rand,
            OverrideType.Add,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
    }


    public override void Apply(IGear gear, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            return;
        int extra = extraDeploys.GetValue(ref rand, upgrade, default(BoostParams));
        behaviour.GrenadeData.maxConcurrentDeploys =
            Mathf.Max(1, behaviour.GrenadeData.maxConcurrentDeploys + extra);
        FriendDeployTracker.MaxConcurrentDeploys =
            Mathf.Max(FriendDeployTracker.MaxConcurrentDeploys, behaviour.GrenadeData.maxConcurrentDeploys);
        FriendCombatHooks.EnsureBound(gear, behaviour.GrenadeData);
    }

    public override void Remove(IGear gear, IGear prefab, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            behaviour.RestoreFromPrefab();
        FriendDeployTracker.MaxConcurrentDeploys = 1;
    }
}

/// <summary>Friend damage returns as blue overhealth (not base HP).</summary>
[Serializable]
public class FriendSympatheticLinkProperty : UpgradeProperty
{
    public global::Range<float> lifesteal = new global::Range<float>(0.08f, 0.15f);

    public override IEnumerator<StatData> GetStatData(
        Pigeon.Math.Random rand,
        IUpgradable gear,
        UpgradeInstance upgrade)
    {
        yield return StatData.Create(
            "Overheal from damage:",
            lifesteal,
            upgrade,
            ref rand,
            OverrideType.Multiply,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
    }

    public override void Apply(IGear gear, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            return;
        float v = lifesteal.GetValue(ref rand, upgrade, default(BoostParams));
        behaviour.GrenadeData.lifestealFraction =
            Mathf.Max(behaviour.GrenadeData.lifestealFraction, v);
        FriendCombatHooks.EnsureBound(gear, behaviour.GrenadeData);
    }

    public override void Remove(IGear gear, IGear prefab, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            behaviour.RestoreFromPrefab();
    }
}

/// <summary>Friend kills refund grenade charge.</summary>
[Serializable]
public class FriendFieldRechargeProperty : UpgradeProperty
{
    public global::Range<float> charge = new global::Range<float>(0.25f, 0.4f);

    public override IEnumerator<StatData> GetStatData(
        Pigeon.Math.Random rand,
        IUpgradable gear,
        UpgradeInstance upgrade)
    {
        yield return StatData.Create(
            "Charge on kill:",
            charge,
            upgrade,
            ref rand,
            OverrideType.Add,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
    }

    public override void Apply(IGear gear, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            return;
        float v = charge.GetValue(ref rand, upgrade, default(BoostParams));
        behaviour.GrenadeData.chargeOnKill =
            Mathf.Max(behaviour.GrenadeData.chargeOnKill, v);
        FriendCombatHooks.EnsureBound(gear, behaviour.GrenadeData);
    }

    public override void Remove(IGear gear, IGear prefab, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            behaviour.RestoreFromPrefab();
    }
}

/// <summary>Friend kills extend active deploy duration.</summary>
[Serializable]
public class FriendOvertimeProperty : UpgradeProperty
{
    public global::Range<float> seconds = new global::Range<float>(1.5f, 2.5f);

    public override IEnumerator<StatData> GetStatData(
        Pigeon.Math.Random rand,
        IUpgradable gear,
        UpgradeInstance upgrade)
    {
        yield return StatData.Create(
            "Duration on kill:",
            seconds,
            upgrade,
            ref rand,
            OverrideType.Add,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
    }

    public override void Apply(IGear gear, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            return;
        float v = seconds.GetValue(ref rand, upgrade, default(BoostParams));
        behaviour.GrenadeData.durationOnKill =
            Mathf.Max(behaviour.GrenadeData.durationOnKill, v);
        FriendCombatHooks.EnsureBound(gear, behaviour.GrenadeData);
    }

    public override void Remove(IGear gear, IGear prefab, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            behaviour.RestoreFromPrefab();
    }
}

/// <summary>Enemies hit by Friend take bonus damage briefly.</summary>
[Serializable]
public class FriendPaintedTargetsProperty : UpgradeProperty
{
    public global::Range<float> bonus = new global::Range<float>(0.15f, 0.25f);
    public global::Range<float> duration = new global::Range<float>(4f, 6f);

    public override IEnumerator<StatData> GetStatData(
        Pigeon.Math.Random rand,
        IUpgradable gear,
        UpgradeInstance upgrade)
    {
        yield return StatData.Create(
            "Mark damage:",
            bonus,
            upgrade,
            ref rand,
            OverrideType.Multiply,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
        yield return StatData.Create(
            "Mark duration:",
            duration,
            upgrade,
            ref rand,
            OverrideType.Add,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
    }

    public override void Apply(IGear gear, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            return;
        float b = bonus.GetValue(ref rand, upgrade, default(BoostParams));
        float d = duration.GetValue(ref rand, upgrade, default(BoostParams));
        behaviour.GrenadeData.markDamageBonus =
            Mathf.Max(behaviour.GrenadeData.markDamageBonus, b);
        behaviour.GrenadeData.markDuration =
            Mathf.Max(behaviour.GrenadeData.markDuration, d);
        FriendCombatHooks.EnsureBound(gear, behaviour.GrenadeData);
    }

    public override void Remove(IGear gear, IGear prefab, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            behaviour.RestoreFromPrefab();
    }
}

/// <summary>Shoot your deployable to detonate early; power scales with remaining duration.</summary>
[Serializable]
public class FriendScuttleChargeProperty : UpgradeProperty
{
    public override IEnumerator<StatData> GetStatData(
        Pigeon.Math.Random rand,
        IUpgradable gear,
        UpgradeInstance upgrade)
    {
        yield return StatData.Create(
            "Shoot to detonate:",
            new global::Range<int>(1, 1),
            upgrade,
            ref rand,
            OverrideType.Add,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
    }

    public override void Apply(IGear gear, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            return;
        behaviour.GrenadeData.shootToDetonate = true;
        FriendCombatHooks.EnsureBound(gear, behaviour.GrenadeData);
        FriendCombatRunner.Ensure();
    }

    public override void Remove(IGear gear, IGear prefab, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            behaviour.RestoreFromPrefab();
    }
}

/// <summary>
/// Turret/mortar/drone guns blend toward the player's primary weapon stats.
/// </summary>
[Serializable]
public class FriendCalibratedLinkProperty : UpgradeProperty
{
    public global::Range<float> portion = new global::Range<float>(0.35f, 0.55f);

    public override IEnumerator<StatData> GetStatData(
        Pigeon.Math.Random rand,
        IUpgradable gear,
        UpgradeInstance upgrade)
    {
        yield return StatData.Create(
            "Weapon stat share:",
            portion,
            upgrade,
            ref rand,
            OverrideType.Multiply,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
    }

    public override void Apply(IGear gear, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            return;
        float t = portion.GetValue(ref rand, upgrade, default(BoostParams));
        behaviour.GrenadeData.copyPlayerWeaponStats = true;
        behaviour.GrenadeData.weaponStatPortion =
            Mathf.Max(behaviour.GrenadeData.weaponStatPortion, Mathf.Clamp01(t));
        FriendCombatHooks.EnsureBound(gear, behaviour.GrenadeData);
    }

    public override void Remove(IGear gear, IGear prefab, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            behaviour.RestoreFromPrefab();
    }
}

/// <summary>
/// Friend prefers the enemy the player last damaged.
/// </summary>
[Serializable]
public class FriendDesignatedTargetProperty : UpgradeProperty
{
    public global::Range<float> duration = new global::Range<float>(4f, 6f);

    public override IEnumerator<StatData> GetStatData(
        Pigeon.Math.Random rand,
        IUpgradable gear,
        UpgradeInstance upgrade)
    {
        yield return StatData.Create(
            "Focus duration:",
            duration,
            upgrade,
            ref rand,
            OverrideType.Add,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
    }

    public override void Apply(IGear gear, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            return;
        float d = duration.GetValue(ref rand, upgrade, default(BoostParams));
        behaviour.GrenadeData.sharePlayerTarget = true;
        behaviour.GrenadeData.sharedTargetDuration =
            Mathf.Max(behaviour.GrenadeData.sharedTargetDuration, d);
        FriendCombatHooks.EnsureBound(gear, behaviour.GrenadeData);
    }

    public override void Remove(IGear gear, IGear prefab, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            behaviour.RestoreFromPrefab();
    }
}

/// <summary>
/// Active Friend deployables (mine/turret/mortar/drone) count as Swarm Launcher
/// Breeding Season (FriendFire) allies. Extra pellets spawn from their positions.
/// </summary>
[Serializable]
public class FriendHiveKinProperty : UpgradeProperty
{
    public override IEnumerator<StatData> GetStatData(
        Pigeon.Math.Random rand,
        IUpgradable gear,
        UpgradeInstance upgrade)
    {
        yield return StatData.Create(
            "Swarm ally deploys:",
            new global::Range<int>(1, 1),
            upgrade,
            ref rand,
            OverrideType.Add,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
    }


    public override void Apply(IGear gear, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            return;
        behaviour.GrenadeData.countsAsSwarmAlly = true;
        FriendCombatHooks.EnsureBound(gear, behaviour.GrenadeData);
    }

    public override void Remove(IGear gear, IGear prefab, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            behaviour.RestoreFromPrefab();
    }
}

/// <summary>
/// When you take damage, gain blue overhealth (does not fill base HP first).
/// </summary>
[Serializable]
public class FriendReactiveShellProperty : UpgradeProperty


{
    public global::Range<float> overheal = new global::Range<float>(8f, 14f);
    public global::Range<float> cooldown = new global::Range<float>(2.5f, 4f);

    public override IEnumerator<StatData> GetStatData(
        Pigeon.Math.Random rand,
        IUpgradable gear,
        UpgradeInstance upgrade)
    {
        yield return StatData.Create(
            "Overheal on hit:",
            overheal,
            upgrade,
            ref rand,
            OverrideType.Add,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
        yield return StatData.Create(
            "Overheal cooldown:",
            cooldown,
            upgrade,
            ref rand,
            OverrideType.Add,
            StatData.LabelType.BeforeWithColon,
            default(BoostParams));
    }

    public override void Apply(IGear gear, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (!FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            return;
        float oh = overheal.GetValue(ref rand, upgrade, default(BoostParams));
        float cd = cooldown.GetValue(ref rand, upgrade, default(BoostParams));
        behaviour.GrenadeData.overhealOnDamaged =
            Mathf.Max(behaviour.GrenadeData.overhealOnDamaged, oh);
        // Prefer shorter cooldown when stacking.
        if (behaviour.GrenadeData.overhealOnDamagedCooldown <= 0f)
            behaviour.GrenadeData.overhealOnDamagedCooldown = cd;
        else
            behaviour.GrenadeData.overhealOnDamagedCooldown =
                Mathf.Min(behaviour.GrenadeData.overhealOnDamagedCooldown, cd);
        FriendCombatHooks.EnsureBound(gear, behaviour.GrenadeData);
    }

    public override void Remove(IGear gear, IGear prefab, UpgradeInstance upgrade, ref Pigeon.Math.Random rand)
    {
        if (FriendinaBoxBehaviour.TryGet(gear, out FriendinaBoxBehaviour behaviour))
            behaviour.RestoreFromPrefab();
    }
}


