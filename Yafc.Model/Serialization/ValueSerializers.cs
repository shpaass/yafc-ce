using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Yafc.I18n;

namespace Yafc.Model;

internal static class ValueSerializer {
    public static bool IsValueSerializerSupported(Type type) {
        // Types listed in this method must have a corresponding ValueSerializer<T> returned from CreateValueSerializer.

        if (type == typeof(int) || type == typeof(float) || type == typeof(bool) || type == typeof(ulong) || type == typeof(string)
            || type == typeof(Type) || type == typeof(Guid) || type == typeof(PageReference)) {

            return true;
        }

        if (typeof(FactorioObject).IsAssignableFrom(type)) {
            return true;
        }

        if (type.IsEnum && type.GetEnumUnderlyingType() == typeof(int)) {
            return true;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IObjectWithQuality<>)) {
            return true;
        }

        if (type.IsClass && !type.IsAbstract && (typeof(ModelObject).IsAssignableFrom(type) || type.GetCustomAttribute<SerializableAttribute>() != null)) {
            return true;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>)) {
            return IsValueSerializerSupported(type.GetGenericArguments()[0]);
        }

        return false;
    }

    public static bool IsKeySerializerSupported(Type type) {
        // Types listed in this method must have a ValueSerializer<T> where GetJsonProperty is overridden.

        if (type == typeof(string) || type == typeof(Type) || type == typeof(Guid)) {
            return true;
        }

        if (typeof(FactorioObject).IsAssignableFrom(type)) {
            return true;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IObjectWithQuality<>)) {
            return true;
        }

        return false;
    }
}

/// <summary>
/// The base class for serializing values that are [<see cref="SerializableAttribute">Serializable</see>], native (e.g. <see langword="int"/>,
/// <see langword="float"/>), or native-like (e.g. <see cref="PageReference"/>).
/// </summary>
/// <typeparam name="T">The type to be serialized/deserialized by this instance.</typeparam>
internal abstract class ValueSerializer<T> {
    /// <summary>
    /// Contains the serializer that should be used for <typeparamref name="T"/>.
    /// </summary>
    public static readonly ValueSerializer<T> Default = (ValueSerializer<T>)CreateValueSerializer();

    private static object CreateValueSerializer() {
        // Types listed in this method must also return true from IsValueSerializerSupported.

        if (typeof(T) == typeof(int)) {
            return new IntSerializer();
        }

        if (typeof(T) == typeof(float)) {
            return new FloatSerializer();
        }

        if (typeof(T) == typeof(bool)) {
            return new BoolSerializer();
        }

        if (typeof(T) == typeof(ulong)) {
            return new ULongSerializer();
        }

        if (typeof(T) == typeof(string)) {
            return new StringSerializer();
        }

        if (typeof(T) == typeof(Type)) {
            return new TypeSerializer();
        }

        if (typeof(T) == typeof(Guid)) {
            return new GuidSerializer();
        }

        if (typeof(T) == typeof(PageReference)) {
            return new PageReferenceSerializer();
        }

        // null-forgiving: Activator.CreateInstance only returns null for Nullable<T>.
        // See System.Private.CoreLib\src\System\Activator.cs:20, e.g. https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Activator.cs#L20

        if (typeof(FactorioObject).IsAssignableFrom(typeof(T))) {
            // `return new FactorioObjectSerializer<T>();`, but in a format that works with the limitations of C#.
            // The following blocks also create newly-constructed values with more restrictive constraints on T.
            return Activator.CreateInstance(typeof(FactorioObjectSerializer<>).MakeGenericType(typeof(T)))!;
        }

        if (typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(IObjectWithQuality<>)) {
            return Activator.CreateInstance(typeof(QualityObjectSerializer<>).MakeGenericType(typeof(T).GenericTypeArguments[0]))!;
        }

        if (typeof(T).IsEnum && typeof(T).GetEnumUnderlyingType() == typeof(int)) {
            return Activator.CreateInstance(typeof(EnumSerializer<>).MakeGenericType(typeof(T)))!;
        }

        if (typeof(T).IsClass && !typeof(T).IsAbstract) {
            if (typeof(ModelObject).IsAssignableFrom(typeof(T))) {
                return Activator.CreateInstance(typeof(ModelObjectSerializer<>).MakeGenericType(typeof(T)))!;
            }

            return Activator.CreateInstance(typeof(PlainClassesSerializer<>).MakeGenericType(typeof(T)))!;
        }

        if (typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(Nullable<>)) {
            return Activator.CreateInstance(typeof(NullableSerializer<>).MakeGenericType(typeof(T).GetGenericArguments()[0]))!;
        }

        throw new InvalidOperationException($"No known serializer for {typeof(T)}.");
    }

    /// <summary>
    /// Reads an object from a project file.
    /// </summary>
    /// <param name="reader">The <see cref="Utf8JsonReader"/> that is reading the project file. On entry, this will point to the first token to
    /// be read. On exit, this should point to the last token read.</param>
    /// <param name="context">The active <see cref="DeserializationContext"/>, for reporting errors in the json data.</param>
    /// <param name="owner">The object that owns this value. This is ignored except when reading <see cref="ModelObject"/>s.</param>
    /// <returns>The object read from the file, or <see langword="null"/> if <c><paramref name="reader"/>.<see cref="Utf8JsonReader.TokenType"/>
    /// == <see cref="JsonTokenType.Null"/></c>.</returns>
    public abstract T? ReadFromJson(ref Utf8JsonReader reader, DeserializationContext context, object? owner);

    /// <summary>
    /// Writes the specified value to a project file.
    /// </summary>
    /// <param name="writer">The <see cref="Utf8JsonWriter"/> that is writing to the project file</param>
    /// <param name="value">The value to be written to <paramref name="writer"/>.</param>
    public abstract void WriteToJson(Utf8JsonWriter writer, T? value);
    /// <summary>
    /// This converts from a value to a string, which can be used as the name of a json property.
    /// Note: When overriding this method, also add a check in IsKeySerializerSupported, to allow the type to be used as a dictionary key.
    /// </summary>
    /// <param name="value">The value to convert to a string</param>
    /// <returns><paramref name="value"/>, converted to a string that <see cref="ReadFromJsonProperty"/> can read.</returns>
    public virtual string GetJsonProperty(T value) => throw new NotSupportedException("Using type " + typeof(T) + " as dictionary key is not supported");

    /// <summary>
    /// Called to read a object from a project file when <c><paramref name="reader"/>.<see cref="Utf8JsonReader.TokenType"/> ==
    /// <see cref="JsonTokenType.PropertyName"/></c>. Instead of overriding this, it is usually better to write <see cref="ReadFromJson"/> so it
    /// can read <see cref="JsonTokenType.PropertyName"/> tokens.
    /// </summary>
    /// <param name="reader">The <see cref="Utf8JsonReader"/> that is reading the project file. On entry, this will point to the property name
    /// token to be read. On exit, this should still point to the property name token.</param>
    /// <param name="context">The active <see cref="DeserializationContext"/>, for reporting errors in the json data.</param>
    /// <param name="owner">The object that owns this value. This is ignored except when reading <see cref="ModelObject"/>s.</param>
    /// <returns>The object read from the file.</returns>
    public virtual T? ReadFromJsonProperty(ref Utf8JsonReader reader, DeserializationContext context, object owner) => ReadFromJson(ref reader, context, owner);

    /// <summary>
    /// Called to read an object from an undo snapshot. The data to be read was written by <see cref="WriteToUndoSnapshot"/>.
    /// </summary>
    /// <param name="reader">The <see cref="UndoSnapshotReader"/> that contains the objects to be read.</param>
    /// <param name="owner">The object that owns this value. This is ignored except when reading <see cref="ModelObject"/>s.</param>
    /// <returns>The object read from the snapshot.</returns>
    public abstract T? ReadFromUndoSnapshot(UndoSnapshotReader reader, object owner);
    /// <summary>
    /// Called to write an object to an undo snapshot. Write data in a format that can be read by <see cref="ReadFromUndoSnapshot"/>.
    /// </summary>
    /// <param name="writer">The <see cref="UndoSnapshotBuilder"/> that will store the objects.</param>
    public abstract void WriteToUndoSnapshot(UndoSnapshotBuilder writer, T? value);

    /// <summary>
    /// Called by other portions of the serialization system to determine whether values of type <typeparamref name="T"/> are allowed to be
    /// <see langword="null"/>.
    /// </summary>
    public virtual bool CanBeNull => false;
}

internal class ModelObjectSerializer<T> : ValueSerializer<T> where T : ModelObject {
    public override T? ReadFromJson(ref Utf8JsonReader reader, DeserializationContext context, object? owner)
        => SerializationMap<T>.DeserializeFromJson((ModelObject?)owner, ref reader, context);

    public override void WriteToJson(Utf8JsonWriter writer, T? value) => SerializationMap<T>.SerializeToJson(value, writer);

    public override T? ReadFromUndoSnapshot(UndoSnapshotReader reader, object owner) => reader.ReadOwnedReference<T>((ModelObject)owner);

    public override void WriteToUndoSnapshot(UndoSnapshotBuilder writer, T? value) => writer.WriteManagedReference(value);

    public override bool CanBeNull => true;
}

internal class NullableSerializer<T> : ValueSerializer<T?> where T : struct {
    private static readonly ValueSerializer<T> baseSerializer = ValueSerializer<T>.Default;
    public override T? ReadFromJson(ref Utf8JsonReader reader, DeserializationContext context, object? owner) {
        if (reader.TokenType == JsonTokenType.Null) {
            return null;
        }

        return baseSerializer.ReadFromJson(ref reader, context, owner);
    }

    public override void WriteToJson(Utf8JsonWriter writer, T? value) {
        if (value == null) {
            writer.WriteNullValue();
        }
        else {
            baseSerializer.WriteToJson(writer, value.Value);
        }
    }

    public override T? ReadFromUndoSnapshot(UndoSnapshotReader reader, object owner) {
        if (!reader.reader.ReadBoolean()) {
            return null;
        }

        return baseSerializer.ReadFromUndoSnapshot(reader, owner);
    }

    public override void WriteToUndoSnapshot(UndoSnapshotBuilder writer, T? value) {
        writer.writer.Write(value != null);
        if (value != null) {
            baseSerializer.WriteToUndoSnapshot(writer, value.Value);
        }
    }

    public override bool CanBeNull => true;
}

internal class IntSerializer : ValueSerializer<int> {
    public override int ReadFromJson(ref Utf8JsonReader reader, DeserializationContext context, object? owner) => reader.GetInt32();

    public override void WriteToJson(Utf8JsonWriter writer, int value) => writer.WriteNumberValue(value);

    public override int ReadFromUndoSnapshot(UndoSnapshotReader reader, object owner) => reader.reader.ReadInt32();

    public override void WriteToUndoSnapshot(UndoSnapshotBuilder writer, int value) => writer.writer.Write(value);
}

internal class FloatSerializer : ValueSerializer<float> {
    public override float ReadFromJson(ref Utf8JsonReader reader, DeserializationContext context, object? owner) => reader.GetSingle();

    public override void WriteToJson(Utf8JsonWriter writer, float value) => writer.WriteNumberValue(value);

    public override float ReadFromUndoSnapshot(UndoSnapshotReader reader, object owner) => reader.reader.ReadSingle();

    public override void WriteToUndoSnapshot(UndoSnapshotBuilder writer, float value) => writer.writer.Write(value);
}

internal class GuidSerializer : ValueSerializer<Guid> {
    // null-forgiving: If reader.GetString() returns null, we don't have a good backup and we'll find out immediately
    public override Guid ReadFromJson(ref Utf8JsonReader reader, DeserializationContext context, object? owner) => new Guid(reader.GetString()!);

    public override void WriteToJson(Utf8JsonWriter writer, Guid value) => writer.WriteStringValue(value.ToString("N"));

    public override string GetJsonProperty(Guid value) => value.ToString("N");

    public override Guid ReadFromUndoSnapshot(UndoSnapshotReader reader, object owner) => new Guid(reader.reader.ReadBytes(16));

    public override void WriteToUndoSnapshot(UndoSnapshotBuilder writer, Guid value) => writer.writer.Write(value.ToByteArray());
}

internal class PageReferenceSerializer : ValueSerializer<PageReference> {
    public override PageReference? ReadFromJson(ref Utf8JsonReader reader, DeserializationContext context, object? owner) {
        string? str = reader.GetString();
        if (str == null) {
            return null;
        }

        return new PageReference(new Guid(str));
    }

    public override void WriteToJson(Utf8JsonWriter writer, PageReference? value) {
        if (value == null) {
            writer.WriteNullValue();
        }
        else {
            writer.WriteStringValue(value.guid.ToString("N"));
        }
    }

    public override PageReference? ReadFromUndoSnapshot(UndoSnapshotReader reader, object owner) => reader.ReadManagedReference() as PageReference;

    public override void WriteToUndoSnapshot(UndoSnapshotBuilder writer, PageReference? value) => writer.WriteManagedReference(value);

    public override bool CanBeNull => true;
}

internal class TypeSerializer : ValueSerializer<Type> {
    public override Type? ReadFromJson(ref Utf8JsonReader reader, DeserializationContext context, object? owner) {
        string? s = reader.GetString();

        if (s == null) {
            return null;
        }

        if (s.StartsWith("YAFC.")) {
            s = "Yafc." + s[5..];
        }
        Type? type = Type.GetType(s);
        if (type == null) {
            context.Error(LSs.LoadErrorUntranslatedTypeDoesNotExist.L(s), ErrorSeverity.MinorDataLoss);
        }

        return type;
    }
    public override void WriteToJson(Utf8JsonWriter writer, Type? value) {
        ArgumentNullException.ThrowIfNull(value, nameof(value));
        string? name = value.FullName;

        writer.WriteStringValue(name);
    }

    public override string GetJsonProperty(Type value) {
        if (value.FullName is null) {
            // If value doesn't have a FullName, we're in a bad state and I don't know what to do.
            throw new ArgumentException($"value must be a type that has a FullName.", nameof(value));
        }

        return value.FullName;
    }

    public override Type? ReadFromUndoSnapshot(UndoSnapshotReader reader, object owner) => reader.ReadManagedReference() as Type;

    public override void WriteToUndoSnapshot(UndoSnapshotBuilder writer, Type? value) => writer.WriteManagedReference(value);
}

internal class BoolSerializer : ValueSerializer<bool> {
    public override bool ReadFromJson(ref Utf8JsonReader reader, DeserializationContext context, object? owner) => reader.GetBoolean();

    public override void WriteToJson(Utf8JsonWriter writer, bool value) => writer.WriteBooleanValue(value);

    public override bool ReadFromUndoSnapshot(UndoSnapshotReader reader, object owner) => reader.reader.ReadBoolean();

    public override void WriteToUndoSnapshot(UndoSnapshotBuilder writer, bool value) => writer.writer.Write(value);
}

internal class ULongSerializer : ValueSerializer<ulong> {
    public override ulong ReadFromJson(ref Utf8JsonReader reader, DeserializationContext context, object? owner) => reader.GetUInt64();

    public override void WriteToJson(Utf8JsonWriter writer, ulong value) => writer.WriteNumberValue(value);

    public override ulong ReadFromUndoSnapshot(UndoSnapshotReader reader, object owner) => reader.reader.ReadUInt64();

    public override void WriteToUndoSnapshot(UndoSnapshotBuilder writer, ulong value) => writer.writer.Write(value);
}

internal class StringSerializer : ValueSerializer<string> {
    public override string? ReadFromJson(ref Utf8JsonReader reader, DeserializationContext context, object? owner) => reader.GetString();

    public override void WriteToJson(Utf8JsonWriter writer, string? value) => writer.WriteStringValue(value);

    public override string GetJsonProperty(string value) => value;

    public override string? ReadFromUndoSnapshot(UndoSnapshotReader reader, object owner) => reader.ReadManagedReference() as string;

    public override void WriteToUndoSnapshot(UndoSnapshotBuilder writer, string? value) => writer.WriteManagedReference(value);

    public override bool CanBeNull => true;
}

internal class FactorioObjectSerializer<T> : ValueSerializer<T> where T : FactorioObject {
    public override T? ReadFromJson(ref Utf8JsonReader reader, DeserializationContext context, object? owner) {
        string? s = reader.GetString();

        if (s == null) {
            return null;
        }

        if (!Database.objectsByTypeName.TryGetValue(s, out var obj)) {
            var substitute = Database.FindClosestVariant(s);

            if (substitute is T t) {
                context.Error(LSs.LoadErrorFluidHasIncorrectTemperature.L(t.locName), ErrorSeverity.MinorDataLoss);
                return t;
            }

            context.Error(LSs.LoadErrorUntranslatedFactorioObjectNotFound.L(s), ErrorSeverity.MinorDataLoss);
        }
        return obj as T;
    }

    public override void WriteToJson(Utf8JsonWriter writer, T? value) {
        if (value == null) {
            writer.WriteNullValue();
        }
        else {
            writer.WriteStringValue(value.typeDotName);
        }
    }
    public override string GetJsonProperty(T value) => value.typeDotName;

    public override T? ReadFromUndoSnapshot(UndoSnapshotReader reader, object owner) => reader.ReadManagedReference() as T;

    public override void WriteToUndoSnapshot(UndoSnapshotBuilder writer, T? value) => writer.WriteManagedReference(value);

    public override bool CanBeNull => true;
}

internal sealed partial class QualityObjectSerializer<T> : ValueSerializer<IObjectWithQuality<T>> where T : FactorioObject {
    private static readonly ValueSerializer<T> objectSerializer = ValueSerializer<T>.Default;
    private static readonly ValueSerializer<Quality> qualitySerializer = ValueSerializer<Quality>.Default;

    public override bool CanBeNull => true;

    public override IObjectWithQuality<T>? ReadFromJson(ref Utf8JsonReader reader, DeserializationContext context, object? owner) {
        if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName) {
            string value = reader.GetString()!;
            if (value.StartsWith('!')) {
                // Read the new dictionary-key format, typically "!Item.coal!normal"
                string separator = Separator().Match(value).Value;
                string[] parts = value.Split(separator);
                if (parts.Length == 3) { // The parts are <empty>, target.typeDotName, and quality.name
                    _ = Database.objectsByTypeName.TryGetValue(parts[1], out var obj);
                    _ = Database.objectsByTypeName.TryGetValue("Quality." + parts[2], out var qual);
                    if (obj is not T) {
                        context.Error(LSs.LoadErrorUntranslatedFactorioObjectNotFound.L(parts[1]), ErrorSeverity.MinorDataLoss);
                    }
                    else if (qual is not Quality quality) {
                        context.Error(LSs.LoadErrorUntranslatedFactorioQualityNotFound.L(parts[2]), ErrorSeverity.MinorDataLoss);
                    }
                    else {
                        return ObjectWithQuality.Get(obj as T, quality);
                    }
                }
                // If anything went wrong reading the dictionary-key format, try the non-quality format instead.
            }

            // Read the old non-quality "Item.coal" format
            return ObjectWithQuality.Get(objectSerializer.ReadFromJson(ref reader, context, owner), Quality.Normal);
        }

        if (reader.TokenType == JsonTokenType.StartObject) {
            // Read the object-based quality format: { "target": "Item.coal", "quality": "Quality.normal" }
            reader.Read(); // {
            reader.Read(); // "target":
            T? obj = objectSerializer.ReadFromJson(ref reader, context, owner);
            reader.Read(); // target
            reader.Read(); // "quality":
            Quality? quality = qualitySerializer.ReadFromJson(ref reader, context, owner);
            reader.Read(); // quality
            // If anything went wrong, the error has already been reported. We can just return null and continue.
            return quality == null ? null : ObjectWithQuality.Get(obj, quality);
        }

        if (reader.TokenType != JsonTokenType.Null) {
            context.Error($"Unexpected token {reader.TokenType} reading a quality object.", ErrorSeverity.MinorDataLoss);
        }

        return null; // Input is JsonTokenType.Null, or couldn't parse the input
    }

    public override void WriteToJson(Utf8JsonWriter writer, IObjectWithQuality<T>? value) {
        if (value == null) {
            writer.WriteNullValue();
            return;
        }

        // TODO: This writes values that can be read by older versions of Yafc. For consistency with quality objects written as dictionary keys,
        // replace these four lines with this after a suitable period of backwards compatibility:
        // writer.WriteStringValue(GetJsonProperty(value));
        writer.WriteStartObject();
        writer.WriteString("target", value.target.typeDotName);
        writer.WriteString("quality", value.quality.typeDotName);
        writer.WriteEndObject();
    }

    public override string GetJsonProperty(IObjectWithQuality<T> value) {
        string target = value.target.typeDotName;
        string quality = value.quality.name;

        // Construct an unambiguous separator that does not appear in the string data: Alternate ! and @ until (A) the separator does not appear
        // in either the target or quality names, and (B) the first character of the object name cannot be interpreted as part of the separator.
        // That is, if target is (somehow) "!target", separator cannot be "!@", because there would otherwise be no way to distinguish
        // "!@" + "!target" from "!@!" + "target".
        // Note that "!@!" + "@quality" is fine. Once we see that the initial "!@!" is not followed by '@', we know the separator is "!@!", and
        // the @ in "@quality" must be part of the quality name.
        // In reality, rule B should never trigger, since types do not start with ! or @.
        // Even rule A will be rare, since most internal names do not contain '!'.
        string separator = "!";
        while (true) {
            if (target.Contains(separator) || quality.Contains(separator) || target.StartsWith('@')) {
                separator += '@';
            }
            else {
                break;
            }
            if (target.Contains(separator) || quality.Contains(separator) || target.StartsWith('!')) {
                separator += '!';
            }
            else {
                break;
            }
        }

        return separator + target + separator + quality;
    }

    public override IObjectWithQuality<T>? ReadFromUndoSnapshot(UndoSnapshotReader reader, object owner)
        => (IObjectWithQuality<T>?)reader.ReadManagedReference();
    public override void WriteToUndoSnapshot(UndoSnapshotBuilder writer, IObjectWithQuality<T>? value) => writer.WriteManagedReference(value);

    [GeneratedRegex("^(!@)*!?")]
    private static partial Regex Separator();
}

internal class EnumSerializer<T> : ValueSerializer<T> where T : struct, Enum {
    public EnumSerializer() {
        if (Unsafe.SizeOf<T>() != 4) {
            throw new NotSupportedException("Only int enums are supported");
        }
    }

    public override T ReadFromJson(ref Utf8JsonReader reader, DeserializationContext context, object? owner) {
        int val = reader.GetInt32();
        return Unsafe.As<int, T>(ref val);
    }

    public override void WriteToJson(Utf8JsonWriter writer, T value) => writer.WriteNumberValue(Unsafe.As<T, int>(ref value));

    public override T ReadFromUndoSnapshot(UndoSnapshotReader reader, object owner) {
        int val = reader.reader.ReadInt32();
        return Unsafe.As<int, T>(ref val);
    }

    public override void WriteToUndoSnapshot(UndoSnapshotBuilder writer, T value) => writer.writer.Write(Unsafe.As<T, int>(ref value));
}

/// <summary>
/// Serializes classes marked with <c>[Serializable]</c>, except blueprint classes, <see cref="FactorioObject"/>s, and <see cref="ModelObject"/>s.
/// </summary>
internal class PlainClassesSerializer<T> : ValueSerializer<T> where T : class {
    static PlainClassesSerializer() {
        // The checks in CreateValueSerializer should prevent these two from happening.
        if (typeof(T).IsAssignableTo(typeof(ModelObject))) {
            throw new InvalidOperationException($"PlainClassesSerializer should not be used for {typeof(T)} because it is derived from ModelObject.");
        }
        if (typeof(T).IsAssignableTo(typeof(FactorioObject))) {
            throw new InvalidOperationException($"PlainClassesSerializer should not be used for {typeof(T)} because it is derived from FactorioObject.");
        }
        if (!typeof(T).FullName!.StartsWith("Yafc.")) {
            // Well, probably. It's unlikely this default serialization will correctly handle types that were not created with it in mind.
            throw new InvalidOperationException($"PlainClassesSerializer should not be used for {typeof(T)} because it is outside the Yafc namespace.");
        }
        if (typeof(T).GetCustomAttribute<SerializableAttribute>() == null) {
            // If you want standard serialization behavior, like BeaconConfiguration, add the [Serializable] attribute to the type instead of
            // explicitly listing the type in IsValueSerializerSupported.
            // If you want non-standard serialization behavior, like FactorioObject, add a check for this type in CreateValueSerializer, and
            // return a custom serializer.
            throw new InvalidOperationException($"PlainClassesSerializer should not be used for {typeof(T)} because it does not have a [Serializable] attribute.");
        }
    }

    private static readonly SerializationMap builder = SerializationMap.GetSerializationMap(typeof(T));
    public override T? ReadFromJson(ref Utf8JsonReader reader, DeserializationContext context, object? owner) => SerializationMap<T>.DeserializeFromJson(null, ref reader, context);

    public override void WriteToJson(Utf8JsonWriter writer, T? value) => SerializationMap<T>.SerializeToJson(value, writer);

    public override T? ReadFromUndoSnapshot(UndoSnapshotReader reader, object owner) {
        T? obj = (T?)reader.ReadManagedReference();
        builder.ReadUndo(obj, reader);
        return obj;
    }
    public override void WriteToUndoSnapshot(UndoSnapshotBuilder writer, T? value) {
        writer.WriteManagedReference(value);
        builder.BuildUndo(value, writer);
    }

    public override bool CanBeNull => true;
}
