using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Yafc.Model;

public partial class ProductionTable {
    /// <summary>
    /// A recipe that appears in the production table, with additional context allowing it to find and obey <see cref="ImplicitLink"/>s.
    /// </summary>
    /// <param name="row">The underlying <see cref="RecipeRow"/>.</param>
    /// <param name="extraLinks">The <see cref="Dictionary{TKey, TValue}"/> that will eventually the contain extra links this recipe needs to
    /// consider. This may be incomplete at the time of construction, provided it is complete before the first call to <see cref="FindLink"/>.
    /// </param>
    private class GenuineRecipe(RecipeRow row, Dictionary<(ProductionTable, IObjectWithQuality<Goods>), IProductionLink> extraLinks) : IRecipeRow {
        /// <summary>
        /// Check both <see cref="row"/> and <see cref="extraLinks"/> for a link corresponding to <paramref name="goods"/>.
        /// </summary>
        public bool FindLink(IObjectWithQuality<Goods> goods, [MaybeNullWhen(false)] out IProductionLink link) {
            // Get the genuine user-visible link, if present.
            _ = row.FindLink(goods, out var genuineLink);

            // Search for a more nested synthetic link
            ProductionTable? linkOwner = row.linkRoot;
            while (linkOwner != genuineLink?.owner) {
                if (extraLinks.TryGetValue((linkOwner!, goods), out link)) { // null-forgiving: genuineLink?.owner is null, linkOwner, or an ancestor of linkOwner.
                    // There's a more-nested synthetic link, so return it instead of the one the project knows about.
                    return true;
                }
                linkOwner = linkOwner!.owner.ownerObject as ProductionTable;
            }

            // Return the genuine link
            link = genuineLink;
            return link != null;
        }

        // Pass all remaining calls through to the underlying RecipeRow.
        public IObjectWithQuality<Goods>? fuel => row.fuel;
        public float fixedBuildings => row.fixedBuildings;
        public RecipeParameters parameters => row.parameters;
        public double recipesPerSecond { set => row.recipesPerSecond = value; }
        public RecipeLinks links => row.links;
        public IEnumerable<SolverIngredient> IngredientsForSolver => row.IngredientsForSolver;
        public IEnumerable<SolverProduct> ProductsForSolver => row.ProductsForSolver;
        public string SolverName => row.recipe.QualityName();
        public double BaseCost => (row.recipe.target as Recipe)?.RecipeBaseCost() ?? 0;

        RecipeRow? IRecipeRow.RecipeRow => row;
    }
}
