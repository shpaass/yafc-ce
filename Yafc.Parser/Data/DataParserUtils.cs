using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Yafc.Parser;

internal static class DataParserUtils {
    private static class ConvertersFromLua<T> {
        public static Converter? convert;

        public delegate bool Converter(object value, T @default, [NotNullIfNotNull(nameof(@default))] out T result);
    }

    static DataParserUtils() {
        ConvertersFromLua<int?>.convert = (object value, int? def, out int? result) => {
            if (value is long l) {
                result = (int)l;
                return true;
            }
            if (value is double d) {
                result = (int)d;
                return true;
            }
            if (value is string s && int.TryParse(s, out int r)) {
                result = r;
                return true;
            }
            result = def;
            return false;
        };
        ConvertersFromLua<float?>.convert = (object value, float? def, out float? result) => {
            if (value is long l) {
                result = l;
                return true;
            }
            if (value is double d) {
                result = (float)d;
                return true;
            }
            if (value is string s && float.TryParse(s, out float r)) {
                result = r;
                return true;
            }
            result = def;
            return false;
        };
        ConvertersFromLua<bool?>.convert = (object value, bool? def, out bool? result) => {
            if (value is bool b) {
                result = b;
                return true;
            }
            if (value.Equals("true")) {
                result = true;
                return true;
            }
            if (value.Equals("false")) {
                result = false;
                return true;
            }
            result = def;
            return false;
        };

        ConvertersFromLua<int>.convert = convertFromNullable;
        ConvertersFromLua<float>.convert = convertFromNullable;
        ConvertersFromLua<bool>.convert = convertFromNullable;

        static bool convertFromNullable<T>(object value, T def, out T result) where T : struct {
            bool ret = ConvertersFromLua<T?>.convert!(value, def, out T? nullable);
            result = nullable.Value; // value and def are not null, so nullable is not null either.
            return ret;
        }
    }

    private static bool Parse<T>(object? value, out T result, T def) {
        if (value == null) {
            result = def;
            return false;
        }

        if (value is T t) {
            result = t;
            return true;
        }
        var converter = ConvertersFromLua<T>.convert;
        if (converter == null) {
            result = def;
            return false;
        }

        return converter(value, def, out result);
    }

    private static bool Parse<T>(object? value, [MaybeNullWhen(false)] out T result) => Parse(value, out result, default!); // null-forgiving: The three-argument Parse takes a non-null default to guarantee a non-null result. We don't make that guarantee.

    public static bool Get<T>(this LuaTable? table, string key, out T result, T def) => Parse(table?[key], out result, def);

    public static bool Get<T>(this LuaTable? table, int key, out T result, T def) => Parse(table?[key], out result, def);

    public static bool Get<T>(this LuaTable? table, string key, [NotNullWhen(true)] out T? result) => Parse(table?[key], out result);

    public static bool Get<T>(this LuaTable? table, int key, [NotNullWhen(true)] out T? result) => Parse(table?[key], out result);

    public static T Get<T>(this LuaTable? table, string key, T def) {
        _ = Parse(table?[key], out var result, def);
        return result;
    }

    public static T Get<T>(this LuaTable? table, int key, T def) {
        _ = Parse(table?[key], out var result, def);
        return result;
    }

    public static T? Get<T>(this LuaTable? table, string key) {
        _ = Parse(table?[key], out T? result);
        return result;
    }

    public static T? Get<T>(this LuaTable? table, int key) {
        _ = Parse(table?[key], out T? result);
        return result;
    }

    public static IEnumerable<T> ArrayElements<T>(this LuaTable? table) => table?.ArrayElements.OfType<T>() ?? [];

    /// <summary>
    /// Reads a <see cref="LuaTable"/> that has the format "Thing or array[Thing]", and calls <paramref name="action"/> for each Thing in the array,
    /// or for the passed Thing, as appropriate.
    /// </summary>
    /// <param name="table">A <see cref="LuaTable"/> that might be either an object or an array of objects.</param>
    /// <param name="action">The action to perform on each object in <paramref name="table"/>.</param>
    public static void ReadObjectOrArray(this LuaTable table, Action<LuaTable> action) {
        if (table.ArrayElements.Count > 0) {
            foreach (LuaTable entry in table.ArrayElements.OfType<LuaTable>()) {
                action(entry);
            }
        }
        else {
            action(table);
        }
    }
}

public static class SpecialNames {
    public const string BurnableFluid = "burnable-fluid.";
    public const string Heat = "heat";
    public const string Void = "void";
    public const string Electricity = "electricity";
    public const string HotFluid = "hot-fluid";
    public const string SpecificFluid = "fluid.";
    public const string MiningRecipe = "mining.";
    public const string BoilerRecipe = "boiler.";
    public const string FakeRecipe = "fake-recipe";
    public const string FixedRecipe = "fixed-recipe.";
    public const string GeneratorRecipe = "generator";
    public const string PumpingRecipe = "pump.";
    public const string Labs = "labs.";
    public const string TechnologyTrigger = "technology-trigger";
    public const string RocketLaunch = "launch";
    public const string RocketCraft = "rocket.";
    public const string ReactorRecipe = "reactor";
    public const string SpoilRecipe = "spoil";
    public const string PlantRecipe = "plant";
    public const string AsteroidCapture = "asteroid-capture";
}
