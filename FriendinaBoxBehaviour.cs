using System;
using Pigeon.Movement;
using UnityEngine;

/// <summary>
/// Custom gameplay data host for Friend in a Box.
/// Attached to the cloned grenade catalog entry and stamped onto live equip instances.
/// Upgrades mutate <see cref="GrenadeData"/>; deployables snapshot it at arm time.
/// </summary>
public sealed class FriendinaBoxBehaviour : MonoBehaviour
{
    [Serializable]
    public struct Data
    {
        public float deployDuration;
        public float detectRadius;
        public float detectRadiusMultiplier;
        public float explosionRadiusMultiplier;
        public float acidPuddleOnExpireDuration;
        public float expireMoveSpeedBonus;
        public float expireMoveSpeedDuration;
        public FriendDeployMode deployMode;
        public float fireRate;
        public float modeDamageScale;
        public float mortarRangeMultiplier;
        public float droneFollowDistance;
        public float droneMoveSpeed;

        /// <summary>Max concurrent field deployables (baseline 1).</summary>
        public int maxConcurrentDeploys;

        /// <summary>Fraction of Friend damage returned as blue overhealth (not base HP).</summary>
        public float lifestealFraction;

        /// <summary>Grenade charge refunded per Friend kill.</summary>
        public float chargeOnKill;

        /// <summary>Seconds added to active deploy duration per Friend kill.</summary>
        public float durationOnKill;

        /// <summary>Bonus outgoing damage multiplier vs marked enemies (e.g. 0.2 = +20%).</summary>
        public float markDamageBonus;

        /// <summary>How long mark lasts after Friend hits an enemy.</summary>
        public float markDuration;

        /// <summary>Player can shoot deployable to detonate early (scaled by remaining duration).</summary>
        public bool shootToDetonate;

        /// <summary>
        /// When player takes damage, grant this much blue overhealth (negative heal).
        /// Does not top up base HP first.
        /// </summary>
        public float overhealOnDamaged;

        /// <summary>Cooldown between overheal-on-damaged procs.</summary>
        public float overhealOnDamagedCooldown;

        /// <summary>Blend turret/mortar shot stats toward the player's primary gun.</summary>
        public bool copyPlayerWeaponStats;

        /// <summary>0–1 portion of primary weapon stats to blend in (e.g. 0.45).</summary>
        public float weaponStatPortion;

        /// <summary>Friend prefers the enemy the player last damaged.</summary>
        public bool sharePlayerTarget;

        /// <summary>How long a player-hit enemy stays the designated target.</summary>
        public float sharedTargetDuration;

        /// <summary>
        /// Active deployables (any form) count as Swarm Launcher Breeding Season (FriendFire) allies.
        /// </summary>
        public bool countsAsSwarmAlly;

    }



    [SerializeField]
    private Data data = CreateDefaultData();

    private Data prefabSnapshot = CreateDefaultData();

    private string description = "Friend in a Box";

    public ref Data GrenadeData => ref data;

    public string Description => description;

    public static Data CreateDefaultData()
    {
        return new Data
        {
            deployDuration = 20f,
            detectRadius = 0f,
            detectRadiusMultiplier = 1f,
            explosionRadiusMultiplier = 1f,
            acidPuddleOnExpireDuration = 0f,
            expireMoveSpeedBonus = 0f,
            expireMoveSpeedDuration = 0f,
            deployMode = FriendDeployMode.None,
            fireRate = 2f,
            modeDamageScale = 0.35f,
            mortarRangeMultiplier = 1f,
            // Horizontal shoulder offset only — vertical hover is HoverHeight on the deployable.
            droneFollowDistance = 0.85f,
            droneMoveSpeed = 14f,

            maxConcurrentDeploys = 1,
            lifestealFraction = 0f,
            chargeOnKill = 0f,
            durationOnKill = 0f,
            markDamageBonus = 0f,
            markDuration = 0f,
            shootToDetonate = false,
            overhealOnDamaged = 0f,
            overhealOnDamagedCooldown = 3f,
            copyPlayerWeaponStats = false,
            weaponStatPortion = 0f,
            sharePlayerTarget = false,
            sharedTargetDuration = 0f,
            countsAsSwarmAlly = false
        };
    }



    public void InitializeAsPrefab(string desc)
    {
        description = desc ?? "Friend in a Box";
        data = CreateDefaultData();
        prefabSnapshot = data;
    }

    public void RestoreFromPrefab()
    {
        data = prefabSnapshot;
    }

    public void CapturePrefabSnapshot()
    {
        prefabSnapshot = data;
    }

    public void CopySnapshotFrom(FriendinaBoxBehaviour template)
    {
        if (template == null)
            return;
        prefabSnapshot = template.prefabSnapshot;
        data = prefabSnapshot;
        description = template.description;
    }

    public static bool TryGet(IGear gear, out FriendinaBoxBehaviour behaviour)
    {
        behaviour = null;
        if (gear?.gameObject == null)
            return false;

        behaviour = gear.gameObject.GetComponent<FriendinaBoxBehaviour>();
        if (behaviour != null)
            return true;

        bool isOurs = gear.Info != null && gear.Info.APIName == FriendinaBoxPlugin.GearApiName;
        FriendinaBoxBehaviour prefabBehaviour = null;
        if (gear.Prefab is Component prefabComp)
            prefabBehaviour = prefabComp.GetComponent<FriendinaBoxBehaviour>();

        if (!isOurs && prefabBehaviour == null)
            return false;

        string desc = prefabBehaviour != null
            ? prefabBehaviour.Description
            : FriendinaBoxPlugin.GearDescription;
        behaviour = gear.gameObject.AddComponent<FriendinaBoxBehaviour>();
        behaviour.InitializeAsPrefab(desc);
        if (prefabBehaviour != null)
            behaviour.data = prefabBehaviour.prefabSnapshot;
        behaviour.CapturePrefabSnapshot();
        return true;
    }

    public static bool IsEquippedOnLocalPlayer()
    {
        Player local = Player.LocalPlayer;
        if (local?.Gear == null)
            return false;

        for (int i = 0; i < local.Gear.Length; i++)
        {
            IGear gear = local.Gear[i];
            if (gear == null)
                continue;

            if (gear.Info != null && gear.Info.APIName == FriendinaBoxPlugin.GearApiName)
                return true;

            if (gear.Info != null && gear.Info.ID == FriendinaBoxPlugin.GearId)
                return true;

            if (gear.gameObject != null && gear.gameObject.GetComponent<FriendinaBoxBehaviour>() != null)
                return true;
        }

        return false;
    }

    /// <summary>Resolve live equipped Friend gear behaviour (if any).</summary>
    public static bool TryGetEquipped(out FriendinaBoxBehaviour behaviour, out IGear gear)
    {
        behaviour = null;
        gear = null;
        Player local = Player.LocalPlayer;
        if (local?.Gear == null)
            return false;

        for (int i = 0; i < local.Gear.Length; i++)
        {
            IGear g = local.Gear[i];
            if (g == null)
                continue;
            if (TryGet(g, out FriendinaBoxBehaviour b))
            {
                // Prefer true Friend gear identity.
                if (g.Info != null &&
                    (g.Info.APIName == FriendinaBoxPlugin.GearApiName || g.Info.ID == FriendinaBoxPlugin.GearId))
                {
                    behaviour = b;
                    gear = g;
                    return true;
                }

                if (behaviour == null)
                {
                    behaviour = b;
                    gear = g;
                }
            }
        }

        return behaviour != null;
    }
}
