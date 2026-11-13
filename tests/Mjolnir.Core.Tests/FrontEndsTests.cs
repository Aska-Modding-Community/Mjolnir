using System;
using System.IO;
using Mjolnir.Core.Mods;
using Mjolnir.Core.Tests;
using Xunit;

// This test assembly plays a front end: it references Core and declares one, as Mjolnir.SDK does.
[assembly: FrontEnd(typeof(FrontEndsTests.TestFrontEnd))]

namespace Mjolnir.Core.Tests;

/// <summary>
/// The front ends Core starts after loading the mods, so the SDK's Mods window exists with every mod turned off: found
/// in Mjolnir/core by their attribute and by referencing Core, never by name.
/// </summary>
public sealed class FrontEndsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mjolnir-frontend-tests", Guid.NewGuid().ToString("N"));

    internal sealed class TestFrontEnd : IFrontEnd
    {
        public void Start() { }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void AnAssemblyThatReferencesCoreAndDeclaresAFrontEndIsFound() =>
        Assert.Contains(typeof(TestFrontEnd), FrontEnds.Find(AppContext.BaseDirectory));

    [Fact]
    public void FilesThatAreNotFrontEndsAreSkipped()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "native.dll"), "not a PE file");
        File.Copy(typeof(Xunit.Assert).Assembly.Location, Path.Combine(_root, "xunit.assert.dll")); // .NET, but no Core reference
        Assert.Empty(FrontEnds.Find(_root));
    }
}
