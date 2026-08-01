using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks active Friend field deployables in spawn order.
/// Baseline: only one at a time — arming a new one quietly destroys the oldest
/// unless max concurrent is raised by upgrades.
/// Also provides drone formation slots so multi-drones don't stack.
/// </summary>
public static class FriendDeployTracker
{
    /// <summary>Baseline max concurrent deploys. Multi-deploy upgrades raise this.</summary>
    public static int MaxConcurrentDeploys { get; set; } = 1;

    private static readonly List<FriendDeployable> Active = new List<FriendDeployable>();

    public static int ActiveCount
    {
        get
        {
            PruneNulls();
            return Active.Count;
        }
    }

    public static bool HasActive => ActiveCount > 0;

    public static void Register(FriendDeployable deployable)
    {
        if (deployable == null)
            return;

        PruneNulls();

        if (Active.Contains(deployable))
            return;

        int max = Mathf.Max(1, MaxConcurrentDeploys);
        while (Active.Count >= max)
        {
            FriendDeployable oldest = Active[0];
            Active.RemoveAt(0);
            if (oldest != null)
            {
                FriendinaBoxPlugin.Logger?.LogInfo(
                    $"[FriendinaBox] Replacing oldest deployable at {oldest.transform.position} (max concurrent={max}).");
                oldest.ForceDespawn();
            }
        }

        Active.Add(deployable);
    }

    public static void Unregister(FriendDeployable deployable)
    {
        if (deployable == null)
            return;
        Active.Remove(deployable);
    }

    public static void Clear()
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            FriendDeployable d = Active[i];
            if (d != null)
                d.ForceDespawn();
        }
        Active.Clear();
    }

    public static void ExtendAllDurations(float seconds)
    {
        if (seconds <= 0f)
            return;
        PruneNulls();
        for (int i = 0; i < Active.Count; i++)
            Active[i]?.ExtendDuration(seconds);
    }

    public static FriendDeployable FindNearest(Vector3 point, float maxDistance)
    {
        PruneNulls();
        FriendDeployable best = null;
        float bestSq = maxDistance * maxDistance;
        for (int i = 0; i < Active.Count; i++)
        {
            FriendDeployable d = Active[i];
            if (d == null)
                continue;
            float sq = (d.transform.position - point).sqrMagnitude;
            if (sq <= bestSq)
            {
                bestSq = sq;
                best = d;
            }
        }
        return best;
    }

    /// <summary>
    /// Among active drones, return this drone's formation index and total drone count.
    /// Index is stable by spawn order (list order).
    /// </summary>
    public static void GetDroneFormationSlot(FriendDeployable self, out int index, out int count)
    {
        index = 0;
        count = 0;
        if (self == null)
            return;

        PruneNulls();
        int foundIndex = 0;
        for (int i = 0; i < Active.Count; i++)
        {
            FriendDeployable d = Active[i];
            if (d == null)
                continue;
            if ((d.Mode & FriendDeployMode.Drone) == 0)
                continue;

            if (d == self)
                foundIndex = count;
            count++;
        }

        index = foundIndex;
        if (count < 1)
            count = 1;
    }

    /// <summary>
    /// Collect active deployables within radius of origin (for Swarm FriendFire / Hive Kin).
    /// Includes mine, turret, mortar, and drone forms.
    /// </summary>
    public static void GetAlliesInRadius(Vector3 origin, float radius, List<FriendDeployable> buffer)
    {
        buffer.Clear();
        if (radius <= 0f)
            return;

        PruneNulls();
        float r2 = radius * radius;
        for (int i = 0; i < Active.Count; i++)
        {
            FriendDeployable d = Active[i];
            if (d == null)
                continue;
            if ((d.transform.position - origin).sqrMagnitude <= r2)
                buffer.Add(d);
        }
    }

    /// <summary>Backward-compatible alias — all deploy forms count as Hive Kin allies.</summary>
    public static void GetDronesInRadius(Vector3 origin, float radius, List<FriendDeployable> buffer)
        => GetAlliesInRadius(origin, radius, buffer);


    /// <summary>
    /// Horizontal offset in player-local space for a drone formation slot.
    /// Spreads drones in a ring so Squad Drop drones don't occupy the same point.
    /// </summary>
    public static Vector3 GetDroneFormationOffset(int index, int count, float radius)

    {
        count = Mathf.Max(1, count);
        radius = Mathf.Max(0.5f, radius);

        if (count == 1)
        {
            // Single drone: slight back-right shoulder.
            return new Vector3(0.45f, 0f, -radius);
        }

        // Evenly spaced around the player, starting from behind.
        float angle = (index / (float)count) * Mathf.PI * 2f + Mathf.PI; // start at back
        float x = Mathf.Sin(angle) * radius;
        float z = Mathf.Cos(angle) * radius;
        return new Vector3(x, 0f, z);
    }

    private static void PruneNulls()
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            if (Active[i] == null)
                Active.RemoveAt(i);
        }
    }
}
