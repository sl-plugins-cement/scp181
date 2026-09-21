# Changelog

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
