using System;
using Pigeon.Movement;
using UnityEngine;

/// <summary>
/// Temporary move-speed buff applied when a Friend mine expires with the Parting Boost upgrade.
/// </summary>
public sealed class FriendExpireSpeedBuff : MonoBehaviour
{
    private Player player;
    private float bonus;
    private float remaining;
    private RefAction<float> onSetSpeed;
    private bool ended;


    public static void Apply(Player target, float speedBonus, float duration)
    {
        if (target == null || speedBonus <= 0f || duration <= 0f)
            return;

        FriendExpireSpeedBuff existing = target.GetComponent<FriendExpireSpeedBuff>();
        if (existing != null)
        {
            // Refresh / stack additively for duration, keep stronger bonus.
            existing.bonus = Mathf.Max(existing.bonus, speedBonus);
            existing.remaining = Mathf.Max(existing.remaining, duration);
            return;
        }

        FriendExpireSpeedBuff buff = target.gameObject.AddComponent<FriendExpireSpeedBuff>();
        buff.StartBuff(target, speedBonus, duration);
    }

    private void StartBuff(Player target, float speedBonus, float duration)
    {
        player = target;
        bonus = speedBonus;
        remaining = duration;
        onSetSpeed = ModifyMoveSpeed;
        player.OnSetMovementSpeed += onSetSpeed;
        FriendinaBoxPlugin.Logger?.LogInfo(
            $"[FriendinaBox] Expire speed buff +{bonus:P0} for {duration:0.#}s");
    }

    private void ModifyMoveSpeed(ref float speed)
    {
        speed *= (1f + bonus);
    }

    private void Update()
    {
        remaining -= Time.deltaTime;
        if (remaining <= 0f)
            EndBuff();
    }

    private void OnDestroy()
    {
        EndBuff();
    }

    private void EndBuff()
    {
        if (ended)
            return;
        ended = true;

        if (player != null && onSetSpeed != null)
        {
            try
            {
                player.OnSetMovementSpeed -= onSetSpeed;
            }
            catch
            {
                // player may already be gone
            }
        }

        player = null;
        onSetSpeed = null;
        if (this != null)
            Destroy(this);
    }

}
