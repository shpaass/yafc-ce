-- This file is run after all mods are loaded, to translate some data from 1.1 to 2.0 formats.
-- Other data (e.g. recipe ingredients/products) is loaded version-agnostically.

-- Create spawn location data
local settings = {}
data.raw.planet = {
	nauvis = {
		name = "nauvis",
		type = "planet",
		map_gen_settings = {
			autoplace_settings = {
				entity = {
					settings = settings
				},
				tile = {
					settings = {
						water = 0
					}
				}
			}
		}
	}
}
for key, _ in pairs(defines.prototypes.entity) do
	for _, entity in pairs(data.raw[key]) do
		if entity.autoplace then
			settings[entity.name] = 0
		end
	end
end
data.raw.tile.water.fluid = "water"

-- Convert 1.1 module usage information (modules have a list of recipes) into 2.0 format (recipes have a list of categories):
-- Each module is assigned to a different category, so the list of categories is also a list of modules.
local addModule = function(recipe, module)
	table.insert(recipe.allowed_module_categories, module.name)
	if module.effect.productivity and module.effect.productivity.bonus > 0 then
		-- allow_productivity is default-false, so set it explicitly if this module has a productivity bonus
		recipe.allow_productivity = true
	end
end

for _, recipe in pairs(data.raw.recipe) do
	recipe.allowed_module_categories = {}
end

for _, module in pairs(data.raw.module) do
	module.category = module.name

	if module.limitation_blacklist then
		for _, recipe in pairs(data.raw.recipe) do
			local allowed = true
			for _, forbidden in ipairs(module.limitation_blacklist) do
				if forbidden == recipe.name then
					allowed = false
					break
				end
			end
			if allowed then
				addModule(recipe, module)
			end
		end
	elseif module.limitation then
		for _, allowed in ipairs(module.limitation) do
			addModule(data.raw.recipe[allowed], module)
		end
	else
		for _, recipe in pairs(data.raw.recipe) do
			addModule(recipe, module)
		end
	end
end
