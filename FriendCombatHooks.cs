using System;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;

/// <summary>
/// Runtime combat hooks for Friend upgrades:
/// lifesteal (blue overhealth via HealWithOverhealth), kill→charge, kill→duration,
/// mark amplify, shoot-to-detonate, overheal-on-damaged.
/// </summary>
public static class FriendCombatHooks
{
    private static bool _playerHooksBound;
    private static DamageCallback _onGearDamage;
    private static KillCallback _onGearKill;
    private static MutableDamageCallback _onPlayerBeforeDamage;
    private static RefAction<DamageData, IDamageSource> _onPlayerAfterTakeDamage;
    private static float _nextOverhealTime;
    private static IGear _hookedGear;

    public static void EnsureBound(IGear gear, FriendinaBoxBehaviour.Data data)
    {
        if (gear is not IDamageSource source)
            return;

        if (_hookedGear != gear)
        {
            UnbindGear();
            _hookedGear = gear;
            _onGearDamage = OnFriendDamage;
            _onGearKill = OnFriendKill;
            try
            {
                source.OnDamageTarget = (DamageCallback)Delegate.Combine(source.OnDamageTarget, _onGearDamage);
                source.OnKillTarget = (KillCallback)Delegate.Combine(source.OnKillTarget, _onGearKill);
            }
            catch (Exception ex)
            {
                FriendinaBoxPlugin.Logger?.LogWarning($"[FriendinaBox] Gear combat bind failed: {ex.Message}");
            }
        }

        EnsurePlayerHooks();
    }

    public static void UnbindGear()
    {
        if (_hookedGear == null)
            return;

        try
        {
            if (_hookedGear is IDamageSource source)
            {
                if (_onGearDamage != null)
                    source.OnDamageTarget = (DamageCallback)Delegate.Remove(source.OnDamageTarget, _onGearDamage);
                if (_onGearKill != null)
                    source.OnKillTarget = (KillCallback)Delegate.Remove(source.OnKillTarget, _onGearKill);
            }
        }
        catch
        {
            // gear may be destroyed
        }

        _hookedGear = null;
    }

    private static void EnsurePlayerHooks()
    {
        if (_playerHooksBound)
            return;

        Player local = Player.LocalPlayer;
        if (local == null)
            return;

        _onPlayerBeforeDamage = OnPlayerBeforeDamageOutgoing;
        _onPlayerAfterTakeDamage = OnPlayerTookDamage;
        try
        {
            local.OnBeforeDamage = (MutableDamageCallback)Delegate.Combine(local.OnBeforeDamage, _onPlayerBeforeDamage);
            local.OnAfterTakeDamage += _onPlayerAfterTakeDamage;
            _playerHooksBound = true;
        }
        catch (Exception ex)
        {
            FriendinaBoxPlugin.Logger?.LogWarning($"[FriendinaBox] Player combat bind failed: {ex.Message}");
        }
    }


    private static bool TryGetLiveData(out FriendinaBoxBehaviour.Data data, out IGear gear, out Player player)
    {
        data = default;
        gear = null;
        player = Player.LocalPlayer;
        if (!FriendinaBoxBehaviour.TryGetEquipped(out FriendinaBoxBehaviour b, out gear) || b == null)
            return false;
        data = b.GrenadeData;
        EnsureBound(gear, data);
        return true;
    }

    private static bool IsFriendSourced(IDamageSource source)
    {
        if (source == null)
            return false;
        if (_hookedGear != null && source == _hookedGear)
            return true;

        for (IDamageSource s = source; s != null; s = s.ParentSource)
        {
            if (_hookedGear != null && s == _hookedGear)
                return true;
            if (s is IGear g && g.Info != null &&
                (g.Info.APIName == FriendinaBoxPlugin.GearApiName || g.Info.ID == FriendinaBoxPlugin.GearId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Blue overhealth only — uses HealWithOverhealth so base HP is not topped up first.
    /// </summary>
    private static void GrantBlueOverhealth(Player player, IDamageSource source, float amount)
    {
        if (player == null || !player.IsAlive || amount <= 0f)
            return;

        try
        {
            // Preferred API (Incendiary uses this for health-on-throw style overheal).
            player.HealWithOverhealth(amount, source);
        }
        catch
        {
            try
            {
                // Fallback: negative heal amount = overhealth (Acid grenade pattern).
                IDamageSource.HealTarget(source, player, -amount, player.InterpolatedPosition);
            }
            catch (Exception ex)
            {
                FriendinaBoxPlugin.Logger?.LogWarning($"[FriendinaBox] Overheal failed: {ex.Message}");
            }
        }
    }

    private static void OnFriendDamage(in DamageCallbackData data)
    {
        if (!TryGetLiveData(out FriendinaBoxBehaviour.Data friend, out IGear gear, out Player player))
            return;
        if (!IsFriendSourced(data.source))
            return;

        float dealt = data.damageData.damage;
        if (dealt <= 0f)
            return;

        // Lifesteal → blue overhealth (does not fill base HP first).
        if (friend.lifestealFraction > 0f && player != null && player.IsLocalPlayer)
        {
            float overheal = dealt * friend.lifestealFraction;
            if (overheal > 0f && gear is IDamageSource src)
                GrantBlueOverhealth(player, src, overheal);
        }

        if (friend.markDamageBonus > 0f && friend.markDuration > 0f && data.target != null)
            FriendMarkedTarget.Apply(data.target, friend.markDamageBonus, friend.markDuration);
    }

    private static void OnFriendKill(in KillCallbackData data)
    {
        if (!TryGetLiveData(out FriendinaBoxBehaviour.Data friend, out IGear gear, out _))
            return;
        if (!IsFriendSourced(data.source))
            return;

        if (friend.chargeOnKill > 0f && gear is Throwable throwable)
        {
            try
            {
                // AddCharge expects normalized charge units (1 = one full charge).
                throwable.CooldownData.AddCharge(friend.chargeOnKill);
            }
            catch
            {
                try
                {
                    throwable.CooldownData.charge = Mathf.Min(
                        throwable.CooldownData.maxCharges,
                        throwable.CooldownData.charge + friend.chargeOnKill);
                }
                catch
                {
                    // ignore
                }
            }
        }

        if (friend.durationOnKill > 0f)
            FriendDeployTracker.ExtendAllDurations(friend.durationOnKill);
    }

    private static void OnPlayerBeforeDamageOutgoing(ref DamageCallbackData data)
    {
        if (data.target == null || data.damageData.damage <= 0f)
            return;

        // Designated Target: remember what the player is shooting.
        if (TryGetLiveData(out FriendinaBoxBehaviour.Data friend, out _, out _) &&
            friend.sharePlayerTarget &&
            friend.sharedTargetDuration > 0f &&
            !IsFriendSourced(data.source))
        {
            FriendSharedTarget.Set(data.target, friend.sharedTargetDuration);
        }

        // Painted Targets mark amplify.
        if (FriendMarkedTarget.TryGetBonus(data.target, out float bonus) && bonus > 0f)
            data.damageData.damage *= (1f + bonus);
    }


    private static void OnPlayerTookDamage(ref DamageData damage, ref IDamageSource source)
    {
        if (!TryGetLiveData(out FriendinaBoxBehaviour.Data friend, out IGear gear, out Player player))
            return;
        if (friend.overhealOnDamaged <= 0f || player == null || !player.IsLocalPlayer || !player.IsAlive)
            return;
        if (Time.time < _nextOverhealTime)
            return;

        _nextOverhealTime = Time.time + Mathf.Max(0.5f, friend.overhealOnDamagedCooldown);

        if (gear is IDamageSource src)
        {
            GrantBlueOverhealth(player, src, friend.overhealOnDamaged);
            FriendinaBoxPlugin.Logger?.LogInfo(
                $"[FriendinaBox] Reactive Shell overheal +{friend.overhealOnDamaged:0.#} blue");
        }
    }

    public static void TickShootToDetonate()
    {
        if (!TryGetLiveData(out FriendinaBoxBehaviour.Data friend, out _, out Player player))
            return;
        if (!friend.shootToDetonate || player == null || !player.IsLocalPlayer)
            return;
        if (!FriendDeployTracker.HasActive)
            return;

        try
        {
            if (PlayerInput.Controls == null || !PlayerInput.Controls.Player.Fire.IsPressed())
                return;
        }
        catch
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam == null && PlayerLook.Instance != null)
            cam = PlayerLook.Instance.GetComponentInChildren<Camera>();
        if (cam == null)
            return;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Collide))
            return;

        FriendDeployable deploy = FriendDeployTracker.FindNearest(hit.point, 1.75f);
        if (deploy != null)
            deploy.DetonateFromPlayerShot();
    }
}

public sealed class FriendCombatRunner : MonoBehaviour
{
    private static FriendCombatRunner _instance;

    public static void Ensure()
    {
        if (_instance != null)
            return;
        var go = new GameObject("[FriendinaBox] CombatRunner");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<FriendCombatRunner>();
    }

    private void Update()
    {
        try
        {
            FriendCombatHooks.TickShootToDetonate();
        }
        catch
        {
            // never break the frame
        }
    }
}

[HarmonyPatch(typeof(Player), "OnNetworkSpawn")]
internal static class FriendPlayerSpawnHook
{
    [HarmonyPostfix]
    private static void Postfix(Player __instance)
    {
        if (__instance == null || !__instance.IsLocalPlayer)
            return;
        FriendCombatRunner.Ensure();
        if (FriendinaBoxBehaviour.TryGetEquipped(out FriendinaBoxBehaviour b, out IGear gear))
            FriendCombatHooks.EnsureBound(gear, b.GrenadeData);
    }
}
