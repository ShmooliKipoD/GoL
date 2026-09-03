namespace GoL.Render;

/// <summary>
/// One frame's menu-relevant input, as plain data.
/// <para>
/// Menus take this rather than a <c>KeyboardStateExtended</c> for two reasons.
/// It makes navigation, wrapping, adjustment and confirmation testable without a
/// graphics device or a window - macOS blocks synthetic keystrokes, so driving
/// the real UI from a script is not available. And it keeps the single-call
/// discipline on <c>KeyboardExtended.Update()</c> enforceable: the edge state is
/// read once, at the top of the frame, and handed down.
/// </para>
/// </summary>
public readonly record struct MenuInput(
    bool Up = false,
    bool Down = false,
    bool Left = false,
    bool Right = false,
    bool Accept = false,
    bool Cancel = false,
    bool Yes = false,
    bool No = false)
{
    public static readonly MenuInput None = new();

    public bool Any => Up || Down || Left || Right || Accept || Cancel || Yes || No;
}
