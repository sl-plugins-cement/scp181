using System.Collections.Generic;
using System.Linq;
using Hints;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using UnityEngine;

namespace Scp181.Services;

internal sealed class VanillaCompatibilityHintProvider : IHintDisplayProvider
{
    private const string LogPrefix = "[Scp181:Hints]";
    private const float PromptSendIntervalSeconds = 1.15f;
    private const float PromptDurationSeconds = 1.25f;

    private readonly HintDisplayConfig _config;
    private readonly Dictionary<(ReferenceHub Hub, string TagId), float> _nextPromptAt = new();
    private bool _eventsRegistered;

    public VanillaCompatibilityHintProvider(HintDisplayConfig config)
    {
        _config = config;
    }

    public bool RequiresPromptRefresh => true;

    public void Enable()
    {
        RegisterEvents();
        LabApi.Features.Console.Logger.Warn($"{LogPrefix} HSM is unavailable and vanilla fallback is enabled. Falling back to throttled vanilla hints.");
    }

    public void Disable()
    {
        UnregisterEvents();
        _nextPromptAt.Clear();
    }

    public void ShowNotice(Player player, string message, float duration)
    {
        player.SendHint(message, duration);
    }

    public void ShowPrompt(Player player, string tagId, float y, string message, float duration)
    {
        SendThrottled(player, NormalizeTagId(tagId), message, duration);
    }

    public void ShowPrompt(Player player, string tagId, float y, string message, float duration, HintVerticalAnchor anchor)
    {
        // Vanilla hints cannot be positioned; the anchor is irrelevant here.
        SendThrottled(player, NormalizeTagId(tagId), message, duration);
    }

    public void ShowPersistentPrompt(Player player, string tagId, float y, string message, HintVerticalAnchor anchor)
    {
        // Vanilla hints cannot be held open, so persistent text becomes a throttled re-send.
        // RequiresPromptRefresh is true so callers keep re-issuing it.
        SendThrottled(player, NormalizeTagId(tagId), message, PromptDurationSeconds);
    }

    public void ShowPrompt(Player player, string tagId, float x, float y, string message, float duration, HintHorizontalAlignment alignment, HintVerticalAnchor anchor)
    {
        // Vanilla hints cannot be positioned; alignment + X are irrelevant here.
        SendThrottled(player, NormalizeTagId(tagId), message, duration);
    }

    public void ShowKeybindPrompt(Player player, string tagId, float y, string message, int keybindSettingId)
    {
        float now = Time.timeSinceLevelLoad;
        (ReferenceHub Hub, string TagId) key = (player.ReferenceHub, NormalizeTagId(tagId));
        if (_nextPromptAt.TryGetValue(key, out float nextAt) && now < nextAt)
        {
            return;
        }

        _nextPromptAt[key] = now + PromptSendIntervalSeconds;
        player.SendHint(
            message,
            [new SSKeybindHintParameter(keybindSettingId)],
            null,
            PromptDurationSeconds);
    }

    public void Remove(Player player, string tagId)
    {
        _nextPromptAt.Remove((player.ReferenceHub, NormalizeTagId(tagId)));
    }

    public void Clear(Player player)
    {
        foreach ((ReferenceHub hub, string tagId) in _nextPromptAt.Keys.ToArray())
        {
            if (hub == player.ReferenceHub)
            {
                _nextPromptAt.Remove((hub, tagId));
            }
        }
    }

    private void SendThrottled(Player player, string tagId, string message, float duration)
    {
        float now = Time.timeSinceLevelLoad;
        (ReferenceHub Hub, string TagId) key = (player.ReferenceHub, tagId);
        if (_nextPromptAt.TryGetValue(key, out float nextAt) && now < nextAt)
        {
            return;
        }

        _nextPromptAt[key] = now + PromptSendIntervalSeconds;
        player.SendHint(message, Mathf.Min(duration, PromptDurationSeconds));
    }

    private void OnPlayerLeft(PlayerLeftEventArgs ev)
    {
        Clear(ev.Player);
    }

    private void RegisterEvents()
    {
        if (_eventsRegistered)
        {
            return;
        }

        _eventsRegistered = true;
        PlayerEvents.Left += OnPlayerLeft;
    }

    private void UnregisterEvents()
    {
        if (!_eventsRegistered)
        {
            return;
        }

        _eventsRegistered = false;
        PlayerEvents.Left -= OnPlayerLeft;
    }

    private string NormalizeTagId(string tagId)
    {
        string prefix = string.IsNullOrWhiteSpace(_config.TagPrefix) ? "scp181." : _config.TagPrefix;
        return tagId.StartsWith(prefix, System.StringComparison.Ordinal) ? tagId : prefix + tagId;
    }
}
