# Changelog

## 1.0.0

- Friend in a Box custom grenade (runtime clone of Incendiary)
- Lands as a proximity mine (default 20s duration, detect = 75% explosion radius)

- While equipped, unlocks Ouroboros `UpgradeFlags.Coop` upgrades in solo
- Auto-unlock into gear select; equip/spawn remap for NGO catalog clones
- Magenta debug cube on armed mines (shared helper for future drone/turret)
- First upgrades: Wider Net, Long Watch, Quick Deploy, Lingering Gift, Parting Boost
- Baseline mine detect radius = 75% of explosion radius
- Only one mine at a time; new deploy quietly replaces the oldest
- Mode scaffolding: Mine (magenta) / Turret (green) / Mortar (orange) / Drone (cyan)
- Mode converters: Sentry Kit, Lobber Kit, Buddy Protocol (drone hybrids supported)
- Turret fires real RailBullet (Cycler); mortar fires real MortarBullet (enemy mortar)
- Boot fix: register gear before PlayerData.OnAwake; harden upgrades; guard vanilla OnAwake RemoveAt bug
- Guard GearSlot.Update NRE (new-upgrades badge) so gear menu stays stable
- Registration scans AllGear instead of FindGear during boot; post-OnAwake GearData + upgrades
- Combat upgrades: Squad Drop, Sympathetic Link (blue overheal), Field Recharge, Overtime, Painted Targets, Scuttle Charge, Reactive Shell
- QoL: turret/mortar engage range floor 50; drone hovers above player (not inside)
- Squad Drop drones use formation ring offsets so they don't occupy the same space
- Calibrated Link: turret/mortar blend primary weapon stats (incl. bullets/shot); Designated Target: focus last player-hit enemy
- Hive Kin: all Friend deployables (mine/turret/mortar/drone) count as Swarm Breeding Season allies
- Squad Drop: one throw multi-spawns deployables in a ring spread (no overlap)
- Fix first-time equip: stop OnClose double-spawn; deferred stamp after SpawnGear RPC
- Post-stamp ApplyUpgrades so Friend upgrades/HUD bind on first close (not second open)
- Project cleanup: rename CSPROJECT → FriendinaBox.csproj; remove Example* template files




















