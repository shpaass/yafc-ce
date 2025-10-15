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

-- Remove the vanilla lab inputs, to remove the vanilla science packs from the default milestones
if data.raw.lab.lab then
    data.raw.lab.lab.inputs = {}
end

return ...;
