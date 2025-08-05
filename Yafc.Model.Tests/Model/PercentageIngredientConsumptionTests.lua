data = {
  raw = {
    item = {
      ["iron-ore"] = {
        type = "item",
        name = "iron-ore",
        icon = "__base__/graphics/icons/iron-ore.png",
        icon_size = 64,
        subgroup = "raw-resource",
        order = "e[iron-ore]",
        stack_size = 50
      },
      ["iron-plate"] = {
        type = "item",
        name = "iron-plate",
        icon = "__base__/graphics/icons/iron-plate.png",
        icon_size = 64,
        subgroup = "raw-material",
        order = "b[iron-plate]",
        stack_size = 100
      },
      ["copper-ore"] = {
        type = "item",
        name = "copper-ore",
        icon = "__base__/graphics/icons/copper-ore.png",
        icon_size = 64,
        subgroup = "raw-resource",
        order = "f[copper-ore]",
        stack_size = 50
      },
      ["copper-plate"] = {
        type = "item",
        name = "copper-plate",
        icon = "__base__/graphics/icons/copper-plate.png",
        icon_size = 64,
        subgroup = "raw-material",
        order = "c[copper-plate]",
        stack_size = 100
      },
      ["steel-plate"] = {
        type = "item",
        name = "steel-plate",
        icon = "__base__/graphics/icons/steel-plate.png",
        icon_size = 64,
        subgroup = "raw-material",
        order = "d[steel-plate]",
        stack_size = 100
      },
      coal = {
        type = "item",
        name = "coal",
        icon = "__base__/graphics/icons/coal.png",
        icon_size = 64,
        fuel_category = "chemical",
        fuel_value = "4MJ",
        subgroup = "raw-resource",
        order = "b[coal]",
        stack_size = 50
      }
    },
    recipe = {
      ["iron-plate"] = {
        type = "recipe",
        name = "iron-plate",
        category = "smelting",
        energy_required = 3.2,
        ingredients = {{"iron-ore", 1}},
        result = "iron-plate"
      },
      ["copper-plate"] = {
        type = "recipe",
        name = "copper-plate",
        category = "smelting",
        energy_required = 3.2,
        ingredients = {{"copper-ore", 1}},
        result = "copper-plate"
      },
      ["steel-plate"] = {
        type = "recipe",
        name = "steel-plate",
        category = "smelting",
        energy_required = 16,
        ingredients = {{"iron-plate", 5}},
        result = "steel-plate"
      },
      ["mixed-recipe"] = {
        type = "recipe",
        name = "mixed-recipe",
        category = "crafting",
        energy_required = 5,
        ingredients = {
          {"iron-plate", 2},
          {"copper-plate", 1}
        },
        result = "steel-plate"
      }
    },
    furnace = {
      ["stone-furnace"] = {
        type = "furnace",
        name = "stone-furnace",
        icon = "__base__/graphics/icons/stone-furnace.png",
        icon_size = 64,
        flags = {"placeable-neutral", "placeable-player", "player-creation"},
        minable = {mining_time = 0.2, result = "stone-furnace"},
        max_health = 200,
        collision_box = {{-0.7, -0.7}, {0.7, 0.7}},
        selection_box = {{-0.8, -0.8}, {0.8, 0.8}},
        crafting_categories = {"smelting"},
        result_inventory_size = 1,
        energy_usage = "90kW",
        crafting_speed = 1,
        source_inventory_size = 1,
        energy_source = {
          type = "burner",
          fuel_category = "chemical",
          effectivity = 1,
          fuel_inventory_size = 1,
          emissions_per_minute = 2
        }
      }
    }
  }
}