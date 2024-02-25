using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using YAFC.UI;

namespace YAFC.Model {
    public class ProductionSummaryGroup : ModelObject<ModelObject>, IElementGroup<ProductionSummaryEntry> {
        public ProductionSummaryGroup(ModelObject owner) : base(owner) { }
        public List<ProductionSummaryEntry> elements { get; } = new List<ProductionSummaryEntry>();
        [NoUndo]
        public bool expanded { get; set; }
        public string name { get; set; }

        public void Solve(Dictionary<Goods, float> totalFlow, float multiplier) {
            foreach (ProductionSummaryEntry element in elements) {
                element.RefreshFlow();
            }

            totalFlow.Clear();
            foreach (ProductionSummaryEntry row in elements) {
                foreach ((Goods item, float amount) in row.flow) {
                    _ = totalFlow.TryGetValue(item, out float prev);
                    totalFlow[item] = prev + (amount * multiplier);
                }
            }
        }

        public void UpdateFilter(Goods filteredGoods, SearchQuery searchQuery) {
            foreach (ProductionSummaryEntry element in elements) {
                element.UpdateFilter(filteredGoods, searchQuery);
            }
        }
    }

    public class ProductionSummaryEntry : ModelObject<ProductionSummaryGroup>, IGroupedElement<ProductionSummaryGroup> {
        public ProductionSummaryEntry(ProductionSummaryGroup owner) : base(owner) { }

        protected internal override void AfterDeserialize() {
            // Must be either page reference, or subgroup, not both
            if (subgroup == null && page == null) {
                throw new NotSupportedException("Referenced page does not exist");
            }

            if (subgroup != null && page != null) {
                page = null;
            }

            base.AfterDeserialize();
        }

        public float multiplier { get; set; } = 1;
        public PageReference page { get; set; }
        public ProductionSummaryGroup subgroup { get; set; }
        public bool visible { get; private set; } = true;
        [SkipSerialization] public Dictionary<Goods, float> flow { get; } = new Dictionary<Goods, float>();
        private bool needRefreshFlow = true;

        public Icon icon => subgroup != null ? Icon.Folder : page.page == null ? Icon.Warning : page.page.icon?.icon ?? Icon.None;

        public string name => page != null ? page.page?.name ?? "Page missing" : "Broken entry";

        public bool CollectSolvingTasks(List<Task> listToFill) {
            Task solutionTask = SolveIfNessessary();
            if (solutionTask != null) {
                listToFill.Add(solutionTask);
                needRefreshFlow = true;
            }

            if (subgroup != null) {
                foreach (ProductionSummaryEntry element in subgroup.elements) {
                    needRefreshFlow |= element.CollectSolvingTasks(listToFill);
                }
            }
            return needRefreshFlow;
        }

        public Task SolveIfNessessary() {
            if (page == null) {
                return null;
            }

            ProjectPage solutionPagepage = page.page;
            return solutionPagepage != null && solutionPagepage.IsSolutionStale() ? solutionPagepage.ExternalSolve() : (Task)null;
        }

        public float GetAmount(Goods goods) {
            return flow.TryGetValue(goods, out float amount) ? amount : 0;
        }

        public void RefreshFlow() {
            if (!needRefreshFlow) {
                return;
            }

            needRefreshFlow = false;
            flow.Clear();
            if (subgroup != null) {
                subgroup.Solve(flow, multiplier);
            }
            else {
                if (page?.page?.content is not ProductionTable spage) {
                    return;
                }

                foreach (ProductionTableFlow flowEntry in spage.flow) {
                    if (flowEntry.amount != 0) {
                        flow[flowEntry.goods] = flowEntry.amount * multiplier;
                    }
                }

                foreach (ProductionLink link in spage.links) {
                    if (link.amount != 0) {
                        _ = flow.TryGetValue(link.goods, out float prevValue);
                        flow[link.goods] = prevValue + (link.amount * multiplier);
                    }
                }
            }
        }

        public void SetOwner(ProductionSummaryGroup newOwner) {
            owner = newOwner;
        }

        public void UpdateFilter(Goods goods, SearchQuery query) {
            visible = flow.ContainsKey(goods);
            subgroup?.UpdateFilter(goods, query);
        }

        public void SetMultiplier(float newMultiplier) {
            _ = this.RecordUndo();
            needRefreshFlow = true;
            multiplier = newMultiplier;
        }
    }

    public class ProductionSummaryColumn : ModelObject<ProductionSummary> {
        public ProductionSummaryColumn(ProductionSummary owner, Goods goods) : base(owner) {
            this.goods = goods ?? throw new ArgumentNullException(nameof(goods), "Object does not exist");
        }
        public Goods goods { get; }
    }

    public class ProductionSummary : ProjectPageContents, IComparer<(Goods goods, float amount)> {
        public ProductionSummary(ModelObject page) : base(page) {
            group = new ProductionSummaryGroup(this);
        }
        public ProductionSummaryGroup group { get; }
        public List<ProductionSummaryColumn> columns { get; } = new List<ProductionSummaryColumn>();
        [SkipSerialization] public List<(Goods goods, float amount)> sortedFlow { get; } = new List<(Goods goods, float amount)>();

        private readonly Dictionary<Goods, float> totalFlow = new Dictionary<Goods, float>();
        [SkipSerialization] public HashSet<Goods> columnsExist { get; } = new HashSet<Goods>();

        public override void InitNew() {
            columns.Add(new ProductionSummaryColumn(this, Database.electricity));
            base.InitNew();
        }

        public float GetTotalFlow(Goods goods) {
            return totalFlow.TryGetValue(goods, out float amount) ? amount : 0;
        }

        public override async Task<string> Solve(ProjectPage page) {
            List<Task> taskList = new List<Task>();
            foreach (ProductionSummaryEntry element in group.elements) {
                _ = element.CollectSolvingTasks(taskList);
            }

            if (taskList.Count > 0) {
                await Task.WhenAll(taskList);
            }

            columnsExist.Clear();
            group.Solve(totalFlow, 1);

            foreach (ProductionSummaryColumn column in columns) {
                _ = columnsExist.Add(column.goods);
            }

            sortedFlow.Clear();
            foreach (KeyValuePair<Goods, float> element in totalFlow) {
                sortedFlow.Add((element.Key, element.Value));
            }

            sortedFlow.Sort(this);
            return null;
        }

        public int Compare((Goods goods, float amount) x, (Goods goods, float amount) y) {
            float amt1 = x.goods.fluid != null ? x.amount / 50f : x.amount;
            float amt2 = y.goods.fluid != null ? y.amount / 50f : y.amount;
            return amt1.CompareTo(amt2);
        }
    }
}
