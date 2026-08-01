using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Temporary mark on enemies hit by Friend — outgoing player damage is amplified.
/// </summary>
public sealed class FriendMarkedTarget : MonoBehaviour
{
    private static readonly List<FriendMarkedTarget> Active = new List<FriendMarkedTarget>();

    private ITarget target;
    private float bonus;
    private float remaining;

    public static void Apply(ITarget enemy, float damageBonus, float duration)
    {
        if (enemy == null || !enemy.IsAlive || damageBonus <= 0f || duration <= 0f)
            return;

        Component host = enemy as Component;
        if (host == null)
            return;

        FriendMarkedTarget existing = host.GetComponent<FriendMarkedTarget>();
        if (existing != null)
        {
            existing.bonus = Mathf.Max(existing.bonus, damageBonus);
            existing.remaining = Mathf.Max(existing.remaining, duration);
            return;
        }

        FriendMarkedTarget mark = host.gameObject.AddComponent<FriendMarkedTarget>();
        mark.target = enemy;
        mark.bonus = damageBonus;
        mark.remaining = duration;
        Active.Add(mark);
    }

    public static bool TryGetBonus(ITarget enemy, out float bonus)
    {
        bonus = 0f;
        if (enemy == null)
            return false;

        for (int i = Active.Count - 1; i >= 0; i--)
        {
            FriendMarkedTarget m = Active[i];
            if (m == null)
            {
                Active.RemoveAt(i);
                continue;
            }

            if (m.target == enemy && m.remaining > 0f)
            {
                bonus = m.bonus;
                return true;
            }
        }

        return false;
    }

    private void Update()
    {
        remaining -= Time.deltaTime;
        if (remaining <= 0f || target == null || !target.IsAlive)
            Destroy(this);
    }

    private void OnDestroy()
    {
        Active.Remove(this);
    }
}
