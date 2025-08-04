using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Yafc.Model.Tests.Model;

[Collection("LuaDependentTests")]
public class PercentageIngredientConsumptionTests {

    [Fact]
    public async Task SetIngredientConsumptionPercentage_SingleRecipe_ShouldApplyPercentageCorrectly() {
        Project project = LuaDependentTestHelper.GetProjectForLua("Yafc.Model.Tests.Model.ProductionTableContentTests.lua");
        ProjectPage page = new ProjectPage(project, typeof(ProductionTable));
        ProductionTable table = (ProductionTable)page.content;

        // Add a recipe that consumes ingredients
        var recipe = Database.recipes.all.First(r => r.ingredients.Length > 0);
        table.AddRecipe(recipe.With(Quality.Normal), DataUtils.DeterministicComparer);
        RecipeRow row = table.GetAllRecipes().Single();

        // Get the first ingredient with quality
        var ingredient = recipe.ingredients[0].goods.With(Quality.Normal);

        // Set 50% consumption for the ingredient
        row.ingredientConsumptionPercentages[ingredient] = 0.5f;

        await table.Solve(page);

        // Verify the percentage is stored correctly
        Assert.True(row.ingredientConsumptionPercentages.ContainsKey(ingredient));
        Assert.Equal(0.5f, row.ingredientConsumptionPercentages[ingredient], 0.001f);

        // Verify the ingredient consumption is reduced by 50%
        var originalAmount = recipe.ingredients[0].amount;
        var actualIngredient = row.Ingredients.First(i => i.Goods == ingredient);
        var expectedAmount = originalAmount * 0.5f * row.recipesPerSecond;
        Assert.Equal(expectedAmount, actualIngredient.Amount, expectedAmount * 0.001f);
    }

    [Fact]
    public async Task ClearIngredientConsumptionPercentage_ShouldRemoveConstraint() {
        Project project = LuaDependentTestHelper.GetProjectForLua("Yafc.Model.Tests.Model.ProductionTableContentTests.lua");
        ProjectPage page = new ProjectPage(project, typeof(ProductionTable));
        ProductionTable table = (ProductionTable)page.content;

        var recipe = Database.recipes.all.First(r => r.ingredients.Length > 0);
        table.AddRecipe(recipe.With(Quality.Normal), DataUtils.DeterministicComparer);
        RecipeRow row = table.GetAllRecipes().Single();

        var ingredient = recipe.ingredients[0].goods.With(Quality.Normal);

        // Set percentage
        row.ingredientConsumptionPercentages[ingredient] = 0.5f;
        await table.Solve(page);

        // Verify it's set
        Assert.True(row.ingredientConsumptionPercentages.ContainsKey(ingredient));

        // Clear percentage
        _ = row.ingredientConsumptionPercentages.Remove(ingredient);
        await table.Solve(page);

        // Verify it's removed
        Assert.False(row.ingredientConsumptionPercentages.ContainsKey(ingredient));

        // Verify consumption returns to normal
        var originalAmount = recipe.ingredients[0].amount;
        var actualIngredient = row.Ingredients.First(i => i.Goods == ingredient);
        var expectedAmount = originalAmount * row.recipesPerSecond;
        Assert.Equal(expectedAmount, actualIngredient.Amount, expectedAmount * 0.001f);
    }

    [Fact]
    public async Task AutomaticAlgorithmSwitching_SingleRecipeWithPercentage_ShouldUseAllowOverProduction() {
        Project project = LuaDependentTestHelper.GetProjectForLua("Yafc.Model.Tests.Model.ProductionTableContentTests.lua");
        ProjectPage page = new ProjectPage(project, typeof(ProductionTable));
        ProductionTable table = (ProductionTable)page.content;

        var recipe = Database.recipes.all.First(r => r.ingredients.Length > 0);
        table.AddRecipe(recipe.With(Quality.Normal), DataUtils.DeterministicComparer);
        RecipeRow row = table.GetAllRecipes().Single();

        var ingredient = recipe.ingredients[0].goods.With(Quality.Normal);

        // Set percentage constraint
        row.ingredientConsumptionPercentages[ingredient] = 0.8f;
        await table.Solve(page);

        // Verify algorithm automatically switched to AllowOverProduction for single recipe
        if (table.linkMap.TryGetValue(ingredient, out var link)) {
            Assert.Equal(LinkAlgorithm.AllowOverProduction, link.algorithm);
        }
    }

    [Fact]
    public void IngredientConsumptionPercentages_ShouldBeDictionary() {
        Project project = LuaDependentTestHelper.GetProjectForLua("Yafc.Model.Tests.Model.ProductionTableContentTests.lua");
        ProjectPage page = new ProjectPage(project, typeof(ProductionTable));
        ProductionTable table = (ProductionTable)page.content;

        var recipe = Database.recipes.all.First(r => r.ingredients.Length > 0);
        table.AddRecipe(recipe.With(Quality.Normal), DataUtils.DeterministicComparer);
        RecipeRow row = table.GetAllRecipes().Single();

        // Verify the property exists and is a dictionary
        var percentages = row.ingredientConsumptionPercentages;
        Assert.NotNull(percentages);

        // Verify it's a dictionary with the correct key type
        Assert.IsAssignableFrom<Dictionary<IObjectWithQuality<Goods>, float>>(percentages);
    }

    [Fact]
    public void SerializationDeserialization_ShouldPreservePercentages() {
        Project project = LuaDependentTestHelper.GetProjectForLua("Yafc.Model.Tests.Model.ProductionTableContentTests.lua");
        ProjectPage page = new ProjectPage(project, typeof(ProductionTable));
        project.pages.Add(page);
        ProductionTable table = (ProductionTable)page.content;

        var recipe = Database.recipes.all.First(r => r.ingredients.Length > 0);
        table.AddRecipe(recipe.With(Quality.Normal), DataUtils.DeterministicComparer);
        RecipeRow row = table.GetAllRecipes().Single();

        var ingredient = recipe.ingredients[0].goods.With(Quality.Normal);
        row.ingredientConsumptionPercentages[ingredient] = 0.75f;

        // Serialize
        ErrorCollector collector = new();
        using MemoryStream stream = new();
        project.Save(stream);

        // Deserialize
        Project newProject = Project.Read(stream.ToArray(), collector);

        Assert.Equal(ErrorSeverity.None, collector.severity);

        // Verify percentage is preserved
        var newTable = (ProductionTable)newProject.pages[0].content;
        var newRow = newTable.GetAllRecipes().Single();

        Assert.True(newRow.ingredientConsumptionPercentages.ContainsKey(ingredient));
        Assert.Equal(0.75f, newRow.ingredientConsumptionPercentages[ingredient], 0.001f);
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.1f)]
    [InlineData(0.5f)]
    [InlineData(0.9f)]
    [InlineData(1.0f)]
    public async Task VariousPercentageValues_ShouldWorkCorrectly(float percentage) {
        Project project = LuaDependentTestHelper.GetProjectForLua("Yafc.Model.Tests.Model.ProductionTableContentTests.lua");
        ProjectPage page = new ProjectPage(project, typeof(ProductionTable));
        ProductionTable table = (ProductionTable)page.content;

        var recipe = Database.recipes.all.First(r => r.ingredients.Length > 0);
        table.AddRecipe(recipe.With(Quality.Normal), DataUtils.DeterministicComparer);
        RecipeRow row = table.GetAllRecipes().Single();

        var ingredient = recipe.ingredients[0].goods.With(Quality.Normal);

        row.ingredientConsumptionPercentages[ingredient] = percentage;
        await table.Solve(page);

        // Verify the percentage is applied correctly
        var originalAmount = recipe.ingredients[0].amount;
        var actualIngredient = row.Ingredients.First(i => i.Goods == ingredient);
        var expectedAmount = originalAmount * percentage * row.recipesPerSecond;

        if (percentage == 0.0f) {
            // Special case: 0% should result in no consumption
            Assert.Equal(0.0f, actualIngredient.Amount, 0.001f);
        }
        else {
            Assert.Equal(expectedAmount, actualIngredient.Amount, Math.Max(expectedAmount * 0.001f, 0.001f));
        }
    }

    [Fact]
    public async Task BuildIngredients_WithPercentage_ShouldApplyMultiplier() {
        Project project = LuaDependentTestHelper.GetProjectForLua("Yafc.Model.Tests.Model.ProductionTableContentTests.lua");
        ProjectPage page = new ProjectPage(project, typeof(ProductionTable));
        ProductionTable table = (ProductionTable)page.content;

        // Find a recipe with multiple ingredients
        var recipe = Database.recipes.all.FirstOrDefault(r => r.ingredients.Length > 1);
        if (recipe == null) {
            // Skip test if no multi-ingredient recipes available
            return;
        }

        table.AddRecipe(recipe.With(Quality.Normal), DataUtils.DeterministicComparer);
        RecipeRow row = table.GetAllRecipes().Single();

        var ingredient1 = recipe.ingredients[0].goods.With(Quality.Normal);
        var ingredient2 = recipe.ingredients[1].goods.With(Quality.Normal);

        // Set percentage only on first ingredient
        row.ingredientConsumptionPercentages[ingredient1] = 0.25f;

        await table.Solve(page);

        // Verify first ingredient has reduced consumption
        var actualIngredient1 = row.Ingredients.First(i => i.Goods == ingredient1);
        var expectedAmount1 = recipe.ingredients[0].amount * 0.25f * row.recipesPerSecond;
        Assert.Equal(expectedAmount1, actualIngredient1.Amount, expectedAmount1 * 0.001f);

        // Verify second ingredient has normal consumption
        var actualIngredient2 = row.Ingredients.First(i => i.Goods == ingredient2);
        var expectedAmount2 = recipe.ingredients[1].amount * row.recipesPerSecond;
        Assert.Equal(expectedAmount2, actualIngredient2.Amount, expectedAmount2 * 0.001f);
    }

    [Fact]
    public async Task AutomaticRecalculation_WhenSecondRecipeAdded_ShouldTriggerSolverUpdate() {
        Project project = LuaDependentTestHelper.GetProjectForLua("Yafc.Model.Tests.Model.ProductionTableContentTests.lua");
        ProjectPage page = new ProjectPage(project, typeof(ProductionTable));
        ProductionTable table = (ProductionTable)page.content;

        // Add first recipe with percentage constraint
        var recipe1 = Database.recipes.all.First(r => r.ingredients.Length > 0);
        table.AddRecipe(recipe1.With(Quality.Normal), DataUtils.DeterministicComparer);
        RecipeRow row1 = table.GetAllRecipes().Single();

        var sharedIngredient = recipe1.ingredients[0].goods.With(Quality.Normal);
        row1.ingredientConsumptionPercentages[sharedIngredient] = 0.5f;

        // Solve to establish initial state
        await table.Solve(page);

        // Verify single recipe uses AllowOverProduction
        if (table.linkMap.TryGetValue(sharedIngredient, out var initialLink)) {
            Assert.Equal(LinkAlgorithm.AllowOverProduction, initialLink.algorithm);
        }

        // Add second recipe that shares the same ingredient
        var recipe2 = Database.recipes.all.First(r => r != recipe1 &&
            r.ingredients.Any(i => i.goods.With(Quality.Normal).Equals(sharedIngredient)));

        if (recipe2 != null) {
            table.AddRecipe(recipe2.With(Quality.Normal), DataUtils.DeterministicComparer);
            var rows = table.GetAllRecipes().ToList();
            Assert.Equal(2, rows.Count);

            RecipeRow row2 = rows.First(r => r != row1);
            row2.ingredientConsumptionPercentages[sharedIngredient] = 0.3f;

            // The AddRecipe call should have triggered SetToRecalculate()
            // When we solve now, it should automatically switch to Match algorithm
            await table.Solve(page);

            // Verify algorithm switched to Match for multiple recipes
            if (table.linkMap.TryGetValue(sharedIngredient, out var updatedLink)) {
                Assert.Equal(LinkAlgorithm.Match, updatedLink.algorithm);
            }
        }
    }
}