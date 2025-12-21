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
serpent = require("Serpent")

local raw_log = _G.raw_log;
_G.raw_log = nil;
function log(s)
	if type(s) ~= "string" then s = serpent.block(s) end;
	raw_log(s);
end

local raw_getinfo = debug.getinfo
-- Tests:
-- 1: Ensure all of the following have the same result both before and after this function declaration:
--log(debug.getinfo(0))
--log(debug.getinfo(1))
--log(debug.getinfo(debug.getinfo))
--
-- 2: Ensure mod logs using source and/or short_src (e.g. the error messages in K2 2.0.13) are identical in factorio-current.log and yafc*.log.
--
-- 3: Ensure this method fixes source and short_src for this call, and leaves the rest of the result unchanged.
--log(debug.getinfo(log))
--
-- Consider this to display the output of the two most recent log(debug.getinfo(...)) calls:
-- watch -w "grep -h currentline yafc*.log | tail -n2 | sed -e s/RenderedMessage.*// -e s/\\\\\\\\n/\\\\n/g -e \"s/\\\\\\\\\\\\(.\\\\)/\\\\1/g\""
--
function debug.getinfo(thread_or_f, f_or_what, what)
	-- If the caller wants info about getinfo (by setting f to either 0 or debug.getinfo), return that information.
	if thread_or_f == debug.getinfo then
		return raw_getinfo(raw_getinfo, f_or_what)
	elseif f_or_what == debug.getinfo then
		return raw_getinfo(thread_or_f, raw_getinfo, what)
	elseif thread_or_f == 0 or f_or_what == 0 then
		-- raw_getinfo(1) will return the name info for this call. (name info isn't available when f is a function.)
		-- Combine that with the method info for the real debug.getinfo method (from passing f == 0):
		local result, name = raw_getinfo(thread_or_f, f_or_what, what), raw_getinfo(1)
		result.name = name.name
		result.namewhat = name.namewhat
		return result
	end

	-- Otherwise, add 1 to f if it's a number, to hide this method from the stack.
	if type(thread_or_f) == "number" then thread_or_f = thread_or_f + 1
	elseif type(f_or_what) == "number" then f_or_what = f_or_what + 1 end

	local result = raw_getinfo(thread_or_f, f_or_what, what)
	-- Do the actual source fixups we're here for
	if result then
		result.short_src, result.source = yafc_sourcefixups(result.short_src, result.source)
	end
	return result
end

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
