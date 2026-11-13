using System.Linq;
using Xunit;

namespace Mjolnir.Core.Tests;

/// <summary>
/// Guards the Core/SDK split: Core is a closed runtime that knows no SDK, none of the SDK's
/// modules and no game.
/// </summary>
public class CoreArchitectureTests
{
    private static readonly System.Reflection.Assembly Core = typeof(Bootstrap).Assembly;

    [Theory]
    [InlineData("Mjolnir.SDK")]
    [InlineData("Mjolnir.SDK.UI")]
    [InlineData("Mjolnir.SDK.Player")]
    [InlineData("Mjolnir.SDK.Content")]
    [InlineData("Tomlyn")]
    [InlineData("Assembly-CSharp")]
    [InlineData("SandSailorStudio")]
    [InlineData("Fusion.Runtime")]
    public void CoreDoesNotReference(string assembly) =>
        Assert.DoesNotContain(Core.GetReferencedAssemblies(), a => a.Name == assembly);

    [Fact]
    public void CoreExportsOnlyTheNativeContract() =>
        Assert.Equal(["Mjolnir.Core.Bootstrap", "Mjolnir.Core.NativeContext"],
                     Core.GetExportedTypes().Select(t => t.FullName).OrderBy(n => n));
}
