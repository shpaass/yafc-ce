using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using Yafc.I18n;
using Yafc.UI;
[assembly: InternalsVisibleTo("Yafc.Parser")]

namespace Yafc.Model;

public interface IFactorioObjectWrapper {
    string text { get; }
    FactorioObject target { get; }
    float amount { get; }
}

internal enum FactorioObjectSortOrder {
    SpecialGoods,
    Items,
    Fluids,
    Recipes,
    Mechanics,
    Technologies,
    Entities,
    Tiles,
    Qualities,
    Locations,
}

public enum FactorioId { }

public abstract class FactorioObject : IFactorioObjectWrapper, IComparable<FactorioObject> {
    public string? factorioType { get; internal set; }
    public string name { get; internal set; } = null!; // null-forgiving: Initialized to non-null by GetObject.
    public string typeDotName => type + '.' + name;
    public string locName { get; internal set; } = null!; // null-forgiving: Copied from name if still null at the end of CalculateMaps
    public string? locDescr { get; internal set; }
    internal FactorioIconPart[]? iconSpec { get; set; }
    public Icon icon { get; internal set; }
    public FactorioId id { get; internal set; }
    internal abstract FactorioObjectSortOrder sortingOrder { get; }
    public FactorioObjectSpecialType specialType { get; internal set; }
    public abstract string type { get; }
    FactorioObject IFactorioObjectWrapper.target => this;
    float IFactorioObjectWrapper.amount => 1f;

    string IFactorioObjectWrapper.text => locName;

    public void FallbackLocalization(FactorioObject? other, LocalizableString1 description) {
        if (locName == null) {
            if (other == null) {
                locName = name;
            }
            else {
                locName = other.locName;
                // locName could still be null at this point. Use name instead, since we have nothing better.
                locDescr = description.L(locName ?? name);
            }
        }

        if (iconSpec == null && other?.iconSpec != null) {
            iconSpec = other.iconSpec;
        }
    }

    public abstract DependencyNode GetDependencies();

    public override string ToString() => name;

    public int CompareTo(FactorioObject? other) => DataUtils.DefaultOrdering.Compare(this, other);

    public bool showInExplorers { get; internal set; } = true;
}

public class FactorioIconPart(string path) {
    public string path = path;
    public int size = 32;
    public float x, y, r = 1, g = 1, b = 1, a = 1;
    public float scale = 1;

    public bool IsSimple() => x == 0 && y == 0 && r == 1 && g == 1 && b == 1 && a == 1 && scale == 1;
}

[Flags]
public enum RecipeFlags {
    UsesMiningProductivity = 1 << 0,
    UsesFluidTemperature = 1 << 2,
    ScaleProductionWithPower = 1 << 3,
    /// <summary>Set when the technology has a research trigger to craft an item</summary>
    HasResearchTriggerCraft = 1 << 4,
    /// <summary>Set when the technology has a research trigger to capture a spawner</summary>
    HasResearchTriggerCaptureEntity = 1 << 5,
    /// <summary>Set when the technology has a research trigger to mine an entity (including a resource)</summary>
    HasResearchTriggerMineEntity = 1 << 6,
    /// <summary>Set when the technology has a research trigger to build an entity</summary>
    HasResearchTriggerBuildEntity = 1 << 7,
    /// <summary>Set when the technology has a research trigger to launch a space platform starter pack</summary>
    HasResearchTriggerCreateSpacePlatform = 1 << 8,
    /// <summary>Set when the technology has a research trigger to launch an arbitrary item.</summary>
    HasResearchTriggerSendToOrbit = 1 << 9,

    HasResearchTriggerMask = HasResearchTriggerCraft | HasResearchTriggerCaptureEntity | HasResearchTriggerMineEntity | HasResearchTriggerBuildEntity
        | HasResearchTriggerCreateSpacePlatform | HasResearchTriggerSendToOrbit,
}

public abstract class RecipeOrTechnology : FactorioObject {
    public EntityCrafter[] crafters { get; internal set; } = null!; // null-forgiving: Initialized by CalculateMaps
    public Ingredient[] ingredients { get; internal set; } = null!; // null-forgiving: Initialized by LoadRecipeData, LoadTechnologyData, and after all calls to CreateSpecialRecipe
    public Product[] products { get; internal set; } = null!; // null-forgiving: Initialized by LoadRecipeData, LoadTechnologyData, and after all calls to CreateSpecialRecipe
    internal Entity? sourceEntity { get; set; }
    internal HashSet<Tile> sourceTiles { get; } = [];
    public Goods? mainProduct { get; internal set; }
    public float time { get; internal set; }
    public bool enabled { get; internal set; }
    public RecipeFlags flags { get; internal set; }
    public override string type => "Recipe";

    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Recipes;

    public sealed override DependencyNode GetDependencies() => DependencyNode.RequireAll(GetDependenciesHelper());

    protected virtual List<DependencyNode> GetDependenciesHelper() {
        List<DependencyNode> collector = [];
        if (ingredients.Length > 0) {
            List<FactorioObject> ingredients = [];
            foreach (Ingredient ingredient in this.ingredients) {
                if (ingredient.variants != null) {
                    collector.Add((ingredient.variants, DependencyNode.Flags.IngredientVariant));
                }
                else {
                    ingredients.Add(ingredient.goods);
                }
            }
            if (ingredients.Count > 0) {
                collector.Add((ingredients, DependencyNode.Flags.Ingredient));
            }
        }
        collector.Add((crafters, DependencyNode.Flags.CraftingEntity));
        if (sourceEntity != null) {
            collector.Add(([sourceEntity], DependencyNode.Flags.SourceEntity));
        }
        if (sourceTiles.Count > 0) {
            collector.Add((sourceTiles.SelectMany(t => t.locations).Distinct(), DependencyNode.Flags.Location));
        }

        return collector;
    }

    public bool CanFit(int itemInputs, int fluidInputs, Goods[]? slots) {
        foreach (var ingredient in ingredients) {
            if (ingredient.goods is Item && --itemInputs < 0) {
                return false;
            }

            if (ingredient.goods is Fluid && --fluidInputs < 0) {
                return false;
            }

            if (slots != null && !slots.Contains(ingredient.goods)) {
                return false;
            }
        }
        return true;
    }

    public virtual bool CanAcceptModule(Module _) => true;
}

public enum FactorioObjectSpecialType {
    Normal,
    Voiding,
    Barreling,
    Stacking,
    Pressurization,
    Crating,
    Recycling,
}

public class Recipe : RecipeOrTechnology {
    public AllowedEffects allowedEffects { get; internal set; }
    public string[]? allowedModuleCategories { get; internal set; }
    public Technology[] technologyUnlock { get; internal set; } = [];
    public Dictionary<Technology, float> technologyProductivity { get; internal set; } = [];
    public bool preserveProducts { get; internal set; }
    public bool hidden { get; internal set; }
    public float? maximumProductivity { get; internal set; }

    public bool HasIngredientVariants() {
        foreach (var ingredient in ingredients) {
            if (ingredient.variants != null) {
                return true;
            }
        }

        return false;
    }

    protected override List<DependencyNode> GetDependenciesHelper() {
        List<DependencyNode> nodes = base.GetDependenciesHelper();

        if (!enabled) {
            nodes.Add((technologyUnlock, DependencyNode.Flags.TechnologyUnlock));
        }
        return nodes;
    }

    public override bool CanAcceptModule(Module module) => EntityWithModules.CanAcceptModule(module.moduleSpecification, allowedEffects, allowedModuleCategories);
}

public class Mechanics : Recipe {
    internal FactorioObject source { get; set; } = null!; // null-forgiving: Set by CreateSpecialRecipe
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Mechanics;
    internal LocalizableString localizationKey = null!; // null-forgiving: Set by CreateSpecialRecipe
    public override string type => "Mechanics";
}

public class Ingredient : IFactorioObjectWrapper {
    public readonly float amount;
    public Goods goods { get; internal set; }
    public Goods[]? variants { get; internal set; }
    public TemperatureRange temperature { get; internal set; } = TemperatureRange.Any;
    public Ingredient(Goods goods, float amount) {
        this.goods = goods;
        this.amount = amount;
        if (goods is Fluid fluid) {
            temperature = fluid.temperatureRange;
        }
    }

    string IFactorioObjectWrapper.text {
        get {
            string text = goods.locName;
            if (amount != 1f) {
                text = LSs.IngredientAmount.L(amount, goods.locName);
            }

            if (!temperature.IsAny()) {
                text = LSs.IngredientAmountWithTemperature.L(amount, goods.locName, temperature);
            }

            return text;
        }
    }

    FactorioObject IFactorioObjectWrapper.target => goods;

    float IFactorioObjectWrapper.amount => amount;

    public bool ContainsVariant(Goods product) {
        if (goods == product) {
            return true;
        }

        if (variants != null) {
            return Array.IndexOf(variants, product) >= 0;
        }

        return false;
    }
}

public class Product : IFactorioObjectWrapper {
    public readonly Goods goods;
    internal readonly float amountMin;
    internal readonly float amountMax;
    internal readonly float probability;
    public readonly float amount; // This is average amount including probability and range
    /// <summary>
    /// Gets or sets the fixed freshness of this product: 0 if the recipe has result_is_always_fresh, or percent_spoiled from the
    /// ItemProductPrototype, or <see langword="null"/> if neither of those values are set.
    /// </summary>
    public float? percentSpoiled { get; internal set; }
    internal float productivityAmount { get; private set; }

    public void SetCatalyst(float catalyst) {
        float catalyticMin = amountMin - catalyst;
        float catalyticMax = amountMax - catalyst;

        if (catalyticMax <= 0) {
            productivityAmount = 0f;
        }
        else if (catalyticMin >= 0f) {
            productivityAmount = (catalyticMin + catalyticMax) * 0.5f * probability;
        }
        else {
            // TODO super duper rare case, might not be precise
            productivityAmount = probability * catalyticMax * catalyticMax * 0.5f / (catalyticMax - catalyticMin);
        }
    }

    internal float GetAmountForRow(RecipeRow row) => GetAmountPerRecipe(row.parameters.productivity) * (float)row.recipesPerSecond;
    internal float GetAmountPerRecipe(float productivityBonus) => amount + (productivityBonus * productivityAmount);

    public Product(Goods goods, float amount) {
        this.goods = goods;
        amountMin = amountMax = this.amount = productivityAmount = amount;
        probability = 1f;
    }

    public Product(Goods goods, float min, float max, float probability) {
        this.goods = goods;
        amountMin = min;
        amountMax = max;
        this.probability = probability;
        amount = productivityAmount = probability * (min + max) / 2;
    }

    /// <summary>
    /// Gets <see langword="true"/> if this product is one <see cref="Item"/> with 100% probability and default spoilage behavior.
    /// </summary>
    public bool IsSimple => amountMin == amountMax && amount == 1 && probability == 1 && percentSpoiled == null && goods is Item;

    FactorioObject IFactorioObjectWrapper.target => goods;

    string IFactorioObjectWrapper.text {
        get {
            string text;

            if (probability == 1) {
                if (amountMin == amountMax) {
                    text = LSs.ProductAmount.L(DataUtils.FormatAmount(amountMax, UnitOfMeasure.None), goods.locName);
                }
                else {
                    text = LSs.ProductAmountRange.L(DataUtils.FormatAmount(amountMin, UnitOfMeasure.None), DataUtils.FormatAmount(amountMax, UnitOfMeasure.None), goods.locName);
                }
            }
            else {
                if (amountMin == amountMax) {
                    if (amountMin == 1) {
                        text = LSs.ProductProbability.L(DataUtils.FormatAmount(probability, UnitOfMeasure.Percent), goods.locName);
                    }
                    else {
                        text = LSs.ProductProbabilityAmount.L(DataUtils.FormatAmount(probability, UnitOfMeasure.Percent), DataUtils.FormatAmount(amountMax, UnitOfMeasure.None), goods.locName);
                    }
                }
                else {
                    text = LSs.ProductProbabilityAmountRange.L(DataUtils.FormatAmount(probability, UnitOfMeasure.Percent), DataUtils.FormatAmount(amountMin, UnitOfMeasure.None), DataUtils.FormatAmount(amountMax, UnitOfMeasure.None), goods.locName);
                }
            }

            if (percentSpoiled == 0) {
                text = LSs.ProductAlwaysFresh.L(text);
            }
            else if (percentSpoiled != null) {
                text = LSs.ProductFixedSpoilage.L(text, DataUtils.FormatAmount(percentSpoiled.Value, UnitOfMeasure.Percent));
            }

            return text;
        }
    }
    float IFactorioObjectWrapper.amount => amount;

    public static Product operator *(Product product, int factor) =>
        new(product.goods, product.amountMin * factor, product.amountMax * factor, product.probability);
}

// Abstract base for anything that can be produced or consumed by recipes (etc)
public abstract class Goods : FactorioObject {
    public float fuelValue { get; internal set; }
    public abstract bool isPower { get; }
    public Fluid? fluid => this as Fluid;
    public Recipe[] production { get; internal set; } = [];
    public Recipe[] usages { get; internal set; } = [];
    public FactorioObject[] miscSources { get; internal set; } = [];
    public Entity[] fuelFor { get; internal set; } = [];
    public abstract UnitOfMeasure flowUnitOfMeasure { get; }
    public bool isLinkable { get; internal set; } = true;

    public override DependencyNode GetDependencies() => (production.Concat(miscSources), DependencyNode.Flags.Source);

    public virtual bool HasSpentFuel([MaybeNullWhen(false)] out Item spent) {
        spent = null;
        return false;
    }
}

public class Item : Goods {
    public Item() {
        getSpoilResult = new(() => getSpoilRecipe()?.products[0].goods);
        getBaseSpoilTime = new(() => getSpoilRecipe()?.time ?? 0);
        Recipe? getSpoilRecipe() => Database.recipes.all.OfType<Mechanics>().SingleOrDefault(r => r.name == "spoil." + name);
    }

    /// <summary>
    /// The prototypes in this array will be loaded in order, before any other prototypes.
    /// This should correspond to the prototypes for subclasses of item, with more derived classes listed before their base classes.
    /// e.g. If ItemWithHealth and ModuleWithHealth C# classes were added, all prototypes for both classes must be listed here.
    /// Ignoring style restrictions, the prototypes could be listed in any order, provided all ModuleWithHealth prototype(s) are listed before "module".
    /// </summary>
    /// <remarks>This forces modules to be loaded before other items, since deserialization otherwise creates Item objects for all spoil results.
    /// It does not protect against modules that spoil into other modules, but one hopes people won't do that.</remarks>
    internal static string[] ExplicitPrototypeLoadOrder { get; } = ["ammo", "module"];

    public Item? fuelResult { get; internal set; }
    public int stackSize { get; internal set; }
    public Entity? placeResult { get; internal set; }
    public Entity? plantResult { get; internal set; }
    public int rocketCapacity { get; internal set; }
    public override bool isPower => false;
    public override string type => "Item";
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Items;
    public override UnitOfMeasure flowUnitOfMeasure => UnitOfMeasure.ItemPerSecond;
    /// <summary>
    /// Gets the result when this item spoils, or <see langword="null"/> if this item doesn't spoil.
    /// </summary>
    public FactorioObject? spoilResult => getSpoilResult.Value;
    /// <summary>
    /// Gets the time it takes for a base-quality item to spoil, in seconds, or 0 if this item doesn't spoil.
    /// </summary>
    public float baseSpoilTime => getBaseSpoilTime.Value;

    public int weight { get; internal set; }
    internal float ingredient_to_weight_coefficient = 0.5f;

    /// <summary>
    /// Gets the <see cref="Quality"/>-adjusted spoilage time for this item, in seconds, or 0 if this item doesn't spoil.
    /// </summary>
    public float GetSpoilTime(Quality quality) => quality.ApplyStandardBonus(baseSpoilTime);

    /// <summary>
    /// The lazy store for getting the spoilage result. By default it searches for and reads a $"Mechanics.spoil.{name}" recipe,
    /// but it can be overridden for items that spoil into Entities.
    /// </summary>
    internal Lazy<FactorioObject?> getSpoilResult;
    /// <summary>
    /// The lazy store for getting the normal-quality spoilage time. By default it searches for and reads a $"Mechanics.spoil.{name}" recipe,
    /// but it can be overridden for items that spoil into Entities.
    /// </summary>
    internal Lazy<float> getBaseSpoilTime;

    public override bool HasSpentFuel([NotNullWhen(true)] out Item? spent) {
        spent = fuelResult;
        return spent != null;
    }

    public override DependencyNode GetDependencies() {
        if (this == Database.science.target) {
            return (Database.technologies.all, DependencyNode.Flags.Source);
        }
        return base.GetDependencies();
    }
}

public class Module : Item {
    public ModuleSpecification moduleSpecification { get; internal set; } = null!; // null-forgiving: Initialized by DeserializeItem.
}

internal class Ammo : Item {
    internal HashSet<string> projectileNames { get; } = [];
    internal HashSet<string>? targetFilter { get; set; }
}

public class Fluid : Goods {
    public override string type => "Fluid";
    public string originalName { get; internal set; } = null!; // name without temperature, null-forgiving: Initialized by DeserializeFluid.
    public float heatCapacity { get; internal set; } = 1e-3f;
    public TemperatureRange temperatureRange { get; internal set; }
    public int temperature { get; internal set; }
    public float heatValue { get; internal set; }
    // This is unlikely to be what you want. Use Ingredient.variants or RecipeRowIngredient.Variants instead; that will be filtered to only the
    // variants (temperatures) that are acceptable for the current recipe.
    internal List<Fluid>? variants { get; set; }
    public override bool isPower => false;
    public override UnitOfMeasure flowUnitOfMeasure => UnitOfMeasure.FluidPerSecond;
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Fluids;
    internal Fluid Clone() => (Fluid)MemberwiseClone();

    internal void SetTemperature(int temp) {
        temperature = temp;
        heatValue = (temp - temperatureRange.min) * heatCapacity;
    }
}

public class Location : FactorioObject {
    public override string type => "Location";

    public Technology[] technologyUnlock { get; internal set; } = [];
    internal List<string> entitySpawns { get; set; } = [];
    internal IReadOnlyList<string>? placementControls { get; set; }

    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Locations;

    public override DependencyNode GetDependencies() => (technologyUnlock, DependencyNode.Flags.TechnologyUnlock);
}

public class Special : Goods {
    internal string? virtualSignal { get; set; }
    internal bool power;
    internal bool isVoid;
    public override bool isPower => power;
    public override string type => isPower ? "Power" : "Special";
    public override UnitOfMeasure flowUnitOfMeasure => isVoid ? UnitOfMeasure.None : isPower ? UnitOfMeasure.Megawatt : UnitOfMeasure.PerSecond;
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.SpecialGoods;
}

[Flags]
public enum AllowedEffects {
    Speed = 1 << 0,
    Productivity = 1 << 1,
    Consumption = 1 << 2,
    Pollution = 1 << 3,
    Quality = 1 << 4,

    All = Speed | Productivity | Consumption | Pollution | Quality,
    None = 0
}

public class Tile : FactorioObject {
    // Tiles participate in accessibility analysis, but only until the pumping recipes get around to reading the locations where their tiles
    // appear. They often don't have an icon, and don't participate in anything else Yafc cares about, so hide them.
    public Tile() => showInExplorers = false;
    public Fluid? Fluid { get; internal set; }

    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Tiles;
    public override string type => "Tile";

    internal HashSet<Location> locations { get; } = [];

    public override DependencyNode GetDependencies() => (locations, DependencyNode.Flags.Location);
}

public class Entity : FactorioObject {
    public Product[] loot { get; internal set; } = [];
    public bool mapGenerated { get; internal set; }
    public float mapGenDensity { get; internal set; }
    public float basePower { get; internal set; }
    public float Power(Quality quality)
        => factorioType is "boiler" or "reactor" or "generator" or "burner-generator" or "accumulator" ? quality.ApplyStandardBonus(basePower)
        : factorioType is "beacon" ? basePower * quality.BeaconConsumptionFactor
        : basePower;
    public EntityEnergy energy { get; internal set; } = null!; // TODO: Prove that this is always properly initialized. (Do we need an EntityWithEnergy type?)
    public Item[] itemsToPlace { get; internal set; } = null!; // null-forgiving: This is initialized in CalculateMaps.
    internal Location[] spawnLocations { get; set; } = null!; // null-forgiving: This is initialized in CalculateMaps.
    internal List<Ammo> captureAmmo { get; } = [];
    internal List<Entity> sourceEntities { get; set; } = null!;
    internal string? autoplaceControl { get; set; }
    public float heatingPower { get; internal set; }
    public int width { get; internal set; }
    public int height { get; internal set; }
    public int size { get; internal set; }
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Entities;
    public override string type => "Entity";
    /// <summary>
    /// Gets the result when this entity spoils (possibly <see langword="null"/>, if the entity burns out with no replacement),
    /// or <see langword="null"/> if this entity doesn't spoil.
    /// </summary>
    public Entity? spoilResult => getSpoilResult?.Value;
    /// <summary>
    /// Gets the time it takes for a base-quality entity to spoil, in seconds, or 0 if this entity doesn't spoil.
    /// </summary>
    public float baseSpoilTime { get; internal set; }
    /// <summary>
    /// Gets the <see cref="Quality"/>-adjusted spoilage time for this entity, in seconds, or 0 if this entity doesn't spoil.
    /// </summary>
    public float GetSpoilTime(Quality quality) => quality.ApplyStandardBonus(baseSpoilTime);
    /// <summary>
    /// The lazy store for getting the spoilage result, if this entity spoils.
    /// </summary>
    internal Lazy<Entity?>? getSpoilResult;

    public sealed override DependencyNode GetDependencies() {
        // All entities require at least one source. Some also require fuel.
        // Implemented sources are:
        // - map gen location (e.g. spawners, ores, asteroids)
        // - itemsToPlace (TODO: requires valid location, possibly not the same as map gen location)
        // - asteroid death
        // - entity capture (e.g. captured spawners)
        // Unimplemented sources include:
        // - entity spawn (e.g. biters from spawners)
        // - item spoilage (e.g. egg spoilage)
        // - most projectile effects (e.g. strafer pentapod projectiles)
        // - entity death (e.g. spawner reversion, explosions, corpses)

        List<DependencyNode> sources = [];

        if (mapGenerated) {
            sources.Add((spawnLocations, DependencyNode.Flags.Location));
        }
        if (itemsToPlace.Length > 0) {
            sources.Add((itemsToPlace, DependencyNode.Flags.Source));
        }
        if (sourceEntities.Count > 0) { // Asteroid death
            sources.Add((sourceEntities, DependencyNode.Flags.Source));
        }

        if (captureAmmo.Count > 0) {
            // Capture sources require spawners and capture-ammo

            // Find the (ammo, [.. spawner]) pairs that can create this, grouped by ammo.
            List<(Ammo ammo, List<EntitySpawner> spawners)> sourceSpawners = [];
            foreach (Ammo ammo in captureAmmo) {
                List<EntitySpawner> spawners;
                if (ammo.targetFilter == null) {
                    spawners = [.. Database.objects.all.OfType<EntitySpawner>().Where(s => s.capturedEntityName == name)];
                }
                else {
                    spawners = ammo.targetFilter.Select(t => Database.objectsByTypeName["Entity." + t] as EntitySpawner)
                        .Where(s => s?.capturedEntityName == name).ToList()!;
                }
                sourceSpawners.Add((ammo, spawners));
            }

            // group the ammo by spawner list, to make ([.. ammo], [.. spawner]) pairs.
            var groups = sourceSpawners.GroupBy(s => s.spawners, new ListComparer()).Select(g => (g.Select(l => l.ammo), g.Key));

            foreach ((IEnumerable<Ammo> ammo, List<EntitySpawner> spawners) in groups) {
                sources.Add(DependencyNode.RequireAll((ammo, DependencyNode.Flags.Source), (spawners, DependencyNode.Flags.Source)));
            }
        }

        // If there are no sources, blame it on not having any items that can place the entity.
        // (Map-generated entities with no locations got a zero-element list in the `if (mapGenerated)` test.)
        if (sources.Count == 0) {
            sources.Add(([], DependencyNode.Flags.ItemToPlace));
        }

        if (energy != null) {
            return DependencyNode.RequireAll((energy.fuels, DependencyNode.Flags.Fuel), DependencyNode.RequireAny(sources));
        }
        else { // Doesn't require fuel
            return DependencyNode.RequireAny(sources);
        }
    }

    private sealed class ListComparer : IEqualityComparer<List<EntitySpawner>> {
        public bool Equals(List<EntitySpawner>? x, List<EntitySpawner>? y) {
            if (x == null && y == null) {
                return true;
            }
            if (x == null || y == null) {
                return false;
            }
            return x.SequenceEqual(y);
        }

        public int GetHashCode([DisallowNull] List<EntitySpawner> obj) {
            HashCode code = new();
            foreach (EntitySpawner item in obj) {
                code.Add(item);
            }
            return code.ToHashCode();
        }
    }
}

public abstract class EntityWithModules : Entity {
    public AllowedEffects allowedEffects { get; internal set; } = AllowedEffects.None;
    public string[]? allowedModuleCategories { get; internal set; }
    public int moduleSlots { get; internal set; }

    public static bool CanAcceptModule(ModuleSpecification module, AllowedEffects effects, string[]? allowedModuleCategories) {
        if (module.baseProductivity > 0f && (effects & AllowedEffects.Productivity) == 0) {
            return false;
        }

        if (module.baseConsumption < 0f && (effects & AllowedEffects.Consumption) == 0) {
            return false;
        }

        if (module.basePollution < 0f && (effects & AllowedEffects.Pollution) == 0) {
            return false;
        }

        if (module.baseSpeed > 0f && (effects & AllowedEffects.Speed) == 0) {
            return false;
        }

        if (module.baseQuality > 0f && (effects & AllowedEffects.Quality) == 0) {
            return false;
        }

        if (allowedModuleCategories?.Contains(module.category) == false) {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Gets the index of this entity's module inventory, as if from defines.inventory.{something}_modules
    /// </summary>
    public abstract int BlueprintModuleInventory { get; }

    public bool CanAcceptModule<T>(IObjectWithQuality<T> module) where T : Module => CanAcceptModule(module.target.moduleSpecification);
    public bool CanAcceptModule(ModuleSpecification module) => CanAcceptModule(module, allowedEffects, allowedModuleCategories);
}

public class EntityCrafter : EntityWithModules {
    public int itemInputs { get; internal set; }
    public int fluidInputs { get; internal set; } // fluid inputs for recipe, not including power
    public bool hasVectorToPlaceResult { get; internal set; }
    public Goods[]? inputs { get; internal set; }
    public RecipeOrTechnology[] recipes { get; internal set; } = null!; // null-forgiving: Set in the first step of CalculateMaps
    private float _craftingSpeed = 1;
    // For rocket silos, the number of inventory slots. Launch recipes are limited by both weight and available slots.
    internal int rocketInventorySize;

    public virtual float baseCraftingSpeed {
        // The speed of a lab is baseSpeed * (1 + researchSpeedBonus) * Math.Min(0.2, 1 + moduleAndBeaconSpeedBonus)
        get => _craftingSpeed * (1 + (factorioType == "lab" ? Project.current.settings.researchSpeedBonus : 0));
        internal set => _craftingSpeed = value;
    }
    public virtual float CraftingSpeed(Quality quality) => factorioType is "agricultural-tower" or "electric-energy-interface" ? baseCraftingSpeed : quality.ApplyStandardBonus(baseCraftingSpeed);
    public EffectReceiver effectReceiver { get; internal set; } = null!;

    public override int BlueprintModuleInventory => factorioType switch {
        "mining-drill" => 2, /* defines.inventory.mining_drill_modules */
        "lab" => 3, /* defines.inventory.lab_modules */
        _ => 4 /* defines.inventory.crafter_modules */
    };
}

public class EntityAttractor : EntityCrafter {
    // TODO(multi-planet): Read PlanetPrototype::lightning_properties.search_radius
    private const int LightningSearchRange = 10;
    // TODO(multi-planet): Read PlanetPrototype::lightning_properties.lightning_types and
    // LightningPrototype::lightnings_per_chunk_per_tick * 60 * LightningPrototype::energy / 1024 * 0.3
    private const float MwPerTile = 0.0293f;
    public override float baseCraftingSpeed {
        get => CraftingSpeed(Quality.Normal);
        internal set => throw new NotSupportedException("To set lightning attractor crafting speed, set the range and efficiency fields.");
    }
    public float drain { get; internal set; }

    internal float range;
    internal float efficiency;
    /// <summary>
    /// Gets the size of the (square) grid for this lightning attractor. The grid is sized to be as large as possible while protecting the
    /// entire areas within the grid: the collection areas of diagonally-adjacent attractors should overlap as little as possible.
    /// </summary>
    public int ConstructionGrid(Quality quality) => (int)MathF.Floor((Range(quality) + LightningSearchRange) * MathF.Sqrt(2));
    public float Range(Quality quality) => quality.ApplyStandardBonus(range);
    public float Efficiency(Quality quality) => quality.ApplyStandardBonus(efficiency);

    public override float CraftingSpeed(Quality quality) {
        // Maximum distance between attractors for full protection, in a square grid:
        float distance = ConstructionGrid(quality);
        float efficiency = Efficiency(quality);
        // Production is coverage area times efficiency times lightning power density
        // Peak coverage area is (π*range²), but (distance²) allows full protection with a simple square grid.
        float area = distance * distance;
        // Assume 90% of the captured energy is lost to the attractor's internal drain.
        return area * efficiency * MwPerTile / 10;
    }

    public float StormPotentialPerTick(Quality quality) {
        // Maximum distance between attractors for full protection, in a square grid:
        float distance = ConstructionGrid(quality);
        // Storm potential is coverage area times lightning power density / .3
        // Peak coverage area is (π*range²), but (distance²) allows full protection with a simple square grid.
        float area = distance * distance;
        return area * MwPerTile / 60 / 0.3f;
    }
}

internal class EntityProjectile : Entity {
    internal HashSet<string> placeEntities { get; } = [];
}

internal class EntitySpawner : Entity {
    internal string? capturedEntityName { get; set; }
}

public sealed class Quality : FactorioObject {
    public static Quality Normal { get; internal set; } = null!;
    /// <summary>
    /// Gets the highest quality that is accessible at the current milestones.
    /// </summary>
    public static Quality MaxAccessible {
        get {
            Quality quality = Normal;
            while (quality.nextQuality?.IsAccessibleWithCurrentMilestones() ?? false) {
                quality = quality.nextQuality;
            }
            return quality;
        }
    }

    public Quality? nextQuality { get; internal set; }
    public int level { get; internal set; }

    public override string type => "Quality";
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Qualities;

    internal List<Technology> technologyUnlock { get; } = [];
    internal Quality? previousQuality { get; set; }

    public override DependencyNode GetDependencies() {
        List<DependencyNode> collector = [];
        collector.Add((technologyUnlock, DependencyNode.Flags.TechnologyUnlock));
        if (previousQuality != null) {
            collector.Add(([previousQuality], DependencyNode.Flags.Source));
        }
        if (level != 0) {
            collector.Add((Database.allModules.Where(m => m.moduleSpecification.baseQuality > 0), DependencyNode.Flags.Source));
        }
        return DependencyNode.RequireAll(collector);
    }

    // applies the "standard" +30% per level bonus
    internal float ApplyStandardBonus(float baseValue) => baseValue * (1 + .3f * level);
    // applies the +100% per level accumulator capacity bonus
    internal float ApplyAccumulatorCapacityBonus(float baseValue) => baseValue * (1 + level);
    // applies the standard +30% per level bonus, but with the exception that the result is floored to the nearest hundredth
    // Modules use this: a 25% normal bonus is a 32% uncommon bonus, not 32.5%.
    internal float ApplyModuleBonus(float baseValue) => MathF.Floor(ApplyStandardBonus(baseValue) * 100) / 100;
    // applies the .2 per level beacon transmission bonus
    internal float ApplyBeaconBonus(float baseValue) => baseValue + level * .2f;

    public float StandardBonus => .3f * level;
    public float AccumulatorCapacityBonus => level;
    public float BeaconTransmissionBonus => .2f * level;
    public float BeaconConsumptionFactor { get; internal set; }
    public float UpgradeChance { get; internal set; }

    public static bool operator <(Quality left, Quality right) => left.level < right.level;
    public static bool operator >(Quality left, Quality right) => left.level > right.level;
    public static bool operator <=(Quality left, Quality right) => left.level <= right.level;
    public static bool operator >=(Quality left, Quality right) => left.level >= right.level;
}

/// <summary>
/// Represents a <see cref="FactorioObject"/> with an attached <see cref="Quality"/> modifier.
/// </summary>
/// <typeparam name="T">The type of the quality-modified object.</typeparam>
/// <remarks>Like <see cref="FactorioObject"/>s and their derived types, any two <see cref="IObjectWithQuality{T}"/> values may be compared using
/// <c>==</c> and the default reference equality.</remarks>
public interface IObjectWithQuality<out T> : IFactorioObjectWrapper where T : FactorioObject {
    /// <summary>
    /// Gets the <typeparamref name="T"/> object managed by this instance.
    /// </summary>
    new T target { get; }
    /// <summary>
    /// Gets the <see cref="Quality"/> of the object managed by this instance.
    /// </summary>
    Quality quality { get; }
}

/// <summary>
/// Provides static methods for interacting with the canonical quality objects.
/// </summary>
/// <remarks>References to the old <c>ObjectWithQuality&lt;T></c> class should be changed to <see cref="IObjectWithQuality{T}"/>, and constructor
/// calls and conversions should be replaced with calls to <see cref="QualityExtensions.With{T}(T, Quality)"/>.</remarks>
public static class ObjectWithQuality {
    private static readonly Dictionary<(FactorioObject, Quality), object> _cache = [];
    // These items do not support quality
    private static readonly HashSet<string> nonQualityItemNames = ["science", "item-total-input", "item-total-output"];

    /// <summary>
    /// Gets the <see cref="IObjectWithQuality{T}"/> representing a <see cref="FactorioObject"/> with an attached <see cref="Quality"/> modifier.
    /// </summary>
    /// <typeparam name="T">The type of the quality-modified object.</typeparam>
    /// <param name="target">The object to be associated with a quality modifier.</param>
    /// <param name="quality">The quality for this object.</param>
    /// <remarks>For shorter expressions/lines, consider calling <see cref="QualityExtensions.With{T}(T, Quality)"/> instead.</remarks>
    [return: NotNullIfNotNull(nameof(target))]
    public static IObjectWithQuality<T>? Get<T>(T? target, Quality quality) where T : FactorioObject
        => target == null ? null : (IObjectWithQuality<T>)_cache[(target, quality ?? throw new ArgumentNullException(nameof(quality)))];

    /// <summary>
    /// Constructs all <see cref="ConcreteObjectWithQuality{T}"/>s for the current contents of data.raw. This should only be called by
    /// <c>FactorioDataDeserializer.LoadData</c>.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static void LoadCache(List<FactorioObject> allObjects) {
        _cache.Clear();
        // Quality.Normal must be processed first, so the reset-to-normal logic will work properly.
        foreach (var quality in allObjects.OfType<Quality>().Where(x => x != Quality.Normal).Prepend(Quality.Normal)) {
            foreach (var obj in allObjects) {
                MakeObject(obj, quality);
            }
        }
    }

    private static void MakeObject(FactorioObject target, Quality quality) {
        Quality realQuality = target switch {
            // Things that don't support quality:
            Fluid or Location or Mechanics { source: Entity } or Quality or Special or Technology or Tile => Quality.Normal,
            Recipe r when r.ingredients.All(i => i.goods is Fluid) => Quality.Normal,
            Item when nonQualityItemNames.Contains(target.name) => Quality.Normal,
            // Everything else supports quality
            _ => quality
        };

        object withQuality;
        if (realQuality == quality) {
            Type type = typeof(ConcreteObjectWithQuality<>).MakeGenericType(target.GetType());
            withQuality = Activator.CreateInstance(type, [target, realQuality])!;
        }
        else {
            // This got changed back to normal quality. Get the previously stored object instead of making a new one.
            withQuality = Get(target, realQuality);
        }

        _cache[(target, quality)] = withQuality;
    }

    /// <summary>
    /// Represents a <see cref="FactorioObject"/> with an attached <see cref="Quality"/> modifier.
    /// </summary>
    /// <typeparam name="T">The concrete type of the quality-modified object.</typeparam>
    private sealed class ConcreteObjectWithQuality<T>(T target, Quality quality) : IObjectWithQuality<T> where T : FactorioObject {
        /// <inheritdoc/>
        public T target { get; } = target ?? throw new ArgumentNullException(nameof(target));
        /// <inheritdoc/>
        public Quality quality { get; } = quality ?? throw new ArgumentNullException(nameof(quality));

        string IFactorioObjectWrapper.text => ((IFactorioObjectWrapper)target).text;
        FactorioObject IFactorioObjectWrapper.target => target;
        float IFactorioObjectWrapper.amount => ((IFactorioObjectWrapper)target).amount;
    }
}

public class Effect {
    public float consumption { get; internal set; }
    public float speed { get; internal set; }
    public float productivity { get; internal set; }
    public float pollution { get; internal set; }
    public float quality { get; internal set; }
}

public class EffectReceiver {
    public Effect baseEffect { get; internal set; } = null!;
    public bool usesModuleEffects { get; internal set; }
    public bool usesBeaconEffects { get; internal set; }
    public bool usesSurfaceEffects { get; internal set; }
}

public class EntityInserter : Entity {
    public bool isBulkInserter { get; internal set; }
    public float inserterSwingTime { get; internal set; }
}

public class EntityAccumulator : Entity {
    internal float baseAccumulatorCapacity { get; set; }
    internal float baseAccumulatorCurrent;
    public float AccumulatorCapacity(Quality quality) => quality.ApplyAccumulatorCapacityBonus(baseAccumulatorCapacity);
}

public class EntityBelt : Entity {
    public float beltItemsPerSecond { get; internal set; }
}

public class EntityReactor : EntityCrafter {
    public float reactorNeighborBonus { get; internal set; }
}

public class EntityBeacon : EntityWithModules {
    public float baseBeaconEfficiency { get; internal set; }
    public float BeaconEfficiency(Quality quality) => quality.ApplyBeaconBonus(baseBeaconEfficiency);
    public float[] profile { get; internal set; } = null!;

    public float GetProfile(int numberOfBeacons) {
        if (numberOfBeacons == 0) {
            return 1f;
        }

        return profile[Math.Min(numberOfBeacons, profile.Length) - 1];
    }

    public override int BlueprintModuleInventory => 1 /*defines.inventory.beacon_modules*/;
}

public class EntityContainer : Entity {
    public int inventorySize { get; internal set; }
    public string? logisticMode { get; set; }
    public int logisticSlotsCount { get; internal set; }
}

public class Technology : RecipeOrTechnology { // Technology is very similar to recipe
    public float count { get; internal set; } // TODO support formula count
    public Technology[] prerequisites { get; internal set; } = [];
    public List<Recipe> unlockRecipes { get; } = [];
    public List<Location> unlockLocations { get; } = [];
    public Dictionary<Recipe, float> changeRecipeProductivity { get; internal set; } = [];
    internal bool unlocksFluidMining { get; set; }
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Technologies;
    public override string type => "Technology";
    /// <summary>
    /// If the technology has a trigger that requires entities, they are stored here.
    /// </summary>
    /// <remarks>Lazy-loaded so the database can load and correctly type (eg EntityCrafter, EntitySpawner, etc.) the entities without having to do another pass.</remarks>
    public IReadOnlyList<Entity> triggerEntities => getTriggerEntities.Value;
    public Item? triggerItem { get; internal set; }

    /// <summary>
    /// Sets the value used to construct <see cref="triggerEntities"/>.
    /// </summary>
    internal Lazy<IReadOnlyList<Entity>> getTriggerEntities { get; set; } = new Lazy<IReadOnlyList<Entity>>(() => []);

    protected override List<DependencyNode> GetDependenciesHelper() {
        List<DependencyNode> nodes = base.GetDependenciesHelper();

        if (prerequisites.Length > 0) {
            nodes.Add((prerequisites, DependencyNode.Flags.TechnologyPrerequisites));
        }
        if (flags.HasFlag(RecipeFlags.HasResearchTriggerMineEntity)) {
            // If we have a mining mechanic, use that as the source; otherwise just use the entity.
            var sources = triggerEntities.Select(e => Database.mechanics.all.SingleOrDefault(m => m.source == e) ?? (FactorioObject)e);
            nodes.Add((sources, DependencyNode.Flags.Source));
        }
        if (flags.HasFlag(RecipeFlags.HasResearchTriggerBuildEntity)) {
            nodes.Add((triggerEntities, DependencyNode.Flags.Source));
        }
        if (flags.HasFlag(RecipeFlags.HasResearchTriggerCreateSpacePlatform)) {
            var items = Database.items.all.Where(i => i.factorioType == "space-platform-starter-pack");
            nodes.Add((items.Select(i => Database.objectsByTypeName["Mechanics.launch." + i.name]), DependencyNode.Flags.Source));
        }
        if (flags.HasFlag(RecipeFlags.HasResearchTriggerSendToOrbit)) {
            nodes.Add(([Database.objectsByTypeName["Mechanics.launch." + triggerItem]], DependencyNode.Flags.Source));
        }

        if (!enabled) {
            nodes.Add(([], DependencyNode.Flags.Disabled));
        }
        return nodes;
    }
}

public enum EntityEnergyType {
    Void,
    Electric,
    Heat,
    SolidFuel,
    FluidFuel,
    FluidHeat,
    Labor, // Special energy type for character
}

public class EntityEnergy {
    public EntityEnergyType type { get; internal set; }
    public TemperatureRange workingTemperature { get; internal set; }
    public TemperatureRange acceptedTemperature { get; internal set; } = TemperatureRange.Any;
    public IReadOnlyList<(string, float)> emissions { get; internal set; } = [];
    public float drain { get; internal set; }
    public float baseFuelConsumptionLimit { get; internal set; } = float.PositiveInfinity;
    public float FuelConsumptionLimit(Quality quality) => quality.ApplyStandardBonus(baseFuelConsumptionLimit);
    public Goods[] fuels { get; internal set; } = [];
    public float effectivity { get; internal set; } = 1f;
}

public class ModuleSpecification {
    public string category { get; internal set; } = null!;
    public float baseConsumption { get; internal set; }
    public float baseSpeed { get; internal set; }
    public float baseProductivity { get; internal set; }
    public float basePollution { get; internal set; }
    public float baseQuality { get; internal set; }
    public float Consumption(Quality quality) => baseConsumption >= 0 ? baseConsumption : quality.ApplyModuleBonus(baseConsumption);
    public float Speed(Quality quality) => baseSpeed <= 0 ? baseSpeed : quality.ApplyModuleBonus(baseSpeed);
    public float Productivity(Quality quality) => baseProductivity <= 0 ? baseProductivity : quality.ApplyModuleBonus(baseProductivity);
    public float Pollution(Quality quality) => basePollution >= 0 ? basePollution : quality.ApplyModuleBonus(basePollution);
    public float Quality(Quality quality) => baseQuality <= 0 ? baseQuality : quality.ApplyModuleBonus(baseQuality);
}

public struct TemperatureRange(int min, int max) {
    public int min = min;
    public int max = max;

    public static readonly TemperatureRange Any = new TemperatureRange(int.MinValue, int.MaxValue);
    public readonly bool IsAny() => min == int.MinValue && max == int.MaxValue;

    public readonly bool IsSingle() => min == max;

    public TemperatureRange(int single) : this(single, single) { }

    public override readonly string ToString() {
        if (min == max) {
            return LSs.Temperature.L(min);
        }

        return LSs.TemperatureRange.L(min, max);
    }

    public readonly bool Contains(int value) => min <= value && max >= value;
}
