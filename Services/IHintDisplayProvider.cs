using LabApi.Features.Wrappers;

namespace Scp181.Services;

/// <summary>
/// Which edge of a hint's text block its Y coordinate refers to (mirrors HSM's
/// HintVerticalAlign). Top pins the first line so a growing multi-line block stays
/// anchored instead of recentering as its height changes.
/// </summary>
internal enum HintVerticalAnchor
{
    Top,
    Middle,
    Bottom,
}

/// <summary>
/// Horizontal placement of a hint. The provider NEVER emits HSM's TMP Left/Right alignment (which is
/// asymmetric: Right clamps at the hint-area edge and ignores X, while Left honors X). Instead it
/// translates Left/Right into Center + a computed X, clamped to the in-game wrap-safe band so the text
/// cannot word-wrap. Center keeps the hint in the middle band (X = offset from centre, screen-centre at
/// X=0). Left/Right pin the corresponding edge of the block: with X=0, Right places the block's right
/// edge at the band edge (~px1620) and Left places its left edge at ~px76; a negative X insets a Right
/// block leftward, a positive X insets a Left block rightward, and pushing past the band clamps to it.
/// This is symmetric and predictable. See <see cref="IHintDisplayProvider.ShowPrompt(Player, string,
/// float, float, string, float, HintHorizontalAlignment, HintVerticalAnchor)"/>.
/// </summary>
internal enum HintHorizontalAlignment
{
    Left,
    Center,
    Right,
}

internal interface IHintDisplayProvider
{
    bool RequiresPromptRefresh { get; }

    void Enable();

    void Disable();

    void ShowNotice(Player player, string message, float duration);

    void ShowPrompt(Player player, string tagId, float y, string message, float duration);

    /// <summary>Shows a positioned hint anchored to the given edge of its text block.</summary>
    void ShowPrompt(Player player, string tagId, float y, string message, float duration, HintVerticalAnchor anchor);

    /// <summary>
    /// Shows a positioned hint with no expiry timer. It stays until <see cref="Remove"/> or
    /// <see cref="Clear"/> takes it down, so a lane that owns persistent text does not have to
    /// re-send it on a timer. Providers that cannot hold a hint open (vanilla) fall back to a
    /// throttled re-send and report <see cref="RequiresPromptRefresh"/>.
    /// </summary>
    void ShowPersistentPrompt(Player player, string tagId, float y, string message, HintVerticalAnchor anchor);

    /// <summary>
    /// Shows a positioned hint with explicit horizontal alignment + X offset, so a feature can hug a screen
    /// edge (Left/Right) instead of the centred default. The provider translates Left/Right into a
    /// Center-aligned X (HSM never sees Left/Right), clamped to the wrap-safe band so the block cannot
    /// word-wrap. <paramref name="x"/> is the alignment offset: for Right, x=0 puts the block's right edge at
    /// the band edge and a negative x insets it leftward; for Left, x=0 puts the left edge at the band edge
    /// and a positive x insets it rightward; for Center, x is the offset from screen centre.
    /// </summary>
    void ShowPrompt(Player player, string tagId, float x, float y, string message, float duration, HintHorizontalAlignment alignment, HintVerticalAnchor anchor);

    void ShowKeybindPrompt(Player player, string tagId, float y, string message, int keybindSettingId);

    void Remove(Player player, string tagId);

    void Clear(Player player);
}
