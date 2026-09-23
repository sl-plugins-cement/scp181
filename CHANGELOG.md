# Changelog

## 2.0.1

- Allow multiple manually assigned SCP-181 players without replacing existing holders.
- List all holders in `scp181 status`; clarify that `scp181 clear` removes all holders.
- Move the default role introduction HSM Y coordinate from 900 to 1000.

## 2.0.0 — LabAPI migration

- Replace all EXILED plugin, player, event, effect, logging and build dependencies with LabAPI.
- Add the orange native SCP-181 name tag and N-menu badge, with original badge restoration.
- Use completed role changes, native damage handlers and post-pickup duplication events.
- Preserve door/locker cancellation while setting LabAPI's CanOpen for lucky unlocks.
- Cancel dodge countdowns on cleanup and refresh persistent hints in vanilla fallback mode.
- Generate configuration through LabAPI and document installation and native damage keys.

## Previous unreleased fixes

### Fixed

- **SCP-049, SCP-106 and SCP-3114 could not kill SCP-181.** The debuff guard stripped
  `CardiacArrest`, `Corroding` and `PocketCorroding` twice a second, but the game gates SCP-049's
  instant kill on `CardiacArrest` (`Scp049AttackAbility`) and SCP-106's Pocket Dimension capture
  on `Corroding` (`Scp106Attack`), and `PocketCorroding` is the Pocket Dimension decay itself.
  Both attacks restarted on every hit and never completed. SCP-3114's strangulation was refused
  as a status-effect tick, and `Strangled.ServerUpdate` releases the hold the moment a tick is
  refused. Those three effects are no longer stripped, and strangulation takes the reduction
  table and SCP cap but is never dodged or denied. The Pocket Dimension escape still clears
  `Corroding` and `PocketCorroding` itself, as the game's own exit does.
- Last stand now cancels native death after damage modifiers and shields settle, restoring 1 HP.
  Blocked or nonlethal hits no longer consume the charge; scripted instant kills bypass immunity.
- Only completed escape transitions to MTF/Chaos retain SCP-181. Other completed role changes
  clear its identity and buffs; cancelled transitions preserve them.
- Debuff cleanup preserves shared Ensnared/Concussed effects used by other plugins.
- A failed or cancelled Class-D conversion rejects assignment before releasing the incumbent.

## 1.1.0

### Fixed

- **SCP-173 either killed instantly or did nothing.** SCP-173's neck snap arrives as a damage
  handler with `Damage == -1`, the game's instant-kill sentinel (`StandardDamageHandler.KillValue`):
  it zeroes health directly and skips `ProcessDamage`. The old code compared `-1 > 10` for the SCP
  damage cap and `-1 >= health` for the last stand, so neither ever triggered; adding a reduction
  multiplier for `Scp173` turned `-1` into a fraction, which the game then read as
  `Damage <= 0 => no damage at all`. Instant kills from an SCP are now converted into
  `ScpDamageCap` damage before any arithmetic runs. The same fix covers SCP-049's instakill and
  SCP-106's grab. Contrary to the previous note in the README, the `Hurting` event was never
  bypassed — `PlayerStats.DealDamage` raises it before `ApplyDamage`.
- **Passives were lost after escaping.** The damage reduction effects were reapplied in
  `ChangingRole`, which fires *before* the role swap; the game disables every effect on the swap
  (`StatusEffectBase.OnRoleChanged`), so they were wiped immediately. They are now applied from
  `Spawned`, once the new role is live.
- **Pocket Dimension escape never ran.** It matched on a `"PocketDimension"` handler type name
  that does not exist — pocket deaths use `UniversalDamageHandler`/`ScpDamageHandler` with the
  `PocketDecay` translation. Detection now uses `DamageType.PocketDimension`. The escape also
  moved from `Dying` to `Hurting`: cancelling `Dying` left a 0 HP player walking around, because
  the game zeroes health before raising it. The escape now mirrors the game's own successful exit
  (best exit pose, `Disabled` + `Traumatized`, decay effects cleared, teleports reshuffled).
- **Damage-over-time immunity never ran.** It matched `"Bleeding"`/`"Corroding"` in the handler
  type name; bleeding is dealt by `UniversalDamageHandler` and corroding by `ScpDamageHandler`.
  Detection now uses `DamageType.IsStatusEffect()`.
- **Item duplication could destroy the pickup.** The duplicate was granted during
  `PickingUpItem`, before the real pickup is inserted. With 7 items held, the copy took slot 8,
  `ServerAddItem` then returned null for the pickup while `ItemSearchCompletor` destroyed it
  anyway, so the player lost the item. Duplication now requires room for both, and the hint only
  shows when the copy actually landed.
- **Buffs outlived the role.** `Remove` did not disable the effects it applied, so a player
  released via `scp181 set`/`clear` kept an hour-long damage reduction buff. `Clear` also leaked
  last-stand budgets across rounds. All identity state and buffs are now torn down in one place.
- **`scp181` had no permission check.** Any Remote Admin user at any tier could assign the role.
  It now requires `PlayersManagement`.
- **Forced respawn on assignment.** `Assign` always called `ServerSetRole`, which drops the
  inventory and moves the player to a Class-D spawn — even for a player who was already Class-D.
  The role is only changed when it is not already Class-D.
- **Unlock rolls could be spammed.** Every interact press re-rolled the 30% chance, converging on
  a guaranteed open. A failed roll is now held for `UnlockRerollCooldownSeconds`.
- **Null `damage_reduction_table` threw.** The fallback lookup dereferenced the table after the
  null guard had already failed.
- **Last stand ignored shields.** A hit absorbed by AHP or Hume Shield could still burn a charge.
- Stacked debuff-guard coroutines on reassignment; `PocketCorroding` was never cleared; the death
  broadcast had a misplaced `</b>`.

### Changed

- Scripted terminations (Alpha Warhead, pit crushing, SCP-079 recontainment, friendly-fire
  detector, RA `kill`) now bypass every mitigation. Previously the dodge roll applied to them, so
  SCP-181 had a flat 50% chance to walk away from the nuke and admin kills could silently fail.
- Hints moved to the shared HintServiceMeow display provider. Without HSM the plugin now logs once
  and shows nothing instead of falling back to broadcasts and per-second vanilla hints; set
  `hint_display.enable_vanilla_fallback` to `true` to opt back into throttled vanilla hints.
- `ScpDamageCap`, `PocketEscapeChances`, `UnlockRerollCooldownSeconds`,
  `DamageReductionIntensity` and `BodyshotReductionIntensity` are configurable. The reduction
  intensities are documented against the game's actual formula: `DamageReduction` keeps
  `1 - intensity * 0.005` of the damage (50 is −25%, not −50%) and `BodyshotReduction` is clamped
  at intensity 4 (−15%).
- `DamageReductionTable` keys are EXILED `DamageType` names rather than handler type names.
  `Firearm` keeps working as a catch-all for weapons.
- Project converted to an SDK-style `Scp181.csproj` that resolves references from configurable
  install paths instead of hard-coded relative ones that no clone could satisfy.
- Repository documentation, code comments and log messages are English; in-game text stays Chinese.
- Added `.gitignore`, `LICENSE` (MIT) and this changelog.

## 1.0.0

Initial release.
