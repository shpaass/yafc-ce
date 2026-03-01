using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Yafc.Model;

/// <summary>
/// Define "dependency tree" as trees of the type "Recipe.engine-unit requires Item.iron-gear-wheel, Item.steel-plate, Item.pipe,
/// (Entity.assembly-machine-1, Entity.assembly-machine-2, OR Entity.assembly-machine-3), AND Technology.engine".<br/>
/// (Contrast this with "accessibility graph", which is the complete graph created by merging all the dependency trees and adding
/// <see cref="Database.rootAccessible"/>.)<br/>
/// This represents one node in a dependency tree, which may be the root node of the tree or the child of another node.
/// </summary>
public abstract class DependencyNode {
    [Flags]
    public enum Flags {
        RequireEverything = 0x100,
        OneTimeInvestment = 0x200,

        Ingredient = 1 | RequireEverything,
        CraftingEntity = 2 | OneTimeInvestment,
        SourceEntity = 3 | OneTimeInvestment,
        TechnologyUnlock = 4 | OneTimeInvestment,
        Source = 5,
        Fuel = 6,
        ItemToPlace = 7,
        TechnologyPrerequisites = 8 | RequireEverything | OneTimeInvestment,
        IngredientVariant = 9,
        Disabled = 10,
        Location = 11 | OneTimeInvestment,
    }

    private DependencyNode() { } // Enforces that all concrete subtypes must be nested classes in this file.

    /// <summary>
    /// Creates a <see cref="DependencyNode"/> from a list of dependencies. <paramref name="flags"/> contains the require-any/-all
    /// behavior and information about how the dependencies should be described. (e.g. "Crafter", "Ingredient", etc.)
    /// </summary>
    public static DependencyNode Create(IEnumerable<FactorioObject> elements, Flags flags) => ListNode.CreateFromObjects(elements, flags);

    /// <summary>
    /// Creates a <see cref="DependencyNode"/> that is satisfied if all of its child nodes are satisfied.
    /// This matches the old behavior for a legacy DependencyList[].
    /// </summary>
    public static DependencyNode RequireAll(IEnumerable<DependencyNode> dependencies) => AndNode.Create(dependencies);
    /// <summary>
    /// Creates a <see cref="DependencyNode"/> that is satisfied if all of its child nodes are satisfied.
    /// This matches the old behavior for a legacy DependencyList[].
    /// </summary>
    public static DependencyNode RequireAll(params DependencyNode[] dependencies) => AndNode.Create(dependencies);

    /// <summary>
    /// Creates a <see cref="DependencyNode"/> that is satisfied if any of its child nodes are satisfied. This behavior was only accessible
    /// within a single legacy DependencyList, by not setting <see cref="Flags.RequireEverything"/>.
    /// </summary>
    public static DependencyNode RequireAny(params DependencyNode[] dependencies) => OrNode.Create(dependencies);
    /// <summary>
    /// Creates a <see cref="DependencyNode"/> that is satisfied if any of its child nodes are satisfied. This behavior was only accessible
    /// within a single legacy DependencyList, by not setting <see cref="Flags.RequireEverything"/>.
    /// </summary>
    public static DependencyNode RequireAny(IEnumerable<DependencyNode> dependencies) => OrNode.Create(dependencies);

    /// <summary>
    /// Gets the sequence of all <see cref="FactorioId"/>s that exist anywhere in this dependency tree.
    /// </summary>
    internal abstract IEnumerable<FactorioId> Flatten();

    /// <summary>
    /// Gets the number of entries in this dependency tree.
    /// </summary>
    /// <returns></returns>
    public int Count() => Flatten().Count();

    /// <summary>
    /// Determines whether the object that owns this dependency tree is accessible, based on the accessibility <paramref name="isAccessible"/>
    /// returns for dependent objects, and the types of this node and its children.
    /// </summary>
    /// <param name="isAccessible">A delegate that returns <see langword="true"/> if the given <see cref="FactorioId"/> is known to be
    /// accessible, or <see langword="false"/> otherwise.</param>
    /// <returns><see langword="true"/> if the known-accessible dependent items are adequate to make the owning item accessible,
    /// or <see langword="false"/> otherwise.</returns>
    internal abstract bool IsAccessible(Func<FactorioId, bool> isAccessible);

    /// <summary>
    /// Gets the additional <see cref="Bits"/> that should be added to this tree's owner, based on the bits <paramref name="getBits"/> returns
    /// for dependent objects, and the types of this node and its children.
    /// </summary>
    /// <param name="getBits">A delegate that returns the <see cref="Bits"/> for a given <see cref="FactorioId"/>.</param>
    /// <returns>The <see cref="Bits"/> for all the dependent items, ORed together appropriately.</returns>
    internal abstract Bits AggregateBits(Func<FactorioId, Bits> getBits);

    /// <summary>
    /// Gets the new <see cref="AutomationStatus"/> for this tree's owner, based on the current value and the statuses returned by
    /// <paramref name="isAutomatable"/>.
    /// </summary>
    /// <param name="isAutomatable">A delegate that returns the <see cref="AutomationStatus"/> for a given <see cref="FactorioId"/>.</param>
    /// <param name="automationState">The initial <see cref="AutomationStatus"/>, which will not be exceeded regardless of the values returned
    /// by <paramref name="isAutomatable"/>.</param>
    /// <returns>The highest feasible <paramref name="automationState"/>, as constrained by <paramref name="isAutomatable"/> and
    /// <paramref name="automationState"/>.</returns>
    internal abstract AutomationStatus IsAutomatable(Func<FactorioId, AutomationStatus> isAutomatable, AutomationStatus automationState);

    /// <summary>
    /// A <see cref="DependencyNode"/> that requires all of its children.
    /// </summary>
    public sealed class AndNode : DependencyNode {
        private readonly DependencyNode[] dependencies;

        /// <summary>Gets the child nodes that must all be satisfied.
        /// Always contains at least two elements; single-element inputs are normalized to the child itself by the factory method.</summary>
        public IReadOnlyList<DependencyNode> Children => dependencies;

        // Do not call directly; use AndNode.Create(IEnumerable<DependencyNode>) to ensure
        // nested AndNodes are flattened and duplicates are removed.
        private AndNode(DependencyNode[] dependencies) {
            System.Diagnostics.Debug.Assert(dependencies.Length >= 2, "AndNode must have at least two children; use Create() which enforces this invariant.");
            this.dependencies = dependencies;
        }

        /// <summary>
        /// Returns a <see cref="DependencyNode"/> that requires all of the children specified in <paramref name="dependencies"/>.
        /// </summary>
        /// <param name="dependencies">The children that should all be required by the returned value.</param>
        internal static DependencyNode Create(IEnumerable<DependencyNode> dependencies) {
            List<DependencyNode> realDependencies = [];
            foreach (DependencyNode item in dependencies) {
                if (item is AndNode and) {
                    realDependencies.AddRange(and.dependencies);
                }
                else {
                    realDependencies.Add(item);
                }
            }
            // Distinct() uses reference equality; structural equality is not implemented, so only
            // the exact same DependencyNode instance will be deduplicated here.
            realDependencies = [.. realDependencies.Distinct()];

            if (realDependencies.Count == 0) {
                throw new ArgumentException("Must not join zero nodes with an 'and'. Instead, create an empty DependencyList to explain what expected dependencies are missing.", nameof(dependencies));
            }

            // A single-child And node is semantically equivalent to the child itself;
            // return it directly to keep the tree normalized and avoid redundant nesting.
            if (realDependencies.Count == 1) {
                return realDependencies[0];
            }
            return new AndNode([.. realDependencies]);
        }

        internal override IEnumerable<FactorioId> Flatten() => dependencies.SelectMany(d => d.Flatten());

        internal override bool IsAccessible(Func<FactorioId, bool> isAccessible) {
            // Use foreach instead of dependencies.All(d => d.IsAccessible(isAccessible)) to reduce allocations and increase speed.
            // Unlike ListNode, switching to for here did not significantly improve speed.
            foreach (DependencyNode item in dependencies) {
                if (!item.IsAccessible(isAccessible)) {
                    return false;
                }
            }
            return true;
        }

        internal override Bits AggregateBits(Func<FactorioId, Bits> getBits) {
            Bits result = default;
            foreach (DependencyNode item in dependencies) {
                result |= item.AggregateBits(getBits);
            }
            return result;
        }
        internal override AutomationStatus IsAutomatable(Func<FactorioId, AutomationStatus> isAutomatable, AutomationStatus automationState)
            => dependencies.Min(d => d.IsAutomatable(isAutomatable, automationState));
    }

    /// <summary>
    /// A <see cref="DependencyNode"/> that requires at least one of its children.
    /// </summary>
    public sealed class OrNode : DependencyNode {
        private readonly DependencyNode[] dependencies;

        /// <summary>Gets the child nodes of which at least one must be satisfied.
        /// Always contains at least two elements; single-element inputs are normalized to the child itself by the factory method.</summary>
        public IReadOnlyList<DependencyNode> Children => dependencies;

        // Do not call directly; use OrNode.Create(IEnumerable<DependencyNode>) to ensure
        // nested OrNodes are flattened and duplicates are removed.
        private OrNode(DependencyNode[] dependencies) {
            System.Diagnostics.Debug.Assert(dependencies.Length >= 2, "OrNode must have at least two children; use Create() which enforces this invariant.");
            this.dependencies = dependencies;
        }

        /// <summary>
        /// Returns a <see cref="DependencyNode"/> that requires at least one of the children specified in <paramref name="dependencies"/>.
        /// </summary>
        /// <param name="dependencies">The children that will satisfy the returned value if at least one of them is satisfied.</param>
        internal static DependencyNode Create(IEnumerable<DependencyNode> dependencies) {
            List<DependencyNode> realDependencies = [];
            foreach (DependencyNode item in dependencies) {
                if (item is OrNode or) {
                    realDependencies.AddRange(or.dependencies);
                }
                else {
                    realDependencies.Add(item);
                }
            }
            // Distinct() uses reference equality; structural equality is not implemented, so only
            // the exact same DependencyNode instance will be deduplicated here.
            realDependencies = [.. realDependencies.Distinct()];

            if (realDependencies.Count == 0) {
                throw new ArgumentException("Must not join zero nodes with an 'or'. Instead, create an empty DependencyList to explain what expected dependencies are missing.", nameof(dependencies));
            }

            // A single-child Or node is semantically equivalent to the child itself;
            // return it directly to keep the tree normalized and avoid redundant nesting.
            if (realDependencies.Count == 1) {
                return realDependencies[0];
            }
            return new OrNode([.. realDependencies]);
        }


        internal override IEnumerable<FactorioId> Flatten() => dependencies.SelectMany(d => d.Flatten());

        internal override bool IsAccessible(Func<FactorioId, bool> isAccessible) => dependencies.Any(d => d.IsAccessible(isAccessible));
        internal override Bits AggregateBits(Func<FactorioId, Bits> getBits) => dependencies.Min(d => d.AggregateBits(getBits));
        internal override AutomationStatus IsAutomatable(Func<FactorioId, AutomationStatus> isAutomatable, AutomationStatus automationState)
            => dependencies.Max(d => d.IsAutomatable(isAutomatable, automationState));
    }

    /// <summary>
    /// A <see cref="DependencyNode"/> that matches the behavior of a legacy <see cref="DependencyList"/>.
    /// </summary>
    public sealed class ListNode : DependencyNode {
        private readonly ReadOnlyCollection<FactorioId> elements;
        private readonly Flags flags;

        /// <summary>Gets the <see cref="FactorioId"/>s of the dependency objects in this list.
        /// Duplicate IDs are guaranteed to have been removed.</summary>
        public IReadOnlyList<FactorioId> Elements => elements;

        /// <summary>Gets the <see cref="Flags"/> that describe the require-any/-all behavior and how to describe the dependencies.</summary>
        public Flags NodeFlags => flags;

        // Do not call directly; use DependencyNode.Create() or the implicit conversion operator.
        // This internal factory delegates instantiation so the constructor can remain private
        // (consistent with AndNode/OrNode, enforcing that creation occurs via factory methods),
        // since C# outer classes cannot directly access private constructors of nested types.
        internal static ListNode CreateFromObjects(IEnumerable<FactorioObject> elements, Flags flags) => new ListNode(elements, flags);

        private ListNode(IEnumerable<FactorioObject> elements, Flags flags) {
            this.elements = elements.Select(e => e.id).Distinct().ToList().AsReadOnly();
            this.flags = flags;
        }

        internal override IEnumerable<FactorioId> Flatten() => elements;

        internal override bool IsAccessible(Func<FactorioId, bool> isAccessible) {
            // Use for instead of foreach or elements.All(isAccessible) to reduce allocations and increase speed.
            if (flags.HasFlags(Flags.RequireEverything)) {
                for (int i = 0; i < elements.Count; i++) {
                    if (!isAccessible(elements[i])) {
                        return false;
                    }
                }
                return true;
            }

            for (int i = 0; i < elements.Count; i++) {
                if (isAccessible(elements[i])) {
                    return true;
                }
            }
            return false;
        }

        internal override Bits AggregateBits(Func<FactorioId, Bits> getBits) {
            Bits bits = new();
            if (flags.HasFlags(Flags.RequireEverything)) {
                foreach (FactorioId item in elements) {
                    bits |= getBits(item);
                }
                return bits;
            }
            else if (elements.Count > 0) {
                return bits | elements.Min(getBits);
            }
            return bits;
        }

        internal override AutomationStatus IsAutomatable(Func<FactorioId, AutomationStatus> getAutomation, AutomationStatus automationState) {
            // Copied from AutomationAnalysis.cs.
            if (!flags.HasFlags(Flags.OneTimeInvestment)) {
                if (flags.HasFlags(Flags.RequireEverything)) {
                    foreach (FactorioId element in elements) {
                        AutomationStatus status = getAutomation(element);
                        if (status < automationState) {
                            automationState = status;
                        }
                    }
                }
                else {
                    AutomationStatus localHighest = AutomationStatus.NotAutomatable;

                    foreach (FactorioId element in elements) {
                        AutomationStatus status = getAutomation(element);
                        if (status > localHighest) {
                            localHighest = status;
                        }
                    }

                    if (localHighest < automationState) {
                        automationState = localHighest;
                    }
                }
            }
            else if (automationState == AutomationStatus.AutomatableNow && flags == Flags.CraftingEntity) {
                // If only character is accessible at current milestones as a crafting entity, don't count the object as currently automatable
                bool hasMachine = false;

                foreach (FactorioId element in elements) {
                    if (element != Database.character?.id && Milestones.Instance.IsAccessibleWithCurrentMilestones(element)) {
                        hasMachine = true;
                        break;
                    }
                }

                if (!hasMachine) {
                    automationState = AutomationStatus.AutomatableLater;
                }
            }
            return automationState;
        }

    }

    public static implicit operator DependencyNode((IEnumerable<FactorioObject> elements, Flags flags) value)
        => Create(value.elements, value.flags);
}
