using System;
using System.IO;
using Xunit;

namespace Yafc.Model.Tests;

public class ProjectTests {
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadFromFile_CanLoadWithEmptyString(bool useMostRecent)
        => Assert.NotNull(Project.ReadFromFile("", new(), useMostRecent));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadFromFile_CanLoadNonexistentFile(bool useMostRecent)
        // Assuming that there are no files named <some-guid>.yafc in the current directory.
        => Assert.NotNull(Project.ReadFromFile(Guid.NewGuid().ToString() + ".yafc", new(), useMostRecent));

    [Fact]
    // PerformAutoSave is expected to be a no-op in this case.
    // This test may need more care and feeding if autosaving is added for nameless projects.
    public void PerformAutoSave_NoThrowWhenLoadedWithEmptyString()
        // No Assert in this test; the test passes if PerformAutoSave does not throw.
        => Project.ReadFromFile("", new(), false).PerformAutoSave();

    [Fact]
    public void AutosaveInDotYafcDirectory_DoesNotModifyDirectory()
        // Use Path.Combine to generate host-native path separators. (GenerateAutosavePath coincidentally converts separators.)
        => Assert.Equal(Path.Combine("home", ".yafc", "ProjectName-autosave-1.yafc"), Project.GenerateAutosavePath("home/.yafc/ProjectName.yafc", 1));

    [Fact]
    public void AutosaveInDotYafcDirectoryWithNoExtension_DoesNotModifyDirectory()
        // Use Path.Combine to generate host-native path separators. (GenerateAutosavePath coincidentally converts separators.)
        => Assert.Equal(Path.Combine("home", ".yafc", "ProjectName-autosave-4.yafc"), Project.GenerateAutosavePath("home/.yafc/ProjectName", 4));
}
