using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

/// <summary>
/// The location(s) where <see cref="ObjectTooltip"/> should display hints
/// (currently only "ctrl+click to add recipe" hints)
/// </summary>
[Flags]
public enum HintLocations {
    /// <summary>
    /// Do not display any hints.
    /// </summary>
    None = 0,
    /// <summary>
    /// Display the ctrl+click recipe-selection hint associated with recipes that produce this <see cref="Goods"/>.
    /// </summary>
    OnProducingRecipes = 1,
    /// <summary>
    /// Display the ctrl+click recipe-selection hint associated with recipes that consume this <see cref="Goods"/>.
    /// </summary>
    OnConsumingRecipes = 2,
    // NOTE: This is [Flags]. The next item, if applicable, should be 4.
}

public class ObjectTooltip : Tooltip {
    public static readonly Padding contentPadding = new Padding(1f, 0.25f);

    public ObjectTooltip() : base(new Padding(0f, 0f, 0f, 0.5f), 25f) { }

    private IFactorioObjectWrapper target = null!; // null-forgiving: Set by SetFocus, aka ShowTooltip.
    private ObjectTooltipOptions tooltipOptions;

    private void BuildHeader(ImGui gui) {
        using (gui.EnterGroup(new Padding(1f, 0.5f), RectAllocator.LeftAlign, spacing: 0f)) {
            string name = target.text;
            if (tooltipOptions.ShowTypeInHeader && target is not Goods) {
                name = LSs.NameWithType.L(name, target.target.type);
            }

            gui.BuildText(name, new TextBlockDisplayStyle(Font.header, true));
            var milestoneMask = Milestones.Instance.GetMilestoneResult(target.target);
            if (milestoneMask.HighestBitSet() > 0 && (target.target.IsAccessible() || Project.current.preferences.showMilestoneOnInaccessible)) {
                int milestoneCount = milestoneMask.PopCount();
                if (milestoneMask[0]) {
                    milestoneCount--; // Bit 0 is accessibility, not a milestone flag.
                }

                // All rows except the last will show at least 22 milestones.
                // If displaying more items per row (up to the user's limit) reduces the number of rows, squish the milestones together slightly.
                int maxItemsPerRow = Project.current.preferences.maxMilestonesPerTooltipLine;
                const int minItemsPerRow = 22;
                int rows = (milestoneCount + maxItemsPerRow - 1) / maxItemsPerRow;
                int itemsPerRow = Math.Max((milestoneCount + rows - 1) / rows, minItemsPerRow);
                // 22.5 is the width of the available area of the tooltip. The original code used spacings from -1 (100% overlap) to 0
                // (no overlap). At the default max of 28 per row, we allow spacings of -0.196 (19.6% overlap) to 0.023 (2.3% stretch).
                float spacing = 22.5f / itemsPerRow - 1f;

                using var milestones = Milestones.Instance.currentMilestones.AsEnumerable().GetEnumerator();
                int maskBit = 1;
                for (int i = 0; i < rows; i++) {
                    using (gui.EnterRow(spacing)) {
                        // Draw itemsPerRow items, then exit this row and allocate a new one
                        for (int j = 0; j < itemsPerRow; /* increment after drawing a milestone */) {
                            if (!milestones.MoveNext()) {
                                goto doneDrawing;
                            }
                            if (milestoneMask[maskBit]) {
                                gui.BuildIcon(milestones.Current.icon, 1f, SchemeColor.Source);
                                j++;
                            }

                            maskBit++;
                        }
                    }
                }
doneDrawing:;
            }
        }

        if (gui.isBuilding) {
            gui.DrawRectangle(gui.lastRect, SchemeColor.Primary);
        }
    }

    private static void BuildSubHeader(ImGui gui, string text) {
        using (gui.EnterGroup(contentPadding)) {
            gui.BuildText(text, Font.subheader);
        }

        if (gui.isBuilding) {
            gui.DrawRectangle(gui.lastRect, SchemeColor.Grey);
        }
    }

    private static void BuildIconRow(ImGui gui, IReadOnlyList<IFactorioObjectWrapper> objects, int maxRows, IComparer<IFactorioObjectWrapper>? comparer = null) {
        const int itemsPerRow = 9;
        int count = objects.Count;
        if (count == 0) {
            gui.BuildText(LSs.TooltipNothingToList, TextBlockDisplayStyle.HintText);
            return;
        }

        List<IFactorioObjectWrapper> arr = new(count);
        arr.AddRange(objects);
        arr.Sort(comparer ?? DataUtils.DefaultOrdering);

        if (count <= maxRows) {
            for (int i = 0; i < count; i++) {
                BuildItem(gui, arr[i]);
            }

            return;
        }

        int index = 0;
        while (count - index - 2 < (maxRows - 1) * itemsPerRow && index < arr.Count) {
            BuildItem(gui, arr[index++]);
            maxRows--;
        }

        int rows = Math.Min(((count - 1 - index) / itemsPerRow) + 1, maxRows);
        for (int i = 0; i < rows; i++) {
            using (gui.EnterRow()) {
                for (int j = 0; j < itemsPerRow; j++) {
                    if (arr.Count <= index) {
                        return;
                    }

                    gui.BuildFactorioObjectIcon(arr[index++]);
                }
            }
        }

        if (index < count) {
            gui.BuildText(LSs.TooltipAndMoreInList.L((count - index)));
        }
    }

    private static void BuildItem(ImGui gui, IFactorioObjectWrapper item, string? extraText = null) {
        using (gui.EnterRow()) {
            gui.BuildFactorioObjectIcon(item.target);
            gui.BuildText(item.text + extraText, TextBlockDisplayStyle.WrappedText);
        }
    }

    protected override void BuildContents(ImGui gui) {
        BuildCommon(target.target, gui);
        Quality? targetQuality = (target as IObjectWithQuality<FactorioObject>)?.quality ?? Quality.Normal;
        switch (target.target) {
            case Technology technology:
                BuildTechnology(technology, gui);
                break;
            case Recipe recipe:
                BuildRecipe(recipe, gui);
                break;
            case Goods goods:
                BuildGoods(goods, targetQuality, gui);
                break;
            case Entity entity:
                BuildEntity(entity, targetQuality, gui);
                break;
            case Quality quality:
                BuildQuality(quality, gui);
                break;
        }
    }

    private void BuildCommon(FactorioObject target, ImGui gui) {
        BuildHeader(gui);
        using (gui.EnterGroup(contentPadding)) {
            tooltipOptions.DrawBelowHeader?.Invoke(gui);

            if (InputSystem.Instance.control) {
                gui.BuildText(target.typeDotName);
            }

            if (target.locDescr != null) {
                gui.BuildText(target.locDescr, TextBlockDisplayStyle.WrappedText);
            }

            if (!target.IsAccessible()) {
                gui.BuildText(LSs.TooltipNotAccessible.L(target.type), TextBlockDisplayStyle.WrappedText);
            }
            else if (!target.IsAutomatable()) {
                gui.BuildText(LSs.TooltipNotAutomatable.L(target.type), TextBlockDisplayStyle.WrappedText);
            }
            else {
                gui.BuildText(CostAnalysis.GetDisplayCost(target), TextBlockDisplayStyle.WrappedText);
            }

            if (target.IsAccessibleWithCurrentMilestones() && !target.IsAutomatableWithCurrentMilestones()) {
                gui.BuildText(LSs.TooltipNotAutomatableYet.L(target.type), TextBlockDisplayStyle.WrappedText);
            }

            if (target.specialType != FactorioObjectSpecialType.Normal) {
                gui.BuildText(LSs.TooltipHasUntranslatedSpecialType.L(target.specialType));
            }
        }
    }

    private static readonly Dictionary<EntityEnergyType, LocalizableString1> EnergyDescriptions = new()
    {
        {EntityEnergyType.Electric, LSs.EnergyElectricity},
        {EntityEnergyType.Heat, LSs.EnergyHeat},
        {EntityEnergyType.Labor, LSs.EnergyLabor},
        {EntityEnergyType.Void, LSs.EnergyFree},
        {EntityEnergyType.FluidFuel, LSs.EnergyFluidFuel},
        {EntityEnergyType.FluidHeat, LSs.EnergyFluidHeat},
        {EntityEnergyType.SolidFuel, LSs.EnergySolidFuel},
    };

    private void BuildEntity(Entity entity, Quality quality, ImGui gui) {
        if (entity.loot.Length > 0) {
            BuildSubHeader(gui, LSs.TooltipHeaderLoot);
            using (gui.EnterGroup(contentPadding)) {
                foreach (var product in entity.loot) {
                    BuildItem(gui, product);
                }
            }
        }

        if (entity.mapGenerated) {
            using (gui.EnterGroup(contentPadding)) {
                gui.BuildText(entity.mapGenDensity <= 0 ? LSs.MapGenerationDensityUnknown
                    : LSs.MapGenerationDensity.L(DataUtils.FormatAmount(entity.mapGenDensity, UnitOfMeasure.None)),
                    TextBlockDisplayStyle.WrappedText);
            }
        }

        if (entity is EntityCrafter crafter) {
            if (crafter.recipes.Length > 0) {
                BuildSubHeader(gui, LSs.EntityCrafts);
                using (gui.EnterGroup(contentPadding)) {
                    BuildIconRow(gui, crafter.recipes, 2);
                    if (crafter.CraftingSpeed(quality) != 1f) {
                        gui.BuildText(LSs.EntityCraftingSpeed.L(DataUtils.FormatAmount(crafter.CraftingSpeed(quality), UnitOfMeasure.Percent)));
                    }

                    Effect baseEffect = crafter.effectReceiver.baseEffect;
                    if (baseEffect.speed != 0f) {
                        gui.BuildText(LSs.EntityCraftingSpeed.L(DataUtils.FormatAmount(baseEffect.speed, UnitOfMeasure.Percent)));
                    }
                    if (baseEffect.productivity != 0f) {
                        gui.BuildText(LSs.EntityCraftingProductivity.L(DataUtils.FormatAmount(baseEffect.productivity, UnitOfMeasure.Percent)));
                    }
                    if (baseEffect.consumption != 0f) {
                        gui.BuildText(LSs.EntityEnergyConsumption.L(DataUtils.FormatAmount(baseEffect.consumption, UnitOfMeasure.Percent)));
                    }

                    if (crafter.allowedEffects != AllowedEffects.None) {
                        gui.BuildText(LSs.EntityModuleSlots.L(crafter.moduleSlots));
                        if (crafter.allowedEffects != AllowedEffects.All) {
                            gui.BuildText(LSs.AllowedModuleEffectsUntranslatedList.L(crafter.allowedEffects, BitOperations.PopCount((uint)crafter.allowedEffects)), TextBlockDisplayStyle.WrappedText);
                        }
                    }
                }
            }

            if (crafter.inputs != null) {
                BuildSubHeader(gui, LSs.LabAllowedInputs.L(crafter.inputs));
                using (gui.EnterGroup(contentPadding)) {
                    BuildIconRow(gui, crafter.inputs, 2);
                }
            }
        }

        float spoilTime = entity.GetSpoilTime(quality); // The spoiling rate setting does not apply to entities.
        if (spoilTime != 0f) {
            BuildSubHeader(gui, LSs.Perishable);
            using (gui.EnterGroup(contentPadding)) {
                if (entity.spoilResult != null) {
                    gui.BuildText(LSs.TooltipEntitySpoilsAfterNoProduction.L(DataUtils.FormatTime(spoilTime)));
                    gui.BuildFactorioObjectButtonWithText(entity.spoilResult.With(quality), iconDisplayStyle: IconDisplayStyle.Default with { AlwaysAccessible = true });
                }
                else {
                    gui.BuildText(LSs.TooltipEntityExpiresAfterNoProduction.L(DataUtils.FormatTime(spoilTime)));
                }
                tooltipOptions.ExtraSpoilInformation?.Invoke(gui);
            }
        }

        if (entity.energy != null) {
            string energyUsage = EnergyDescriptions[entity.energy.type].L(DataUtils.FormatAmount(entity.Power(quality), UnitOfMeasure.Megawatt));
            if (entity.energy.drain > 0f) {
                energyUsage = LSs.TooltipActivePlusDrainPower.L(energyUsage, DataUtils.FormatAmount(entity.energy.drain, UnitOfMeasure.Megawatt));
            }

            BuildSubHeader(gui, energyUsage);
            using (gui.EnterGroup(contentPadding)) {
                if (entity.energy.type is EntityEnergyType.FluidFuel or EntityEnergyType.SolidFuel or EntityEnergyType.FluidHeat) {
                    BuildIconRow(gui, entity.energy.fuels, 2);
                }

                foreach ((string name, float amount) in entity.energy.emissions) {
                    if (amount != 0f) {
                        TextBlockDisplayStyle emissionStyle = TextBlockDisplayStyle.Default(SchemeColor.BackgroundText);
                        if (amount < 0f) {
                            emissionStyle = TextBlockDisplayStyle.Default(SchemeColor.Green);
                            gui.BuildText(LSs.EntityAbsorbsPollution.L(name), emissionStyle);
                            gui.BuildText(LSs.TooltipEntityAbsorbsPollution.L(DataUtils.FormatAmount(-amount, UnitOfMeasure.None), name), emissionStyle);
                        }
                        else {
                            if (amount >= 20f) {
                                emissionStyle = TextBlockDisplayStyle.Default(SchemeColor.Error);
                                gui.BuildText(LSs.EntityHasHighPollution, emissionStyle);
                            }
                            gui.BuildText(LSs.TooltipEntityEmitsPollution.L(DataUtils.FormatAmount(amount, UnitOfMeasure.None), name), emissionStyle);
                        }
                    }
                }
            }
        }

        if (entity.heatingPower != 0) {
            using (gui.EnterGroup(contentPadding))
            using (gui.EnterRow(0)) {
                gui.AllocateRect(0, 1.5f);
                gui.BuildText(LSs.TooltipEntityRequiresHeat.L(DataUtils.FormatAmount(entity.heatingPower, UnitOfMeasure.Megawatt)));
            }
        }

        string? miscText = null;

        switch (entity) {
            case EntityBelt belt:
                miscText = LSs.BeltThroughput.L(DataUtils.FormatAmount(belt.beltItemsPerSecond, UnitOfMeasure.PerSecond));
                break;
            case EntityInserter inserter:
                miscText = LSs.InserterSwingTime.L(DataUtils.FormatAmount(inserter.inserterSwingTime, UnitOfMeasure.Second));
                break;
            case EntityBeacon beacon:
                miscText = LSs.BeaconEfficiency.L(DataUtils.FormatAmount(beacon.BeaconEfficiency(quality), UnitOfMeasure.Percent));
                break;
            case EntityAccumulator accumulator:
                miscText = LSs.AccumulatorCapacity.L(DataUtils.FormatAmount(accumulator.AccumulatorCapacity(quality), UnitOfMeasure.Megajoule));
                break;
            case EntityAttractor attractor:
                if (attractor.baseCraftingSpeed > 0f) {
                    miscText = LSs.LightningAttractorExtraInfo.L(DataUtils.FormatAmount(attractor.CraftingSpeed(quality), UnitOfMeasure.Megawatt), attractor.ConstructionGrid(quality), DataUtils.FormatAmount(attractor.Range(quality), UnitOfMeasure.None), DataUtils.FormatAmount(attractor.Efficiency(quality), UnitOfMeasure.Percent));
                }

                break;
            case EntityCrafter solarPanel:
                if (solarPanel.baseCraftingSpeed > 0f && entity.factorioType == "solar-panel") {
                    miscText = LSs.SolarPanelAverageProduction.L(DataUtils.FormatAmount(solarPanel.CraftingSpeed(quality), UnitOfMeasure.Megawatt));
                }
                break;
        }

        if (miscText != null) {
            using (gui.EnterGroup(contentPadding)) {
                gui.BuildText(miscText, TextBlockDisplayStyle.WrappedText);
            }
        }
    }

    private void BuildGoods(Goods goods, Quality quality, ImGui gui) {
        if (goods.showInExplorers) {
            using (gui.EnterGroup(contentPadding)) {
                gui.BuildText(LSs.OpenNeieMiddleClickHint.L(goods.type), TextBlockDisplayStyle.WrappedText);
            }
        }

        if (goods.production.Length > 0) {
            BuildSubHeader(gui, LSs.TooltipHeaderProductionRecipes.L(goods.production.Length));
            using (gui.EnterGroup(contentPadding)) {
                BuildIconRow(gui, goods.production, 2);
                if (tooltipOptions.HintLocations.HasFlag(HintLocations.OnProducingRecipes)) {
                    _ = goods.production.SelectSingle(out string recipeTip);
                    gui.BuildText(recipeTip, TextBlockDisplayStyle.HintText);
                }
            }
        }

        if (goods.miscSources.Length > 0) {
            BuildSubHeader(gui, LSs.TooltipHeaderMiscellaneousSources.L(goods.miscSources.Length));
            using (gui.EnterGroup(contentPadding)) {
                BuildIconRow(gui, goods.miscSources, 2);
            }
        }

        if (goods.usages.Length > 0) {
            BuildSubHeader(gui, LSs.TooltipHeaderConsumptionRecipes.L(goods.usages.Length));
            using (gui.EnterGroup(contentPadding)) {
                BuildIconRow(gui, goods.usages, 4);
                if (tooltipOptions.HintLocations.HasFlag(HintLocations.OnConsumingRecipes)) {
                    _ = goods.usages.SelectSingle(out string recipeTip);
                    gui.BuildText(recipeTip, TextBlockDisplayStyle.HintText);
                }
            }
        }

        if (goods is Item { spoilResult: FactorioObject spoiled } perishable) {
            BuildSubHeader(gui, LSs.Perishable);
            using (gui.EnterGroup(contentPadding)) {
                float spoilTime = perishable.GetSpoilTime(quality) / Project.current.settings.spoilingRate;
                gui.BuildText(LSs.TooltipItemSpoils.L(DataUtils.FormatTime(spoilTime)));
                gui.BuildFactorioObjectButtonWithText(spoiled, iconDisplayStyle: IconDisplayStyle.Default with { AlwaysAccessible = true });
                tooltipOptions.ExtraSpoilInformation?.Invoke(gui);
            }
        }

        if (goods.fuelFor.Length > 0) {
            if (goods.fuelValue > 0f) {
                BuildSubHeader(gui, LSs.FuelValueCanBeUsed.L(DataUtils.FormatAmount(goods.fuelValue, UnitOfMeasure.Megajoule)));
            }
            else {
                BuildSubHeader(gui, LSs.FuelValueZeroCanBeUsed);
            }

            using (gui.EnterGroup(contentPadding)) {
                BuildIconRow(gui, goods.fuelFor, 2);
            }
        }

        if (goods is Item item) {
            if (goods.fuelValue > 0f && item.fuelResult != null) {
                using (gui.EnterGroup(contentPadding)) {
                    BuildItem(gui, item.fuelResult);
                }
            }

            if (item.placeResult != null) {
                BuildSubHeader(gui, LSs.TooltipHeaderItemPlacementResult);
                using (gui.EnterGroup(contentPadding)) {
                    BuildItem(gui, item.placeResult);
                }
            }

            if (item is Module { moduleSpecification: ModuleSpecification moduleSpecification }) {
                BuildSubHeader(gui, LSs.TooltipHeaderModuleProperties);
                using (gui.EnterGroup(contentPadding)) {
                    if (moduleSpecification.baseProductivity != 0f) {
                        gui.BuildText(LSs.ProductivityProperty.L(DataUtils.FormatAmount(moduleSpecification.Productivity(quality), UnitOfMeasure.Percent)));
                    }

                    if (moduleSpecification.baseSpeed != 0f) {
                        gui.BuildText(LSs.SpeedProperty.L(DataUtils.FormatAmount(moduleSpecification.Speed(quality), UnitOfMeasure.Percent)));
                    }

                    if (moduleSpecification.baseConsumption != 0f) {
                        gui.BuildText(LSs.ConsumptionProperty.L(DataUtils.FormatAmount(moduleSpecification.Consumption(quality), UnitOfMeasure.Percent)));
                    }

                    if (moduleSpecification.basePollution != 0f) {
                        gui.BuildText(LSs.PollutionProperty.L(DataUtils.FormatAmount(moduleSpecification.Pollution(quality), UnitOfMeasure.Percent)));
                    }

                    if (moduleSpecification.baseQuality != 0f) {
                        gui.BuildText(LSs.QualityProperty.L(DataUtils.FormatAmount(moduleSpecification.Quality(quality), UnitOfMeasure.Percent)));
                    }
                }
            }

            using (gui.EnterGroup(contentPadding)) {
                gui.BuildText(LSs.ItemStackSize.L(item.stackSize));
                gui.BuildText(LSs.ItemRocketCapacity.L(DataUtils.FormatAmount(item.rocketCapacity, UnitOfMeasure.None)));
            }
        }
    }

    private static void BuildRecipe(RecipeOrTechnology recipe, ImGui gui) {
        using (gui.EnterGroup(contentPadding, RectAllocator.LeftRow)) {
            gui.BuildIcon(Icon.Time, 2f, SchemeColor.BackgroundText);
            gui.BuildText(DataUtils.FormatAmount(recipe.time, UnitOfMeasure.Second));
        }

        using (gui.EnterGroup(contentPadding)) {
            if (recipe is Technology) {
                BuildIconRow(gui, recipe.ingredients, 2, DataUtils.DefaultSciencePackOrdering);
            }
            else {
                foreach (Ingredient ingredient in recipe.ingredients) {
                    BuildItem(gui, ingredient);
                }
            }

            if (recipe is Recipe rec) {
                float waste = rec.RecipeWaste();
                if (waste > 0.01f) {
                    int wasteAmount = MathUtils.Round(waste * 100f);
                    TextBlockDisplayStyle style = TextBlockDisplayStyle.WrappedText with { Color = wasteAmount < 90 ? SchemeColor.BackgroundText : SchemeColor.Error };
                    if (recipe.products.Length == 1) {
                        gui.BuildText(LSs.AnalysisBetterRecipesToCreate.L(recipe.products[0].goods.locName, wasteAmount), style);
                    }
                    else if (recipe.products.Length > 0) {
                        gui.BuildText(LSs.AnalysisBetterRecipesToCreateAll.L(wasteAmount), style);
                    }
                    else {
                        gui.BuildText(LSs.AnalysisWastesUsefulProducts, style);
                    }
                }
            }
            if (recipe.flags.HasFlags(RecipeFlags.UsesFluidTemperature)) {
                gui.BuildText(LSs.RecipeUsesFluidTemperature);
            }

            if (recipe.flags.HasFlags(RecipeFlags.UsesMiningProductivity)) {
                gui.BuildText(LSs.RecipeUsesMiningProductivity);
            }

            if (recipe.flags.HasFlags(RecipeFlags.ScaleProductionWithPower)) {
                gui.BuildText(LSs.RecipeProductionScalesWithPower);
            }
        }

        if (recipe is Recipe { products.Length: > 0 } && !(recipe.products.Length == 1 && recipe.products[0].IsSimple)) {
            BuildSubHeader(gui, LSs.TooltipHeaderRecipeProducts.L(recipe.products.Length));
            using (gui.EnterGroup(contentPadding)) {
                string? extraText = recipe is Recipe { preserveProducts: true } ? LSs.ProductSuffixPreserved : null;
                foreach (var product in recipe.products) {
                    BuildItem(gui, product, extraText);
                }
            }
        }

        BuildSubHeader(gui, LSs.TooltipHeaderRecipeCrafters.L(recipe.crafters.Length));
        using (gui.EnterGroup(contentPadding)) {
            BuildIconRow(gui, recipe.crafters, 2);
        }

        List<Module> allowedModules = [.. Database.allModules.Where(recipe.CanAcceptModule)];

        if (allowedModules.Count > 0) {
            BuildSubHeader(gui, LSs.TooltipHeaderAllowedModules.L(allowedModules.Count));
            using (gui.EnterGroup(contentPadding)) {
                BuildIconRow(gui, allowedModules, 1);
            }

            //var crafterCommonModules = AllowedEffects.All;
            //foreach (var crafter in recipe.crafters) {
            //    if (crafter.moduleSlots > 0) {
            //        crafterCommonModules &= crafter.allowedEffects;
            //    }
            //}

            //foreach (var module in recipe.modules) {
            //    if (!EntityWithModules.CanAcceptModule(module.moduleSpecification, crafterCommonModules, null)) {
            //        using (gui.EnterGroup(contentPadding)) {
            //            gui.BuildText("Some crafters restrict module usage");
            //        }

            //        break;
            //    }
            //}
        }

        if (recipe is Recipe lockedRecipe && !lockedRecipe.enabled) {
            BuildSubHeader(gui, LSs.TooltipHeaderUnlockedByTechnologies.L(lockedRecipe.technologyUnlock.Length));
            using (gui.EnterGroup(contentPadding)) {
                if (lockedRecipe.technologyUnlock.Length > 2) {
                    BuildIconRow(gui, lockedRecipe.technologyUnlock, 1);
                }
                else {
                    foreach (var technology in lockedRecipe.technologyUnlock) {
                        var ingredient = TechnologyScienceAnalysis.Instance.GetMaxTechnologyIngredient(technology);
                        using (gui.EnterRow(allocator: RectAllocator.RightRow)) {
                            gui.spacing = 0f;
                            if (ingredient != null) {
                                gui.BuildFactorioObjectIcon(ingredient.goods);
                                gui.BuildText(DataUtils.FormatAmount(ingredient.amount, UnitOfMeasure.None));
                            }

                            gui.allocator = RectAllocator.RemainingRow;
                            _ = gui.BuildFactorioObjectButtonWithText(technology);
                        }
                    }
                }
            }
        }
    }

    private static void BuildTechnology(Technology technology, ImGui gui) {
        bool isResearchTriggerCraft = technology.flags.HasFlag(RecipeFlags.HasResearchTriggerCraft);
        bool isResearchTriggerCapture = technology.flags.HasFlag(RecipeFlags.HasResearchTriggerCaptureEntity);
        bool isResearchTriggerMine = technology.flags.HasFlag(RecipeFlags.HasResearchTriggerMineEntity);
        bool isResearchTriggerBuild = technology.flags.HasFlag(RecipeFlags.HasResearchTriggerBuildEntity);
        bool isResearchTriggerPlatform = technology.flags.HasFlag(RecipeFlags.HasResearchTriggerCreateSpacePlatform);
        bool isResearchTriggerLaunch = technology.flags.HasFlag(RecipeFlags.HasResearchTriggerSendToOrbit);

        if (!technology.flags.HasFlagAny(RecipeFlags.HasResearchTriggerMask)) {
            BuildRecipe(technology, gui);
        }

        if (!technology.enabled) {
            using (gui.EnterGroup(contentPadding)) {
                gui.BuildText(LSs.TechnologyIsDisabled, TextBlockDisplayStyle.WrappedText);
            }
        }

        if (technology.prerequisites.Length > 0) {
            BuildSubHeader(gui, LSs.TooltipHeaderTechnologyPrerequisites.L(technology.prerequisites.Length));
            using (gui.EnterGroup(contentPadding)) {
                BuildIconRow(gui, technology.prerequisites, 1);
            }
        }

        if (isResearchTriggerCraft) {
            BuildSubHeader(gui, LSs.TooltipHeaderTechnologyItemCrafting);
            using (gui.EnterGroup(contentPadding)) {
                using var grid = gui.EnterInlineGrid(3f);
                grid.Next();
                _ = gui.BuildFactorioObjectWithAmount(technology.ingredients[0].goods, technology.ingredients[0].amount, ButtonDisplayStyle.ProductionTableUnscaled);
            }
        }
        else if (isResearchTriggerCapture) {
            BuildSubHeader(gui, LSs.TooltipHeaderTechnologyCapture.L(technology.triggerEntities.Count));
            using (gui.EnterGroup(contentPadding)) {
                BuildIconRow(gui, technology.triggerEntities, 2);
            }
        }
        else if (isResearchTriggerMine) {
            BuildSubHeader(gui, LSs.TooltipHeaderTechnologyMineEntity.L(technology.triggerEntities.Count));
            using (gui.EnterGroup(contentPadding)) {
                BuildIconRow(gui, technology.triggerEntities, 2);
            }
        }
        else if (isResearchTriggerBuild) {
            BuildSubHeader(gui, LSs.TooltipHeaderTechnologyBuildEntity.L(technology.triggerEntities.Count));
            using (gui.EnterGroup(contentPadding)) {
                BuildIconRow(gui, technology.triggerEntities, 2);
            }
        }
        else if (isResearchTriggerPlatform) {
            List<Item> items = [.. Database.items.all.Where(i => i.factorioType == "space-platform-starter-pack")];
            BuildSubHeader(gui, LSs.TooltipHeaderTechnologyLaunchItem.L(items.Count));
            using (gui.EnterGroup(contentPadding)) {
                BuildIconRow(gui, items, 2);
            }
        }
        else if (isResearchTriggerLaunch) {
            BuildSubHeader(gui, LSs.TooltipHeaderTechnologyLaunchItem.L(1));
            using (gui.EnterGroup(contentPadding)) {
                gui.BuildFactorioObjectButtonWithText(technology.triggerItem);
            }
        }

        if (technology.unlockRecipes.Count > 0) {
            BuildSubHeader(gui, LSs.TooltipHeaderUnlocksRecipes.L(technology.unlockRecipes.Count));
            using (gui.EnterGroup(contentPadding)) {
                BuildIconRow(gui, technology.unlockRecipes, 2);
            }
        }

        if (technology.unlockLocations.Count > 0) {
            BuildSubHeader(gui, LSs.TooltipHeaderUnlocksLocations.L(technology.unlockLocations.Count));
            using (gui.EnterGroup(contentPadding)) {
                BuildIconRow(gui, technology.unlockLocations, 2);
            }
        }

        var packs = TechnologyScienceAnalysis.Instance.allSciencePacks[technology];
        if (packs.Length > 0) {
            BuildSubHeader(gui, LSs.TooltipHeaderTotalScienceRequired);
            using (gui.EnterGroup(contentPadding)) {
                using var grid = gui.EnterInlineGrid(3f);
                foreach (var pack in packs) {
                    grid.Next();
                    _ = gui.BuildFactorioObjectWithAmount(pack.goods, pack.amount, ButtonDisplayStyle.ProductionTableUnscaled);
                }
            }
        }
    }

    private static void BuildQuality(Quality quality, ImGui gui) {
        using (gui.EnterGroup(contentPadding)) {
            if (quality.UpgradeChance > 0) {
                gui.BuildText(LSs.TooltipQualityUpgradeChance.L(DataUtils.FormatAmount(quality.UpgradeChance, UnitOfMeasure.Percent)));
            }
        }

        BuildSubHeader(gui, LSs.TooltipHeaderQualityBonuses);
        using (gui.EnterGroup(contentPadding)) {
            if (quality == Quality.Normal) {
                gui.BuildText(LSs.TooltipNoNormalBonuses, TextBlockDisplayStyle.WrappedText);
                return;
            }

            gui.allocator = RectAllocator.LeftAlign;
            (string left, string right)[] text = [
                (LSs.TooltipQualityCraftingSpeed, LSs.QualityBonusValue.L(DataUtils.FormatAmount(quality.StandardBonus, UnitOfMeasure.Percent))),
                (LSs.TooltipQualityAccumulatorCapacity, LSs.QualityBonusValue.L(DataUtils.FormatAmount(quality.AccumulatorCapacityBonus, UnitOfMeasure.Percent))),
                (LSs.TooltipQualityModuleEffects, LSs.QualityBonusValueWithFootnote.L(DataUtils.FormatAmount(quality.StandardBonus, UnitOfMeasure.Percent))),
                (LSs.TooltipQualityBeaconTransmission, LSs.QualityBonusValue.L(DataUtils.FormatAmount(quality.BeaconTransmissionBonus, UnitOfMeasure.None))),
                (LSs.TooltipQualityTimeBeforeSpoiling, LSs.QualityBonusValue.L(DataUtils.FormatAmount(quality.StandardBonus, UnitOfMeasure.Percent))),
                (LSs.TooltipQualityLightningAttractor, LSs.QualityBonusValue.L(DataUtils.FormatAmount(quality.StandardBonus, UnitOfMeasure.Percent))),
            ];

            float rightWidth = text.Max(t => gui.GetTextDimensions(out _, t.right).X);

            gui.allocator = RectAllocator.LeftAlign;
            foreach (var (left, right) in text) {
                gui.BuildText(left);
                Rect rect = new(gui.statePosition.Width - rightWidth, gui.lastRect.Y, rightWidth, gui.lastRect.Height);
                gui.DrawText(rect, right);
            }
            gui.BuildText(LSs.TooltipQualityModuleFootnote, TextBlockDisplayStyle.WrappedText);
        }
    }

    public void SetFocus(IFactorioObjectWrapper target, ImGui gui, Rect rect, ObjectTooltipOptions tooltipOptions) {
        this.tooltipOptions = tooltipOptions;
        this.target = target;
        base.SetFocus(gui, rect);
    }

    public bool IsSameObjectHovered(ImGui gui, FactorioObject? factorioObject) => source == gui && factorioObject == target.target && gui.IsMouseOver(sourceRect);
}

public struct ObjectTooltipOptions {
    /// <summary>
    /// If <see langword="true"/> and the target object is not a <see cref="Goods"/>, this tooltip will specify the type of object.
    /// e.g. "Radar" is the item, "Radar (Recipe)" is the recipe, and "Radar (Entity)" is the building.
    /// </summary>
    public bool ShowTypeInHeader { get; set; }
    /// <summary>
    /// Gets or sets flags indicating where hints should be displayed in the tooltip.
    /// </summary>
    public HintLocations HintLocations { get; set; }
    /// <summary>
    /// Gets or sets a value that, if not <see langword="null"/>, will be called after drawing the tooltip header.
    /// </summary>
    public DrawBelowHeader? DrawBelowHeader { get; set; }
    /// <summary>
    /// Gets or sets a value that, if not <see langword="null"/>, will be called after drawing the spoilage information.
    /// </summary>
    public GuiBuilder ExtraSpoilInformation { get; set; }

    // Reduce boilerplate by permitting unambiguous and relatively obvious implicit conversions.
    public static implicit operator ObjectTooltipOptions(HintLocations hintLocations) => new() { HintLocations = hintLocations };
    public static implicit operator ObjectTooltipOptions(DrawBelowHeader drawBelowHeader) => new() { DrawBelowHeader = drawBelowHeader };
}

/// <summary>
/// Called to draw additional information in the tooltip after drawing the tooltip header.
/// </summary>
public delegate void DrawBelowHeader(ImGui gui);
