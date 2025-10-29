using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Yafc.Model.Tests.Model;

[Collection("LuaDependentTests")]
public class ProductionTableContentTests {
    [Fact]
    public void ChangeFuelEntityModules_ShouldPreserveFixedAmount() {
        Project project = LuaDependentTestHelper.GetProjectForLua();

        ProjectPage page = new ProjectPage(project, typeof(ProductionTable));
        ProductionTable table = (ProductionTable)page.content;
        table.AddRecipe(Database.recipes.all.Single(r => r.name == "recipe").With(Quality.Normal), DataUtils.DeterministicComparer);
        RecipeRow row = table.GetAllRecipes().Single();

        table.modules.beacon = Database.allBeacons.Single().With(Quality.Normal);
        table.modules.beaconModule = Database.allModules.Single(m => m.name == "speed-module").With(Quality.Normal);
        table.modules.beaconsPerBuilding = 2;
        table.modules.autoFillPayback = MathF.Sqrt(float.MaxValue);

        RunTest(row, testCombinations, (3 * 3 + 3 * 1) * (9 + 2) * 6); // Crafter&fuel * modules * available fixed values

        // Cycle through all crafters (3 burner, 3 electric), fuels (3+1), and internal modules (9 + empty + default), and call assert for each combination.
        // assert will ensure the currently fixed value has not changed by more than 0.01%.
        static void testCombinations(RecipeRow row, ProductionTable table, Action assert) {
            foreach (EntityCrafter crafter in Database.allCrafters) {
                row.entity = crafter.With(Quality.Normal);

                foreach (Goods fuel in crafter.energy.fuels) {
                    row.fuel = fuel.With(Quality.Normal);

                    foreach (Module module in Database.allModules.Concat([null])) {
                        ModuleTemplateBuilder builder = new();

                        if (module != null) {
                            builder.list.Add((module.With(Quality.Normal), 0));
                        }

                        row.modules = builder.Build(row);
                        table.Solve((ProjectPage)table.owner).Wait();
                        assert();
                    }
                    row.modules = null;
                    table.Solve((ProjectPage)table.owner).Wait();
                    assert();
                }
            }
        }
    }

    [Fact]
    public void ChangeProductionTableModuleConfig_ShouldPreserveFixedAmount() {
        Project project = LuaDependentTestHelper.GetProjectForLua();

        ProjectPage page = new ProjectPage(project, typeof(ProductionTable));
        ProductionTable table = (ProductionTable)page.content;
        table.AddRecipe(Database.recipes.all.Single(r => r.name == "recipe").With(Quality.Normal), DataUtils.DeterministicComparer);
        RecipeRow row = table.GetAllRecipes().Single();

        List<Module> modules = [.. Database.allModules.Where(m => !m.name.Contains("productivity"))];
        EntityBeacon beacon = Database.allBeacons.Single();

        RunTest(row, testCombinations, (3 * 3 + 3 * 1) * 6 * 13 * 32 * 6); // Crafter&fuel * modules * beacon count * payback values * available fixed values

        // Cycle through all crafters (3 burner, 3 electric), fuels (3+1), and beacon modules (6). Also cycle through 0-12 beacons per building and 32 possible payback values.
        // Call assert for each combination. assert will ensure the currently fixed value has not changed by more than 0.01%.
        void testCombinations(RecipeRow row, ProductionTable table, Action assert) {
            foreach (EntityCrafter crafter in Database.allCrafters) {
                row.entity = crafter.With(Quality.Normal);

                foreach (Goods fuel in crafter.energy.fuels) {
                    row.fuel = fuel.With(Quality.Normal);

                    foreach (Module module in modules) {
                        for (int beaconCount = 0; beaconCount < 13; beaconCount++) {
                            for (float payback = 1; payback < float.MaxValue; payback *= 16) {
                                if (table.GetType().GetProperty("modules").SetMethod is MethodInfo method) {
                                    // Preemptive code for if ProductionTable.modules is made writable.
                                    // The ProductionTable.modules setter must notify all relevant recipes if it is added.
                                    _ = method.Invoke(table, [new ModuleFillerParameters(table) {
                                        beacon = beacon.With(Quality.Normal),
                                        beaconModule = module.With(Quality.Normal),
                                        beaconsPerBuilding = beaconCount,
                                    }]);
                                }
                                else {
                                    table.modules.beacon = beacon.With(Quality.Normal);
                                    table.modules.beaconModule = module.With(Quality.Normal);
                                    table.modules.beaconsPerBuilding = beaconCount;
                                }
                                table.Solve((ProjectPage)table.owner).Wait();
                                assert();
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Test many combinations of buildings and fuels to make sure products (especially spent fuels) display the appropriate amounts.
    /// </summary>
    [Fact]
    public async Task AllCombinationsWithVariousFixedCounts_DisplayedProductsMatchScaledSolverProducts() {
        Project project = LuaDependentTestHelper.GetProjectForLua();
        ProjectPage page = new ProjectPage(project, typeof(ProductionTable));
        ProductionTable table = (ProductionTable)page.content;

        // Generate random but repeatable building counts
        Random r = new Random(0);
        int testCount = 0;

        // Run through all combinations of recipe, crafter, fuel, and fixed module, including all qualities.
        foreach (IObjectWithQuality<RecipeOrTechnology> recipe in Database.recipes.all.WithAllQualities()) {
            table.AddRecipe(recipe, DataUtils.DeterministicComparer);
            RecipeRow row = table.GetAllRecipes().Last();

            foreach (IObjectWithQuality<EntityCrafter> crafter in Database.allCrafters.WithAllQualities()) {
                row.entity = crafter;

                foreach (IObjectWithQuality<Goods> fuel in crafter.target.energy.fuels.WithAllQualities()) {
                    row.fuel = fuel;

                    foreach (IObjectWithQuality<Module> module in Database.allModules.WithAllQualities().Prepend(null)) {
                        row.modules = module == null ? null : new ModuleTemplateBuilder { list = { (module, 0) } }.Build(row);
                        do {
                            // r.NextDouble could (at least in theory) return 0 or a value that rounds to 0.
                            // Discard such values and try again.
                            row.fixedBuildings = (float)r.NextDouble();
                        } while (row.fixedBuildings == 0);

                        await table.Solve(page);

                        foreach (var (display, solver) in row.Ingredients.Zip(row.IngredientsForSolver)) {
                            var (solverGoods, solverAmount, _, _, _) = solver;
                            var (displayGoods, displayAmount, _, _) = display;

                            try {
                                // If this fails, something weird went wrong
                                Assert.Equal(solverGoods, displayGoods);
                                // This tests for a failure related to https://github.com/shpaass/yafc-ce/issues/441, but for ingredients instead
                                Assert.Equal(solverAmount * row.recipesPerSecond, displayAmount, solverAmount * .0001);
                            }
                            catch {
                                // XUnit wants us to use async/await instead of calling Solve().Wait()
                                // If we do that, VS does not break when an Assert throws. Set a breakpoint here instead.
                                throw;
                            }
                        }

                        foreach (var (display, solver) in row.Products.Zip(row.ProductsForSolver
                            // ProductsForSolver doesn't include the spent fuel. Append an entry for the spent fuel, in the case that the spent
                            // fuel is not a recipe product.
                            // If the spent fuel is also a recipe product, this value will ignored in favor of the recipe-product value.
                            .Append(new(row.fuel.FuelResult(), 0, null, 0, null)))) {

                            var (solverGoods, solverAmount, _, _, _) = solver;
                            var (displayGoods, displayAmount, _, _) = display;

                            if (solverGoods == row.fuel.FuelResult()) {
                                // ProductsForSolver doesn't include the spent fuel (in either the real or test-specific result)
                                // Add the spent fuel amount to the value given to the solver.
                                solverAmount += row.parameters.fuelUsagePerSecondPerRecipe;
                            }

                            try {
                                // If this fails, something weird went wrong
                                Assert.Equal(solverGoods, displayGoods);
                                // This tests for actual failure observed in https://github.com/shpaass/yafc-ce/issues/441
                                Assert.Equal(solverAmount * row.recipesPerSecond, displayAmount, solverAmount * .0001);
                            }
                            catch {
                                // XUnit wants us to use async/await instead of calling Solve().Wait()
                                // If we do that, VS does not break when an Assert throws. Set a breakpoint here instead.
                                throw;
                            }
                        }

                        testCount++;
                    }
                }
            }
        }

        // Ignoring quality, we have:
        // 2 recipes, 2 mechanics, 3 electric crafters and 3 burner crafters (with 3 fuels), and 9 modules (plus no modules)
        // Considering quality, we have:
        // 4 recipes, 2 mechanics, 6 electric crafters and 6 burner crafters (with 6 fuels), and 18 modules (plus no modules)
        // All combinations should be tested
        Assert.Equal((4 + 2) * (6 + 6 * 6) * (18 + 1), testCount);
    }

    /// <summary>
    /// Run the <c>Change..._ShouldPreserve...</c> tests for fixed buildings, fuel, ingredients, and products.
    /// </summary>
    /// <param name="row">The row containing the recipe to test with fixed amounts.</param>
    /// <param name="testCombinations">An action that loops through the various combinations of entities, beacons, etc, and calls its third parameter for each combination.</param>
    /// <param name="expectedAssertCalls">The expected number of calls to <paramref name="testCombinations"/>' third parameter. The test will fail if some of the calls are omitted.</param>
    private static void RunTest(RecipeRow row, Action<RecipeRow, ProductionTable, Action> testCombinations, int expectedAssertCalls) {
        ProductionTable table = row.owner;
        int assertCalls = 0;
        // Ensure that building count remains constant when the building count is fixed.
        row.fixedBuildings = 1;
        testCombinations(row, table, () => { Assert.Equal(1, row.fixedBuildings); assertCalls++; });

        // Ensure that the fuel consumption remains constant, except when the entity and fuel change simultaneously.
        row.fixedFuel = true;
        ((Goods oldFuel, _), float fuelAmount, _, _) = row.FuelInformation;
        testCombinations(row, table, testFuel(row, table));
        row.fixedFuel = false;

        // Ensure that ingredient consumption remains constant across all possible changes.
        foreach (RecipeRowIngredient ingredient in row.Ingredients) {
            float fixedAmount = ingredient.Amount;
            row.fixedIngredient = ingredient.Goods;
            testCombinations(row, table, () => { Assert.Equal(fixedAmount, row.Ingredients.Single(i => i.Goods == ingredient.Goods).Amount, fixedAmount * .0001); assertCalls++; });
        }
        row.fixedIngredient = null;

        // Ensure that product production remains constant across all possible changes.
        foreach (RecipeRowProduct product in row.Products) {
            float fixedAmount = product.Amount;
            row.fixedProduct = product.Goods;
            testCombinations(row, table, () => { Assert.Equal(fixedAmount, row.Products.Single(p => p.Goods == product.Goods).Amount, fixedAmount * .0001); assertCalls++; });
        }

        Assert.Equal(expectedAssertCalls, assertCalls);

        // The complicated tests for when the fixed value is expected to reset when fixed fuels are involved.
        Action testFuel(RecipeRow row, ProductionTable table) => () => {
            if (row.entity.target.energy.fuels.Contains(oldFuel)) {
                Assert.Equal(fuelAmount, row.FuelInformation.Amount, fuelAmount * .0001);
                assertCalls++;
            }
            else {
                Assert.Equal(0, row.FuelInformation.Amount);
                row.fixedBuildings = 1;
                row.fixedFuel = true;
                table.Solve((ProjectPage)table.owner).Wait();
                ((oldFuel, _), fuelAmount, _, _) = row.FuelInformation;
                assertCalls++;
            }
        };
    }
}
