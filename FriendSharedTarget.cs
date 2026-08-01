using UnityEngine;

/// <summary>
/// Tracks the enemy the local player last damaged so Friend deployables can focus it
/// (Designated Target upgrade).
/// </summary>
public static class FriendSharedTarget
{
    private static ITarget current;
    private static float expireTime;

    public static void Set(ITarget target, float duration)
    {
        if (target == null || !target.IsAlive || duration <= 0f)
            return;
        if (target.IsPlayer())
            return;

        current = target;
        expireTime = Time.time + duration;
    }

    public static void Clear()
    {
        current = null;
        expireTime = 0f;
    }

    /// <summary>
    /// Returns the designated target if still valid and (optionally) within range of origin.
    /// </summary>
    public static bool TryGet(Vector3 origin, float maxRange, out ITarget target)
    {
        target = null;
        if (current == null)
            return false;

        if (Time.time > expireTime || !current.IsAlive)
        {
            Clear();
            return false;
        }

        if (maxRange > 0f)
        {
            float sq = (current.GetHealthbarPosition() - origin).sqrMagnitude;
            if (sq > maxRange * maxRange)
                return false;
        }

        target = current;
        return true;
    }

    public static bool IsActive =>
        current != null && current.IsAlive && Time.time <= expireTime;
}
