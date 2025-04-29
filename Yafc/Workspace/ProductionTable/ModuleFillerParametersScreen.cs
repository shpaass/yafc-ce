using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

public class ModuleFillerParametersScreen : PseudoScreen {
    private static readonly float ModulesMinPayback = MathF.Log(600f);
    private static readonly float ModulesMaxPayback = MathF.Log(3600f * 120f);
    private readonly ModuleFillerParameters modules;
    private readonly VirtualScrollList<KeyValuePair<EntityCrafter, BeaconOverrideConfiguration>> overrideList;

    public static void Show(ModuleFillerParameters parameters) => _ = MainScreen.Instance.ShowPseudoScreen(new ModuleFillerParametersScreen(parameters));

    private ModuleFillerParametersScreen(ModuleFillerParameters modules) {
        this.modules = modules;
        overrideList = new(11, new(3.25f, 4.5f), ListDrawer, collapsible: true) { data = [.. modules.overrideCrafterBeacons] };
    }

    /// <summary>
    /// Draw one item in the per-crafter beacon override display.
    /// </summary>
    private void ListDrawer(ImGui gui, KeyValuePair<EntityCrafter, BeaconOverrideConfiguration> element, int index) {
        (EntityCrafter crafter, BeaconOverrideConfiguration config) = element;
        DisplayAmount amount = config.beaconCount;
        GoodsWithAmountEvent click = gui.BuildFactorioObjectWithEditableAmount(crafter, amount, ButtonDisplayStyle.ProductionTableUnscaled, allowScroll: false,
            tooltipOptions: new() {
                DrawBelowHeader = gui => {
                    using (gui.EnterRow()) {
                        // Allocate the width now, but draw the text later so it can be vertically centered.
                        Rect rect = gui.AllocateTextRect(out _, LSs.AffectedByNBeacons.L(element.Value.beaconCount), TextBlockDisplayStyle.Default(SchemeColor.None));
                        gui.BuildFactorioObjectIcon(element.Value.beacon, ButtonDisplayStyle.ProductionTableUnscaled);
                        rect.Height = gui.lastRect.Height;
                        gui.DrawText(rect, LSs.AffectedByNBeacons.L(element.Value.beaconCount));
                        gui.BuildText(LSs.EachContainingNModules.L(element.Value.beacon.target.moduleSlots));
                        gui.BuildFactorioObjectIcon(element.Value.beaconModule, ButtonDisplayStyle.ProductionTableUnscaled);
                    }
                }
            });
        gui.DrawIcon(new(gui.lastRect.X, gui.lastRect.Y, 1.25f, 1.25f), config.beacon.target.icon, SchemeColor.Source);
        gui.DrawIcon(new(gui.lastRect.TopRight - new Vector2(1.25f, 0), new Vector2(1.25f, 1.25f)), config.beaconModule.target.icon, SchemeColor.Source);
        switch (click) {
            case GoodsWithAmountEvent.LeftButtonClick:
                SelectSingleObjectPanel.SelectQualityWithNone(Database.usableBeacons, LSs.SelectBeacon, selectedBeacon => {

                    if (selectedBeacon is null) {
                        _ = modules.overrideCrafterBeacons.Remove(crafter);
                    }
                    else {
                        modules.overrideCrafterBeacons[crafter] = modules.overrideCrafterBeacons[crafter] with { beacon = selectedBeacon };

                        if (!selectedBeacon.target.CanAcceptModule(modules.overrideCrafterBeacons[crafter].beaconModule)) {
                            _ = Database.GetDefaultModuleFor(selectedBeacon.target, out Module? module);
                            // null-forgiving: Anything from usableBeacons accepts at least one module.
                            modules.overrideCrafterBeacons[crafter] = modules.overrideCrafterBeacons[crafter] with { beaconModule = module!.With(Quality.MaxAccessible) };
                        }
                    }

                    overrideList.data = [.. modules.overrideCrafterBeacons];
                }, modules.overrideCrafterBeacons[crafter].beacon.quality, noneTooltip: LSs.ModuleFillerRemoveCurrentOverrideHint);
                break;
            case GoodsWithAmountEvent.RightButtonClick:
                SelectSingleObjectPanel.SelectQualityWithNone(Database.allModules.Where(m => modules.overrideCrafterBeacons[crafter].beacon.target.CanAcceptModule(m.moduleSpecification)),
                    LSs.SelectBeaconModule, selectedModule => {

                        if (selectedModule is null) {
                            _ = modules.overrideCrafterBeacons.Remove(crafter);
                        }
                        else {
                            modules.overrideCrafterBeacons[crafter] = modules.overrideCrafterBeacons[crafter] with { beaconModule = selectedModule };
                        }

                        overrideList.data = [.. modules.overrideCrafterBeacons];
                    }, modules.overrideCrafterBeacons[crafter].beaconModule.quality, noneTooltip: LSs.ModuleFillerRemoveCurrentOverrideHint);
                break;
            case GoodsWithAmountEvent.TextEditing when amount.Value >= 0:
                modules.overrideCrafterBeacons[crafter] = modules.overrideCrafterBeacons[crafter] with { beaconCount = (int)amount.Value };
                overrideList.data = [.. modules.overrideCrafterBeacons];
                break;
        }
    }

    /// <summary>
    /// Draw the slider that controls the price of the modules that are automatically selected.
    /// </summary>
    public static void BuildSimple(ImGui gui, ModuleFillerParameters modules) {
        float payback = modules.autoFillPayback;
        float modulesLog = MathUtils.LogarithmicToLinear(payback, ModulesMinPayback, ModulesMaxPayback);
        if (gui.BuildSlider(modulesLog, out float newValue)) {
            payback = MathUtils.LinearToLogarithmic(newValue, ModulesMinPayback, ModulesMaxPayback, 0f, float.MaxValue); // JSON can't handle infinities
            modules.autoFillPayback = payback;
        }

        if (payback <= 0f) {
            gui.BuildText(LSs.UseNoModules);
        }
        else if (payback >= float.MaxValue) {
            gui.BuildText(LSs.UseBestModules);
        }
        else {
            gui.BuildText(LSs.ModuleFillerPaybackEstimate.L(DataUtils.FormatTime(payback)), TextBlockDisplayStyle.WrappedText);
        }
    }

    /// <summary>
    /// Draw the full configuration panel, with all the options except the slider.
    /// </summary>
    public override void Build(ImGui gui) {
        EntityBeacon? beacon = Database.usableBeacons.FirstOrDefault();
        _ = Database.GetDefaultModuleFor(beacon, out Module? defaultBeaconModule);
        IObjectWithQuality<EntityBeacon>? defaultBeacon = beacon.With(Quality.MaxAccessible);
        IObjectWithQuality<Module>? beaconFillerModule = defaultBeaconModule.With(Quality.MaxAccessible);

        BuildHeader(gui, LSs.ModuleFillerHeaderAutofill);
        BuildSimple(gui, modules);
        if (gui.BuildCheckBox(LSs.ModuleFillerFillMiners, modules.fillMiners, out bool newFill)) {
            modules.fillMiners = newFill;
        }

        gui.AllocateSpacing();
        gui.BuildText(LSs.ModuleFillerModule, Font.subheader);
        gui.BuildText(LSs.ModuleFillerModuleHint, TextBlockDisplayStyle.WrappedText);
        if (gui.BuildFactorioObjectButtonWithText(modules.fillerModule) == Click.Left) {
            SelectSingleObjectPanel.SelectQualityWithNone(Database.allModules, LSs.ModuleFillerSelectModule, select => modules.fillerModule = select, modules.fillerModule?.quality);
        }

        gui.AllocateSpacing();
        gui.BuildText(LSs.ModuleFillerHeaderBeacons, Font.subheader);
        if (defaultBeacon is null || beaconFillerModule is null) {
            gui.BuildText(LSs.ModuleFillerNoBeacons);
        }
        else {
            if (gui.BuildFactorioObjectButtonWithText(modules.beacon) == Click.Left) {
                SelectSingleObjectPanel.SelectQualityWithNone(Database.allBeacons, LSs.SelectBeacon, select => {
                    modules.beacon = select;
                    if (modules.beaconModule != null && (modules.beacon == null || !modules.beacon.target.CanAcceptModule(modules.beaconModule))) {
                        modules.beaconModule = null;
                    }

                    gui.Rebuild();
                }, modules.beacon?.quality);
            }

            if (gui.BuildFactorioObjectButtonWithText(modules.beaconModule) == Click.Left) {
                SelectSingleObjectPanel.SelectQualityWithNone(Database.allModules.Where(x => modules.beacon?.target.CanAcceptModule(x.moduleSpecification) ?? false),
                    LSs.ModuleFillerSelectBeaconModule, select => modules.beaconModule = select, modules.beaconModule?.quality);
            }

            using (gui.EnterRow()) {
                gui.BuildText(LSs.ModuleFillerBeaconsPerBuilding);
                DisplayAmount amount = modules.beaconsPerBuilding;

                if (gui.BuildFloatInput(amount, TextBoxDisplayStyle.ModuleParametersTextInput) && (int)amount.Value > 0) {
                    modules.beaconsPerBuilding = (int)amount.Value;
                }
            }
            gui.BuildText(LSs.ModuleFillerBeaconsNotCalculated, TextBlockDisplayStyle.WrappedText);

            gui.AllocateSpacing();
            gui.BuildText(LSs.ModuleFillerOverrideBeacons, Font.subheader);

            if (modules.overrideCrafterBeacons.Count > 0) {
                using (gui.EnterGroup(new Padding(1, 0, 0, 0))) {
                    gui.BuildText(LSs.ModuleFillerOverrideBeaconsHint, TextBlockDisplayStyle.WrappedText, topOffset: -0.5f);
                }
            }
            gui.AllocateSpacing(.5f);
            overrideList.Build(gui);

            using (gui.EnterRow(allocator: RectAllocator.Center)) {
                if (gui.BuildButton(LSs.ModuleFillerAddBeaconOverride)) {
                    SelectMultiObjectPanel.Select(Database.allCrafters.Where(x => x.allowedEffects != AllowedEffects.None && !modules.overrideCrafterBeacons.ContainsKey(x)),
                        LSs.ModuleFillerSelectOverriddenCrafter,
                        crafter => {
                            modules.overrideCrafterBeacons[crafter] = new BeaconOverrideConfiguration(modules.beacon ?? defaultBeacon, modules.beaconsPerBuilding,
                                modules.beaconModule ?? beaconFillerModule);
                            overrideList.data = [.. modules.overrideCrafterBeacons];
                        });
                }
            }
        }

        if (gui.BuildButton(LSs.Done)) {
            Close();
        }

        if (Project.current.settings.justChanged) {
            Project.current.RecalculateDisplayPages();
        }
    }

    protected override void ReturnPressed() => Close();
}
