using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Yafc.Model;

public partial class ProductionTable {
    /// <summary>
    /// A recipe for allowing the solver to treat one quality science pack as (Quality.level+1) normal science packs
    /// </summary>
    /// <param name="pack">The normal-quality pack produced by this recipe.</param>
    /// <param name="quality">The quality of the recipe's input pack.</param>
    /// <param name="productLink">The (genuine) link for the output pack.</param>
    /// <param name="ingredientLink">The (implicit) link for the input pack.</param>
    private class ScienceDecomposition(Goods pack, Quality quality, IProductionLink productLink, ImplicitLink ingredientLink) : IRecipeRow {
        public IObjectWithQuality<Goods>? fuel => null;

        /// <summary>
        /// Always 0; the solver must scale this recipe to match the available quality packs.
        /// </summary>
        public float fixedBuildings => 0;

        /// <inheritdoc/>
        public IEnumerable<SolverIngredient> IngredientsForSolver => [new SolverIngredient(pack.With(quality), 1, ingredientLink, 0, null)];
        /// <inheritdoc/>
        public IEnumerable<SolverProduct> ProductsForSolver => [new SolverProduct(pack.With(Quality.Normal), quality.level + 1, productLink, 0, null)];

        public RecipeParameters parameters { get; } = RecipeParameters.Empty;
        public double recipesPerSecond { set { } }
        public RecipeLinks links => new() { ingredients = [null] };

        /// <inheritdoc/>
        public string SolverName => $"Convert {pack.name} from {quality.name}";

        public double BaseCost => 0;

        public bool FindLink(IObjectWithQuality<Goods> goods, [MaybeNullWhen(false)] out IProductionLink link) {
            if (ingredientLink.goods == goods) {
                link = ingredientLink;
                return true;
            }
            if (productLink.goods == goods) {
                link = productLink;
                return true;
            }
            link = null;
            return false;
        }

        /// <summary>
        /// Always null; this does not represent a user-visible <see cref="RecipeRow"/>.
        /// </summary>
        RecipeRow? IRecipeRow.RecipeRow => null;
    }
}
