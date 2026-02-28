using System;
using System.Collections.Generic;
using System.Numerics;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

/// <summary>
/// Extension methods for rendering a <see cref="DependencyNode"/> dependency tree using ImGui.
/// Keeps all UI/rendering logic out of the domain model layer.
/// </summary>
internal static class DependencyNodeDrawExtensions {
    /// <summary>
    /// Draws the dependency tree rooted at <paramref name="node"/> onto <paramref name="gui"/>.
    /// Uses C# type pattern matching against the public sealed node types to dispatch
    /// rendering without coupling the domain model to any UI library.
    /// </summary>
    /// <param name="node">The root of the dependency (sub-)tree to draw.</param>
    /// <param name="gui">The drawing destination.</param>
    /// <param name="builder">A delegate that will draw the passed dependency information onto the passed <see cref="ImGui"/>.</param>
    public static void Draw(this DependencyNode node, ImGui gui, Action<ImGui, IReadOnlyList<FactorioId>, DependencyNode.Flags> builder) {
        switch (node) {
            case DependencyNode.AndNode andNode:
                bool previousChildWasOr = false;
                foreach (DependencyNode dependency in andNode.Children) {
                    if (dependency is DependencyNode.OrNode && previousChildWasOr) {
                        gui.AllocateSpacing(.5f);
                    }
                    dependency.Draw(gui, builder);
                    previousChildWasOr = dependency is DependencyNode.OrNode;
                }
                break;

            case DependencyNode.OrNode orNode:
                Vector2 offset = new(.4f, 0);
                using (gui.EnterGroup(new(1f, 0, 0, 0))) {
                    bool isFirst = true;
                    foreach (var dependency in orNode.Children) {
                        if (!isFirst) {
                            using (gui.EnterGroup(new(1, .25f))) {
                                gui.BuildText(LSs.DependencyOrBar, Font.productionTableHeader);
                            }
                            gui.DrawRectangle(gui.lastRect - offset, SchemeColor.GreyAlt);
                        }
                        isFirst = false;
                        dependency.Draw(gui, builder);
                    }
                }
                gui.DrawRectangle(gui.lastRect.LeftPart(.2f) + offset, SchemeColor.GreyAlt);
                break;

            case DependencyNode.ListNode listNode:
                builder(gui, listNode.Elements, listNode.NodeFlags);
                break;
        }
    }
}
