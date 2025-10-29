using System.Collections.Generic;

namespace Yafc.Model;

public partial class ProductionTable {
    /// <summary>
    /// An implicitly-created link, used to ensure synthetic recipes are properly connected to the rest of the production table.
    /// </summary>
    /// <param name="goods">The linked <see cref="IObjectWithQuality{T}"/>.</param>
    /// <param name="owner">The <see cref="ProductionTable"/> that owns this link.</param>
    /// <param name="displayLink">The ordinary link that caused the creation of this implicit link, and the link that will be displayed if the
    /// user requests the summary for this link.</param>
    private class ImplicitLink(IObjectWithQuality<Goods> goods, ProductionTable owner, ProductionLink displayLink) : IProductionLink {
        /// <summary>
        /// Always <see cref="LinkAlgorithm.Match"/>; implicit links never allow over/under production.
        /// </summary>
        public LinkAlgorithm algorithm => LinkAlgorithm.Match;

        public IObjectWithQuality<Goods> goods { get; } = goods;

        /// <summary>
        /// Always 0; implicit links never request additional production or consumption.
        /// </summary>
        public float amount => 0;

        public HashSet<ISolverRow> capturedRecipes { get; } = [];
        public int solverIndex { get; set; }
        public float linkFlow { set { } }
        public float notMatchedFlow { get; set; }

        public ProductionTable owner { get; } = owner;
        public ProductionLink.Flags flags { get; set; }

        /// <summary>
        /// Always empty; implicit links never have warnings.
        /// </summary>
        public IEnumerable<string> LinkWarnings { get; } = [];

        public ProductionLink DisplayLink { get; } = displayLink;
    }
}
