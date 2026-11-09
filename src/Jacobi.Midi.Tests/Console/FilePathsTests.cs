using Jacobi.Midi.Console.Core.Files;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// The paths one console file uses to refer to another: written relative to the file that refers, with forward
/// slashes, and resolved from its directory again.
/// </summary>
public class FilePathsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "jacobi-filepaths");

    [Fact]
    public void RelativeTo_AFileInAnotherFolder_IsRelativeWithForwardSlashes()
    {
        var rig = Path.Combine(Root, "Rigs", "Studio.rig.xml");
        var dashboards = Path.Combine(Root, "Dashboards");

        Assert.Equal("../Rigs/Studio.rig.xml", FilePaths.RelativeTo(rig, dashboards));
    }

    [Fact]
    public void Resolve_WhatRelativeToWrote_GivesTheSameFile()
    {
        var rig = Path.Combine(Root, "Rigs", "Studio.rig.xml");
        var dashboards = Path.Combine(Root, "Dashboards");

        var resolved = FilePaths.Resolve(FilePaths.RelativeTo(rig, dashboards), dashboards);

        Assert.True(FilePaths.AreSame(rig, resolved));
    }

    [Fact]
    public void Resolve_WithoutADirectory_LeavesThePath()
    {
        Assert.Equal("../Rigs/Studio.rig.xml", FilePaths.Resolve("../Rigs/Studio.rig.xml", null));
    }
}
