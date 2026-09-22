using System;
using System.Collections.Generic;
using System.Linq;
using Exiled.API.Enums;
using Exiled.API.Extensions;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Server;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.PlayableScps.Scp106;
using Scp181.Visuals;
using Scp181.Services;
using UnityEngine;
using PlayerHandler = Exiled.Events.Handlers.Player;
using ServerHandler = Exiled.Events.Handlers.Server;

namespace Scp181.Events
{
    public class Scp181Events
    {
        private MEC.CoroutineHandle _selection;
        /// <summary>Inventory slots the game allows. A copy may only be granted below this.</summary>
        private const int InventoryCapacity = 8;

        private static readonly HashSet<DoorType> Scp079Doors = new HashSet<DoorType>
        {
            DoorType.Scp079First,
            DoorType.Scp079Second,
            DoorType.Scp079Armory,
        };

        private static readonly HashSet<RoleTypeId> NtfRoles = new HashSet<RoleTypeId>
        {
            RoleTypeId.NtfPrivate, RoleTypeId.NtfSergeant,
            RoleTypeId.NtfCaptain, RoleTypeId.NtfSpecialist,
        };

        private static readonly HashSet<RoleTypeId> ChaosRoles = new HashSet<RoleTypeId>
        {
            RoleTypeId.ChaosConscript, RoleTypeId.ChaosRifleman,
            RoleTypeId.ChaosMarauder, RoleTypeId.ChaosRepressor,
        };

        /// <summary>Last failed unlock roll per (player, interactable), so spamming cannot re-roll it.</summary>
        private static readonly Dictionary<string, float> UnlockCooldowns = new Dictionary<string, float>();

        private static Config Config => MainClass.Instance!.Config;

        public void RegisterEvents()
        {
            ServerHandler.RoundStarted += OnRoundStarted;
            ServerHandler.RoundEnded += OnRoundEnded;
            ServerHandler.RestartingRound += OnRestartingRound;
            LabApi.Events.Handlers.PlayerEvents.Dying += OnDying;
            PlayerHandler.Spawned += OnSpawned;
            PlayerHandler.Hurting += OnHurting;
            PlayerHandler.Died += OnDied;
            PlayerHandler.Left += OnLeft;
            PlayerHandler.PickingUpItem += OnPickingUpItem;
            PlayerHandler.InteractingDoor += OnInteractingDoor;
            PlayerHandler.InteractingLocker += OnInteractingLocker;
        }

        public void UnregisterEvents()
        {
            CancelSelection();
            ServerHandler.RoundStarted -= OnRoundStarted;
            ServerHandler.RoundEnded -= OnRoundEnded;
            ServerHandler.RestartingRound -= OnRestartingRound;
            LabApi.Events.Handlers.PlayerEvents.Dying -= OnDying;
            PlayerHandler.Spawned -= OnSpawned;
            PlayerHandler.Hurting -= OnHurting;
            PlayerHandler.Died -= OnDied;
            PlayerHandler.Left -= OnLeft;
            PlayerHandler.PickingUpItem -= OnPickingUpItem;
            PlayerHandler.InteractingDoor -= OnInteractingDoor;
            PlayerHandler.InteractingLocker -= OnInteractingLocker;
        }

        // ================= Round lifecycle =================

        private void OnRoundStarted()
        {
            CancelSelection();
            ResetFlags();
            _selection = MEC.Timing.RunCoroutine(SelectAfterInitialRoles());
        }

        private IEnumerator<float> SelectAfterInitialRoles()
        {
            yield return MEC.Timing.WaitForSeconds(1f);
            float deadline = Time.realtimeSinceStartup + 60f;
            while (ReinforcementRoleBridge.IsSelectionPending)
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    Log.Warn("[Scp181] Reinforcements role selection was not ready within 60 seconds; skipping automatic assignment.");
                    yield break;
                }
                yield return MEC.Timing.WaitForSeconds(0.1f);
            }
            try { Scp181Manager.TrySelectRoundStart(); }
            catch (Exception ex) { Log.Error($"[Scp181] Round-start selection failed: {ex.Message}"); }
        }

        private void CancelSelection()
        {
            MEC.Timing.KillCoroutines(_selection);
        }

        private void OnRoundEnded(RoundEndedEventArgs ev)
        {
            CancelSelection();
            Scp181Manager.Clear();
            ResetFlags();
        }

        private void OnRestartingRound()
        {
            CancelSelection();
            Scp181Manager.Clear();
            ResetFlags();
        }

        private static void ResetFlags() => UnlockCooldowns.Clear();

        // ================= Escape keeps the passives =================

        private void OnSpawned(SpawnedEventArgs ev)
        {
            if (!Scp181Manager.IsScp181(ev.Player))
                return;

            // Keep death ownership until OnDied emits the containment announcement.
            if (ev.Reason == SpawnReason.Died)
                return;

            // Inspect the completed swap, so a cancelled role request cannot strip ownership.
            if (ev.Reason != SpawnReason.Escaped || !ev.Player.IsAlive ||
                (!NtfRoles.Contains(ev.Player.Role.Type) && !ChaosRoles.Contains(ev.Player.Role.Type)))
            {
                Scp181Manager.Remove(ev.Player);
                return;
            }

            Scp181Manager.SetTeam(ev.Player, NtfRoles.Contains(ev.Player.Role.Type)
                ? Scp181Team.Ntf : Scp181Team.Chaos);
            Scp181Team team = Scp181Manager.GetTeam(ev.Player) ?? Scp181Team.D;
            Scp181Hints.RemoveRoleIntro(ev.Player);
            Scp181Hints.ShowRoleIntro(ev.Player, team);

            // The role change cleared the effects; put them back now that the new role is live.
            Scp181Manager.ApplyReductionEffects(ev.Player);
        }

        // ================= Damage mitigation =================

        private void OnHurting(HurtingEventArgs ev)
        {
            Player victim = ev.Player;
            if (!ev.IsAllowed || !Scp181Manager.IsScp181(victim) || !victim.IsAlive)
                return;

            DamageType damageType = ev.DamageHandler?.Type ?? DamageType.Unknown;
            bool hasAttacker = ev.Attacker != null && ev.Attacker != victim;
            RoleTypeId? attackerRole = hasAttacker ? ev.Attacker!.Role.Type : (RoleTypeId?)null;
            bool fromScp = damageType.IsScp(checkItems: false)
                           || (attackerRole.HasValue && PlayerRolesUtils.GetTeam(attackerRole.Value) == Team.SCPs);

            // Scripted instant kills bypass immunity as well as damage mitigation.
            if (ev.IsInstantKill && !fromScp && damageType != DamageType.PocketDimension)
                return;

            // Post-last-stand immunity window: nothing lands at all.
            if (Scp181Manager.InSurviveImmunity(victim))
            {
                if (Config.Debug)
                    Log.Debug($"[Scp181] {victim.Nickname} ignored {damageType} ({ev.Amount:F2}) during last-stand immunity.");
                Deny(ev, victim);
                return;
            }

            // Damage-over-time from a status effect (bleeding, poison, hypothermia, ...) never lands.
            // SCP-3114's strangulation is excluded: it is an SCP attack streamed per frame, and the
            // hold ends as soon as one tick is refused (Strangled.ServerUpdate). It only takes the
            // mitigation below and is never dodged.
            bool strangled = damageType == DamageType.Strangled;
            if (!strangled && damageType.IsStatusEffect())
            {
                Deny(ev, victim);
                return;
            }

            // Pocket Dimension: the first lethal outcome per round is converted into an escape
            // instead of a death. Non-lethal decay ticks fall through to normal mitigation.
            if (damageType == DamageType.PocketDimension)
            {
                if (IsLethal(ev, victim) && Scp181Manager.TryUsePocketEscape(victim))
                {
                    Deny(ev, victim);
                    EscapePocket(victim);
                    return;
                }
            }

            // A damage handler with Damage == -1 is the game's instant-kill sentinel
            // (StandardDamageHandler.KillValue): it zeroes health directly and skips both
            // ProcessDamage and any arithmetic done here. It has to be turned into a real number
            // BEFORE anything multiplies it, otherwise a reduction multiplier turns a guaranteed
            // kill into "Damage <= 0 => no damage at all".
            if (ev.IsInstantKill)
            {
                // Scripted terminations (warhead, pit, recontainment, RA kill, friendly-fire
                // detector) are deliberately left alone: SCP-181 is a survivability role, not an
                // exemption from the round's own kill switches.
                if (!fromScp)
                    return;

                // SCP-173's neck snap, SCP-049's instakill and SCP-106's grab all arrive here.
                // They obey the same cap as any other SCP hit. The clamp keeps a misconfigured
                // negative cap from re-creating the instant-kill sentinel.
                ev.Amount = Mathf.Max(0f, Config.ScpDamageCap);
            }

            float multiplier = ResolveMultiplier(damageType, attackerRole);
            if (multiplier <= 0f)
            {
                Deny(ev, victim);
                return;
            }

            if (multiplier < 1f)
                ev.Amount *= multiplier;

            if (fromScp && ev.Amount > Config.ScpDamageCap)
                ev.Amount = Config.ScpDamageCap;

            // Flat dodge chance against everything that got this far.
            if (!strangled && UnityEngine.Random.value < Config.DodgeChance)
            {
                Deny(ev, victim);
                if (hasAttacker)
                    Scp181Hints.ShowDodgeMsg(ev.Attacker!);

                return;
            }

        }

        private void OnDying(LabApi.Events.Arguments.PlayerEvents.PlayerDyingEventArgs ev)
        {
            Player victim = Player.Get(ev.Player.ReferenceHub);
            if (!ev.IsAllowed || !Scp181Manager.IsScp181(victim))
                return;

            // Native Dying runs after protection, damage modifiers, AHP and Hume Shield,
            // but before death callbacks. Scripted instant kills must still terminate the role.
            if (ev.DamageHandler is not PlayerStatsSystem.StandardDamageHandler damage || damage.Damage == -1f)
                return;

            if (!Scp181Manager.TryUseSurvive(victim))
            {
                if (Config.Debug)
                    Log.Debug($"[Scp181] {victim.Nickname} dies to {damage.GetType().Name} ({damage.Damage:F2}); no last-stand charge left.");
                return;
            }

            victim.Health = 1f;
            ev.IsAllowed = false;
            Scp181Manager.GrantSurviveImmunity(victim, Config.SurviveImmunitySeconds);
            Scp181Hints.ShowSurviveMsg(victim);
            if (Config.Debug)
                Log.Debug($"[Scp181] {victim.Nickname} survived {damage.GetType().Name} ({damage.Damage:F2}) on 1 HP; immune for {Config.SurviveImmunitySeconds:F1}s.");
        }

        private static void Deny(HurtingEventArgs ev, Player victim)
        {
            ev.IsAllowed = false;
            Scp181Manager.ClearScpDebuffs(victim);
        }

        /// <summary>Whether this hit would actually finish the player, shields included.</summary>
        private static bool IsLethal(HurtingEventArgs ev, Player victim)
            => ev.IsInstantKill || ev.Amount >= victim.Health + victim.ArtificialHealth + victim.HumeShield;

        /// <summary>
        /// Looks the kept-damage fraction up by the exact damage type, then by the generic
        /// "Firearm" key for any weapon, then by the attacker's role name.
        /// </summary>
        private static float ResolveMultiplier(DamageType damageType, RoleTypeId? attackerRole)
        {
            Dictionary<string, float> table = Config.DamageReductionTable;
            if (table == null || table.Count == 0)
                return 1f;

            if (table.TryGetValue(damageType.ToString(), out float exact))
                return exact;

            if (damageType.IsWeapon() && table.TryGetValue(nameof(DamageType.Firearm), out float firearm))
                return firearm;

            if (attackerRole.HasValue && table.TryGetValue(attackerRole.Value.ToString(), out float byRole))
                return byRole;

            return 1f;
        }

        /// <summary>
        /// Moves SCP-181 out of the Pocket Dimension alive, mirroring the game's own successful
        /// exit (PocketDimensionTeleport.Exit): best exit pose for the role, the same Disabled +
        /// Traumatized aftermath, decay effects cleared and the teleports reshuffled.
        /// This runs from Hurting rather than Dying on purpose - by the time Dying fires the game
        /// has already zeroed the health bar, so cancelling there leaves a 0 HP player walking.
        /// </summary>
        private static void EscapePocket(Player p)
        {
            try
            {
                if (p.Role.Base is not IFpcRole fpcRole)
                {
                    Log.Warn("[Scp181] Pocket Dimension escape skipped: the role has no first-person module.");
                    return;
                }

                fpcRole.FpcModule.ServerOverridePosition(Scp106PocketExitFinder.GetBestExitPosition(fpcRole));
                p.DisableEffect(EffectType.PocketCorroding);
                p.DisableEffect(EffectType.Corroding);
                Scp181Manager.ClearScpDebuffs(p);
                p.EnableEffect(EffectType.Disabled, 10f, addDurationIfActive: true);
                p.EnableEffect(EffectType.Traumatized);
                PocketDimensionGenerator.RandomizeTeleports();

                if (Config.Debug)
                    Log.Info($"[Scp181] {p.Nickname} escaped the Pocket Dimension.");
            }
            catch (Exception ex)
            {
                Log.Error($"[Scp181] Pocket Dimension escape failed: {ex.Message}");
            }
        }

        // ================= Death broadcast =================

        private void OnDied(DiedEventArgs ev)
        {
            if (!Scp181Manager.IsScp181(ev.Player))
                return;

            try
            {
                string killerName = ev.Attacker != null ? ev.Attacker.Nickname : "未知";
                if (Config.DiedCassieEnable)
                    Exiled.API.Features.Cassie.MessageTranslated(Config.CassieTransmission, Config.CassieSubtitles, false, true, true);

                Map.Broadcast((ushort)Config.DeathAnnounceSeconds, Config.DeathAnnounce.Replace("{name}", killerName));
            }
            catch (Exception ex)
            {
                Log.Error($"[Scp181] Death broadcast failed: {ex.Message}");
            }

            Scp181Manager.Remove(ev.Player);
        }

        private void OnLeft(LeftEventArgs ev)
        {
            Scp181Manager.Remove(ev.Player);
            ClearUnlockCooldowns(ev.Player);
        }

        // ================= Item duplication =================

        private void OnPickingUpItem(PickingUpItemEventArgs ev)
        {
            if (!Scp181Manager.IsScp181(ev.Player) || ev.Pickup == null)
                return;

            if (Config.Debug)
                Log.Info($"[Scp181] {ev.Player.Nickname} picked up {ev.Pickup.Info.ItemId}.");

            if (UnityEngine.Random.value >= Config.CopyChance)
                return;

            // This fires BEFORE the pickup itself is inserted. Handing out the copy at slot 8 would
            // make the real pickup's ServerAddItem return null while the completor destroys the
            // pickup anyway, so the player would lose the item they were picking up. Only duplicate
            // when there is room for both.
            if (ev.Player.Items.Count >= InventoryCapacity - 1)
                return;

            try
            {
                if (ev.Player.AddItem(ev.Pickup.Info.ItemId) != null)
                    Scp181Hints.ShowCopyMsg(ev.Player);
            }
            catch (Exception ex)
            {
                if (Config.Debug)
                    Log.Debug($"[Scp181] Item duplication failed: {ex.Message}");
            }
        }

        // ================= Lucky unlocks =================

        private void OnInteractingDoor(InteractingDoorEventArgs ev)
        {
            if (!Scp181Manager.IsScp181(ev.Player) || ev.Door == null)
                return;

            // SCP-079's own doors and anything 079 has locked stay out of reach.
            if (Scp079Doors.Contains(ev.Door.Type) || ev.Door.IsLocked)
                return;

            if (!TryUnlockRoll(ev.Player, "door:" + ev.Door.Base.GetInstanceID()))
                return;

            ev.IsAllowed = true;
            if (Config.Debug)
                Log.Info($"[Scp181] {ev.Player.Nickname} got lucky on door {ev.Door.Name}.");
        }

        private void OnInteractingLocker(InteractingLockerEventArgs ev)
        {
            if (!Scp181Manager.IsScp181(ev.Player) || ev.InteractingLocker == null)
                return;

            if (TryUnlockRoll(ev.Player, $"locker:{ev.InteractingLocker.Base.GetInstanceID()}:{ev.InteractingChamber?.Id}"))
                ev.IsAllowed = true;
        }

        /// <summary>
        /// Rolls the unlock chance once per interactable, then holds the result for
        /// UnlockRerollCooldownSeconds. Without the hold, spamming the interact key re-rolls every
        /// press and turns a 30% chance into a guaranteed open.
        /// </summary>
        private static bool TryUnlockRoll(Player player, string interactableKey)
        {
            string key = player.Id + "|" + interactableKey;
            float now = Time.timeSinceLevelLoad;

            if (UnlockCooldowns.TryGetValue(key, out float retryAt) && now < retryAt)
                return false;

            if (UnityEngine.Random.value < Config.UnlockChance)
            {
                UnlockCooldowns.Remove(key);
                return true;
            }

            UnlockCooldowns[key] = now + Mathf.Max(0f, Config.UnlockRerollCooldownSeconds);
            return false;
        }

        private static void ClearUnlockCooldowns(Player player)
        {
            if (player == null)
                return;

            string prefix = player.Id + "|";
            foreach (string key in UnlockCooldowns.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                UnlockCooldowns.Remove(key);
        }
    }
}
