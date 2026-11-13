using System;

namespace Mjolnir.SDK.UI;

/// <summary>The pure rules of the hover card (no Unity: unit-tested).</summary>
internal static class ToolTips
{
    /// <summary>How long the pointer rests on an element before its card shows, in seconds (the game's cards come at once on its tabs).</summary>
    public const float Delay = 0.35f;

    /// <summary>A tooltip string as the card shows it: the first line its title, the rest its text (null without one).</summary>
    public static (string Title, string? Text) Split(string tip)
    {
        string text = tip.Replace("\r\n", "\n").Trim();
        int line = text.IndexOf('\n');
        if (line < 0) return (text, null);
        string rest = text[(line + 1)..].Trim();
        return (text[..line].Trim(), rest.Length > 0 ? rest : null);
    }

    /// <summary>
    /// Where the card goes, in screen pixels: right of the element, centred on it; left of it when the right side has no
    /// room. Returns the card's anchor point and whether it is on the right (its arrow points left).
    /// </summary>
    /// <param name="element">The element's screen rectangle: left, bottom, right, top.</param>
    /// <param name="card">The card's size.</param>
    /// <param name="screen">The screen's size.</param>
    /// <param name="gap">Space between the element and the card.</param>
    public static (float X, float Y, bool Right) Place((float Left, float Bottom, float Right, float Top) element, (float Width, float Height) card,
                                                       (float Width, float Height) screen, float gap)
    {
        float y = (element.Bottom + element.Top) / 2;
        y = Math.Clamp(y, card.Height / 2, Math.Max(card.Height / 2, screen.Height - card.Height / 2));
        bool right = element.Right + gap + card.Width <= screen.Width || element.Left - gap - card.Width < 0;
        return (right ? element.Right + gap : element.Left - gap, y, right);
    }
}
