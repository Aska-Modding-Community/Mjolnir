using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Threading;
using UnityEngine;
using Xunit;
using static Mjolnir.SDK.Terrain.Tests.LandscapeTests;

namespace Mjolnir.SDK.Terrain.Tests;

/// <summary>
/// The game's levelling fields on a fake game: listed and found nearest a point; placed over an area as whole tiles and
/// refused where the world will not take one; levelled, finished and taken away; the console's field.
/// </summary>
[Collection("Terrain")]
public sealed class FieldsTests : IDisposable
{
    private readonly FakeTerrain _game = new();

    public FieldsTests()
    {
        Dispatcher.ResetForTests();
        DevConsole.ResetForTests();
        Landscape.ResetForTests();
        Landscape.Source = _game;
        DevConsole.OwnerOf = a => a == typeof(FieldsTests).Assembly ? "test.mod" : "mjolnir";
        TerrainCommands.Register();
    }

    public void Dispose()
    {
        Landscape.ResetForTests();
        DevConsole.ResetForTests();
        Dispatcher.ResetForTests();
    }

    [Fact]
    public void AFieldIsPlacedAsWholeTilesRoundTheAreasMiddle()
    {
        var field = LevellingFields.Place(LandscapeArea.Rectangle(V(10, 0, 20), 9, 5, blend: 0));
        Assert.NotNull(field);
        Assert.Equal(10f, field.Width, 3);  // 9 m is five tiles of 2 m; half a tile counts as one
        Assert.Equal(6f, field.Depth, 3);
        Assert.Equal(15, field.Tiles);
        Assert.Equal(10f, field.Center.x, 3);
        Assert.Equal(20f, field.Center.z, 3);
        Assert.Equal(0, field.LevelledTiles);
        Assert.False(field.IsLevelled);
        Assert.False(field.IsGone);
        Assert.Contains("levelling field of 10 × 6 m", field.ToString(), StringComparison.Ordinal);
        Assert.Single(LevellingFields.All);

        // A tiny area still takes one tile.
        Assert.Equal(1, LevellingFields.Place(LandscapeArea.Circle(V(0, 0, 0), 0.5f, blend: 0))?.Tiles);
    }

    [Fact]
    public void AFieldIsRefusedWhereTheWorldWillNotTakeOne()
    {
        Assert.Null(LevellingFields.Place(LandscapeArea.Rectangle(V(98, 0, 0), 8, 8))); // not all loaded
        Assert.Null(LevellingFields.Place(LandscapeArea.Rectangle(V(0, 0, 0), 48, 48))); // 576 tiles, more than the game allows
        _game.NoFields = true;
        Assert.Null(LevellingFields.Place(LandscapeArea.Rectangle(V(0, 0, 10), 8, 8)));
        _game.NoFields = false;
        _game.IsAvailable = false;
        Assert.Null(LevellingFields.Place(LandscapeArea.Rectangle(V(0, 0, 10), 8, 8)));
        Assert.Empty(LevellingFields.All);
        Assert.Null(LevellingFields.Nearest(V(0, 0, 0), 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => LevellingFields.Nearest(V(0, 0, 0), 0));
    }

    [Fact]
    public void AFieldIsLevelledFinishedAndTakenAway()
    {
        var field = LevellingFields.Place(LandscapeArea.Rectangle(V(0, 0, 10), 8, 8, blend: 0))!;
        Assert.Equal(16, field.Tiles);
        Assert.True(field.HeightDifference > 0);

        Assert.Equal(16, field.LevelNow());
        Assert.True(field.IsLevelled);
        Assert.Equal(0f, field.HeightDifference, 3);
        Assert.Equal(field.Height, Landscape.HeightAt(0, 10), 3); // the ground went to the field's height

        var second = LevellingFields.Place(LandscapeArea.Rectangle(V(20, 0, 10), 4, 4, blend: 0))!;
        Assert.Equal(2, LevellingFields.All.Count);
        Assert.Equal(20f, LevellingFields.Nearest(V(22, 0, 10), 8)!.Center.x, 3);
        Assert.True(second.Remove());
        Assert.True(second.IsGone);
        Assert.Equal(0, second.LevelNow());
        Assert.Single(LevellingFields.All);

        Assert.True(field.Finish());
        Assert.True(field.IsGone);
        Assert.Empty(LevellingFields.All);
    }

    [Fact]
    public void OffTheMainThreadNothingIsReadOrChanged()
    {
        var field = LevellingFields.Place(LandscapeArea.Rectangle(V(0, 0, 10), 8, 8, blend: 0))!;
        Dispatcher.Tick(); // this thread is the main one now
        int tiles = -1, levelled = -1;
        bool gone = false, removed = true;
        var thread = new System.Threading.Thread(() =>
        {
            tiles = field.Tiles;
            levelled = field.LevelNow();
            gone = field.IsGone;
            removed = field.Remove();
        });
        thread.Start();
        thread.Join();
        Assert.Equal(0, tiles);
        Assert.Equal(0, levelled);
        Assert.True(gone); // nothing can be told from another thread
        Assert.False(removed);
        Assert.False(field.IsGone);
        Assert.DoesNotContain(LevellingFields.All, _ => false);
    }

    [Fact]
    public void TheConsolePlacesListsLevelsAndTakesAwayAField()
    {
        DevConsole.Execute("field");
        Assert.Contains(Lines(), l => l == "No levelling fields in the loaded world.");

        DevConsole.Execute("field place");
        Assert.True(LevellingFields.All.Count == 1, string.Join(" | ", DevConsole.Lines.Select(l => l.Kind + ": " + l.Text)));
        Assert.Contains(Lines(), l => l.StartsWith("Placed a levelling field of 8 × 8 m", StringComparison.Ordinal));
        var field = Assert.Single(LevellingFields.All);
        Assert.Equal(TerrainCommands.AheadOf(TerrainCommands.FieldSize), field.Center.z, 1); // in front of the player

        DevConsole.Execute("field");
        Assert.Contains(Lines(), l => l == "1 levelling field(s):");
        DevConsole.Execute("field level");
        Assert.Contains(Lines(), l => l == "Levelled 16 of 16 tiles of the field at (0, 7.5).");
        DevConsole.Execute("field remove");
        Assert.Contains(Lines(), l => l == "The field is taken away.");
        Assert.Empty(LevellingFields.All);

        DevConsole.Execute("field level");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text.Contains("No levelling field within 32 m", StringComparison.Ordinal));
        DevConsole.Execute("field dig");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text.Contains("is nothing a field does", StringComparison.Ordinal));
    }

    private static string[] Lines() => DevConsole.Lines.Select(l => l.Text).ToArray();
}

/// <summary>A levelling field of a fake world: its tiles are levelled to the ground's height at its middle.</summary>
internal sealed class FakeField : IFieldView
{
    private readonly FakeTerrain _game;
    private readonly bool[] _levelled;

    public FakeField(FakeTerrain game, float x, float z, int tilesX, int tilesZ, float tileSize)
    {
        _game = game;
        Width = tilesX * tileSize;
        Depth = tilesZ * tileSize;
        Center = LandscapeTests.V(x + Width / 2, 0, z + Depth / 2);
        TileSize = tileSize;
        Corner = (x, z);
        TilesX = tilesX;
        _levelled = new bool[tilesX * tilesZ];
        Height = FakeTerrain.Ground(Center.x);
    }

    public (float X, float Z) Corner { get; }

    public int TilesX { get; }

    public float TileSize { get; }

    public Vector3 Center { get; }

    public float Width { get; }

    public float Depth { get; }

    public int Tiles => IsGone ? 0 : _levelled.Length;

    public int LevelledTiles => IsGone ? 0 : _levelled.Count(l => l);

    public float Height { get; }

    public float HeightDifference
    {
        get
        {
            float worst = 0;
            for (int i = 0; i < _levelled.Length; i++)
            {
                var (x, z) = Middle(i);
                worst = Math.Max(worst, Math.Abs((Landscape.TryGetHeight(x, z, out float ground) ? ground : Height) - Height));
            }
            return worst;
        }
    }

    public bool IsGone { get; private set; }

    public int LevelNow()
    {
        if (IsGone) return 0;
        for (int i = 0; i < _levelled.Length; i++)
        {
            if (_levelled[i]) continue;
            var (x, z) = Middle(i);
            // The ground of the tile goes to the field's height, as a villager's hoe levels it.
            for (float dx = -TileSize / 2; dx <= TileSize / 2; dx += 0.5f)
                for (float dz = -TileSize / 2; dz <= TileSize / 2; dz += 0.5f)
                    _game.Written[FakeTerrain.Cell(x + dx, z + dz)] = Height;
            _levelled[i] = true;
        }
        return LevelledTiles;
    }

    public bool Finish()
    {
        IsGone = true;
        _game.Fields.Remove(this);
        return true;
    }

    public bool Remove() => Finish();

    private (float X, float Z) Middle(int index) =>
        (Corner.X + (index % TilesX + 0.5f) * TileSize, Corner.Z + (index / TilesX + 0.5f) * TileSize);
}
