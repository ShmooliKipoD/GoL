using Microsoft.Xna.Framework;

namespace GoL.Render;

/// <summary>One row of a <see cref="MenuList"/>.</summary>
/// <param name="Label">Left-hand text. For a value row this is the setting name.</param>
/// <param name="OnActivate">Invoked on Enter. Null for a row that only adjusts.</param>
/// <param name="ValueText">
/// When non-null the row renders as "Label: &lt; value &gt;" and Left/Right adjust it.
/// Read fresh every frame, so the row always shows the live value.
/// </param>
/// <param name="OnAdjust">Invoked with -1 / +1 on Left / Right.</param>
public sealed record MenuEntry(
    string Label,
    Action? OnActivate = null,
    Func<string>? ValueText = null,
    Action<int>? OnAdjust = null);

/// <summary>
/// A keyboard-driven vertical menu.
/// <para>
/// QiLight declares its menus as a private enum, a matching item count constant,
/// and a <c>string[]</c> literal inside the renderer whose order must be kept in
/// sync by hand - and then duplicates the whole thing for the pause overlay.
/// This exists so a menu is one list of data in one place: the main menu, the
/// configuration screen and the pause overlay are three instances of this type.
/// </para>
/// </summary>
public sealed class MenuList
{
    private const float BobPeriod = 0.16f;
    private const int BobRange = 3;

    private readonly List<MenuEntry> _entries;
    private int _index;

    // FlyingText bob: the selected label steps +-1px every BobPeriod, ping-ponging
    // across 0..BobRange. Carried over from QiLight - it is what makes the
    // selection read as lifted off the page rather than merely tinted.
    private float _bobTimer;
    private int _bobOffset = BobRange;
    private int _bobDirection = -1;

    public MenuList(IEnumerable<MenuEntry> entries)
    {
        _entries = entries.ToList();
        if (_entries.Count == 0)
            throw new ArgumentException("A menu needs at least one entry.", nameof(entries));
    }

    public int SelectedIndex => _index;
    public MenuEntry Selected => _entries[_index];
    public int Count => _entries.Count;

    /// <summary>Moves the cursor to the last entry. Used to arm a "Back"/"Exit" row.</summary>
    public void SelectLast() => _index = _entries.Count - 1;

    /// <summary>Handles navigation and activation. Selection wraps at both ends.</summary>
    /// <returns>True if this frame consumed an input, so the caller can skip its
    /// own handling of the same keypress.</returns>
    public bool Update(float dt, MenuInput input)
    {
        TickBob(dt);

        if (input.Up)
        {
            _index = (_index + _entries.Count - 1) % _entries.Count;
            return true;
        }

        if (input.Down)
        {
            _index = (_index + 1) % _entries.Count;
            return true;
        }

        var entry = _entries[_index];

        if (entry.OnAdjust is not null)
        {
            if (input.Left) { entry.OnAdjust(-1); return true; }
            if (input.Right) { entry.OnAdjust(+1); return true; }
        }

        if (input.Accept && entry.OnActivate is not null)
        {
            entry.OnActivate();
            return true;
        }

        return false;
    }

    private void TickBob(float dt)
    {
        _bobTimer += dt;
        if (_bobTimer <= BobPeriod) return;

        _bobOffset += _bobDirection;
        if (_bobOffset >= BobRange || _bobOffset <= 0)
            _bobDirection = -_bobDirection;
        _bobTimer = 0f;
    }

    /// <summary>Draws the list centred horizontally on <paramref name="centerX"/>,
    /// starting at <paramref name="topY"/>. Returns the Y below the last row.</summary>
    public float Draw(TextRenderer text, float centerX, float topY, float scale)
    {
        float lineHeight = text.LineHeight(scale);
        float y = topY;

        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            bool selected = i == _index;

            string label = entry.ValueText is null
                ? entry.Label
                : $"{entry.Label}: < {entry.ValueText()} >";

            var color = selected ? Palette.Accent : Palette.Ink * 0.75f;
            var bob = selected ? new Vector2(_bobOffset, _bobOffset) : Vector2.Zero;

            text.DrawCenteredShadowed(label, centerX, y, color, scale, bob);
            y += lineHeight;
        }

        return y;
    }
}
