using System;

/// <summary>
/// Field form of a Friend deployable. Flags can combine for drone hybrids
/// (drone + turret gun, drone + mortar lobs).
/// </summary>
[Flags]
public enum FriendDeployMode : byte
{
    /// <summary>Default proximity mine (no converter upgrades).</summary>
    None = 0,

    /// <summary>Stationary auto-turret.</summary>
    Turret = 1,

    /// <summary>Stationary mortar — periodic lobbed AoE.</summary>
    Mortar = 2,

    /// <summary>Hover near player; suicide by default, inherits turret/mortar if set.</summary>
    Drone = 4,
}

public static class FriendDeployModeUtil
{
    /// <summary>
    /// Resolve display / primary behaviour kind.
    /// Priority: Drone > Mortar > Turret > Mine.
    /// </summary>
    public static FriendDeployMode GetPrimary(FriendDeployMode flags)
    {
        if ((flags & FriendDeployMode.Drone) != 0)
            return FriendDeployMode.Drone;
        if ((flags & FriendDeployMode.Mortar) != 0)
            return FriendDeployMode.Mortar;
        if ((flags & FriendDeployMode.Turret) != 0)
            return FriendDeployMode.Turret;
        return FriendDeployMode.None; // mine
    }

    public static string GetLabel(FriendDeployMode flags)
    {
        FriendDeployMode primary = GetPrimary(flags);
        switch (primary)
        {
            case FriendDeployMode.Drone:
                if ((flags & FriendDeployMode.Turret) != 0 && (flags & FriendDeployMode.Mortar) != 0)
                    return "Drone+Turret+Mortar";
                if ((flags & FriendDeployMode.Turret) != 0)
                    return "Drone+Turret";
                if ((flags & FriendDeployMode.Mortar) != 0)
                    return "Drone+Mortar";
                return "Drone";
            case FriendDeployMode.Mortar:
                return "Mortar";
            case FriendDeployMode.Turret:
                return "Turret";
            default:
                return "Mine";
        }
    }

    public static UnityEngine.Color GetDebugColor(FriendDeployMode flags)
    {
        switch (GetPrimary(flags))
        {
            case FriendDeployMode.Drone:
                return new UnityEngine.Color(0.2f, 0.85f, 1f); // cyan
            case FriendDeployMode.Mortar:
                return new UnityEngine.Color(1f, 0.55f, 0.1f); // orange
            case FriendDeployMode.Turret:
                return new UnityEngine.Color(0.3f, 1f, 0.35f); // green
            default:
                return UnityEngine.Color.magenta;
        }
    }
}
