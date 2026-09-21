using System.Collections;
using System.Collections.Generic;
using Exiled.API.Features;
using MEC;

namespace Scp181.Visuals
{
    /// <summary>SCP-181 当前立场所对应的角色介绍配色状态。</summary>
    public enum Scp181Team
    {
        D,
        Ntf,
        Chaos
    }

    /// <summary>
    /// SCP-181 视觉提示。
    /// — 角色介绍：屏幕底部偏上常驻两行（D/九尾/混沌配色不同）。
    /// — 免伤提示：给攻击者，中心偏下短暂显示。
    /// — 绝境生还提示：给 SCP-181 本人，中心偏下短暂显示。
    /// </summary>
    public static class Scp181Hints
    {
        public const string RoleGroup = "scp181.role";
        public const string DodgeGroup = "scp181.dodge";
        public const string SurviveGroup = "scp181.survive";
        public const string CopyGroup = "scp181.copy";

        private static Config Config => MainClass.Instance.Config;

        /// <summary>角色介绍（底部常驻，撤离后按配色切换）。</summary>
        public static void ShowRoleIntro(Player p, Scp181Team team)
        {
            if (p == null) return;
            string color = TeamColor(team);
            string text =
                $"你是[<color={color}>SCP181</color>]\n" +
                "<size=22><color=#E7ECF3>你拥有非常逆天的免伤,拾取物品时有概率复制一份,要在设施里尽力苟活口牙</color></size>";
            if (HsmHelper.ShowHint(p, text, Config.RoleIntroY, 26, "role", RoleGroup))
                return;
            p.Broadcast(10, text);
        }

        public static void RemoveRoleIntro(Player p)
            => HsmHelper.RemoveHint(p, "role", RoleGroup);

        /// <summary>免伤提示（给攻击者，中心偏下带 5 秒倒计时，结束后自动清除）。</summary>
        public static void ShowDodgeMsg(Player attacker)
        {
            if (attacker == null) return;
            Timing.RunCoroutine(DodgeCountdown(attacker));
        }

        private static IEnumerator<float> DodgeCountdown(Player p)
        {
            const string id = "dodge";
            const string grp = DodgeGroup;
            int total = (int)Config.DodgeMsgSeconds;
            for (int i = total; i >= 1; i--)
            {
                if (p == null || !p.IsConnected)
                    yield break;

                string text = $"<size=28><color=#FF9500>[{i}]对方是SCP-181,免疫了你的伤害嘻嘻(^_^)</color></size>";
                if (!HsmHelper.ShowHint(p, text, Config.DodgeMsgY, 22, id, grp))
                    p.ShowHint(text, 1f);

                yield return Timing.WaitForSeconds(1f);
            }
            if (p != null && p.IsConnected)
                HsmHelper.RemoveHint(p, id, grp);
        }

        /// <summary>绝境生还提示（给 SCP-181 本人）。</summary>
        public static void ShowSurviveMsg(Player p)
        {
            if (p == null) return;
            string text = "<size=30><color=#5BFF80>幸运眷顾！你以1点血扛下了这次致命伤害</color></size>";
            if (HsmHelper.ShowHint(p, text, Config.SurviveMsgY, 24, "survive", SurviveGroup))
            {
                Timing.RunCoroutine(RemoveAfter(p, "survive", SurviveGroup, Config.SurviveMsgSeconds));
                return;
            }
            p.ShowHint(text, Config.SurviveMsgSeconds);
        }

        /// <summary>复制物品提示（给 SCP-181 本人，位置与免伤提示相同：中心偏下）。</summary>
        public static void ShowCopyMsg(Player p)
        {
            if (p == null) return;
            string text = $"[<color={Config.ScpColor}>SCP-181</color>]<color=#9EDC9E>运气不错!</color>物品数量+1";
            if (HsmHelper.ShowHint(p, text, Config.DodgeMsgY, 22, "copy", CopyGroup))
            {
                Timing.RunCoroutine(RemoveAfter(p, "copy", CopyGroup, Config.CopyMsgSeconds));
                return;
            }
            p.ShowHint(text, Config.CopyMsgSeconds);
        }

        /// <summary>玩家死亡/离场时清除临时的免伤、生还、复制提示，避免残留。</summary>
        public static void ClearTransient(Player p)
        {
            if (p == null) return;
            HsmHelper.RemoveHint(p, "dodge", DodgeGroup);
            HsmHelper.RemoveHint(p, "survive", SurviveGroup);
            HsmHelper.RemoveHint(p, "copy", CopyGroup);
        }

        private static IEnumerator<float> RemoveAfter(Player p, string id, string group, float seconds)
        {
            yield return Timing.WaitForSeconds(seconds);
            if (p == null) yield break;
            HsmHelper.RemoveHint(p, id, group);
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