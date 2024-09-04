﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Google.OrTools.LinearSolver;
using Serilog;
using Yafc.UI;

namespace Yafc.Model {
    public class CostAnalysis(bool onlyCurrentMilestones) : Analysis {
        private readonly ILogger logger = Logging.GetLogger<CostAnalysis>();

        public static readonly CostAnalysis Instance = new CostAnalysis(false);
        public static readonly CostAnalysis InstanceAtMilestones = new CostAnalysis(true);
        public static CostAnalysis Get(bool atCurrentMilestones) {
            return atCurrentMilestones ? InstanceAtMilestones : Instance;
        }

        private const float CostPerSecond = 0.1f;
        private const float CostPerMj = 0.1f;
        private const float CostPerIngredientPerSize = 0.1f;
        private const float CostPerProductPerSize = 0.2f;
        private const float CostPerItem = 0.02f;
        private const float CostPerFluid = 0.0005f;
        private const float CostPerPollution = 0.01f;
        private const float CostLowerLimit = -10f;
        private const float CostLimitWhenGeneratesOnMap = 1e4f;
        private const float MiningPenalty = 1f; // Penalty for any mining
        private const float MiningMaxDensityForPenalty = 2000; // Mining things with less density than this gets extra penalty
        private const float MiningMaxExtraPenaltyForRarity = 10f;

        public Mapping<FactorioObject, float> cost;
        public Mapping<Recipe, float> recipeCost;
        public Mapping<FactorioObject, float> flow;
        public Mapping<Recipe, float> recipeWastePercentage;
        public Goods[]? importantItems;
        private readonly bool onlyCurrentMilestones = onlyCurrentMilestones;
        private string? itemAmountPrefix;

        private bool ShouldInclude(FactorioObject obj) {
            // Consider all recipes
            if (obj is Recipe) {
                return true;
            }
            return onlyCurrentMilestones ? obj.IsAutomatableWithCurrentMilestones() : obj.IsAutomatable();
        }

        public override void Compute(Project project, ErrorCollector warnings) {
            var workspaceSolver = DataUtils.CreateSolver();
            var objective = workspaceSolver.Objective();
            objective.SetMaximization();
            Stopwatch time = Stopwatch.StartNew();

            var variables = Database.goods.CreateMapping<Variable>();
            var constraints = Database.recipes.CreateMapping<Constraint>();

            Dictionary<Goods, float> sciencePackUsage = [];
            if (!onlyCurrentMilestones && project.preferences.targetTechnology != null) {
                itemAmountPrefix = "Estimated amount for " + project.preferences.targetTechnology.locName + ": ";
                foreach (var spUsage in TechnologyScienceAnalysis.Instance.allSciencePacks[project.preferences.targetTechnology]) {
                    sciencePackUsage[spUsage.goods] = spUsage.amount;
                }
            }
            else {
                itemAmountPrefix = "Estimated amount for all researches: ";
                foreach (Technology technology in Database.technologies.all.ExceptExcluded(this)) {
                    if (technology.IsAccessible() && technology.ingredients is not null) {
                        foreach (var ingredient in technology.ingredients) {
                            if (ingredient.goods.IsAutomatable()) {
                                if (onlyCurrentMilestones && !Milestones.Instance.IsAccessibleAtNextMilestone(ingredient.goods)) {
                                    continue;
                                }

                                _ = sciencePackUsage.TryGetValue(ingredient.goods, out float prev);
                                sciencePackUsage[ingredient.goods] = prev + (ingredient.amount * technology.count);
                            }
                        }
                    }
                }
            }


            foreach (Goods goods in Database.goods.all.ExceptExcluded(this)) {
                if (!ShouldInclude(goods)) {
                    continue;
                }

                float mapGeneratedAmount = 0f;
                foreach (var src in goods.miscSources) {
                    if (src is Entity ent && ent.mapGenerated) {
                        foreach (var product in ent.loot) {
                            if (product.goods == goods) {
                                mapGeneratedAmount += product.amount;
                            }
                        }
                    }
                }
                var variable = workspaceSolver.MakeVar(CostLowerLimit, CostLimitWhenGeneratesOnMap / mapGeneratedAmount, false, goods.name);
                objective.SetCoefficient(variable, 1e-3); // adding small amount to each object cost, so even objects that aren't required for science will get cost calculated
                variables[goods] = variable;
            }

            foreach (var (item, count) in sciencePackUsage) {
                objective.SetCoefficient(variables[item], count / 1000f);
            }

            var export = Database.objects.CreateMapping<float>();
            recipeCost = Database.recipes.CreateMapping<float>();
            flow = Database.objects.CreateMapping<float>();
            var lastVariable = Database.goods.CreateMapping<Variable>();
            foreach (Recipe recipe in Database.recipes.all.ExceptExcluded(this)) {
                if (!ShouldInclude(recipe)) {
                    continue;
                }

                if (onlyCurrentMilestones && !recipe.IsAccessibleWithCurrentMilestones()) {
                    continue;
                }

                // TODO incorporate fuel selection. Now just select fuel if it only uses 1 fuel
                Goods? singleUsedFuel = null;
                float singleUsedFuelAmount = 0f;
                float minEmissions = 100f;
                int minSize = 15;
                float minPower = 1000f;
                foreach (var crafter in recipe.crafters) {
                    minEmissions = MathF.Min(crafter.energy.emissions, minEmissions);
                    if (crafter.energy.type == EntityEnergyType.Heat) {
                        break;
                    }

                    if (crafter.size < minSize) {
                        minSize = crafter.size;
                    }

                    float power = crafter.energy.type == EntityEnergyType.Void ? 0f : recipe.time * crafter.power / (crafter.craftingSpeed * crafter.energy.effectivity);
                    if (power < minPower) {
                        minPower = power;
                    }

                    foreach (var fuel in crafter.energy.fuels) {
                        if (!ShouldInclude(fuel)) {
                            continue;
                        }

                        if (fuel.fuelValue <= 0f) {
                            singleUsedFuel = null;
                            break;
                        }
                        float amount = power / fuel.fuelValue;
                        if (singleUsedFuel == null) {
                            singleUsedFuel = fuel;
                            singleUsedFuelAmount = amount;
                        }
                        else if (singleUsedFuel == fuel) {
                            singleUsedFuelAmount = MathF.Min(singleUsedFuelAmount, amount);
                        }
                        else {
                            singleUsedFuel = null;
                            break;
                        }
                    }
                    if (singleUsedFuel == null) {
                        break;
                    }
                }

                if (minPower < 0f) {
                    minPower = 0f;
                }

                int size = Math.Max(minSize, (recipe.ingredients.Length + recipe.products.Length) / 2);
                float sizeUsage = CostPerSecond * recipe.time * size;
                float logisticsCost = (sizeUsage * (1f + (CostPerIngredientPerSize * recipe.ingredients.Length) + (CostPerProductPerSize * recipe.products.Length))) + (CostPerMj * minPower);

                if (singleUsedFuel == Database.electricity || singleUsedFuel == Database.voidEnergy || singleUsedFuel == Database.heat) {
                    singleUsedFuel = null;
                }

                var constraint = workspaceSolver.MakeConstraint(double.NegativeInfinity, 0, recipe.name);
                constraints[recipe] = constraint;

                foreach (var product in recipe.products) {
                    var var = variables[product.goods];
                    float amount = product.amount;
                    constraint.SetCoefficientCheck(var, amount, ref lastVariable[product.goods]);
                    if (product.goods is Item) {
                        logisticsCost += amount * CostPerItem;
                    }
                    else if (product.goods is Fluid) {
                        logisticsCost += amount * CostPerFluid;
                    }
                }

                if (singleUsedFuel != null) {
                    var var = variables[singleUsedFuel];
                    constraint.SetCoefficientCheck(var, -singleUsedFuelAmount, ref lastVariable[singleUsedFuel]);
                }

                foreach (var ingredient in recipe.ingredients) {
                    var var = variables[ingredient.goods]; // TODO split cost analysis
                    constraint.SetCoefficientCheck(var, -ingredient.amount, ref lastVariable[ingredient.goods]);
                    if (ingredient.goods is Item) {
                        logisticsCost += ingredient.amount * CostPerItem;
                    }
                    else if (ingredient.goods is Fluid) {
                        logisticsCost += ingredient.amount * CostPerFluid;
                    }
                }

                if (recipe.sourceEntity != null && recipe.sourceEntity.mapGenerated) {
                    float totalMining = 0f;
                    foreach (var product in recipe.products) {
                        totalMining += product.amount;
                    }

                    float miningPenalty = MiningPenalty;
                    float totalDensity = recipe.sourceEntity.mapGenDensity / totalMining;
                    if (totalDensity < MiningMaxDensityForPenalty) {
                        float extraPenalty = MathF.Log(MiningMaxDensityForPenalty / totalDensity);
                        miningPenalty += Math.Min(extraPenalty, MiningMaxExtraPenaltyForRarity);
                    }

                    logisticsCost *= miningPenalty;
                }

                if (minEmissions >= 0f) {
                    logisticsCost += minEmissions * CostPerPollution * recipe.time * project.settings.PollutionCostModifier;
                }

                constraint.SetUb(logisticsCost);
                export[recipe] = logisticsCost;
                recipeCost[recipe] = logisticsCost;

                // export controls the value shown by the "There are better recipes to create X (wasting Y% of YAFC cost)" string.
                // recipeCost controls the decisions made by the solver. We only want to affect the solver.
                if (onlyCurrentMilestones ? !recipe.IsAccessibleWithCurrentMilestones() : !recipe.IsAccessible()) {
                    recipeCost[recipe] *= project.settings.InaccessibleRecipePenalty;
                }
            }

            // TODO this is temporary fix for strange item sources (make the cost of item not higher than the cost of its source)
            foreach (Item item in Database.items.all.ExceptExcluded(this)) {
                if (ShouldInclude(item)) {
                    foreach (var source in item.miscSources) {
                        if (source is Goods g && ShouldInclude(g)) {
                            var constraint = workspaceSolver.MakeConstraint(double.NegativeInfinity, 0, "source-" + item.locName);
                            constraint.SetCoefficient(variables[g], -1);
                            constraint.SetCoefficient(variables[item], 1);
                        }
                    }
                }
            }

            // TODO this is temporary fix for fluid temperatures (make the cost of fluid with lower temp not higher than the cost of fluid with higher temp)
            foreach (var (name, fluids) in Database.fluidVariants) {
                var prev = fluids[0];
                for (int i = 1; i < fluids.Count; i++) {
                    var cur = fluids[i];
                    var constraint = workspaceSolver.MakeConstraint(double.NegativeInfinity, 0, "fluid-" + name + "-" + prev.temperature);
                    constraint.SetCoefficient(variables[prev], 1);
                    constraint.SetCoefficient(variables[cur], -1);
                    prev = cur;
                }
            }

            var result = workspaceSolver.TrySolveWithDifferentSeeds();
            logger.Information("Cost analysis completed in {ElapsedTime}ms with result {result}", time.ElapsedMilliseconds, result);
            float sumImportance = 1f;
            int totalRecipes = 0;
            if (result is Solver.ResultStatus.OPTIMAL or Solver.ResultStatus.FEASIBLE) {
                float objectiveValue = (float)objective.Value();
                logger.Information("Estimated modpack cost: {EstimatedCost}", DataUtils.FormatAmount(objectiveValue * 1000f, UnitOfMeasure.None));
                foreach (Goods g in Database.goods.all.ExceptExcluded(this)) {
                    if (variables[g] == null) {
                        continue;
                    }

                    float value = (float)variables[g].SolutionValue();
                    export[g] = value;
                }

                foreach (Recipe recipe in Database.recipes.all.ExceptExcluded(this)) {
                    if (constraints[recipe] == null) {
                        continue;
                    }

                    float recipeFlow = (float)constraints[recipe].DualValue();
                    if (recipeFlow > 0f) {
                        totalRecipes++;
                        sumImportance += recipeFlow;
                        flow[recipe] = recipeFlow;
                        foreach (var product in recipe.products) {
                            flow[product.goods] += recipeFlow * product.amount;
                        }
                    }
                }
            }
            foreach (FactorioObject o in Database.objects.all.ExceptExcluded(this)) {
                if (!ShouldInclude(o)) {
                    export[o] = float.PositiveInfinity;
                    continue;
                }

                if (o is RecipeOrTechnology recipe) {
                    foreach (var ingredient in recipe.ingredients) // TODO split
{
                        if (float.IsFinite(export[ingredient.goods])) {
                            export[o] += export[ingredient.goods] * ingredient.amount;
                        }
                    }
                }
                else if (o is Entity entity) {
                    float minimal = float.PositiveInfinity;
                    foreach (var item in entity.itemsToPlace) {
                        if (export[item] < minimal) {
                            minimal = export[item];
                        }
                    }
                    export[o] = minimal;
                }
            }
            cost = export;

            recipeWastePercentage = Database.recipes.CreateMapping<float>();
            if (result is Solver.ResultStatus.OPTIMAL or Solver.ResultStatus.FEASIBLE) {
                foreach (var (recipe, constraint) in constraints) {
                    if (constraint == null) {
                        continue;
                    }

                    float productCost = 0f;
                    foreach (var product in recipe.products) {
                        productCost += product.amount * export[product.goods];
                    }

                    recipeWastePercentage[recipe] = 1f - (productCost / export[recipe]);
                }
            }
            else {
                if (!onlyCurrentMilestones) {
                    warnings.Error("Cost analysis was unable to process this modpack. This may mean YAFC bug.", ErrorSeverity.AnalysisWarning);
                }
            }

            importantItems = [.. Database.goods.all.ExceptExcluded(this).Where(x => x.usages.Length > 1).OrderByDescending(x => flow[x] * cost[x] * x.usages.Count(y => ShouldInclude(y) && recipeWastePercentage[y] == 0f))];

            workspaceSolver.Dispose();
        }

        public override string description => "Cost analysis computes a hypothetical late-game base. This simulation has two very important results: How much does stuff (items, recipes, etc) cost and how much of stuff do you need. " +
                                              "It also collects a bunch of auxiliary results, for example how efficient are different recipes. These results are used as heuristics and weights for calculations, and are also useful by themselves.";

        private static readonly StringBuilder sb = new StringBuilder();
        public static string GetDisplayCost(FactorioObject goods) {
            float cost = goods.Cost();
            float costNow = goods.Cost(true);
            if (float.IsPositiveInfinity(cost)) {
                return "YAFC analysis: Unable to find a way to fully automate this";
            }

            _ = sb.Clear();

            float compareCost = cost;
            float compareCostNow = costNow;
            string costPrefix;
            if (goods is Fluid) {
                compareCost = cost * 50;
                compareCostNow = costNow * 50;
                costPrefix = "YAFC cost per 50 units of fluid:";
            }
            else if (goods is Item) {
                costPrefix = "YAFC cost per item:";
            }
            else if (goods is Special special && special.isPower) {
                costPrefix = "YAFC cost per 1 MW:";
            }
            else if (goods is Recipe) {
                costPrefix = "YAFC cost per recipe:";
            }
            else {
                costPrefix = "YAFC cost:";
            }

            _ = sb.Append(costPrefix).Append(" ¥").Append(DataUtils.FormatAmount(compareCost, UnitOfMeasure.None));
            if (compareCostNow > compareCost && !float.IsPositiveInfinity(compareCostNow)) {
                _ = sb.Append(" (Currently ¥").Append(DataUtils.FormatAmount(compareCostNow, UnitOfMeasure.None)).Append(')');
            }

            return sb.ToString();
        }

        public static float GetBuildingHours(Recipe recipe, float flow) {
            return recipe.time * flow * (1000f / 3600f);
        }

        public string? GetItemAmount(Goods goods) {
            float itemFlow = flow[goods];
            if (itemFlow <= 1f) {
                return null;
            }

            return DataUtils.FormatAmount(itemFlow * 1000f, UnitOfMeasure.None, itemAmountPrefix);
        }
    }
}
