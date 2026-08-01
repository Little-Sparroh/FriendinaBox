using System;
using Pigeon.Movement;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Field entity spawned when a Friend grenade lands.
/// Modes: Mine (default), Turret, Mortar, Drone (+ hybrids).
/// Local / sandbox scaffolding — magenta/colored debug box visuals.
/// </summary>
public sealed class FriendDeployable : MonoBehaviour
{
    private IGear ownerGear;
    private Player ownerPlayer;
    private FriendinaBoxBehaviour.Data friendData;
    private FriendDeployMode mode;
    private float remainingDuration;
    private float detectRadius;
    private float explosionRadius;
    private DamageData damage;
    private float explosionShake;
    private bool finished;
    private float nextScanTime;
    private float nextFireTime;
    private bool droneDiving;
    private Vector3 droneDiveTarget;
    private float maxDuration;
    private SphereCollider hitProxy;
    private float effectiveFireRate;

    private const float ScanInterval = 0.1f;

    /// <summary>How high above the player the drone sits (world units).</summary>
    private const float HoverHeight = 2.4f;
    /// <summary>Minimum engagement range for turret / mortar modes.</summary>
    private const float ModeEngageRange = 50f;


    public FriendDeployMode Mode => mode;
    public float RemainingDuration => remainingDuration;
    public float MaxDuration => maxDuration;


    public static FriendDeployable Spawn(
        Vector3 position,
        IGear gear,
        FriendinaBoxBehaviour.Data friendData,
        float explosionRadius,
        DamageData damage,
        float explosionShake,
        float selfEffectMultiplier)
    {
        var go = new GameObject("[FriendinaBox] Deployable");
        go.transform.position = position;
        FriendDeployable deploy = go.AddComponent<FriendDeployable>();
        deploy.Arm(gear, friendData, explosionRadius, damage, explosionShake);
        return deploy;
    }

    private void Arm(
        IGear gear,
        FriendinaBoxBehaviour.Data friendData,
        float explosionRadius,
        DamageData damage,
        float explosionShake)
    {
        ownerGear = gear;
        ownerPlayer = gear is Throwable t ? t.Player : Player.LocalPlayer;
        this.friendData = friendData;
        mode = friendData.deployMode;
        remainingDuration = Mathf.Max(0.5f, friendData.deployDuration);
        maxDuration = remainingDuration;
        this.explosionRadius = Mathf.Max(0.1f, explosionRadius);

        // Multi-deploy: raise tracker cap from snapshot.
        if (friendData.maxConcurrentDeploys > FriendDeployTracker.MaxConcurrentDeploys)
            FriendDeployTracker.MaxConcurrentDeploys = friendData.maxConcurrentDeploys;


        float baseDetect = friendData.detectRadius > 0f
            ? friendData.detectRadius
            : this.explosionRadius * 0.75f;
        detectRadius = baseDetect * Mathf.Max(0.01f, friendData.detectRadiusMultiplier);

        this.damage = damage;
        this.explosionShake = explosionShake;
        finished = false;
        nextScanTime = 0f;
        nextFireTime = Time.time + 0.35f;
        droneDiving = false;
        effectiveFireRate = Mathf.Max(0.25f, friendData.fireRate);


        // Drone starts above the land point so it doesn't spawn in the ground/player.
        if ((mode & FriendDeployMode.Drone) != 0)
        {
            Vector3 p = transform.position;
            p.y += HoverHeight;
            transform.position = p;
        }


        FriendDeployTracker.Register(this);
        FriendDebugVisual.Attach(
            transform,
            FriendDeployModeUtil.GetDebugColor(mode),
            scale: (mode & FriendDeployMode.Drone) != 0 ? 0.55f : 0.75f);

        // Soft hit proxy so shoot-to-detonate raycasts can land.
        if (friendData.shootToDetonate)
        {
            hitProxy = gameObject.AddComponent<SphereCollider>();
            hitProxy.isTrigger = true;
            hitProxy.radius = 0.9f;
            hitProxy.center = Vector3.up * 0.4f;
        }

        // Bind combat hooks on owner gear for lifesteal / kills / mark.
        if (gear != null)
            FriendCombatHooks.EnsureBound(gear, friendData);
        FriendCombatRunner.Ensure();

        FriendinaBoxPlugin.Logger?.LogInfo(
            $"[FriendinaBox] {FriendDeployModeUtil.GetLabel(mode)} armed at {transform.position} " +
            $"duration={remainingDuration:0.#}s detect={detectRadius:0.##} explode={this.explosionRadius:0.##}");
    }

    public void ExtendDuration(float seconds)
    {
        if (finished || seconds <= 0f)
            return;
        remainingDuration += seconds;
        maxDuration = Mathf.Max(maxDuration, remainingDuration);
        FriendinaBoxPlugin.Logger?.LogInfo(
            $"[FriendinaBox] Duration +{seconds:0.#}s → {remainingDuration:0.#}s remaining");
    }

    /// <summary>Player shot the deployable — explode scaled by remaining duration fraction.</summary>
    public void DetonateFromPlayerShot()
    {
        if (finished)
            return;

        float frac = maxDuration > 0.01f
            ? Mathf.Clamp01(remainingDuration / maxDuration)
            : 1f;
        // At least 35% power so early scuttle still hurts; full power near end of life.
        float power = Mathf.Lerp(0.35f, 1f, frac);
        explosionRadius *= power;
        damage.damage *= power;
        damage.effectAmount *= power;

        FriendinaBoxPlugin.Logger?.LogInfo(
            $"[FriendinaBox] Scuttle shot detonate power={power:P0} remaining={remainingDuration:0.#}s");
        Detonate("player shot");
    }


    private void OnDestroy()
    {
        FriendDeployTracker.Unregister(this);
    }

    /// <summary>Quiet remove — no explode / expire effects (single-deploy replacement).</summary>
    public void ForceDespawn()
    {
        if (finished)
            return;
        finished = true;
        FriendinaBoxPlugin.Logger?.LogInfo(
            $"[FriendinaBox] {FriendDeployModeUtil.GetLabel(mode)} force-despawned at {transform.position}");
        FriendDeployTracker.Unregister(this);
        Destroy(gameObject);
    }

    private void Update()
    {
        if (finished)
            return;

        remainingDuration -= Time.deltaTime;
        if (remainingDuration <= 0f)
        {
            Expire();
            return;
        }

        FriendDeployMode primary = FriendDeployModeUtil.GetPrimary(mode);
        switch (primary)
        {
            case FriendDeployMode.None:
                TickMine();
                break;
            case FriendDeployMode.Turret:
                TickTurret();
                break;
            case FriendDeployMode.Mortar:
                TickMortar();
                break;
            case FriendDeployMode.Drone:
                TickDrone();
                break;
        }
    }

    #region Mode ticks

    private void TickMine()
    {
        if (Time.time < nextScanTime)
            return;
        nextScanTime = Time.time + ScanInterval;

        if (TryFindEnemy(detectRadius, out _))
            Detonate("proximity");
    }

    private float GetTurretRange()
    {
        // Turrets need long engagement — floor at ModeEngageRange (~50).
        return Mathf.Max(detectRadius, ModeEngageRange);
    }

    private float GetMortarRange()
    {
        float scaled = detectRadius * Mathf.Max(1f, friendData.mortarRangeMultiplier);
        return Mathf.Max(scaled, ModeEngageRange);
    }

    private void TickTurret()
    {
        if (Time.time < nextFireTime)
            return;

        float interval = 1f / Mathf.Max(0.25f, effectiveFireRate);
        if (!TryFindEnemy(GetTurretRange(), out ITarget target))

        {
            nextFireTime = Time.time + ScanInterval;
            return;
        }

        nextFireTime = Time.time + interval;
        FireAtTarget(target, lobbed: false);
    }

    private void TickMortar()
    {
        if (Time.time < nextFireTime)
            return;

        float interval = 1f / Mathf.Max(0.15f, effectiveFireRate * 0.55f);
        if (!TryFindEnemy(GetMortarRange(), out ITarget target))

        {
            nextFireTime = Time.time + ScanInterval;
            return;
        }

        nextFireTime = Time.time + interval;
        FireAtTarget(target, lobbed: true);
    }

    private void TickDrone()
    {
        // Hover above the player; multi-drones use formation slots so they don't stack.
        if (!droneDiving && ownerPlayer != null)
        {
            Vector3 playerPos = ownerPlayer.InterpolatedPosition;

            FriendDeployTracker.GetDroneFormationSlot(this, out int slot, out int droneCount);
            float ringRadius = Mathf.Clamp(friendData.droneFollowDistance, 0.75f, 1.6f);
            // Slightly wider ring when more drones are out.
            if (droneCount > 1)
                ringRadius = Mathf.Max(ringRadius, 1.1f + 0.15f * (droneCount - 1));

            Vector3 localOffset = FriendDeployTracker.GetDroneFormationOffset(slot, droneCount, ringRadius);
            // Slight height stagger so stacked-looking angles still separate.
            float heightStagger = (droneCount > 1) ? (slot - (droneCount - 1) * 0.5f) * 0.2f : 0f;

            // Rotate offset into player yaw so formation follows facing.
            Quaternion yaw = Quaternion.Euler(0f, ownerPlayer.transform.eulerAngles.y, 0f);
            Vector3 worldOffset = yaw * localOffset;

            Vector3 goal = playerPos
                + Vector3.up * (HoverHeight + heightStagger)
                + worldOffset;

            float speed = Mathf.Max(1f, friendData.droneMoveSpeed);
            transform.position = Vector3.MoveTowards(transform.position, goal, speed * Time.deltaTime);
        }


        // Hybrid: turret or mortar while following.
        bool hasTurret = (mode & FriendDeployMode.Turret) != 0;
        bool hasMortar = (mode & FriendDeployMode.Mortar) != 0;

        if (hasTurret || hasMortar)
        {
            if (Time.time >= nextFireTime)
            {
                float range = hasMortar ? GetMortarRange() : GetTurretRange();
                if (TryFindEnemy(range, out ITarget target))
                {
                    float rate = effectiveFireRate * (hasMortar && !hasTurret ? 0.55f : 1f);
                    nextFireTime = Time.time + 1f / Mathf.Max(0.25f, rate);

                    FireAtTarget(target, lobbed: hasMortar && !hasTurret);
                }
                else
                {
                    nextFireTime = Time.time + ScanInterval;
                }
            }
            return; // hybrid drones don't suicide-dive
        }


        // Default suicide drone: dive on nearest enemy in detect radius.
        if (!droneDiving)
        {
            if (Time.time < nextScanTime)
                return;
            nextScanTime = Time.time + ScanInterval;

            if (TryFindEnemy(detectRadius * 1.5f, out ITarget target))
            {
                droneDiving = true;
                droneDiveTarget = target.GetHealthbarPosition();
                FriendinaBoxPlugin.Logger?.LogInfo($"[FriendinaBox] Drone diving toward {droneDiveTarget}");
            }
            return;
        }

        float diveSpeed = Mathf.Max(1f, friendData.droneMoveSpeed * 1.75f);
        transform.position = Vector3.MoveTowards(transform.position, droneDiveTarget, diveSpeed * Time.deltaTime);
        if ((transform.position - droneDiveTarget).sqrMagnitude <= 0.35f * 0.35f)
            Detonate("suicide dive");
    }

    #endregion

    #region Combat helpers

    private static readonly RangeData LongRangeData = new RangeData
    {
        falloffStartDistance = 799f,
        falloffEndDistance = 800f,
        maxFalloffDamageMultiplier = 1f,
        maxDamageRange = 800f
    };

    // Match enemy projectile masks from ProjectileGunArmTip.
    private const int SurfaceMask = 10241;
    private const int TargetMaskNonPlayer = 345216;

    private void FireAtTarget(ITarget target, bool lobbed)
    {
        if (target == null)
            return;

        if (ownerGear is not IDamageSource source)
        {
            FireExplosionFallback(target, lobbed);
            return;
        }

        Vector3 origin = transform.position + Vector3.up * 0.35f;
        Vector3 aim = target.GetHealthbarPosition();
        Vector3 toTarget = aim - origin;
        float dist = toTarget.magnitude;
        if (dist < 0.05f)
            return;

        Vector3 dir = toTarget / dist;
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);

        float dmgScale = Mathf.Max(0.05f, friendData.modeDamageScale);
        float shotDamage = damage.damage * dmgScale;
        float shotEffect = damage.effectAmount * dmgScale;
        EffectType shotEffectType = damage.effect;
        int pellets = 1;

        // Calibrated Link: blend toward player's primary weapon stats.
        if (friendData.copyPlayerWeaponStats &&
            friendData.weaponStatPortion > 0f &&
            TryGetPrimaryWeaponStats(
                out float primaryDmg,
                out EffectType primaryFx,
                out float primaryFxAmt,
                out float primaryInterval,
                out int primaryBps))
        {
            float t = Mathf.Clamp01(friendData.weaponStatPortion);
            shotDamage = Mathf.Lerp(shotDamage, primaryDmg * dmgScale, t);
            shotEffect = Mathf.Lerp(shotEffect, primaryFxAmt * dmgScale, t);
            if (t >= 0.25f && primaryFx > EffectType.Normal)
                shotEffectType = primaryFx;

            // Optionally snappier fire rate from primary (capped) — local only, not compounding.
            if (primaryInterval > 0.01f)
            {
                float primaryRate = 1f / primaryInterval;
                float baseRate = Mathf.Max(0.25f, friendData.fireRate);
                float blended = Mathf.Lerp(baseRate, primaryRate, t * 0.65f);
                effectiveFireRate = Mathf.Clamp(blended, 0.5f, 12f);
            }

            // Bullets-per-shot: blend 1 → primary BPS by portion (turret/rail only).
            if (!lobbed && primaryBps > 1)
            {
                float blendedBps = Mathf.Lerp(1f, primaryBps, t);
                pellets = Mathf.Clamp(Mathf.RoundToInt(blendedBps), 1, 8);
            }
        }

        try
        {
            bool fired;
            if (lobbed)
            {
                fired = TryFireMortar(source, origin, rot, dir, aim, target, shotDamage, shotEffect, shotEffectType);
            }
            else
            {
                fired = false;
                // Multi-pellet rail volley with light cone spread.
                float spreadDeg = pellets > 1 ? Mathf.Lerp(1.2f, 4.5f, (pellets - 1) / 7f) : 0f;
                for (int i = 0; i < pellets; i++)
                {
                    Quaternion pelletRot = rot;
                    if (pellets > 1)
                    {
                        float yaw = UnityEngine.Random.Range(-spreadDeg, spreadDeg);
                        float pitch = UnityEngine.Random.Range(-spreadDeg * 0.65f, spreadDeg * 0.65f);
                        pelletRot = rot * Quaternion.Euler(pitch, yaw, 0f);
                    }

                    Vector3 pelletDir = pelletRot * Vector3.forward;
                    if (TryFireRail(source, origin, pelletRot, pelletDir, shotDamage, shotEffect, shotEffectType))
                        fired = true;
                }
            }

            if (!fired)
                FireExplosionFallback(target, lobbed, shotDamage, shotEffect, shotEffectType);
        }
        catch (Exception ex)
        {
            FriendinaBoxPlugin.Logger?.LogWarning($"[FriendinaBox] Mode fire failed: {ex.Message}");
            FireExplosionFallback(target, lobbed, shotDamage, shotEffect, shotEffectType);
        }
    }

    private bool TryGetPrimaryWeaponStats(
        out float primaryDamage,
        out EffectType primaryEffect,
        out float primaryEffectAmount,
        out float primaryFireInterval,
        out int primaryBulletsPerShot)
    {
        primaryDamage = 0f;
        primaryEffect = EffectType.Normal;
        primaryEffectAmount = 0f;
        primaryFireInterval = 0f;
        primaryBulletsPerShot = 1;

        Player p = ownerPlayer != null ? ownerPlayer : Player.LocalPlayer;
        if (p?.Gear == null)
            return false;

        for (int i = 0; i < p.Gear.Length; i++)
        {
            IGear g = p.Gear[i];
            if (g == null)
                continue;
            // Skip Friend / throwables — want the gun.
            if (g is Throwable)
                continue;
            if (g.Info != null &&
                (g.Info.APIName == FriendinaBoxPlugin.GearApiName || g.Info.ID == FriendinaBoxPlugin.GearId))
                continue;
            if (g is not IWeapon weapon)
                continue;

            ref GunData gd = ref weapon.GunData;
            primaryDamage = gd.damage;
            primaryEffect = gd.damageEffect;
            primaryEffectAmount = gd.damageEffectAmount;
            primaryFireInterval = gd.fireInterval;
            primaryBulletsPerShot = Mathf.Max(1, gd.bulletsPerShot);
            return primaryDamage > 0f;
        }

        return false;
    }



    private bool TryFireRail(
        IDamageSource source,
        Vector3 origin,
        Quaternion rot,
        Vector3 dir,
        float shotDamage,
        float shotEffect,
        EffectType shotEffectType)
    {
        GameObject prefab = FriendBulletCache.RailPrefab;
        if (prefab == null)
            return false;

        GameObject instance = SimplePool.Get(prefab);
        if (instance == null)
            return false;

        IBullet bullet = instance.GetComponent<IBullet>();
        if (bullet == null)
        {
            SimplePool.Release(prefab, instance);
            return false;
        }

        var data = new BulletData
        {
            position = origin,
            rotation = rot,
            direction = dir,
            gravity = 0f,
            speed = 400f,
            damage = shotDamage,
            damageEffect = shotEffectType,
            damageEffectAmount = shotEffect,
            damageFlags = damage.damageFlags,
            maxBounces = 0,
            surfaceCollisionMask = SurfaceMask,
            targetCollisionMask = TargetMaskNonPlayer,
            surfaceMagnetism = 0f,
            targetMagnetism = 0f,
            range = LongRangeData,
            force = 0f,
            impactSize = 1f
        };


        // IsOwner | ShowDamageText — do NOT set SpawnObserverBullets (casts ParentSource to Gun).
        BulletFlags flags = BulletFlags.IsOwner | BulletFlags.ShowDamageText | BulletFlags.CustomSpawned;
        Action<IBullet> release = b =>
        {
            if (b is Component c && c != null)
                SimplePool.Release(prefab, c.gameObject);
        };

        bullet.Initialize(data, source, release, flags);
        return true;
    }

    private bool TryFireMortar(
        IDamageSource source,
        Vector3 origin,
        Quaternion rot,
        Vector3 dir,
        Vector3 aimPoint,
        ITarget target,
        float shotDamage,
        float shotEffect,
        EffectType shotEffectType)
    {
        GameObject prefab = FriendBulletCache.MortarPrefab;
        if (prefab == null)
            return false;

        GameObject instance = SimplePool.Get(prefab);
        if (instance == null)
            return false;

        IBullet bullet = instance.GetComponent<IBullet>();
        if (bullet == null)
        {
            SimplePool.Release(prefab, instance);
            return false;
        }

        float force = Mathf.Max(0.75f, explosionRadius * 0.65f);
        var data = new BulletData
        {
            position = origin,
            rotation = rot,
            direction = dir,
            gravity = 18f,
            speed = 28f,
            damage = shotDamage,
            damageEffect = shotEffectType,
            damageEffectAmount = shotEffect,
            damageFlags = damage.damageFlags | DamageFlags.AOE,
            maxBounces = 0,
            surfaceCollisionMask = SurfaceMask,
            targetCollisionMask = TargetMaskNonPlayer,
            surfaceMagnetism = 0f,
            targetMagnetism = 1f,
            range = LongRangeData,
            force = force,
            impactSize = 1f
        };


        BulletFlags flags = BulletFlags.IsOwner | BulletFlags.ShowDamageText | BulletFlags.CustomSpawned;
        Action<IBullet> release = b =>
        {
            if (b is Component c && c != null)
                SimplePool.Release(prefab, c.gameObject);
        };

        bullet.Initialize(data, source, release, flags);

        if (bullet is RocketSalvoBullet rocket)
            rocket.SetTarget(target, aimPoint);

        return true;
    }

    private void FireExplosionFallback(
        ITarget target,
        bool lobbed,
        float shotDamageOverride = -1f,
        float shotEffectOverride = -1f,
        EffectType? shotEffectTypeOverride = null)
    {
        if (target == null || GameManager.Instance == null)
            return;

        Vector3 aim = target.GetHealthbarPosition();
        float dmgScale = Mathf.Max(0.05f, friendData.modeDamageScale);
        float dmg = shotDamageOverride >= 0f ? shotDamageOverride : damage.damage * dmgScale;
        float fxAmt = shotEffectOverride >= 0f ? shotEffectOverride : damage.effectAmount * dmgScale;
        EffectType fx = shotEffectTypeOverride ?? damage.effect;
        var shotDamage = new DamageData(dmg, fx, fxAmt, damage.damageFlags);


        float radius = lobbed
            ? explosionRadius * 0.65f
            : Mathf.Max(0.4f, explosionRadius * 0.2f);

        try
        {
            if (ownerGear is Throwable throwable)
            {
                GameManager.Instance.SpawnExplosionFirstPerson(
                    throwable,
                    aim,
                    radius,
                    TargetType.NonPlayer,
                    shotDamage,
                    lobbed ? explosionShake * 0.35f : explosionShake * 0.15f);
            }
            else if (ownerGear is IDamageSource source)
            {
                GameManager.Instance.SpawnExplosionFirstPerson(
                    source,
                    aim,
                    radius,
                    TargetType.NonPlayer,
                    shotDamage,
                    lobbed ? explosionShake * 0.35f : explosionShake * 0.15f);
            }
        }
        catch (Exception ex)
        {
            FriendinaBoxPlugin.Logger?.LogWarning($"[FriendinaBox] Fallback fire failed: {ex.Message}");
        }
    }


    private bool TryFindEnemy(float radius, out ITarget found)
    {
        found = null;
        float bestSq = float.MaxValue;
        Vector3 origin = transform.position;

        // Designated Target: prefer the enemy the player last hit.
        if (friendData.sharePlayerTarget &&
            FriendSharedTarget.TryGet(origin, radius, out ITarget shared) &&
            IsValidEnemy(shared))
        {
            found = shared;
            return true;
        }

        try
        {

            IDamageSource.TargetEnumerator enumerator = default;
            try
            {
                if (enumerator.GetTargetsInSphere(origin, radius, ~0, TargetType.Enemy))
                {
                    while (enumerator.MoveNext())
                    {
                        ITarget target = enumerator.Current;
                        if (!IsValidEnemy(target))
                            continue;
                        float sq = (target.GetHealthbarPosition() - origin).sqrMagnitude;
                        if (sq < bestSq)
                        {
                            bestSq = sq;
                            found = target;
                        }
                    }
                }
            }
            finally
            {
                ((IDisposable)enumerator).Dispose();
            }
        }
        catch
        {
            // Physics fallback below.
        }

        if (found != null)
            return true;

        Collider[] hits = Physics.OverlapSphere(origin, radius, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            ITarget target = IDamageSource.GetTarget(hits[i]);
            if (!IsValidEnemy(target))
                continue;
            float sq = (target.GetHealthbarPosition() - origin).sqrMagnitude;
            if (sq < bestSq)
            {
                bestSq = sq;
                found = target;
            }
        }

        return found != null;
    }

    private static bool IsValidEnemy(ITarget target)
    {
        if (target == null || !target.IsAlive)
            return false;
        if (target.IsPlayer())
            return false;
        if ((target.Type & TargetType.Enemy) != 0 || target.Type == TargetType.Enemy)
            return true;
        if ((target.Type & TargetType.Player) == 0 && target.MaxHealth > 0f)
            return true;
        return false;
    }

    #endregion

    #region End states

    private void Detonate(string reason)
    {
        if (finished)
            return;
        finished = true;

        Vector3 pos = transform.position;
        FriendinaBoxPlugin.Logger?.LogInfo(
            $"[FriendinaBox] {FriendDeployModeUtil.GetLabel(mode)} detonated ({reason}) at {pos}");

        try
        {
            if (GameManager.Instance != null && ownerGear is Throwable throwable)
            {
                GameManager.Instance.SpawnExplosionFirstPerson(
                    throwable,
                    pos,
                    explosionRadius,
                    TargetType.NonPlayer,
                    damage,
                    explosionShake);
            }
            else if (GameManager.Instance != null && ownerGear is IDamageSource source)
            {
                GameManager.Instance.SpawnExplosionFirstPerson(
                    source,
                    pos,
                    explosionRadius,
                    TargetType.NonPlayer,
                    damage,
                    explosionShake);
            }
        }
        catch (Exception ex)
        {
            FriendinaBoxPlugin.Logger?.LogError($"[FriendinaBox] Detonate failed: {ex}");
        }

        FriendDeployTracker.Unregister(this);
        Destroy(gameObject);
    }

    private void Expire()
    {
        if (finished)
            return;
        finished = true;

        Vector3 pos = transform.position;
        FriendinaBoxPlugin.Logger?.LogInfo(
            $"[FriendinaBox] {FriendDeployModeUtil.GetLabel(mode)} expired at {pos}");

        TrySpawnAcidPuddle(pos);
        TryGrantExpireMoveSpeed();

        FriendDeployTracker.Unregister(this);
        Destroy(gameObject);
    }

    private void TrySpawnAcidPuddle(Vector3 pos)
    {
        if (friendData.acidPuddleOnExpireDuration <= 0f)
            return;

        try
        {
            if (GameManager.Instance == null || ownerGear is not IDamageSource)
                return;

            float size = Mathf.Max(1f, explosionRadius * 0.85f);
            NetworkBehaviourReference sourceRef = IDamageSource.GetNetworkRef(ownerGear as IDamageSource);
            GameManager.Instance.SpawnAcidPuddle_Rpc(
                sourceRef,
                pos,
                friendData.acidPuddleOnExpireDuration,
                TargetType.NonPlayer,
                size);
        }
        catch (Exception ex)
        {
            FriendinaBoxPlugin.Logger?.LogWarning($"[FriendinaBox] Acid puddle spawn failed: {ex.Message}");
        }
    }

    private void TryGrantExpireMoveSpeed()
    {
        if (friendData.expireMoveSpeedBonus <= 0f || friendData.expireMoveSpeedDuration <= 0f)
            return;

        Player target = ownerPlayer != null ? ownerPlayer : Player.LocalPlayer;
        if (target == null || !target.IsLocalPlayer)
            return;

        FriendExpireSpeedBuff.Apply(
            target,
            friendData.expireMoveSpeedBonus,
            friendData.expireMoveSpeedDuration);
    }

    #endregion
}
