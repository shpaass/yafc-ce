﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Serilog;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Yafc.Model.Tests")]

namespace Yafc.Parser;

public class LuaException(string luaMessage) : Exception(luaMessage) {
}
internal partial class LuaContext : IDisposable {
    private enum Result {
        LUA_OK = 0,
        LUA_YIELD = 1,
        LUA_ERRRUN = 2,
        LUA_ERRSYNTAX = 3,
        LUA_ERRMEM = 4,
        LUA_ERRGCMM = 5,
        LUA_ERRERR = 6
    }

    private enum Type {
        LUA_TNONE = -1,
        LUA_TNIL = 0,
        LUA_TBOOLEAN = 1,
        LUA_TLIGHTUSERDATA = 2,
        LUA_TNUMBER = 3,
        LUA_TSTRING = 4,
        LUA_TTABLE = 5,
        LUA_TFUNCTION = 6,
        LUA_TUSERDATA = 7,
        LUA_TTHREAD = 8,
    }

    private const int REGISTRY = -1001000;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LuaCFunction(IntPtr lua);
    private const string LUA = "lua52";

    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial IntPtr luaL_newstate();
    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial IntPtr luaL_openlibs(IntPtr state);
    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void lua_close(IntPtr state);

    [LibraryImport(LUA, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new System.Type[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static partial Result luaL_loadbufferx(IntPtr state, in byte buf, IntPtr sz, string name, string? mode);
    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial Result lua_pcallk(IntPtr state, int nargs, int nresults, int msgh, IntPtr ctx, IntPtr k);
    [LibraryImport(LUA, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new System.Type[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static partial void luaL_traceback(IntPtr state, IntPtr state2, string? msg, int level);

    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial Type lua_type(IntPtr state, int idx);
    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = new System.Type[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static partial IntPtr lua_tolstring(IntPtr state, int idx, out IntPtr len);
    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial int lua_toboolean(IntPtr state, int idx);
    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = new System.Type[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static partial double lua_tonumberx(IntPtr state, int idx, [MarshalAs(UnmanagedType.Bool)] out bool isnum);

    [LibraryImport(LUA, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new System.Type[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static partial int lua_getglobal(IntPtr state, string var);
    [LibraryImport(LUA, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new System.Type[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static partial void lua_setglobal(IntPtr state, string name);
    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial int luaL_ref(IntPtr state, int t);
    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial int lua_checkstack(IntPtr state, int n);

    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void lua_pushnil(IntPtr state);
    [LibraryImport(LUA, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new System.Type[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static partial IntPtr lua_pushstring(IntPtr state, string s);
    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void lua_pushnumber(IntPtr state, double d);
    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void lua_pushboolean(IntPtr state, int b);
    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void lua_pushcclosure(IntPtr state, IntPtr callback, int n);
    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void lua_createtable(IntPtr state, int narr, int nrec);

    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void lua_rawgeti(IntPtr state, int idx, long n);
    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void lua_rawget(IntPtr state, int idx);
    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void lua_rawset(IntPtr state, int idx);
    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void lua_rawseti(IntPtr state, int idx, long n);
    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void lua_len(IntPtr state, int index);
    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial int lua_next(IntPtr state, int idx);

    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial int lua_gettop(IntPtr state);
    [LibraryImport(LUA)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void lua_settop(IntPtr state, int idx);

    private IntPtr L;
    private readonly int tracebackReg;
    private readonly List<(string mod, string name)> fullChunkNames = [];
    private readonly Dictionary<(string mod, string name), int> required = [];
    private readonly Dictionary<(string mod, string name), byte[]> modFixes = [];

    private static readonly ILogger logger = Logging.GetLogger<LuaContext>();
    private string currentfile;
    public LuaContext(Version gameVersion) {
        L = luaL_newstate();
        _ = luaL_openlibs(L);

        var helpers = NewTable();
        helpers["game_version"] = gameVersion.ToString(3);
        SetGlobal("helpers", helpers);

        RegisterApi(Log, "raw_log");
        RegisterApi(Require, "require");
        RegisterApi(DebugTraceback, "debug", "traceback");
        RegisterApi(DebugGetinfo, "debug", "getinfo");
        RegisterApi(CompareVersions, "helpers", "compare_versions");
        currentfile = "__no__/file";
        _ = lua_pushstring(L, Project.currentYafcVersion.ToString());
        lua_setglobal(L, "yafc_version");
        var mods = NewTable();

        foreach (var mod in FactorioDataSource.allMods) {
            mods[mod.Key] = mod.Value.version;
        }

        SetGlobal("mods", mods);

        var featureFlags = NewTable();
        featureFlags["quality"] = FactorioDataSource.allMods.Any(x => x.Value.quality_required);
        featureFlags["rail_bridges"] = FactorioDataSource.allMods.Any(x => x.Value.rail_bridges_required);
        featureFlags["space_travel"] = FactorioDataSource.allMods.Any(x => x.Value.space_travel_required);
        featureFlags["spoiling"] = FactorioDataSource.allMods.Any(x => x.Value.spoiling_required);
        featureFlags["freezing"] = FactorioDataSource.allMods.Any(x => x.Value.freezing_required);
        featureFlags["segmented_units"] = FactorioDataSource.allMods.Any(x => x.Value.segmented_units_required);
        featureFlags["expansion_shaders"] = FactorioDataSource.allMods.Any(x => x.Value.expansion_shaders_required);

        SetGlobal("feature_flags", featureFlags);

        LuaCFunction traceback = CreateErrorTraceback;
        neverCollect.Add(traceback);
        lua_pushcclosure(L, Marshal.GetFunctionPointerForDelegate(traceback), 0);
        tracebackReg = luaL_ref(L, REGISTRY);

        if (Directory.Exists("Data/Mod-fixes/")) {
            foreach (string file in Directory.EnumerateFiles("Data/Mod-fixes/", "*.lua")) {
                string fileName = Path.GetFileName(file);
                string[] modAndFile = fileName.Split('.');
                string assemble = string.Join('/', modAndFile.Skip(1).SkipLast(1));
                modFixes[(modAndFile[0], assemble + ".lua")] = File.ReadAllBytes(file);
            }
        }
    }

    private static int ParseTracebackEntry(string s, out int endOfName) {
        endOfName = 0;

        if (s.StartsWith("[string \"", StringComparison.Ordinal)) {
            int endOfNum = s.IndexOf(' ', 9);
            endOfName = s.IndexOf("\"]:", 9, StringComparison.Ordinal) + 2;

            if (endOfNum >= 0 && endOfName >= 0) {
                return int.Parse(s[9..endOfNum]);
            }
        }

        return -1;
    }

    private string ReplaceChunkIdsInTraceback(string rawTraceback) {
        string[] split = rawTraceback.Split("\n\t");

        for (int i = 0; i < split.Length; i++) {
            int chunkId = ParseTracebackEntry(split[i], out int endOfName);

            if (chunkId >= 0) {
                (string mod, string name) = fullChunkNames[chunkId];
                split[i] = $"__{mod}__/{name}{split[i][endOfName..]}";
            }
        }

        string traceBack = string.Join("\n\t", split);
        return traceBack;
    }

    private int CreateErrorTraceback(IntPtr lua) {
        string? message = GetString(1);
        luaL_traceback(L, L, message, 0);
        string rawTraceback = GetString(-1)!; // null-forgiving: luaL_traceback always pushes a string
        string traceback = ReplaceChunkIdsInTraceback(rawTraceback);
        _ = lua_pushstring(L, traceback);
        return 1;
    }

    private int DebugTraceback(IntPtr lua) {
        luaL_traceback(L, L, null, 0);
        string rawTraceback = GetString(-1)!; // null-forgiving: luaL_traceback always pushes a string
        string traceback = ReplaceChunkIdsInTraceback(rawTraceback);
        _ = lua_pushstring(L, traceback);
        return 1;
    }

    private int DebugGetinfo(IntPtr lua) {
        LuaTable outdata = NewTable();
        outdata["short_src"] = currentfile;
        PushManagedObject(outdata);
        return 1;
    }

    private int CompareVersions(IntPtr lua) {
        string? string1 = GetString(1);
        string? string2 = GetString(2);
        if (Version.TryParse(string1, out Version? version1) && Version.TryParse(string2, out Version? version2)) {
            lua_pushnumber(lua, version1.CompareTo(version2));
        }
        else {
            // Got something that wasn't a version for one or both arguments?
            lua_pushnumber(lua, string.Compare(string1, string2));
        }
        return 1;
    }

    private int Log(IntPtr lua) {
        logger.Information(GetString(1) ?? "A lua script attempted to log a {LuaType} value.", lua_type(lua, 1));
        return 0;
    }
    private void GetReg(int refId) => lua_rawgeti(L, REGISTRY, refId);

    private void Pop(int popc) => lua_settop(L, lua_gettop(L) - popc);

    public List<object?> ArrayElements(int refId) {
        ObjectDisposedException.ThrowIf(L == IntPtr.Zero, this);
        GetReg(refId); // 1
        lua_pushnil(L);
        List<object?> list = [];

        while (lua_next(L, -2) != 0) {
            object? value = PopManagedValue(1);
            object? key = PopManagedValue(0);

            if (key is double) {
                list.Add(value);
            }
            else {
                break;
            }
        }

        Pop(1);

        return list;
    }

    public Dictionary<object, object?> ObjectElements(int refId) {
        ObjectDisposedException.ThrowIf(L == IntPtr.Zero, this);
        GetReg(refId); // 1
        lua_pushnil(L);
        Dictionary<object, object?> dict = [];

        while (lua_next(L, -2) != 0) {
            object? value = PopManagedValue(1);
            object? key = PopManagedValue(0);
            if (key != null) {
                dict[key] = value;
            }
        }

        Pop(1);

        return dict;
    }

    public LuaTable NewTable() {
        ObjectDisposedException.ThrowIf(L == IntPtr.Zero, this);
        lua_createtable(L, 0, 0);
        return new LuaTable(this, luaL_ref(L, REGISTRY));
    }

    public object? GetGlobal(string name) {
        ObjectDisposedException.ThrowIf(L == IntPtr.Zero, this);
        _ = lua_getglobal(L, name); // 1
        return PopManagedValue(1);
    }

    public void SetGlobal(string name, object value) {
        ObjectDisposedException.ThrowIf(L == IntPtr.Zero, this);
        PushManagedObject(value);
        lua_setglobal(L, name);
    }
    public object? GetValue(int refId, int idx) {
        ObjectDisposedException.ThrowIf(L == IntPtr.Zero, this);
        GetReg(refId); // 1
        lua_rawgeti(L, -1, idx); // 2
        return PopManagedValue(2);
    }

    public object? GetValue(int refId, string idx) {
        ObjectDisposedException.ThrowIf(L == IntPtr.Zero, this);
        GetReg(refId); // 1
        _ = lua_pushstring(L, idx); // 2
        lua_rawget(L, -2); // 2 (pop & push)
        return PopManagedValue(2);
    }

    private object? PopManagedValue(int popc) {
        object? result = null;

        switch (lua_type(L, -1)) {
            case Type.LUA_TBOOLEAN:
                result = lua_toboolean(L, -1) != 0;
                break;
            case Type.LUA_TNUMBER:
                result = lua_tonumberx(L, -1, out _);
                break;
            case Type.LUA_TSTRING:
                result = GetString(-1);
                break;
            case Type.LUA_TTABLE:
                int refId = luaL_ref(L, REGISTRY);
                LuaTable table = new LuaTable(this, refId);

                if (popc == 0) {
                    GetReg(table.refId);
                }
                else {
                    popc--;
                }

                result = table;
                break;
        }
        if (popc > 0) {
            Pop(popc);
        }

        return result;
    }

    private void PushManagedObject(object? value) {
        if (value is double d) {
            lua_pushnumber(L, d);
        }
        else if (value is int i) {
            lua_pushnumber(L, i);
        }
        else if (value is long l) {
            lua_pushnumber(L, l);
        }
        else if (value is ulong ul) {
            lua_pushnumber(L, ul);
        }
        else if (value is string s) {
            _ = lua_pushstring(L, s);
        }
        else if (value is LuaTable t) {
            GetReg(t.refId);
        }
        else if (value is bool b) {
            lua_pushboolean(L, b ? 1 : 0);
        }
        else {
            lua_pushnil(L);
        }
    }

    public void SetValue(int refId, string idx, object? value) {
        ObjectDisposedException.ThrowIf(L == IntPtr.Zero, this);
        GetReg(refId); // 1;
        _ = lua_pushstring(L, idx); // 2
        PushManagedObject(value); // 3;
        lua_rawset(L, -3);
        Pop(3);
    }

    public void SetValue(int refId, int idx, object? value) {
        ObjectDisposedException.ThrowIf(L == IntPtr.Zero, this);
        GetReg(refId); // 1;
        PushManagedObject(value); // 2;
        lua_rawseti(L, -2, idx);
        Pop(2);
    }

    private static string GetDirectoryName(string s) {
        int lastSlash = s.LastIndexOf('/');
        return lastSlash >= 0 ? s[..(lastSlash + 1)] : "";
    }

    private int Require(IntPtr lua) {
        string file = GetString(1) ?? throw new NotSupportedException("Cannot require(nil)");

        string argument = file;

        if (file.Contains("..")) {
            throw new NotSupportedException("Attempt to traverse to parent directory");
        }

        if (file.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) {
            file = file[..^4];
        }

        file = file.Replace('\\', '/');
        string originalFile = file;
        file = file.Replace('.', '/');
        string fileExt = file + ".lua";
        Pop(1);
        luaL_traceback(L, L, null, 1); //2
        // TODO how to determine where to start require search? Parsing lua traceback output for now
        string tracebackS = GetString(-1)!; // null-forgiving: luaL_traceback always pushes a string
        string[] tracebackVal = tracebackS.Split("\n\t");
        int traceId = -1;

        foreach (string traceLine in tracebackVal) // TODO slightly hacky
        {
            traceId = ParseTracebackEntry(traceLine, out _);

            if (traceId >= 0) {
                break;
            }
        }
        var (mod, source) = fullChunkNames[traceId];
        (string mod, string path) requiredFile = (mod, fileExt);

        if (file.StartsWith("__")) {
            requiredFile = FactorioDataSource.ResolveModPath(mod, originalFile, true);
        }
        else if (mod == "*") {
            byte[] localFile = File.ReadAllBytes("Data/" + fileExt);
            int result = Exec(localFile, "*", file);
            GetReg(result);
            return 1;
        }
        // TODO: Does Factorio's require check intermediate directories, in addition to current and mod-root?
        else if (FactorioDataSource.ModPathExists(requiredFile.mod, GetDirectoryName(source) + fileExt)) {
            requiredFile.path = GetDirectoryName(source) + fileExt;
        }
        else if (FactorioDataSource.ModPathExists(requiredFile.mod, fileExt)) { }
        else if (FactorioDataSource.ModPathExists("core", "lualib/" + fileExt)) {
            requiredFile.mod = "core";
            requiredFile.path = "lualib/" + fileExt;
        }
        else { // Just find anything ffs
            foreach (string path in FactorioDataSource.GetAllModFiles(requiredFile.mod, GetDirectoryName(source))) {
                if (path.EndsWith(fileExt, StringComparison.OrdinalIgnoreCase)) {
                    requiredFile.path = path;
                    break;
                }
            }
        }

        if (required.TryGetValue(requiredFile, out int value)) {
            GetReg(value);
            return 1;
        }

        logger.Information("Require {RequiredFile}", requiredFile.mod + "/" + requiredFile.path);
        currentfile = "__" + requiredFile.mod + "__/" + requiredFile.path;
        byte[] bytes = FactorioDataSource.ReadModFile(requiredFile.mod, requiredFile.path);

        if (bytes != null) {
            _ = lua_pushstring(L, argument);
            int argumentReg = luaL_ref(L, REGISTRY);
            int result = Exec(bytes, requiredFile.mod, requiredFile.path, argumentReg);
            result = RunModFix(requiredFile.mod, requiredFile.path, result);

            required[requiredFile] = result;
            GetReg(result);
        }
        else {
            logger.Error("LUA require failed: mod {mod}, file {file}", mod, file);
            lua_pushnil(L);
        }
        return 1;
    }

    private int RunModFix(string mod, string path, int result = 0) {
        if (modFixes.TryGetValue((mod, path), out byte[]? fix)) {
            string modFixName = $"mod-fix-{mod}.{path}";
            logger.Information("Running mod-fix {ModFix}", modFixName);
            result = Exec(fix, "*", modFixName, result);
        }

        return result;
    }

    protected readonly List<object> neverCollect = []; // references callbacks that could be called from native code to not be garbage collected
    private void RegisterApi(LuaCFunction callback, string name) {
        neverCollect.Add(callback);
        lua_pushcclosure(L, Marshal.GetFunctionPointerForDelegate(callback), 0);
        lua_setglobal(L, name);
    }

    /// <summary>
    /// Stores a C# method that will be called when scripts attempt to call <c>_G[<paramref name="topLevel"/>][<paramref name="name"/>]</c>.
    /// </summary>
    /// <param name="callback">The C# method to call when scripts call the specified function.</param>
    /// <param name="topLevel">The name of the global table that will receive <paramref name="callback"/>. This table may be empty,
    /// but it must already exist.</param>
    /// <param name="name">The table key that will receive <paramref name="callback"/>.</param>
    private void RegisterApi(LuaCFunction callback, string topLevel, string name) {
        neverCollect.Add(callback);
        _ = lua_getglobal(L, topLevel);
        _ = lua_pushstring(L, name);
        lua_pushcclosure(L, Marshal.GetFunctionPointerForDelegate(callback), 0);
        lua_rawset(L, -3);
        Pop(1);
    }

    private string? GetString(int index) {
        nint ptr = lua_tolstring(L, index, out nint len);
        if (ptr == IntPtr.Zero) {
            return null;
        }
        byte[] buf = new byte[(int)len];
        Marshal.Copy(ptr, buf, 0, buf.Length);

        return Encoding.UTF8.GetString(buf);
    }

    public int Exec(ReadOnlySpan<byte> chunk, string mod, string name, int argument = 0) {
        ObjectDisposedException.ThrowIf(L == IntPtr.Zero, this);
        // since lua cuts file name to a few dozen symbols, add index to start of every name
        fullChunkNames.Add((mod, name));
        name = fullChunkNames.Count - 1 + " " + name;
        GetReg(tracebackReg);
        chunk = chunk.CleanupBom();

        var result = luaL_loadbufferx(L, in chunk.GetPinnableReference(), chunk.Length, name, null);

        if (result != Result.LUA_OK) {
            throw new LuaException("Loading terminated with code " + result + "\n" + GetString(-1));
        }

        int argcount = 0;

        if (argument > 0) {
            GetReg(argument);
            argcount = 1;
        }

        result = lua_pcallk(L, argcount, 1, -2 - argcount, IntPtr.Zero, IntPtr.Zero);

        if (result != Result.LUA_OK) {
            if (result == Result.LUA_ERRRUN) {
                throw new LuaException(GetString(-1)!); // null-forgiving: lua_pcallk always pushes a string on error
            }

            throw new LuaException("Execution " + mod + "/" + name + " terminated with code " + result + "\n" + GetString(-1));
        }
        return luaL_ref(L, REGISTRY);
    }

    public void Dispose() {
        lua_close(L);
        L = IntPtr.Zero;
    }

    public void DoModFiles(string[] modorder, string fileName, IProgress<(string, string)> progress) {
        string header = LSs.ProgressExecutingModAtDataStage.L(fileName);

        foreach (string mod in modorder) {
            required.Clear();
            FactorioDataSource.CurrentLoadingMod = mod;
            progress.Report((header, mod));
            byte[] bytes = FactorioDataSource.ReadModFile(mod, fileName);

            if (bytes == null) {
                continue;
            }

            logger.Information("Executing file {Filename}", mod + "/" + fileName);
            currentfile = "__" + mod + "__/" + fileName;
            _ = Exec(bytes, mod, fileName);
            RunModFix(mod, fileName);
        }
    }

    public LuaTable data => (LuaTable)GetGlobal("data")!;
    public LuaTable defines => (LuaTable)GetGlobal("defines")!;
}

internal class LuaTable : ILocalizable {
    public readonly LuaContext context;
    public readonly int refId;

    internal LuaTable(LuaContext context, int refId) {
        this.context = context;
        this.refId = refId;
    }

    public object? this[int index] {
        get => context.GetValue(refId, index);
        set => context.SetValue(refId, index, value);
    }
    public object? this[string index] {
        get => context.GetValue(refId, index);
        set => context.SetValue(refId, index, value);
    }

    public List<object?> ArrayElements => context.ArrayElements(refId);
    public Dictionary<object, object?> ObjectElements => context.ObjectElements(refId);

    bool ILocalizable.Get([NotNullWhen(true)] out string? key, out object[] parameters) {
        parameters = ArrayElements.Skip(1).ToArray()!;
        return this.Get(1, out key);
    }
}
