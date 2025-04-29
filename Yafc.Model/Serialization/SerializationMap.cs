using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Serilog;
using Yafc.I18n;
using Yafc.UI;

namespace Yafc.Model;

[AttributeUsage(AttributeTargets.Property)]
public class SkipSerializationAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public class NoUndoAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
public sealed class DeserializeWithNonPublicConstructorAttribute : Attribute { }

/// <summary>
/// Represents a type that can be deserialized from JSON formats that do not match its current serialization format. This typically happens when
/// an object changes between value (string/float), object, and array representations.
/// </summary>
/// <typeparam name="T">The type that is being defined with custom deserialization rules.</typeparam>
/// <remarks>When adding new static members to this interface, also add matching instance members to 
/// <see cref="SerializationMap{T}.ICustomDeserializerHelper"/> and <see cref="SerializationMap{T}.CustomDeserializerHelper{U}"/>. Check that
/// <c>helper</c> is not <see langword="null"/> and call the members of this interface using <c>helper.<em>MemberName</em></c>.</remarks>
internal interface ICustomJsonDeserializer<T> {
    /// <summary>
    /// Attempts to deserialize an object from custom (aka 'old') formats.
    /// </summary>
    /// <param name="reader">The <see cref="Utf8JsonReader"/> that points to the first token that might need custom deserialization. If the deserialization is successful,
    /// this must be advanced to the last token that was consumed to deserialize the object. (e.g. If three tokens were used, advance this by two tokens.)</param>
    /// <param name="result">If the deserialization was successful (this method returns <see langword="true"/>), this is set to the result of reading the consumed JSON tokens.
    /// If unsuccessful, the output value of this parameter should not be used.</param>
    /// <returns><see langword="true"/> when the custom deserialization was successful, and <see langword="false"/> when the default serializer should be used instead.</returns>
    // This is a static method so it can be declared for types that do not have a default constructor.
    static abstract bool Deserialize(ref Utf8JsonReader reader, DeserializationContext context, out T? result);

    // When adding new static members to this interface, also add them to SerializationMap's nested types; see the remarks above.
}

internal abstract class SerializationMap {
    private static readonly ILogger logger = Logging.GetLogger<SerializationMap>();
    public UndoSnapshot MakeUndoSnapshot(ModelObject target) {
        UndoSnapshotBuilder snapshotBuilder = new(target);
        BuildUndo(target, snapshotBuilder);

        return snapshotBuilder.Build();
    }
    public void RevertToUndoSnapshot(ModelObject target, UndoSnapshot snapshot) {
        UndoSnapshotReader snapshotReader = new(snapshot);
        ReadUndo(target, snapshotReader);
    }
    public abstract void BuildUndo(object? target, UndoSnapshotBuilder builder);
    public abstract void ReadUndo(object? target, UndoSnapshotReader reader);

    private static readonly Dictionary<Type, SerializationMap> undoBuilders = [];
    protected static int deserializingCount;
    public static bool IsDeserializing => deserializingCount > 0;

    public static SerializationMap GetSerializationMap(Type type) {
        if (undoBuilders.TryGetValue(type, out var builder)) {
            return builder;
        }

        return undoBuilders[type] = (SerializationMap)Activator.CreateInstance(typeof(SerializationMap<>.SpecificSerializationMap).MakeGenericType(type))!;
    }

    public abstract void SerializeToJson(object target, Utf8JsonWriter writer);
    public abstract void PopulateFromJson(object target, ref Utf8JsonReader reader, DeserializationContext context);
}

internal static class SerializationMap<T> where T : class {
    private static readonly ILogger logger = Logging.GetLogger(typeof(SerializationMap<T>));
    private static readonly Type? parentType;
    private static readonly ConstructorInfo constructor;
    private static readonly PropertySerializer<T>[] properties;
    private static readonly int constructorProperties;
    private static readonly ulong constructorFieldMask;
    private static readonly ulong requiredConstructorFieldMask;
    private static readonly ICustomDeserializerHelper? helper;

    public class SpecificSerializationMap : SerializationMap {
        public override void BuildUndo(object? target, UndoSnapshotBuilder builder) {
            T? t = (T?)target;

            if (t != null) {
                foreach (var property in properties) {
                    if (property.type == PropertyType.Normal) {
                        property.SerializeToUndoBuilder(t, builder);
                    }
                }
            }
        }

        public override void ReadUndo(object? target, UndoSnapshotReader reader) {
            try {
                deserializingCount++;
                T? t = (T?)target;

                if (t != null) {
                    foreach (var property in properties) {
                        if (property.type == PropertyType.Normal) {
                            property.DeserializeFromUndoBuilder(t, reader);
                        }
                    }
                }
            }
            finally {
                deserializingCount--;
            }
        }

        public override void SerializeToJson(object target, Utf8JsonWriter writer) => SerializationMap<T>.SerializeToJson((T)target, writer);

        public override void PopulateFromJson(object target, ref Utf8JsonReader reader, DeserializationContext context) {
            try {
                deserializingCount++;
                SerializationMap<T>.PopulateFromJson((T)target, ref reader, context);
            }
            finally {
                deserializingCount--;
            }
        }
    }

    private static bool GetInterfaceSerializer(Type iface, [MaybeNullWhen(false)] out Type serializerType, out Type? keyType, [MaybeNullWhen(false)] out Type elementType) {
        if (iface.IsGenericType) {
            var definition = iface.GetGenericTypeDefinition();

            if (definition == typeof(ICollection<>)) {
                elementType = iface.GetGenericArguments()[0];
                keyType = null;

                if (ValueSerializer.IsValueSerializerSupported(elementType)) {
                    serializerType = typeof(CollectionSerializer<,,>);

                    return true;
                }
            }

            if (definition == typeof(IDictionary<,>)) {
                var args = iface.GetGenericArguments();

                keyType = args[0];
                elementType = args[1];
                if (ValueSerializer.IsKeySerializerSupported(keyType) && ValueSerializer.IsValueSerializerSupported(elementType)) {
                    serializerType = typeof(DictionarySerializer<,,,>);
                    return true;
                }
            }
        }

        keyType = elementType = serializerType = null;

        return false;
    }

    static SerializationMap() {

        List<PropertySerializer<T>> list = [];
        bool isModel = typeof(ModelObject).IsAssignableFrom(typeof(T));

        if (typeof(T).GetCustomAttribute<DeserializeWithNonPublicConstructorAttribute>() != null) {
            constructor = typeof(T).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0];
        }
        else {
            constructor = typeof(T).GetConstructors(BindingFlags.Public | BindingFlags.Instance)[0];
        }

        if (typeof(ICustomJsonDeserializer<T>).IsAssignableFrom(typeof(T))) {
            // I want to call methods by using, e.g. `((ICustomJsonDeserializer<T>)T).Deserialize(...)`, but that isn't supported.
            // This helper calls ICustomJsonDeserializer<T>.Deserialize (and any new methods) by the least obscure method I could come up with.

            helper = (ICustomDeserializerHelper)Activator.CreateInstance(typeof(CustomDeserializerHelper<>).MakeGenericType(typeof(T), typeof(T)))!;
        }

        var constructorParameters = constructor.GetParameters();
        List<PropertyInfo> processedProperties = [];

        if (constructorParameters.Length > 0) {
            int firstReadOnlyArg = 0;

            if (isModel) {
                parentType = constructorParameters[0].ParameterType;

                if (!typeof(ModelObject).IsAssignableFrom(parentType)) {
                    throw new NotSupportedException("First parameter of constructor of type " + typeof(T) + " should be 'parent'");
                }

                firstReadOnlyArg = 1;
            }
            for (int i = firstReadOnlyArg; i < constructorParameters.Length; i++) {
                var argument = constructorParameters[i];

                if (!ValueSerializer.IsValueSerializerSupported(argument.ParameterType)) {
                    throw new NotSupportedException("Constructor of type " + typeof(T) + " parameter " + argument.Name + " should be value");
                }

                PropertyInfo property = typeof(T).GetProperty(argument.Name!) ?? throw new NotSupportedException("Constructor of type " + typeof(T) + " parameter " + argument.Name + " should have matching property");
                processedProperties.Add(property);
                PropertySerializer<T> serializer = (PropertySerializer<T>)Activator.CreateInstance(typeof(ValuePropertySerializer<,>).MakeGenericType(typeof(T), argument.ParameterType), property)!;
                list.Add(serializer);
                constructorFieldMask |= 1ul << (i - firstReadOnlyArg);

                if (!argument.IsOptional) {
                    requiredConstructorFieldMask |= 1ul << (i - firstReadOnlyArg);
                }
            }
        }

        constructorProperties = list.Count;

        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            if (property.GetCustomAttribute<SkipSerializationAttribute>() != null) {
                continue;
            }

            if (processedProperties.Contains(property)) {
                continue;
            }

            var propertyType = property.PropertyType;
            Type? serializerType = null, elementType = null, keyType = null;

            if (property.CanWrite && property.GetSetMethod() != null) {
                if (ValueSerializer.IsValueSerializerSupported(propertyType)) {
                    serializerType = typeof(ValuePropertySerializer<,>);
                }
                else {
                    throw new NotSupportedException("Type " + typeof(T) + " has property " + property.Name + " that cannot be serialized");
                }
            }
            else {
                if (typeof(ModelObject).IsAssignableFrom(propertyType)) {
                    serializerType = typeof(ReadOnlyReferenceSerializer<,>);
                }
                else if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(ReadOnlyCollection<>) && ValueSerializer.IsValueSerializerSupported(propertyType.GetGenericArguments()[0])) {
                    serializerType = typeof(ReadOnlyCollectionSerializer<,,>);
                    elementType = propertyType.GetGenericArguments()[0];
                }
                else if (!propertyType.IsInterface || !GetInterfaceSerializer(propertyType, out serializerType, out keyType, out elementType)) {
                    foreach (Type iface in propertyType.GetInterfaces()) {
                        if (GetInterfaceSerializer(iface, out serializerType, out keyType, out elementType)) {
                            break;
                        }
                    }
                }
            }

            if (serializerType != null) {
                Type[] typeArgs = elementType == null ? [typeof(T), propertyType] : keyType == null ? [typeof(T), propertyType, elementType] : [typeof(T), propertyType, keyType, elementType];
                list.Add((PropertySerializer<T>)Activator.CreateInstance(serializerType.MakeGenericType(typeArgs), property)!);
            }
        }
        properties = [.. list];
    }

    public static void SerializeToJson(T? value, Utf8JsonWriter writer) {
        if (value == null) {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();

        foreach (var property in properties) {
            if (property.type == PropertyType.Obsolete) {
                continue;
            }

            writer.WritePropertyName(property.propertyName);
            property.SerializeToJson(value, writer);
        }
        writer.WriteEndObject();
    }

    private static PropertySerializer<T>? FindProperty(ref Utf8JsonReader reader, ref int lastMatch) {
        if (reader.TokenType != JsonTokenType.PropertyName) {
            return null;
        }

        for (int i = lastMatch + 1; i < properties.Length; i++) {
            if (reader.ValueTextEquals(properties[i].propertyName.EncodedUtf8Bytes)) {
                lastMatch = i;
                return properties[i];
            }
        }

        for (int i = 0; i < lastMatch; i++) {
            if (reader.ValueTextEquals(properties[i].propertyName.EncodedUtf8Bytes)) {
                lastMatch = i;
                return properties[i];
            }
        }

        return null;
    }

    /// <summary>
    /// A helper type for calling static members of <see cref="ICustomJsonDeserializer{T}"/>. Static members there should have matching instance
    /// members here.
    /// </summary>
    private interface ICustomDeserializerHelper {
        bool Deserialize(ref Utf8JsonReader reader, DeserializationContext context, out T? deserializedObject);
    }

    /// <summary>
    /// A helper type for calling static members in <see cref="ICustomJsonDeserializer{T}"/>. Static members there should have matching instance
    /// members here, which call <typeparamref name="U"/>.<em>MemberName</em> and return the result, casting as appropriate.
    /// </summary>
    /// <typeparam name="U">Always the same as <typeparamref name="T"/>, though the compiler doesn't know this.</typeparam>
    private class CustomDeserializerHelper<U> : ICustomDeserializerHelper where U : ICustomJsonDeserializer<U> {
        public bool Deserialize(ref Utf8JsonReader reader, DeserializationContext context, out T? deserializedObject) {
            bool result = U.Deserialize(ref reader, context, out U? intermediate);
            deserializedObject = (T?)(object?)intermediate;
            return result;
        }
    }

    public static T? DeserializeFromJson(ModelObject? owner, ref Utf8JsonReader reader, DeserializationContext context) {
        if (reader.TokenType == JsonTokenType.Null) {
            return null;
        }

        if (helper != null) {
            Utf8JsonReader savedState = reader;
            if (helper.Deserialize(ref reader, context, out T? result)) {
                // The custom deserializer was successful; return its result.
                return result;
            }
            // The custom deserializer didn't want to deal with the input; return to the previous state and use the normal deserializer.
            reader = savedState;
        }

        if (reader.TokenType != JsonTokenType.StartObject) {
            throw new JsonException("Expected start object");
        }

        int depth = reader.CurrentDepth;
        try {
            T obj;

            if (parentType != null || constructorProperties > 0) {
                if (parentType != null && !parentType.IsInstanceOfType(owner)) {
                    throw new NotSupportedException("Parent is of wrong type");
                }

                int firstReadOnlyArg = parentType == null ? 0 : 1;
                object?[] constructorArgs = new object[constructorProperties + firstReadOnlyArg];
                constructorArgs[0] = owner;

                if (constructorProperties > 0) {
                    var savedReaderState = reader;
                    int lastMatch = -1;
                    ulong constructorMissingFields = constructorFieldMask;
                    _ = reader.Read(); // StartObject

                    while (constructorMissingFields != 0 && reader.TokenType != JsonTokenType.EndObject) {
                        var property = FindProperty(ref reader, ref lastMatch);

                        if (property != null && lastMatch < constructorProperties) {
                            _ = reader.Read(); // PropertyName
                            constructorMissingFields &= ~(1ul << lastMatch);
                            constructorArgs[lastMatch + firstReadOnlyArg] = property.DeserializeFromJson(ref reader, context);
                        }
                        else {
                            reader.Skip();
                        }

                        _ = reader.Read(); // Property value (String, Number, True, False, Null) or end of property value (EndObject, EndArray)
                    }

                    if ((constructorMissingFields & requiredConstructorFieldMask) != 0) {
                        throw new JsonException("Json has missing constructor parameters");
                    }

                    reader = savedReaderState;
                }

                obj = (T)constructor.Invoke(constructorArgs);
            }
            else {
                obj = Activator.CreateInstance<T>();
            }

            PopulateFromJson(obj, ref reader, context);

            return obj;
        }
        catch (Exception ex) {
            context.Exception(ex, LSs.LoadErrorUnableToDeserializeUntranslated.L(typeof(T).Name), ErrorSeverity.MajorDataLoss);

            if (reader.TokenType == JsonTokenType.StartObject && reader.CurrentDepth == depth) {
                _ = reader.Read();
            }

            while (reader.CurrentDepth > depth) {
                _ = reader.Read();
            }

            return null;
        }
    }

    public static void PopulateFromJson(T obj, ref Utf8JsonReader reader, DeserializationContext allObjects) {
        if (obj is ModelObject modelObject) {
            allObjects.Add(modelObject);
        }

        if (reader.TokenType != JsonTokenType.StartObject) {
            throw new JsonException("Expected start object");
        }

        int lastMatch = -1;
        _ = reader.Read();

        while (reader.TokenType != JsonTokenType.EndObject) {
            var property = FindProperty(ref reader, ref lastMatch);

            if (property == null || lastMatch < constructorProperties) {
                if (property == null) {
                    logger.Error("Json has extra property: " + reader.GetString());
                }

                reader.Skip();
            }
            else {
                _ = reader.Read();
                try {
                    property.DeserializeFromJson(obj, ref reader, allObjects);
                }
                catch (InvalidOperationException ex) {
                    allObjects.Exception(ex, LSs.LoadErrorEncounteredUnexpectedValue, ErrorSeverity.MajorDataLoss);
                }
            }
            _ = reader.Read();
        }
    }
}

public class DeserializationContext(ErrorCollector? errorCollector) {
    private readonly List<ModelObject> allObjects = [];

    public void Add(ModelObject obj) => allObjects.Add(obj);

    public void Notify() {
        foreach (var o in allObjects) {
            o.AfterDeserialize();
        }

        foreach (var o in allObjects) {
            o.ThisChanged(false);
        }
    }

    public void Error(string message, ErrorSeverity severity) => errorCollector?.Error(message, severity);

    public void Exception(Exception exception, string message, ErrorSeverity severity) => errorCollector?.Exception(exception, message, severity);
}
