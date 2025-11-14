if not mods["space-exploration"] then
    -- Unlock the initial science lab
    data.script_enabled:insert{
        type = "entity",
        name = "kr-spaceship-research-computer"
    }
end

return ...
