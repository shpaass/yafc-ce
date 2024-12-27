using System;
using System.Collections.Generic;
using System.Linq;

namespace Yafc.Model;

[Flags]
public enum WarningFlags {
    // Non-errors
    AssumesNauvisSolarRatio = 1 << 0,
    ReactorsNeighborsFromPrefs = 1 << 1,
    FuelUsageInputLimited = 1 << 2,
    AsteroidCollectionNotModelled = 1 << 3,

    // Static errors
    EntityNotSpecified = 1 << 8,
    FuelNotSpecified = 1 << 9,
    FuelTemperatureExceedsMaximum = 1 << 10,
    FuelDoesNotProvideEnergy = 1 << 11,
    FuelWithTemperatureNotLinked = 1 << 12,

    // Solution errors
    DeadlockCandidate = 1 << 16,
    OverproductionRequired = 1 << 17,
    ExceedsBuiltCount = 1 << 18,

    // Not implemented warnings
    TemperatureForIngredientNotMatch = 1 << 24,
}

public struct UsedModule {
    public (ObjectWithQuality<Module> module, int count, bool beacon)[]? modules;
    public ObjectWithQuality<EntityBeacon>? beacon;
    public int beaconCount;
}

internal class RecipeParameters(float recipeTime, float fuelUsagePerSecondPerBuilding, WarningFlags warningFlags, ModuleEffects activeEffects, UsedModule modules) {
    public float recipeTime { get; } = recipeTime;
    public float fuelUsagePerSecondPerBuilding { get; } = fuelUsagePerSecondPerBuilding;
    public float productivity => activeEffects.productivity;
    public WarningFlags warningFlags { get; internal set; } = warningFlags;
    public ModuleEffects activeEffects { get; } = activeEffects;
    public UsedModule modules { get; } = modules;

    public static RecipeParameters Empty = new(0, 0, 0, default, default);

    public float fuelUsagePerSecondPerRecipe => recipeTime * fuelUsagePerSecondPerBuilding;

    public static RecipeParameters CalculateParameters(RecipeRow row) {
        WarningFlags warningFlags = 0;
        ObjectWithQuality<EntityCrafter>? entity = row.entity;
        RecipeOrTechnology recipe = row.recipe;
        Goods? fuel = row.fuel;
        float recipeTime, fuelUsagePerSecondPerBuilding = 0, productivity, speed, consumption;
        ModuleEffects activeEffects = default;
        UsedModule modules = default;

        if (entity == null) {
            warningFlags |= WarningFlags.EntityNotSpecified;
            recipeTime = recipe.time;
            productivity = 0f;
            speed = 0;
            consumption = 0f;
        }
        else {
            recipeTime = recipe.time / entity.GetCraftingSpeed();
            productivity = entity.target.effectReceiver.baseEffect.productivity;
            speed = entity.target.effectReceiver.baseEffect.speed;
            consumption = entity.target.effectReceiver.baseEffect.consumption;
            EntityEnergy energy = entity.target.energy;
            float energyUsage = entity.GetPower();
            float energyPerUnitOfFuel = 0f;

            // Special case for fuel
            if (energy != null && fuel != null) {
                var fluid = fuel.fluid;
                energyPerUnitOfFuel = fuel.fuelValue;

                if (energy.type == EntityEnergyType.FluidHeat) {
                    if (fluid == null) {
                        warningFlags |= WarningFlags.FuelWithTemperatureNotLinked;
                    }
                    else {
                        int temperature = fluid.temperature;

                        if (temperature > energy.workingTemperature.max) {
                            temperature = energy.workingTemperature.max;
                            warningFlags |= WarningFlags.FuelTemperatureExceedsMaximum;
                        }

                        float heatCap = fluid.heatCapacity;
                        energyPerUnitOfFuel = (temperature - energy.workingTemperature.min) * heatCap;
                    }
                }

                if (fluid != null && !energy.acceptedTemperature.Contains(fluid.temperature)) {
                    warningFlags |= WarningFlags.FuelDoesNotProvideEnergy;
                }

                if (energyPerUnitOfFuel > 0f) {
                    fuelUsagePerSecondPerBuilding = energyUsage <= 0f ? 0f : energyUsage / (energyPerUnitOfFuel * energy.effectivity);
                }
                else {
                    fuelUsagePerSecondPerBuilding = 0;
                    warningFlags |= WarningFlags.FuelDoesNotProvideEnergy;
                }
            }
            else {
                fuelUsagePerSecondPerBuilding = energyUsage;
                warningFlags |= WarningFlags.FuelNotSpecified;
                energy ??= new EntityEnergy { type = EntityEnergyType.Void };
            }

            // Special case for generators
            if (recipe.flags.HasFlags(RecipeFlags.ScaleProductionWithPower) && energyPerUnitOfFuel > 0 && entity.target.energy.type != EntityEnergyType.Void) {
                if (energyUsage == 0) {
                    fuelUsagePerSecondPerBuilding = energy.FuelConsumptionLimit(entity.quality);
                    recipeTime = 1f / (energy.FuelConsumptionLimit(entity.quality) * energyPerUnitOfFuel * energy.effectivity);
                }
                else {
                    recipeTime = 1f / energyUsage;
                }
            }

            bool isMining = recipe.flags.HasFlags(RecipeFlags.UsesMiningProductivity);
            activeEffects = new ModuleEffects();

            if (isMining) {
                productivity += Project.current.settings.miningProductivity;
            }
            else if (recipe is Technology) {
                productivity += Project.current.settings.researchProductivity;
            }
            else if (recipe is Recipe actualRecipe) {
                Dictionary<Technology, int> levels = Project.current.settings.productivityTechnologyLevels;
                foreach ((Technology productivityTechnology, float changePerLevel) in actualRecipe.technologyProductivity) {
                    if (!levels.TryGetValue(productivityTechnology, out int productivityTechLevel)) {
                        continue;
                    }

                    productivity += changePerLevel * productivityTechLevel;
                }
            }

            if (entity.target is EntityReactor reactor && reactor.reactorNeighborBonus > 0f) {
                productivity += reactor.reactorNeighborBonus * Project.current.settings.GetReactorBonusMultiplier();
                warningFlags |= WarningFlags.ReactorsNeighborsFromPrefs;
            }

            if (entity.target.factorioType == "solar-panel") {
                warningFlags |= WarningFlags.AssumesNauvisSolarRatio;
            }
            else if (entity.target.factorioType == "asteroid-collector") {
                warningFlags |= WarningFlags.AsteroidCollectionNotModelled;
            }

            modules = default;

            if (entity.target.allowedEffects != AllowedEffects.None && entity.target.allowedModuleCategories is not []) {
                row.GetModulesInfo((recipeTime, fuelUsagePerSecondPerBuilding), entity.target, ref activeEffects, ref modules);
            }

            activeEffects.productivity += productivity;
            activeEffects.speed += speed;
            activeEffects.consumption += consumption;

            recipeTime /= activeEffects.speedMod;
            fuelUsagePerSecondPerBuilding *= activeEffects.energyUsageMod;

            if (energy.drain > 0f) {
                fuelUsagePerSecondPerBuilding += energy.drain / energyPerUnitOfFuel;
            }

            if (fuelUsagePerSecondPerBuilding > energy.FuelConsumptionLimit(entity.quality)) {
                recipeTime *= fuelUsagePerSecondPerBuilding / energy.FuelConsumptionLimit(entity.quality);
                fuelUsagePerSecondPerBuilding = energy.FuelConsumptionLimit(entity.quality);
                warningFlags |= WarningFlags.FuelUsageInputLimited;
            }
        }

        return new RecipeParameters(recipeTime, fuelUsagePerSecondPerBuilding, warningFlags, activeEffects, modules);
    }
}
