using System.Collections.Generic;
using System.Linq;

namespace YAFC.Model {
    public class TechnologyScienceAnalysis : Analysis {
        public static readonly TechnologyScienceAnalysis Instance = new TechnologyScienceAnalysis();
        public Mapping<Technology, Ingredient[]> allSciencePacks { get; private set; }

        public Ingredient GetMaxTechnologyIngredient(Technology tech) {
            Ingredient[] list = allSciencePacks[tech];
            Ingredient ingr = null;
            Bits order = new Bits();
            foreach (Ingredient elem in list) {
                Bits elemOrder = Milestones.Instance.GetMilestoneResult(elem.goods.id) - 1;
                if (ingr == null || elemOrder > order) {
                    order = elemOrder;
                    ingr = elem;
                }
            }

            return ingr;
        }

        public override void Compute(Project project, ErrorCollector warnings) {
            Item[] sciencePacks = Database.allSciencePacks;
            Mapping<Goods, int> sciencePackIndex = Database.goods.CreateMapping<int>();
            for (int i = 0; i < sciencePacks.Length; i++) {
                sciencePackIndex[sciencePacks[i]] = i;
            }

            Mapping<Technology, float>[] sciencePackCount = new Mapping<Technology, float>[sciencePacks.Length];
            for (int i = 0; i < sciencePacks.Length; i++) {
                sciencePackCount[i] = Database.technologies.CreateMapping<float>();
            }

            Mapping<Technology, bool> processing = Database.technologies.CreateMapping<bool>();
            Mapping<Technology, Technology, bool> requirementMap = Database.technologies.CreateMapping<Technology, bool>(Database.technologies);

            Queue<Technology> queue = new Queue<Technology>();
            foreach (Technology tech in Database.technologies.all) {
                if (tech.prerequisites.Length == 0) {
                    processing[tech] = true;
                    queue.Enqueue(tech);
                }
            }
            Queue<Technology> prerequisiteQueue = new Queue<Technology>();

            while (queue.Count > 0) {
                Technology current = queue.Dequeue();

                // Fast processing for the first prerequisite (just copy everything)
                if (current.prerequisites.Length > 0) {
                    Technology firstRequirement = current.prerequisites[0];
                    foreach (Mapping<Technology, float> pack in sciencePackCount) {
                        pack[current] += pack[firstRequirement];
                    }

                    requirementMap.CopyRow(firstRequirement, current);
                }

                requirementMap[current, current] = true;
                prerequisiteQueue.Enqueue(current);

                while (prerequisiteQueue.Count > 0) {
                    Technology prerequisite = prerequisiteQueue.Dequeue();
                    foreach (Ingredient ingredient in prerequisite.ingredients) {
                        int science = sciencePackIndex[ingredient.goods];
                        sciencePackCount[science][current] += ingredient.amount * prerequisite.count;
                    }

                    foreach (Technology prerequisitePrerequisite in prerequisite.prerequisites) {
                        if (!requirementMap[current, prerequisitePrerequisite]) {
                            prerequisiteQueue.Enqueue(prerequisitePrerequisite);
                            requirementMap[current, prerequisitePrerequisite] = true;
                        }
                    }
                }

                foreach (FactorioId unlocks in Dependencies.reverseDependencies[current]) {
                    if (Database.objects[unlocks] is Technology tech && !processing[tech]) {
                        foreach (Technology techPreq in tech.prerequisites) {
                            if (!processing[techPreq]) {
                                goto locked;
                            }
                        }

                        processing[tech] = true;
                        queue.Enqueue(tech);

locked:;
                    }
                }
            }

            allSciencePacks = Database.technologies.CreateMapping(tech => sciencePackCount.Select((x, id) => x[tech] == 0 ? null : new Ingredient(sciencePacks[id], x[tech])).Where(x => x != null).ToArray());
        }

        public override string description =>
            "Technology analysis calculates the total amount of science packs required for each technology";
    }
}
