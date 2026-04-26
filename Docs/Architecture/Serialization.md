# Serialization system
The project serialization system is somewhat fragile.
It adapts properly to many changes, but can also fail silently.
To reduce the chances of silent failures, it has some [guardrails in the tests](#guarding-tests), to ensure any relevant changes are intentional and functional.

The serialization system only deals with public instance properties.
Broadly, there are two kinds of public properties, which we'll call 'writable' and 'non-writable'.
A writable property has a public setter, a constructor parameter with the same name and type, or both.
Any other public property is non-writable.
`[Obsolete]` writable properties may (and usually should) omit the getter.

All constructor parameters, except for the first parameter when derived from `ModelObject<>`, must have the same name and type as a writable property.

## Property types
The primary data structures that get serialized and deserialized are types derived from `ModelObject` and concrete (non-abstract) classes with the `[Serializable]` attribute.
These are serialized property-by-property, recursively.
Properties are handled as described here:

|Property type|Writable|Non-writable|
|-|-|-|
|[`ModelObject` and derived types](#modelobjects-and-serializable-types)|Supported if concrete|Supported|
|[`[Serializable]` classes](#modelobjects-and-serializable-types)|Supported if concrete|Ignored|
|[`ReadOnlyCollection<>` and types that implement `ICollection<>` or `IDictionary<,>`](#collections)|Error|Supported if content is supported|
|[`FactorioObject`, all derived types, and `IObjectWithQuality<>`s](#factorioobjects-and-iobjectwithqualitys)|Supported, even when abstract|Ignored|
|[Native and native-like types](#native-types)|Supported if listed|Ignored|
|Any type, if the property has `[SkipSerialization]`|Ignored|Ignored|
|Other types|Error|Ignored|

Notes:
* The constructor must initialize serialized collections to a non-`null` value.
The property may be declared to return any type that matches the _Property type_ column above.
* Value types are only supported if they appear in the list of [supported native types](#native-types).
* `[Obsolete]` properties must follow all the same rules, except that they do not need a getter if they are writable.

### `ModelObject`s and `[Serializable]` types
Each class should have exactly one public constructor.
If the class has multiple public constructors, the serialization system will use the first, for whatever definition of "first" the compiler happens to use.\
**Exception**: If the class has the `[DeserializeWithNonPublicConstructor]` attribute, it should have exactly one non-public constructor instead.

The constructor may have any number of parameters, subject to the following limitations:
* The first constructor parameter for a type derived (directly or indirectly) from `ModelObject<T>` does not follow the normal rules.
It must be present, must be of type `T`, and can have any name.
* Each (other) parameter must have the same type and name as one of the class's writable properties.
Parameters may match either directly owned properties or properties inherited from a base class.
* If the parameter has a default value, that value must be `default`.
(Or an equivalent: e.g. `null` for reference types and `Nullable<>`, `0` for numeric types, and/or `new()` for value types without a explicit 0-parameter constructor.)

Writable properties that are not one of the supported types must have the `[SkipSerialization]` attribute.

Collection properties (always non-writable) must be initialized by the constructor to a non-`null` value.
The constructor is not required to initialize non-writable `ModelObject` properties.
If it does not, the serialization system will discard non-`null` values encountered in the project file.

### Collections
Collection values must be stored in non-writable properties, must not be passed to the constructor, and must be initialized to an empty collection.
Unsupported key or value types will cause the property to be silently ignored.
Arrays and other fixed-size collections (of supported values) will cause an error when deserializing, even though they implement `ICollection<>`.

Keys (for dictionaries) must be: `FactorioObject` or derived from it, `IObjectWithQuality<>`, `string`, `Guid`, or `Type`.

Values may be of any type that can be stored in a writable property.
Explicitly, values are allowed to contain collections, but may not themselves be collections.
`null` values are permitted in collections, but will be removed when loading from JSON.

The serializer has special magic allowing it to modify a `ReadOnlyCollection<>`.
Initialize the `ReadOnlyCollection<>` to `new([])`, and the serializer will populate it correctly.

### `FactorioObject`s and `IObjectWithQuality<>`s
When deserialized, `FactorioObject`s are set to the corresponding object from `Database.allObjects`.
If that object cannot be found (for example, if the mod defining it was removed), it will be set to `null`.
Properties may return `FactorioObject` or any derived type, even if the type is abstract.

`IObjectWithQuality<>`s are the same, except that the object is fetched by calling `ObjectWithQuality.Get`.

### Native types
The supported native and native-like types are `int`, `float`, `bool`, `ulong`, `string`, `Type`, `Guid`, `PageReference`, and `enum`s (if backed by `int`).
The `Nullable<>` versions of these types are also supported, where applicable.

Any value types not listed above, including `Tuple`, `ValueTuple`, and all custom `struct`s, cannot be serialized/deserialized.

## Guarding tests
There are some "unit" tests guarding the types that are processed by the serialization system.
These tests inspect things the serialization system is likely to encounter in the wild, and ensure that they comply with the rules given under [Property types](#property-types).
They also check for changes to the serialized data, to ensure that any changes are intentional.

The failure messages should tell you exactly why the test is unhappy.
In general, the tests failures have the following meanings:
* If [`ModelObjects_AreSerializable`](../../Yafc.Model.Tests/Serialization/SerializationTypeValidation.cs) fails, it thinks you violated one of [the rules](#modelobjects-and-serializable-types) in a type derived from `ModelObject`.
* If [`Serializables_AreSerializable`](../../Yafc.Model.Tests/Serialization/SerializationTypeValidation.cs) fails, it thinks you violated one of [the rules](#modelobjects-and-serializable-types) in a `[Serializable]` type.
* If [`TreeHasNotChanged`](../../Yafc.Model.Tests/Serialization/SerializationTreeChangeDetection.cs) fails, you have added, changed, or removed serialized types or properties.
  * Ensure the change was intentional and does not break serialization (e.g. changing between `List<>` and `ReadOnlyCollection<>`) or that you have handled the necessary conversions (e.g. for changing from `FactorioObject` to `List<FactorioObject>`).
Then update the dictionary initializer to match your changes.
If you made significant changes, you should be able to run `BuildPropertyTree` in the debugger to produce the initializer that the test expects.
* If you intended to add a new property to be serialized, but `TreeHasNotChanged` does not fail, the new property probably fell into one of the ["Ignored" categories](#property-types).
* If you intended to add a new `[Serializable]` type to be serialized, but `TreeHasNotChanged` does not fail, make sure there is a writable property of that type.

Test failures are usually related to writable properties.
Non-writable properties that return array types will also cause test failures.

## Handling property type changes
The simplest solution is probably introducing a new property and applying `[Obsolete]` to the old property.
The json deserializer will continue reading the corresponding  values from the project file, if present,
but the undo system and the json serializer will ignore the old property.
You may (should?) remove the getter from an obsolete writable property.

Sometimes obsoleting a property is not reasonable, as was the case for the changes from `FactorioObject` to `ObjectWithQuality<>`.
Depending on the requirements, you can either implement `ICustomJsonDeserializer<T>` (as used by `ObjectWithQuality<T>` in [081e9c0f](https://github.com/Yafc-CE/yafc-ce/tree/081e9c0f6b47e155fbc82763590a70d90a64c83c/Yafc.Model/Data/DataClasses.cs#L819) and earlier),
or create a new `ValueSerializer<T>` (as seen in the current `QualityObjectSerializer<T>` implementation).

## Adding new supported types
Consider workarounds such as using `List<NestedList<T>>` instead of `List<List<T>>`, where
```c#
[Serializable]
public sealed class NestedList<T> : IEnumerable<T> /* do not implement ICollection<> or IList<> */ {
    public List<T> List { get; } = [];
    public IEnumerator<T> GetEnumerator() => List.GetEnumerator();
}
```

### Writable properties and collection values
If the type is already part of Yafc, you may be able to support it simply by adding `[Serializable]` to the type in question.
If that isn't adequate, implement a new class derived from `ValueSerializer<T>`.
The new type should be generic if the serialized type is generic or a type hierarchy.
Add checks for your newly supported type(s) in `IsValueSerializerSupported` and `CreateValueSerializer`.

The tests should automatically update themselves for newly supported types in writable properties and collections.

### Dictionary keys
Find the corresponding `ValueSerializer<T>` implementation, or create a new one as described in the previous section.
Add an override for `GetJsonProperty`, to convert the value to a string.
If `ReadFromJson` does not support reading from a `JsonTokenType.PropertyName`, update it to do so, or override `ReadFromJsonProperty`.
In either case, return the desired object by reading the property name.
Add a check for the newly supported type in `IsKeySerializerSupported`.

The tests should automatically update themselves for newly supported types in dictionary keys.

### Non-writable properties
To add support for a new type in a non-writable property, implement a new
```c#
internal sealed class NewReadOnlyPropertySerializer<TOwner, TPropertyType[, TOtherTypes]>(PropertyInfo property)
    where TPropertyType : NewPropertyType[<TOtherTypes>] where TOwner : class
    : PropertySerializer<TOwner, TPropertyType>(property, PropertyType.Normal, false)
```
Include `TOtherTypes` if the newly supported  type is generic.
Serialize nested objects using `ValueSerializer<TOtherType>.Default`, to ensure that newly introduced `ValueSerializer<T>` implementations are supported by your collection.

If your newly supported type is an interface, add a check for the interface type in `GetInterfaceSerializer`.

If your newly supported type is a class, find the two calls to `GetInterfaceSerializer`.
In the outer else block, add another check for your newly supported type.

Changes to non-writable property support may require changes to `SerializationTypeValidation` in the vicinity of the calls to `AssertNoArraysOfSerializableValues`,
and will require changes to `TreeHasNotChanged` in the vicinity of the `typeof(ReadOnlyCollection<>)`, `typeof(IDictionary<,>)` and/or `typeof(ICollection<>)` tests.
