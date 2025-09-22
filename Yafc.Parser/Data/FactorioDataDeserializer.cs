using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SDL2;
using Serilog;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc.Parser;

internal partial class FactorioDataDeserializer {
    private static readonly ILogger logger = Logging.GetLogger<FactorioDataDeserializer>();
    private LuaTable raw = null!; // null-forgiving: Initialized at the beginning of LoadData.
    private bool GetRef<T>(LuaTable table, string key, [NotNullWhen(true)] out T? result) where T : FactorioObject, new() {
        result = null;
        if (!table.Get(key, out string? name)) {
            return false;
        }

        result = GetObject<T>(name);

        return true;
    }

    private T? GetRef<T>(LuaTable table, string key) where T : FactorioObject, new() {
        _ = GetRef<T>(table, key, out var result);

        return result;
    }

    private Fluid GetFluidFixedTemp(string key, int temperature) {
        var basic = GetObject<Fluid>(key);

        if (basic.temperature == temperature) {
            return basic;
        }

        if (temperature < basic.temperatureRange.min) {
            temperature = basic.temperatureRange.min;
        }

        string idWithTemp = key + "@" + temperature;

        if (basic.temperature == 0) {
            basic.SetTemperature(temperature);
            registeredObjects[(typeof(Fluid), idWithTemp)] = basic;

            return basic;
        }

        if (registeredObjects.TryGetValue((typeof(Fluid), idWithTemp), out var fluidWithTemp)) {
            return (Fluid)fluidWithTemp;
        }

        var split = SplitFluid(basic, temperature);
        allObjects.Add(split);
        registeredObjects[(typeof(Fluid), idWithTemp)] = split;

        return split;
    }

    private void UpdateSplitFluids() {
        HashSet<List<Fluid>> processedFluidLists = [];

        foreach (var fluid in allObjects.OfType<Fluid>()) {
            if (fluid.temperature == 0) {
                fluid.temperature = fluid.temperatureRange.min;
            }

            if (fluid.variants == null || !processedFluidLists.Add(fluid.variants)) {
                continue;
            }

            fluid.variants.Sort(DataUtils.FluidTemperatureComparer);
            fluidVariants[fluid.typeDotName] = fluid.variants;

            foreach (var variant in fluid.variants) {
                AddTemperatureToFluidIcon(variant);
                variant.name += "@" + variant.temperature;
            }
        }
    }

    private static void AddTemperatureToFluidIcon(Fluid fluid) {
        string iconStr = fluid.temperature + "d";
        fluid.iconSpec =
        [
            .. fluid.iconSpec ?? [],
            .. iconStr.Take(4).Select((x, n) => new FactorioIconPart("__.__/" + x) { size = 64, y = -32, x = (n * 14) - 24, scale = 0.28f }),
        ];
    }

    /// <summary>
    /// Process the data loaded from Factorio and the mods, and load the project specified by <paramref name="projectPath"/>.
    /// </summary>
    /// <param name="projectPath">The path to the project file to create or load. May be <see langword="null"/> or empty.</param>
    /// <param name="data">The Lua table data (containing data.raw) that was populated by the lua scripts.</param>
    /// <param name="prototypes">The Lua table defines.prototypes that was populated by the lua scripts.</param>
    /// <param name="netProduction">If <see langword="true"/>, recipe selection windows will only display recipes that provide net production or consumption 
    /// of the <see cref="Goods"/> in question.
    /// If <see langword="false"/>, recipe selection windows will show all recipes that produce or consume any quantity of that <see cref="Goods"/>.<br/>
    /// For example, Kovarex enrichment will appear for both production and consumption of both U-235 and U-238 when <see langword="false"/>,
    /// but will appear as only producing U-235 and consuming U-238 when <see langword="true"/>.</param>
    /// <param name="progress">An <see cref="IProgress{T}"/> that receives two strings describing the current loading state.</param>
    /// <param name="errorCollector">An <see cref="ErrorCollector"/> that will collect the errors and warnings encountered while loading and processing the file and data.</param>
    /// <param name="renderIcons">If <see langword="true"/>, Yafc will render the icons necessary for UI display.</param>
    /// <param name="useLatestSave">If <see langword="true"/>, Yafc will try to find the most recent autosave.</param>
    /// <returns>A <see cref="Project"/> containing the information loaded from <paramref name="projectPath"/>. Also sets the <see langword="static"/> properties
    /// in <see cref="Database"/>.</returns>
    public Project LoadData(string projectPath, LuaTable data, LuaTable prototypes, bool netProduction,
        IProgress<(string, string)> progress, ErrorCollector errorCollector, bool renderIcons, bool useLatestSave) {

        progress.Report((LSs.ProgressLoading, LSs.ProgressLoadingItems));
        raw = (LuaTable?)data["raw"] ?? throw new ArgumentException("Could not load data.raw from data argument", nameof(data));
        LuaTable itemPrototypes = (LuaTable?)prototypes?["item"] ?? throw new ArgumentException("Could not load prototypes.item from data argument", nameof(prototypes));

        foreach (object prototypeName in Item.ExplicitPrototypeLoadOrder.Intersect(itemPrototypes.ObjectElements.Keys)) {
            DeserializePrototypes(raw, (string)prototypeName, DeserializeItem, progress, errorCollector);
        }

        foreach (object prototypeName in itemPrototypes.ObjectElements.Keys.Except(Item.ExplicitPrototypeLoadOrder)) {
            DeserializePrototypes(raw, (string)prototypeName, DeserializeItem, progress, errorCollector);
        }

        allModules.AddRange(allObjects.OfType<Module>());
        progress.Report((LSs.ProgressLoading, LSs.ProgressLoadingFluids));
        DeserializePrototypes(raw, "fluid", DeserializeFluid, progress, errorCollector);
        progress.Report((LSs.ProgressLoading, LSs.ProgressLoadingTiles));
        // pumpable tiles depend on fluids, so they can read the default fluid temperature
        DeserializePrototypes(raw, "tile", DeserializeTile, progress, errorCollector);
        progress.Report((LSs.ProgressLoading, LSs.ProgressLoadingRecipes));
        DeserializePrototypes(raw, "recipe", DeserializeRecipe, progress, errorCollector);
        progress.Report((LSs.ProgressLoading, LSs.ProgressLoadingLocations));
        DeserializePrototypes(raw, "planet", DeserializeLocation, progress, errorCollector);
        DeserializePrototypes(raw, "space-location", DeserializeLocation, progress, errorCollector);
        rootAccessible.Add(GetObject<Location>("nauvis"));
        progress.Report((LSs.ProgressLoading, LSs.ProgressLoadingTechnologies));
        DeserializePrototypes(raw, "technology", DeserializeTechnology, progress, errorCollector);
        progress.Report((LSs.ProgressLoading, LSs.ProgressLoadingQualities));
        DeserializePrototypes(raw, "quality", DeserializeQuality, progress, errorCollector);
        Quality.Normal = GetObject<Quality>("normal");
        rootAccessible.Add(Quality.Normal);
        DeserializePrototypes(raw, "asteroid-chunk", DeserializeAsteroidChunk, progress, errorCollector);

        progress.Report((LSs.ProgressLoading, LSs.ProgressLoadingEntities));
        LuaTable entityPrototypes = (LuaTable?)prototypes["entity"] ?? throw new ArgumentException("Could not load prototypes.entity from data argument", nameof(prototypes));
        foreach (object prototypeName in entityPrototypes.ObjectElements.Keys) {
            DeserializePrototypes(raw, (string)prototypeName, DeserializeEntity, progress, errorCollector);
        }

        ParseCaptureEffects();

        ParseModYafcHandles(data["script_enabled"] as LuaTable);
        progress.Report((LSs.ProgressPostprocessing, LSs.ProgressComputingMaps));
        // Deterministically sort all objects
        allObjects.Sort((a, b) => a.sortingOrder == b.sortingOrder ? string.Compare(a.typeDotName, b.typeDotName, StringComparison.Ordinal) : a.sortingOrder - b.sortingOrder);

        for (int i = 0; i < allObjects.Count; i++) {
            allObjects[i].id = (FactorioId)i;
        }

        rocketCapacity = raw.Get<LuaTable>("utility-constants").Get<LuaTable>("default").Get("rocket_lift_weight", 1000000);
        defaultItemWeight = raw.Get<LuaTable>("utility-constants").Get<LuaTable>("default").Get("default_item_weight", 100);
        UpdateSplitFluids();
        UpdateRecipeIngredientFluids(errorCollector);
        CalculateMaps(netProduction);
        var iconRenderTask = renderIcons ? Task.Run(RenderIcons) : Task.CompletedTask;
        UpdateRecipeCatalysts();
        CalculateItemWeights();
        ObjectWithQuality.LoadCache(allObjects);
        ExportBuiltData();
        progress.Report((LSs.ProgressPostprocessing, LSs.ProgressCalculatingDependencies));
        Dependencies.Calculate();
        TechnologyLoopsFinder.FindTechnologyLoops();
        progress.Report((LSs.ProgressPostprocessing, LSs.ProgressCreatingProject));
        Project project = Project.ReadFromFile(projectPath, errorCollector, useLatestSave);
        Analysis.ProcessAnalyses(progress, project, errorCollector);
        progress.Report((LSs.ProgressRenderingIcons, ""));
        iconRenderedProgress = progress;
        iconRenderTask.Wait();

        return project;
    }

    private IProgress<(string, string)>? iconRenderedProgress;

    private Icon CreateSimpleIcon(Dictionary<(string mod, string path), IntPtr> cache, string graphicsPath)
        => CreateIconFromSpec(cache, new FactorioIconPart("__core__/graphics/" + graphicsPath + ".png"));

    private void RenderIcons() {
        Dictionary<(string mod, string path), IntPtr> cache = [];
        try {
            foreach (char digit in "0123456789d") {
                cache[(".", digit.ToString())] = SDL_image.IMG_Load("Data/Digits/" + digit + ".png");
            }

            DataUtils.NoFuelIcon = CreateSimpleIcon(cache, "fuel-icon-red");
            DataUtils.WarningIcon = CreateSimpleIcon(cache, "warning-icon");
            DataUtils.HandIcon = CreateSimpleIcon(cache, "hand");

            Dictionary<string, Icon> simpleSpritesCache = [];
            int rendered = 0;

            foreach (var o in allObjects) {
                if (++rendered % 100 == 0) {
                    iconRenderedProgress?.Report((LSs.ProgressRenderingIcons, LSs.ProgressRenderingXOfY.L(rendered, allObjects.Count)));
                }

                if (o.iconSpec != null && o.iconSpec.Length > 0) {
                    bool simpleSprite = o.iconSpec.Length == 1 && o.iconSpec[0].IsSimple();

                    if (simpleSprite && simpleSpritesCache.TryGetValue(o.iconSpec[0].path, out var icon)) {
                        o.icon = icon;
                        continue;
                    }

                    try {
                        o.icon = CreateIconFromSpec(cache, o.iconSpec);

                        if (simpleSprite) {
                            simpleSpritesCache[o.iconSpec[0].path] = o.icon;
                        }
                    }
                    catch (Exception ex) {
                        Console.Error.WriteException(ex);
                    }
                }
                else if (o is Recipe recipe && recipe.mainProduct != null) {
                    o.icon = recipe.mainProduct.icon;
                }
            }
        }
        finally {
            foreach (var (_, image) in cache) {
                if (image != IntPtr.Zero) {
                    SDL.SDL_FreeSurface(image);
                }
            }
        }
    }

    private unsafe Icon CreateIconFromSpec(Dictionary<(string mod, string path), IntPtr> cache, params FactorioIconPart[] spec) {
        const int cachedIconSize = IconCollection.IconSize;
        int renderSize = MathUtils.Round(spec[0].size * spec[0].scale);
        nint targetSurface = SDL.SDL_CreateRGBSurfaceWithFormat(0, renderSize, renderSize, 0, SDL.SDL_PIXELFORMAT_RGBA8888);
        _ = SDL.SDL_SetSurfaceBlendMode(targetSurface, SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND);

        foreach (var icon in spec) {
            var modPath = FactorioDataSource.ResolveModPath("", icon.path);

            if (!cache.TryGetValue(modPath, out nint image)) {
                byte[] imageSource = FactorioDataSource.ReadModFile(modPath.mod, modPath.path);

                if (imageSource == null) {
                    image = cache[modPath] = IntPtr.Zero;
                }
                else {
                    fixed (byte* data = imageSource) {
                        nint src = SDL.SDL_RWFromMem((IntPtr)data, imageSource.Length);
                        image = SDL_image.IMG_Load_RW(src, (int)SDL.SDL_bool.SDL_TRUE);

                        if (image != IntPtr.Zero) {
                            ref var surface = ref RenderingUtils.AsSdlSurface(image);
                            uint format = Unsafe.AsRef<SDL.SDL_PixelFormat>((void*)surface.format).format;

                            if (format != SDL.SDL_PIXELFORMAT_RGB24 && format != SDL.SDL_PIXELFORMAT_RGBA8888) {
                                // SDL is failing to blit palette surfaces, converting them
                                nint old = image;
                                image = SDL.SDL_ConvertSurfaceFormat(old, SDL.SDL_PIXELFORMAT_RGBA8888, 0);
                                SDL.SDL_FreeSurface(old);
                            }
                        }
                        cache[modPath] = image;
                    }
                }
            }

            if (image == IntPtr.Zero) {
                continue;
            }

            ref var sdlSurface = ref RenderingUtils.AsSdlSurface(image);
            int targetSize = MathUtils.Round(icon.size * icon.scale);
            _ = SDL.SDL_SetSurfaceColorMod(image, MathUtils.FloatToByte(icon.r), MathUtils.FloatToByte(icon.g), MathUtils.FloatToByte(icon.b));
            //SDL.SDL_SetSurfaceAlphaMod(image, MathUtils.FloatToByte(icon.a));
            int basePosition = (renderSize - targetSize) / 2;
            SDL.SDL_Rect targetRect = new SDL.SDL_Rect {
                x = basePosition,
                y = basePosition,
                w = targetSize,
                h = targetSize
            };

            if (icon.x != 0) {
                // These two formulas have variously multiplied icon.x (or icon.y) by a scaling factor of iconSize / icon.size,
                // iconSize * icon.scale, or iconSize (iconSize is now const int cachedIconSize = 32).
                // Presumably the scaling factor had a purpose, but I can't find it. Py and Vanilla objects (e.g. Recipe.Moss-1 and
                // Entity.lane-splitter) draw correctly after removing the scaling factor.
                targetRect.x = MathUtils.Clamp(targetRect.x + MathUtils.Round(icon.x), 0, renderSize - targetRect.w);
            }

            if (icon.y != 0) {
                targetRect.y = MathUtils.Clamp(targetRect.y + MathUtils.Round(icon.y), 0, renderSize - targetRect.h);
            }

            SDL.SDL_Rect srcRect = new SDL.SDL_Rect {
                w = sdlSurface.h, // That is correct (cutting mip maps)
                h = sdlSurface.h
            };
            _ = SDL.SDL_BlitScaled(image, ref srcRect, targetSurface, ref targetRect);
        }

        if (RenderingUtils.AsSdlSurface(targetSurface).h > cachedIconSize * 2) {
            targetSurface = SoftwareScaler.DownscaleIcon(targetSurface, cachedIconSize);
        }
        return IconCollection.AddIcon(targetSurface);
    }

    private static void DeserializePrototypes(LuaTable data, string type, Action<LuaTable, ErrorCollector> deserializer,
        IProgress<(string, string)> progress, ErrorCollector errorCollector) {

        object? table = data[type];
        progress.Report((LSs.ProgressBuildingObjects, type));

        if (table is not LuaTable luaTable) {
            return;
        }

        foreach (var entry in luaTable.ObjectElements) {
            if (entry.Value is LuaTable entryTable) {
                deserializer(entryTable, errorCollector);
            }
        }
    }

    private static float ParseEnergy(string? energy) {
        if (energy is null || energy.Length < 2) {
            return 0f;
        }

        char energyMul = energy[^2];
        // internally store energy in megawatts / megajoules to be closer to 1
        if (char.IsLetter(energyMul)) {
            float energyBase = float.Parse(energy[..^2]);

            switch (energyMul) {
                // 2.0 only allows k; 1.1 allows either. Assume 2.0 mods don't rely on whatever Factorio does with 'K'.
                case 'k' or 'K': return energyBase * 1e-3f;
                case 'M': return energyBase;
                case 'G': return energyBase * 1e3f;
                case 'T': return energyBase * 1e6f;
                case 'P': return energyBase * 1e9f;
                case 'E': return energyBase * 1e12f;
                case 'Z': return energyBase * 1e15f;
                case 'Y': return energyBase * 1e18f;
                case 'R': return energyBase * 1e21f;
                case 'Q': return energyBase * 1e24f;
            }
        }
        return float.Parse(energy[..^1]) * 1e-6f;
    }

    private static Effect ParseEffect(LuaTable table) {
        return new Effect {
            consumption = load(table, "consumption"),
            speed = load(table, "speed"),
            productivity = load(table, "productivity"),
            pollution = load(table, "pollution"),
            quality = load(table, "quality"),
        };

        // table[effect].bonus in 1.1; table[effect] in 2.0. Assume [effect] is not a table in 2.0.
        static float load(LuaTable table, string effect) => table.Get<LuaTable>(effect)?.Get("bonus", 0f) ?? table.Get(effect, 0f);
    }

    private static EffectReceiver ParseEffectReceiver(LuaTable? table) {
        if (table == null) {
            return new EffectReceiver {
                baseEffect = new Effect(),
                usesModuleEffects = true,
                usesBeaconEffects = true,
                usesSurfaceEffects = true
            };
        }

        return new EffectReceiver {
            baseEffect = table.Get("base_effect", out LuaTable? baseEffect) ? ParseEffect(baseEffect) : new Effect(),
            usesModuleEffects = table.Get("uses_module_effects", true),
            usesBeaconEffects = table.Get("uses_beacon_effects ", true),
            usesSurfaceEffects = table.Get("uses_surface_effects ", true),
        };
    }

    private void DeserializeItem(LuaTable table, ErrorCollector _1) {
        if (table.Get("type", "") == "module" && table.Get("effect", out LuaTable? moduleEffect)) {
            Module module = GetObject<Item, Module>(table);
            var effect = ParseEffect(moduleEffect);
            module.moduleSpecification = new ModuleSpecification {
                category = table.Get("category", ""),
                baseConsumption = effect.consumption,
                baseSpeed = effect.speed,
                baseProductivity = effect.productivity,
                basePollution = effect.pollution,
                baseQuality = effect.quality,
            };
        }
        else if (table.Get("type", "") == "ammo" && table["ammo_type"] is LuaTable ammo_type) {
            Ammo ammo = GetObject<Item, Ammo>(table);
            ammo_type.ReadObjectOrArray(readAmmoType);

            if (ammo_type["target_filter"] is LuaTable targets) {
                ammo.targetFilter = [.. targets.ArrayElements.OfType<string>()];
            }

            void readAmmoType(LuaTable table) {
                if (table["action"] is LuaTable action) {
                    action.ReadObjectOrArray(readTrigger);
                }
            }
            void readTrigger(LuaTable table) {
                if (table.Get<string>("type") == "direct" && table["action_delivery"] is LuaTable delivery && delivery.Get<string>("type") == "projectile") {
                    ammo.projectileNames.Add(delivery.Get<string>("projectile")!);
                }
            }
        }

        Item item = DeserializeCommon<Item>(table, "item");

        if (table.Get("place_result", out string? placeResult) && !string.IsNullOrEmpty(placeResult)) {
            placeResults[item] = [placeResult];
        }

        item.stackSize = table.Get("stack_size", 1);

        // place_as for 2.0, placed_as for 1.1
        if (item.locName == null && (table.Get("place_as_equipment_result", out string? result) || table.Get("placed_as_equipment_result", out result))) {
            item.locName = LocalisedStringParser.ParseKey("equipment-name." + result, [])!;
        }
        if (table.Get("fuel_value", out string? fuelValue)) {
            item.fuelValue = ParseEnergy(fuelValue);
            item.fuelResult = GetRef<Item>(table, "burnt_result");

            if (table.Get("fuel_category", out string? category)) {
                fuels.Add(category, item);
            }
        }

        Product[]? launchProducts = null;
        if (table.Get("send_to_orbit_mode", "not-sendable") != "not-sendable" || item.factorioType == "space-platform-starter-pack"
            || factorioVersion < v2_0) {

            if (table.Get("rocket_launch_product", out LuaTable? product)) {
                launchProducts = [LoadProduct("rocket_launch_product", item.stackSize)(product)];
            }
            else if (table.Get("rocket_launch_products", out LuaTable? products)) {
                launchProducts = [.. products.ArrayElements<LuaTable>().Select(LoadProduct(item.typeDotName, item.stackSize))];
            }
            else if (factorioVersion >= v2_0) {
                launchProducts = [];
            }
        }
        if (launchProducts != null) {
            EnsureLaunchRecipe(item, launchProducts);
        }

        if (table.Get("weight", out int weight)) {
            item.weight = weight;
        }

        if (table.Get("ingredient_to_weight_coefficient", out float ingredient_to_weight_coefficient)) {
            item.ingredient_to_weight_coefficient = ingredient_to_weight_coefficient;
        }

        if (GetRef(table, "spoil_result", out Item? spoiled)) {
            var recipe = CreateSpecialRecipe(item, SpecialNames.SpoilRecipe, LSs.SpecialRecipeSpoiling);
            recipe.ingredients = [new Ingredient(item, 1)];
            recipe.products = [new Product(spoiled, 1)];
            recipe.time = table.Get("spoil_ticks", 0) / 60f;
        }
        // Read spoil-into-entity triggers
        else if (table["spoil_to_trigger_result"] is LuaTable spoil && spoil["trigger"] is LuaTable triggers) {
            triggers.ReadObjectOrArray(readTrigger);

            void readTrigger(LuaTable trigger) {
                if (trigger.Get<string>("type") == "direct" && trigger["action_delivery"] is LuaTable delivery) {
                    delivery.ReadObjectOrArray(readDelivery);
                }
            }
            void readDelivery(LuaTable delivery) {
                if (delivery.Get<string>("type") == "instant" && delivery["source_effects"] is LuaTable effects) {
                    effects.ReadObjectOrArray(readEffect);
                }
            }
            void readEffect(LuaTable effect) {
                if (effect.Get<string>("type") == "create-entity") {
                    float spoilTime = table.Get("spoil_ticks", 0) / 60f;
                    string entityName = "Entity." + effect.Get<string>("entity_name");
                    item.getBaseSpoilTime = new(() => spoilTime);
                    item.getSpoilResult = new(() => {
                        _ = Database.objectsByTypeName.TryGetValue(entityName, out FactorioObject? result);
                        return result;
                    });
                }
            }
        }

        if (table.Get("plant_result", out string? plantResult) && !string.IsNullOrEmpty(plantResult)) {
            plantResults[item] = plantResult;
        }
    }

    // This was constructed from educated guesses and https://forums.factorio.com/viewtopic.php?f=23&t=120781.
    // It was compared with https://rocketcal.cc/weights.json. Where the results differed, these results were verified in Factorio.
    private void CalculateItemWeights() {
        if (factorioVersion < v2_0) {
            return;
        }

        Dictionary<Item, List<Item>> dependencies = [];
        foreach (Recipe recipe in allObjects.OfType<Recipe>()) {
            foreach (Item ingredient in recipe.ingredients.Select(i => i.goods).OfType<Item>()) {
                if (!dependencies.TryGetValue(ingredient, out List<Item>? dependents)) {
                    dependencies[ingredient] = dependents = [];
                }
                dependents.AddRange(recipe.products.Select(p => p.goods).OfType<Item>());
            }
        }

        Queue<Item> queue = new(allObjects.OfType<Item>());
        while (queue.TryDequeue(out Item? item)) {
            if (item.weight != 0) {
                continue;
            }
            if (item.stackSize == 0) {
                continue; // Py's synthetic items sometimes have a stack size of 0.
            }

            Recipe? recipe = allObjects.OfType<Recipe>().FirstOrDefault(r => r.name == item.name);
            // Hidden recipes appear to be ignored by Factorio; a pistol weighs 100g, not the 200kg that would be expected from its stack size.
            if (recipe?.products.Length > 0 && recipe.ingredients.Length > 0 && !recipe.hidden) {
                float weight = 0;
                foreach (Ingredient ingredient in recipe.ingredients) {
                    if (ingredient.goods is Item i) {
                        if (i.weight == 0) {
                            // Wait to calculate this weight until we have the weights of all its ingredients
                            goto nextWeightCalculation;
                        }

                        weight += i.weight * ingredient.amount;
                    }
                    else { // fluid
                        // Barrels gain 5 kg when filled with 50 units of fluid.
                        // This matches data.raw.utility-constants.default_item_weight, so that seems like a good value to use.
                        weight += defaultItemWeight * ingredient.amount;
                    }
                }

                item.weight = (int)(weight / recipe.products[0].amount * item.ingredient_to_weight_coefficient);

                if (!recipe.allowedEffects.HasFlag(AllowedEffects.Productivity)) {
                    // When productivity is disallowed, a rocket can never carry more than one stack.
                    item.weight = Math.Max(item.weight, rocketCapacity / item.stackSize);
                }
                else if (item.weight * item.stackSize < rocketCapacity) {
                    // When productivity is allowed, a rocket can carry either < 1 stack or an integer number of stacks.
                    // Do lots of integer division to floor the stack count.
                    item.weight = rocketCapacity / (rocketCapacity / item.weight / item.stackSize) / item.stackSize;
                }

                if (dependencies.TryGetValue(item, out List<Item>? products)) {
                    foreach (Item product in dependencies[item]) {
                        if (product.weight == 0) {
                            queue.Enqueue(product);
                        }
                    }
                }
            }
nextWeightCalculation:;
        }

        int maxStacks = registeredObjects.Values.OfType<EntityCrafter>().Where(e => e.factorioType == "rocket-silo").MaxBy(e => e.rocketInventorySize)?.rocketInventorySize
            ?? 1; // if we have no rocket silos, default to one stack.

        foreach (Item item in allObjects.OfType<Item>()) {
            // If it doesn't otherwise have a weight, it gets the default weight.
            if (item.weight == 0) {
                item.weight = defaultItemWeight;
            }

            // The item count is initialized to 1, but it should be the rocket capacity. Scale up the ingredient and product(s).
            // item.weight == 0 is possible if defaultItemWeight is 0, so we bail out on the / item.weight in that case.
            int maxFactor = maxStacks * item.stackSize;
            int factor = item.weight == 0 ? maxFactor : Math.Min(rocketCapacity / item.weight, maxFactor);

            item.rocketCapacity = factor;

            if (registeredObjects.TryGetValue((typeof(Mechanics), SpecialNames.RocketLaunch + "." + item.name), out FactorioObject? r)
                && r is Mechanics recipe) {

                recipe.ingredients[0] = new(item, factor);
                for (int i = 0; i < recipe.products.Length; i++) {
                    recipe.products[i] *= factor;
                }
            }
        }
    }

    /// <summary>
    /// Creates, or confirms the existence of, a recipe for launching a particular item.
    /// </summary>
    /// <param name="item">The <see cref="Item"/> to be launched.</param>
    /// <param name="launchProducts">The result of launching <see cref="Item"/>, if known. Otherwise <see langword="null"/>, to preserve
    /// the existing launch products of a preexisting recipe, or set no products for a new recipe.</param>
    private void EnsureLaunchRecipe(Item item, Product[]? launchProducts) {
        Recipe recipe = CreateSpecialRecipe(item, SpecialNames.RocketLaunch, LSs.SpecialRecipeLaunched);
        // When this is called in 2.0, we don't know the item weight or the rocket capacity.
        // CalculateItemWeights will scale this ingredient and the products appropriately (but not the launch slot), only in 2.0.
        int ingredientCount = factorioVersion < v2_0 ? item.stackSize : 1;
        recipe.ingredients =
        [
            new Ingredient(item, ingredientCount),
            new Ingredient(rocketLaunch, 1),
        ];
        recipe.products = launchProducts ?? recipe.products ?? [];
        recipe.time = 0f; // TODO what to put here?
    }

    private Fluid SplitFluid(Fluid basic, int temperature) {
        logger.Information("Splitting fluid {FluidName} at {Temperature}", basic.name, temperature);
        basic.variants ??= [basic];
        var copy = basic.Clone();
        copy.SetTemperature(temperature);
        copy.variants!.Add(copy); // null-forgiving: Clone was given a non-null variants.

        if (copy.fuelValue > 0f) {
            fuels.Add(SpecialNames.BurnableFluid, copy);
        }

        fuels.Add(SpecialNames.SpecificFluid + basic.name, copy);

        return copy;
    }

    private void DeserializeTile(LuaTable table, ErrorCollector _) {
        var tile = DeserializeCommon<Tile>(table, "tile");

        if (table.Get("fluid", out string? fluid)) {
            Fluid baseFluid = GetObject<Fluid>(fluid);
            // Explicitly produce this fluid at the default temperature, to ensure the temperature split is created.
            Fluid pumpingFluid = GetFluidFixedTemp(fluid, baseFluid.temperatureRange.min);
            tile.Fluid = pumpingFluid;

            string recipeCategory = SpecialNames.PumpingRecipe + "tile";
            Recipe recipe = CreateSpecialRecipe(pumpingFluid, recipeCategory, LSs.SpecialRecipePumping);
            // We changed the names of pumping recipes when adding support for 2.0.
            formerAliases[$"Mechanics.pump.{pumpingFluid.name}.{pumpingFluid.name}"] = recipe;

            if (recipe.products == null) {
                recipe.products = [new Product(pumpingFluid, 1200f)]; // set to Factorio default pump amounts - looks nice in tooltip
                recipe.ingredients = [];
                recipe.time = 1f;
            }
            recipe.sourceTiles.Add(tile);
        }
    }

    private void DeserializeFluid(LuaTable table, ErrorCollector _) {
        var fluid = DeserializeCommon<Fluid>(table, "fluid");
        fluid.originalName = fluid.name;

        if (table.Get("fuel_value", out string? fuelValue)) {
            fluid.fuelValue = ParseEnergy(fuelValue);
            fuels.Add(SpecialNames.BurnableFluid, fluid);
        }

        fuels.Add(SpecialNames.SpecificFluid + fluid.name, fluid);

        if (table.Get("heat_capacity", out string? heatCap)) {
            fluid.heatCapacity = ParseEnergy(heatCap);
        }

        fluid.temperatureRange = new TemperatureRange(table.Get("default_temperature", 0), table.Get("max_temperature", 0));
    }

    private Goods? LoadItemOrFluid(LuaTable table, bool useTemperature) {
        if (table.Get("type", out string? type)) {
            if (table.Get("name", out string? name)) {
                if (type == "item") {
                    return GetObject<Item>(table);
                }
                else if (type == "fluid") {
                    if (useTemperature) {
                        if (table.Get("temperature", out int temperature)) {
                            return GetFluidFixedTemp(name, temperature);
                        }
                        else {
                            // If the temperature isn't specified, produce the default (minimum) temperature
                            return GetFluidFixedTemp(name, GetObject<Fluid>(table).temperatureRange.min);
                        }
                    }

                    return GetObject<Fluid>(table);
                }
            }
            else if (type == "research-progress" && table.Get("research_item", out string? researchItem)) {
                return GetObject<Item>(researchItem);
            }
        }
        else if (factorioVersion < v2_0) {
            // In 1.1, type is optional, and an item ingredient/product can be represented as { [1] = "name", [2] = amount }
            if (table.Get("name", out string? name)) {
                return GetObject<Item>(name);
            }

            if (table.Get(1, out name)) {
                // Copy the amount from [2] to ["amount"] to translate for later code.
                table["amount"] = table.Get<int>(2);
                return GetObject<Item>(name);
            }
        }
        return null;
    }

    private void DeserializeLocation(LuaTable table, ErrorCollector collector) {
        Location location = DeserializeCommon<Location>(table, "space-location");
        if (table.Get("map_gen_settings", out LuaTable? mapGen)) {
            if (mapGen.Get("autoplace_controls", out LuaTable? controls)) {
                location.placementControls = [.. controls.ObjectElements.Keys.OfType<string>()];
            }
            if (mapGen.Get<LuaTable>("autoplace_settings").Get<LuaTable>("entity").Get<LuaTable>("settings") is LuaTable entities) {
                location.entitySpawns = [.. entities.ObjectElements.Keys.OfType<string>()];
            }
            if (mapGen.Get<LuaTable>("autoplace_settings").Get<LuaTable>("tile").Get<LuaTable>("settings") is LuaTable tiles) {
                foreach (string tile in tiles.ObjectElements.Keys.Cast<string>()) {
                    allObjects.OfType<Tile>().Single(t => t.name == tile).locations.Add(location);
                }
            }
        }

        if (table.Get("asteroid_spawn_definitions", out LuaTable? spawns)) {
            foreach (LuaTable spawn in spawns.ArrayElements.OfType<LuaTable>()) {
                if (spawn.Get("asteroid", out string? asteroid)) {
                    location.entitySpawns.Add(asteroid);
                }
            }
        }
    }

    private T DeserializeCommon<T>(LuaTable table, string prototypeType) where T : FactorioObject, new() {
        if (!table.Get("name", out string? name)) {
            throw new NotSupportedException($"Read a definition of a {prototypeType} that does not have a name.");
        }

        var target = GetObject<T>(table);
        target.factorioType = table.Get("type", "");

        if (table.Get("localised_name", out object? loc)) {  // Keep UK spelling for Factorio/LUA data objects
            target.locName = LocalisedStringParser.ParseObject(loc)!;
        }
        else {
            target.locName = LocalisedStringParser.ParseKey(prototypeType + "-name." + target.name, [])!;
        }

        if (table.Get("localised_description", out loc)) {  // Keep UK spelling for Factorio/LUA data objects
            target.locDescr = LocalisedStringParser.ParseObject(loc);
        }
        else {
            target.locDescr = LocalisedStringParser.ParseKey(prototypeType + "-description." + target.name, []);
        }

        if (table.Get("icon", out string? s)) {
            target.iconSpec = [new FactorioIconPart(s) { size = table.Get("icon_size", 64) }];
        }
        else if (table.Get("icons", out LuaTable? iconList)) {
            target.iconSpec = [.. iconList.ArrayElements<LuaTable>().Select(x => {
                if (!x.Get("icon", out string? path)) {
                    throw new NotSupportedException($"One of the icon layers for {name} does not have a path.");
                }

                int expectedSize = target switch {
                    // These are the only expected sizes for icons we render.
                    // New classes of icons (e.g. achievements) will need new cases to make IconParts with default scale render correctly.
                    // https://lua-api.factorio.com/latest/types/IconData.html
                    Technology => 256, // I think this should be 512 in 1.1, but that doesn't fix vanilla icons, and makes SE icons worse.
                    _ => 64
                };

                FactorioIconPart part = new(path) { size = x.Get("icon_size", 64) };
                part.scale = x.Get("scale", expectedSize / 2f / part.size);
                if (factorioVersion < v2_0) {
                    // Mystery adjustment that makes 1.1 technology icons render correctly, and doesn't appear to mess up the recipe icons.
                    // (Checked many of vanilla/SE's tech icons and the se-simulation-* recipes.)
                    part.scale *= part.size / 64f;
                }

                if (x.Get("shift", out LuaTable? shift)) {
                    part.x = shift.Get<float>(1);
                    part.y = shift.Get<float>(2);
                }

                if (x.Get("tint", out LuaTable? tint)) {
                    part.r = tint.Get("r", 1f);
                    part.g = tint.Get("g", 1f);
                    part.b = tint.Get("b", 1f);
                    part.a = tint.Get("a", 1f);
                }

                return part;
            })];
        }

        return target;
    }
}
