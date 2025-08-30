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
