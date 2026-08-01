# Friend in a Box

A BepInEx mod for **Mycopunk** that adds **Friend in a Box** — a deployable grenade that:

- Lands as a **proximity mine** (Incendiary stats, ~20s duration)
- Detonates when enemies enter its detect radius
- While **equipped**, enables multiplayer-only (`UpgradeFlags.Coop`) upgrades in **Ouroboros** solo runs
- Supports mine / turret / mortar / drone modes and 18 upgrades

## Features

| Feature | Detail |
|---|---|
| Gear | `friend_in_a_box` (id `92100`), auto-unlocked |
| Baseline | Cloned Incendiary Grenade stats / visuals |
| Deploy | On fuse/detonate → arm field entity instead of instant boom |
| Duration | 20s default |
| Detect | Defaults to 75% of explosion radius |
| Concurrent | 1 by default; **Squad Drop** multi-spawns in a spread |
| Modes | Mine / Turret (RailBullet) / Mortar (MortarBullet) / Drone |
| Coop unlock | Equipped Friend enables multiplayer-only Ouroboros upgrades in solo |
| Upgrades | 18 total — mine path, mode kits, combat, Hive Kin (Swarm Breeding Season) |

## Install

```
<Mycopunk>/BepInEx/plugins/FriendinaBox.dll
```

Build:

```bash
dotnet build FriendinaBox.sln --configuration Release
```

Output: `bin/Release/netstandard2.1/FriendinaBox.dll`

## Architecture

| Type | Role |
|---|---|
| `FriendinaBoxPlugin` | BepInEx entry, registration, Harmony |
| `GrenadeRegistration` | Clone Incendiary → AllGear + GearInfo |
| `FriendinaBoxBehaviour` | Deploy data host on gear |
| `FriendGrenadeHooks` | Detonate prefix → spawn deployable(s) |
| `FriendDeployable` | Mine / turret / mortar / drone field entity |
| `FriendDeployTracker` | Concurrent deploys, formation, Hive Kin allies |
| `CoopUnlockHook` | Solo Coop upgrade unlock while equipped |
| `SpawnGearHooks` | NGO equip remap + identity stamp + ApplyUpgrades |
| `SwarmFriendFireHooks` | Hive Kin ↔ Swarm Breeding Season |
| `FriendCombatHooks` | Lifesteal, marks, kill charge/duration, scuttle |

## Test checklist

1. Log shows gear registration + `Registered 18 upgrades`
2. Gear select lists **Friend in a Box**; equip works on first menu close
3. Throw → lands → sits (no instant boom)
4. Enemy enters radius → explodes
5. Duration ends → despawn / expire effects
6. Solo Ouroboros with Friend **equipped** → Coop upgrades can appear
7. Squad Drop → multi-spawn ring; Hive Kin + Swarm Breeding Season → pellets from deploys

## License

MIT — see `LICENSE`
