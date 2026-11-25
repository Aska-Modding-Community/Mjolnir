using System;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>
/// A mod's own settings page (<see cref="ModSettings"/>): the Mods window asks for the calling mod's builder and
/// falls back to the page it builds from the config when there is none.
/// </summary>
public sealed class ModSettingsTests : IDisposable
{
    private static readonly System.Reflection.Assembly Here = typeof(ModSettingsTests).Assembly;

    public void Dispose() => ModSettings.ClearPage();

    [Fact]
    public void No_page_by_default()
    {
        ModSettings.ClearPage();
        Assert.Null(ModSettings.PageOf(Here));
    }

    [Fact]
    public void Set_page_is_found_for_the_calling_assembly()
    {
        var content = new UiStackPanel();
        ModSettings.SetPage(() => content);

        var build = ModSettings.PageOf(Here);
        Assert.NotNull(build);
        Assert.Same(content, build!());
    }

    [Fact]
    public void The_builder_is_not_run_when_it_is_set()
    {
        int built = 0;
        ModSettings.SetPage(() =>
        {
            built++;
            return new UiStackPanel();
        });
        Assert.Equal(0, built);

        ModSettings.PageOf(Here)!();
        Assert.Equal(1, built);
    }

    [Fact]
    public void Setting_it_again_replaces_the_page()
    {
        var first = new UiStackPanel();
        var second = new UiStackPanel();
        ModSettings.SetPage(() => first);
        ModSettings.SetPage(() => second);

        Assert.Same(second, ModSettings.PageOf(Here)!());
    }

    [Fact]
    public void Clear_page_puts_the_built_page_back()
    {
        ModSettings.SetPage(() => new UiStackPanel());
        ModSettings.ClearPage();

        Assert.Null(ModSettings.PageOf(Here));
    }

    [Fact]
    public void A_null_builder_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => ModSettings.SetPage(null!));
    }
}
