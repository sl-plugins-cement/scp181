# SCP-181 (Lucky Charm)

An SCP:SL role plugin for LabAPI. At round start one player is quietly turned into **SCP-181**, a
Class-D with absurd survivability — heavy damage mitigation, item duplication and lucky door
unlocks. SCP-181 has no objective beyond staying alive inside the facility.

- Framework: LabAPI 1.1.7+ / .NET Framework 4.8; no EXILED dependencies
- HUD: HintServiceMeow through the shared hint display provider
- Source material: [SCP-181 - Lucky Charm](https://scp-wiki-cn.wikidot.mer.run/scp-181)

In-game text is Chinese by design; everything else in this repository is English.

## Features

### Selection

- At round start, when more than `MinPlayers` players are alive, one is picked: Class-D first,
  otherwise Tutorial/Scientist. SCPs, MTF and Chaos are never eligible.
- `scp181 set <name/id>` assigns the role manually. The command requires the RA
  **PlayersManagement** permission. Assigning another player does not remove existing SCP-181s.
- Multiple SCP-181 players can coexist, each with independent survival charges, effects and badges.
  `scp181 status` lists them all; `scp181 clear` removes all of them.
  Reassigning an existing SCP-181 refreshes their presentation without refilling charges.
- A player who is not already Class-D is respawned as Class-D. A Class-D target keeps their
  position and inventory.

When ReinforcementsSystem is installed, automatic selection waits for its initial manager and
spy ownership passes to finish (up to 60 seconds). Both automatic selection and `scp181 set`
exclude players it tracks as Facility Manager, GOC spy, or reinforcement members. Rejected manual
assignments preserve existing SCP-181 players. Install a ReinforcementsSystem build exposing
`IsTrackedRole(LabApi.Features.Wrappers.Player)` and `IsInitialRoleSelectionPending`; a missing or
failing API blocks assignment with a server error. Without ReinforcementsSystem, selection works
standalone. This check does not prevent another plugin assigning a new role to SCP-181 later.

### Orange name tag and player-list badge

While assigned, the player has the native orange `SCP-181` badge, visible above their name
and beside their name in the **N** server/player list. It stays orange after escape; HUD role
card colors can still change with the team. The original badge text, color and visibility are
restored on removal, death, reassignment, round end or plugin disable, unless another plugin
has replaced the badge. This changes display fields only and does not grant RA permissions.
Native name-tag visibility rules (distance, line of sight, etc.) still apply.

### Passives

1. **Item duplication** — picking an item up has a `CopyChance` chance of yielding a second copy.
   The copy is only granted when the inventory has room for both, so a pickup can never be
   swallowed by a full inventory.
2. **Dodge** — any attack that reaches SCP-181 has a `DodgeChance` chance of being negated
   outright. The attacker sees a countdown notice.
3. **Damage reduction table** — `DamageReductionTable` maps a damage source to the fraction of
   damage that still lands. Keys are native damage aliases or firearm `ItemType` names; a specific weapon type wins over
   the generic `Firearm` key, and the attacker's role name is tried last.
4. **SCP damage cap** — no single hit from an SCP exceeds `ScpDamageCap`. This includes the
   instant-kill abilities (SCP-173's neck snap, SCP-049's instakill, SCP-106's grab), which are
   converted into capped damage instead of a guaranteed death.
5. **Status effect immunity** — damage-over-time from bleeding, poison, hypothermia and similar
   status effects never lands, and those debuffs are stripped twice a second. SCP attack states
   are left alone: SCP-049's cardiac arrest, SCP-106's corrosion and the Pocket Dimension run
   their course, and their damage takes the SCP mitigation above. SCP-3114's strangulation is
   reduced and capped but never dodged, because the hold breaks as soon as a tick is refused.
   Shared movement effects (`Ensnared` and `Concussed`) are preserved.
6. **Last stand** — a lethal hit is survived on 1 HP, `SurviveChances` times per assignment,
   followed by `SurviveImmunitySeconds` of full immunity. Lethality is evaluated after native damage reduction and shields.
7. **Pocket Dimension escape** — the first lethal Pocket Dimension outcome per assignment is
   converted into a successful exit instead of a death, with the same aftermath the game applies
   to a normal exit.
8. **Lucky unlocks** — keycard doors and SCP locker chambers open with `UnlockChance`.
   SCP-079's own doors and anything SCP-079 has locked are excluded. A failed roll is held for
   `UnlockRerollCooldownSeconds` so spamming the interact key cannot force an open.
9. **Escape keeps everything** — escaping as MTF or Chaos retains every passive; only the role
   card color changes (MTF blue `#4DA6FF`, Chaos dark green `#1E6B3A`). Other completed
   role changes remove the identity and passives.

Scripted terminations — the Alpha Warhead, pit crushing, SCP-079 recontainment, the friendly-fire
detector and the RA `kill` command — bypass every mitigation above on purpose. SCP-181 is a
survivability role, not an exemption from the round's own kill switches.

### Death broadcast

- CASSIE announces `SCP-181 HAS BEEN CONTAINED SUCCESSFULLY`.
- A server-wide broadcast names the killer.

## Configuration

LabAPI generates `LabAPI/configs/<port>/Scp181/config.yml` on first load.
Copy option values from an older config into this file, without its outer `scp181:` section.
Weapon-specific keys now use native names such as `GunCOM15`, `GunE11SR` and `GunAK`.
Common damage keys include `Firearm`, `Scp173`, `Scp049`, `Scp0492`, `Scp106`, `Scp096`,
`Scp3114`, `Strangled`, `PocketDimension`, `Fall`, `Explosion`, `Tesla`, `Poison` and `Bleeding`.
Other native handlers use their class name without the `DamageHandler` suffix.

| Option | Default | Meaning |
|---|---|---|
| `min_players` | `5` | Alive player count that must be exceeded for a round-start pick |
| `copy_chance` | `0.1` | Item duplication chance |
| `dodge_chance` | `0.5` | Chance an attack is negated |
| `damage_reduction_table` | `Firearm: 0.1` | Damage source → fraction of damage kept |
| `scp_damage_cap` | `10` | Cap per SCP hit, and the value SCP instant-kills become |
| `unlock_chance` | `0.3` | Keycard door / SCP locker unlock chance |
| `unlock_reroll_cooldown_seconds` | `8` | Hold time on a failed unlock roll |
| `survive_chances` | `1` | Last-stand charges |
| `survive_immunity_seconds` | `1.5` | Immunity window after a last stand |
| `pocket_escape_chances` | `1` | Guaranteed Pocket Dimension escapes |
| `damage_reduction_intensity` | `50` | `DamageReduction` intensity; the game keeps `1 - intensity * 0.005` of the damage, so 50 is −25% and 200 is immune |
| `bodyshot_reduction_intensity` | `4` | `BodyshotReduction` intensity; the game clamps this at 4 (−15%) |
| `scp_color` / `ntf_color` / `chaos_color` | orange / blue / dark green | Role card colors |
| `role_intro_y` / `dodge_msg_y` / `survive_msg_y` | `1000` / `800` / `780` | HSM Y coordinates |
| `cassie_transmission` / `death_announce` | … | Death broadcast text |
| `hint_display` | see below | Hint display provider settings |

### Hints

The default `role_intro_y` is now `1000`. Existing configs retain their saved value; change
`role_intro_y: 900` to `role_intro_y: 1000` to move the introduction in an existing installation.

Hints go through the shared HintServiceMeow provider (`Services/`), which uses stable IDs and
groups so this plugin's hints compose with other plugins instead of fighting them. When HSM is
not loaded the plugin logs once and displays nothing. Set `hint_display.enable_vanilla_fallback`
to `true` to opt into throttled vanilla hints instead.

## Building

```
dotnet build -c Release
```

The project uses a sibling `_buildrefs` directory when available; otherwise it checks the
standard Steam dedicated-server install. Always build against the target server's assemblies.
To override the location in PowerShell:

```powershell
dotnet build -c Release -p:SCP_SL_MANAGED="D:\steam\steamapps\common\SCP Secret Laboratory Dedicated Server\SCPSL_Data\Managed"
```

Install `bin/Release/net48/Scp181.dll` in the server's
`%APPDATA%/SCP Secret Laboratory/LabAPI/plugins/global/` (or `plugins/<port>/`) and restart.
Remove the previous SCP-181 plugin from its old loader directory before using this build.
Only the plugin DLL is needed; do not copy the game reference assemblies.
HintServiceMeow is optional and must be a LabAPI-compatible build; the badge and passives
work without it. HUD hints follow the fallback option described above.

See `tests/README.md` for offline regression checks and the in-game checklist.
