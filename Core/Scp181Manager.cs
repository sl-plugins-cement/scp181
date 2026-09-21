using System;
using System.Collections.Generic;
using System.Linq;
using Exiled.API.Enums;
using Exiled.API.Features;
using MEC;
using PlayerRoles;
using Scp181.Visuals;
using Scp181.Services;

namespace Scp181
{
    /// <summary>
    /// Tracks who is SCP-181, which side they currently count as (role card color), and the
    /// per-player budgets that the passives draw from (last stand, Pocket Dimension escape,
    /// post-last-stand immunity window).
    /// </summary>
    public static class Scp181Manager
    {
        /// <summary>Current SCP-181 player id -> side used for the role card color.</summary>
        private static readonly Dictionary<int, Scp181Team> Active = new Dictionary<int, Scp181Team>();

        /// <summary>End of the post-last-stand immunity window, in <see cref="DateTime.UtcNow"/> ticks.</summary>
        private static readonly Dictionary<int, long> ImmortalUntil = new Dictionary<int, long>();

        /// <summary>Remaining last-stand charges.</summary>
        private static readonly Dictionary<int, int> SurviveLeft = new Dictionary<int, int>();

        /// <summary>Remaining guaranteed Pocket Dimension escapes.</summary>
        private static readonly Dictionary<int, int> PocketEscapesLeft = new Dictionary<int, int>();

        /// <summary>Running debuff-guard coroutines, so a re-assign cannot stack a second one.</summary>
        private static readonly Dictionary<int, CoroutineHandle> DebuffGuards = new Dictionary<int, CoroutineHandle>();

        private static readonly EffectType[] ClearedDebuffs =
        {
            EffectType.CardiacArrest,
            EffectType.Corroding,
            EffectType.PocketCorroding,
            EffectType.Bleeding,
            EffectType.Poisoned,
            EffectType.Ensnared,
            EffectType.Concussed,
            EffectType.Hemorrhage,
            EffectType.Burned,
        };

        private static Config Config => MainClass.Instance!.Config;

        public static bool IsScp181(Player p) => p != null && Active.ContainsKey(p.Id);

        public static Scp181Team? GetTeam(Player p)
            => p != null && Active.TryGetValue(p.Id, out Scp181Team t) ? (Scp181Team?)t : null;

        /// <summary>Records the side SCP-181 currently counts as, for the role card color.</summary>
        public static void SetTeam(Player p, Scp181Team team)
        {
            if (p != null && Active.ContainsKey(p.Id))
                Active[p.Id] = team;
        }

        /// <summary>Whether the player is inside the short immunity window granted by a last stand.</summary>
        public static bool InSurviveImmunity(Player p)
            => p != null && ImmortalUntil.TryGetValue(p.Id, out long until) && DateTime.UtcNow.Ticks < until;

        public static void GrantSurviveImmunity(Player p, float seconds)
        {
            if (p != null)
                ImmortalUntil[p.Id] = DateTime.UtcNow.AddSeconds(seconds).Ticks;
        }

        /// <summary>Spends one last-stand charge; false when none are left.</summary>
        public static bool TryUseSurvive(Player p) => TrySpend(SurviveLeft, p);

        /// <summary>Spends one guaranteed Pocket Dimension escape; false when none are left.</summary>
        public static bool TryUsePocketEscape(Player p) => TrySpend(PocketEscapesLeft, p);

        private static bool TrySpend(IDictionary<int, int> budget, Player p)
        {
            if (p == null || !budget.TryGetValue(p.Id, out int left) || left <= 0)
                return false;

            budget[p.Id] = left - 1;
            return true;
        }

        /// <summary>
        /// Makes the given living player SCP-181: Class-D body, passives attached, role card shown.
        /// Re-assigning the current SCP-181 only refreshes the card, so the command is idempotent.
        /// </summary>
        public static void Assign(Player p) => TryAssign(p);

        public static bool TryAssign(Player p)
        {
            if (p == null || !p.IsConnected || !p.IsAlive || !ReinforcementRoleBridge.CanAssign(p))
                return false;

            // Validate before releasing the incumbent so a rejected request changes no ownership.
            foreach (Player current in Player.List.Where(x => x.Id != p.Id && IsScp181(x)).ToList())
                Remove(current);

            if (!IsScp181(p))
            {
                Active[p.Id] = Scp181Team.D;
                SurviveLeft[p.Id] = Config.SurviveChances;
                PocketEscapesLeft[p.Id] = Config.PocketEscapeChances;

                // Only respawn a player who is not already Class-D. ServerSetRole drops the
                // inventory and moves the player to a Class-D spawn, so re-rolling an existing
                // Class-D would silently take away round-start items and their position.
                if (p.Role.Type != RoleTypeId.ClassD)
                {
                    try
                    {
                        p.ReferenceHub.roleManager.ServerSetRole(RoleTypeId.ClassD, RoleChangeReason.None);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[Scp181] Failed to set the Class-D role: {ex.Message}");
                    }
                }

                p.MaxHealth = 100;
                p.Health = 100;
            }
            else
            {
                Active[p.Id] = Scp181Team.D;
            }

            Scp181Hints.RemoveRoleIntro(p);
            Scp181Hints.ShowRoleIntro(p, Scp181Team.D);
            ApplyReductionEffects(p);
            StartDebuffGuard(p);

            if (Config.Debug)
                Log.Info($"[Scp181] {p.Nickname} ({p.UserId}) is now SCP-181.");
            return true;
        }

        /// <summary>
        /// (Re)applies the permanent damage reduction effects. The game disables every effect on a
        /// role change (StatusEffectBase.OnRoleChanged), so this has to run AFTER the new role is
        /// live - see the Spawned handler in Scp181Events.
        /// </summary>
        public static void ApplyReductionEffects(Player p)
        {
            if (p == null || !p.IsConnected)
                return;

            try
            {
                p.EnableEffect(EffectType.BodyshotReduction, Config.BodyshotReductionIntensity, 3600f);
                p.EnableEffect(EffectType.DamageReduction, Config.DamageReductionIntensity, 3600f);
            }
            catch (Exception ex)
            {
                Log.Error($"[Scp181] Failed to apply the damage reduction effects: {ex.Message}");
            }
        }

        /// <summary>Takes the plugin's own buffs back off a player who is no longer SCP-181.</summary>
        private static void RemoveReductionEffects(Player p)
        {
            if (p == null || !p.IsConnected)
                return;

            try
            {
                p.DisableEffect(EffectType.BodyshotReduction);
                p.DisableEffect(EffectType.DamageReduction);
            }
            catch (Exception ex)
            {
                Log.Error($"[Scp181] Failed to remove the damage reduction effects: {ex.Message}");
            }
        }

        /// <summary>Clears the status effects SCP-181 is immune to (SCP debuffs plus Pocket Dimension decay).</summary>
        public static void ClearScpDebuffs(Player p)
        {
            if (p == null || !p.IsConnected)
                return;

            try
            {
                foreach (EffectType effect in ClearedDebuffs)
                    p.DisableEffect(effect);
            }
            catch (Exception ex)
            {
                if (Config.Debug)
                    Log.Debug($"[Scp181] Failed to clear debuffs: {ex.Message}");
            }
        }

        private static void StartDebuffGuard(Player p)
        {
            if (p == null)
                return;

            if (DebuffGuards.TryGetValue(p.Id, out CoroutineHandle running))
                Timing.KillCoroutines(running);

            DebuffGuards[p.Id] = Timing.RunCoroutine(GuardDebuffs(p));
        }

        /// <summary>Re-clears SCP debuffs twice a second for as long as the player is SCP-181.</summary>
        private static IEnumerator<float> GuardDebuffs(Player p)
        {
            while (p != null && p.IsConnected && IsScp181(p))
            {
                ClearScpDebuffs(p);
                yield return Timing.WaitForSeconds(0.5f);
            }
        }

        /// <summary>
        /// Drops the SCP-181 identity and everything attached to it. Every exit path (death, leave,
        /// reassignment, round end) goes through here so no buff or budget outlives the identity.
        /// </summary>
        public static void Remove(Player p)
        {
            if (p == null)
                return;

            if (DebuffGuards.TryGetValue(p.Id, out CoroutineHandle guard))
            {
                Timing.KillCoroutines(guard);
                DebuffGuards.Remove(p.Id);
            }

            bool wasActive = Active.Remove(p.Id);
            ImmortalUntil.Remove(p.Id);
            SurviveLeft.Remove(p.Id);
            PocketEscapesLeft.Remove(p.Id);

            Scp181Hints.RemoveRoleIntro(p);
            Scp181Hints.ClearTransient(p);

            // A living ex-SCP-181 would otherwise keep an hour-long damage reduction buff.
            if (wasActive && p.IsConnected && p.IsAlive)
                RemoveReductionEffects(p);
        }

        /// <summary>
        /// Round-start pick: Class-D first, then Tutorial/Scientist. SCPs, MTF and Chaos are never
        /// eligible because turning them into SCP-181 would force a Class-D respawn.
        /// </summary>
        public static void TrySelectRoundStart()
        {
            if (!Config.AutoSelectOnRoundStart || Active.Count != 0)
                return;

            List<Player> alive = Player.List.Where(x => x.IsConnected && x.IsAlive).ToList();
            if (alive.Count <= Config.MinPlayers)
                return;

            List<Player> available = alive.Where(ReinforcementRoleBridge.CanAssign).ToList();
            List<Player> pool = available.Where(x => x.Role.Type == RoleTypeId.ClassD).ToList();
            if (pool.Count == 0)
                pool = available.Where(x => x.Role.Type == RoleTypeId.Tutorial || x.Role.Type == RoleTypeId.Scientist).ToList();

            if (pool.Count == 0)
            {
                Log.Info("[Scp181] No unclaimed eligible Class-D/Tutorial/Scientist at round start; no SCP-181 this round.");
                return;
            }

            Assign(pool[UnityEngine.Random.Range(0, pool.Count)]);
        }

        /// <summary>Clears every SCP-181 identity and all attached state.</summary>
        public static void Clear()
        {
            foreach (int id in Active.Keys.ToList())
            {
                Player p = Player.Get(id);
                if (p != null)
                {
                    Remove(p);
                    continue;
                }

                // The player object is already gone; drop the bookkeeping directly.
                if (DebuffGuards.TryGetValue(id, out CoroutineHandle guard))
                {
                    Timing.KillCoroutines(guard);
                    DebuffGuards.Remove(id);
                }

                Active.Remove(id);
                ImmortalUntil.Remove(id);
                SurviveLeft.Remove(id);
                PocketEscapesLeft.Remove(id);
            }

            Active.Clear();
            ImmortalUntil.Clear();
            SurviveLeft.Clear();
            PocketEscapesLeft.Clear();

            foreach (CoroutineHandle guard in DebuffGuards.Values)
                Timing.KillCoroutines(guard);
            DebuffGuards.Clear();
        }
    }
}
