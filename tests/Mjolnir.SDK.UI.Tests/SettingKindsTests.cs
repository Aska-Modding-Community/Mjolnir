using System;
using System.Globalization;
using System.IO;
using Mjolnir.SDK.Config;
using Mjolnir.SDK.UI;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>The pure part of the Mods window's generated settings pages: which control a setting gets and the conversions.</summary>
public sealed class SettingKindsTests
{
    private enum Small { A, B, C }

    private enum Big { A, B, C, D, E, F, G }

    private static ModConfig NewConfig() =>
        ModConfig.Load(Path.Combine(Path.GetTempPath(), "mjolnir-setting-kinds", Guid.NewGuid().ToString("N") + ".toml"), "Test", _ => { });

    [Fact]
    public void EachTypeGetsItsControl()
    {
        var config = NewConfig();
        Assert.Equal(SettingKind.Toggle, SettingKinds.Of(config.Bind("S", "Bool", true)));
        Assert.Equal(SettingKind.Choice, SettingKinds.Of(config.Bind("S", "Small", Small.B)));
        Assert.Equal(SettingKind.Dropdown, SettingKinds.Of(config.Bind("S", "Big", Big.G)));
        Assert.Equal(SettingKind.Slider, SettingKinds.Of(config.Bind("S", "Ranged", 3).WithRange(0, 5)));
        Assert.Equal(SettingKind.Number, SettingKinds.Of(config.Bind("S", "Free", 1.5f)));
        Assert.Equal(SettingKind.Text, SettingKinds.Of(config.Bind("S", "Name", "x")));
    }

    [Theory]
    [InlineData(0, 5, 2)]
    [InlineData(0, 1, 2)]
    [InlineData(0, 10, 1)]
    [InlineData(0, 100, 0)]
    [InlineData(0, 1000, 0)]
    [InlineData(0, 0.05, 4)]
    public void SlidersKeepAboutTwoSignificantDigits(double min, double max, int decimals) =>
        Assert.Equal(decimals, SettingKinds.Decimals(min, max));

    [Fact]
    public void SliderPositionsBecomeTheSettingType()
    {
        Assert.Equal(1.25f, SettingKinds.FromSlider(1.2499f, typeof(float), 2));
        Assert.Equal(4, SettingKinds.FromSlider(3.6f, typeof(int), 0));
        Assert.Equal(4L, SettingKinds.FromSlider(3.6f, typeof(long), 0));
        Assert.Equal(0.5, SettingKinds.FromSlider(0.5f, typeof(double), 2));
        Assert.Equal("0.##", SettingKinds.NumberFormat(2));
        Assert.Equal("0", SettingKinds.NumberFormat(0));
    }

    [Fact]
    public void TextInputShowsNumbersLikeTheFileAndTakesBothDecimalSeparators()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
        try
        {
            Assert.Equal("1.5", SettingKinds.Show(1.5f)); // shown the way the config file writes it
            // The page's text input parses through the binding's automatic conversion.
            var parse = AutoConvert<string, float>.Converter!;
            Assert.True(parse("1.5", out float dot));
            Assert.Equal(1.5f, dot);
            Assert.True(parse("1,5", out float comma));
            Assert.Equal(1.5f, comma);
            Assert.True(AutoConvert<string, int>.Converter!(" 42 ", out int whole));
            Assert.Equal(42, whole);
            Assert.False(AutoConvert<string, int>.Converter!("abc", out _));
            Assert.False(AutoConvert<string, double>.Converter!("NaN", out _));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
