using System;
using System.Collections.Generic;
using System.Linq;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

public class PreferencesScreen : PseudoScreen {
    private static readonly PreferencesScreen Instance = new PreferencesScreen();
    private const int GENERAL_PAGE = 0, PROGRESSION_PAGE = 1;
    private readonly TabControl tabControl;

    private PreferencesScreen() => tabControl = new((LSs.PreferencesTabGeneral, DrawGeneral), (LSs.PreferencesTabProgression, DrawProgression));

    public override void Build(ImGui gui) {
        BuildHeader(gui, LSs.Preferences);
        gui.AllocateSpacing();
        tabControl.Build(gui);
        gui.AllocateSpacing();
        if (gui.BuildButton(LSs.Done)) {
            Close();
        }

        if (Project.current.preferences.justChanged) {
            MainScreen.Instance.RebuildProjectView();
        }

        if (Project.current.settings.justChanged) {
            Project.current.RecalculateDisplayPages();
        }
    }

    private static VirtualScrollList<Technology>? technologyList;

    private void DrawProgression(ImGui gui) {
        ProjectPreferences preferences = Project.current.preferences;

        ChooseObject(gui, LSs.PreferencesDefaultBelt, Database.allBelts, preferences.defaultBelt, s => {
            preferences.RecordUndo().defaultBelt = s;
            gui.Rebuild();
        });
        ChooseObject(gui, LSs.PreferencesDefaultInserter, Database.allInserters, preferences.defaultInserter, s => {
            preferences.RecordUndo().defaultInserter = s;
            gui.Rebuild();
        });

        using (gui.EnterRow()) {
            gui.BuildText(LSs.PreferencesInserterCapacity, topOffset: 0.5f);
            if (gui.BuildIntegerInput(preferences.inserterCapacity, out int newCapacity)) {
                preferences.RecordUndo().inserterCapacity = newCapacity;
            }
        }
        ChooseObjectWithNone(gui, LSs.PreferencesTargetTechnology, Database.technologies.all, preferences.targetTechnology, x => {
            preferences.RecordUndo().targetTechnology = x;
            gui.Rebuild();
        }, width: 25f);

        gui.AllocateSpacing();
        using (gui.EnterRow()) {
            gui.BuildText(LSs.PreferencesMiningProductivityBonus, topOffset: 0.5f);
            DisplayAmount amount = new(Project.current.settings.miningProductivity, UnitOfMeasure.Percent);
            if (gui.BuildFloatInput(amount, TextBoxDisplayStyle.DefaultTextInput) && amount.Value >= 0) {
                Project.current.settings.RecordUndo().miningProductivity = amount.Value;
            }
        }
        using (gui.EnterRow()) {
            gui.BuildText(LSs.PreferencesResearchSpeedBonus, topOffset: 0.5f);
            DisplayAmount amount = new(Project.current.settings.researchSpeedBonus, UnitOfMeasure.Percent);
            if (gui.BuildFloatInput(amount, TextBoxDisplayStyle.DefaultTextInput) && amount.Value >= 0) {
                Project.current.settings.RecordUndo().researchSpeedBonus = amount.Value;
            }
        }
        using (gui.EnterRow()) {
            gui.BuildText(LSs.PreferencesResearchProductivityBonus, topOffset: 0.5f);
            DisplayAmount amount = new(Project.current.settings.researchProductivity, UnitOfMeasure.Percent);
            if (gui.BuildFloatInput(amount, TextBoxDisplayStyle.DefaultTextInput) && amount.Value >= 0) {
                Project.current.settings.RecordUndo().researchProductivity = amount.Value;
            }
        }

        int count = technologyList?.data.Count ?? Database.technologies.all.Count(x => x.changeRecipeProductivity.Count != 0);
        float height = tabControl.GetRemainingContentHeight(Math.Min(count, 6) * 2.75f + 0.25f);
        if (technologyList?.height != height) {
            technologyList = new(height, new(gui.layoutRect.Width, 2.75f), DrawTechnology) {
                data = [.. Database.technologies.all
                    .Where(x => x.changeRecipeProductivity.Count != 0)
                    .OrderBy(x => x.locName)]
            };
        }

        technologyList.Build(gui);
        technologyList.RebuildContents();
    }

    private void DrawTechnology(ImGui gui, Technology tech, int _) {
        using (gui.EnterGroup(new(0, .25f, 0, .75f)))
        using (gui.EnterRow()) {
            gui.allocator = RectAllocator.LeftRow;
            gui.BuildFactorioObjectButton(tech, ButtonDisplayStyle.Default);
            gui.BuildText(LSs.PreferencesTechnologyLevel.L(tech.locName));
            int currentLevel = Project.current.settings.productivityTechnologyLevels.GetValueOrDefault(tech, 0);
            if (gui.BuildIntegerInput(currentLevel, out int newLevel) && newLevel >= 0) {
                Project.current.settings.RecordUndo().productivityTechnologyLevels[tech] = newLevel;
            }
        }
    }

    private static void DrawGeneral(ImGui gui) {
        ProjectPreferences preferences = Project.current.preferences;
        ProjectSettings settings = Project.current.settings;
        bool newValue;

        gui.BuildText(LSs.PrefsUnitOfTime, Font.subheader);
        using (gui.EnterRow()) {
            if (gui.BuildRadioButton(LSs.PrefsUnitSeconds, preferences.time == 1)) {
                preferences.RecordUndo(true).time = 1;
            }

            if (gui.BuildRadioButton(LSs.PrefsUnitMinutes, preferences.time == 60)) {
                preferences.RecordUndo(true).time = 60;
            }

            if (gui.BuildRadioButton(LSs.PrefsTimeUnitHours, preferences.time == 3600)) {
                preferences.RecordUndo(true).time = 3600;
            }

            if (gui.BuildRadioButton(LSs.PrefsTimeUnitCustom, preferences.time is not 1 and not 60 and not 3600)) {
                preferences.RecordUndo(true).time = 0;
            }

            if (gui.BuildIntegerInput(preferences.time, out int newTime)) {
                preferences.RecordUndo(true).time = newTime;
            }
        }
        gui.AllocateSpacing(1f);
        gui.BuildText(LSs.PrefsHeaderItemUnits, Font.subheader);
        BuildUnitPerTime(gui, false, preferences);
        gui.BuildText(LSs.PrefsHeaderFluidUnits, Font.subheader);
        BuildUnitPerTime(gui, true, preferences);

        using (gui.EnterRowWithHelpIcon(LSs.PrefsPollutionCostHint)) {
            gui.BuildText(LSs.PrefsPollutionCost, topOffset: 0.5f);
            DisplayAmount amount = new(settings.PollutionCostModifier, UnitOfMeasure.Percent);
            if (gui.BuildFloatInput(amount, TextBoxDisplayStyle.DefaultTextInput) && amount.Value >= 0) {
                settings.RecordUndo().PollutionCostModifier = amount.Value;
                gui.Rebuild();
            }
        }

        using (gui.EnterRowWithHelpIcon(LSs.PrefsIconScaleHint)) {
            gui.BuildText(LSs.PrefsIconScale, topOffset: 0.5f);
            DisplayAmount amount = new(preferences.iconScale, UnitOfMeasure.Percent);
            if (gui.BuildFloatInput(amount, TextBoxDisplayStyle.DefaultTextInput) && amount.Value > 0 && amount.Value <= 1) {
                preferences.RecordUndo().iconScale = amount.Value;
                gui.Rebuild();
            }
        }

        // Don't show this preference if it isn't relevant.
        // (Takes ~3ms for pY, which would concern me in the regular UI, but should be fine here.)
        if (Database.objects.all.Any(o => Milestones.Instance.GetMilestoneResult(o).PopCount() > 22)) {
            using (gui.EnterRowWithHelpIcon(LSs.PrefsMilestonesPerLineHint)) {
                gui.BuildText(LSs.PrefsMilestonesPerLine, topOffset: 0.5f);
                if (gui.BuildIntegerInput(preferences.maxMilestonesPerTooltipLine, out int newIntValue) && newIntValue >= 22) {
                    preferences.RecordUndo().maxMilestonesPerTooltipLine = newIntValue;
                    gui.Rebuild();
                }
            }
        }

        using (gui.EnterRow()) {
            gui.BuildText(LSs.PrefsReactorLayout, topOffset: 0.5f);
            if (gui.BuildTextInput(settings.reactorSizeX + LSs.PrefsReactorXYSeparator + settings.reactorSizeY, out string newSize, null, delayed: true)) {
                int px = newSize.IndexOf(LSs.PrefsReactorXYSeparator);
                if (px < 0 && int.TryParse(newSize, out int value)) {
                    settings.RecordUndo().reactorSizeX = value;
                    settings.reactorSizeY = value;
                }
                else if (int.TryParse(newSize[..px], out int sizeX) && int.TryParse(newSize[(px + 1)..], out int sizeY)) {
                    settings.RecordUndo().reactorSizeX = sizeX;
                    settings.reactorSizeY = sizeY;
                }
            }
        }

        using (gui.EnterRowWithHelpIcon(LSs.PrefsSpoilingRateHint)) {
            gui.BuildText(LSs.PrefsSpoilingRate, topOffset: 0.5f);
            DisplayAmount amount = new(settings.spoilingRate, UnitOfMeasure.Percent);
            if (gui.BuildFloatInput(amount, TextBoxDisplayStyle.DefaultTextInput)) {
                settings.RecordUndo().spoilingRate = Math.Clamp(amount.Value, .1f, 10);
                gui.Rebuild();
            }
        }

        gui.AllocateSpacing();

        if (gui.BuildCheckBox(LSs.PrefsShowInaccessibleMilestoneOverlays, preferences.showMilestoneOnInaccessible, out newValue)) {
            preferences.RecordUndo().showMilestoneOnInaccessible = newValue;
        }

        if (gui.BuildCheckBox(LSs.PrefsDarkMode, Preferences.Instance.darkMode, out newValue)) {
            Preferences.Instance.darkMode = newValue;
            Preferences.Instance.Save();
            RenderingUtils.SetColorScheme(newValue);
        }

        if (gui.BuildCheckBox(LSs.PrefsAutosave, Preferences.Instance.autosaveEnabled, out newValue)) {
            Preferences.Instance.autosaveEnabled = newValue;
        }
    }

    protected override void ReturnPressed() => Close();

    /// <summary>Add a GUI element that opens a popup to allow the user to choose from the <paramref name="list"/>, which triggers <paramref name="selectItem"/>.</summary>
    /// <param name="text">Label to show.</param>
    /// <param name="width">Width of the popup. Make sure it is wide enough to fit text!</param>
    private static void ChooseObject<T>(ImGui gui, string text, T[] list, T? current, Action<T> selectItem, float width = 20f) where T : FactorioObject {
        using (gui.EnterRow()) {
            gui.BuildText(text, topOffset: 0.5f);
            if (gui.BuildFactorioObjectButtonWithText(current) == Click.Left) {
                gui.BuildObjectSelectDropDown(list, selectItem, new(text), width);
            }
        }
    }

    /// <summary>Add a GUI element that opens a popup to allow the user to choose from the <paramref name="list"/>, which triggers <paramref name="selectItem"/>.
    /// An additional "clear" or "none" option will also be displayed.</summary>
    /// <param name="text">Label to show.</param>
    /// <param name="width">Width of the popup. Make sure it is wide enough to fit text!</param>
    private static void ChooseObjectWithNone<T>(ImGui gui, string text, T[] list, T? current, Action<T?> selectItem, float width = 20f) where T : FactorioObject {
        using (gui.EnterRow()) {
            gui.BuildText(text, topOffset: 0.5f);
            if (gui.BuildFactorioObjectButtonWithText(current) == Click.Left) {
                gui.BuildObjectSelectDropDownWithNone(list, selectItem, new(text), width);
            }
        }
    }

    private static void BuildUnitPerTime(ImGui gui, bool fluid, ProjectPreferences preferences) {
        DisplayAmount unit = fluid ? preferences.fluidUnit : preferences.itemUnit;
        if (gui.BuildRadioButton(LSs.PrefsGoodsUnitSimple.L(preferences.GetPerTimeUnit().suffix), unit == 0f)) {
            unit = 0f;
        }

        using (gui.EnterRow()) {
            if (gui.BuildRadioButton(LSs.PrefsGoodsUnitCustom, unit != 0f)) {
                unit = 1f;
            }

            gui.AllocateSpacing();
            gui.allocator = RectAllocator.RightRow;
            if (!fluid) {
                if (gui.BuildButton(LSs.PrefsGoodsUnitFromBelt)) {
                    gui.BuildObjectSelectDropDown(Database.allBelts, setBelt => {
                        _ = preferences.RecordUndo(true);
                        preferences.itemUnit = setBelt.beltItemsPerSecond;
                    }, new(LSs.PrefsSelectBelt, DataUtils.DefaultOrdering, ExtraText: b => DataUtils.FormatAmount(b.beltItemsPerSecond, UnitOfMeasure.PerSecond)));
                }
            }
            gui.BuildText(LSs.PerSecondSuffixLong);
            _ = gui.BuildFloatInput(unit, TextBoxDisplayStyle.DefaultTextInput);
        }
        gui.AllocateSpacing(1f);

        if (fluid) {
            if (preferences.fluidUnit != unit.Value) {
                preferences.RecordUndo(true).fluidUnit = unit.Value;
            }
        }
        else {
            if (preferences.itemUnit != unit.Value) {
                preferences.RecordUndo(true).itemUnit = unit.Value;
            }
        }
    }

    public static void ShowProgression() {
        Instance.tabControl.SetActivePage(PROGRESSION_PAGE);
        _ = MainScreen.Instance.ShowPseudoScreen(Instance);
    }
    public static void ShowGeneral() {
        Instance.tabControl.SetActivePage(GENERAL_PAGE);
        _ = MainScreen.Instance.ShowPseudoScreen(Instance);
    }
    public static void ShowPreviousState() => _ = MainScreen.Instance.ShowPseudoScreen(Instance);
}
