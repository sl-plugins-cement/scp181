using System.ComponentModel;

namespace Scp181;

public sealed class HintDisplayConfig
{
    [Description("If true, missing HintServiceMeow falls back to conservative vanilla hints. Leave false unless deliberate compatibility is required.")]
    public bool EnableVanillaFallback { get; set; } = false;

    [Description("HSM group used for this plugin's hints. Keep it unique per plugin.")]
    public string GroupName { get; set; } = "scp181.hints";

    [Description("Prefix added to hint IDs before they are sent to HSM. Keep it unique per plugin.")]
    public string TagPrefix { get; set; } = "scp181.";

    [Description("Default HSM X coordinate for provider helper methods. HSM uses 0 as screen center; keep helpers center-anchored and tune this value for edge placement.")]
    public float DefaultX { get; set; } = 0f;

    [Description("Default HSM Y coordinate for notice hints. HSM uses 0 at top and 1080 at bottom.")]
    public float NoticeY { get; set; } = 760f;

    [Description("Font size used for notice hints.")]
    public int NoticeTextSize { get; set; } = 24;

    [Description("Font size used for prompt hints.")]
    public int PromptTextSize { get; set; } = 20;

    [Description("Extra HSM line height added between rendered lines.")]
    public float LineHeight { get; set; } = 0f;

    [Description("If true, asks HSM to refresh immediately after add/update/remove operations. Default false: HSM coalesces text updates within the hint's SyncSpeed window (~0.1s for Fast); adds/removes are still immediate. Only set true for genuinely per-frame-animated hints.")]
    public bool ForceFastUpdates { get; set; } = false;

    [Description("Fallback text used by ShowKeybindPrompt when HSM cannot resolve native keybind parameters. {0} is the keybind setting id.")]
    public string KeybindTokenFormat { get; set; } = "[key:{0}]";
}
