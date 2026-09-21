# SCP-181 (Lucky Charm)

An SCP:SL role plugin for EXILED. At round start one player is quietly turned into **SCP-181**, a
Class-D with absurd survivability — heavy damage mitigation, item duplication and lucky door
unlocks. SCP-181 has no objective beyond staying alive inside the facility.

- Framework: EXILED 9.14+ (LabAPI underneath)
- HUD: HintServiceMeow through the shared hint display provider
- Source material: [SCP-181 - Lucky Charm](https://scp-wiki-cn.wikidot.mer.run/scp-181)

In-game text is Chinese by design; everything else in this repository is English.

## Features

### Selection

- At round start, when more than `MinPlayers` players are alive, one is picked: Class-D first,
  otherwise Tutorial/Scientist. SCPs, MTF and Chaos are never eligible.
- `scp181 set <name/id>` assigns the role manually. The command requires the RA
  **PlayersManagement** permission.
- A player who is not already Class-D is respawned as Class-D. A Class-D target keeps their
  position and inventory.

### Passives

1. **Item duplication** — picking an item up has a `CopyChance` chance of yielding a second copy.
   The copy is only granted when the inventory has room for both, so a pickup can never be
   swallowed by a full inventory.
2. **Dodge** — any attack that reaches SCP-181 has a `DodgeChance` chance of being negated
   outright. The attacker sees a countdown notice.
3. **Damage reduction table** — `DamageReductionTable` maps a damage source to the fraction of
   damage that still lands. Keys are EXILED `DamageType` names; a specific weapon type wins over
   the generic `Firearm` key, and the attacker's role name is tried last.
4. **SCP damage cap** — no single hit from an SCP exceeds `ScpDamageCap`. This includes the
   instant-kill abilities (SCP-173's neck snap, SCP-049's instakill, SCP-106's grab), which are
   converted into capped damage instead of a guaranteed death.
5. **Status effect immunity** — damage-over-time from bleeding, poison, hypothermia and similar
   status effects never lands, and the effects themselves are stripped twice a second.
6. **Last stand** — a lethal hit is survived on 1 HP, `SurviveChances` times per assignment,
   followed by `SurviveImmunitySeconds` of full immunity. Shields count toward "lethal".
7. **Pocket Dimension escape** — the first lethal Pocket Dimension outcome per assignment is
   converted into a successful exit instead of a death, with the same aftermath the game applies
   to a normal exit.
8. **Lucky unlocks** — keycard doors and SCP locker chambers open with `UnlockChance`.
   SCP-079's own doors and anything SCP-079 has locked are excluded. A failed roll is held for
   `UnlockRerollCooldownSeconds` so spamming the interact key cannot force an open.
9. **Escape keeps everything** — escaping as MTF or Chaos retains every passive; only the role
   card color changes (MTF blue `#4DA6FF`, Chaos dark green `#1E6B3A`).

Scripted terminations — the Alpha Warhead, pit crushing, SCP-079 recontainment, the friendly-fire
detector and the RA `kill` command — bypass every mitigation above on purpose. SCP-181 is a
survivability role, not an exemption from the round's own kill switches.

### Death broadcast

- CASSIE announces `SCP-181 HAS BEEN CONTAINED SUCCESSFULLY`.
- A server-wide broadcast names the killer.

## Configuration

Everything lives under the `scp181` section of the EXILED config file.

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
| `role_intro_y` / `dodge_msg_y` / `survive_msg_y` | `900` / `800` / `780` | HSM Y coordinates |
| `cassie_transmission` / `death_announce` | … | Death broadcast text |
| `hint_display` | see below | Hint display provider settings |

### Hints

Hints go through the shared HintServiceMeow provider (`Services/`), which uses stable IDs and
groups so this plugin's hints compose with other plugins instead of fighting them. When HSM is
not loaded the plugin logs once and displays nothing. Set `hint_display.enable_vanilla_fallback`
to `true` to opt into throttled vanilla hints instead.

## Building

```
dotnet build -c Release
```

The project resolves the game and EXILED assemblies from the default install paths. Override them
when your install lives elsewhere:

```
dotnet build -c Release \
  -p:SCP_SL_MANAGED="D:\srv\SCPSL_Data\Managed" \
  -p:EXILED_REFS="%APPDATA%\SCP Secret Laboratory\LabAPI\dependencies\global" \
  -p:EXILED_PLUGINS="%APPDATA%\EXILED\Plugins"
```

Drop `bin/Release/net48/Scp181.dll` into the server's EXILED `Plugins` directory and restart —
net48 cannot hot-reload. Configuration is generated on first load under `EXILED/Configs`.
