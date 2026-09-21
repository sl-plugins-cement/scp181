using System.Collections.Generic;
using System.ComponentModel;
using Exiled.API.Interfaces;

namespace Scp181
{
    public class Config : IConfig
    {
        [Description("是否启用插件")]
        public bool IsEnabled { get; set; } = true;

        [Description("调试日志")]
        public bool Debug { get; set; } = false;

        // ---- 开局选人 ----
        [Description("开局自动选择 SCP-181（需玩家数大于 MinPlayers）")]
        public bool AutoSelectOnRoundStart { get; set; } = true;
        [Description("开局选人所需的最少玩家数（大于此值才选）")]
        public int MinPlayers { get; set; } = 5;

        // ---- 被动概率 ----
        [Description("复制物品并弹出提示时的概率（0-1）")]
        public float CopyChance { get; set; } = 0.1f;
        [Description("任何来源攻击失效(免伤)的概率（0-1）")]
        public float DodgeChance { get; set; } = 0.5f;
        [Description("伤害减免比例表：伤害来源关键词→保留的伤害比例(0.1=只留10%，0=完全无效)。\n" +
                     "内置关键词：Firearm(枪械子弹)。也可按 SCP 角色名加项，如 \"Scp173\":0.5、\"Scp106\":0。")]
        public Dictionary<string, float> DamageReductionTable { get; set; } =
            new Dictionary<string, float>
            {
                ["Firearm"] = 0.1f
            };
        [Description("开启权限门 / SCP 物品柜的概率（0-1）")]
        public float UnlockChance { get; set; } = 0.3f;
        [Description("绝境生还次数：受致命伤害时以1血存活的次数上限（用一次少一次）")]
        public int SurviveChances { get; set; } = 1;
        [Description("绝境生还后获得免伤的持续时间（秒）")]
        public float SurviveImmunitySeconds { get; set; } = 1.5f;
        [Description("复制物品提示的显示时长（秒）")]
        public float CopyMsgSeconds { get; set; } = 3f;
        [Description("免伤提示(给攻击者)的倒计时秒数")]
        public float DodgeMsgSeconds { get; set; } = 5f;
        [Description("绝境生还提示持续秒数")]
        public float SurviveMsgSeconds { get; set; } = 3f;

        // ---- 配色 ----
        [Description("SCP-181 标题色（D 级时期）")]
        public string ScpColor { get; set; } = "#FF9500";
        [Description("撤离为九尾狐后的 SCP-181 标题色")]
        public string NtfColor { get; set; } = "#4DA6FF";
        [Description("撤离为混沌后的 SCP-181 标题色")]
        public string ChaosColor { get; set; } = "#1E6B3A";

        // ---- HSM 坐标 ----
        [Description("角色介绍 HSM Y 坐标（底部偏上）")]
        public float RoleIntroY { get; set; } = 900f;
        [Description("免伤提示(给攻击者) HSM Y 坐标（中心偏下）")]
        public float DodgeMsgY { get; set; } = 800f;
        [Description("绝境生还提示(给SCP181) HSM Y 坐标")]
        public float SurviveMsgY { get; set; } = 780f;

        // ---- 死亡广播 ----
        [Description("SCP-181 死亡 Cassie TTS(朗读)文本")]
        public string CassieTransmission { get; set; } = ".G5 SCP 1 8 1 HAS BEEN CONTAINED SUCCESSFULLY .G6";
        [Description("SCP-181 死亡 Cassie 屏幕字幕")]
        public string CassieSubtitles { get; set; } = "SCP-181 已被重新收容";
        [Description("SCP-181 死亡全体公告（{name} 会被替换为杀死 181 的玩家昵称）")]
        public string DeathAnnounce { get; set; } = "<b>[<color=#FF9500>SCP181</color>]已被重新收容，收容大蛇[<color=#8DEEEE>{name}</color></b>]";
        [Description("SCP-181 死亡公告持续秒数")]
        public float DeathAnnounceSeconds { get; set; } = 8f;
        [Description("SCP-181 死亡公告是否发送 CASSIE 朗读")]
        public bool DiedCassieEnable { get; set; } = true;
    }
}