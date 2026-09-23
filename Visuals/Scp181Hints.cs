using System.Collections.Generic;
using LabApi.Features.Wrappers;
using MEC;
using Scp181.Services;
using UnityEngine;

namespace Scp181.Visuals
{
    /// <summary>Which side SCP-181 currently counts as, for the role card color.</summary>
    public enum Scp181Team
    {
        D,
        Ntf,
        Chaos,
    }

    /// <summary>
    /// SCP-181's on-screen text, all of it routed through the shared hint display provider:
    /// the role card (persistent, bottom of the screen), the dodge notice shown to the attacker,
    /// the last-stand notice and the item duplication notice.
    /// </summary>
    public static class Scp181Hints
    {
        private const string RoleTag = "role";
        private const string DodgeTag = "dodge";
        private const string SurviveTag = "survive";
        private const string CopyTag = "copy";
        private static readonly Dictionary<Player, CoroutineHandle> DodgeCountdowns = new();

        private static Config Config => MainClass.Instance!.Config;

        private static IHintDisplayProvider? Hints => MainClass.Instance?.Hints;

        /// <summary>Persistent role card; the color follows the side SCP-181 currently counts as.</summary>
        public static void ShowRoleIntro(Player p, Scp181Team team)
        {
            Player? target = p;
            if (target == null)
                return;

            string text =
                $"你是[<color={TeamColor(team)}>SCP-181</color>]\n" +
                "<size=22><color=#E7ECF3>你拥有非常逆天的免伤,拾取物品时有概率复制一份,要在设施里尽力苟活口牙</color></size>";

            Hints?.ShowPersistentPrompt(target, RoleTag, Config.RoleIntroY, text, HintVerticalAnchor.Middle);
        }

        public static void RemoveRoleIntro(Player p)
        {
            Player? target = p;
            if (target != null)
                Hints?.Remove(target, RoleTag);
        }

        /// <summary>Dodge notice for the attacker, counting down to its own removal.</summary>
        public static void ShowDodgeMsg(Player attacker)
        {
            if (attacker != null)
            {
                StopDodge(attacker);
                DodgeCountdowns[attacker] = Timing.RunCoroutine(DodgeCountdown(attacker));
            }
        }

        private static IEnumerator<float> DodgeCountdown(Player p)
        {
            int total = Mathf.Max(1, (int)Config.DodgeMsgSeconds);
            for (int i = total; i >= 1; i--)
            {
                Player? target = p;
                if (target == null || p.IsDestroyed)
                    yield break;

                string text = $"<size=28><color=#FF9500>[{i}]对方是SCP-181,免疫了你的伤害嘻嘻(^_^)</color></size>";

                // A slightly longer duration than the tick keeps the hint alive between updates and
                // still expires on its own if the countdown is cut short.
                Hints?.ShowPrompt(target, DodgeTag, Config.DodgeMsgY, text, 1.5f, HintVerticalAnchor.Middle);

                yield return Timing.WaitForSeconds(1f);
            }

            Player? final = p;
            if (final != null)
                Hints?.Remove(final, DodgeTag);
            DodgeCountdowns.Remove(p);
        }

        /// <summary>Last-stand notice for SCP-181.</summary>
        public static void ShowSurviveMsg(Player p)
        {
            Player? target = p;
            if (target == null)
                return;

            Hints?.ShowPrompt(
                target,
                SurviveTag,
                Config.SurviveMsgY,
                "<size=30><color=#5BFF80>幸运眷顾！你以1点血扛下了这次致命伤害</color></size>",
                Config.SurviveMsgSeconds,
                HintVerticalAnchor.Middle);
        }

        /// <summary>Item duplication notice for SCP-181.</summary>
        public static void ShowCopyMsg(Player p)
        {
            Player? target = p;
            if (target == null)
                return;

            Hints?.ShowPrompt(
                target,
                CopyTag,
                Config.DodgeMsgY,
                $"[<color={Config.ScpColor}>SCP-181</color>]<color=#9EDC9E>运气不错!</color>物品数量+1",
                Config.CopyMsgSeconds,
                HintVerticalAnchor.Middle);
        }

        /// <summary>Drops the short-lived notices so nothing lingers after a death or a disconnect.</summary>
        public static void ClearTransient(Player p)
        {
            StopDodge(p);
            Player? target = p;
            if (target == null)
                return;

            Hints?.Remove(target, DodgeTag);
            Hints?.Remove(target, SurviveTag);
            Hints?.Remove(target, CopyTag);
        }

        private static void StopDodge(Player player)
        {
            if (DodgeCountdowns.TryGetValue(player, out CoroutineHandle handle))
            {
                Timing.KillCoroutines(handle);
                DodgeCountdowns.Remove(player);
            }
        }

        internal static void ClearAll()
        {
            foreach (Player player in new List<Player>(DodgeCountdowns.Keys))
                ClearTransient(player);
        }

        private static string TeamColor(Scp181Team team)
        {
            switch (team)
            {
                case Scp181Team.Ntf: return Config.NtfColor;
                case Scp181Team.Chaos: return Config.ChaosColor;
                default: return Config.ScpColor;
            }
        }
    }
}
