using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;

namespace GoL.Render;

/// <summary>
/// A modal yes/no prompt drawn as an overlay by its host screen.
/// <para>
/// An overlay rather than a screen on purpose: <c>ScreenManager.LoadScreen</c>
/// unloads the outgoing screen, so anything that must not destroy the state
/// beneath it - this and the pause menu - has to be drawn in place.
/// </para>
/// <para>
/// The spec asks for a y/n prompt where Esc also confirms. This deliberately
/// differs from QiLight, which arms a three-second window and quits on a second
/// keypress; that is a different interaction, and porting it verbatim would
/// quietly not be what was asked for.
/// </para>
/// </summary>
public sealed class ConfirmPrompt
{
    private readonly string _question;
    private readonly Action _onYes;

    public ConfirmPrompt(string question, Action onYes)
    {
        _question = question;
        _onYes = onYes;
    }

    public bool IsOpen { get; private set; }

    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;

    /// <summary>Handles input while open. Returns true if the prompt consumed the
    /// frame's input, so the host screen skips its own handling.</summary>
    public bool Update(MenuInput input)
    {
        if (!IsOpen) return false;

        // Esc confirms here rather than cancelling: per the spec, Esc at this
        // prompt exits. N cancels.
        if (input.Yes || input.Cancel)
        {
            IsOpen = false;
            _onYes();
            return true;
        }

        if (input.No)
        {
            IsOpen = false;
            return true;
        }

        return true; // modal: swallow everything else while open
    }

    public void Draw(SpriteBatch batch, TextRenderer text, float centerX, float centerY)
    {
        if (!IsOpen) return;

        const float questionScale = 2f;
        const float hintScale = 2f;

        var size = text.Measure(_question, questionScale);
        float boxW = MathF.Max(size.X, 260f) + 64f;
        float boxH = 132f;
        var box = new RectangleF(centerX - boxW / 2f, centerY - boxH / 2f, boxW, boxH);

        batch.FillRectangle(box, Palette.Panel);
        batch.DrawRectangle(box, Palette.Warning, 2f);

        text.DrawCentered(_question, centerX, box.Y + 34f, Palette.Ink, questionScale);
        text.DrawCentered("Y / N", centerX, box.Y + 76f, Palette.Warning, hintScale);
    }
}
