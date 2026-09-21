using System.Collections.Generic;
using System.ComponentModel;
using Exiled.API.Interfaces;

namespace Scp181
{
    public class Config : IConfig
    {
        [Description("Whether the plugin is enabled.")]
        public bool IsEnabled { get; set; } = true;

        [Description("Verbose debug logging.")]
        public bool Debug { get; set; } = false;

        // ---- Round-start selection ----
        [Description("Pick an SCP-181 automatically at round start (requires more than MinPlayers alive players).")]
        public bool AutoSelectOnRoundStart { get; set; } = true;

        [Description("Alive player count that must be EXCEEDED before a round-start SCP-181 is picked.")]
        public int MinPlayers { get; set; } = 5;

        // ---- Passive chances ----
        [Description("Chance (0-1) that picking an item up also duplicates it.")]
        public float CopyChance { get; set; } = 0.1f;

        [Description("Chance (0-1) that an incoming attack is negated outright.")]
        public float DodgeChance { get; set; } = 0.5f;

        [Description("Damage source -> fraction of damage that still lands (0.1 keeps 10%, 0 negates it).\n" +
                     "Keys are EXILED DamageType names (Firearm, Scp173, Scp106, Explosion, Tesla, ...).\n" +
                     "A specific weapon type wins over the generic \"Firearm\" key; if neither matches, the\n" +
                     "attacker's RoleTypeId name (Scp173, ChaosRifleman, ...) is tried last.")]
        public Dictionary<string, float> DamageReductionTable { get; set; } =
            new Dictionary<string, float>
            {
                ["Firearm"] = 0.1f,
            };

        [Description("Hard cap on a single hit from any SCP. Also the damage an SCP instant-kill\n" +
                     "(SCP-173 neck snap, SCP-049 instakill, SCP-106 grab) is converted into.")]
        public float ScpDamageCap { get; set; } = 10f;

        [Description("Chance (0-1) to force-open a keycard door or an SCP locker chamber.")]
        public float UnlockChance { get; set; } = 0.3f;

        [Description("Seconds before a failed unlock roll on the same door/chamber may be rolled again.\n" +
                     "Without this, spamming the interact key converges on a guaranteed open.")]
        public float UnlockRerollCooldownSeconds { get; set; } = 8f;

        [Description("Last-stand charges: number of times a lethal hit is survived with 1 HP instead.")]
        public int SurviveChances { get; set; } = 1;

        [Description("Seconds of full immunity granted right after a last stand.")]
        public float SurviveImmunitySeconds { get; set; } = 1.5f;

        [Description("Number of guaranteed escapes from a lethal Pocket Dimension outcome, per round.")]
        public int PocketEscapeChances { get; set; } = 1;

        [Description("Intensity of the permanent DamageReduction effect. The game computes the kept\n" +
                     "damage as 1 - intensity * 0.005, so 50 = 25% less damage and 200 = immune.")]
        public byte DamageReductionIntensity { get; set; } = 50;

        [Description("Intensity of the permanent BodyshotReduction effect. The game clamps this to its\n" +
                     "5-entry table, so anything at or above 4 is the maximum 15% body-shot reduction.")]
        public byte BodyshotReductionIntensity { get; set; } = 4;

        [Description("Display time (seconds) of the item-duplication hint.")]
        public float CopyMsgSeconds { get; set; } = 3f;

        [Description("Countdown length (seconds) of the dodge hint shown to the attacker.")]
        public float DodgeMsgSeconds { get; set; } = 5f;

        [Description("Display time (seconds) of the last-stand hint.")]
        public float SurviveMsgSeconds { get; set; } = 3f;

        // ---- Colors ----
        [Description("Role card color while SCP-181 is Class-D.")]
        public string ScpColor { get; set; } = "#FF9500";

        [Description("Role card color after SCP-181 escapes as MTF.")]
        public string NtfColor { get; set; } = "#4DA6FF";

        [Description("Role card color after SCP-181 escapes as Chaos Insurgency.")]
        public string ChaosColor { get; set; } = "#1E6B3A";

        // ---- Hint coordinates ----
        [Description("HSM Y coordinate of the persistent role card.")]
        public float RoleIntroY { get; set; } = 900f;

        [Description("HSM Y coordinate of the dodge hint shown to the attacker.")]
        public float DodgeMsgY { get; set; } = 800f;

        [Description("HSM Y coordinate of the last-stand hint shown to SCP-181.")]
        public float SurviveMsgY { get; set; } = 780f;

        // ---- Death broadcast ----
        [Description("CASSIE announcement played when SCP-181 dies.")]
        public string CassieTransmission { get; set; } = ".G5 SCP 1 8 1 HAS BEEN CONTAINED SUCCESSFULLY .G6";

        [Description("CASSIE subtitle shown alongside the announcement.")]
        public string CassieSubtitles { get; set; } = "SCP-181 已被重新收容";

        [Description("Server-wide broadcast when SCP-181 dies. {name} is replaced with the killer's nickname.")]
        public string DeathAnnounce { get; set; } = "<b>[<color=#FF9500>SCP181</color>]已被重新收容，收容者[<color=#8DEEEE>{name}</color>]</b>";

        [Description("Duration (seconds) of the death broadcast.")]
        public float DeathAnnounceSeconds { get; set; } = 8f;

        [Description("Whether the death broadcast is accompanied by the CASSIE announcement.")]
        public bool DiedCassieEnable { get; set; } = true;

        // ---- Hint display ----
        [Description("HintServiceMeow display settings. Hints are disabled entirely when HSM is missing\n" +
                     "unless EnableVanillaFallback is turned on.")]
        public HintDisplayConfig HintDisplay { get; set; } = new HintDisplayConfig();
    }
}
