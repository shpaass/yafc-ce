using System.Numerics;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

public class MilestonesWidget : VirtualScrollList<FactorioObject> {
    public MilestonesWidget() : base(30f, new Vector2(3f, 3f), MilestoneDrawer) => data = Project.current.settings.milestones;

    private static void MilestoneDrawer(ImGui gui, FactorioObject element, int index) {
        var settings = Project.current.settings;
        bool unlocked = settings.Flags(element).HasFlags(ProjectPerItemFlags.MilestoneUnlocked);
        if (gui.BuildFactorioObjectButton(element, ButtonDisplayStyle.Milestone(unlocked ? SchemeColor.Primary : SchemeColor.None)) == Click.Left) {
            if (!unlocked) {
                var massUnlock = Milestones.Instance.GetMilestoneResult(element);
                int subIndex = 0;
                settings.SetFlag(element, ProjectPerItemFlags.MilestoneUnlocked, true);
                foreach (var milestone in settings.milestones) {
                    subIndex++;
                    if (massUnlock[subIndex]) {
                        settings.SetFlag(milestone, ProjectPerItemFlags.MilestoneUnlocked, true);
                    }
                }
            }
            else {
                settings.SetFlag(element, ProjectPerItemFlags.MilestoneUnlocked, false);
            }
        }
        if (unlocked && gui.isBuilding) {
            gui.DrawIcon(gui.lastRect, Icon.Check, SchemeColor.Error);
        }
    }
}

public class MilestonesPanel : PseudoScreen {
    private static MilestonesWidget? instance;
    private readonly MilestonesWidget milestonesWidget = new();

    public override void Build(ImGui gui) {
        instance = milestonesWidget;
        gui.spacing = 1f;
        BuildHeader(gui, LSs.Milestones);
        gui.BuildText(LSs.MilestonesHeader);
        gui.AllocateSpacing(2f);
        milestonesWidget.Build(gui);
        gui.AllocateSpacing(2f);
        gui.BuildText(LSs.MilestonesDescription, TextBlockDisplayStyle.WrappedText);
        using (gui.EnterRow()) {
            if (gui.BuildButton(LSs.MilestonesEdit, SchemeColor.Grey)) {
                MilestonesEditor.Show();
            }

            if (gui.BuildButton(LSs.MilestonesEditSettings)) {
                Close();
                PreferencesScreen.ShowProgression();
            }

            if (gui.RemainingRow().BuildButton(LSs.Done)) {
                Close();
            }
        }
    }

    protected override void ReturnPressed() => Close();

    internal static new void Rebuild() => instance?.RebuildContents();
}
