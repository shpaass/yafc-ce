﻿using System;
using System.Collections.Generic;
using System.Linq;
using Yafc.I18n;
using Yafc.Model;

namespace Yafc.Parser;

internal partial class FactorioDataDeserializer {
    private readonly List<FactorioObject> allObjects = [];
    private readonly List<FactorioObject> rootAccessible = [];
    private readonly Dictionary<(Type? type, string? name), FactorioObject> registeredObjects = [];
    private readonly DataBucket<string, Goods> fuels = new DataBucket<string, Goods>();
    private readonly DataBucket<Entity, string> fuelUsers = new DataBucket<Entity, string>();
    private readonly DataBucket<string, RecipeOrTechnology> recipeCategories = new DataBucket<string, RecipeOrTechnology>();
    private readonly DataBucket<EntityCrafter, string> recipeCrafters = new DataBucket<EntityCrafter, string>();
    private readonly DataBucket<Recipe, Module> recipeModules = new DataBucket<Recipe, Module>();
    private readonly Dictionary<Item, List<string>> placeResults = [];
    private readonly Dictionary<Item, string> plantResults = [];
    private readonly DataBucket<string, Entity> asteroids = new();
    private readonly List<Module> allModules = [];
    private readonly HashSet<Item> sciencePacks = [];
    private readonly Dictionary<string, List<Fluid>> fluidVariants = [];
    private readonly Dictionary<string, FactorioObject> formerAliases = [];

    private readonly Recipe generatorProduction;
    private readonly Recipe reactorProduction;
    private readonly Special voidEnergy;
    private readonly Special heat;
    private readonly Special electricity;
    private readonly Special rocketLaunch;
    private readonly Item science;
    // Note: These must be Items (or possibly a derived type) so belt capacity can be displayed and set.
    private readonly Item totalItemOutput, totalItemInput;
    private readonly EntityEnergy voidEntityEnergy;
    private readonly EntityEnergy laborEntityEnergy;
    private Entity? character;
    private readonly Version factorioVersion;
    private int rocketCapacity;
    private int defaultItemWeight;

    private static readonly Version v0_18 = new Version(0, 18);

    public FactorioDataDeserializer(Version factorioVersion) {
        this.factorioVersion = factorioVersion;

        Special createSpecialObject(bool isPower, string name, string locName, string locDescr, string icon, string signal) {
            var obj = GetObject<Special>(name);
            obj.virtualSignal = signal;
            obj.factorioType = "special";
            obj.locName = locName;
            obj.locDescr = locDescr;
            obj.iconSpec = [new FactorioIconPart(icon)];
            obj.power = isPower;
            if (isPower) {
                obj.fuelValue = 1f;
                fuels.Add(name, obj);
            }

            return obj;
        }

        Item createSpecialItem(string name, string locName, string locDescr, string icon) {
            Item obj = GetObject<Item>(name);
            obj.factorioType = "special";
            obj.locName = locName;
            obj.locDescr = locDescr;
            obj.iconSpec = [new FactorioIconPart(icon)];
            obj.isLinkable = false;
            obj.showInExplorers = false;
            rootAccessible.Add(obj);

            return obj;
        }

        electricity = createSpecialObject(true, SpecialNames.Electricity, LSs.SpecialObjectElectricity, LSs.SpecialObjectElectricityDescription,
            "__core__/graphics/icons/alerts/electricity-icon-unplugged.png", "signal-E");

        heat = createSpecialObject(true, SpecialNames.Heat, LSs.SpecialObjectHeat, LSs.SpecialObjectHeatDescription, "__core__/graphics/arrows/heat-exchange-indication.png", "signal-H");

        voidEnergy = createSpecialObject(true, SpecialNames.Void, LSs.SpecialObjectVoid, LSs.SpecialObjectVoidDescription, "__core__/graphics/icons/mip/infinity.png", "signal-V");
        voidEnergy.isVoid = true;
        voidEnergy.isLinkable = false;
        voidEnergy.showInExplorers = false;
        rootAccessible.Add(voidEnergy);

        rocketLaunch = createSpecialObject(false, SpecialNames.RocketLaunch, LSs.SpecialObjectLaunchSlot,
            LSs.SpecialObjectLaunchSlotDescription, "__base__/graphics/entity/rocket-silo/rocket-static-pod.png", "signal-R");

        science = GetObject<Item>("science");
        science.showInExplorers = false;
        Analysis.ExcludeFromAnalysis<CostAnalysis>(science);
        formerAliases["Special.research-unit"] = science;

        generatorProduction = CreateSpecialRecipe(electricity, SpecialNames.GeneratorRecipe, LSs.SpecialRecipeGenerating);
        generatorProduction.products = [new Product(electricity, 1f)];
        generatorProduction.flags |= RecipeFlags.ScaleProductionWithPower;
        generatorProduction.ingredients = [];

        reactorProduction = CreateSpecialRecipe(heat, SpecialNames.ReactorRecipe, LSs.SpecialRecipeGenerating);
        reactorProduction.products = [new Product(heat, 1f)];
        reactorProduction.flags |= RecipeFlags.ScaleProductionWithPower;
        reactorProduction.ingredients = [];

        voidEntityEnergy = new EntityEnergy { type = EntityEnergyType.Void, effectivity = float.PositiveInfinity };
        laborEntityEnergy = new EntityEnergy { type = EntityEnergyType.Labor, effectivity = float.PositiveInfinity };

        // Note: These must be Items (or possibly a derived type) so belt capacity can be displayed and set.
        totalItemInput = createSpecialItem("item-total-input", LSs.SpecialItemTotalConsumption, LSs.SpecialItemTotalConsumptionDescription, "__base__/graphics/icons/signal/signal_I.png");
        totalItemOutput = createSpecialItem("item-total-output", LSs.SpecialItemTotalProduction, LSs.SpecialItemTotalProductionDescription, "__base__/graphics/icons/signal/signal_O.png");
        formerAliases["Special.total-item-input"] = totalItemInput;
        formerAliases["Special.total-item-output"] = totalItemOutput;
    }

    /// <summary>
    /// Gets or creates an object with the specified type (or a derived type), based on the supplied <see cref="LuaTable"/>. If
    /// <paramref name="table"/> describes a blueprint parameter, the returned object will not be shown in the NEIE, Dependency Explorer, or
    /// Desired Product windows.
    /// </summary>
    /// <typeparam name="TReturn">The (compile-time) type of the return value.</typeparam>
    /// <param name="table">The <see cref="LuaTable"/> to read to get the object's name. This table must have a <c>name</c> key. In addition, if the
    /// value of its <c>parameter</c> key is <see langword="true"/>, <see cref="FactorioObject.showInExplorers"/>, and <see cref="Goods.isLinkable"/>
    /// if applicable, will be set to <see langword="false"/>.</param>
    /// <returns>The new or pre-existing object described by <typeparamref name="TReturn"/> and <paramref name="table"/>.</returns>
    /// <remarks>The concrete type is determined based on the value of <c>data.raw[some_table][table.name].type</c>. <typeparamref name="TReturn"/>
    /// serves to cast the result before returning and to select the correct <c>some_table</c>. <typeparamref name="TReturn"/> cannot force the use of
    /// an inadequate or inappropriate type. For example,
    /// <c><see cref="GetObject{TReturn}(LuaTable)">GetObject</see>&lt;<see cref="Entity"/>>(data.raw.furnace["electric-furnace"]).<see cref="object.GetType">GetType</see>() == <see langword="typeof"/>(<see cref="EntityCrafter"/>)</c>,
    /// and <c><see cref="GetObject{TReturn}(LuaTable)">GetObject</see>&lt;<see cref="EntityAccumulator"/>>(data.raw.furnace["electric-furnace"])</c>
    /// will throw.</remarks>
    /// <exception cref="ArgumentException">Thrown if <paramref name="table"/> does not describe an object in <c>data.raw</c> that can be loaded as
    /// a <typeparamref name="TReturn"/>, or if <paramref name="table"/> does not have a name key.</exception>
    private TReturn GetObject<TReturn>(LuaTable table) where TReturn : FactorioObject, new() {
        if (!table.Get("name", out string? name)) {
            throw new ArgumentException($"{nameof(table)} must contain a 'name' key. Call GetObject(string) instead.", nameof(table));
        }
        TReturn result = GetObject<TReturn>(name);
        if (table.Get("parameter", false)) {
            result.showInExplorers = false;
            if (result is Goods goods) {
                goods.isLinkable = false;
            }
        }
        return result;
    }

    /// <summary>
    /// Gets or creates an object with the specified type (or a derived type) and name. 
    /// </summary>
    /// <typeparam name="TReturn">The (compile-time) type of the return value.</typeparam>
    /// <param name="name">The name of the object to get or create.</param>
    /// <returns>The new or pre-existing object described by <typeparamref name="TReturn"/> and <paramref name="name"/>.</returns>
    /// <remarks>The concrete type is determined based on the value of <c>data.raw[some_table][name].type</c>. <typeparamref name="TReturn"/>
    /// serves to cast the result before returning and to select the correct <c>some_table</c>. <typeparamref name="TReturn"/> cannot force the use of
    /// an inadequate or inappropriate type. For example,
    /// <c><see cref="GetObject{TReturn}(LuaTable)">GetObject</see>&lt;<see cref="Entity"/>>("electric-furnace").<see cref="object.GetType">GetType</see>() == <see langword="typeof"/>(<see cref="EntityCrafter"/>)</c>,
    /// and <c><see cref="GetObject{TReturn}(LuaTable)">GetObject</see>&lt;<see cref="EntityAccumulator"/>>("electric-furnace")</c> will throw.</remarks>
    /// <exception cref="ArgumentException">Thrown if <paramref name="name"/> does not describe an object in <c>data.raw</c> that can be loaded as
    /// a <typeparamref name="TReturn"/>.</exception>
    private TReturn GetObject<TReturn>(string name) where TReturn : FactorioObject, new() {
        // Look for an existing object. All items use Item in the key, and all entities use Entity, so lookup works without the actual C# type.
        var key = (Type: typeof(TReturn), name);
        if (typeof(TReturn).IsAssignableTo(typeof(Item))) {
            key = (typeof(Item), name);
        }
        else if (typeof(TReturn).IsAssignableTo(typeof(Entity))) {
            key = (typeof(Entity), name);
        }

        if (registeredObjects.TryGetValue(key, out FactorioObject? existing)) {
            if (existing is TReturn result) {
                return result;
            }
            // If `existing` was incorrectly constructed (e.g. as an Entity instead of a RailEntity), update the switch expressions below, adding
            // "type" => typeof(DerivedFactorioObject),
            throw new ArgumentException($"The best match in data.raw for '{name}' and '{typeof(TReturn).Name}' cannot be loaded as a(n) '{typeof(TReturn).Name}'.", nameof(TReturn));
        }

        // For the remainder of this method, "prototype" refers to the keys in defines.prototypes.
        // "C# type" and "tActual" refer to the classes derived from FactorioObject.
        // "type" refers to the keys in data.raw and/or the values of PrototypeBase::type.

        // No existing object. Determine the C# type for items and entities. (Everything else is as-requested.)
        Type tActual;
        string? type = null;
        if (key.Type == typeof(Item)) {
            if (!prototypes.TryGetValue(("item", name), out type) && !prototypes.TryGetValue(("asteroid-chunk", name), out type)) {
                // To add another prototype that can be loaded as an item, add another check above, e.g. TryGetValue(("achievement", name), out type)
                // for achievements. If necessary, also add an entry to the switch below, so each individual type (e.g. "produce-achievement") is
                // constructed as the correct C# type.
                throw new ArgumentException($"data.raw does not contain an object named '{name}' that can be loaded as a(n) {typeof(TReturn).Name}", nameof(name));
            }

            tActual = type switch {
                // The C# types to be used for the items in data.raw[type]:
                "ammo" => typeof(Ammo),
                "module" => typeof(Module),
                _ => typeof(Item)
            };
        }
        else if (key.Type == typeof(Entity)) {
            if (!prototypes.TryGetValue(("entity", name), out type) && !prototypes.TryGetValue(("asteroid-chunk", name), out type)) {
                // To add another prototype that can be loaded as an entity, add another check above, e.g. TryGetValue(("equipment", name), out type)
                // for armor equipment. If necessary, also add an entry to the switch below, so each individual type (e.g. "generator-equipment") is
                // constructed as the correct C# type.
                throw new ArgumentException($"data.raw does not contain an object named '{name}' that can be loaded as a(n) {typeof(TReturn).Name}", nameof(name));
            }

            tActual = type switch {
                // The C# types to be used for the entities in data.raw[type]:
                "accumulator" => typeof(EntityAccumulator),
                "agricultural-tower" => typeof(EntityCrafter),
                "assembling-machine" => typeof(EntityCrafter),
                "asteroid-collector" => typeof(EntityCrafter),
                "beacon" => typeof(EntityBeacon),
                "boiler" => typeof(EntityCrafter),
                "burner-generator" => typeof(EntityCrafter),
                "character" => typeof(EntityCrafter),
                "container" => typeof(EntityContainer),
                "electric-energy-interface" => typeof(EntityCrafter),
                "furnace" => typeof(EntityCrafter),
                "generator" => typeof(EntityCrafter),
                "inserter" => typeof(EntityInserter),
                "lab" => typeof(EntityCrafter),
                "lightning-attractor" => typeof(EntityAttractor),
                "logistic-container" => typeof(EntityContainer),
                "mining-drill" => typeof(EntityCrafter),
                "offshore-pump" => typeof(EntityCrafter),
                "projectile" => typeof(EntityProjectile),
                "reactor" => typeof(EntityReactor),
                "rocket-silo" => typeof(EntityCrafter),
                "solar-panel" => typeof(EntityCrafter),
                "transport-belt" => typeof(EntityBelt),
                "unit-spawner" => typeof(EntitySpawner),
                _ => typeof(Entity)
            };
        }
        else {
            tActual = typeof(TReturn);
        }

        if (!tActual.IsAssignableTo(typeof(TReturn))) {
            // If data.raw[type][name] should be able to loaded as a TReturn, update the switch expressions above, adding
            // "type" => typeof(DerivedFactorioObject),
            throw new ArgumentException($"data.raw['{type}']['{name}'] does not describe an object that can be loaded as a(n) {typeof(TReturn).Name}", nameof(TReturn));
        }


        // Construct, store, and return the new object.
        TReturn newObject = (TReturn)Activator.CreateInstance(tActual)!; // null-forgiving: CreateInstance only returns null for Nullable<T>.
        newObject.name = name;
        allObjects.Add(newObject);
        registeredObjects[key] = newObject;
        return newObject;
    }

    private int Skip(int from, FactorioObjectSortOrder sortOrder) {
        for (; from < allObjects.Count; from++) {
            if (allObjects[from].sortingOrder != sortOrder) {
                break;
            }
        }

        return from;
    }

    private void ExportBuiltData() {
        Database.rootAccessible = [.. rootAccessible];
        Database.objectsByTypeName = allObjects.ToDictionary(x => x.typeDotName);
        foreach (var alias in formerAliases) {
            _ = Database.objectsByTypeName.TryAdd(alias.Key, alias.Value);
        }

        Database.allSciencePacks = [.. sciencePacks];
        Database.voidEnergy = voidEnergy.With(Quality.Normal);
        Database.science = science.With(Quality.Normal);
        Database.itemInput = totalItemInput.With(Quality.Normal);
        Database.itemOutput = totalItemOutput.With(Quality.Normal);
        Database.electricity = electricity.With(Quality.Normal);
        Database.electricityGeneration = generatorProduction.With(Quality.Normal);
        Database.heat = heat.With(Quality.Normal);
        Database.character = character;
        int firstSpecial = 0;
        int firstItem = Skip(firstSpecial, FactorioObjectSortOrder.SpecialGoods);
        int firstFluid = Skip(firstItem, FactorioObjectSortOrder.Items);
        int firstRecipe = Skip(firstFluid, FactorioObjectSortOrder.Fluids);
        int firstMechanics = Skip(firstRecipe, FactorioObjectSortOrder.Recipes);
        int firstTechnology = Skip(firstMechanics, FactorioObjectSortOrder.Mechanics);
        int firstEntity = Skip(firstTechnology, FactorioObjectSortOrder.Technologies);
        int firstTile = Skip(firstEntity, FactorioObjectSortOrder.Entities);
        int firstQuality = Skip(firstTile, FactorioObjectSortOrder.Tiles);
        int firstLocation = Skip(firstQuality, FactorioObjectSortOrder.Qualities);
        int last = Skip(firstLocation, FactorioObjectSortOrder.Locations);
        if (last != allObjects.Count) {
            throw new Exception("Something is not right");
        }

        Database.objects = new FactorioIdRange<FactorioObject>(0, last, allObjects);
        Database.specials = new FactorioIdRange<Special>(firstSpecial, firstItem, allObjects);
        Database.items = new FactorioIdRange<Item>(firstItem, firstFluid, allObjects);
        Database.fluids = new FactorioIdRange<Fluid>(firstFluid, firstRecipe, allObjects);
        Database.goods = new FactorioIdRange<Goods>(firstSpecial, firstRecipe, allObjects);
        Database.recipes = new FactorioIdRange<Recipe>(firstRecipe, firstTechnology, allObjects);
        Database.mechanics = new FactorioIdRange<Mechanics>(firstMechanics, firstTechnology, allObjects);
        Database.recipesAndTechnologies = new FactorioIdRange<RecipeOrTechnology>(firstRecipe, firstEntity, allObjects);
        Database.technologies = new FactorioIdRange<Technology>(firstTechnology, firstEntity, allObjects);
        Database.entities = new FactorioIdRange<Entity>(firstEntity, firstTile, allObjects);
        Database.qualities = new FactorioIdRange<Quality>(firstQuality, firstLocation, allObjects);
        Database.locations = new FactorioIdRange<Location>(firstLocation, last, allObjects);
        Database.fluidVariants = fluidVariants;

        Database.allModules = [.. allModules];
        Database.allBeacons = [.. Database.entities.all.OfType<EntityBeacon>()];
        Database.allCrafters = [.. Database.entities.all.OfType<EntityCrafter>()];
        Database.allBelts = [.. Database.entities.all.OfType<EntityBelt>()];
        Database.allInserters = [.. Database.entities.all.OfType<EntityInserter>()];
        Database.allAccumulators = [.. Database.entities.all.OfType<EntityAccumulator>()];
        Database.allContainers = [.. Database.entities.all.OfType<EntityContainer>()];

        Database.rocketCapacity = rocketCapacity;
    }

    private static bool AreInverseRecipes(Recipe packing, Recipe unpacking) {
        var packedProduct = packing.products[0];

        // Check for deterministic production
        if (packedProduct.probability != 1f || unpacking.products.Any(p => p.probability != 1)) {
            return false;
        }
        if (packedProduct.amountMin != packedProduct.amountMax || unpacking.products.Any(p => p.amountMin != p.amountMax)) {
            return false;
        }
        if (unpacking.ingredients.Length != 1 || packing.ingredients.Length != unpacking.products.Length) {
            return false;
        }

        // Check for 'packing.ingredients == unpacking.products'.
        float ratio = 0;
        Recipe? largerRecipe = null;

        // Check for 'packing.ingredients == unpacking.products'.
        if (!checkRatios(packing, unpacking, ref ratio, ref largerRecipe)) {
            return false;
        }

        // Check for 'unpacking.ingredients == packing.products'.
        if (!checkRatios(unpacking, packing, ref ratio, ref largerRecipe)) {
            return false;
        }

        return true;

        // Test to see if running `first` M times and `second` once, or vice versa, can reproduce all the original input.
        // Track which recipe is larger to keep ratio an integer and prevent floating point rounding issues.
        static bool checkRatios(Recipe first, Recipe second, ref float ratio, ref Recipe? larger) {
            Dictionary<Goods, float> ingredients = [];

            foreach (var item in first.ingredients) {
                if (ingredients.ContainsKey(item.goods)) {
                    return false; // Refuse to deal with duplicate ingredients.
                }

                ingredients[item.goods] = item.amount;
            }

            foreach (var item in second.products) {
                if (!ingredients.TryGetValue(item.goods, out float count)) {
                    return false;
                }

                if (count > item.amount) {
                    if (!checkProportions(first, count, item.amount, ref ratio, ref larger)) {
                        return false;
                    }
                }
                else if (count == item.amount) {
                    if (ratio != 0 && ratio != 1) {
                        return false;
                    }
                    ratio = 1;
                }
                else {
                    if (!checkProportions(second, item.amount, count, ref ratio, ref larger)) {
                        return false;
                    }
                }
            }

            return true;
        }

        // Within the previous check, make sure the ratio is an integer.
        // If the ratio was set by a previous ingredient/product Goods, make sure this ratio matches the previous one.
        static bool checkProportions(Recipe currentLargerRecipe, float largerCount, float smallerCount, ref float ratio, ref Recipe? larger) {
            if (largerCount / smallerCount != MathF.Floor(largerCount / smallerCount)) {
                return false;
            }
            if (ratio != 0 && ratio != largerCount / smallerCount) {
                return false;
            }
            if (larger != null && larger != currentLargerRecipe) {
                return false;
            }
            ratio = largerCount / smallerCount;
            larger = currentLargerRecipe;

            return true;
        }
    }

    /// <summary>
    /// Locates and stores all the links between different objects, e.g. which crafters can be used by a recipe, which recipes produce a particular product, and so on.
    /// </summary>
    /// <param name="netProduction">If <see langword="true"/>, recipe selection windows will only display recipes that provide net production or consumption of the <see cref="Goods"/> in question.
    /// If <see langword="false"/>, recipe selection windows will show all recipes that produce or consume any quantity of that <see cref="Goods"/>.<br/>
    /// For example, Kovarex enrichment will appear for both production and consumption of both U-235 and U-238 when <see langword="false"/>,
    /// but will appear as only producing U-235 and consuming U-238 when <see langword="true"/>.</param>
    private void CalculateMaps(bool netProduction) {
        DataBucket<Goods, Recipe> itemUsages = new DataBucket<Goods, Recipe>();
        DataBucket<Goods, Recipe> itemProduction = new DataBucket<Goods, Recipe>();
        DataBucket<Goods, FactorioObject> miscSources = new DataBucket<Goods, FactorioObject>();
        DataBucket<Entity, Item> entityPlacers = new DataBucket<Entity, Item>();
        DataBucket<Recipe, Technology> recipeUnlockers = new DataBucket<Recipe, Technology>();
        DataBucket<Location, Technology> locationUnlockers = new();
        DataBucket<Entity, Location> entityLocations = new();
        DataBucket<string, Location> autoplaceControlLocations = new();
        // Because actual recipe availability may be different than just "all recipes from that category" because of item slot limit and fluid usage restriction, calculate it here
        DataBucket<RecipeOrTechnology, EntityCrafter> actualRecipeCrafters = new DataBucket<RecipeOrTechnology, EntityCrafter>();
        DataBucket<Goods, Entity> usageAsFuel = new DataBucket<Goods, Entity>();
        List<Recipe> allRecipes = [];
        List<Mechanics> allMechanics = [];

        // step 1 - collect maps

        foreach (var o in allObjects) {
            switch (o) {
                case Technology technology:
                    foreach (var recipe in technology.unlockRecipes) {
                        recipeUnlockers.Add(recipe, technology);
                    }
                    foreach (Location location in technology.unlockLocations) {
                        locationUnlockers.Add(location, technology);
                    }

                    break;
                case Recipe recipe:
                    allRecipes.Add(recipe);

                    foreach (var product in recipe.products.GroupBy(p => p.goods)) {
                        // If the ingredient has variants and is an output, we aren't doing catalyst things: water@15-90 to water@90 does produce water@90,
                        // even if it consumes 10 water@15-90 to produce 9 water@90.
                        Ingredient? ingredient = recipe.ingredients.FirstOrDefault(i => i.goods == product.Key && i.variants is null);
                        float inputAmount = netProduction ? (ingredient?.amount ?? 0) : 0;
                        float outputAmount = product.Sum(p => p.amount);

                        if (outputAmount > inputAmount) {
                            itemProduction.Add(product.Key, recipe);
                        }
                    }

                    foreach (var ingredient in recipe.ingredients) {
                        // The reverse also applies. 9 water@15-90 to produce 10 water@15 consumes water@90, even though it's a net water producer.
                        float inputAmount = ingredient.amount;
                        IEnumerable<Product> products = recipe.products.Where(p => p.goods == ingredient.goods);
                        float outputAmount = netProduction && ingredient.variants is null ? products.Sum(p => p.amount) : 0;

                        if (ingredient.variants == null && inputAmount > outputAmount) {
                            itemUsages.Add(ingredient.goods, recipe);
                        }
                        else if (ingredient.variants != null) {
                            ingredient.goods = ingredient.variants[0];

                            foreach (var variant in ingredient.variants) {
                                itemUsages.Add(variant, recipe);
                            }
                        }
                    }
                    if (recipe is Mechanics mechanics) {
                        allMechanics.Add(mechanics);
                    }

                    break;
                case Item item:
                    if (placeResults.TryGetValue(item, out var placeResultNames)) {
                        item.placeResult = GetObject<Entity>(placeResultNames[0]);

                        foreach (string name in placeResultNames) {
                            entityPlacers.Add(GetObject<Entity>(name), item);
                        }
                    }
                    if (plantResults.TryGetValue(item, out string? plantResultName)) {
                        item.plantResult = GetObject<Entity>(plantResultName);
                        entityPlacers.Add(GetObject<Entity>(plantResultName), item, true);
                    }
                    if (item.fuelResult != null) {
                        miscSources.Add(item.fuelResult, item);
                    }

                    break;
                case Entity entity:
                    foreach (var product in entity.loot) {
                        miscSources.Add(product.goods, entity);
                    }

                    if (entity is EntityCrafter crafter) {
                        crafter.recipes = [.. recipeCrafters.GetRaw(crafter)
                            .SelectMany(x => recipeCategories.GetRaw(x).Where(y => y.CanFit(crafter.itemInputs, crafter.fluidInputs, crafter.inputs)))];
                        foreach (var recipe in crafter.recipes) {
                            actualRecipeCrafters.Add(recipe, crafter, true);
                        }
                    }

                    if (entity.energy != null && entity.energy != voidEntityEnergy) {
                        var fuelList = fuelUsers.GetRaw(entity).SelectMany(fuels.GetRaw);

                        if (entity.energy.type == EntityEnergyType.FluidHeat) {
                            fuelList = fuelList.Where(x => x is Fluid f && entity.energy.acceptedTemperature.Contains(f.temperature) && f.temperature > entity.energy.workingTemperature.min);
                        }

                        var fuelListArr = fuelList.ToArray();
                        entity.energy.fuels = fuelListArr;

                        foreach (var fuel in fuelListArr) {
                            usageAsFuel.Add(fuel, entity);
                        }
                    }

                    break;
                case Location location:
                    foreach (string name in location.entitySpawns ?? []) {
                        entityLocations.Add(GetObject<Entity>(name), location);
                    }
                    foreach (string name in location.placementControls ?? []) {
                        autoplaceControlLocations.Add(name, location);
                    }
                    break;
            }
        }

        voidEntityEnergy.fuels = [voidEnergy];

        actualRecipeCrafters.Seal();
        usageAsFuel.Seal();
        recipeUnlockers.Seal();
        locationUnlockers.Seal();
        entityPlacers.Seal();
        entityLocations.Seal();
        autoplaceControlLocations.Seal();
        asteroids.Seal();

        // step 2 - fill maps

        foreach (var o in allObjects) {
            switch (o) {
                case RecipeOrTechnology recipeOrTechnology:
                    if (recipeOrTechnology is Recipe recipe) {
                        recipe.FallbackLocalization(recipe.mainProduct, LSs.LocalizationFallbackDescriptionRecipeToCreate);
                        recipe.technologyUnlock = recipeUnlockers.GetArray(recipe);
                    }

                    recipeOrTechnology.crafters = actualRecipeCrafters.GetArray(recipeOrTechnology);
                    break;
                case Goods goods:
                    goods.usages = itemUsages.GetArray(goods);
                    goods.production = [.. itemProduction.GetArray(goods).Distinct()];
                    goods.miscSources = miscSources.GetArray(goods);

                    if (o is Item item) {
                        if (item.placeResult != null) {
                            item.FallbackLocalization(item.placeResult, LSs.LocalizationFallbackDescriptionItemToBuild);
                        }
                    }
                    else if (o is Fluid fluid && fluid.variants != null) {
                        if (fluid.locDescr == null) {
                            fluid.locDescr = LSs.FluidDescriptionTemperatureSolo.L(fluid.temperature);
                        }
                        else {
                            fluid.locDescr = LSs.FluidDescriptionTemperatureAdded.L(fluid.temperature, fluid.locDescr);
                        }
                    }

                    goods.fuelFor = usageAsFuel.GetArray(goods);
                    break;
                case Entity entity:
                    entity.itemsToPlace = entityPlacers.GetArray(entity);
                    if (entity.autoplaceControl != null) {
                        entity.spawnLocations = [.. entityLocations.GetArray(entity).Union(autoplaceControlLocations.GetArray(entity.autoplaceControl))];
                    }
                    else {
                        entity.spawnLocations = entityLocations.GetArray(entity);
                    }
                    if (entity.spawnLocations.Length > 0) {
                        entity.mapGenerated = true;
                    }

                    entity.sourceEntities = [.. asteroids.GetArray(entity.name)];
                    break;
                case Location location:
                    location.technologyUnlock = locationUnlockers.GetArray(location);
                    break;
            }
        }

        Queue<EntityCrafter> crafters = new(allObjects.OfType<EntityCrafter>());

        while (crafters.TryDequeue(out EntityCrafter? crafter)) {
            // If this is a crafter with a fixed recipe with data.raw.recipe["fixed-recipe-name"].enabled = false
            // (Exclude Mechanics; they aren't recipes in Factorio's fixed_recipe sense.)
            if (recipeCrafters.GetRaw(crafter).SingleOrDefault(s => s.StartsWith(SpecialNames.FixedRecipe), false) != null
                && crafter.recipes.SingleOrDefault(r => r.GetType() == typeof(Recipe), false) is Recipe { enabled: false } fixedRecipe) {

                bool addedUnlocks = false;

                foreach (Recipe itemRecipe in crafter.itemsToPlace.SelectMany(i => i.production)) {
                    // and (a recipe that creates an item that places) the crafter is accessible
                    // from the beginning of the game, the fixed recipe is also accessible.
                    if (itemRecipe.enabled) {
                        fixedRecipe.enabled = true;
                        addedUnlocks = true;
                        break;
                    }
                    // otherwise, the recipe is also unlocked by all technologies that
                    // unlock (a recipe that creates an item that places) the crafter.
                    else if (itemRecipe.technologyUnlock.Except(fixedRecipe.technologyUnlock).Any()) {
                        // Add the missing technology/ies
                        fixedRecipe.technologyUnlock = [.. fixedRecipe.technologyUnlock.Union(itemRecipe.technologyUnlock)];
                        addedUnlocks = true;
                    }
                }

                if (addedUnlocks) {
                    // If we added unlocks, and the fixed recipe creates (items that place) crafters,
                    // queue those crafters for a second check, in case they also have fixed recipes.
                    Item[] products = [.. fixedRecipe.products.Select(p => p.goods).OfType<Item>()];
                    foreach (EntityCrafter newCrafter in allObjects.OfType<EntityCrafter>()) {
                        if (newCrafter.itemsToPlace.Intersect(products).Any()) {
                            crafters.Enqueue(newCrafter);
                        }
                    }
                }
            }
        }

        foreach (var mechanic in allMechanics) {
            mechanic.locName = mechanic.localizationKey.Localize(mechanic.source.locName, mechanic.products.FirstOrDefault()?.goods.fluid?.temperature!);
            mechanic.locDescr = mechanic.source.locDescr;
            mechanic.iconSpec = mechanic.source.iconSpec;
        }

        // step 3a - detect voiding recipes
        // Do this first so voiding recipes don't re-normalize packed items/fluids.
        foreach (var recipe in allRecipes) {
            if (recipe.products.Length == 0) {
                recipe.specialType = FactorioObjectSpecialType.Voiding;
            }
        }

        // step 3b - detect packing/unpacking (e.g. barreling/unbarreling, stacking/unstacking, etc.) recipes
        foreach (var recipe in allRecipes) {
            if (recipe.specialType != FactorioObjectSpecialType.Normal) {
                continue;
            }

            if (recipe.products.Length != 1 || recipe.ingredients.Length == 0) {
                continue;
            }

            Goods packed = recipe.products[0].goods;

            if (countNonDsrRecipes(packed.usages) != 1 && countNonDsrRecipes(packed.production) != 1) {
                continue;
            }

            if (recipe.ingredients.Sum(i => i.amount) <= recipe.products.Sum(p => p.amount)) {
                // If `recipe` is part of packing/unpacking pair, it's the unpacking half. Ignore it until we find the packing half of the pair.
                continue;
            }

            foreach (var unpacking in packed.usages) {
                if (AreInverseRecipes(recipe, unpacking)) {
                    if (packed is Fluid && unpacking.products.All(p => p.goods is Fluid)) {
                        recipe.specialType = FactorioObjectSpecialType.Pressurization;
                        unpacking.specialType = FactorioObjectSpecialType.Pressurization;
                        packed.specialType = FactorioObjectSpecialType.Pressurization;
                    }
                    else if (packed is Item && unpacking.products.All(p => p.goods is Item)) {
                        if (unpacking.products.Length == 1) {
                            recipe.specialType = FactorioObjectSpecialType.Stacking;
                            unpacking.specialType = FactorioObjectSpecialType.Stacking;
                            packed.specialType = FactorioObjectSpecialType.Stacking;
                        }
                        else {
                            recipe.specialType = FactorioObjectSpecialType.Crating;
                            unpacking.specialType = FactorioObjectSpecialType.Crating;
                            packed.specialType = FactorioObjectSpecialType.Crating;
                        }
                    }
                    else if (packed is Item && unpacking.products.Any(p => p.goods is Item) && unpacking.products.Any(p => p.goods is Fluid)) {
                        recipe.specialType = FactorioObjectSpecialType.Barreling;
                        unpacking.specialType = FactorioObjectSpecialType.Barreling;
                        packed.specialType = FactorioObjectSpecialType.Barreling;
                    }
                    else {
                        continue;
                    }

                    // The packed good is used in other recipes or is fuel, constructs a building, or is a module. Only the unpacking recipe should be flagged as special.
                    if (countNonDsrRecipes(packed.usages) != 1 || (packed is Item item && (item.fuelValue != 0 || item.placeResult != null || item is Module))) {
                        recipe.specialType = FactorioObjectSpecialType.Normal;
                        packed.specialType = FactorioObjectSpecialType.Normal;
                    }

                    // The packed good can be mined or has a non-packing source. Only the packing recipe should be flagged as special.
                    if (packed.miscSources.OfType<Entity>().Any() || countNonDsrRecipes(packed.production) > 1) {
                        unpacking.specialType = FactorioObjectSpecialType.Normal;
                        packed.specialType = FactorioObjectSpecialType.Normal;
                    }
                }
            }
        }

        foreach (var any in allObjects) {
            any.locName ??= any.name;
        }

        foreach (var (_, list) in fluidVariants) {
            foreach (var fluid in list) {
                fluid.locName = LSs.FluidNameWithTemperature.L(fluid.locName, fluid.temperature);
            }
        }

        // The recipes added by deadlock_stacked_recipes (with CompressedFluids, if present) need to be filtered out to get decent results.
        // Also exclude recycling and voiding recipes: "I can recycle water barrels" does not count as "used in other recipes". (up ~25 lines)
        static int countNonDsrRecipes(IEnumerable<Recipe> recipes)
            => recipes.Count(r => !r.name.Contains("StackedRecipe-") && !r.name.Contains("DSR_HighPressure-")
                && r.specialType is not FactorioObjectSpecialType.Recycling and not FactorioObjectSpecialType.Voiding);
    }

    private Recipe CreateSpecialRecipe(FactorioObject production, string category, LocalizableString specialRecipeKey) {
        string fullName = category + (category.EndsWith('.') ? "" : ".") + production.name;

        if (registeredObjects.TryGetValue((typeof(Mechanics), fullName), out var recipeRaw)) {
            return (Recipe)recipeRaw;
        }

        var recipe = GetObject<Mechanics>(fullName);
        recipe.time = 1f;
        recipe.factorioType = SpecialNames.FakeRecipe;
        recipe.name = fullName;
        recipe.source = production;
        recipe.localizationKey = specialRecipeKey;
        recipe.enabled = true;
        recipe.hidden = true;
        recipe.technologyUnlock = [];
        recipeCategories.Add(category, recipe);

        return recipe;
    }

    private void ParseCaptureEffects() {
        HashSet<string> captureRobots = [.. allObjects.Where(e => e.factorioType == "capture-robot").Select(e => e.name)];
        // Projectiles that create capture robots.
        HashSet<string> captureProjectiles = [.. allObjects.OfType<EntityProjectile>().Where(p => p.placeEntities.Intersect(captureRobots).Any()).Select(p => p.name)];
        // Ammo that creates projectiles that create capture robots.
        List<Ammo> captureAmmo = [.. allObjects.OfType<Ammo>().Where(a => captureProjectiles.Intersect(a.projectileNames).Any())];

        Dictionary<string, Entity> entities = allObjects.OfType<Entity>().ToDictionary(e => e.name);
        foreach (Ammo ammo in captureAmmo) {
            foreach (EntitySpawner spawner in allObjects.OfType<EntitySpawner>()) {
                if ((ammo.targetFilter == null || ammo.targetFilter.Contains(spawner.name)) && spawner.capturedEntityName != null) {
                    if (!entities[spawner.capturedEntityName].captureAmmo.Contains(ammo)) {
                        entities[spawner.capturedEntityName].captureAmmo.Add(ammo);
                    }
                }
            }
        }
    }

    private class DataBucket<TKey, TValue> : IEqualityComparer<List<TValue>> where TKey : notnull where TValue : notnull {
        private readonly Dictionary<TKey, IList<TValue>> storage = [];
        /// <summary>This function provides a default list of values for the key for when the key is not present in the storage.</summary>
        /// <remarks>The provided function must *must not* return null.</remarks>
        private Func<TKey, IEnumerable<TValue>> defaultList = NoExtraItems;

        /// <summary>When true, it is not allowed to add new items to this bucket.</summary>
        private bool isSealed;

        /// <summary>
        /// Replaces the list values in storage with array values while (optionally) adding extra values depending on the item.
        /// </summary>
        /// <param name="addExtraItems">Function to provide extra items, *must not* return null.</param>
        public void Seal(Func<TKey, IEnumerable<TValue>>? addExtraItems = null) {
            if (isSealed) {
                throw new InvalidOperationException("Data bucket is already sealed");
            }

            if (addExtraItems != null) {
                defaultList = addExtraItems;
            }

            KeyValuePair<TKey, IList<TValue>>[] values = [.. storage];

            foreach ((TKey key, IList<TValue> value) in values) {
                if (value is not List<TValue> list) {
                    // Unexpected type, (probably) never happens
                    continue;
                }

                // Add the extra values to the list when provided before storing the complete array.
                IEnumerable<TValue> completeList = addExtraItems != null ? list.Concat(addExtraItems(key)) : list;
                TValue[] completeArray = [.. completeList];

                storage[key] = completeArray;
            }

            isSealed = true;
        }

        public void Add(TKey key, TValue value, bool checkUnique = false) {
            if (isSealed) {
                throw new InvalidOperationException("Data bucket is sealed");
            }

            if (key == null) {
                return;
            }

            if (!storage.TryGetValue(key, out var list)) {
                storage[key] = [value];
            }
            else if (!checkUnique || !list.Contains(value)) {
                list.Add(value);
            }
        }

        public TValue[] GetArray(TKey key) {
            if (!storage.TryGetValue(key, out var list)) {
                return [.. defaultList(key)];
            }

            return list is TValue[] value ? value : [.. list];
        }

        public IList<TValue> GetRaw(TKey key) {
            if (!storage.TryGetValue(key, out var list)) {
                list = [.. defaultList(key)];

                if (isSealed) {
                    list = [.. list];
                }

                storage[key] = list;
            }
            return list;
        }

        ///<summary>Just return an empty enumerable.</summary>
        private static IEnumerable<TValue> NoExtraItems(TKey item) => [];

        public bool Equals(List<TValue>? x, List<TValue>? y) {
            if (x is null && y is null) {
                return true;
            }

            if (x is null || y is null || x.Count != y.Count) {
                return false;
            }

            var comparer = EqualityComparer<TValue>.Default;
            for (int i = 0; i < x.Count; i++) {
                if (!comparer.Equals(x[i], y[i])) {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(List<TValue> obj) {
            int count = obj.Count;
            return count == 0 ? 0 : (((obj.Count * 347) + obj[0].GetHashCode()) * 347) + obj[count - 1].GetHashCode();
        }
    }

    public static Type? TypeNameToType(string? typeName) => typeName switch {
        "item" => typeof(Item),
        "fluid" => typeof(Fluid),
        "technology" => typeof(Technology),
        "recipe" => typeof(Recipe),
        "entity" => typeof(Entity),
        _ => null,
    };

    private void ParseModYafcHandles(LuaTable? scriptEnabled) {
        if (scriptEnabled != null) {
            foreach (object? element in scriptEnabled.ArrayElements) {
                if (element is LuaTable table) {
                    _ = table.Get("type", out string? type);
                    _ = table.Get("name", out string? name);
                    if (registeredObjects.TryGetValue((TypeNameToType(type), name), out var existing)) {
                        rootAccessible.Add(existing);
                    }
                }
            }
        }
    }
}
