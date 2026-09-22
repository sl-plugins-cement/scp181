# Local damage and lifecycle probe

`DamageProbe.csproj` is an opt-in LabAPI plugin for a quick-tier dummy walkthrough. It is
excluded from Scp181.dll and must never be shipped to production. Build with `dotnet build
DamageProbe.csproj -c Release` and load only Scp181DamageProbe.dll on the isolated test port.

Use native `dummy spawn`, `forceclass`, `scp181 set`, `effect`, and `scp181 status` commands.
`damageprobe list` prints player ids, roles and HP. `damageprobe state <id>` observes role, HP,
room and the SCP attack effects. `damageprobe sethp <id> <hp>` arranges a health fixture.
`damageprobe damage <id> <amount>` exercises native PlayerStats.DealDamage with fall damage; it
does not demonstrate a physical fall. `damageprobe place <scp id> <target id> <dx> <dz>` stands
an SCP dummy beside the target facing it, and `damageprobe attack106 <scp id> <target id>` /
`damageprobe attack049 <scp id> <target id>` feed the native attack subroutine the same command
payload a client sends, so range, line of sight, cooldown and effect gating run natively.
`damageprobe escape <id>` requests a native role change with Escaped reason; it does not
establish escape-zone traversal. `protect` and `cancelrole` take `<id> true|false` and toggle
local fixture event cancellation. Always reset these flags after the case.

Set dodge_chance to 0 for deterministic damage checks. A raw 100 hit from 100 HP should remain
nonlethal after reductions. Block a 1000 hit, unblock it, then repeat: the blocked hit preserves
HP and the first applied lethal hit leaves 1 HP. After immunity expires, another lethal hit
must cause death. Apply Ensnared and wait beyond 0.5 seconds to verify it survives cleanup.
Check escape retention, cancelled role-change retention, successful role-change removal,
same-Class-D reassignment removal, and failed conversion preserving the incumbent.

SCP capture path: two `attack106` hits about two seconds apart must first enable Corroding (still
enabled two seconds later) and then move the target to the Pocket room with PocketCorroding; decay
ticks must land, and with HP below the next tick the target must exit alive and Traumatized. A hit
on the Traumatized target must cost capped damage, not the instant kill. For SCP-049, the first
`attack049` must enable CardiacArrest, which must survive the debuff guard and tick; the second
hit must cost capped damage. `effect Bleeding 1 30 <id>` must still be stripped within a second.

Native contracts: `.references/Decompiled/DedicatedServer/Assembly-CSharp/PlayerStatsSystem/PlayerStats.cs`
(DealDamage and native Dying), `PlayerStatsSystem/StandardDamageHandler.cs` (final damage), and
`PlayerRoles/RoleChangeReason.cs` in the metarepo. EXILED Spawned carries the completed role's
spawn reason; see `.references/Reference Plugins/EXILED/EXILED/Exiled.Events/EventArgs/Player/SpawnedEventArgs.cs`.
