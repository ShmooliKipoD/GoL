using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;

namespace GoL.Render;

/// <summary>
/// A modal menu drawn over a running screen.
/// <para>
/// An overlay rather than a screen, for the reason <c>GolGame.ShowScreen</c> states:
/// loading a screen <b>unloads</b> the outgoing one, so a pause menu that was a
/// screen would destroy the very arena it is pausing.
/// </para>
/// <para>
/// It is a <see cref="MenuList"/> plus a panel, because <see cref="MenuList.Draw"/>
/// paints text rows and nothing else - laid over a field of plants and sense rays,
/// unbacked pixel-font glyphs are unreadable.
/// </para>
/// </summary>
public sealed class PauseOverlay
{
    private const float TitleScale = 3f;
    private const float ItemScale = 2f;

    private readonly string _title;
    private readonly MenuList _menu;

    public PauseOverlay(string title, IEnumerable<MenuEntry> entries)
    {
        _title = title;
        _menu = new MenuList(entries);
    }

    public bool IsOpen { get; private set; }

    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;

    /// <summary>
    /// Handles input while open.
    /// <para>
    /// Returns true for <b>every</b> frame it is open, not merely the ones it acted
    /// on. That is what makes it modal, and it is load-bearing here rather than
    /// tidiness: the host screen binds A, N and Space to its own overlays, and
    /// <c>InputMap</c> maps those same keys to Left, No and Accept. A frame this
    /// swallows only partly would move the menu cursor and toggle a panel at once.
    /// </para>
    /// </summary>
    public bool Update(float dt, MenuInput input)
    {
        if (!IsOpen) return false;

        // Esc closes rather than confirming - unlike ConfirmPrompt, where the spec
        // asks for Esc to mean yes. Here there is nothing to confirm; the way out
        // is the way back in.
        if (input.Cancel)
        {
            IsOpen = false;
            return true;
        }

        _menu.Update(dt, input);
        return true;
    }

    public void Draw(SpriteBatch batch, TextRenderer text, float viewWidth, float viewHeight)
    {
        if (!IsOpen) return;

        float titleHeight = text.LineHeight(TitleScale);
        float itemHeight = text.LineHeight(ItemScale);

        float boxW = MathF.Min(viewWidth - 64f, 520f);
        float boxH = titleHeight + 24f + _menu.Count * itemHeight + 48f;

        float centerX = viewWidth / 2f;
        var box = new RectangleF(centerX - boxW / 2f, (viewHeight - boxH) / 2f, boxW, boxH);

        // Dim the whole arena first, so the panel reads as being in front of the
        // simulation rather than painted onto it.
        batch.FillRectangle(new RectangleF(0f, 0f, viewWidth, viewHeight),
            Palette.Background * 0.7f);

        batch.FillRectangle(box, Palette.Panel);
        batch.DrawRectangle(box, Palette.Accent, 2f);

        text.DrawCentered(_title, centerX, box.Y + 22f, Palette.Ink, TitleScale);
        _menu.Draw(text, centerX, box.Y + 22f + titleHeight + 24f, ItemScale);
    }
}
