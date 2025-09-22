-- This file is the first to run on fresh lua state, before all mod files
-- Should setup sandboxing and other data that factorio mods expect
-- "require", "raw_log", "mods" and "settings" are already set up here

local unsetGlobal = {"getfenv","load","loadfile","loadstring","setfenv","coroutine","module","package","io","os","newproxy"};

for i=1,#unsetGlobal do
	_G[unsetGlobal] = nil;
end

local parentTypes = {};

defines = ...; -- Yafc simulated require(Defines<version>) and stuck the result in `...`
for defineType,typeTable in pairs(defines.prototypes) do
	for subType,_ in pairs(typeTable) do
		if (defineType ~= subType) then
			parentTypes[subType] = defineType;
		end
	end
end	

require("__core__/lualib/dataloader.lua")

local raw_log = _G.raw_log;
_G.raw_log = nil;
function log(s)
	if type(s) ~= "string" then s = serpent.block(s) end;
	raw_log(s);
end

serpent = require("Serpent")

table_size = function(t)
	local count = 0
	for k,v in pairs(t) do
		count = count + 1
	end
	return count
end

if data then
	-- If data isn't set, we couldn't load __core__/lualib/dataloader, which means we're running tests. They replace the entire data table.
	data.data_crawler = "yafc "..yafc_version;
	log(data.data_crawler);
	data.script_enabled = {}
	function data.script_enabled:insert(entry, ...)
		if entry then
			if entry[1] then
				-- unpack an array argument
				for _, e in pairs(entry) do
					table.insert(self, e)
				end
			else
				-- insert a non-array argument
				table.insert(self, entry)
			end
			-- continue to the next argument
			self:insert(...)
		end
	end
end

size=32;
