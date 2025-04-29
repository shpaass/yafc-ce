using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using Yafc.I18n;
using Yafc.UI;

namespace Yafc.Model;

public class Milestones : Analysis {
    public static readonly Milestones Instance = new Milestones();

    private static readonly ILogger logger = Logging.GetLogger<Milestones>();

    public FactorioObject[] currentMilestones = [];
    private Mapping<FactorioObject, Bits> milestoneResult;
    public Bits lockedMask { get; private set; } = new();
    private Project? project;

    public bool IsAccessibleWithCurrentMilestones(FactorioId obj) => (milestoneResult[obj] & lockedMask) == 1;

    public bool IsAccessibleWithCurrentMilestones(FactorioObject obj) => (milestoneResult[obj] & lockedMask) == 1;

    public bool IsAccessibleAtNextMilestone(FactorioObject obj) {
        var milestoneMask = milestoneResult[obj] & lockedMask;
        if (milestoneMask == 1) {
            return true;
        }

        if (milestoneMask[0]) {
            return false;
        }
        // TODO Always returns false -> milestoneMask is a power of 2 + 1 always has bit 0 set, as x pow 2 sets one (high) bit,
        // so the + 1 adds bit 0, which is detected by (milestoneMask & 1) != 0
        // return ((milestoneMask - 1) & (milestoneMask - 2)) == 0; // milestoneMask is a power of 2 + 1
        return false;
    }
    /// <summary>
    /// Return a copy of Bits
    /// </summary>
    public Bits GetMilestoneResult(FactorioId obj) => new Bits(milestoneResult[obj]);

    /// <summary>
    /// Return a copy of Bits
    /// </summary>
    public Bits GetMilestoneResult(FactorioObject obj) => new Bits(milestoneResult[obj]);

    private void GetLockedMaskFromProject() {
        if (project is null) {
            throw new InvalidOperationException($"{nameof(project)} must be set before calling {nameof(GetLockedMaskFromProject)}");
        }

        Bits bits = new(true); // The first bit is skipped (index is increased before the first bit is written) and always set
        int index = 0;
        foreach (var milestone in currentMilestones) {
            index++;
            bits[index] = !project.settings.Flags(milestone).HasFlags(ProjectPerItemFlags.MilestoneUnlocked);
        }
        lockedMask = bits;
    }

    private void ProjectSettingsChanged(bool visualOnly) {
        if (!visualOnly) {
            GetLockedMaskFromProject();
        }
    }

    public FactorioObject? GetHighest(FactorioObject target, bool all) {
        if (target == null) {
            return null;
        }

        var ms = milestoneResult[target];
        if (!all) {
            ms &= lockedMask;
        }

        if (ms == 0) {
            return null;
        }

        int msb = ms.HighestBitSet() - 1;

        return msb < 0 || msb >= currentMilestones.Length ? null : currentMilestones[msb];
    }

    [Flags]
    private enum ProcessingFlags : byte {
        InQueue = 1,
        Initial = 2,
        MilestoneNeedOrdering = 4,
        ForceInaccessible = 8
    }

    public override void Compute(Project project, ErrorCollector warnings) {
        if (project.settings.milestones.Count == 0) {
            FactorioObject[] milestones = [.. new List<FactorioObject>([.. Database.allSciencePacks, .. Database.locations.all])
                .Where(m => m is not Location { name: "nauvis" or "space-location-unknown" })];
            ComputeWithParameters(project, warnings, milestones, true);
        }
        else {
            ComputeWithParameters(project, warnings, [.. project.settings.milestones], false);
        }
    }

    public void ComputeWithParameters(Project project, ErrorCollector warnings, FactorioObject[] milestones, bool autoSort) {
        if (this.project == null) {
            this.project = project;
            project.settings.changed += ProjectSettingsChanged;
        }

        Stopwatch time = Stopwatch.StartNew();
        ConcurrentDictionary<FactorioId, bool[]> accessibility = [];
        const FactorioId noObject = (FactorioId)(-1);

        List<FactorioObject> sortedMilestones = [];
        HashSet<FactorioObject> markedInaccessible = [];
        foreach ((FactorioObject item, ProjectPerItemFlags flags) in project.settings.itemFlags) {
            if (flags.HasFlag(ProjectPerItemFlags.MarkedInaccessible)) {
                markedInaccessible.Add(item);
            }
        }

        // Walk the accessibility graph to find accessible items. The first walk is for basic accessibility and milestone sorting.
        logger.Information("Processing object accessibility");
        accessibility[noObject] = WalkAccessibilityGraph(project, markedInaccessible, milestones, sortedMilestones);

        // The rest of the walks prune the graph at the selected milestone, for computing milestone flags.
        // Do these in parallel; the only write operation for these is setting the dictionary value at the end.
        Parallel.ForEach(milestones, milestone => {
            logger.Information("Processing milestone {Milestone}", milestone.locName);
            HashSet<FactorioObject> pruneAt = [.. markedInaccessible.Append(milestone)];
            accessibility[milestone.id] = WalkAccessibilityGraph(project, pruneAt, [], null);
        });

        // Apply the milestone sort results, if requested.
        if (autoSort) {
            // If the user has inaccessible milestones, restore them at the end of the list.
            currentMilestones = [.. sortedMilestones.Union(milestones)];
        }
        else {
            currentMilestones = milestones;
        }

        // Turn the walk results into milestone bitmasks.
        Mapping<FactorioObject, Bits> result = Database.objects.CreateMapping<Bits>();
        foreach (FactorioObject obj in Database.objects.all.Where(o => accessibility[noObject][(int)o.id])) {
            Bits bits = new(true);

            for (int i = 0; i < currentMilestones.Length; i++) {
                FactorioObject milestone = currentMilestones[i];
                if (!accessibility[milestone.id][(int)obj.id]) {
                    bits[i + 1] = true;
                }
            }
            result[obj] = bits;
        }

        // Predict the milestone mask for inaccessible items by OR-ing the masks for their parents.
        Queue<FactorioObject> inaccessibleQueue = new(Database.objects.all.Where(o => !accessibility[noObject][(int)o.id]));
        while (inaccessibleQueue.TryDequeue(out FactorioObject? inaccessible)) {
            Bits milestoneBits = Dependencies.dependencyList[inaccessible].AggregateBits(id => result[id]);

            milestoneBits[0] = false; // Clear bit 0, since this object is inaccessible.

            if (milestoneBits != result[inaccessible]) {
                // Be sure to OR this; some inaccessible objects will otherwise flip infinitely between two different sets of milestones.
                result[inaccessible] |= milestoneBits;
                foreach (FactorioObject child in Dependencies.reverseDependencies[inaccessible].Select(id => Database.objects[id])) {
                    if (!result[child][0] && !inaccessibleQueue.Contains(child)) {
                        // Reprocess this inaccessible child to add our new bits to its mask.
                        inaccessibleQueue.Enqueue(child);
                    }
                }
            }
        }

        if (!project.settings.milestones.SequenceEqual(currentMilestones)) {
            _ = project.settings.RecordUndo();
            project.settings.milestones.Clear();
            project.settings.milestones.AddRange(currentMilestones);
        }
        GetLockedMaskFromProject();

        int accessibleObjects = accessibility[noObject].Count(x => x);
        bool hasAutomatableRocketLaunch = result[Database.objectsByTypeName["Special.launch"]] != 0;
        List<FactorioObject> milestonesNotReachable = [.. milestones.Except(sortedMilestones)];
        if (accessibleObjects < Database.objects.count / 2) {
            warnings.Error(LSs.MilestoneAnalysisMostInaccessible, ErrorSeverity.AnalysisWarning);
        }
        else if (!hasAutomatableRocketLaunch) {
            warnings.Error(LSs.MilestoneAnalysisNoRocketLaunch, ErrorSeverity.AnalysisWarning);
        }
        else if (milestonesNotReachable.Count > 0) {
            warnings.Error(LSs.MilestoneAnalysisInaccessibleMilestones.L(string.Join(LSs.ListSeparator, milestonesNotReachable.Select(x => x.locName))), ErrorSeverity.AnalysisWarning);
        }

        logger.Information("Milestones calculation finished in {ElapsedTime}ms.", time.ElapsedMilliseconds);
        milestoneResult = result;
    }

    /// <summary>
    /// Walks the accessibility graph, refusing to traverse the specified nodes, and determines what objects are accessible. If requested, also
    /// adds objects from <paramref name="milestones"/> to <paramref name="sortedMilestones"/>, based on the order they were encountered.
    /// </summary>
    /// <param name="project">The project to be analyzed, for reading <see cref="ProjectPerItemFlags.MarkedAccessible"/>.</param>
    /// <param name="pruneAt">The nodes that should be ignored when walking the graph.</param>
    /// <param name="milestones">The milestones to sort, or an empty array if milestone sorting is not desired.</param>
    /// <param name="sortedMilestones">A list that will receive the milestones in the order they were encountered. Must not be
    /// <see langword="null"/> if <paramref name="milestones"/> is not empty.
    /// </param>
    /// <returns>An array of bools, where the true values correspond to the <see cref="FactorioId"/>s of objects that can be accessed without
    /// any of the nodes in <paramref name="pruneAt"/>.</returns>
    private static bool[] WalkAccessibilityGraph(Project project, HashSet<FactorioObject> pruneAt, FactorioObject[] milestones,
        List<FactorioObject>? sortedMilestones) {

        if (milestones.Length != 0) {
            ArgumentNullException.ThrowIfNull(sortedMilestones);
        }

        // Queue the known-accessible items for graph walking.
        Queue<FactorioId> accessibleQueue = new(Database.rootAccessible.Except(pruneAt).Select(o => o.id));
        foreach ((FactorioObject obj, ProjectPerItemFlags flag) in project.settings.itemFlags) {
            if (flag.HasFlags(ProjectPerItemFlags.MarkedAccessible)) {
                accessibleQueue.Enqueue(obj.id);
            }
        }
        bool[] accessibleWithoutPruning = new bool[Database.objects.count];
        foreach (FactorioId item in accessibleQueue) {
            accessibleWithoutPruning[(int)item] = true;
        }
        bool[] prune = new bool[Database.objects.count];
        foreach (FactorioObject item in pruneAt) {
            prune[(int)item.id] = true;
        }

        while (accessibleQueue.TryDequeue(out FactorioId node)) {
            // null-forgiving: sortedMilestones is not null when milestones is not empty.
            if (milestones.Contains(Database.objects[node]) && !sortedMilestones!.Contains(Database.objects[node])) {
                // Sort milestones in the order we unlock them in the accessibility walk.
                sortedMilestones.Add(Database.objects[node]);
            }

            // Mark and queue this object's newly-accessible children.
            foreach (FactorioId child in Dependencies.reverseDependencies[node]) {
                if (!accessibleWithoutPruning[(int)child] && !prune[(int)child]
                    && Dependencies.dependencyList[child].IsAccessible(x => accessibleWithoutPruning[(int)x])) {

                    accessibleWithoutPruning[(int)child] = true;
                    accessibleQueue.Enqueue(child);
                }
            }
        }

        return accessibleWithoutPruning;
    }
}
