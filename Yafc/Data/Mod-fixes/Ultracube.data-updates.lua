-- Enable all technologies, reversing the disable-all Ultracube just did
for _, t in pairs(data.raw.technology) do
    t.enabled = true
end

-- Unlock the initial items
data.script_enabled:insert({
    type = "item",
    name = "cube-fabricator"
},
{
    type = "item",
    name = "cube-synthesizer"
},
{
    type = "item",
    name = "cube-ultradense-utility-cube"
},
-- And also two magic items that unlock the final science pack
{
    type= "item",
    name = "cube-qubits"
},
{
    type= "entity",
    name = "cube-quantum-decoder-dummy"
})

-- Remove the vanilla lab, to remove the vanilla science packs from the default milestones
data.raw.lab["lab"] = nil
data.raw.recipe["lab"] = nil
data.raw.item["lab"] = nil
if data.raw.technology["electronics"] then
    data.raw.technology["electronics"].effects = nil
end

-- For 1.1, update Technology.cube-construct-forbidden-ziggurat to a trigger tech instead.
if data.raw.technology["cube-construct-forbidden-ziggurat"].unit then
    data.raw.technology["cube-construct-forbidden-ziggurat"].unit = nil
    data.raw.technology["cube-construct-forbidden-ziggurat"].research_trigger = {
        entity = { name = "cube-forbidden-ziggurat" },
        type = "build-entity"
    }
end

return ...;
