using System;
using System.Collections.Generic;
using System.Linq;
using CustomPlayerEffects;
using LabApi.Features.Wrappers;
using Log = LabApi.Features.Console.Logger;
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

        private static Config Config => MainClass.Instance!.Config;

        public static bool IsScp181(Player p) => p != null && Active.ContainsKey(p.PlayerId);

        public static Scp181Team? GetTeam(Player p)
            => p != null && Active.TryGetValue(p.PlayerId, out Scp181Team t) ? (Scp181Team?)t : null;

        /// <summary>Records the side SCP-181 currently counts as, for the role card color.</summary>
        public static void SetTeam(Player p, Scp181Team team)
        {
            if (p != null && Active.ContainsKey(p.PlayerId))
                Active[p.PlayerId] = team;
        }

        /// <summary>Whether the player is inside the short immunity window granted by a last stand.</summary>
        public static bool InSurviveImmunity(Player p)
            => p != null && ImmortalUntil.TryGetValue(p.PlayerId, out long until) && DateTime.UtcNow.Ticks < until;

        public static void GrantSurviveImmunity(Player p, float seconds)
        {
            if (p != null)
                ImmortalUntil[p.PlayerId] = DateTime.UtcNow.AddSeconds(seconds).Ticks;
        }

        /// <summary>Spends one last-stand charge; false when none are left.</summary>
        public static bool TryUseSurvive(Player p) => TrySpend(SurviveLeft, p);

        /// <summary>Spends one guaranteed Pocket Dimension escape; false when none are left.</summary>
        public static bool TryUsePocketEscape(Player p) => TrySpend(PocketEscapesLeft, p);

        private static bool TrySpend(IDictionary<int, int> budget, Player p)
        {
            if (p == null || !budget.TryGetValue(p.PlayerId, out int left) || left <= 0)
                return false;

            budget[p.PlayerId] = left - 1;
            return true;
        }

        /// <summary>
        /// Makes the given living player SCP-181: Class-D body, passives attached, role card shown.
        /// Re-assigning the current SCP-181 only refreshes the card, so the command is idempotent.
        /// </summary>
        public static void Assign(Player p) => TryAssign(p);

        public static bool TryAssign(Player p)
        {
            if (p == null || p.IsDestroyed || !p.IsAlive || !ReinforcementRoleBridge.CanAssign(p))
                return false;

            // Complete the native role swap before claiming ownership. Other SCP-181
            // players retain their identity, effects and independent survival budgets.
            if (!IsScp181(p) && p.Role != RoleTypeId.ClassD)
            {
                try
                {
                    p.SetRole(RoleTypeId.ClassD, RoleChangeReason.None);
                }
                catch (Exception ex)
                {
                    Log.Error($"[Scp181] Failed to set the Class-D role: {ex.Message}");
                    return false;
                }

                if (p.IsDestroyed || p.Role != RoleTypeId.ClassD || !ReinforcementRoleBridge.CanAssign(p))
                    return false;
            }

            if (!IsScp181(p))
            {
                Active[p.PlayerId] = Scp181Team.D;
                SurviveLeft[p.PlayerId] = Config.SurviveChances;
                PocketEscapesLeft[p.PlayerId] = Config.PocketEscapeChances;

                p.MaxHealth = 100;
                p.Health = 100;
            }

            Scp181Identity.Apply(p);
            Scp181Hints.RemoveRoleIntro(p);
            Scp181Hints.ShowRoleIntro(p, GetTeam(p) ?? Scp181Team.D);
            ApplyReductionEffects(p);
            StartDebuffGuard(p);

            if (Config.Debug)
                Log.Info($"[Scp181] {p.Nickname} ({p.UserId}) is now SCP-181.");
            return true;
        }

        /// <summary>
        /// (Re)applies the permanent damage reduction effects. The game disables every effect on a
        /// role change (StatusEffectBase.OnRoleChanged), so this has to run AFTER the new role is
        /// live - see the ChangedRole handler in Scp181Events.
        /// </summary>
        public static void ApplyReductionEffects(Player p)
        {
            if (p == null || p.IsDestroyed)
                return;

            try
            {
                p.EnableEffect<BodyshotReduction>(Config.BodyshotReductionIntensity);
                p.EnableEffect<DamageReduction>(Config.DamageReductionIntensity);
            }
            catch (Exception ex)
            {
                Log.Error($"[Scp181] Failed to apply the damage reduction effects: {ex.Message}");
            }
        }

        /// <summary>Takes the plugin's own buffs back off a player who is no longer SCP-181.</summary>
        private static void RemoveReductionEffects(Player p)
        {
            if (p == null || p.IsDestroyed)
                return;

            try
            {
                p.DisableEffect<BodyshotReduction>();
                p.DisableEffect<DamageReduction>();
            }
            catch (Exception ex)
            {
                Log.Error($"[Scp181] Failed to remove the damage reduction effects: {ex.Message}");
            }
        }

        /// <summary>
        /// Damaging debuffs SCP-181 shrugs off. SCP attack states are deliberately absent:
        /// SCP-049's instant kill is gated on <c>CardiacArrest</c> (Scp049AttackAbility) and
        /// SCP-106's Pocket Dimension capture on <c>Corroding</c> (Scp106Attack), while
        /// <c>PocketCorroding</c> is the Pocket Dimension itself. Stripping those left both SCPs
        /// unable to finish their attacks.
        /// </summary>
        public static void ClearScpDebuffs(Player p)
        {
            if (p == null || p.IsDestroyed)
                return;

            try
            {
                p.DisableEffect<Bleeding>();
                p.DisableEffect<Poisoned>();
                p.DisableEffect<Hemorrhage>();
                p.DisableEffect<Burned>();
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

            if (DebuffGuards.TryGetValue(p.PlayerId, out CoroutineHandle running))
                Timing.KillCoroutines(running);

            DebuffGuards[p.PlayerId] = Timing.RunCoroutine(GuardDebuffs(p));
        }

        /// <summary>Re-clears SCP debuffs twice a second for as long as the player is SCP-181.</summary>
        private static IEnumerator<float> GuardDebuffs(Player p)
        {
            while (p != null && !p.IsDestroyed && IsScp181(p))
            {
                ClearScpDebuffs(p);
                if (MainClass.Instance?.Hints.RequiresPromptRefresh == true)
                    Scp181Hints.ShowRoleIntro(p, GetTeam(p) ?? Scp181Team.D);
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

            if (DebuffGuards.TryGetValue(p.PlayerId, out CoroutineHandle guard))
            {
                Timing.KillCoroutines(guard);
                DebuffGuards.Remove(p.PlayerId);
            }

            Scp181Identity.Remove(p);
            bool wasActive = Active.Remove(p.PlayerId);
            ImmortalUntil.Remove(p.PlayerId);
            SurviveLeft.Remove(p.PlayerId);
            PocketEscapesLeft.Remove(p.PlayerId);

            Scp181Hints.RemoveRoleIntro(p);
            Scp181Hints.ClearTransient(p);

            // A living ex-SCP-181 would otherwise keep a permanent damage reduction buff.
            if (wasActive && !p.IsDestroyed && p.IsAlive)
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

            List<Player> alive = Player.List.Where(x => !x.IsDestroyed && x.IsAlive).ToList();
            if (alive.Count <= Config.MinPlayers)
                return;

            List<Player> available = alive.Where(ReinforcementRoleBridge.CanAssign).ToList();
            List<Player> pool = available.Where(x => x.Role == RoleTypeId.ClassD).ToList();
            if (pool.Count == 0)
                pool = available.Where(x => x.Role == RoleTypeId.Tutorial || x.Role == RoleTypeId.Scientist).ToList();

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
                Player? p = Player.Get(id);
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

            Scp181Identity.Clear();
            Scp181Hints.ClearAll();
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
