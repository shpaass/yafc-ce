using System;
using System.Linq;
using Yafc.I18n;
using Yafc.Model;

namespace Yafc.Parser;

internal partial class FactorioDataDeserializer {
    private const bool expensiveRecipes = false;

    private T DeserializeWithDifficulty<T>(LuaTable table, string prototypeType, Action<T, LuaTable, bool, ErrorCollector> loader, ErrorCollector errorCollector) where T : FactorioObject, new() {
        var obj = DeserializeCommon<T>(table, prototypeType);
        object? current = null, fallback = null;
        if (factorioVersion < v2_0) {
            current = expensiveRecipes ? table["expensive"] : table["normal"];
            fallback = expensiveRecipes ? table["normal"] : table["expensive"];
        }
        if (current is LuaTable c) {
            loader(obj, c, false, errorCollector);
        }
        else if (fallback is LuaTable f) {
            loader(obj, f, current is bool b && !b, errorCollector);
        }
        else {
            loader(obj, table, false, errorCollector);
        }

        return obj;
    }

    private void DeserializeRecipe(LuaTable table, ErrorCollector errorCollector) {
        var recipe = DeserializeWithDifficulty<Recipe>(table, "recipe", LoadRecipeData, errorCollector);
        _ = table.Get("category", out string recipeCategory, "crafting");
        // "recycling-or-hand-crafting" is Scrap recycling. It's special so it isn't considered a potential source recipe when ctrl+clicking.
        // It'll still be used as a ctrl+click consumption recipe for scrap.
        if (recipeCategory is "recycling" or "recycling-or-hand-crafting") {
            recipe.specialType = FactorioObjectSpecialType.Recycling;
        }
        recipeCategories.Add(recipeCategory, recipe);
        AllowedEffects allowedEffects = AllowedEffects.None;
        if (table.Get("allow_consumption", true)) {
            allowedEffects |= AllowedEffects.Consumption;
        }
        if (table.Get("allow_speed", true)) {
            allowedEffects |= AllowedEffects.Speed;
        }
        if (table.Get("allow_productivity", false)) {
            allowedEffects |= AllowedEffects.Productivity;
        }
        if (table.Get("allow_pollution", true)) {
            allowedEffects |= AllowedEffects.Pollution;
        }
        if (table.Get("allow_quality", true)) {
            allowedEffects |= AllowedEffects.Quality;
        }

        recipe.allowedEffects = allowedEffects;
        if (table.Get("allowed_module_categories", out LuaTable? categories)) {
            recipe.allowedModuleCategories = [.. categories.ArrayElements<string>()];
        }
    }

    private void DeserializeTechnology(LuaTable table, ErrorCollector errorCollector) {
        var technology = DeserializeWithDifficulty<Technology>(table, "technology", LoadTechnologyData, errorCollector);
        technology.products = [new(science, 1)];
    }

    private void DeserializeQuality(LuaTable table, ErrorCollector errorCollector) {
        Quality quality = DeserializeCommon<Quality>(table, "quality");
        if (table.Get("next", out string? nextQuality)) {
            quality.nextQuality = GetObject<Quality>(nextQuality);
            quality.nextQuality.previousQuality = quality;
        }
        quality.BeaconConsumptionFactor = table.Get("beacon_power_usage_multiplier", 1f);
        quality.level = table.Get("level", 0);
        quality.UpgradeChance = table.Get("next_probability", 0f);
    }

    private void UpdateRecipeCatalysts() {
        if (factorioVersion < v2_0) {
            foreach (var recipe in allObjects.OfType<Recipe>()) {
                foreach (var product in recipe.products) {
                    if (product.productivityAmount == product.amount) {
                        float catalyst = recipe.GetConsumptionPerRecipe(product.goods);

                        if (catalyst > 0f) {
                            product.SetCatalyst(catalyst);
                        }
                    }
                }
            }
        }
    }

    private void UpdateRecipeIngredientFluids(ErrorCollector errorCollector) {
        foreach (var recipe in allObjects.OfType<Recipe>()) {
            if (recipe.ingredients == null || recipe.products == null) {
                errorCollector.Error($"The recipe '{recipe.name}' is not defined in data.raw[\"recipe\"].", ErrorSeverity.Important);
                recipe.ingredients = [];
                recipe.products = [];
                continue;
            }
            foreach (var ingredient in recipe.ingredients) {
                if (ingredient.goods is Fluid fluid && fluid.variants != null) {
                    int min = -1, max = fluid.variants.Count - 1;

                    for (int i = 0; i < fluid.variants.Count; i++) {
                        var variant = fluid.variants[i];

                        if (variant.temperature < ingredient.temperature.min) {
                            continue;
                        }

                        if (min == -1) {
                            min = i;
                        }

                        if (variant.temperature > ingredient.temperature.max) {
                            max = i - 1;
                            break;
                        }
                    }

                    if (min >= 0 && max >= 0) {
                        ingredient.goods = fluid.variants[min];

                        if (max > min) {
                            Fluid[] fluidVariants = new Fluid[max - min + 1];
                            ingredient.variants = fluidVariants;
                            fluid.variants.CopyTo(min, fluidVariants, 0, max - min + 1);
                        }
                    }
                }
            }
        }
    }

    private void LoadTechnologyData(Technology technology, LuaTable table, bool forceDisable, ErrorCollector errorCollector) {
        if (table.Get("unit", out LuaTable? unit)) {
            technology.ingredients = LoadResearchIngredientList(unit);
            recipeCategories.Add(SpecialNames.Labs, technology);
        }
        else if (table.Get("research_trigger", out LuaTable? researchTriggerTable)) {
            LoadResearchTrigger(researchTriggerTable, ref technology, errorCollector);
            technology.ingredients ??= [];
            recipeCategories.Add(SpecialNames.TechnologyTrigger, technology);
        }
        else {
            errorCollector.Error($"Could not get requirement(s) to unlock {technology.name}.", ErrorSeverity.AnalysisWarning);
        }

        technology.enabled = !forceDisable && table.Get("enabled", true);
        technology.time = unit.Get("time", 1f);
        technology.count = unit.Get("count", 1000f);

        if (table.Get("prerequisites", out LuaTable? prerequisitesList)) {
            technology.prerequisites = [.. prerequisitesList.ArrayElements<string>().Select(GetObject<Technology>)];
        }

        if (table.Get("effects", out LuaTable? modifiers)) {
            foreach (LuaTable modifier in modifiers.ArrayElements<LuaTable>()) {
                switch (modifier.Get("type", "")) {
                    case "unlock-recipe": {
                            if (!GetRef<Recipe>(modifier, "recipe", out var recipe)) {
                                continue;
                            }

                            technology.unlockRecipes.Add(recipe);

                            break;
                        }

                    case "change-recipe-productivity": {
                            if (!GetRef<Recipe>(modifier, "recipe", out var recipe)) {
                                continue;
                            }

                            _ = technology.changeRecipeProductivity.TryGetValue(recipe, out float change);
                            change += modifier.Get("change", 0f);

                            technology.changeRecipeProductivity[recipe] = change;
                            recipe.technologyProductivity[technology] = change;

                            break;
                        }

                    case "unlock-quality": {
                            if (GetRef(modifier, "quality", out Quality? quality)) {
                                quality.technologyUnlock.Add(technology);
                            }
                            break;
                        }
                    case "mining-with-fluid": {
                            technology.unlocksFluidMining = true;
                            break;
                        }
                    case "unlock-space-location": {
                            if (!GetRef(modifier, "space_location", out Location? location)) {
                                continue;
                            }
                            technology.unlockLocations.Add(location);
                            break;
                        }
                }
            }
        }
    }

    private Func<LuaTable, Product> LoadProduct(string typeDotName, int multiplier = 1, float? percentSpoiled = null) => table => {
        Goods? goods = LoadItemOrFluid(table, true);

        float min, max, catalyst;
        if (goods is Item) {
            if (table.Get("amount", out int effectiveAmount)) {
                min = max = effectiveAmount;
            }
            else if (table.Get("amount_min", out int effectiveMin) && table.Get("amount_max", out int effectiveMax)) {
                min = effectiveMin;
                max = effectiveMax;
            }
            else {
                throw new NotSupportedException($"Could not load amount for one of the products for {typeDotName}, possibly named '{table.Get("name", "")}'.");
            }

            percentSpoiled ??= table.Get<float?>("percent_spoiled");

            float extraCountFraction = table.Get("extra_count_fraction", 0f);
            min += extraCountFraction;
            max += extraCountFraction;

            // ignored_by_productivity (default is the value of ignored_by_stats) in 2.0; catalyst_amount in 1.1.
            // Assume mods don't declare these inconsistently.
            catalyst = table.Get("ignored_by_productivity", table.Get("ignored_by_stats", table.Get("catalyst_amount", 0)));
        }
        else if (goods is Fluid) {
            if (table.Get("amount", out float amount)) {
                min = max = amount;
            }
            else if (table.Get("amount_min", out min) && table.Get("amount_max", out max)) {
                // nothing to do
            }
            else {
                throw new NotSupportedException($"Could not load amount for one of the products for {typeDotName}, possibly named '{table.Get("name", "")}'.");
            }

            // ignored_by_productivity (default is the value of ignored_by_stats) in 2.0; catalyst_amount in 1.1.
            // Assume mods don't declare these inconsistently.
            catalyst = table.Get("ignored_by_productivity", table.Get("ignored_by_stats", table.Get("catalyst_amount", 0f)));
        }
        else {
            throw new NotSupportedException($"Could not load one of the products for {typeDotName}, possibly named '{table.Get("name", "")}'.");
        }

        Product product = new Product(goods, min * multiplier, max * multiplier, table.Get("probability", 1f)) { percentSpoiled = percentSpoiled };

        if (catalyst > 0f) {
            product.SetCatalyst(catalyst);
        }

        return product;
    };

    private Product[] LoadProductList(LuaTable table, string typeDotName, bool allowSimpleSyntax) {
        float? percentSpoiled = table.Get("result_is_always_fresh", false) ? 0 : null;

        if (table.Get("results", out LuaTable? resultList)) {
            return [.. resultList.ArrayElements<LuaTable>().Select(LoadProduct(typeDotName, percentSpoiled: percentSpoiled)).Where(x => x.amount != 0)];
        }

        if (allowSimpleSyntax && table.Get("result", out string? name)) {
            // Sometimes it's count, other times it's result_count. Assume mods don't declare them when unused.
            // (Only 1.1 uses result_count.)
            return [(new Product(GetObject<Item>(name), table.Get("count", table.Get("result_count", 1))) { percentSpoiled = percentSpoiled })];
        }

        return [];
    }

    private Ingredient[] LoadIngredientList(LuaTable table, string typeDotName, ErrorCollector errorCollector) {
        _ = table.Get("ingredients", out LuaTable? ingredientsList);

        return ingredientsList?.ArrayElements<LuaTable>().Select(table => {
            Goods? goods = LoadItemOrFluid(table, false);

            if (goods is null) {
                errorCollector.Error($"Failed to load at least one ingredient for {typeDotName}.", ErrorSeverity.AnalysisWarning);
                return null!;
            }

            float amount;
            if (goods is Item) {
                _ = table.Get("amount", out int effectiveAmount);
                amount = effectiveAmount;
            }
            else {
                _ = table.Get("amount", out amount);
            }

            Ingredient ingredient = new Ingredient(goods, amount);

            if (goods is Fluid f) {
                ingredient.temperature = table.Get("temperature", out int temp)
                    ? new TemperatureRange(temp)
                    : new TemperatureRange(table.Get("minimum_temperature", f.temperatureRange.min), table.Get("maximum_temperature", f.temperatureRange.max));
            }

            return ingredient;
        }).Where(x => x is not null).ToArray() ?? [];
    }

    private Ingredient[] LoadResearchIngredientList(LuaTable table) {
        _ = table.Get("ingredients", out LuaTable? ingredientsList);
        return ingredientsList?.ArrayElements<LuaTable>().Select(table => {
            if (table.Get(1, out string? name) && table.Get(2, out int amount)) {
                Item goods = GetObject<Item>(name);
                Ingredient ingredient = new Ingredient(goods, amount);
                return ingredient;
            }

            return null!;
        }).Where(x => x is not null).ToArray() ?? [];
    }

    private void LoadResearchTrigger(LuaTable researchTriggerTable, ref Technology technology, ErrorCollector errorCollector) {
        if (!researchTriggerTable.Get("type", out string? type)) {
            errorCollector.Error($"Research trigger of {technology.typeDotName} does not have a type field", ErrorSeverity.MinorDataLoss);
            return;
        }

        switch (type) {
            case "craft-item":
                if (!researchTriggerTable.Get("item", out string? craftItemName)) {
                    errorCollector.Error($"Research trigger {type} of {technology.typeDotName} does not have an item field", ErrorSeverity.MinorDataLoss);
                    break;
                }
                float craftCount = researchTriggerTable.Get("count", 1);
                technology.ingredients = [new Ingredient(GetObject<Item>(craftItemName), craftCount)];
                technology.flags = RecipeFlags.HasResearchTriggerCraft;

                break;
            case "capture-spawner":
                technology.flags = RecipeFlags.HasResearchTriggerCaptureEntity;
                if (researchTriggerTable.Get("entity", out string? entity)) {
                    technology.getTriggerEntities = new(() => [((Entity)Database.objectsByTypeName["Entity." + entity])]);
                }
                else {
                    technology.getTriggerEntities = new(static () =>
                        Database.entities.all.OfType<EntitySpawner>()
                        .Where(e => e.capturedEntityName != null)
                        .ToList());
                }
                break;
            case "mine-entity":
                technology.flags = RecipeFlags.HasResearchTriggerMineEntity;
                if (!researchTriggerTable.Get("entity", out entity)) {
                    errorCollector.Error($"Research trigger {type} of {technology.typeDotName} does not have an entity field", ErrorSeverity.MinorDataLoss);
                    break;
                }
                technology.getTriggerEntities = new(() => [((Entity)Database.objectsByTypeName["Entity." + entity])]);
                break;
            case "build-entity":
                technology.flags = RecipeFlags.HasResearchTriggerBuildEntity;
                if (researchTriggerTable.Get("entity", out entity)
                    || (researchTriggerTable.Get("entity", out LuaTable? entityFilter) && entityFilter.Get("name", out entity))) {

                    technology.getTriggerEntities = new(() => [((Entity)Database.objectsByTypeName["Entity." + entity])]);
                }
                else {
                    errorCollector.Error($"Research trigger {type} of {technology.typeDotName} does not have an entity field", ErrorSeverity.MinorDataLoss);
                }

                break;
            case "create-space-platform":
                technology.flags = RecipeFlags.HasResearchTriggerCreateSpacePlatform;
                break;
            case "send-item-to-orbit":
                if (!researchTriggerTable.Get("item", out string? itemName)) {
                    errorCollector.Error($"Research trigger {type} of {technology.typeDotName} does not have an item field", ErrorSeverity.MinorDataLoss);
                    break;
                }
                Item item = GetObject<Item>(itemName);
                technology.triggerItem = item;
                technology.flags |= RecipeFlags.HasResearchTriggerSendToOrbit;
                // Create a launch recipe for items without a `rocket_launch_products`, without altering existing launch recipes.
                EnsureLaunchRecipe(item, null);
                break;
            default:
                errorCollector.Error(LSs.ResearchHasAnUnsupportedTriggerType.L(technology.typeDotName, type), ErrorSeverity.MinorDataLoss);
                break;
        }
    }

    private void LoadRecipeData(Recipe recipe, LuaTable table, bool forceDisable, ErrorCollector errorCollector) {
        recipe.ingredients = LoadIngredientList(table, recipe.typeDotName, errorCollector);
        recipe.products = LoadProductList(table, recipe.typeDotName, allowSimpleSyntax: factorioVersion < v2_0);

        recipe.time = table.Get("energy_required", 0.5f);
        recipe.preserveProducts = table.Get("preserve_products_in_machine_output", false);

        if (table.Get("main_product", out string? mainProductName) && mainProductName != "") {
            recipe.mainProduct = recipe.products.FirstOrDefault(x => x.goods.name == mainProductName)?.goods;
        }
        else if (recipe.products.Length == 1) {
            recipe.mainProduct = recipe.products[0]?.goods;
        }

        recipe.hidden = table.Get("hidden", false);
        recipe.enabled = !forceDisable && table.Get("enabled", true);
        recipe.maximumProductivity = table.Get("maximum_productivity", 3f);
    }
}
