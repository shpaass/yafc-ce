using System.Collections.Generic;
using System.Linq;
using SDL2;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

public class DependencyExplorer : PseudoScreen {
    private readonly ScrollArea dependencies;
    private readonly ScrollArea dependents;
    private static readonly Padding listPad = new Padding(0.5f);

    private readonly List<FactorioObject> history = [];
    private FactorioObject current;

    private static readonly Dictionary<DependencyNode.Flags, (string name, string missingText)> dependencyListTexts = new Dictionary<DependencyNode.Flags, (string, string)>()
    {
        {DependencyNode.Flags.Fuel, (LSs.DependencyFuel, LSs.DependencyFuelMissing)},
        {DependencyNode.Flags.Ingredient, (LSs.DependencyIngredient, LSs.DependencyIngredientMissing)},
        {DependencyNode.Flags.IngredientVariant, (LSs.DependencyIngredient, LSs.DependencyIngredientVariantsMissing)},
        {DependencyNode.Flags.CraftingEntity, (LSs.DependencyCrafter, LSs.DependencyCrafterMissing)},
        {DependencyNode.Flags.Source, (LSs.DependencySource, LSs.DependencySourcesMissing)},
        {DependencyNode.Flags.TechnologyUnlock, (LSs.DependencyTechnology, LSs.DependencyTechnologyMissing)},
        {DependencyNode.Flags.TechnologyPrerequisites, (LSs.DependencyTechnology, LSs.DependencyTechnologyNoPrerequisites)},
        {DependencyNode.Flags.ItemToPlace, (LSs.DependencyItem, LSs.DependencyItemMissing)},
        {DependencyNode.Flags.SourceEntity, (LSs.DependencySource, LSs.DependencyMapSourceMissing)},
        {DependencyNode.Flags.Disabled, ("", LSs.DependencyTechnologyDisabled)},
        {DependencyNode.Flags.Location, (LSs.DependencyLocation, LSs.DependencyLocationMissing)},
    };

    public DependencyExplorer(FactorioObject current) : base(60f) {
        dependencies = new ScrollArea(30f, DrawDependencies);
        dependents = new ScrollArea(30f, DrawDependents);
        this.current = current;
    }

    public static void Show(FactorioObject target) => _ = MainScreen.Instance.ShowPseudoScreen(new DependencyExplorer(target));

    private void DrawFactorioObject(ImGui gui, FactorioId id) {
        FactorioObject obj = Database.objects[id];
        using (gui.EnterGroup(listPad, RectAllocator.LeftRow)) {
            gui.BuildFactorioObjectIcon(obj);
            string text = LSs.NameWithType.L(obj.locName, obj.type);
            gui.RemainingRow(0.5f).BuildText(text, TextBlockDisplayStyle.WrappedText with { Color = obj.IsAccessible() ? SchemeColor.BackgroundText : SchemeColor.BackgroundTextFaint });
        }
        if (gui.BuildFactorioObjectButtonBackground(gui.lastRect, obj, tooltipOptions: new() { ShowTypeInHeader = true }) == Click.Left) {
            Change(obj);
        }
    }

    private void DrawDependencies(ImGui gui) {
        gui.spacing = 0f;

        Dependencies.dependencyList[current].Draw(gui, (gui, elements, flags) => {
            if (!dependencyListTexts.TryGetValue(flags, out var dependencyType)) {
                dependencyType = (flags.ToString(), "Missing " + flags);
            }

            if (elements.Count > 0) {
                gui.AllocateSpacing(0.5f);
                if (elements.Count == 1) {
                    gui.BuildText(LSs.DependencyRequireSingle.L(dependencyType.name));
                }
                else if (flags.HasFlags(DependencyNode.Flags.RequireEverything)) {
                    gui.BuildText(LSs.DependencyRequireAll.L(dependencyType.name));
                }
                else {
                    gui.BuildText(LSs.DependencyRequireAny.L(dependencyType.name));
                }

                gui.AllocateSpacing(0.5f);
                foreach (var id in elements.OrderByDescending(x => CostAnalysis.Instance.flow[x])) {
                    DrawFactorioObject(gui, id);
                }
            }
            else {
                string text = dependencyType.missingText;
                if (Database.rootAccessible.Contains(current)) {
                    text = LSs.DependencyAccessibleAnyway.L(text);
                }
                else {
                    text = LSs.DependencyAndNotAccessible.L(text);
                }

                gui.BuildText(text, TextBlockDisplayStyle.WrappedText);
            }
        });
    }

    private void DrawDependents(ImGui gui) {
        gui.spacing = 0f;
        foreach (var reverseDependency in Dependencies.reverseDependencies[current].OrderByDescending(x => CostAnalysis.Instance.flow[x])) {
            DrawFactorioObject(gui, reverseDependency);
        }
    }

    private void SetFlag(ProjectPerItemFlags flag, bool set) {
        Project.current.settings.SetFlag(current, flag, set);
        Analysis.Do<Milestones>(Project.current);
        Rebuild();
        dependents.Rebuild();
        dependencies.Rebuild();
    }

    public override void Build(ImGui gui) {
        gui.allocator = RectAllocator.Center;
        BuildHeader(gui, LSs.DependencyExplorer);
        using (gui.EnterRow()) {
            gui.BuildText(LSs.DependencyCurrentlyInspecting, Font.subheader);
            if (gui.BuildFactorioObjectButtonWithText(current) == Click.Left) {
                SelectSingleObjectPanel.Select(Database.objects.explorable, LSs.DependencySelectSomething, Change);
            }

            gui.DrawText(gui.lastRect, LSs.DependencyClickToChangeHint, RectAlignment.MiddleRight, color: TextBlockDisplayStyle.HintText.Color);
        }
        using (gui.EnterRow()) {
            var settings = Project.current.settings;
            if (current.IsAccessible()) {
                if (current.IsAutomatable()) {
                    gui.BuildText(LSs.DependencyAutomatable);
                }
                else {
                    gui.BuildText(LSs.DependencyAccessible);
                }

                if (settings.Flags(current).HasFlags(ProjectPerItemFlags.MarkedAccessible)) {
                    gui.BuildText(LSs.DependencyMarkedAccessible);
                    if (gui.BuildLink(LSs.DependencyClearMark)) {
                        SetFlag(ProjectPerItemFlags.MarkedAccessible, false);
                        NeverEnoughItemsPanel.Refresh();
                    }
                }
                else {
                    if (gui.BuildLink(LSs.DependencyMarkNotAccessible)) {
                        SetFlag(ProjectPerItemFlags.MarkedInaccessible, true);
                        NeverEnoughItemsPanel.Refresh();
                    }

                    if (gui.BuildLink(LSs.DependencyMarkAccessibleIgnoringMilestones)) {
                        SetFlag(ProjectPerItemFlags.MarkedAccessible, true);
                        NeverEnoughItemsPanel.Refresh();
                    }
                }
            }
            else {
                if (settings.Flags(current).HasFlags(ProjectPerItemFlags.MarkedInaccessible)) {
                    gui.BuildText(LSs.DependencyMarkedNotAccessible);
                    if (gui.BuildLink(LSs.DependencyClearMark)) {
                        SetFlag(ProjectPerItemFlags.MarkedInaccessible, false);
                        NeverEnoughItemsPanel.Refresh();
                    }
                }
                else {
                    gui.BuildText(LSs.DependencyNotAccessible);
                    if (gui.BuildLink(LSs.DependencyMarkAccessible)) {
                        SetFlag(ProjectPerItemFlags.MarkedAccessible, true);
                        NeverEnoughItemsPanel.Refresh();
                    }
                }
            }
        }
        gui.AllocateSpacing(2f);
        using var split = gui.EnterHorizontalSplit(2);
        split.Next();
        gui.BuildText(LSs.DependencyHeaderDependencies, Font.subheader);
        dependencies.Build(gui);
        split.Next();
        gui.BuildText(LSs.DependencyHeaderDependents, Font.subheader);
        dependents.Build(gui);
    }

    public void Change(FactorioObject target) {
        history.Add(current);
        if (history.Count > 100) {
            history.RemoveRange(0, 20);
        }

        current = target;
        dependents.Rebuild();
        dependencies.Rebuild();
        Rebuild();
    }

    public override bool KeyDown(SDL.SDL_Keysym key) {
        if (key.scancode == SDL.SDL_Scancode.SDL_SCANCODE_BACKSPACE && history.Count > 0) {
            var last = history[^1];
            Change(last);
            history.RemoveRange(history.Count - 2, 2);
            return true;
        }
        return base.KeyDown(key);
    }
}
