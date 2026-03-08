using System.Linq;
using Xunit;

namespace Yafc.Model.Tests;

/// <summary>
/// Tests for the <see cref="DependencyNode"/> type hierarchy.
/// </summary>
public class DependencyNodeTests {
    /// <summary>
    /// Guards against adding a new <see cref="DependencyNode"/> concrete subtype without updating
    /// <c>DependencyNodeDrawExtensions.Draw</c> in <c>Yafc/Windows/DependencyNodeDrawExtensions.cs</c>.
    /// <para>
    /// If this test fails, a matching <c>case</c> MUST be added to the <c>switch</c> statement
    /// in <c>DependencyNodeDrawExtensions.Draw</c>; otherwise an <see cref="System.Diagnostics.UnreachableException"/>
    /// will be thrown at runtime when the new subtype is rendered.
    /// </para>
    /// </summary>
    [Fact]
    public void DependencyNode_ConcreteSubtypeCount_MatchesKnownHandledTypes() {
        // Scan the entire assembly (not just direct nested types) to catch any deeply nested
        // or non-nested concrete subtypes that would miss the depth-1 GetNestedTypes search.
        // NOTE: This test guards only the model-layer type inventory. It cannot verify that
        // DependencyNodeDrawExtensions.Draw (Yafc project) handles every subtype: a developer
        // can satisfy this test by bumping the expected count without touching Draw, which would
        // compile successfully but throw UnreachableException at runtime on the first render.
        // The UnreachableException default branch in Draw() is the runtime safety net.
        var concreteSubtypes = typeof(DependencyNode).Assembly
            .GetTypes()
            .Where(t => t.IsSealed && t.IsSubclassOf(typeof(DependencyNode)))
            .ToList();

        // If this assertion fails, add or remove a corresponding 'case' in
        // DependencyNodeDrawExtensions.Draw (Yafc/Windows/DependencyNodeDrawExtensions.cs)
        // and update this expected count.
        Assert.Equal(3, concreteSubtypes.Count);
        Assert.Contains(typeof(DependencyNode.AndNode), concreteSubtypes);
        Assert.Contains(typeof(DependencyNode.OrNode), concreteSubtypes);
        Assert.Contains(typeof(DependencyNode.ListNode), concreteSubtypes);
    }
}
