## Script execution

Mod-specific fixes go in this folder.
The file name format is _modname.path.to.modfile_.lua

Scripts are executed after `require("__modname__.path.to.modfile")`.
`...` (varargs) will contain the return from the normal call to `require("")`.
The script is expected to return a patched require result.

## YAFC-specific data
### Script-generated objects

`data.script_enabled` is initially-empty sequence of tables that can be modified by these mod-fix scripts, or by mods themselves.
Each table in `data.script_enabled` should contain `type` and `name` keys with string values.
The object identified by the type and name (e.g. `{ type = "item", name = "carbonic-asteroid-chunk" }`) will be treated as an accessibility root, similar to nauvis or the character.
The primary use for this table is to specify entities that are placed on the map via scripts, and starting items.

`data.script_enabled:insert` accepts any number of array-or-entry arguments, and will insert all the supplied entries into `script_enabled`.
For example, the first three calls all insert three items, but the last will insert coal and one invalid (ignored) entry:
```lua
data.script_enabled:insert({ type = "item", name = "iron-ore" }, { type = "item", name = "copper-ore" }, { type = "item", name = "coal" })
data.script_enabled:insert({{ type = "item", name = "iron-ore" }, { type = "item", name = "copper-ore" }}, { type = "item", name = "coal" })
data.script_enabled:insert({{ type = "item", name = "iron-ore" }, { type = "item", name = "copper-ore" }, { type = "item", name = "coal" }})
-- Don't do this, though:
data.script_enabled:insert({{{ type = "item", name = "iron-ore" }, { type = "item", name = "copper-ore" }}, { type = "item", name = "coal" }})
```

## Example of a valid mod fix
<filename: core.lualib.util.lua>
```lua
local util = ...;
log("Captured it!");
return util;
```
