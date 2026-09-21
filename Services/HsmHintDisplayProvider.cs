using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Console;
using LabApi.Features.Wrappers;

namespace Scp181.Services;

internal sealed class HsmHintDisplayProvider : IHintDisplayProvider
{
    private const string HsmAssemblyName = "HintServiceMeow";
    private const string HsmPlayerDisplayTypeName = "HintServiceMeow.Core.Utilities.PlayerDisplay";
    private const string HsmHintTypeName = "HintServiceMeow.Core.Models.Hints.Hint";
    private const string HsmAbstractHintTypeName = "HintServiceMeow.Core.Models.Hints.AbstractHint";
    private const string HsmHintAlignmentTypeName = "HintServiceMeow.Core.Enum.HintAlignment";
    private const string HsmHintVerticalAlignTypeName = "HintServiceMeow.Core.Enum.HintVerticalAlign";
    private const string HsmHintSyncSpeedTypeName = "HintServiceMeow.Core.Enum.HintSyncSpeed";
    private const string HsmCoordinateToolsTypeName = "HintServiceMeow.Core.Utilities.Tools.CoordinateTools";
    private const string LogPrefix = "[Scp181:Hints]";

    // HSM lays out each line of a multi-line hint by advancing downward by the *lower* line's
    // measured height (~0.8x its font size) plus the hint's LineHeight. A large title line followed
    // by a smaller line (e.g. <size=24> over <size=18>) therefore overlaps when LineHeight is 0.
    // Floor the line height so those collisions never happen regardless of config. Single-line hints
    // are unaffected (the +LineHeight start offset cancels the per-line subtraction), and Top-anchored
    // hints are excluded so a lane that pins its first line keeps its own calibrated spacing.
    private const float MinimumLineHeight = 12f;

    // --- Left/Right -> Center+X alignment handler ------------------------------------------------
    // HSM's TMP <align=left/right> is asymmetric and janky: <align=right> CLAMPS the block at the
    // hint-area edge and IGNORES XCoordinate, while <align=left> honors X, so the two do not mirror.
    // Center alignment + an explicit X is fully predictable: on the 1920x1080 virtual canvas an HSM
    // Center hint lands at pixel ~= CenterPixelX + PixelsPerHsmUnit * XCoordinate. So this provider
    // NEVER emits HSM Left/Right; it translates Left/Right into Center + a computed X, clamped to the
    // in-game wrap-safe pixel band so the block can never word-wrap.
    // Constants are in-game-measured 2026-06-29; see .tests/UI preview-core.js HSM header.
    private const float CenterPixelX = 960f;        // X=0 (Center) lands at screen-centre px960.
    private const float PixelsPerHsmUnit = 0.556f;  // Pixel advance per HSM X unit (measured).
    private const float WrapSafeLeftPx = 76f;       // Left edge of the TMP wrap-safe band.
    private const float WrapSafeRightPx = 1620f;    // Right edge of the TMP wrap-safe band.
    private const float HsmCanvasHalfWidth = 1200f; // HSM CoordinateTools alignment canvas half-width.

    // Strips Unity/TMP rich-text tags for the width estimator fallback (HSM measures rich text itself).
    private static readonly Regex RichTextTagPattern = new("<[^>]*>", RegexOptions.Compiled);

    private readonly HintDisplayConfig _config;
    private readonly Dictionary<(ReferenceHub Hub, string TagId), ActiveHint> _activeHints = new();
    private readonly HashSet<string> _clampLogged = new();
    private readonly object _gate = new();

    private Type? _playerDisplayType;
    private Type? _hintType;
    private Type? _abstractHintType;
    private ConstructorInfo? _hintConstructor;
    private MethodInfo? _getDisplayMethod;
    private MethodInfo? _addHintMethod;
    private MethodInfo? _removeHintMethod;
    private MethodInfo? _forceUpdateMethod;
    private PropertyInfo? _idProperty;
    private PropertyInfo? _textProperty;
    private PropertyInfo? _xCoordinateProperty;
    private PropertyInfo? _yCoordinateProperty;
    private PropertyInfo? _fontSizeProperty;
    private PropertyInfo? _lineHeightProperty;
    private PropertyInfo? _alignmentProperty;
    private PropertyInfo? _yCoordinateAlignProperty;
    private PropertyInfo? _syncSpeedProperty;
    private object? _alignmentCenter;
    private object? _verticalAlignTop;
    private object? _verticalAlignMiddle;
    private object? _verticalAlignBottom;
    private object? _syncSpeedFast;
    private Type? _coordinateToolsType;
    private object? _coordinateToolsInstance;
    private MethodInfo? _getTextWidthMethod;
    private bool _available;
    private bool _initialized;
    private bool _eventsRegistered;
    private bool _loggedUnavailable;
    private bool _loggedAvailable;
    private bool _loggedWidthFallback;
    private DateTime _nextMissingAssemblyRetryUtc = DateTime.MinValue;
    private string? _unavailableReason;

    public HsmHintDisplayProvider(HintDisplayConfig config)
    {
        _config = config;
    }

    public static bool IsHsmLoaded => AppDomain.CurrentDomain.GetAssemblies()
        .Any(static assembly => string.Equals(assembly.GetName().Name, HsmAssemblyName, StringComparison.OrdinalIgnoreCase));

    public bool RequiresPromptRefresh => false;

    public void Enable()
    {
        if (TryInitialize())
        {
            RegisterEvents();
        }
    }

    public void Disable()
    {
        UnregisterEvents();

        List<(ReferenceHub Hub, string TagId)> keys;
        lock (_gate)
        {
            keys = _activeHints.Keys.ToList();
        }

        foreach ((ReferenceHub hub, string tagId) in keys)
        {
            Remove(hub, tagId, forceUpdate: false);
        }
    }

    public void ShowNotice(Player player, string message, float duration)
    {
        Show(
            player,
            "notice",
            _config.DefaultX,
            _config.NoticeY,
            message,
            duration,
            _config.NoticeTextSize,
            HintVerticalAnchor.Middle);
    }

    public void ShowPrompt(Player player, string tagId, float y, string message, float duration)
    {
        Show(
            player,
            tagId,
            _config.DefaultX,
            y,
            message,
            duration,
            _config.PromptTextSize,
            HintVerticalAnchor.Middle);
    }

    public void ShowPrompt(Player player, string tagId, float y, string message, float duration, HintVerticalAnchor anchor)
    {
        Show(
            player,
            tagId,
            _config.DefaultX,
            y,
            message,
            duration,
            _config.PromptTextSize,
            anchor);
    }

    public void ShowPersistentPrompt(Player player, string tagId, float y, string message, HintVerticalAnchor anchor)
    {
        Show(
            player,
            tagId,
            _config.DefaultX,
            y,
            message,
            duration: null,
            _config.PromptTextSize,
            anchor);
    }

    public void ShowPrompt(Player player, string tagId, float x, float y, string message, float duration, HintHorizontalAlignment alignment, HintVerticalAnchor anchor)
    {
        Show(
            player,
            tagId,
            x,
            y,
            message,
            duration,
            _config.PromptTextSize,
            anchor,
            alignment);
    }

    public void ShowKeybindPrompt(Player player, string tagId, float y, string message, int keybindSettingId)
    {
        Show(
            player,
            tagId,
            _config.DefaultX,
            y,
            FormatKeybindMessage(message, keybindSettingId),
            duration: null,
            _config.PromptTextSize,
            HintVerticalAnchor.Middle);
    }

    public void Remove(Player player, string tagId)
    {
        if (player?.ReferenceHub == null)
        {
            return;
        }

        Remove(player.ReferenceHub, NormalizeTagId(tagId), forceUpdate: true);
    }

    public void Clear(Player player)
    {
        if (player?.ReferenceHub == null)
        {
            return;
        }

        List<string> tagIds;
        object? display = null;
        lock (_gate)
        {
            tagIds = _activeHints.Keys
                .Where(key => key.Hub == player.ReferenceHub)
                .Select(key => key.TagId)
                .ToList();
            if (tagIds.Count > 0 && _activeHints.TryGetValue((player.ReferenceHub, tagIds[0]), out ActiveHint activeHint))
            {
                display = activeHint.Display;
            }
        }

        if (tagIds.Count == 0)
        {
            return;
        }

        foreach (string tagId in tagIds)
        {
            Remove(player.ReferenceHub, tagId, forceUpdate: false);
        }

        ForceUpdate(display, useFastUpdate: true);
    }

    public bool TryInitialize(bool logResult = true)
    {
        if (_available)
        {
            LogAvailable(logResult);
            return true;
        }

        if (_initialized)
        {
            if (_unavailableReason != null && _unavailableReason.IndexOf("not loaded", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (DateTime.UtcNow >= _nextMissingAssemblyRetryUtc)
                {
                    _initialized = false;
                }
                else
                {
                    if (logResult)
                    {
                        LogUnavailable(_unavailableReason);
                    }

                    return false;
                }
            }
            else
            {
                if (logResult && _unavailableReason != null)
                {
                    LogUnavailable(_unavailableReason);
                }

                return false;
            }
        }

        _initialized = true;
        return TryInitializeHsm(logResult);
    }

    private void Show(Player player, string tagId, float x, float y, string message, float? duration, int textSize, HintVerticalAnchor anchor, HintHorizontalAlignment horizontalAlignment = HintHorizontalAlignment.Center)
    {
        if (!EnsureAvailable("show hint") || !IsDisplayable(player))
        {
            return;
        }

        if (duration.HasValue && duration.Value <= 0f)
        {
            Remove(player, tagId);
            return;
        }

        object? display = GetDisplay(player);
        if (display == null)
        {
            return;
        }

        string normalizedTagId = NormalizeTagId(tagId);
        // Translate Left/Right alignment into a Center-aligned X here; HSM only ever sees Center.
        float resolvedX = ResolveCenterX(horizontalAlignment, x, message, textSize, normalizedTagId);
        (ReferenceHub Hub, string TagId) key = (player.ReferenceHub, normalizedTagId);
        float lineHeight = anchor == HintVerticalAnchor.Top
            ? _config.LineHeight
            : Math.Max(_config.LineHeight, MinimumLineHeight);
        ActiveHint activeHint;
        bool created = false;

        lock (_gate)
        {
            if (!_activeHints.TryGetValue(key, out activeHint))
            {
                object? hint = CreateHint(normalizedTagId, resolvedX, y, message, textSize, anchor, lineHeight);
                if (hint == null)
                {
                    return;
                }

                activeHint = new ActiveHint(display, hint);
                _activeHints[key] = activeHint;
                created = true;
            }
            else
            {
                activeHint.Display = display;
                UpdateHint(activeHint.Hint, resolvedX, y, message, textSize, lineHeight);
                activeHint.CancelRemoveTimer();
            }
        }

        if (created)
        {
            InvokeHsm(() => _addHintMethod!.Invoke(display, new[] { activeHint.Hint, GroupName }));
        }

        if (duration.HasValue)
        {
            ScheduleRemoval(key, activeHint, duration.Value);
        }

        ForceUpdate(display, _config.ForceFastUpdates);
    }

    private object? CreateHint(string tagId, float x, float y, string message, int textSize, HintVerticalAnchor anchor, float lineHeight)
    {
        try
        {
            object hint = _hintConstructor!.Invoke(Array.Empty<object>());
            _idProperty!.SetValue(hint, tagId);
            // Horizontal alignment is ALWAYS Center: Left/Right are translated to a Center-aligned X by
            // ResolveCenterX before we get here, so HSM never sees its asymmetric Left/Right modes. X is
            // the offset from centre. The vertical anchor lets a lane pin its first line (Top) so a
            // growing multi-line block does not recenter.
            _alignmentProperty!.SetValue(hint, _alignmentCenter);
            _yCoordinateAlignProperty!.SetValue(hint, ResolveVerticalAlign(anchor));
            _syncSpeedProperty!.SetValue(hint, _syncSpeedFast);
            UpdateHint(hint, x, y, message, textSize, lineHeight);
            return hint;
        }
        catch (Exception ex)
        {
            Logger.Error($"{LogPrefix} HSM failed to create hint: {ex.GetBaseException().Message}");
            return null;
        }
    }

    private static object? TryParseEnum(Type enumType, string name)
    {
        try
        {
            return Enum.Parse(enumType, name);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private object? ResolveVerticalAlign(HintVerticalAnchor anchor)
    {
        return anchor switch
        {
            HintVerticalAnchor.Top => _verticalAlignTop,
            HintVerticalAnchor.Bottom => _verticalAlignBottom,
            _ => _verticalAlignMiddle,
        };
    }

    // Translates Left/Right alignment into a Center-aligned X by reproducing HSM's own
    // GetXCoordinateWithAlignment edge math, then clamps the block inside the wrap-safe band so it
    // cannot word-wrap. Center alignment returns the X unchanged (no measurement cost).
    private float ResolveCenterX(HintHorizontalAlignment alignment, float x, string message, int textSize, string tagId)
    {
        if (alignment == HintHorizontalAlignment.Center)
        {
            return x;
        }

        float width = MeasureTextWidth(message, Math.Max(6, textSize));
        float halfWidth = width / 2f;

        // HSM Left pins the block's LEFT edge at (XCoordinate - canvasHalfWidth); Right pins the
        // RIGHT edge at (XCoordinate + canvasHalfWidth). Feed the resulting centre back as a
        // Center-aligned XCoordinate so Left and Right mirror each other and honor X symmetrically.
        float centerX = alignment == HintHorizontalAlignment.Left
            ? (x - HsmCanvasHalfWidth) + halfWidth
            : (x + HsmCanvasHalfWidth) - halfWidth;

        // Clamp so neither edge of the block crosses the in-game wrap-safe pixel band (px76..1620),
        // which would word-wrap/garble the line. Band edges are expressed in HSM X units.
        float bandLeftX = (WrapSafeLeftPx - CenterPixelX) / PixelsPerHsmUnit;   // ~ -1590
        float bandRightX = (WrapSafeRightPx - CenterPixelX) / PixelsPerHsmUnit; // ~ +1187
        bool clamped = false;

        if (width >= bandRightX - bandLeftX)
        {
            // Wider than the whole band; nothing keeps both edges in. Anchor the LEFT edge and accept
            // the right overflow (a client-side TMP limit, not fixable from the server).
            centerX = bandLeftX + halfWidth;
            clamped = true;
        }
        else
        {
            if (centerX + halfWidth > bandRightX)
            {
                centerX = bandRightX - halfWidth;
                clamped = true;
            }

            if (centerX - halfWidth < bandLeftX)
            {
                centerX = bandLeftX + halfWidth;
                clamped = true;
            }
        }

        if (clamped)
        {
            LogClampedOnce(tagId);
        }

        return centerX;
    }

    // Text width in HSM canvas units (same scale as XCoordinate). Prefers HSM's own measurement
    // (CoordinateTools.GetTextWidth, which parses rich text exactly like the client) and falls back to
    // a tag-stripping per-line char-advance estimate when HSM reflection is unavailable.
    private float MeasureTextWidth(string message, int fontSize)
    {
        string text = message ?? string.Empty;
        if (_coordinateToolsInstance != null && _getTextWidthMethod != null && _alignmentCenter != null)
        {
            try
            {
                object? result = _getTextWidthMethod.Invoke(_coordinateToolsInstance, new[] { text, (object)fontSize, _alignmentCenter });
                if (result is float width)
                {
                    return width;
                }
            }
            catch (Exception ex)
            {
                if (!_loggedWidthFallback)
                {
                    _loggedWidthFallback = true;
                    Logger.Debug($"{LogPrefix} HSM GetTextWidth failed; using the width estimator. {ex.GetBaseException().Message}");
                }
            }
        }

        return EstimateTextWidth(text, fontSize);
    }

    // Fallback width estimate (HSM canvas units): strip rich-text tags, take the widest line, sum char
    // advances (ASCII / half-width ~= 0.5 * fontSize, CJK / full-width ~= 1.0 * fontSize).
    private static float EstimateTextWidth(string text, int fontSize)
    {
        string stripped = StripRichText(text);
        float max = 0f;
        foreach (string line in stripped.Split('\n'))
        {
            float width = 0f;
            foreach (char c in line)
            {
                width += IsFullWidth(c) ? fontSize : fontSize * 0.5f;
            }

            if (width > max)
            {
                max = width;
            }
        }

        return max;
    }

    private static string StripRichText(string text)
    {
        return string.IsNullOrEmpty(text) ? string.Empty : RichTextTagPattern.Replace(text, string.Empty);
    }

    private static bool IsFullWidth(char c)
    {
        int cp = c;
        return (cp >= 0x1100 && cp <= 0x115F) // Hangul Jamo
            || (cp >= 0x2E80 && cp <= 0x303E) // CJK radicals / Kangxi / CJK symbols
            || (cp >= 0x3041 && cp <= 0x33FF) // Hiragana / Katakana / CJK symbols & punctuation
            || (cp >= 0x3400 && cp <= 0x4DBF) // CJK Unified Ideographs Extension A
            || (cp >= 0x4E00 && cp <= 0x9FFF) // CJK Unified Ideographs
            || (cp >= 0xA000 && cp <= 0xA4CF) // Yi Syllables / Radicals
            || (cp >= 0xAC00 && cp <= 0xD7A3) // Hangul Syllables
            || (cp >= 0xF900 && cp <= 0xFAFF) // CJK Compatibility Ideographs
            || (cp >= 0xFE30 && cp <= 0xFE4F) // CJK Compatibility Forms
            || (cp >= 0xFF00 && cp <= 0xFF60) // Fullwidth Forms
            || (cp >= 0xFFE0 && cp <= 0xFFE6); // Fullwidth signs
    }

    private void LogClampedOnce(string tagId)
    {
        bool isNew;
        lock (_gate)
        {
            isNew = _clampLogged.Add(tagId);
        }

        if (isNew)
        {
            Logger.Debug($"{LogPrefix} Hint '{tagId}' was clamped to the wrap-safe band; its requested edge was past px{WrapSafeLeftPx}..{WrapSafeRightPx} and would word-wrap in-game.");
        }
    }

    private void UpdateHint(object hint, float x, float y, string message, int textSize, float lineHeight)
    {
        _textProperty!.SetValue(hint, message ?? string.Empty);
        _xCoordinateProperty!.SetValue(hint, x);
        _yCoordinateProperty!.SetValue(hint, y);
        _fontSizeProperty!.SetValue(hint, Math.Max(6, textSize));
        _lineHeightProperty!.SetValue(hint, Math.Max(0f, lineHeight));
    }

    private object? GetDisplay(Player player)
    {
        try
        {
            return _getDisplayMethod!.Invoke(null, new object[] { player });
        }
        catch (Exception ex)
        {
            Logger.Error($"{LogPrefix} HSM failed to get display for {player.UserId}: {ex.GetBaseException().Message}");
            return null;
        }
    }

    private void Remove(ReferenceHub hub, string tagId, bool forceUpdate)
    {
        ActiveHint? activeHint;
        lock (_gate)
        {
            if (!_activeHints.TryGetValue((hub, tagId), out activeHint))
            {
                return;
            }

            _activeHints.Remove((hub, tagId));
            activeHint.CancelRemoveTimer();
        }

        InvokeHsm(() => _removeHintMethod!.Invoke(activeHint.Display, new[] { activeHint.Hint, GroupName }));

        if (forceUpdate)
        {
            ForceUpdate(activeHint.Display, useFastUpdate: true);
        }
    }

    private void ScheduleRemoval((ReferenceHub Hub, string TagId) key, ActiveHint activeHint, float duration)
    {
        CancellationTokenSource cts = activeHint.ReplaceRemoveTimer();
        _ = RemoveAfterAsync(key, activeHint, TimeSpan.FromSeconds(duration), cts);
    }

    private async Task RemoveAfterAsync((ReferenceHub Hub, string TagId) key, ActiveHint expectedHint, TimeSpan delay, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(delay, cts.Token).ConfigureAwait(false);

            lock (_gate)
            {
                if (!_activeHints.TryGetValue(key, out ActiveHint current) || !ReferenceEquals(current, expectedHint))
                {
                    return;
                }
            }

            if (cts.IsCancellationRequested)
            {
                return;
            }

            Remove(key.Hub, key.TagId, forceUpdate: true);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    private void ForceUpdate(object? display, bool useFastUpdate)
    {
        if (!_config.ForceFastUpdates || display == null)
        {
            return;
        }

        InvokeHsm(() => _forceUpdateMethod!.Invoke(display, new object[] { useFastUpdate }));
    }

    private bool TryInitializeHsm(bool logResult)
    {
        Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(static asm => string.Equals(asm.GetName().Name, HsmAssemblyName, StringComparison.OrdinalIgnoreCase));

        if (assembly == null)
        {
            _nextMissingAssemblyRetryUtc = DateTime.UtcNow.AddSeconds(2);
            return FailInitialize("HintServiceMeow.dll is not loaded. This plugin will not display HSM hints.", logResult);
        }

        // Reflection against a present-but-incompatible HSM build (wrong version / failed-to-load
        // assembly) can throw (TypeLoadException, ReflectionTypeLoadException, etc.). Catch it and
        // fall back gracefully instead of letting it bubble up and crash the whole plugin's Enable.
        try
        {
            _playerDisplayType = assembly.GetType(HsmPlayerDisplayTypeName);
            _hintType = assembly.GetType(HsmHintTypeName);
            _abstractHintType = assembly.GetType(HsmAbstractHintTypeName);
            Type? alignmentType = assembly.GetType(HsmHintAlignmentTypeName);
            Type? verticalAlignType = assembly.GetType(HsmHintVerticalAlignTypeName);
            Type? syncSpeedType = assembly.GetType(HsmHintSyncSpeedTypeName);

            if (_playerDisplayType == null || _hintType == null || _abstractHintType == null ||
                alignmentType == null || verticalAlignType == null || syncSpeedType == null)
            {
                return FailInitialize("HintServiceMeow is loaded but required HSM API types were not found.", logResult);
            }

            _hintConstructor = _hintType.GetConstructor(Type.EmptyTypes);
            _getDisplayMethod = _playerDisplayType.GetMethod("Get", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Player) }, null);
            _addHintMethod = _playerDisplayType.GetMethod("AddHint", BindingFlags.Public | BindingFlags.Instance, null, new[] { _abstractHintType, typeof(string) }, null);
            _removeHintMethod = _playerDisplayType.GetMethod("RemoveHint", BindingFlags.Public | BindingFlags.Instance, null, new[] { _abstractHintType, typeof(string) }, null);
            _forceUpdateMethod = _playerDisplayType.GetMethod("ForceUpdate", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(bool) }, null);
            _idProperty = _abstractHintType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
            _textProperty = _abstractHintType.GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
            _fontSizeProperty = _abstractHintType.GetProperty("FontSize", BindingFlags.Public | BindingFlags.Instance);
            _lineHeightProperty = _abstractHintType.GetProperty("LineHeight", BindingFlags.Public | BindingFlags.Instance);
            _xCoordinateProperty = _hintType.GetProperty("XCoordinate", BindingFlags.Public | BindingFlags.Instance);
            _yCoordinateProperty = _hintType.GetProperty("YCoordinate", BindingFlags.Public | BindingFlags.Instance);
            _alignmentProperty = _hintType.GetProperty("Alignment", BindingFlags.Public | BindingFlags.Instance);
            _yCoordinateAlignProperty = _hintType.GetProperty("YCoordinateAlign", BindingFlags.Public | BindingFlags.Instance);
            _syncSpeedProperty = _abstractHintType.GetProperty("SyncSpeed", BindingFlags.Public | BindingFlags.Instance);
            // HSM only ever sees Center: Left/Right are translated to a Center-aligned X by
            // ResolveCenterX, so the asymmetric HSM Left/Right enum values are never used here.
            _alignmentCenter = Enum.Parse(alignmentType, "Center");
            _verticalAlignMiddle = Enum.Parse(verticalAlignType, "Middle");
            // Top/Bottom are standard HSM values, but parse them defensively so a build that
            // somehow lacks them degrades to Middle (the prior behavior) instead of throwing
            // out of initialization and disabling every hint this plugin shows.
            _verticalAlignTop = TryParseEnum(verticalAlignType, "Top") ?? _verticalAlignMiddle;
            _verticalAlignBottom = TryParseEnum(verticalAlignType, "Bottom") ?? _verticalAlignMiddle;
            _syncSpeedFast = Enum.Parse(syncSpeedType, "Fast");

            if (_hintConstructor == null || _getDisplayMethod == null || _addHintMethod == null ||
                _removeHintMethod == null || _forceUpdateMethod == null || _idProperty == null ||
                _textProperty == null || _fontSizeProperty == null || _lineHeightProperty == null ||
                _xCoordinateProperty == null || _yCoordinateProperty == null || _alignmentProperty == null ||
                _yCoordinateAlignProperty == null || _syncSpeedProperty == null)
            {
                return FailInitialize("HintServiceMeow is loaded but required HSM members were not found.", logResult);
            }

            // Optional: HSM CoordinateTools gives an exact rich-text width measurement for the
            // Left/Right -> Center+X handler. It is best-effort -- a failure here must NOT disable
            // hints, so it gets its own try/catch and falls back to the built-in width estimator.
            try
            {
                _coordinateToolsType = assembly.GetType(HsmCoordinateToolsTypeName);
                if (_coordinateToolsType != null)
                {
                    ConstructorInfo? coordinateToolsCtor = _coordinateToolsType
                        .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault();
                    if (coordinateToolsCtor != null)
                    {
                        // ctor is CoordinateTools(IPool<RichTextParser> = null); pass null so it uses
                        // the shared RichTextParser pool.
                        _coordinateToolsInstance = coordinateToolsCtor.GetParameters().Length == 0
                            ? coordinateToolsCtor.Invoke(Array.Empty<object>())
                            : coordinateToolsCtor.Invoke(new object?[] { null });
                    }

                    _getTextWidthMethod = _coordinateToolsType
                        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(static mi => mi.Name == "GetTextWidth"
                            && mi.GetParameters().Length >= 2
                            && mi.GetParameters()[0].ParameterType == typeof(string)
                            && mi.GetParameters()[1].ParameterType == typeof(int));
                }
            }
            catch (Exception ex)
            {
                _coordinateToolsType = null;
                _coordinateToolsInstance = null;
                _getTextWidthMethod = null;
                Logger.Debug($"{LogPrefix} HSM CoordinateTools reflection unavailable; the width estimator will be used. {ex.GetBaseException().Message}");
            }
        }
        catch (Exception ex)
        {
            return FailInitialize($"HintServiceMeow is loaded but incompatible; falling back. {ex.GetBaseException().Message}", logResult);
        }

        _available = true;
        _unavailableReason = null;
        LogAvailable(logResult);
        return true;
    }

    private bool EnsureAvailable(string action)
    {
        if (TryInitialize())
        {
            RegisterEvents();
            return true;
        }

        LogUnavailable($"Cannot {action}: HSM is not available.");
        return false;
    }

    private bool FailInitialize(string reason, bool logResult)
    {
        _unavailableReason = reason;
        if (logResult)
        {
            LogUnavailable(reason);
        }

        return false;
    }

    private void LogUnavailable(string message)
    {
        if (_loggedUnavailable)
        {
            return;
        }

        _loggedUnavailable = true;
        Logger.Error($"{LogPrefix} {message}");
    }

    private void LogAvailable(bool logResult)
    {
        if (!logResult || _loggedAvailable)
        {
            return;
        }

        _loggedAvailable = true;
        Logger.Info($"{LogPrefix} HintServiceMeow detected; hints will use HSM.");
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

    private void OnPlayerLeft(PlayerLeftEventArgs ev)
    {
        Clear(ev.Player);
    }

    private string NormalizeTagId(string tagId)
    {
        string prefix = string.IsNullOrWhiteSpace(_config.TagPrefix) ? "scp181." : _config.TagPrefix;
        return tagId.StartsWith(prefix, StringComparison.Ordinal) ? tagId : prefix + tagId;
    }

    private string GroupName => string.IsNullOrWhiteSpace(_config.GroupName) ? "scp181.hints" : _config.GroupName;

    private string FormatKeybindMessage(string message, int keybindSettingId)
    {
        string token = string.Format(_config.KeybindTokenFormat ?? "[key:{0}]", keybindSettingId);
        if (string.IsNullOrWhiteSpace(message))
        {
            return token;
        }

        return message.Contains("{key}") ? message.Replace("{key}", token) : $"{token} {message}";
    }

    private static bool IsDisplayable(Player? player)
    {
        return player?.ReferenceHub != null && (player.IsDummy || (player.IsPlayer && player.IsReady));
    }

    private static bool InvokeHsm(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"{LogPrefix} HSM invocation failed: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private sealed class ActiveHint
    {
        private CancellationTokenSource? _removeTimer;

        public ActiveHint(object display, object hint)
        {
            Display = display;
            Hint = hint;
        }

        public object Display { get; set; }

        public object Hint { get; }

        public CancellationTokenSource ReplaceRemoveTimer()
        {
            CancelRemoveTimer();
            _removeTimer = new CancellationTokenSource();
            return _removeTimer;
        }

        public void CancelRemoveTimer()
        {
            if (_removeTimer == null)
            {
                return;
            }

            _removeTimer.Cancel();
            _removeTimer = null;
        }
    }
}
