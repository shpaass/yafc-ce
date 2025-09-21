using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SDL2;
using Yafc.Blueprints;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

public class ProductionTableView : ProjectPageView<ProductionTable> {
    private readonly FlatHierarchy<RecipeRow, ProductionTable> flatHierarchyBuilder;

    public ProductionTableView() {
        DataGrid<RecipeRow> grid = new DataGrid<RecipeRow>(new RecipePadColumn(this), new RecipeColumn(this), new EntityColumn(this),
            new IngredientsColumn(this), new ProductsColumn(this), new ModulesColumn(this));

        flatHierarchyBuilder = new FlatHierarchy<RecipeRow, ProductionTable>(grid, BuildSummary,
            LSs.ProductionTableNestedGroup);
    }

    /// <param name="widthStorage">If not <see langword="null"/>, names an instance property in <see cref="Preferences"/> that will be used to store the width of this column.
    /// If the current value of the property is out of range, the initial width will be <paramref name="initialWidth"/>.</param>
    private abstract class ProductionTableDataColumn(ProductionTableView view, string header, float initialWidth, float minWidth = 0, float maxWidth = 0, bool hasMenu = true, string? widthStorage = null)
        : TextDataColumn<RecipeRow>(header, initialWidth, minWidth, maxWidth, hasMenu, widthStorage) {

        protected readonly ProductionTableView view = view;
    }

    private class RecipePadColumn(ProductionTableView view) : ProductionTableDataColumn(view, "", 3f, hasMenu: false) {
        public override void BuildElement(ImGui gui, RecipeRow row) {
            gui.allocator = RectAllocator.Center;
            gui.spacing = 0f;

            if (row.subgroup != null) {
                if (gui.BuildButton(row.subgroup.expanded ? Icon.ChevronDown : Icon.ChevronRight)) {
                    if (InputSystem.Instance.control) {
                        toggleAll(!row.subgroup.expanded, view.model);
                    }
                    else {
                        row.subgroup.RecordChange().expanded = !row.subgroup.expanded;
                    }

                    view.flatHierarchyBuilder.SetData(view.model);
                }
            }

            if (row.warningFlags != 0) {
                bool isError = row.warningFlags >= WarningFlags.EntityNotSpecified;
                ButtonEvent evt;

                if (isError) {
                    evt = gui.BuildRedButton(Icon.Error, invertedColors: true);
                }
                else {
                    using (gui.EnterGroup(ImGuiUtils.DefaultIconPadding)) {
                        gui.BuildIcon(Icon.Help);
                    }

                    evt = gui.BuildButton(gui.lastRect, SchemeColor.None, SchemeColor.Grey);
                }

                if (evt == ButtonEvent.MouseOver) {
                    gui.ShowTooltip(g => {
                        if (isError) {
                            g.boxColor = SchemeColor.Error;
                            g.textColor = SchemeColor.ErrorText;
                        }
                        foreach (var (flag, key) in WarningsMeaning) {
                            if ((row.warningFlags & flag) != 0) {
                                g.BuildText(key, TextBlockDisplayStyle.WrappedText);
                            }
                        }
                    });
                }
                else if (evt == ButtonEvent.Click) {
                    if (row.warningFlags.HasFlag(WarningFlags.ReactorsNeighborsFromPrefs)) {
                        PreferencesScreen.ShowGeneral();
                    }
                    else if (row.warningFlags.HasFlag(WarningFlags.UselessQuality)) {
                        _ = MainScreen.Instance.ShowPseudoScreen(new MilestonesPanel());
                    }
                    else if (row.warningFlags.HasFlag(WarningFlags.ExcessProductivity)) {
                        PreferencesScreen.ShowProgression();
                    }
                }
            }
            else {
                if (row.tag != 0) {
                    BuildRowMarker(gui, row);
                }
            }

            static void toggleAll(bool state, ProductionTable table) {
                foreach (var subgroup in table.recipes.Select(r => r.subgroup).WhereNotNull()) {
                    subgroup.RecordChange().expanded = state;
                    toggleAll(state, subgroup);
                }
            }
        }

        private static void BuildRowMarker(ImGui gui, RecipeRow row) {
            int markerId = row.tag;

            if (markerId < 0 || markerId >= tagIcons.Length) {
                markerId = 0;
            }

            var (icon, color) = tagIcons[markerId];
            gui.BuildIcon(icon, color: color);

            if (gui.BuildButton(gui.lastRect, SchemeColor.None, SchemeColor.BackgroundAlt)) {
                gui.ShowDropDown(imGui => DrawRecipeTagSelect(imGui, row));
            }
        }
    }

    private class RecipeColumn(ProductionTableView view) : ProductionTableDataColumn(view, LSs.ProductionTableHeaderRecipe, 13f, 13f, 30f, widthStorage: nameof(Preferences.recipeColumnWidth)) {
        public override void BuildElement(ImGui gui, RecipeRow recipe) {
            gui.spacing = 0.5f;
            switch (gui.BuildFactorioObjectButton(recipe.recipe, ButtonDisplayStyle.ProductionTableUnscaled)) {
                case Click.Left:
                    gui.ShowDropDown(delegate (ImGui imgui) {
                        DrawRecipeTagSelect(imgui, recipe);

                        if (recipe.subgroup == null && imgui.BuildButton(LSs.ProductionTableCreateNested) && imgui.CloseDropdown()) {
                            recipe.RecordUndo().subgroup = new ProductionTable(recipe);
                        }

                        if (recipe.subgroup != null && imgui.BuildButton(LSs.ProductionTableAddNestedProduct) && imgui.CloseDropdown()) {
                            AddDesiredProductAtLevel(recipe.subgroup);
                        }

                        if (recipe.subgroup != null) {
                            BuildRecipeButton(imgui, recipe.subgroup);
                        }

                        if (recipe.subgroup != null && imgui.BuildButton(LSs.ProductionTableUnpackNested).WithTooltip(imgui, recipe.subgroup.expanded ? LSs.ProductionTableShortcutRightClick : LSs.ProductionTableShortcutExpandAndRightClick) && imgui.CloseDropdown()) {
                            unpackNestedTable();
                        }

                        if (recipe.subgroup != null && imgui.BuildButton(LSs.ShoppingList) && imgui.CloseDropdown()) {
                            view.BuildShoppingList(recipe);
                        }

                        if (imgui.BuildCheckBox(LSs.ProductionTableShowTotalIo, recipe.showTotalIO, out bool newShowTotalIO)) {
                            recipe.RecordUndo().showTotalIO = newShowTotalIO;
                        }

                        if (imgui.BuildCheckBox(LSs.Enabled, recipe.enabled, out bool newEnabled)) {
                            recipe.RecordUndo().enabled = newEnabled;
                        }

                        BuildFavorites(imgui, recipe.recipe.target, LSs.AddRecipeToFavorites);

                        if (recipe.subgroup != null && imgui.BuildRedButton(LSs.ProductionTableDeleteNested).WithTooltip(imgui, recipe.subgroup.expanded ? LSs.ProductionTableShortcutCollapseAndRightClick : LSs.ProductionTableShortcutRightClick) && imgui.CloseDropdown()) {
                            _ = recipe.owner.RecordUndo().recipes.Remove(recipe);
                        }

                        if (recipe.subgroup == null && imgui.BuildRedButton(LSs.ProductionTableDeleteRecipe).WithTooltip(imgui, LSs.ProductionTableShortcutRightClick) && imgui.CloseDropdown()) {
                            _ = recipe.owner.RecordUndo().recipes.Remove(recipe);
                        }
                    });
                    break;
                case Click.Right when recipe.subgroup?.expanded ?? false: // With expanded subgroup
                    unpackNestedTable();
                    break;
                case Click.Right: // With collapsed or no subgroup
                    _ = recipe.owner.RecordUndo().recipes.Remove(recipe);
                    break;
            }

            if (!recipe.enabled) {
                gui.textColor = SchemeColor.BackgroundTextFaint;
            }
            else if (view.flatHierarchyBuilder.nextRowIsHighlighted) {
                gui.textColor = view.flatHierarchyBuilder.nextRowTextColor;
            }
            else {
                gui.textColor = recipe.hierarchyEnabled ? SchemeColor.BackgroundText : SchemeColor.BackgroundTextFaint;
            }

            gui.BuildText(recipe.recipe.target.locName, TextBlockDisplayStyle.WrappedText);

            void unpackNestedTable() {
                var evacuate = recipe.subgroup.recipes;
                _ = recipe.subgroup.RecordUndo();
                recipe.RecordUndo().subgroup = null;
                int index = recipe.owner.recipes.IndexOf(recipe);

                foreach (var evacRecipe in evacuate) {
                    evacRecipe.SetOwner(recipe.owner);
                }

                recipe.owner.RecordUndo().recipes.InsertRange(index + 1, evacuate);
            }
        }

        private static void RemoveZeroRecipes(ProductionTable productionTable) {
            _ = productionTable.RecordUndo().recipes.RemoveAll(x => x.subgroup == null && x.recipesPerSecond == 0);

            foreach (var recipe in productionTable.recipes) {
                if (recipe.subgroup != null) {
                    RemoveZeroRecipes(recipe.subgroup);
                }
            }
        }

        public override void BuildMenu(ImGui gui) {
            BuildRecipeButton(gui, view.model);

            gui.BuildText(LSs.ProductionTableExportToBlueprint, TextBlockDisplayStyle.WrappedText);
            using (gui.EnterRow()) {
                gui.BuildText(LSs.ExportBlueprintAmountPer);

                if (gui.BuildLink(LSs.ExportBlueprintAmountPerSecond) && gui.CloseDropdown()) {
                    ExportIo(1f);
                }

                if (gui.BuildLink(LSs.ExportBlueprintAmountPerMinute) && gui.CloseDropdown()) {
                    ExportIo(60f);
                }

                if (gui.BuildLink(LSs.ExportBlueprintAmountPerHour) && gui.CloseDropdown()) {
                    ExportIo(3600f);
                }
            }

            if (gui.BuildButton(LSs.ProductionTableRemoveZeroBuildingRecipes) && gui.CloseDropdown()) {
                RemoveZeroRecipes(view.model);
            }

            if (gui.BuildRedButton(LSs.ProductionTableClearRecipes) && gui.CloseDropdown()) {
                view.model.RecordUndo().recipes.Clear();
            }

            if (InputSystem.Instance.control && gui.BuildButton(LSs.ProductionTableAddAllRecipes) && gui.CloseDropdown()) {
                foreach (var recipe in Database.recipes.all) {
                    if (!recipe.IsAccessible()) {
                        continue;
                    }

                    foreach (var ingredient in recipe.ingredients) {
                        if (ingredient.goods.production.Length == 0) {
                            // 'goto' is a readable way to break out of a nested loop.
                            // See https://stackoverflow.com/questions/324831/breaking-out-of-a-nested-loop
                            goto goodsHaveNoProduction;
                        }
                    }

                    foreach (var quality in Database.qualities.all) {
                        foreach (var product in recipe.products) {
                            view.RebuildIf(view.model.CreateLink(product.goods.With(quality)));
                        }

                        view.model.AddRecipe(recipe.With(quality), DefaultVariantOrdering);
                    }
goodsHaveNoProduction:;
                }
            }
        }

        /// <summary>
        /// Build the "Add raw recipe" button and handle its clicks.
        /// </summary>
        /// <param name="table">The table that will receive the new recipes or technologies, if any are selected</param>
        private static void BuildRecipeButton(ImGui gui, ProductionTable table) {
            if (gui.BuildButton(LSs.ProductionTableAddRawRecipe).WithTooltip(gui, LSs.ProductionTableAddTechnologyHint) && gui.CloseDropdown()) {
                if (InputSystem.Instance.control) {
                    SelectMultiObjectPanel.Select(Database.technologies.all, new(LSs.ProductionTableAddTechnology, Multiple: true,
                        Checkmark: table.Contains, YellowMark: table.ContainsAnywhere),
                        r => table.AddRecipe(r.With(Quality.Normal), DefaultVariantOrdering));
                }
                else {
                    SelectMultiObjectPanel.SelectWithQuality(Database.recipes.explorable, new(LSs.ProductionTableAddRawRecipe, Multiple: true,
                        Checkmark: table.Contains, YellowMark: table.ContainsAnywhere, SelectedQuality: Quality.Normal),
                        r => table.AddRecipe(r, DefaultVariantOrdering));
                }
            }
        }

        private void ExportIo(float multiplier) {
            List<(IObjectWithQuality<Goods>, int)> goods = [];
            foreach (var link in view.model.allLinks) {
                int rounded = MathUtils.Round(link.amount * multiplier);

                if (rounded == 0) {
                    continue;
                }

                goods.Add((link.goods, rounded));
            }

            foreach (var flow in view.model.flow) {
                int rounded = MathUtils.Round(flow.amount * multiplier);

                if (rounded == 0) {
                    continue;
                }

                goods.Add((flow.goods, rounded));
            }

            _ = BlueprintUtilities.ExportConstantCombinators(view.projectPage!.name, goods); // null-forgiving: An active view always has an active page.
        }
    }

    private class EntityColumn(ProductionTableView view) : ProductionTableDataColumn(view, LSs.ProductionTableHeaderEntity, 8f) {
        public override void BuildElement(ImGui gui, RecipeRow recipe) {
            if (recipe.isOverviewMode) {
                return;
            }

            Click click;
            using (var group = gui.EnterGroup(default, RectAllocator.Stretch, spacing: 0f)) {
                group.SetWidth(3f);
                if (recipe is { fixedBuildings: > 0, fixedFuel: false, fixedIngredient: null, fixedProduct: null, hierarchyEnabled: true }) {
                    DisplayAmount amount = recipe.fixedBuildings;
                    GoodsWithAmountEvent evt = gui.BuildFactorioObjectWithEditableAmount(recipe.entity, amount, ButtonDisplayStyle.ProductionTableUnscaled,
                        setKeyboardFocus: recipe.ShouldFocusFixedCountThisTime());

                    if (evt == GoodsWithAmountEvent.TextEditing && amount.Value >= 0) {
                        recipe.RecordUndo().fixedBuildings = amount.Value;
                    }

                    click = (Click)evt;
                }
                else {
                    click = gui.BuildFactorioObjectWithAmount(recipe.entity, recipe.buildingCount, ButtonDisplayStyle.ProductionTableUnscaled);
                }

                if (recipe.builtBuildings != null) {
                    DisplayAmount amount = recipe.builtBuildings.Value;

                    if (gui.BuildFloatInput(amount, TextBoxDisplayStyle.FactorioObjectInput with { ColorGroup = SchemeColorGroup.Grey }, recipe.ShouldFocusBuiltCountThisTime())
                        && amount.Value >= 0) {

                        recipe.RecordUndo().builtBuildings = (int)amount.Value;
                    }
                }
            }

            if (recipe.recipe.target.crafters.Length == 0) {
                // ignore all clicks
            }
            else if (click == Click.Left) {
                ShowEntityDropdown(gui, recipe);
            }
            else if (click == Click.Right) {
                // null-forgiving: We know recipe.recipe.crafters is not empty, so AutoSelect can't return null.
                EntityCrafter favoriteCrafter = recipe.recipe.target.crafters.AutoSelect(DataUtils.FavoriteCrafter)!;

                if (favoriteCrafter != null && recipe.entity?.target != favoriteCrafter) {
                    _ = recipe.RecordUndo();
                    recipe.entity = favoriteCrafter.With(recipe.entity?.quality ?? Quality.MaxAccessible);

                    if (!recipe.entity.target.energy.fuels.Contains(recipe.fuel?.target)) {
                        recipe.fuel = recipe.entity.target.energy.fuels.AutoSelect(DataUtils.FavoriteFuel).With(Quality.Normal);
                    }
                }
                else if (recipe.fixedBuildings > 0) {
                    recipe.RecordUndo().fixedBuildings = 0;
                    // Clear the keyboard focus: If we hide and then recreate the edit box without removing the focus, the UI system will restore the old value.
                    // (If the focus was on a text box we aren't hiding, other code also removes the focus.)
                    // To observe (prior to this fix), add a fixed or built count with a non-default value, clear it with right-click, and then click the "Set ... building count" button again.
                    // The old behavior is that the non-default value is restored.
                    InputSystem.Instance.currentKeyboardFocus?.FocusChanged(false);
                }
                else if (recipe.builtBuildings != null) {
                    recipe.RecordUndo().builtBuildings = null;
                    // Clear the keyboard focus: as above
                    InputSystem.Instance.currentKeyboardFocus?.FocusChanged(false);
                }
            }

            gui.AllocateSpacing(0.5f);
            if (recipe.fuel != Database.voidEnergy || recipe.entity == null || recipe.entity.target.energy.type != EntityEnergyType.Void) {
                var (fuel, fuelAmount, fuelLink, _) = recipe.FuelInformation;
                view.BuildGoodsIcon(gui, fuel, fuelLink, fuelAmount, ProductDropdownType.Fuel, recipe, recipe.linkRoot, HintLocations.OnProducingRecipes);
            }
            else {
                if (recipe.recipe == Database.electricityGeneration && recipe.entity.target.factorioType is "solar-panel" or "lightning-attractor") {
                    BuildAccumulatorView(gui, recipe);
                }
            }
        }

        private static void BuildAccumulatorView(ImGui gui, RecipeRow recipe) {
            var accumulator = recipe.GetVariant(Database.allAccumulators);
            Quality accumulatorQuality = recipe.GetVariant(Database.qualities.all.OrderBy(q => q.level).ToArray());
            float requiredAccumulators = 0;
            if (recipe.entity?.target.factorioType == "solar-panel") {
                float requiredMj = recipe.entity?.GetCraftingSpeed() * recipe.buildingCount * (70 / 0.7f) ?? 0; // 70 seconds of charge time to last through the night
                requiredAccumulators = requiredMj / accumulator.AccumulatorCapacity(accumulatorQuality);
            }
            else if (recipe.entity is IObjectWithQuality<EntityAttractor> attractor) {
                // Model the storm as rising from 0% to 100% over 30 seconds, staying at 100% for 24 seconds, and decaying over 30 seconds.
                // I adjusted these until the right answers came out of my Excel model.
                // TODO(multi-planet): Adjust these numbers based on day length.
                const int stormRiseTicks = 30 * 60, stormPlateauTicks = 24 * 60, stormFallTicks = 30 * 60;
                const int stormTotalTicks = stormRiseTicks + stormPlateauTicks + stormFallTicks;

                // Don't try to model the storm with less than 1 attractor (6 lightning strikes for a normal rod)
                float stormMjPerTick = attractor.StormPotentialPerTick() * (recipe.buildingCount < 1 ? 1 : recipe.buildingCount);
                // TODO(multi-planet): Use the appropriate LightningPrototype::energy instead of hardcoding the 1000 of Fulgoran lightning.
                // Tick numbers will be wrong if the first and last strike don't happen in the rise and fall periods. This is okay because
                // a single normal rod has the first strike at 23 seconds, and the _second_ at 32.
                float totalStormEnergy = stormMjPerTick * 3 * 60 * 60 /*TODO(multi-planet): ticks per day*/ * 0.3f;
                float lostStormEnergy = totalStormEnergy % 1000;
                float firstStrikeTick = (MathF.Sqrt(1 + 8 * 1000 * stormRiseTicks / stormMjPerTick) + 1) / 2;
                float lastStrikeTick = stormTotalTicks - (MathF.Sqrt(1 + 8 * lostStormEnergy * stormFallTicks / stormMjPerTick) + 1) / 2;
                int strikeCount = (int)(totalStormEnergy / 1000);

                float requiredPower = attractor.GetCraftingSpeed() * recipe.buildingCount;

                // Two different conditions need to be tested here. The first test is for capacity when discharging: the accumulators must have
                // a capacity of requiredPower * timeBetween(lastStrikeDischarged, firstStrike + 1 day)
                // As simplifying assumptions for this calculation, (1) the accumulators are fully charged when the last strike hits, and
                // (2) the attractor's internal buffer is empty when the last strike hits.
                // If incorrect, these cause errors in opposite directions.
                float lastStrikeDrainedTick = lastStrikeTick + 1000 * attractor.GetAttractorEfficiency() / (requiredPower + attractor.target.drain) * 60;
                float requiredTicks = 3 * 60 * 60 /*TODO(multi-planet): ticks per day*/ - lastStrikeDrainedTick + firstStrikeTick;
                float requiredMj = requiredPower * requiredTicks / 60;

                // The second test is for capacity when charging: The accumulators must draw at least requiredMj out of the attractors.
                // Solve: chargeTimePerStrike = 1000MJ * effectiveness / (150MW + chargePower + requiredPower)
                // And: chargePower * chargeTimePerStrike * #strikes - requiredPower * nonStrikeStormTime = requiredMj
                // Not fun (see Fulgora lightning model.md), but the result is:
                float stormLengthSeconds = (lastStrikeDrainedTick - firstStrikeTick) / 60;
                float stormEnergy = 1000 * attractor.GetAttractorEfficiency() * strikeCount;
                float numerator = requiredMj * attractor.target.drain + requiredPower * (requiredMj + stormLengthSeconds * (attractor.target.drain + requiredPower) - stormEnergy);
                float denominator = stormEnergy - requiredPower * stormLengthSeconds - requiredMj;
                float requiredChargeMw = numerator / denominator;

                requiredAccumulators = Math.Max(requiredMj / accumulator.AccumulatorCapacity(accumulatorQuality),
                    requiredChargeMw / accumulator.Power(accumulatorQuality));
            }

            IObjectWithQuality<Entity> accumulatorWithQuality = accumulator.With(accumulatorQuality);
            if (gui.BuildFactorioObjectWithAmount(accumulatorWithQuality, requiredAccumulators, ButtonDisplayStyle.ProductionTableUnscaled) == Click.Left) {
                ShowAccumulatorDropdown(gui, recipe, accumulator, accumulatorQuality);
            }
        }

        private static void ShowAccumulatorDropdown(ImGui gui, RecipeRow recipe, Entity currentAccumulator, Quality accumulatorQuality) {
            QualitySelectOptions<EntityAccumulator> options = new(LSs.ProductionTableSelectAccumulator, ExtraText: extraText) { SelectedQuality = accumulatorQuality };
            options.SelectedQualitiesChanged += gui => {
                gui.CloseDropdown();
                recipe.RecordUndo().ChangeVariant(accumulatorQuality, options.SelectedQuality);
            };
            gui.BuildObjectQualitySelectDropDown(Database.allAccumulators,
                newAccumulator => recipe.RecordUndo().ChangeVariant(currentAccumulator, newAccumulator.target),
                options);

            string extraText(EntityAccumulator x) => DataUtils.FormatAmount(x.AccumulatorCapacity(accumulatorQuality), UnitOfMeasure.Megajoule);
        }

        private static void ShowEntityDropdown(ImGui gui, RecipeRow recipe) {
            Quality quality = recipe.entity?.quality ?? Quality.Normal;

            QualitySelectOptions<EntityCrafter> options = null!;
            options = new(LSs.ProductionTableSelectCraftingEntity, DataUtils.FavoriteCrafter, ExtraText: extraText) { SelectedQuality = quality };
            string extraText(EntityCrafter x) => DataUtils.FormatAmount(x.CraftingSpeed(options.SelectedQuality!), UnitOfMeasure.Percent);

            if (recipe.entity != null) {
                options.SelectedQualitiesChanged += gui => {
                    _ = gui.CloseDropdown();
                    recipe.RecordUndo().entity = recipe.entity.With(options.SelectedQuality);
                };
            }

            gui.ShowDropDown(gui => {
                EntityCrafter? favoriteCrafter = recipe.recipe.target.crafters.AutoSelect(DataUtils.FavoriteCrafter);
                if (favoriteCrafter == recipe.entity?.target) { favoriteCrafter = null; }
                bool willResetFixed = favoriteCrafter == null, willResetBuilt = willResetFixed && recipe.fixedBuildings == 0;

                gui.BuildInlineObjectListAndButton(recipe.recipe.target.crafters, sel => {
                    if (recipe.entity?.target == sel) {
                        return;
                    }

                    _ = recipe.RecordUndo();
                    recipe.entity = sel.With(options.SelectedQuality);
                    if (!sel.energy.fuels.Contains(recipe.fuel?.target)) {
                        recipe.fuel = recipe.entity.target.energy.fuels.AutoSelect(DataUtils.FavoriteFuel).With(Quality.Normal);
                    }
                }, options);

                gui.AllocateSpacing(0.5f);

                if (recipe.fixedBuildings > 0f && (recipe.fixedFuel || recipe.fixedIngredient != null || recipe.fixedProduct != null || !recipe.hierarchyEnabled)) {
                    ButtonEvent evt = gui.BuildButton(LSs.ProductionTableClearFixedMultiplier);
                    if (willResetFixed) {
                        _ = evt.WithTooltip(gui, LSs.ProductionTableShortcutRightClick);
                    }
                    if (evt && gui.CloseDropdown()) {
                        recipe.RecordUndo().fixedBuildings = 0f;
                    }
                }

                if (recipe.hierarchyEnabled) {
                    using (gui.EnterRowWithHelpIcon(LSs.ProductionTableFixedBuildingsHint)) {
                        gui.allocator = RectAllocator.RemainingRow;
                        if (recipe.fixedBuildings > 0f && !recipe.fixedFuel && recipe.fixedIngredient == null && recipe.fixedProduct == null) {
                            ButtonEvent evt = gui.BuildButton(LSs.ProductionTableClearFixedBuildingCount);

                            if (willResetFixed) {
                                _ = evt.WithTooltip(gui, LSs.ProductionTableShortcutRightClick);
                            }

                            if (evt && gui.CloseDropdown()) {
                                recipe.RecordUndo().fixedBuildings = 0f;
                            }
                        }
                        else if (gui.BuildButton(LSs.ProductionTableSetFixedBuildingCount) && gui.CloseDropdown()) {
                            recipe.RecordUndo().fixedBuildings = recipe.buildingCount <= 0f ? 1f : recipe.buildingCount;
                            recipe.fixedFuel = false;
                            recipe.fixedIngredient = null;
                            recipe.fixedProduct = null;
                            recipe.FocusFixedCountOnNextDraw();
                        }
                    }
                }

                using (gui.EnterRowWithHelpIcon(LSs.ProductionTableBuiltBuildingCountHint)) {
                    gui.allocator = RectAllocator.RemainingRow;

                    if (recipe.builtBuildings != null) {
                        ButtonEvent evt = gui.BuildButton(LSs.ProductionTableClearBuiltBuildingCount);

                        if (willResetBuilt) {
                            _ = evt.WithTooltip(gui, LSs.ProductionTableShortcutRightClick);
                        }

                        if (evt && gui.CloseDropdown()) {
                            recipe.RecordUndo().builtBuildings = null;
                        }
                    }
                    else if (gui.BuildButton(LSs.ProductionTableSetBuiltBuildingCount) && gui.CloseDropdown()) {
                        recipe.RecordUndo().builtBuildings = Math.Max(0, Convert.ToInt32(Math.Ceiling(recipe.buildingCount)));
                        recipe.FocusBuiltCountOnNextDraw();
                    }
                }

                if (recipe.entity != null) {
                    using (gui.EnterRowWithHelpIcon(LSs.ProductionTableGenerateBuildingBlueprintHint)) {
                        gui.allocator = RectAllocator.RemainingRow;

                        if (gui.BuildButton(LSs.ProductionTableGenerateBuildingBlueprint) && gui.CloseDropdown()) {
                            BlueprintEntity entity = new BlueprintEntity { index = 1, name = recipe.entity.target.name };

                            if (!recipe.recipe.Is<Mechanics>()) {
                                entity.recipe = recipe.recipe.target.name;
                                entity.recipe_quality = recipe.recipe.quality.name;
                            }

                            var modules = recipe.usedModules.modules;

                            if (modules != null) {
                                int idx = 0;
                                foreach (var (module, count, beacon) in modules) {
                                    if (!beacon) {
                                        BlueprintItem item = new BlueprintItem {
                                            item = module.target.name,
                                            count = count,
                                            id = { name = module.target.name, quality = module.quality.name }
                                        };
                                        item.items.inInventory.AddRange(Enumerable.Range(idx, count).Select(i => new BlueprintInventoryItem { stack = i }));
                                        entity.items.Add(item);
                                        idx += count;
                                    }
                                }
                            }

                            if (Preferences.Instance.exportEntitiesWithFuelFilter && recipe.fuel is not null && !recipe.fuel.target.isPower) {
                                entity.SetFuel(recipe.fuel.target.name, recipe.fuel.quality.name);
                            }

                            BlueprintString bp = new BlueprintString(recipe.recipe.target.locName) { blueprint = { entities = { entity } } };
                            _ = SDL.SDL_SetClipboardText(bp.ToBpString());
                        }
                    }

                    if (recipe.recipe.target.crafters.Length > 1) {
                        BuildFavorites(gui, recipe.entity.target, LSs.ProductionTableAddBuildingToFavorites);
                    }
                }
            });
        }

        public override void BuildMenu(ImGui gui) {
            if (gui.BuildButton(LSs.ProductionTableMassSetAssembler) && gui.CloseDropdown()) {
                SelectSingleObjectPanel.Select(Database.allCrafters, new(LSs.ProductionTableMassSetAssembler, DataUtils.FavoriteCrafter), set => {
                    DataUtils.FavoriteCrafter.AddToFavorite(set, 10);

                    foreach (var recipe in view.GetRecipesRecursive()) {
                        if (recipe.recipe.target.crafters.Contains(set)) {
                            _ = recipe.RecordUndo();
                            recipe.entity = set.With(recipe.entity?.quality ?? Quality.Normal);

                            if (!set.energy.fuels.Contains(recipe.fuel?.target)) {
                                recipe.fuel = recipe.entity.target.energy.fuels.AutoSelect(DataUtils.FavoriteFuel).With(Quality.Normal);
                            }
                        }
                    }
                });
            }

            QualitySelectOptions<FactorioObject> options = new(null) { QualityHeader = LSs.ProductionTableMassSetQuality, SelectedQuality = null };
            if (gui.BuildQualityList(options) && gui.CloseDropdown()) {
                foreach (RecipeRow recipe in view.GetRecipesRecursive()) {
                    // null-forgiving: When options.Multiple is false and BuildQualityList returns true, SelectedQuality is not null.
                    recipe.RecordUndo().entity = recipe.entity?.With(options.SelectedQuality!);
                }
            }

            if (gui.BuildButton(LSs.ProductionTableMassSetFuel) && gui.CloseDropdown()) {
                SelectSingleObjectPanel.SelectWithQuality(Database.goods.all.Where(x => x.fuelValue > 0), new(LSs.ProductionTableMassSetFuel, DataUtils.FavoriteFuel), set => {
                    DataUtils.FavoriteFuel.AddToFavorite(set.target, 10);

                    foreach (var recipe in view.GetRecipesRecursive()) {
                        if (recipe.entity != null && recipe.entity.target.energy.fuels.Contains(set.target)) {
                            recipe.RecordUndo().fuel = set;
                        }
                    }
                });
            }

            if (gui.BuildButton(LSs.ShoppingList) && gui.CloseDropdown()) {
                view.BuildShoppingList(null);
            }

            if (gui.BuildButton(LSs.ProductionTableExportEntitiesToBlueprint) && gui.CloseDropdown()) {
                bool includeFuel = Preferences.Instance.exportEntitiesWithFuelFilter;
                var uniqueEntites = view
                    .GetRecipesRecursive()
                    .DistinctBy(row => (row.entity, row.recipe, includeFuel ? row.fuel : null));

                _ = BlueprintUtilities.ExportRecipiesAsBlueprint(view.projectPage!.name, uniqueEntites, includeFuel);
            }
        }
    }

    private class IngredientsColumn(ProductionTableView view) : ProductionTableDataColumn(view, LSs.ProductionTableHeaderIngredients, 32f, 16f, 100f, hasMenu: false, nameof(Preferences.ingredientsColumWidth)) {
        public override void BuildElement(ImGui gui, RecipeRow recipe) {
            var grid = gui.EnterInlineGrid(3f, 1f);

            if (recipe.isOverviewMode) {
                view.BuildTableIngredients(gui, recipe.subgroup, recipe.owner, ref grid);
            }
            else {
                foreach (var (goods, amount, link, variants) in recipe.Ingredients) {
                    grid.Next();
                    view.BuildGoodsIcon(gui, goods, link, amount, ProductDropdownType.Ingredient, recipe, recipe.linkRoot, HintLocations.OnProducingRecipes, variants);
                }
                if (recipe.fixedIngredient == Database.itemInput || recipe.showTotalIO) {
                    grid.Next();
                    view.BuildGoodsIcon(gui, recipe.hierarchyEnabled ? Database.itemInput : null, null, recipe.Ingredients.Where(i => i.Goods?.target is Item).Sum(i => i.Amount),
                        ProductDropdownType.Ingredient, recipe, recipe.linkRoot, HintLocations.None);
                }
            }
            grid.Dispose();
        }
    }

    private class ProductsColumn(ProductionTableView view) : ProductionTableDataColumn(view, LSs.ProductionTableHeaderProducts, 12f, 10f, 70f, hasMenu: false, nameof(Preferences.productsColumWidth)) {
        public override void BuildElement(ImGui gui, RecipeRow recipe) {
            var grid = gui.EnterInlineGrid(3f, 1f);
            if (recipe.isOverviewMode) {
                view.BuildTableProducts(gui, recipe.subgroup, recipe.owner, ref grid, false);
            }
            else {
                foreach (var (goods, amount, link, percentSpoiled) in recipe.Products) {
                    grid.Next();
                    if (recipe.recipe.target is Recipe { preserveProducts: true }) {
                        view.BuildGoodsIcon(gui, goods, link, amount, ProductDropdownType.Product, recipe, recipe.linkRoot, new() {
                            HintLocations = HintLocations.OnConsumingRecipes,
                            ExtraSpoilInformation = gui => gui.BuildText(LSs.ProductionTableOutputPreservedInMachine, TextBlockDisplayStyle.WrappedText)
                        });
                    }
                    else if (percentSpoiled == null) {
                        view.BuildGoodsIcon(gui, goods, link, amount, ProductDropdownType.Product, recipe, recipe.linkRoot, HintLocations.OnConsumingRecipes);
                    }
                    else if (percentSpoiled == 0) {
                        view.BuildGoodsIcon(gui, goods, link, amount, ProductDropdownType.Product, recipe, recipe.linkRoot,
                            new() { HintLocations = HintLocations.OnConsumingRecipes, ExtraSpoilInformation = gui => gui.BuildText(LSs.ProductionTableOutputAlwaysFresh) });
                    }
                    else {
                        view.BuildGoodsIcon(gui, goods, link, amount, ProductDropdownType.Product, recipe, recipe.linkRoot, new() {
                            HintLocations = HintLocations.OnConsumingRecipes,
                            ExtraSpoilInformation = gui => gui.BuildText(LSs.ProductionTableOutputFixedSpoilage.L(DataUtils.FormatAmount(percentSpoiled.Value, UnitOfMeasure.Percent)))
                        });
                    }
                }
                if (recipe.fixedProduct == Database.itemOutput || recipe.showTotalIO) {
                    grid.Next();
                    view.BuildGoodsIcon(gui, recipe.hierarchyEnabled ? Database.itemOutput : null, null, recipe.Products.Where(i => i.Goods?.target is Item).Sum(i => i.Amount),
                        ProductDropdownType.Product, recipe, recipe.linkRoot, HintLocations.None);
                }
            }
            grid.Dispose();
        }
    }

    private class ModulesColumn : ProductionTableDataColumn {
        private readonly VirtualScrollList<ProjectModuleTemplate> moduleTemplateList;
        private RecipeRow editingRecipeModules = null!; // null-forgiving: This is set as soon as we open a module dropdown.

        public ModulesColumn(ProductionTableView view) : base(view, LSs.ProductionTableHeaderModules, 10f, 7f, 16f, widthStorage: nameof(Preferences.modulesColumnWidth))
            => moduleTemplateList = new VirtualScrollList<ProjectModuleTemplate>(15f, new Vector2(20f, 2.5f), ModuleTemplateDrawer, collapsible: true);

        private void ModuleTemplateDrawer(ImGui gui, ProjectModuleTemplate element, int index) {
            var evt = gui.BuildContextMenuButton(element.name, icon: element.icon?.icon ?? default, disabled: !element.template.IsCompatibleWith(editingRecipeModules));

            if (evt == ButtonEvent.Click && gui.CloseDropdown()) {
                var copied = JsonUtils.Copy(element.template, editingRecipeModules, null);
                editingRecipeModules.RecordUndo().modules = copied;
                view.Rebuild();
            }
            else if (evt == ButtonEvent.MouseOver) {
                ShowModuleTemplateTooltip(gui, element.template);
            }
        }

        public override void BuildElement(ImGui gui, RecipeRow recipe) {
            if (recipe.isOverviewMode) {
                return;
            }

            if (recipe.entity == null || recipe.entity.target.allowedEffects == AllowedEffects.None || recipe.entity.target.allowedModuleCategories is []) {
                return;
            }

            using var grid = gui.EnterInlineGrid(3f);
            if (recipe.usedModules.modules == null || recipe.usedModules.modules.Length == 0) {
                drawItem(gui, null, 0);
            }
            else {
                bool wasBeacon = false;

                foreach (var (module, count, beacon) in recipe.usedModules.modules) {
                    if (beacon && !wasBeacon) {
                        wasBeacon = true;

                        if (recipe.usedModules.beacon != null) {
                            drawItem(gui, recipe.usedModules.beacon, recipe.usedModules.beaconCount);
                        }
                    }
                    drawItem(gui, module, count);
                }
            }

            void drawItem(ImGui gui, IObjectWithQuality<FactorioObject>? item, int count) {
                grid.Next();
                switch (gui.BuildFactorioObjectWithAmount(item, count, ButtonDisplayStyle.ProductionTableUnscaled)) {
                    case Click.Left:
                        ShowModuleDropDown(gui, recipe);
                        break;
                    case Click.Right when recipe.modules != null:
                        recipe.RecordUndo().RemoveFixedModules();
                        break;
                }
            }
        }

        private void ShowModuleTemplateTooltip(ImGui gui, ModuleTemplate template) => gui.ShowTooltip(imGui => {
            if (!template.IsCompatibleWith(editingRecipeModules)) {
                imGui.BuildText(LSs.ProductionTableModuleTemplateIncompatible, TextBlockDisplayStyle.WrappedText);
            }

            using var grid = imGui.EnterInlineGrid(3f, 1f);
            foreach (var module in template.list) {
                grid.Next();
                _ = imGui.BuildFactorioObjectWithAmount(module.module, module.fixedCount, ButtonDisplayStyle.ProductionTableUnscaled);
            }

            if (template.beacon != null) {
                grid.Next();
                _ = imGui.BuildFactorioObjectWithAmount(template.beacon, template.CalculateBeaconCount(), ButtonDisplayStyle.ProductionTableUnscaled);
                foreach (var module in template.beaconList) {
                    grid.Next();
                    _ = imGui.BuildFactorioObjectWithAmount(module.module, module.fixedCount, ButtonDisplayStyle.ProductionTableUnscaled);
                }
            }
        });

        private void ShowModuleDropDown(ImGui gui, RecipeRow recipe) {
            Module[] modules = [.. Database.allModules.Where(x => recipe.recipe.CanAcceptModule(x) && (recipe.entity?.target.CanAcceptModule(x.moduleSpecification) ?? false))];
            editingRecipeModules = recipe;
            moduleTemplateList.data = [.. Project.current.sharedModuleTemplates
                // null-forgiving: non-nullable collections are happy to report they don't contain null values.
                .Where(x => x.filterEntities.Count == 0 || x.filterEntities.Contains(recipe.entity?.target!))
                .OrderByDescending(x => x.template.IsCompatibleWith(recipe))];

            QualitySelectOptions<Module> options = new(LSs.ProductionTableSelectModules, DataUtils.FavoriteModule) { SelectedQuality = Quality.Normal };

            if (recipe.modules?.list.Count > 0) {
                options.SelectedQuality = recipe.modules.list[0].module.quality;
                options.SelectedQualitiesChanged += gui => {
                    _ = gui.CloseDropdown();
                    ModuleTemplateBuilder builder = recipe.modules.GetBuilder();
                    builder.list[0] = builder.list[0] with { module = builder.list[0].module.With(options.SelectedQuality) };
                    recipe.RecordUndo().modules = builder.Build(recipe);
                };
            }

            gui.ShowDropDown(dropGui => {
                if (recipe.modules != null && dropGui.BuildButton(LSs.ProductionTableUseDefaultModules).WithTooltip(dropGui, LSs.ProductionTableShortcutRightClick) && dropGui.CloseDropdown()) {
                    recipe.RemoveFixedModules();
                }

                if (recipe.entity?.target.moduleSlots > 0) {
                    dropGui.BuildInlineObjectListAndButton(modules, (Module m) => recipe.SetFixedModule(m.With(options.SelectedQuality)), options);
                }

                if (moduleTemplateList.data.Count > 0) {
                    dropGui.BuildText(LSs.ProductionTableUseModuleTemplate, Font.subheader);
                    moduleTemplateList.Build(dropGui);
                }
                if (dropGui.BuildButton(LSs.ProductionTableConfigureModuleTemplates) && dropGui.CloseDropdown()) {
                    ModuleTemplateConfiguration.Show();
                }

                if (dropGui.BuildButton(LSs.ProductionTableCustomizeModules) && dropGui.CloseDropdown()) {
                    ModuleCustomizationScreen.Show(recipe);
                }
            });
        }

        public override void BuildMenu(ImGui gui) {
            var model = view.model;

            gui.BuildText(LSs.ProductionTableAutoModules, Font.subheader);
            ModuleFillerParametersScreen.BuildSimple(gui, model.modules!); // null-forgiving: owner is a ProjectPage, so modules is not null.
            if (gui.BuildButton(LSs.ProductionTableModuleSettings) && gui.CloseDropdown()) {
                ModuleFillerParametersScreen.Show(model.modules!);
            }
        }
    }

    public static void BuildFavorites(ImGui imgui, FactorioObject? obj, LocalizableString0 prompt) {
        if (obj == null) {
            return;
        }

        bool isFavorite = Project.current.preferences.favorites.Contains(obj);
        using (imgui.EnterRow(0.5f, RectAllocator.LeftRow)) {
            imgui.BuildIcon(isFavorite ? Icon.StarFull : Icon.StarEmpty);
            imgui.RemainingRow().BuildText(isFavorite ? LSs.Favorite : prompt);
        }
        if (imgui.OnClick(imgui.lastRect)) {
            Project.current.preferences.ToggleFavorite(obj);
        }
    }

    public override float CalculateWidth() => flatHierarchyBuilder.width;

    public static void CreateProductionSheet() => ProjectPageSettingsPanel.Show(null, (name, icon) => MainScreen.Instance.AddProjectPage(name, icon, typeof(ProductionTable), true, true));

    private static readonly IComparer<Goods> DefaultVariantOrdering =
        new DataUtils.FactorioObjectComparer<Goods>((x, y) => (y.ApproximateFlow() / MathF.Abs(y.Cost())).CompareTo(x.ApproximateFlow() / MathF.Abs(x.Cost())));

    private enum ProductDropdownType {
        DesiredProduct,
        Fuel,
        Ingredient,
        Product,
        DesiredIngredient,
    }

    private void RebuildIf(bool rebuild) {
        if (rebuild) {
            Rebuild();
        }
    }

    private static void CreateNewProductionTable(IObjectWithQuality<Goods> goods, float amount) {
        var page = MainScreen.Instance.AddProjectPage(goods.target.locName, goods.target, typeof(ProductionTable), true, false);
        ProductionTable content = (ProductionTable)page.content;
        ProductionLink link = new ProductionLink(content, goods) { amount = amount > 0 ? amount : 1 };
        content.links.Add(link);
        content.RebuildLinkMap();
    }

    /// <param name="recipe">If not <see langword="null"/>, the source icon for this dropdown is associated with this <see cref="RecipeRow"/>.
    /// If <see langword="null"/>, the icon is from a non-<see cref="RecipeRow"/> location, such as the desired products box or a collapsed
    /// sub-table.</param>
    /// <param name="variants">If not <see langword="null"/>, the fluid variants (temperatures) that are valid for this particular ingredient.
    /// This may exclude some fluid temperatures that aren't acceptable for the current <paramref name="recipe"/>. This is always
    /// <see cref="null"/> if the source icon is not directly owned by a <see cref="RecipeRow"/>.</param>
    private void OpenProductDropdown(ImGui targetGui, Rect rect, IObjectWithQuality<Goods> goods, float amount, IProductionLink? iLink,
        ProductDropdownType type, RecipeRow? recipe, ProductionTable context, Goods[]? variants = null) {

        if (InputSystem.Instance.control && InputSystem.Instance.shift) {
            var bestRecipe = goods.target.production
                // Only currently available recipes are considered.
                .Where(r => r.IsAccessible() && r.IsAutomatableWithCurrentMilestones())
                // Barreling recipes should be the last resort, so we sort them to the end. There's too many to weight them by cost.
                .OrderBy(r => r.name.Contains("barrel", StringComparison.OrdinalIgnoreCase))
                // Sort by recipe waste percentage, then cost at current milestones.
                .ThenBy(r => CostAnalysis.InstanceAtMilestones.recipeWastePercentage[r])
                .ThenBy(r => r.Cost(true))
                .FirstOrDefault();

            if (bestRecipe != null) {
                RebuildIf(context.CreateLink(goods));
                context.AddRecipe(bestRecipe.With(goods.quality), DefaultVariantOrdering);
            }
            return;
        }

        if (InputSystem.Instance.shift) {
            Project.current.preferences.SetSourceResource(goods.target, !goods.IsSourceResource());
            targetGui.Rebuild();
            return;
        }

        var comparer = DataUtils.GetRecipeComparerFor(goods.target);

        IObjectWithQuality<Goods>? selectedFuel = null;
        IObjectWithQuality<Goods>? spentFuel = null;

        async void addRecipe(RecipeOrTechnology rec) {
            IObjectWithQuality<RecipeOrTechnology> qualityRecipe = rec.With(goods.quality);
            RebuildIf(context.CreateLink(goods));

            if (!context.Contains(qualityRecipe) || (await MessageBox.Show(LSs.ProductionTableAlertRecipeExists, LSs.ProductionTableQueryAddCopy.L(rec.locName), LSs.ProductionTableAddCopy, LSs.Cancel)).choice) {
                context.AddRecipe(qualityRecipe, DefaultVariantOrdering, selectedFuel, spentFuel);
            }
        }

        if (InputSystem.Instance.control) {
            bool isInput = type <= ProductDropdownType.Ingredient;
            var recipeList = isInput ? goods.target.production : goods.target.usages;

            if (recipeList.SelectSingle(out _) is Recipe selected) {
                addRecipe(selected);
                return;
            }
        }

        Recipe[] fuelUseList = [.. goods.target.fuelFor.OfType<EntityCrafter>()
            .SelectMany(e => e.recipes).OfType<Recipe>()
            .Distinct().OrderBy(e => e, DataUtils.DefaultRecipeOrdering)];

        Recipe[] spentFuelRecipes = [.. goods   .target.miscSources.OfType<Item>()
            .SelectMany(e => e.fuelFor.OfType<EntityCrafter>())
            .SelectMany(e => e.recipes).OfType<Recipe>()
            .Distinct().OrderBy(e => e, DataUtils.DefaultRecipeOrdering)];

        targetGui.ShowDropDown(rect, dropDownContent, new Padding(1f), 25f);

        void dropDownContent(ImGui gui) {
            if (type == ProductDropdownType.Fuel && recipe?.entity != null) {
                EntityEnergy? energy = recipe.entity.target.energy;

                if (energy == null || energy.fuels.Length == 0) {
                    gui.BuildText(LSs.ProductionTableAlertNoKnownFuels);
                }
                else if (energy.fuels.Length > 1 || energy.fuels[0] != recipe.fuel?.target) {
                    Func<Goods, string> fuelDisplayFunc = energy.type == EntityEnergyType.FluidHeat
                         ? g => DataUtils.FormatAmount(g.fluid?.heatValue ?? 0, UnitOfMeasure.Megajoule)
                         : g => DataUtils.FormatAmount(g.fuelValue, UnitOfMeasure.Megajoule);

                    BuildFavorites(gui, recipe.fuel!.target, LSs.ProductionTableAddFuelToFavorites);
                    gui.BuildInlineObjectListAndButton(energy.fuels, fuel => recipe.RecordUndo().fuel = fuel.With(Quality.Normal),
                        new ObjectSelectOptions<Goods>(LSs.ProductionTableSelectFuel, DataUtils.FavoriteFuel, ExtraText: fuelDisplayFunc));
                }
            }

            if (variants != null) {
                gui.BuildText(LSs.ProductionTableAcceptedFluids);
                using (var grid = gui.EnterInlineGrid(3f)) {
                    foreach (var variant in variants) {
                        grid.Next();

                        if (gui.BuildFactorioObjectButton(variant, ButtonDisplayStyle.ProductionTableScaled(variant == goods.target ? SchemeColor.Primary : SchemeColor.None),
                            tooltipOptions: HintLocations.OnProducingRecipes) == Click.Left && variant != goods.target) {

                            // null-forgiving: If variants is not null, neither is recipe: Only the call from BuildGoodsIcon sets variants,
                            // and the only call to BuildGoodsIcon that sets variants also sets recipe.
                            recipe!.RecordUndo().ChangeVariant(goods.target, variant);

                            if (recipe!.fixedIngredient == goods) {
                                // variants are always fluids, so this could also be .With(Quality.Normal).
                                recipe.fixedIngredient = variant.With(recipe.recipe.quality);
                            }

                            goods = variant.With(recipe.recipe.quality);
                            comparer = DataUtils.GetRecipeComparerFor(goods.target);
                            if (recipe.FindLink(goods, out iLink) && iLink.flags.HasFlag(ProductionLink.Flags.HasProduction)) {
                                _ = gui.CloseDropdown();
                            }
                        }
                    }
                }

                gui.allocator = RectAllocator.Stretch;
            }

            if (iLink != null) {
                foreach (string warning in iLink.LinkWarnings) {
                    gui.BuildText(warning, TextBlockDisplayStyle.ErrorText);
                }
            }

            #region Recipe selection
            int numberOfShownRecipes = 0;

            Recipe[] allProduction = goods.target.production;

            if (goods == Database.science) {
                if (gui.BuildButton(LSs.ProductionTableAddTechnology) && gui.CloseDropdown()) {
                    SelectMultiObjectPanel.Select(Database.technologies.all, new(LSs.ProductionTableAddTechnology, Multiple: true, Checkmark: context.Contains, YellowMark: context.ContainsAnywhere),
                        r => context.AddRecipe(r.With(Quality.Normal), DefaultVariantOrdering));
                }
            }
            else if (type <= ProductDropdownType.Ingredient && allProduction.Length > 0) {
                gui.BuildInlineObjectListAndButton(allProduction, addRecipe, new(LSs.ProductionTableAddProductionRecipe, comparer, 6, true, context.Contains, context.ContainsAnywhere));
                numberOfShownRecipes += allProduction.Length;

                if (iLink == null) {
                    Rect iconRect = new Rect(gui.lastRect.Right - 2f, gui.lastRect.Top, 2f, 2f);
                    gui.DrawIcon(iconRect.Expand(-0.2f), Icon.OpenNew, gui.textColor);
                    var evt = gui.BuildButton(iconRect, SchemeColor.None, SchemeColor.Grey);

                    if (evt == ButtonEvent.Click && gui.CloseDropdown()) {
                        CreateNewProductionTable(goods, amount);
                    }
                    else if (evt == ButtonEvent.MouseOver) {
                        gui.ShowTooltip(iconRect, LSs.ProductionTableCreateTableFor.L(goods.target.locName));
                    }
                }
            }

            if (type <= ProductDropdownType.Ingredient && spentFuelRecipes.Length > 0) {
                gui.BuildInlineObjectListAndButton(
                    spentFuelRecipes,
                    (x) => { spentFuel = goods; addRecipe(x); },
                    new(LSs.ProductionTableProduceAsSpentFuel,
                    DataUtils.AlreadySortedRecipe,
                    3,
                    true,
                    context.Contains,
                    context.ContainsAnywhere));
                numberOfShownRecipes += spentFuelRecipes.Length;
            }

            if (type >= ProductDropdownType.Product && goods.target.usages.Length > 0) {
                gui.BuildInlineObjectListAndButton(
                    goods.target.usages,
                    addRecipe,
                    new(LSs.ProductionTableAddConsumptionRecipe,
                    DataUtils.DefaultRecipeOrdering,
                    6,
                    true,
                    context.Contains,
                    context.ContainsAnywhere));
                numberOfShownRecipes += goods.target.usages.Length;
            }

            if (type >= ProductDropdownType.Product && fuelUseList.Length > 0) {
                gui.BuildInlineObjectListAndButton(
                    fuelUseList,
                    (x) => { selectedFuel = goods; addRecipe(x); },
                    new(LSs.ProductionTableAddFuelUsage,
                    DataUtils.AlreadySortedRecipe,
                    6,
                    true,
                    context.Contains,
                    context.ContainsAnywhere));
                numberOfShownRecipes += fuelUseList.Length;
            }

            if (type >= ProductDropdownType.Product && Database.allSciencePacks.Contains(goods.target)
                && gui.BuildButton(LSs.ProductionTableAddConsumptionTechnology) && gui.CloseDropdown()) {
                // Select from the technologies that consume this science pack.
                SelectMultiObjectPanel.Select(Database.technologies.all.Where(t => t.ingredients.Select(i => i.goods).Contains(goods.target)),
                    new(LSs.ProductionTableAddTechnology, Multiple: true, Checkmark: context.Contains, YellowMark: context.ContainsAnywhere), addRecipe);
            }

            if (type >= ProductDropdownType.Product && allProduction.Length > 0) {
                gui.BuildInlineObjectListAndButton(allProduction, addRecipe, new(LSs.ProductionTableAddProductionRecipe, comparer, 1, true, context.Contains, context.ContainsAnywhere));
                numberOfShownRecipes += allProduction.Length;
            }

            if (numberOfShownRecipes > 1) {
                gui.BuildText(LSs.ProductionTableAddMultipleHint, TextBlockDisplayStyle.HintText);
            }
            #endregion

            #region Link management
            ProductionLink? link = iLink as ProductionLink;

            if (link != null && gui.BuildCheckBox(LSs.ProductionTableAllowOverproduction, link.algorithm == LinkAlgorithm.AllowOverProduction, out bool newValue)) {
                link.RecordUndo().algorithm = newValue ? LinkAlgorithm.AllowOverProduction : LinkAlgorithm.Match;
            }

            if (iLink != null && gui.BuildButton(LSs.ProductionTableViewLinkSummary) && gui.CloseDropdown()) {
                ProductionLinkSummaryScreen.Show(iLink.DisplayLink);
            }

            if (link != null && link.owner == context) {
                if (link.amount != 0) {
                    gui.BuildText(LSs.ProductionTableCannotUnlink.L(goods.target.locName), TextBlockDisplayStyle.WrappedText);
                }
                else {
                    gui.BuildText(LSs.ProductionTableCurrentlyLinked.L(goods.target.locName), TextBlockDisplayStyle.WrappedText);
                }

                if (type is ProductDropdownType.DesiredIngredient or ProductDropdownType.DesiredProduct) {
                    if (gui.BuildButton(LSs.ProductionTableRemoveDesiredProduct) && gui.CloseDropdown()) {
                        link.RecordUndo().amount = 0;
                    }

                    if (gui.BuildButton(LSs.ProductionTableRemoveAndUnlinkDesiredProduct).WithTooltip(gui, LSs.ProductionTableShortcutRightClick) && gui.CloseDropdown()) {
                        RebuildIf(link.Destroy());
                    }
                }
                else if (link.amount == 0 && gui.BuildButton(LSs.ProductionTableUnlink).WithTooltip(gui, LSs.ProductionTableShortcutRightClick) && gui.CloseDropdown()) {
                    RebuildIf(link.Destroy());
                }
            }
            else if (goods != null) {
                if (link != null) {
                    gui.BuildText(LSs.ProductionTableLinkedInParent.L(goods.target.locName), TextBlockDisplayStyle.WrappedText);
                }
                else if (iLink != null) {
                    string implicitLink = LSs.ProductionTableImplicitlyLinked.L(goods.target.locName, goods.quality.locName);
                    gui.BuildText(implicitLink, TextBlockDisplayStyle.WrappedText);
                    if (gui.BuildButton(LSs.ProductionTableCreateLink).WithTooltip(gui, LSs.ProductionTableShortcutRightClick) && gui.CloseDropdown()) {
                        RebuildIf(context.CreateLink(goods));
                    }
                }
                else if (goods.target.isLinkable) {
                    gui.BuildText(LSs.ProductionTableNotLinked.L(goods.target.locName), TextBlockDisplayStyle.WrappedText);
                    if (gui.BuildButton(LSs.ProductionTableCreateLink).WithTooltip(gui, LSs.ProductionTableShortcutRightClick) && gui.CloseDropdown()) {
                        RebuildIf(context.CreateLink(goods));
                    }
                }
            }
            #endregion

            #region Fixed production/consumption
            if (goods != null && recipe != null && recipe.hierarchyEnabled) {
                if (recipe.fixedBuildings == 0
                    || (type == ProductDropdownType.Fuel && !recipe.fixedFuel)
                    || (type == ProductDropdownType.Ingredient && recipe.fixedIngredient != goods)
                    || (type == ProductDropdownType.Product && recipe.fixedProduct != goods)) {
                    string? prompt = type switch {
                        ProductDropdownType.Fuel => LSs.ProductionTableSetFixedFuel,
                        ProductDropdownType.Ingredient => LSs.ProductionTableSetFixedIngredient,
                        ProductDropdownType.Product => LSs.ProductionTableSetFixedProduct,
                        _ => null
                    };
                    if (prompt != null) {
                        ButtonEvent evt;
                        if (recipe.fixedBuildings == 0) {
                            evt = gui.BuildButton(prompt);
                        }
                        else {
                            using (gui.EnterRowWithHelpIcon(LSs.ProductionTableSetFixedWillReplace)) {
                                gui.allocator = RectAllocator.RemainingRow;
                                evt = gui.BuildButton(prompt);
                            }
                        }
                        if (evt && gui.CloseDropdown()) {
                            recipe.RecordUndo().fixedBuildings = recipe.buildingCount <= 0 ? 1 : recipe.buildingCount;
                            switch (type) {
                                case ProductDropdownType.Fuel:
                                    recipe.fixedFuel = true;
                                    recipe.fixedIngredient = null;
                                    recipe.fixedProduct = null;
                                    break;
                                case ProductDropdownType.Ingredient:
                                    recipe.fixedFuel = false;
                                    recipe.fixedIngredient = goods;
                                    recipe.fixedProduct = null;
                                    break;
                                case ProductDropdownType.Product:
                                    recipe.fixedFuel = false;
                                    recipe.fixedIngredient = null;
                                    recipe.fixedProduct = goods;
                                    break;
                                default:
                                    break;
                            }
                            recipe.FocusFixedCountOnNextDraw();
                            targetGui.Rebuild();
                        }
                    }
                }

                if (recipe.fixedBuildings != 0
                    && ((type == ProductDropdownType.Fuel && recipe.fixedFuel)
                    || (type == ProductDropdownType.Ingredient && recipe.fixedIngredient == goods)
                    || (type == ProductDropdownType.Product && recipe.fixedProduct == goods))) {
                    string? prompt = type switch {
                        ProductDropdownType.Fuel => LSs.ProductionTableClearFixedFuel,
                        ProductDropdownType.Ingredient => LSs.ProductionTableClearFixedIngredient,
                        ProductDropdownType.Product => LSs.ProductionTableClearFixedProduct,
                        _ => null
                    };
                    if (prompt != null && gui.BuildButton(prompt) && gui.CloseDropdown()) {
                        recipe.RecordUndo().fixedBuildings = 0;
                    }
                    targetGui.Rebuild();
                }
            }
            #endregion

            if (goods is { target: Item item }) {
                BuildBeltInserterInfo(gui, item, amount, recipe?.buildingCount ?? 0);
            }
        }
    }

    public override void SetSearchQuery(SearchQuery query) {
        _ = model.Search(query);
        bodyContent.Rebuild();
    }

    private void DrawDesiredProduct(ImGui gui, ProductionLink element) {
        gui.allocator = RectAllocator.Stretch;
        gui.spacing = 0f;
        SchemeColor iconColor = SchemeColor.Primary;

        if (element.flags.HasFlags(ProductionLink.Flags.LinkNotMatched)) {
            if (element.linkFlow > element.amount && CheckPossibleOverproducing(element)) {
                // Actual overproduction occurred for this product
                iconColor = SchemeColor.Magenta;
            }
            else {
                // There is not enough production (most likely none at all, otherwise the analyzer will have a deadlock)
                iconColor = SchemeColor.Error;
            }
        }

        ObjectTooltipOptions tooltipOptions = element.amount < 0 ? HintLocations.OnConsumingRecipes : HintLocations.OnProducingRecipes;
        if (element.LinkWarnings is IEnumerable<string> warnings) {
            tooltipOptions.DrawBelowHeader = gui => {
                foreach (string warning in warnings) {
                    gui.BuildText(warning, TextBlockDisplayStyle.ErrorText);
                }
            };
        }

        DisplayAmount amount = new(element.amount, element.goods.target.flowUnitOfMeasure);
        switch (gui.BuildFactorioObjectWithEditableAmount(element.goods, amount, ButtonDisplayStyle.ProductionTableScaled(iconColor), tooltipOptions: tooltipOptions)) {
            case GoodsWithAmountEvent.LeftButtonClick:
                OpenProductDropdown(gui, gui.lastRect, element.goods, element.amount, element,
                    element.amount < 0 ? ProductDropdownType.DesiredIngredient : ProductDropdownType.DesiredProduct, null, element.owner);
                break;
            case GoodsWithAmountEvent.RightButtonClick:
                RebuildIf(element.Destroy());
                break;
            case GoodsWithAmountEvent.TextEditing when amount.Value != 0:
                element.RecordUndo().amount = amount.Value;
                break;
        }
    }

    public override void Rebuild(bool visualOnly = false) {
        flatHierarchyBuilder.SetData(model);
        base.Rebuild(visualOnly);
    }

    /// <param name="recipe">If not <see langword="null"/>, this icon is directly associated with this <see cref="RecipeRow"/>.
    /// If <see langword="null"/>, the icon is from a non-<see cref="RecipeRow"/> location, such as the desired products box or a collapsed
    /// sub-table.</param>
    /// <param name="variants">If not <see langword="null"/>, the fluid variants (temperatures) that are valid for this particular ingredient.
    /// This may exclude some fluid temperatures that aren't acceptable for the current <paramref name="recipe"/>. This is always
    /// <see cref="null"/> if <paramref name="recipe"/> is <see cref="null"/>.</param>
    private void BuildGoodsIcon(ImGui gui, IObjectWithQuality<Goods>? goods, IProductionLink? link, float amount, ProductDropdownType dropdownType,
        RecipeRow? recipe, ProductionTable context, ObjectTooltipOptions tooltipOptions, Goods[]? variants = null) {

        SchemeColor iconColor;
        bool drawTransparent = false;

        if (link != null) {
            // The icon is part of a production link
            if ((link.flags & (ProductionLink.Flags.HasProductionAndConsumption | ProductionLink.Flags.LinkRecursiveNotMatched | ProductionLink.Flags.ChildNotMatched)) != ProductionLink.Flags.HasProductionAndConsumption) {
                // The link has production and consumption sides, but either the production and consumption is not matched, or 'child was not matched'
                iconColor = SchemeColor.Error;
            }
            // TODO (shpaass/yafc-ce/issues/269): refactor enum check into explicit instead of ordinal instructions
            else if (dropdownType >= ProductDropdownType.Product && CheckPossibleOverproducing(link as ProductionLink)) {
                // Actual overproduction occurred in the recipe
                iconColor = SchemeColor.Magenta;
            }
            else if (link.owner != context) {
                // It is a foreign link (e.g. not part of the sub group)
                iconColor = SchemeColor.Secondary;
            }
            else {
                // Regular (nothing going on) linked icon
                iconColor = SchemeColor.Primary;
            }
            drawTransparent = link is not ProductionLink;
        }
        else {
            // The icon is not part of a production link
            iconColor = goods.IsSourceResource() ? SchemeColor.Green : SchemeColor.None;
        }

        // TODO: See https://github.com/have-fun-was-taken/yafc-ce/issues/91
        //       and https://github.com/have-fun-was-taken/yafc-ce/pull/86#discussion_r1550377021
        SchemeColor textColor = flatHierarchyBuilder.nextRowTextColor;

        if (!flatHierarchyBuilder.nextRowIsHighlighted) {
            textColor = SchemeColor.None;
        }
        else if (recipe is { enabled: false }) {
            textColor = SchemeColor.BackgroundTextFaint;
        }

        GoodsWithAmountEvent evt;
        DisplayAmount displayAmount = new(amount, goods?.target.flowUnitOfMeasure ?? UnitOfMeasure.None);

        if (link?.LinkWarnings is IEnumerable<string> warnings) {
            tooltipOptions.DrawBelowHeader += gui => {
                foreach (string warning in warnings) {
                    gui.BuildText(warning, TextBlockDisplayStyle.ErrorText);
                }
            };
        }

        if (recipe != null && recipe.fixedBuildings > 0 && recipe.hierarchyEnabled
            && ((dropdownType == ProductDropdownType.Fuel && recipe.fixedFuel)
            || (dropdownType == ProductDropdownType.Ingredient && recipe.fixedIngredient == goods)
            || (dropdownType == ProductDropdownType.Product && recipe.fixedProduct == goods))) {

            evt = gui.BuildFactorioObjectWithEditableAmount(goods, displayAmount, ButtonDisplayStyle.ProductionTableScaled(iconColor, drawTransparent), tooltipOptions: tooltipOptions,
                setKeyboardFocus: recipe.ShouldFocusFixedCountThisTime());
        }
        else {
            evt = (GoodsWithAmountEvent)gui.BuildFactorioObjectWithAmount(goods, displayAmount, ButtonDisplayStyle.ProductionTableScaled(iconColor, drawTransparent),
                TextBlockDisplayStyle.Centered with { Color = textColor }, tooltipOptions: tooltipOptions);
        }

        switch (evt) {
            case GoodsWithAmountEvent.LeftButtonClick when goods is not null:
                OpenProductDropdown(gui, gui.lastRect, goods, amount, link, dropdownType, recipe, context, variants);
                break;
            case GoodsWithAmountEvent.RightButtonClick when goods is not null and { target.isLinkable: true } && (link is not ProductionLink || link.owner != context):
                RebuildIf(context.CreateLink(goods));
                break;
            case GoodsWithAmountEvent.RightButtonClick when link is ProductionLink { amount: 0 } pLink && link.owner == context:
                RebuildIf(pLink.Destroy());
                break;
            case GoodsWithAmountEvent.TextEditing when displayAmount.Value >= 0:
                // The amount is always stored in fixedBuildings. Scale it to match the requested change to this item.
                recipe!.RecordUndo().fixedBuildings *= displayAmount.Value / amount;
                break;
        }
    }

    /// <summary>
    /// Checks some criteria that are necessary but not sufficient to consider something overproduced.
    /// </summary>
    private static bool CheckPossibleOverproducing(ProductionLink? link) => link?.algorithm == LinkAlgorithm.AllowOverProduction && link.flags.HasFlag(ProductionLink.Flags.LinkNotMatched);

    /// <param name="isForSummary">If <see langword="true"/>, this call is for a summary box, at the top of a root-level or nested table.
    /// If <see langword="false"/>, this call is for collapsed recipe row.</param>
    /// <param name="initializeDrawArea">If not <see langword="null"/>, this will be called before drawing the first element. This method may choose not to draw
    /// some or all of a table's extra products, and this lets the caller suppress the surrounding UI elements if no product end up being drawn.</param>
    private void BuildTableProducts(ImGui gui, ProductionTable table, ProductionTable context, ref ImGuiUtils.InlineGridBuilder grid,
        bool isForSummary, Action<ImGui>? initializeDrawArea = null) {

        var flow = table.flow;
        int firstProduct = Array.BinarySearch(flow, new ProductionTableFlow(Database.voidEnergy, 1e-9f, null), model);

        if (firstProduct < 0) {
            firstProduct = ~firstProduct;
        }

        for (int i = firstProduct; i < flow.Length; i++) {
            float amt = flow[i].amount;

            if (isForSummary) {
                amt -= flow[i].link?.amount ?? 0;
            }

            if (amt <= 0f) {
                continue;
            }

            initializeDrawArea?.Invoke(gui);
            initializeDrawArea = null;

            grid.Next();
            BuildGoodsIcon(gui, flow[i].goods, flow[i].link, amt, ProductDropdownType.Product, null, context, HintLocations.OnConsumingRecipes);
        }
    }

    private static void FillRecipeList(ProductionTable table, List<RecipeRow> list) {
        foreach (var recipe in table.recipes) {
            list.Add(recipe);

            if (recipe.subgroup != null) {
                FillRecipeList(recipe.subgroup, list);
            }
        }
    }

    private static void FillLinkList(ProductionTable table, List<IProductionLink> list) {
        list.AddRange(table.allLinks);
        foreach (var recipe in table.recipes) {
            if (recipe.subgroup != null) {
                FillLinkList(recipe.subgroup, list);
            }
        }
    }

    private List<RecipeRow> GetRecipesRecursive() {
        List<RecipeRow> list = [];
        FillRecipeList(model, list);
        return list;
    }

    private static List<RecipeRow> GetRecipesRecursive(RecipeRow recipeRoot) {
        List<RecipeRow> list = [recipeRoot];

        if (recipeRoot.subgroup != null) {
            FillRecipeList(recipeRoot.subgroup, list);
        }

        return list;
    }

    private void BuildShoppingList(RecipeRow? recipeRoot) => ShoppingListScreen.Show(recipeRoot == null ? GetRecipesRecursive() : GetRecipesRecursive(recipeRoot));

    private static void BuildBeltInserterInfo(ImGui gui, Item item, float amount, float buildingCount) {
        var preferences = Project.current.preferences;
        var belt = preferences.defaultBelt;
        var inserter = preferences.defaultInserter;

        if (belt == null || inserter == null) {
            return;
        }

        float beltCount = amount / belt.beltItemsPerSecond;
        float buildingsPerHalfBelt = belt.beltItemsPerSecond * buildingCount / (amount * 2f);
        bool click = false;

        using (gui.EnterRow()) {
            click |= gui.BuildFactorioObjectButton(belt, ButtonDisplayStyle.Default) == Click.Left;
            gui.BuildText(DataUtils.FormatAmount(beltCount, UnitOfMeasure.None));

            if (buildingsPerHalfBelt > 0f) {
                gui.BuildText(LSs.ProductionTableBuildingsPerHalfBelt.L(DataUtils.FormatAmount(buildingsPerHalfBelt, UnitOfMeasure.None)));
            }
        }

        using (gui.EnterRow()) {
            int capacity = Math.Min(item.stackSize, preferences.inserterCapacity);
            float inserterBase = inserter.inserterSwingTime * amount / capacity;
            click |= gui.BuildFactorioObjectButton(inserter, ButtonDisplayStyle.Default) == Click.Left;
            string text = DataUtils.FormatAmount(inserterBase, UnitOfMeasure.None);

            if (buildingCount > 1) {
                text = LSs.ProductionTableInsertersPerBuilding.L(DataUtils.FormatAmount(inserterBase, UnitOfMeasure.None),
                    DataUtils.FormatAmount(inserterBase / buildingCount, UnitOfMeasure.None));
            }

            gui.BuildText(text);

            if (capacity > 1) {
                float withBeltSwingTime = inserter.inserterSwingTime + (2f * (capacity - 1.5f) / belt.beltItemsPerSecond);
                float inserterToBelt = amount * withBeltSwingTime / capacity;
                click |= gui.BuildFactorioObjectButton(belt, ButtonDisplayStyle.Default) == Click.Left;
                gui.AllocateSpacing(-1.5f);
                click |= gui.BuildFactorioObjectButton(inserter, ButtonDisplayStyle.Default) == Click.Left;
                text = LSs.ProductionTableApproximateInserters.L(DataUtils.FormatAmount(inserterToBelt, UnitOfMeasure.None));

                if (buildingCount > 1) {
                    text = LSs.ProductionTableApproximateInsertersPerBuilding.L(DataUtils.FormatAmount(inserterToBelt, UnitOfMeasure.None),
                        DataUtils.FormatAmount(inserterToBelt / buildingCount, UnitOfMeasure.None));
                }

                gui.BuildText(text);
            }
        }

        if (click && gui.CloseDropdown()) {
            PreferencesScreen.ShowProgression();
        }
    }

    private void BuildTableIngredients(ImGui gui, ProductionTable table, ProductionTable context, ref ImGuiUtils.InlineGridBuilder grid) {
        foreach (var flow in table.flow) {
            if (flow.amount >= 0f) {
                break;
            }

            grid.Next();
            BuildGoodsIcon(gui, flow.goods, flow.link, -flow.amount, ProductDropdownType.Ingredient, null, context, HintLocations.OnProducingRecipes);
        }
    }

    private static void DrawRecipeTagSelect(ImGui gui, RecipeRow recipe) {
        using (gui.EnterRow()) {
            for (int i = 0; i < tagIcons.Length; i++) {
                var (icon, color) = tagIcons[i];
                bool selected = i == recipe.tag;
                gui.BuildIcon(icon, color: selected ? SchemeColor.Background : color);

                if (selected) {
                    gui.DrawRectangle(gui.lastRect, color);
                }
                else {
                    var evt = gui.BuildButton(gui.lastRect, SchemeColor.None, SchemeColor.BackgroundAlt, SchemeColor.BackgroundAlt);

                    if (evt) {
                        recipe.RecordUndo(true).tag = i;
                    }
                }
            }
        }
    }

    protected override void BuildHeader(ImGui gui) {
        base.BuildHeader(gui);
        flatHierarchyBuilder.BuildHeader(gui);
    }

    protected override void BuildPageTooltip(ImGui gui, ProductionTable contents) {
        foreach (var link in contents.allLinks) {
            if (link.amount != 0f) {
                using (gui.EnterRow()) {
                    gui.BuildFactorioObjectIcon(link.goods);
                    using (gui.EnterGroup(default, RectAllocator.LeftAlign, spacing: 0)) {
                        gui.BuildText(link.goods.target.locName);
                        gui.BuildText(DataUtils.FormatAmount(link.amount, link.goods.target.flowUnitOfMeasure));
                    }
                }
            }
        }

        foreach (var row in contents.recipes) {
            if (row.fixedBuildings != 0 && row.entity != null) {
                using (gui.EnterRow()) {
                    gui.BuildFactorioObjectIcon(row.recipe);
                    using (gui.EnterGroup(default, RectAllocator.LeftAlign, spacing: 0)) {
                        gui.BuildText(row.recipe.target.locName);
                        gui.BuildText(row.entity.target.locName + ": " + DataUtils.FormatAmount(row.fixedBuildings, UnitOfMeasure.None));
                    }
                }
            }

            if (row.subgroup != null) {
                BuildPageTooltip(gui, row.subgroup);
            }
        }
    }

    private static readonly Dictionary<WarningFlags, LocalizableString0> WarningsMeaning = new()
    {
        {WarningFlags.DeadlockCandidate, LSs.WarningDescriptionDeadlockCandidate},
        {WarningFlags.OverproductionRequired, LSs.WarningDescriptionOverproductionRequired},
        {WarningFlags.EntityNotSpecified, LSs.WarningDescriptionEntityNotSpecified},
        {WarningFlags.FuelNotSpecified, LSs.WarningDescriptionFuelNotSpecified},
        {WarningFlags.FuelWithTemperatureNotLinked, LSs.WarningDescriptionFluidWithTemperature},
        {WarningFlags.FuelTemperatureExceedsMaximum, LSs.WarningDescriptionFluidTooHot},
        {WarningFlags.FuelDoesNotProvideEnergy, LSs.WarningDescriptionFuelDoesNotProvideEnergy},
        {WarningFlags.FuelUsageInputLimited, LSs.WarningDescriptionHasMaxFuelConsumption},
        {WarningFlags.TemperatureForIngredientNotMatch, LSs.WarningDescriptionIngredientTemperatureRange},
        {WarningFlags.ReactorsNeighborsFromPrefs, LSs.WarningDescriptionAssumesReactorFormation},
        {WarningFlags.AssumesNauvisSolarRatio, LSs.WarningDescriptionAssumesNauvisSolar},
        {WarningFlags.ExceedsBuiltCount, LSs.WarningDescriptionNeedsMoreBuildings},
        {WarningFlags.AsteroidCollectionNotModelled, LSs.WarningDescriptionAsteroidCollectors},
        {WarningFlags.AssumesFulgoraAndModel, LSs.WarningDescriptionAssumesFulgoranLightning},
        {WarningFlags.UselessQuality, LSs.WarningDescriptionUselessQuality},
        {WarningFlags.ExcessProductivity, LSs.WarningDescriptionExcessProductivityBonus},
    };

    private static readonly (Icon icon, SchemeColor color)[] tagIcons = [
        (Icon.Empty, SchemeColor.BackgroundTextFaint),
        (Icon.Check, SchemeColor.Green),
        (Icon.Warning, SchemeColor.WarningIcon),
        (Icon.Error, SchemeColor.Error),
        (Icon.Edit, SchemeColor.Primary),
        (Icon.Help, SchemeColor.BackgroundText),
        (Icon.Time, SchemeColor.BackgroundText),
        (Icon.DarkMode, SchemeColor.BackgroundText),
        (Icon.Settings, SchemeColor.BackgroundText),
    ];

    protected override void BuildContent(ImGui gui) {
        if (model == null) {
            return;
        }

        BuildSummary(gui, model);
        gui.AllocateSpacing();
        flatHierarchyBuilder.Build(gui);
        gui.SetMinWidth(flatHierarchyBuilder.width);
    }

    private static void AddDesiredProductAtLevel(ProductionTable table) => SelectMultiObjectPanel.SelectWithQuality(
        Database.goods.all.Except(table.linkMap.Where(p => p.Value.amount != 0).Select(p => p.Key.target)).Where(g => g.isLinkable),
        new(LSs.ProductionTableAddDesiredProduct, Multiple: true, SelectedQuality: Quality.Normal),
        product => {
            if (table.linkMap.TryGetValue(product, out var existing) && existing is ProductionLink link) {
                if (link.amount != 0) {
                    return;
                }

                link.RecordUndo().amount = 1f;
            }
            else {
                table.RecordUndo().links.Add(new ProductionLink(table, product.target.With(product.quality)) { amount = 1f });
                table.RebuildLinkMap();
            }
        });

    private void BuildSummary(ImGui gui, ProductionTable table) {
        bool isRoot = table == model;
        if (!isRoot && !table.containsDesiredProducts) {
            return;
        }

        int elementsPerRow = MathUtils.Floor((flatHierarchyBuilder.width - 2f) / 4f);
        gui.spacing = 1f;
        Padding pad = new Padding(1f, 0.2f);
        using (gui.EnterGroup(pad)) {
            gui.BuildText(LSs.ProductionTableDesiredProducts);
            using var grid = gui.EnterInlineGrid(3f, 1f, elementsPerRow);
            foreach (var link in table.links.ToList()) {
                if (link.amount != 0f) {
                    grid.Next();
                    DrawDesiredProduct(gui, link);
                }
            }

            grid.Next();
            if (gui.BuildButton(Icon.Plus, SchemeColor.Primary, SchemeColor.PrimaryAlt, size: 2.5f)) {
                AddDesiredProductAtLevel(table);
            }
        }

        if (gui.isBuilding) {
            gui.DrawRectangle(gui.lastRect, SchemeColor.Background, RectangleBorder.Thin);
        }

        if (table.flow.Length > 0 && table.flow[0].amount < 0) {
            using (gui.EnterGroup(pad)) {
                gui.BuildText(isRoot ? LSs.ProductionTableSummaryIngredients : LSs.ProductionTableImportIngredients);
                var grid = gui.EnterInlineGrid(3f, 1f, elementsPerRow);
                BuildTableIngredients(gui, table, table, ref grid);
                grid.Dispose();
            }

            if (gui.isBuilding) {
                gui.DrawRectangle(gui.lastRect, SchemeColor.Background, RectangleBorder.Thin);
            }
        }

        if (table.flow.Length > 0 && table.flow[^1].amount > 0) {
            ImGui.Context? context = null;
            ImGuiUtils.InlineGridBuilder grid = default;
            void initializeGrid(ImGui gui) {
                context = gui.EnterGroup(pad);
                gui.BuildText(isRoot ? LSs.ProductionTableExtraProducts : LSs.ProductionTableExportProducts);
                grid = gui.EnterInlineGrid(3f, 1f, elementsPerRow);
            }

            BuildTableProducts(gui, table, table, ref grid, true, initializeGrid);

            if (context != null) {
                grid.Dispose();
                context.Value.Dispose();

                if (gui.isBuilding) {
                    gui.DrawRectangle(gui.lastRect, SchemeColor.Background, RectangleBorder.Thin);
                }
            }
        }
    }
}
