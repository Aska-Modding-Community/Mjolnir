using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>An element over a point in the world (Hud.AddAt) and the ids of counting notifications (Notify.Counter).</summary>
public sealed class HudMarkerTests
{
    [Fact]
    public void APointOnScreenGoesToItsPlaceOnTheCanvasFromTheMiddle()
    {
        // 1920×1080 at scale 1: the middle is (0, 0); a point at (1460, 790) is 500 right and 250 up of it.
        Assert.Equal((true, 500f, 250f), HudMarkers.Place((1460, 790, 10), (1920, 1080), 1, (0, 0)));
        // At 2560×1440 the canvas scales by 1.333: screen pixels become fewer reference pixels; the offset is in reference pixels.
        var (visible, x, y) = HudMarkers.Place((1280 + 400, 720, 5), (2560, 1440), 4f / 3, (0, 20));
        Assert.True(visible);
        Assert.Equal(300f, x, 3);
        Assert.Equal(20f, y, 3);
    }

    [Fact]
    public void APointBehindTheCameraOrFarOffTheScreenHides()
    {
        Assert.False(HudMarkers.Place((960, 540, -3), (1920, 1080), 1, (0, 0)).Visible); // behind
        Assert.False(HudMarkers.Place((960, 540, 0), (1920, 1080), 1, (0, 0)).Visible);
        Assert.True(HudMarkers.Place((-HudMarkers.Margin + 1, 540, 3), (1920, 1080), 1, (0, 0)).Visible); // just past the edge
        Assert.False(HudMarkers.Place((-HudMarkers.Margin - 1, 540, 3), (1920, 1080), 1, (0, 0)).Visible);
        Assert.False(HudMarkers.Place((960, 1080 + HudMarkers.Margin + 1, 3), (1920, 1080), 1, (0, 0)).Visible);
    }

    [Fact]
    public void ACountingNotificationsIdIsItsModsAndTheSameInEverySession()
    {
        int wood = NotifyIds.Of("MyMod", "wood");
        Assert.Equal(wood, NotifyIds.Of("MyMod", "wood"));
        Assert.NotEqual(wood, NotifyIds.Of("OtherMod", "wood"));
        Assert.NotEqual(wood, NotifyIds.Of("MyMod", "stone"));
        Assert.Equal(189480819, wood); // FNV-1a of "MyMod/wood": no per-process hash
    }
}
