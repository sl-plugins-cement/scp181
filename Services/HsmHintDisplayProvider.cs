using System;
using HsmAdapter;
using LabApi.Features.Console;
using LabApi.Features.Wrappers;

namespace Scp181.Services;

/// <summary>
/// Keeps SCP-181's HSM geometry and markup while HsmAdapter owns hint handles, expiry and cleanup.
/// Call on the game thread. The scope lives from <see cref="Enable"/> to <see cref="Disable"/>; the
/// adapter clears it on WaitingForPlayers and forgets departed players, so nothing is cached here.
/// </summary>
internal sealed class HsmHintDisplayProvider : IHintDisplayProvider
{
    private const string LogPrefix = "[Scp181:Hints]";

    // HSM advances each line of a multi-line hint by the lower line's height plus LineHeight, so a
    // large title over a smaller line overlaps when LineHeight is 0. Top-anchored hints keep the
    // configured spacing because their first line is pinned.
    private const float MinimumLineHeight = 12f;

    // Timed prompts sit in HsmAdapter's Bottom notice region. Each one reserves a centred band on the
    // shared reference canvas for its lifetime so other plugins' notices are placed around it.
    private const float ReservedBandX = 410f;
    private const float ReservedBandWidth = 1100f;
    private const float ReservedBandHeight = 60f;
    private const float CanvasHeight = 1080f;

    // Left/Right placement is translated into Center + X (HSM's own Left/Right modes are asymmetric)
    // and clamped to the in-game wrap-safe band. Measured 2026-06-29; see .tests/UI preview-core.js.
    private const float CenterPixelX = 960f;
    private const float PixelsPerHsmUnit = 0.556f;
    private const float WrapSafeLeftPx = 76f;
    private const float WrapSafeRightPx = 1620f;
    private const float HsmCanvasHalfWidth = 1200f;

    private readonly HintDisplayConfig _config;
    private HintScope? _scope;
    private bool _loggedUnavailable;
    private bool _loggedFailure;
    private bool _loggedMeasurement;

    public HsmHintDisplayProvider(HintDisplayConfig config) => _config = config;

    public bool RequiresPromptRefresh => false;

    public void Enable()
    {
        if (_scope != null)
            return;

        _scope = HsmAdapter.Hints.Acquire("Scp181",
            string.IsNullOrWhiteSpace(_config.GroupName) ? "scp181.hints" : _config.GroupName);
        Logger.Info($"{LogPrefix} Hints use HsmAdapter; HintServiceMeow readiness is checked per hint.");
    }

    public void Disable()
    {
        _scope?.Dispose();
        _scope = null;
    }

    public void ShowNotice(Player player, string message, float duration) =>
        Show(player, "notice", _config.DefaultX, _config.NoticeY, message, duration,
            _config.NoticeTextSize, HintVerticalAnchor.Middle, reserve: true);

    public void ShowPrompt(Player player, string tagId, float y, string message, float duration) =>
        Show(player, tagId, _config.DefaultX, y, message, duration, _config.PromptTextSize,
            HintVerticalAnchor.Middle, reserve: true);

    public void ShowPrompt(Player player, string tagId, float y, string message, float duration, HintVerticalAnchor anchor) =>
        Show(player, tagId, _config.DefaultX, y, message, duration, _config.PromptTextSize, anchor, reserve: true);

    public void ShowPersistentPrompt(Player player, string tagId, float y, string message, HintVerticalAnchor anchor) =>
        Show(player, tagId, _config.DefaultX, y, message, null, _config.PromptTextSize, anchor, reserve: false);

    public void ShowPrompt(Player player, string tagId, float x, float y, string message, float duration,
        HintHorizontalAlignment alignment, HintVerticalAnchor anchor)
    {
        // The centred band does not describe an edge-aligned block, so these prompts reserve nothing.
        bool centred = alignment == HintHorizontalAlignment.Center;
        Show(player, tagId, centred ? x : ResolveCenterX(alignment, x, message), y, message, duration,
            _config.PromptTextSize, anchor, reserve: centred);
    }

    public void ShowKeybindPrompt(Player player, string tagId, float y, string message, int keybindSettingId)
    {
        string token = string.Format(_config.KeybindTokenFormat ?? "[key:{0}]", keybindSettingId);
        string text = string.IsNullOrWhiteSpace(message) ? token
            : message.Contains("{key}") ? message.Replace("{key}", token) : $"{token} {message}";
        Show(player, tagId, _config.DefaultX, y, text, null, _config.PromptTextSize,
            HintVerticalAnchor.Middle, reserve: false);
    }

    public void Remove(Player player, string tagId)
    {
        if (_scope == null || player?.ReferenceHub == null)
            return;

        try { _scope.Remove(player, NormalizeTagId(tagId)); }
        catch (Exception ex) { LogFailure(ex); }
    }

    public void Clear(Player player)
    {
        if (_scope == null || player?.ReferenceHub == null)
            return;

        try { _scope.Clear(player); }
        catch (Exception ex) { LogFailure(ex); }
    }

    /// <summary>A null duration persists until removal; a non-positive duration removes the hint.</summary>
    private void Show(Player player, string tagId, float x, float y, string message, float? duration,
        int textSize, HintVerticalAnchor anchor, bool reserve)
    {
        if (_scope == null || !IsDisplayable(player))
            return;

        if (duration.HasValue && duration.Value <= 0f)
        {
            Remove(player, tagId);
            return;
        }

        string key = NormalizeTagId(tagId);
        float lineHeight = anchor == HintVerticalAnchor.Top
            ? _config.LineHeight
            : Math.Max(_config.LineHeight, MinimumLineHeight);
        // Extra HSM refreshes only when configured, as before; HSM still schedules its own updates.
        HsmHintLayout layout = new(message ?? string.Empty, x, y, Math.Min(120, Math.Max(6, textSize)),
            ToAdapterAnchor(anchor), HsmHorizontalAlignment.Center, HsmSyncSpeed.Fast,
            fastUpdate: true, lineHeight: Math.Max(0f, lineHeight),
            forceUpdate: _config.ForceFastUpdates, forceMembershipUpdate: _config.ForceFastUpdates);

        try
        {
            bool shown = reserve && duration.HasValue
                ? _scope.ShowHsmReserved(player, key, layout, ReservedBand(y, anchor), duration.Value) == NoticeResult.Visible
                : _scope.ShowHsm(player, key, layout, duration ?? 0f);
            if (!shown)
                LogUnavailable();
        }
        catch (Exception ex)
        {
            LogFailure(ex);
        }
    }

    private static ScreenRect ReservedBand(float y, HintVerticalAnchor anchor)
    {
        float top = anchor switch
        {
            HintVerticalAnchor.Top => y,
            HintVerticalAnchor.Bottom => y - ReservedBandHeight,
            _ => y - ReservedBandHeight / 2f,
        };
        top = Math.Max(0f, Math.Min(CanvasHeight - ReservedBandHeight, top));
        return new ScreenRect(ReservedBandX, top, ReservedBandWidth, ReservedBandHeight);
    }

    private static VerticalAnchor ToAdapterAnchor(HintVerticalAnchor anchor) => anchor switch
    {
        HintVerticalAnchor.Top => VerticalAnchor.Top,
        HintVerticalAnchor.Bottom => VerticalAnchor.Bottom,
        _ => VerticalAnchor.Middle,
    };

    // HSM Left pins the block's left edge at X - canvasHalfWidth and Right pins the right edge at
    // X + canvasHalfWidth. Feed the resulting centre back as a Center X, clamped so neither edge
    // crosses the wrap-safe band.
    private float ResolveCenterX(HintHorizontalAlignment alignment, float x, string message)
    {
        float? measured = HsmAdapter.Hints.MeasureHsmWidth(message ?? string.Empty,
            Math.Min(96, Math.Max(6, _config.PromptTextSize)));
        if (!measured.HasValue)
        {
            if (!_loggedMeasurement)
            {
                _loggedMeasurement = true;
                Logger.Warn($"{LogPrefix} HSM text measurement is unavailable; edge-aligned hints use their X as a centre offset.");
            }

            return x;
        }

        float width = measured.Value;
        float half = width / 2f;
        float centre = alignment == HintHorizontalAlignment.Left
            ? x - HsmCanvasHalfWidth + half
            : x + HsmCanvasHalfWidth - half;
        const float bandLeft = (WrapSafeLeftPx - CenterPixelX) / PixelsPerHsmUnit;
        const float bandRight = (WrapSafeRightPx - CenterPixelX) / PixelsPerHsmUnit;
        // A block wider than the band keeps its left edge in and overflows to the right.
        if (width >= bandRight - bandLeft)
            return bandLeft + half;

        return Math.Max(bandLeft + half, Math.Min(bandRight - half, centre));
    }

    private string NormalizeTagId(string tagId)
    {
        string prefix = string.IsNullOrWhiteSpace(_config.TagPrefix) ? "scp181." : _config.TagPrefix;
        return tagId.StartsWith(prefix, StringComparison.Ordinal) ? tagId : prefix + tagId;
    }

    private static bool IsDisplayable(Player? player) =>
        player?.ReferenceHub != null && (player.IsDummy || (player.IsPlayer && player.IsReady));

    private void LogUnavailable()
    {
        if (_loggedUnavailable)
            return;

        _loggedUnavailable = true;
        Logger.Error($"{LogPrefix} HintServiceMeow is unavailable through HsmAdapter; SCP-181 hints are not displayed.");
    }

    private void LogFailure(Exception ex)
    {
        if (_loggedFailure)
            return;

        _loggedFailure = true;
        Logger.Error($"{LogPrefix} HsmAdapter display failed: {ex.GetBaseException().Message}");
    }
}
