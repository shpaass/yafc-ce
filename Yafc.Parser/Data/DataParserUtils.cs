using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Yafc.Parser;

internal static class DataParserUtils {
    private static class ConvertersFromLua<T> {
        public static Converter? convert;

        [return: NotNullIfNotNull(nameof(@default))]
        public delegate T Converter(object value, T @default);
    }

    static DataParserUtils() {
        ConvertersFromLua<int>.convert = (o, def) => o is long l ? (int)l : o is double d ? (int)d : o is string s && int.TryParse(s, out int res) ? res : def;
        ConvertersFromLua<int?>.convert = (o, def) => o is long l ? (int)l : o is double d ? (int)d : o is string s && int.TryParse(s, out int res) ? res : def;
        ConvertersFromLua<float>.convert = (o, def) => o is long l ? l : o is double d ? (float)d : o is string s && float.TryParse(s, out float res) ? res : def;
        ConvertersFromLua<float?>.convert = (o, def) => o is long l ? l : o is double d ? (float)d : o is string s && float.TryParse(s, out float res) ? res : def;
        ConvertersFromLua<bool>.convert = (o, def) => ConvertersFromLua<bool?>.convert!(o, def).Value;
        ConvertersFromLua<bool?>.convert = (o, def) => {

            if (o is bool b) {
                return b;
            }

            if (o == null) {
                return def;
            }

            if (o.Equals("true")) {
                return true;
            }

            if (o.Equals("false")) {
                return false;
            }

            return def;
        };
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

        result = converter(value, def);
        return true;
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
    public const string ResearchUnit = "research-unit";
    public const string SpoilRecipe = "spoil";
    public const string PlantRecipe = "plant";
    public const string AsteroidCapture = "asteroid-capture";
}
