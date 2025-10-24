using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SDL2;
using Yafc.Model;

namespace Yafc.Blueprints;

public static class BlueprintUtilities {
    private static string ExportBlueprint(BlueprintString blueprint, bool copyToClipboard) {
        string result = blueprint.ToBpString();

        if (copyToClipboard) {
            _ = SDL.SDL_SetClipboardText(result);
        }

        return result;
    }

    public static string ExportConstantCombinators(string name, IReadOnlyList<(IObjectWithQuality<Goods> item, int amount)> goods, bool copyToClipboard = true) {
        int combinatorCount = ((goods.Count - 1) / Database.constantCombinatorCapacity) + 1;
        int offset = -combinatorCount / 2;
        BlueprintString blueprint = new BlueprintString(name);
        int index = 0;
        BlueprintEntity? last = null;

        for (int i = 0; i < combinatorCount; i++) {
            BlueprintControlBehavior controlBehavior = new BlueprintControlBehavior();
            BlueprintEntity entity = new BlueprintEntity { index = i + 1, position = { x = i + offset, y = 0 }, name = "constant-combinator", controlBehavior = controlBehavior };
            blueprint.blueprint.entities.Add(entity);

            for (int j = 0; j < Database.constantCombinatorCapacity; j++) {
                var (item, amount) = goods[index++];
                BlueprintControlFilter filter = new BlueprintControlFilter { index = j + 1, count = amount };
                filter.signal.Set(item);
                controlBehavior.filters.Add(filter);

                if (index >= goods.Count) {
                    break;
                }
            }

            if (last != null) {
                entity.Connect(last);
            }

            last = entity;
        }

        return ExportBlueprint(blueprint, copyToClipboard);
    }

    public static string ExportRequesterChests(string name, IReadOnlyList<(IObjectWithQuality<Item> item, int amount)> goods, EntityContainer chest, bool copyToClipboard = true) {
        if (chest.logisticSlotsCount <= 0) {
            throw new ArgumentException("Chest does not have logistic slots");
        }

        int combinatorCount = ((goods.Count - 1) / chest.logisticSlotsCount) + 1;
        int offset = -chest.size * combinatorCount / 2;
        BlueprintString blueprint = new BlueprintString(name);
        int index = 0;

        for (int i = 0; i < combinatorCount; i++) {
            BlueprintEntity entity = new BlueprintEntity { index = i + 1, position = { x = (i * chest.size) + offset, y = 0 }, name = chest.name };
            blueprint.blueprint.entities.Add(entity);

            var section = new BlueprintRequestFilterSection {
                index = 1,
            };

            entity.requestFilters = new BlueprintRequestFilterSections();
            entity.requestFilters.sections.Add(section);


            for (int j = 0; j < chest.logisticSlotsCount; j++) {
                var (item, amount) = goods[index++];
                BlueprintRequestFilter filter = new BlueprintRequestFilter { index = j + 1, count = amount, max_count = amount, name = item.target.name, quality = item.quality.name, comparator = "=" };
                section.filters.Add(filter);

                if (index >= goods.Count) {
                    break;
                }
            }
        }

        return ExportBlueprint(blueprint, copyToClipboard);
    }

    private class PlacedEntity {
        public RecipeRow Recipe { get; }
        public int X { get; } // Top-left X coordinate
        public int Y { get; } // Top-left Y coordinate

        public PlacedEntity(RecipeRow recipe, int x, int y) {
            Recipe = recipe;
            X = x;
            Y = y;
        }
    }

    private class LayoutResult {
        public int TotalWidth { get; set; }
        public int TotalHeight { get; set; }
        public List<PlacedEntity> Placements { get; set; } = new();
    }

    public static string ExportRecipiesAsBlueprint(string name, IEnumerable<RecipeRow> recipies, bool includeFuel, bool copyToClipboard = true) {
        // Sort buildings largest to smallest (by height then width) for better packing
        var entities = recipies
            .Where(r => r.entity is not null)
            .OrderByDescending(r => r.entity!.target.size)
            .ToList();

        // Estimate total area and try square-ish dimensions
        int totalArea = entities.Sum(r => (r.entity!.target.width + 1) * (r.entity!.target.height + 1));
        int sideLengthEstimate = (int)Math.Ceiling(Math.Sqrt(totalArea));

        // Try increasing row width until it fits all buildings efficiently
        int bestWidth = int.MaxValue;
        int bestHeight = int.MaxValue;
        LayoutResult? bestResult = null;

        for (int tryWidth = sideLengthEstimate; tryWidth < sideLengthEstimate * 2; tryWidth++) {
            int x = 0, y = 0, rowHeight = 0;
            List<PlacedEntity> placed = new();

            foreach (var b in entities) {
                if (x + b.entity!.target.width > tryWidth) {
                    // Move to next row
                    y += rowHeight + 1; // Add 1 for gap
                    x = 0;
                    rowHeight = 0;
                }

                placed.Add(new PlacedEntity(b, x, y));
                x += b.entity!.target.width + 1; // Add gap
                rowHeight = Math.Max(rowHeight, b.entity!.target.height);
            }

            int totalHeight = y + rowHeight;
            int totalWidth = tryWidth;

            // Evaluate how square-like it is
            if (Math.Max(totalWidth, totalHeight) < Math.Max(bestWidth, bestHeight)) {
                bestResult = new LayoutResult {
                    TotalWidth = totalWidth,
                    TotalHeight = totalHeight,
                    Placements = placed
                };
                bestWidth = totalWidth;
                bestHeight = totalHeight;
            }
        }

        BlueprintString blueprint = new BlueprintString(name);
        int buildingIndex = 0;

        foreach (var placement in bestResult!.Placements) {
            var recipe = placement.Recipe;

            BlueprintEntity entity = new BlueprintEntity {
                index = buildingIndex,
                position = { x = placement.X, y = placement.Y },
                name = recipe.entity!.target.name,
            };

            if (!recipe.recipe.Is<Mechanics>()) {
                entity.recipe = recipe.recipe.target.name;
                entity.recipe_quality = recipe.recipe.quality.name;
            }

            if (includeFuel && recipe.fuel is not null && !recipe.fuel.target.isPower) {
                entity.SetFuel(recipe.fuel.target.name, recipe.fuel.quality.name);
            }

            var modules = recipe.usedModules.modules;

            if (modules != null) {
                int idx = 0;
                foreach (var (module, count, beacon) in modules) {
                    if (!beacon) {
                        BlueprintItem item = new BlueprintItem {
                            item = module.target.name,
                            count = count,
                            id = { name = module.target.name, quality = module.quality.name }
                        };
                        item.items.inInventory.AddRange(Enumerable.Range(idx, count).Select(i => new BlueprintInventoryItem { inventory = recipe.entity.target.BlueprintModuleInventory, stack = i }));
                        entity.items.Add(item);
                        idx += count;
                    }
                }
            }

            blueprint.blueprint.entities.Add(entity);
            buildingIndex += 1;
        }

        return ExportBlueprint(blueprint, copyToClipboard);
    }
}
