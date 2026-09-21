using LabApi.Features.Console;
using LabApi.Features.Wrappers;

namespace Scp181.Services;

internal sealed class NullHintDisplayProvider : IHintDisplayProvider
{
    private const string LogPrefix = "[Scp181:Hints]";

    private readonly string _reason;
    private bool _logged;

    public NullHintDisplayProvider(string reason)
    {
        _reason = reason;
    }

    public bool RequiresPromptRefresh => false;

    public void Enable()
    {
        LogOnce();
    }

    public void Disable()
    {
    }

    public void ShowNotice(Player player, string message, float duration)
    {
        LogOnce();
    }

    public void ShowPrompt(Player player, string tagId, float y, string message, float duration)
    {
        LogOnce();
    }

    public void ShowPrompt(Player player, string tagId, float y, string message, float duration, HintVerticalAnchor anchor)
    {
        LogOnce();
    }

    public void ShowPersistentPrompt(Player player, string tagId, float y, string message, HintVerticalAnchor anchor)
    {
        LogOnce();
    }

    public void ShowPrompt(Player player, string tagId, float x, float y, string message, float duration, HintHorizontalAlignment alignment, HintVerticalAnchor anchor)
    {
        LogOnce();
    }

    public void ShowKeybindPrompt(Player player, string tagId, float y, string message, int keybindSettingId)
    {
        LogOnce();
    }

    public void Remove(Player player, string tagId)
    {
    }

    public void Clear(Player player)
    {
    }

    private void LogOnce()
    {
        if (_logged)
        {
            return;
        }

        _logged = true;
        Logger.Error($"{LogPrefix} {_reason} No hint text will be displayed.");
    }
}
