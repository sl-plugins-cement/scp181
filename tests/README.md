# Local damage and lifecycle probe

`DamageProbe.csproj` is an opt-in LabAPI plugin for a quick-tier dummy walkthrough. It is
excluded from Scp181.dll and must never be shipped to production. Build with `dotnet build
DamageProbe.csproj -c Release` and load Scp181DamageProbe.dll alongside Scp181.dll on an isolated test port.

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

Native contracts: the target server's `PlayerStatsSystem.PlayerStats.DealDamage`,
`PlayerStatsSystem.StandardDamageHandler.ApplyDamage`, and
`PlayerRoles.PlayerRoleManager.ServerSetRole`. LabAPI `ChangedRole` runs after the
completed native swap and effect cleanup; `Death` runs after the spectator swap.

## Offline regression checks

From the repository root in PowerShell:

```powershell
dotnet build -c Release
dotnet build tests/RegressionChecks.csproj -c Release -p:SCP_SL_MANAGED="D:\steam\steamapps\common\SCP Secret Laboratory Dedicated Server\SCPSL_Data\Managed"
& ./tests/bin/Release/net48/RegressionChecks.exe ./bin/Release/net48/Scp181.dll
```

Checks use actual native damage-handler types without starting Unity. They verify pocket,
status-effect, SCP attack, strangulation and firearm classification, plus the compiled plugin's
LabAPI dependency and absence of EXILED references. These are not live gameplay tests.
`RegressionChecks.cs` is excluded from both plugin assemblies.

Build the optional probe against the same server with:

```powershell
dotnet build tests/DamageProbe.csproj -c Release -p:Game="D:\steam\steamapps\common\SCP Secret Laboratory Dedicated Server\SCPSL_Data\Managed"
```

## Identity line and migration client checklist

Use an isolated server with LabAPI only and two clients. Set `copy_chance` and `unlock_chance`
to 1 for deterministic positive cases; set `dodge_chance` to 0 for damage checks.

1. Assign a living Class-D with `scp181 set <id>`. From the other client, look at their name
   look-at panel: it should show the orange `SCP-181` line, and their PlayerBadge badge must be
   unchanged. A late-joining viewer should see the same line. Check names containing spaces with `scp181 set <full nickname>`.
2. Assign A, then B: both must keep SCP-181 identity lines, buffs and role hints. `scp181 status`
   must list both. Spend A's survival charge and verify B's charge is unaffected. Reassign A:
   B must remain SCP-181 and A's charges must not refill. Kill or change A's role: only A
   loses the identity. `scp181 clear` must remove all remaining SCP-181s.
   With a fresh config, the role introduction must use HSM Y=1000.
3. Begin with a PlayerBadge or RA badge, assign SCP-181, then run `scp181 clear`. The badge
   must never change; the original custom info and its visibility must return.
4. Repeat cleanup through death, role reassignment, round end,
   disconnect/reconnect and plugin disable. No SCP-181 line or reduction effect should remain.
   `damageprobe state <id>` prints custom info, badge text and info-area flags for server inspection.
5. Escape to MTF or Chaos: the line stays orange while the HUD card changes team color. A
   cancelled role change must retain the current identity and line.
6. With 6 inventory items, a pickup adds the original and one copy; with 7, it adds only
   the original; with 8, it cannot add anything. Cancelled pickups grant no copy.
7. Unlocked keycard doors/lockers can open without a keycard. SCP-079 gates/armory and locked
   doors cannot. Another plugin cancelling the event must still block interaction. Set chance
   to 0 and verify the failed-roll cooldown is respected.
8. Check death broadcast/subtitles and SCP damage/last-stand/pocket cases above. With no HSM,
   identity line/passives must work; enabling vanilla fallback must keep the role hint refreshed.

## Verification record — 2026-09-23

- Release build: passed, zero warnings/errors, against local LabAPI 1.1.7 assemblies.
- Offline regression executable: 22 checks passed.
- Client display, network replication and live gameplay checklist: not run.
