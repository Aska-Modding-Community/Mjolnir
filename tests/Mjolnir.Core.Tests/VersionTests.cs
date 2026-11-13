using System;
using Mjolnir.Core.Mods;
using Xunit;

namespace Mjolnir.Core.Tests;

public class VersionTests
{
    [Theory]
    [InlineData("1", "1.0.0.0")]
    [InlineData("1.2", "1.2.0.0")]
    [InlineData("1.2.3", "1.2.3.0")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    [InlineData(" 2.0.1-beta.3 ", "2.0.1.0")]
    [InlineData("2.0.1+build7", "2.0.1.0")]
    public void ParsesAndPadsVersions(string text, string expected)
    {
        Assert.True(ModVersion.TryParse(text, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a.b")]
    [InlineData("1.2.3.4.5")]
    [InlineData("-1")]
    [InlineData("1..2")]
    public void RejectsInvalidVersions(string text) => Assert.False(ModVersion.TryParse(text, out _));

    [Theory]
    [InlineData("", "0.0.1", true)]
    [InlineData("*", "9.9", true)]
    [InlineData("1.2", "1.2.0", true)]          // bare version = minimum
    [InlineData("1.2", "1.1.9", false)]
    [InlineData(">=1.2.0 <2.0.0", "1.9.9", true)]
    [InlineData(">=1.2.0 <2.0.0", "2.0.0", false)]
    [InlineData(">=1.2.0, <2", "1.2", true)]
    [InlineData(">1.0", "1.0.0.0", false)]
    [InlineData("<=1.0", "1.0.0", true)]
    [InlineData("=1.2.3", "1.2.3", true)]
    [InlineData("==1.2.3", "1.2.4", false)]
    public void RangeContains(string range, string version, bool expected)
    {
        Assert.True(ModVersion.TryParse(version, out var v));
        Assert.Equal(expected, VersionRange.Parse(range).Contains(v));
    }

    [Fact]
    public void ContainsAcceptsShortSystemVersions() =>
        Assert.True(VersionRange.Parse(">=1.0.0").Contains(new Version(1, 0)));

    [Theory]
    [InlineData(">=x")]
    [InlineData("1.0 >")]
    public void RejectsInvalidRanges(string range) => Assert.Throws<FormatException>(() => VersionRange.Parse(range));
}
