# Local damage and lifecycle probe

`DamageProbe.csproj` is an opt-in LabAPI plugin for a quick-tier dummy walkthrough. It is
excluded from Scp181.dll and must never be shipped to production. Build with `dotnet build
DamageProbe.csproj -c Release` and load only Scp181DamageProbe.dll on the isolated test port.

Use native `dummy spawn`, `forceclass`, `scp181 set`, `effect`, and `scp181 status` commands.
`damageprobe state <id>` observes role, HP and effects. `damageprobe damage <id> <amount>`
exercises native PlayerStats.DealDamage with fall damage; it does not demonstrate a physical fall.
`damageprobe escape <id>` requests a native role change with Escaped reason; it does not
establish escape-zone traversal. `protect` and `cancelrole` take `<id> true|false` and toggle
local fixture event cancellation. Always reset these flags after the case.

Set dodge_chance to 0 for deterministic damage checks. A raw 100 hit from 100 HP should remain
nonlethal after reductions. Block a 1000 hit, unblock it, then repeat: the blocked hit preserves
HP and the first applied lethal hit leaves 1 HP. After immunity expires, another lethal hit
must cause death. Apply Ensnared and wait beyond 0.5 seconds to verify it survives cleanup.
Check escape retention, cancelled role-change retention, successful role-change removal,
same-Class-D reassignment removal, and failed conversion preserving the incumbent.

Native contracts: `.references/Decompiled/DedicatedServer/Assembly-CSharp/PlayerStatsSystem/PlayerStats.cs`
(DealDamage and native Dying), `PlayerStatsSystem/StandardDamageHandler.cs` (final damage), and
`PlayerRoles/RoleChangeReason.cs` in the metarepo. EXILED Spawned carries the completed role's
spawn reason; see `.references/Reference Plugins/EXILED/EXILED/Exiled.Events/EventArgs/Player/SpawnedEventArgs.cs`.
