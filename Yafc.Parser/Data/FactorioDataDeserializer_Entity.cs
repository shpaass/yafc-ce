using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc.Parser;

internal partial class FactorioDataDeserializer {
    private const float EstimationDistanceFromCenter = 3000f;
    private bool GetFluidBoxFilter(LuaTable? table, string fluidBoxName, int temperature, [NotNullWhen(true)] out Fluid? fluid, out TemperatureRange range) {
        fluid = null;
        range = default;

        if (!table.Get(fluidBoxName, out LuaTable? fluidBoxData)) {
            return false;
        }

        if (!fluidBoxData.Get("filter", out string? fluidName)) {
            return false;
        }

        fluid = temperature == 0 ? GetObject<Fluid>(fluidName) : GetFluidFixedTemp(fluidName, temperature);
        _ = fluidBoxData.Get("minimum_temperature", out range.min, fluid.temperatureRange.min);
        _ = fluidBoxData.Get("maximum_temperature", out range.max, fluid.temperatureRange.max);

        return true;
    }

    private static int CountFluidBoxes(LuaTable list, bool input) {
        int count = 0;

        foreach (var fluidBox in list.ArrayElements<LuaTable>()) {
            if (fluidBox.Get("production_type", out string? prodType) && (prodType == "input-output" || (input && prodType == "input") || (!input && prodType == "output"))) {
                ++count;
            }
        }

        return count;
    }

    private void ReadFluidEnergySource(LuaTable? energySource, Entity entity) {
        var energy = entity.energy;
        _ = energySource.Get("burns_fluid", out bool burns, false);
        energy.type = burns ? EntityEnergyType.FluidFuel : EntityEnergyType.FluidHeat;
        energy.workingTemperature = TemperatureRange.Any;

        if (energySource.Get("fluid_usage_per_tick", out float fuelLimit)) {
            energy.baseFuelConsumptionLimit = fuelLimit * 60f;
        }

        if (GetFluidBoxFilter(energySource, "fluid_box", 0, out var fluid, out var filterTemperature)) {
            string fuelCategory = SpecialNames.SpecificFluid + fluid.name;
            fuelUsers.Add(entity, fuelCategory);
            if (!burns) {
                var temperature = fluid.temperatureRange;
                int maxT = energySource.Get("maximum_temperature", int.MaxValue);
                temperature.max = Math.Min(temperature.max, maxT);
                energy.workingTemperature = temperature;
                energy.acceptedTemperature = filterTemperature;
            }
        }
        else if (burns) {
            fuelUsers.Add(entity, SpecialNames.BurnableFluid);
        }
        else {
            fuelUsers.Add(entity, SpecialNames.HotFluid);
        }
    }

    private void ReadEnergySource(LuaTable? energySource, Entity entity, float defaultDrain = 0f) {
        _ = energySource.Get("type", out string type, "burner");

        if (type == "void") {
            entity.energy = voidEntityEnergy;
            return;
        }

        EntityEnergy energy = new EntityEnergy();
        entity.energy = energy;
        List<(string, float)> emissions = [];
        // emissions_per_minute is a table in 2.0, and a number in 1.1.
        if (energySource.Get("emissions_per_minute", out LuaTable? table) && factorioVersion >= v2_0) {
            foreach (var (key, value) in table?.ObjectElements ?? []) {
                if (key is string k && value is double v) {
                    emissions.Add((k, (float)v));
                }
            }
        }
        else if (energySource.Get("emissions_per_minute", out float emission) && factorioVersion < v2_0) {
            emissions.Add(("pollution", emission));
        }
        energy.emissions = emissions.AsReadOnly();
        energy.effectivity = energySource.Get("effectivity", 1f);

        switch (type) {
            case "electric":
                fuelUsers.Add(entity, SpecialNames.Electricity);
                energy.type = EntityEnergyType.Electric;
                string? drainS = energySource.Get<string>("drain");
                energy.drain = drainS == null ? defaultDrain : ParseEnergy(drainS);
                break;
            case "burner":
                energy.type = EntityEnergyType.SolidFuel;
                if (energySource.Get("fuel_categories", out LuaTable? categories)) {
                    foreach (string cat in categories.ArrayElements<string>()) {
                        fuelUsers.Add(entity, cat);
                    }
                }
                else {
                    // fuel_category is not used in 2.0. Assume it's not present for 2.0 mods. Use this to load either the 1.1 value (or default),
                    // or the { "chemical" } default value for 2.0's fuel_categories.
                    fuelUsers.Add(entity, energySource.Get("fuel_category", "chemical"));
                }

                break;
            case "heat":
                energy.type = EntityEnergyType.Heat;
                fuelUsers.Add(entity, SpecialNames.Heat);
                energy.workingTemperature = new TemperatureRange(energySource.Get("min_working_temperature", 15), energySource.Get("max_temperature", 15));
                break;
            case "fluid":
                ReadFluidEnergySource(energySource, entity);
                break;
        }
    }

    private static (int Width, int Height) GetDimensions(LuaTable box) {
        _ = box.Get(1, out LuaTable? topLeft);
        _ = box.Get(2, out LuaTable? bottomRight);
        _ = topLeft.Get(1, out float x0);
        _ = topLeft.Get(2, out float y0);
        _ = bottomRight.Get(1, out float x1);
        _ = bottomRight.Get(2, out float y1);

        return (MathUtils.Round(x1 - x0), MathUtils.Round(y1 - y0));
    }

    private static void ParseModules(LuaTable table, EntityWithModules entity, AllowedEffects def) {
        if (table.Get("allowed_effects", out object? obj)) {
            if (obj is string s) {
                entity.allowedEffects = Enum.Parse<AllowedEffects>(s, true);
            }
            else if (obj is LuaTable t) {
                entity.allowedEffects = AllowedEffects.None;
                foreach (string str in t.ArrayElements<string>()) {
                    entity.allowedEffects |= Enum.Parse<AllowedEffects>(str, true);
                }
            }
        }
        else {
            entity.allowedEffects = def;
        }

        if (table.Get("allowed_module_categories", out LuaTable? categories)) {
            entity.allowedModuleCategories = [.. categories.ArrayElements<string>()];
        }

        // table.module_specification.module_slots in 1.1; table.module_slots in 2.0. Assume module_specification is not present for 2.0 mods.
        entity.moduleSlots = table.Get<LuaTable>("module_specification")?.Get<int>("module_slots") ?? table.Get("module_slots", 0);
    }

    private Recipe CreateLaunchRecipe(EntityCrafter entity, Recipe recipe, int partsRequired, int outputCount) {
        string launchCategory = SpecialNames.RocketCraft + entity.name;
        var launchRecipe = CreateSpecialRecipe(recipe, launchCategory, LSs.SpecialRecipeLaunch);
        recipeCrafters.Add(entity, launchCategory);
        launchRecipe.ingredients = [.. recipe.products.Select(x => new Ingredient(x.goods, x.amount * partsRequired))];
        launchRecipe.products = [new Product(rocketLaunch, outputCount)];
        launchRecipe.time = 40.33f / outputCount;
        recipeCrafters.Add(entity, SpecialNames.RocketLaunch);

        return launchRecipe;
    }

    // TODO: Work with AAI-I to support offshore pumps that consume energy.
    private static readonly HashSet<string> noDefaultEnergyParsing = [
        // Has custom parsing:
        "generator",
        "burner-generator",
        // Doesn't consume energy:
        "offshore-pump",
        "solar-panel",
        "accumulator",
        "electric-energy-interface",
        "lightning-attractor",
    ];

    private void DeserializeEntity(LuaTable table, ErrorCollector errorCollector) {
        string factorioType = table.Get("type", "");
        string name = table.Get("name", "");
        string? usesPower;
        float defaultDrain = 0f;

        if (table.Get("placeable_by", out LuaTable? placeableBy) && placeableBy.Get("item", out string? itemName)) {
            var item = GetObject<Item>(itemName);
            if (!placeResults.TryGetValue(item, out var resultNames)) {
                resultNames = placeResults[item] = [];
            }
            resultNames.Add(name);
        }

        switch (factorioType) {
            // NOTE: Please add new cases in alphabetical order, even if the new case shares code with another case. That is,
            // case "rocket-silo":
            //    goto case "furnace";
            // instead of 
            // case "furnace":
            // case "rocket-silo": // Out of order
            case "accumulator":
                var accumulator = GetObject<Entity, EntityAccumulator>(table);

                if (table.Get("energy_source", out LuaTable? accumulatorEnergy)) {
                    if (accumulatorEnergy.Get("buffer_capacity", out string? capacity)) {
                        accumulator.baseAccumulatorCapacity = ParseEnergy(capacity);
                    }
                    if (accumulatorEnergy.Get("input_flow_limit", out string? inputPower)) {
                        accumulator.basePower = ParseEnergy(inputPower);
                    }
                }
                break;
            case "agricultural-tower":
                var agriculturalTower = GetObject<Entity, EntityCrafter>(table);
                _ = table.Get("energy_usage", out usesPower);
                agriculturalTower.basePower = ParseEnergy(usesPower);
                float radius = table.Get("radius", 1f);
                agriculturalTower.baseCraftingSpeed = (float)(Math.Pow(2 * radius + 1, 2) - 1);
                agriculturalTower.itemInputs = 1;
                recipeCrafters.Add(agriculturalTower, SpecialNames.PlantRecipe);
                break;
            case "assembling-machine":
                goto case "furnace";
            case "asteroid":
                Entity asteroid = GetObject<Entity>(table);
                if (table.Get("dying_trigger_effect", out LuaTable? death)) {
                    death.ReadObjectOrArray(trigger => {
                        switch (trigger.Get<string>("type")) {
                            case "create-entity" when trigger.Get("entity_name", out string? result):
                            case "create-asteroid-chunk" when trigger.Get("asteroid_name", out result):
                                asteroids.Add(result, asteroid);
                                break;
                        }
                    });
                }
                break;
            case "asteroid-collector":
                EntityCrafter collector = GetObject<Entity, EntityCrafter>(table);
                _ = table.Get("arm_energy_usage", out usesPower);
                collector.basePower = ParseEnergy(usesPower) * 60;
                _ = table.Get("passive_energy_usage", out usesPower);
                defaultDrain = ParseEnergy(usesPower) * 60;
                collector.baseCraftingSpeed = 1;
                recipeCrafters.Add(collector, SpecialNames.AsteroidCapture);
                break;
            case "beacon":
                var beacon = GetObject<Entity, EntityBeacon>(table);
                beacon.baseBeaconEfficiency = table.Get("distribution_effectivity", 0f);
                beacon.profile = table.Get("profile", out LuaTable? profile) ? [.. profile.ArrayElements<double>().Select(x => (float)x)] : [1f];
                _ = table.Get("energy_usage", out usesPower);
                ParseModules(table, beacon, AllowedEffects.None);
                beacon.basePower = ParseEnergy(usesPower);
                break;
            case "boiler":
                var boiler = GetObject<Entity, EntityCrafter>(table);
                _ = table.Get("energy_consumption", out usesPower);
                boiler.basePower = ParseEnergy(usesPower);
                boiler.fluidInputs = 1;
                bool hasOutput = table.Get("mode", out string? mode) && mode == "output-to-separate-pipe";
                _ = GetFluidBoxFilter(table, "fluid_box", 0, out Fluid? input, out var acceptTemperature);
                _ = table.Get("target_temperature", out int targetTemp);
                Fluid? output = hasOutput ? GetFluidBoxFilter(table, "output_fluid_box", targetTemp, out var fluid, out _) ? fluid : null : input;

                if (input == null || output == null) { // TODO - boiler works with any fluid - not supported
                    break;
                }

                // otherwise convert boiler production to a recipe
                string category = SpecialNames.BoilerRecipe + boiler.name;
                var recipe = CreateSpecialRecipe(output, category, LSs.SpecialRecipeBoiling);
                recipeCrafters.Add(boiler, category);
                recipe.flags |= RecipeFlags.UsesFluidTemperature;
                // TODO: input fluid amount now depends on its temperature, using min temperature should be OK for non-modded
                float inputEnergyPerOneFluid = (targetTemp - acceptTemperature.min) * input.heatCapacity;
                recipe.ingredients = [new Ingredient(input, boiler.basePower / inputEnergyPerOneFluid) { temperature = acceptTemperature }];
                float outputEnergyPerOneFluid = (targetTemp - output.temperatureRange.min) * output.heatCapacity;
                recipe.products = [new Product(output, boiler.basePower / outputEnergyPerOneFluid)];
                recipe.time = 1f;
                boiler.baseCraftingSpeed = 1f;
                break;
            case "burner-generator":
                var generator = GetObject<Entity, EntityCrafter>(table);

                // generator energy input config is strange
                if (table.Get("max_power_output", out string? maxPowerOutput)) {
                    generator.basePower = ParseEnergy(maxPowerOutput);
                }

                if ((factorioVersion < v0_18 || factorioType == "burner-generator") && table.Get("burner", out LuaTable? burnerSource)) {
                    ReadEnergySource(burnerSource, generator);
                }
                else {
                    generator.energy = new EntityEnergy { effectivity = table.Get("effectivity", 1f) };
                    ReadFluidEnergySource(table, generator);
                }

                recipeCrafters.Add(generator, SpecialNames.GeneratorRecipe);
                break;
            case "character":
                var character = GetObject<Entity, EntityCrafter>(table);
                character.itemInputs = 255;

                if (table.Get("mining_categories", out LuaTable? resourceCategories)) {
                    foreach (string playerMining in resourceCategories.ArrayElements<string>()) {
                        recipeCrafters.Add(character, SpecialNames.MiningRecipe + playerMining);
                    }
                }

                if (table.Get("crafting_categories", out LuaTable? craftingCategories)) {
                    foreach (string playerCrafting in craftingCategories.ArrayElements<string>()) {
                        recipeCrafters.Add(character, playerCrafting);
                    }
                }

                recipeCrafters.Add(character, SpecialNames.TechnologyTrigger);
                recipeCrafters.Add(character, SpecialNames.SpoilRecipe);

                character.energy = laborEntityEnergy;
                if (character.name == "character") {
                    this.character = character;
                    character.mapGenerated = true;
                    rootAccessible.Insert(0, character);
                }
                break;
            case "constant-combinator":
                if (name == "constant-combinator") {
                    Database.constantCombinatorCapacity = table.Get("item_slot_count", 18);
                }
                break;
            case "container":
                var container = GetObject<Entity, EntityContainer>(table);
                container.inventorySize = table.Get("inventory_size", 0);

                if (factorioType == "logistic-container") {
                    container.logisticMode = table.Get("logistic_mode", "");
                    container.logisticSlotsCount = table.Get("logistic_slots_count", 0);
                    if (container.logisticSlotsCount == 0) {
                        container.logisticSlotsCount = table.Get("max_logistic_slots", 1000);
                    }
                }
                break;
            case "electric-energy-interface":
                var eei = GetObject<Entity, EntityCrafter>(table);
                eei.energy = voidEntityEnergy;

                if (table.Get("energy_production", out string? interfaceProduction)) {
                    eei.baseCraftingSpeed = ParseEnergy(interfaceProduction);
                    if (eei.baseCraftingSpeed > 0) {
                        recipeCrafters.Add(eei, SpecialNames.GeneratorRecipe);
                    }
                }
                break;
            case "furnace":
                var crafter = GetObject<Entity, EntityCrafter>(table);
                _ = table.Get("energy_usage", out usesPower);
                ParseModules(table, crafter, AllowedEffects.None);
                crafter.basePower = ParseEnergy(usesPower);
                defaultDrain = crafter.basePower / 30f;
                crafter.baseCraftingSpeed = table.Get("crafting_speed", 1f);
                crafter.itemInputs = factorioType == "furnace" ? table.Get("source_inventory_size", 1) : table.Get("ingredient_count", 255);

                if (table.Get("fluid_boxes", out LuaTable? fluidBoxes)) {
                    crafter.fluidInputs = CountFluidBoxes(fluidBoxes, true);
                }

                if (table.Get("vector_to_place_result", out LuaTable? hasVectorToPlaceResult)) {
                    crafter.hasVectorToPlaceResult = hasVectorToPlaceResult != null;
                }

                Recipe? fixedRecipe = null;

                if (table.Get("fixed_recipe", out string? fixedRecipeName)) {
                    string fixedRecipeCategoryName = SpecialNames.FixedRecipe + fixedRecipeName;
                    fixedRecipe = GetObject<Recipe>(fixedRecipeName);
                    recipeCrafters.Add(crafter, fixedRecipeCategoryName);
                    recipeCategories.Add(fixedRecipeCategoryName, fixedRecipe);
                }
                else {
                    _ = table.Get("crafting_categories", out craftingCategories);
                    foreach (string categoryName in craftingCategories.ArrayElements<string>()) {
                        recipeCrafters.Add(crafter, categoryName);
                    }
                }

                if (factorioType == "rocket-silo") {
                    bool launchToSpacePlatforms = table.Get("launch_to_space_platforms", false);
                    int rocketInventorySize = table.Get("to_be_inserted_to_rocket_inventory_size", factorioVersion < v2_0 ? 1 : 0);

                    if (rocketInventorySize > 0) {
                        _ = table.Get("rocket_parts_required", out int partsRequired, 100);

                        if (fixedRecipe != null) {
                            var launchRecipe = CreateLaunchRecipe(crafter, fixedRecipe, partsRequired, rocketInventorySize);
                            formerAliases["Mechanics.launch" + crafter.name + "." + crafter.name] = launchRecipe;
                        }
                        else {
                            foreach (string categoryName in recipeCrafters.GetRaw(crafter).ToArray()) {
                                foreach (var possibleRecipe in recipeCategories.GetRaw(categoryName)) {
                                    if (possibleRecipe is Recipe rec) {
                                        _ = CreateLaunchRecipe(crafter, rec, partsRequired, rocketInventorySize);
                                    }
                                }
                            }
                        }

                        crafter.rocketInventorySize = rocketInventorySize;
                    }
                }
                break;
            case "generator":
                goto case "burner-generator";
            case "inserter":
                var inserter = GetObject<Entity, EntityInserter>(table);
                inserter.inserterSwingTime = 1f / (table.Get("rotation_speed", 1f) * 60);
                // Assume mods don't declare bulk(2.0)/stack(1.1) inconsistently.
                inserter.isBulkInserter = table.Get("bulk", false) || table.Get("stack", false);
                break;
            case "lab":
                var lab = GetObject<Entity, EntityCrafter>(table);
                _ = table.Get("energy_usage", out usesPower);
                ParseModules(table, lab, AllowedEffects.All ^ AllowedEffects.Quality);
                lab.basePower = ParseEnergy(usesPower);
                lab.baseCraftingSpeed = table.Get("researching_speed", 1f);
                recipeCrafters.Add(lab, SpecialNames.Labs);
                _ = table.Get("inputs", out LuaTable? inputs);
                lab.inputs = [.. inputs.ArrayElements<string>().Select(GetObject<Item>)];
                sciencePacks.UnionWith(lab.inputs.Select(x => (Item)x));
                lab.itemInputs = lab.inputs.Length;
                break;
            case "logistic-container":
                goto case "container";
            case "lightning-attractor":
                if (table.Get("range_elongation", out int range) && table.Get("efficiency", out float efficiency) && efficiency > 0) {
                    EntityAttractor attractor = GetObject<Entity, EntityAttractor>(table);
                    attractor.energy = voidEntityEnergy;
                    attractor.range = range;
                    attractor.efficiency = efficiency;
                    if (table.Get("energy_source", out LuaTable? energy) && energy.Get("drain", out string? drain)) {
                        attractor.drain = ParseEnergy(drain) * 60; // Drain is listed as MJ/tick, not MW
                    }
                    recipeCrafters.Add(attractor, SpecialNames.GeneratorRecipe);
                }
                break;
            case "mining-drill":
                var drill = GetObject<Entity, EntityCrafter>(table);
                _ = table.Get("energy_usage", out usesPower);
                drill.basePower = ParseEnergy(usesPower);
                ParseModules(table, drill, AllowedEffects.All);
                drill.baseCraftingSpeed = table.Get("mining_speed", 1f);
                _ = table.Get("resource_categories", out resourceCategories);

                if (table.Get("input_fluid_box", out LuaTable? _)) {
                    drill.fluidInputs = 1;
                }

                // All drills have/require the vector_to_place_result to drop their items
                drill.hasVectorToPlaceResult = true;

                foreach (string resource in resourceCategories.ArrayElements<string>()) {
                    recipeCrafters.Add(drill, SpecialNames.MiningRecipe + resource);
                }
                break;
            case "offshore-pump":
                var pump = GetObject<Entity, EntityCrafter>(table);
                _ = table.Get("energy_usage", out usesPower);
                pump.basePower = ParseEnergy(usesPower);
                pump.baseCraftingSpeed = table.Get("pumping_speed", 20f) / 20f;

                if (table.Get("fluid_box", out LuaTable? fluidBox) && fluidBox.Get("fluid", out string? fluidName)) {
                    var pumpingFluid = GetFluidFixedTemp(fluidName, 0);
                    string recipeCategory = SpecialNames.PumpingRecipe + pumpingFluid.name;
                    recipe = CreateSpecialRecipe(pumpingFluid, recipeCategory, LSs.SpecialRecipePumping);
                    recipeCrafters.Add(pump, recipeCategory);
                    pump.energy = voidEntityEnergy;

                    if (recipe.products == null) {
                        recipe.products = [new Product(pumpingFluid, 1200f)]; // set to Factorio default pump amounts - looks nice in tooltip
                        recipe.ingredients = [];
                        recipe.time = 1f;
                    }
                }
                else {
                    string recipeCategory = SpecialNames.PumpingRecipe + "tile";
                    recipeCrafters.Add(pump, recipeCategory);
                    pump.energy = voidEntityEnergy;
                }
                break;
            case "projectile":
                var projectile = GetObject<Entity, EntityProjectile>(table);
                if (table["action"] is LuaTable actions) {
                    actions.ReadObjectOrArray(parseAction);
                }

                void parseAction(LuaTable action) {
                    if (action.Get<string>("type") == "direct" && action["action_delivery"] is LuaTable delivery) {
                        delivery.ReadObjectOrArray(parseDelivery);
                    }
                }
                void parseDelivery(LuaTable delivery) {
                    if (delivery.Get<string>("type") == "instant" && delivery["target_effects"] is LuaTable effects) {
                        effects.ReadObjectOrArray(parseEffect);
                    }
                }
                void parseEffect(LuaTable effect) {
                    if (effect.Get<string>("type") == "create-entity" && effect.Get("entity_name", out string? createdEntity)) {
                        projectile.placeEntities.Add(createdEntity);
                    }
                }
                break;
            case "reactor":
                var reactor = GetObject<Entity, EntityReactor>(table);
                reactor.reactorNeighborBonus = table.Get("neighbour_bonus", 1f); // Keep UK spelling for Factorio/LUA data objects
                _ = table.Get("consumption", out usesPower);
                reactor.basePower = ParseEnergy(usesPower);
                reactor.baseCraftingSpeed = reactor.basePower;
                recipeCrafters.Add(reactor, SpecialNames.ReactorRecipe);
                break;
            case "rocket-silo":
                goto case "furnace";
            case "solar-panel":
                var solarPanel = GetObject<Entity, EntityCrafter>(table);
                solarPanel.energy = voidEntityEnergy;
                _ = table.Get("production", out string? powerProduction);
                recipeCrafters.Add(solarPanel, SpecialNames.GeneratorRecipe);
                solarPanel.baseCraftingSpeed = ParseEnergy(powerProduction) * 0.7f; // 0.7f is a solar panel ratio on nauvis
                break;
            case "transport-belt":
                GetObject<Entity, EntityBelt>(table).beltItemsPerSecond = table.Get("speed", 0f) * 480f;
                break;
            case "unit-spawner":
                var spawner = GetObject<Entity, EntitySpawner>(table);
                spawner.capturedEntityName = table.Get<string>("captured_spawner_entity");
                break;
        }

        var entity = DeserializeCommon<Entity>(table, "entity");

        if (table.Get("loot", out LuaTable? lootList)) {
            entity.loot = [.. lootList.ArrayElements<LuaTable>().Select(x => {
                Product product = new Product(GetObject<Item>(x.Get("item", "")), x.Get("count_min", 1f), x.Get("count_max", 1f), x.Get("probability", 1f));
                return product;
            })];
        }

        if (table.Get("minable", out LuaTable? minable)) {
            var products = LoadProductList(minable, "minable", allowSimpleSyntax: true);

            if (factorioType == "resource") {
                // mining resource is processed as a recipe
                _ = table.Get("category", out string category, "basic-solid");
                var recipe = CreateSpecialRecipe(entity, SpecialNames.MiningRecipe + category, LSs.SpecialRecipeMining);
                recipe.flags = RecipeFlags.UsesMiningProductivity;
                recipe.time = minable.Get("mining_time", 1f);
                recipe.products = products;
                recipe.allowedEffects = AllowedEffects.All;
                recipe.sourceEntity = entity;

                if (minable.Get("required_fluid", out string? requiredFluid)) {
                    _ = minable.Get("fluid_amount", out float amount);
                    recipe.ingredients = [new Ingredient(GetObject<Fluid>(requiredFluid), amount / 10f)]; // 10x difference is correct but why?
                    foreach (var tech in allObjects.OfType<Technology>().Where(t => t.unlocksFluidMining)) {
                        // Maybe incorrect: Leave the mining recipe enabled if no technologies unlock fluid mining
                        recipe.enabled = false;
                        tech.unlockRecipes.Add(recipe);
                    }
                }
                else {
                    recipe.ingredients = [];
                }
            }
            else if (factorioType == "plant") {
                // harvesting plants is processed as a recipe
                foreach (var seed in plantResults.Where(x => x.Value == name).Select(x => x.Key)) {
                    var recipe = CreateSpecialRecipe(seed, SpecialNames.PlantRecipe, LSs.SpecialRecipePlanting);
                    recipe.time = table.Get("growth_ticks", 0) / 60f;
                    recipe.ingredients = [new Ingredient(seed, 1)];
                    recipe.products = products;
                }

                // can also be mined normally
                entity.loot = products;
            }
            else {
                // otherwise it is processed as loot
                entity.loot = products;
            }
        }

        (entity.width, entity.height) = table.Get("selection_box", out LuaTable? box) ? GetDimensions(box) : (3, 3);

        entity.size = Math.Max(entity.width, entity.height);

        _ = table.Get("energy_source", out LuaTable? energySource);

        if (energySource != null && !noDefaultEnergyParsing.Contains(factorioType)) {
            ReadEnergySource(energySource, entity, defaultDrain);
        }

        if (entity is EntityCrafter entityCrafter) {
            _ = table.Get("effect_receiver", out LuaTable? effectReceiver);
            entityCrafter.effectReceiver = ParseEffectReceiver(effectReceiver);
        }

        if (table.Get("autoplace", out LuaTable? generation)) {
            if (generation.Get("probability_expression", out LuaTable? prob)) {
                float probability = EstimateNoiseExpression(prob);
                float richness = generation.Get("richness_expression", out LuaTable? rich) ? EstimateNoiseExpression(rich) : probability;
                entity.mapGenDensity = richness * probability;
            }
            else if (generation.Get("coverage", out float coverage)) {
                float richBase = generation.Get("richness_base", 0f);
                float richMultiplier = generation.Get("richness_multiplier", 0f);
                float richMultiplierDistance = generation.Get("richness_multiplier_distance_bonus", 0f);
                float estimatedAmount = coverage * (richBase + richMultiplier + (richMultiplierDistance * EstimationDistanceFromCenter));
                entity.mapGenDensity = estimatedAmount;
            }
            if (generation.Get("control", out string? control)) {
                entity.mapGenerated = true;
                entity.autoplaceControl = generation.Get<string>("control");
            }
        }

        entity.loot ??= [];

        if (entity.energy == voidEntityEnergy || entity.energy == laborEntityEnergy) {
            fuelUsers.Add(entity, SpecialNames.Void);
        }

        entity.heatingPower = ParseEnergy(table.Get<string>("heating_energy"));

        if (table.Get("production_health_effect", out LuaTable? healthEffect) && healthEffect.Get("not_producing", out float? lossPerTick)) {
            entity.baseSpoilTime = (float)(table.Get<float>("max_health") * -60 * lossPerTick.Value);
            table.Get<LuaTable>("dying_trigger_effect")?.ReadObjectOrArray(readDeathEffect);

            void readDeathEffect(LuaTable effect) {
                if (effect.Get("type", "") == "create-entity" && effect.Get("entity_name", out string? spoilEntity)) {
                    entity.getSpoilResult = new(() => {
                        Database.objectsByTypeName.TryGetValue("Entity." + spoilEntity, out FactorioObject? spoil);
                        return spoil as Entity;
                    });
                }
            }
        }
    }

    private void DeserializeAsteroidChunk(LuaTable table, ErrorCollector errorCollector) {
        Entity chunk = DeserializeCommon<Entity>(table, "asteroid-chunk");
        Item asteroid = GetObject<Item>(chunk.name);
        if (asteroid.showInExplorers) { // don't create mining recipes for parameter chunks.
            Recipe recipe = CreateSpecialRecipe(asteroid, SpecialNames.AsteroidCapture, LSs.SpecialRecipeMining);
            recipe.time = 1;
            recipe.ingredients = [];
            recipe.products = [new Product(asteroid, 1)];
            recipe.sourceEntity = chunk;
        }
    }

    private float EstimateArgument(LuaTable args, string name, float def = 0) => args.Get(name, out LuaTable? res) ? EstimateNoiseExpression(res) : def;

    private float EstimateArgument(LuaTable args, int index, float def = 0) => args.Get(index, out LuaTable? res) ? EstimateNoiseExpression(res) : def;

    private float EstimateNoiseExpression(LuaTable expression) {
        string type = expression.Get("type", "typed");

        switch (type) {
            case "variable":
                string varName = expression.Get("variable_name", "");

                if (varName is "x" or "y" or "distance") {
                    return EstimationDistanceFromCenter;
                }

                if (((LuaTable?)raw["noise-expression"]).Get(varName, out LuaTable? noiseExpr)) {
                    return EstimateArgument(noiseExpr, "expression");
                }

                return 1f;
            case "function-application":
                string funName = expression.Get("function_name", "");
                var args = expression.Get<LuaTable>("arguments");

                if (args is null) {
                    return 0;
                }

                switch (funName) {
                    case "add":
                        float res = 0f;

                        foreach (var el in args.ArrayElements<LuaTable>()) {
                            res += EstimateNoiseExpression(el);
                        }

                        return res;

                    case "multiply":
                        res = 1f;

                        foreach (var el in args.ArrayElements<LuaTable>()) {
                            res *= EstimateNoiseExpression(el);
                        }

                        return res;

                    case "subtract":
                        return EstimateArgument(args, 1) - EstimateArgument(args, 2);
                    case "divide":
                        return EstimateArgument(args, 1) / EstimateArgument(args, 2);
                    case "exponentiate":
                        return MathF.Pow(EstimateArgument(args, 1), EstimateArgument(args, 2));
                    case "absolute-value":
                        return MathF.Abs(EstimateArgument(args, 1));
                    case "clamp":
                        return MathUtils.Clamp(EstimateArgument(args, 1), EstimateArgument(args, 2), EstimateArgument(args, 3));
                    case "log2":
                        return MathF.Log(EstimateArgument(args, 1), 2f);
                    case "distance-from-nearest-point":
                        return EstimateArgument(args, "maximum_distance");
                    case "ridge":
                        return (EstimateArgument(args, 2) + EstimateArgument(args, 3)) * 0.5f; // TODO
                    case "terrace":
                        return EstimateArgument(args, "value"); // TODO what terrace does
                    case "random-penalty":
                        float source = EstimateArgument(args, "source");
                        float penalty = EstimateArgument(args, "amplitude");

                        if (penalty > source) {
                            return source / penalty;
                        }

                        return (source + source - penalty) / 2;

                    case "spot-noise":
                        float quantity = EstimateArgument(args, "spot_quantity_expression");
                        float spotCount;

                        if (args.Get("candidate_spot_count", out LuaTable? spots)) {
                            spotCount = EstimateNoiseExpression(spots);
                        }
                        else {
                            spotCount = EstimateArgument(args, "candidate_point_count", 256) / EstimateArgument(args, "skip_span", 1);
                        }

                        float regionSize = EstimateArgument(args, "region_size", 512);
                        regionSize *= regionSize;
                        float count = spotCount * quantity / regionSize;

                        return count;

                    case "factorio-basis-noise":
                    case "factorio-quick-multioctave-noise":
                    case "factorio-multioctave-noise":
                        float outputScale = EstimateArgument(args, "output_scale", 1f);
                        return 0.1f * outputScale;
                    default:
                        return 0f;
                }
            case "procedure-delimiter":
                return EstimateArgument(expression, "expression");
            case "literal-number":
                return expression.Get("literal_value", 0f);
            case "literal-expression":
                return EstimateArgument(expression, "literal_value");
            default:
                return 0f;
        }
    }
}
