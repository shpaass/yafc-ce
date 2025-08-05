using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace Yafc.Model.Serialization.Tests;

public class SerializationTreeChangeDetection {
    private static readonly Dictionary<Type, Dictionary<string, Type>> propertyTree = new() {
        [typeof(AutoPlanner)] = new() {
            [nameof(AutoPlanner.goals)] = typeof(List<AutoPlannerGoal>),
            [nameof(AutoPlanner.done)] = typeof(HashSet<Recipe>),
            [nameof(AutoPlanner.roots)] = typeof(HashSet<Goods>),
        },
        [typeof(ModuleFillerParameters)] = new() {
            [nameof(ModuleFillerParameters.fillMiners)] = typeof(bool),
            [nameof(ModuleFillerParameters.autoFillPayback)] = typeof(float),
            [nameof(ModuleFillerParameters.fillerModule)] = typeof(IObjectWithQuality<Module>),
            [nameof(ModuleFillerParameters.beacon)] = typeof(IObjectWithQuality<EntityBeacon>),
            [nameof(ModuleFillerParameters.beaconModule)] = typeof(IObjectWithQuality<Module>),
            [nameof(ModuleFillerParameters.beaconsPerBuilding)] = typeof(int),
            [nameof(ModuleFillerParameters.overrideCrafterBeacons)] = typeof(OverrideCrafterBeacons),
        },
        [typeof(ProductionSummaryGroup)] = new() {
            [nameof(ProductionSummaryGroup.elements)] = typeof(List<ProductionSummaryEntry>),
            [nameof(ProductionSummaryGroup.expanded)] = typeof(bool),
            [nameof(ProductionSummaryGroup.name)] = typeof(string),
        },
        [typeof(ProductionSummaryEntry)] = new() {
            [nameof(ProductionSummaryEntry.multiplier)] = typeof(float),
            [nameof(ProductionSummaryEntry.page)] = typeof(PageReference),
            [nameof(ProductionSummaryEntry.subgroup)] = typeof(ProductionSummaryGroup),
        },
        [typeof(ProductionSummaryColumn)] = new() {
            [nameof(ProductionSummaryColumn.goods)] = typeof(IObjectWithQuality<Goods>),
        },
        [typeof(ProductionSummary)] = new() {
            [nameof(ProductionSummary.group)] = typeof(ProductionSummaryGroup),
            [nameof(ProductionSummary.columns)] = typeof(List<ProductionSummaryColumn>),
        },
        [typeof(ProductionTable)] = new() {
            [nameof(ProductionTable.expanded)] = typeof(bool),
            [nameof(ProductionTable.links)] = typeof(List<ProductionLink>),
            [nameof(ProductionTable.recipes)] = typeof(List<RecipeRow>),
            [nameof(ProductionTable.modules)] = typeof(ModuleFillerParameters),
        },
        [typeof(RecipeRowCustomModule)] = new() {
            [nameof(RecipeRowCustomModule.module)] = typeof(IObjectWithQuality<Module>),
            [nameof(RecipeRowCustomModule.fixedCount)] = typeof(int),
        },
        [typeof(ModuleTemplate)] = new() {
            [nameof(ModuleTemplate.beacon)] = typeof(IObjectWithQuality<EntityBeacon>),
            [nameof(ModuleTemplate.list)] = typeof(ReadOnlyCollection<RecipeRowCustomModule>),
            [nameof(ModuleTemplate.beaconList)] = typeof(ReadOnlyCollection<RecipeRowCustomModule>),
        },
        [typeof(RecipeRow)] = new() {
            [nameof(RecipeRow.recipe)] = typeof(IObjectWithQuality<RecipeOrTechnology>),
            [nameof(RecipeRow.entity)] = typeof(IObjectWithQuality<EntityCrafter>),
            [nameof(RecipeRow.fuel)] = typeof(IObjectWithQuality<Goods>),
            [nameof(RecipeRow.fixedBuildings)] = typeof(float),
            [nameof(RecipeRow.fixedFuel)] = typeof(bool),
            [nameof(RecipeRow.fixedIngredient)] = typeof(IObjectWithQuality<Goods>),
            [nameof(RecipeRow.fixedProduct)] = typeof(IObjectWithQuality<Goods>),
            [nameof(RecipeRow.builtBuildings)] = typeof(int?),
            [nameof(RecipeRow.showTotalIO)] = typeof(bool),
            [nameof(RecipeRow.enabled)] = typeof(bool),
            [nameof(RecipeRow.tag)] = typeof(int),
            [nameof(RecipeRow.ingredientConsumptionPercentages)] = typeof(Dictionary<IObjectWithQuality<Goods>, float>),
            [nameof(RecipeRow.modules)] = typeof(ModuleTemplate),
            [nameof(RecipeRow.subgroup)] = typeof(ProductionTable),
            [nameof(RecipeRow.variants)] = typeof(HashSet<FactorioObject>),
        },
        [typeof(ProductionLink)] = new() {
            [nameof(ProductionLink.goods)] = typeof(IObjectWithQuality<Goods>),
            [nameof(ProductionLink.amount)] = typeof(float),
            [nameof(ProductionLink.algorithm)] = typeof(LinkAlgorithm),
            [nameof(ProductionLink.splitPercentage)] = typeof(float?),
        },
        [typeof(Project)] = new() {
            [nameof(Project.settings)] = typeof(ProjectSettings),
            [nameof(Project.preferences)] = typeof(ProjectPreferences),
            [nameof(Project.sharedModuleTemplates)] = typeof(List<ProjectModuleTemplate>),
            [nameof(Project.yafcVersion)] = typeof(string),
            [nameof(Project.pages)] = typeof(List<ProjectPage>),
            [nameof(Project.displayPages)] = typeof(List<Guid>),
        },
        [typeof(ProjectSettings)] = new() {
            [nameof(ProjectSettings.milestones)] = typeof(List<FactorioObject>),
            [nameof(ProjectSettings.itemFlags)] = typeof(SortedList<FactorioObject, ProjectPerItemFlags>),
            [nameof(ProjectSettings.miningProductivity)] = typeof(float),
            [nameof(ProjectSettings.researchSpeedBonus)] = typeof(float),
            [nameof(ProjectSettings.researchProductivity)] = typeof(float),
            [nameof(ProjectSettings.productivityTechnologyLevels)] = typeof(Dictionary<Technology, int>),
            [nameof(ProjectSettings.reactorSizeX)] = typeof(int),
            [nameof(ProjectSettings.reactorSizeY)] = typeof(int),
            [nameof(ProjectSettings.PollutionCostModifier)] = typeof(float),
            [nameof(ProjectSettings.spoilingRate)] = typeof(float),
        },
        [typeof(ProjectPreferences)] = new() {
            [nameof(ProjectPreferences.time)] = typeof(int),
            [nameof(ProjectPreferences.itemUnit)] = typeof(float),
            [nameof(ProjectPreferences.fluidUnit)] = typeof(float),
            [nameof(ProjectPreferences.defaultBelt)] = typeof(EntityBelt),
            [nameof(ProjectPreferences.defaultInserter)] = typeof(EntityInserter),
            [nameof(ProjectPreferences.inserterCapacity)] = typeof(int),
            [nameof(ProjectPreferences.sourceResources)] = typeof(HashSet<FactorioObject>),
            [nameof(ProjectPreferences.favorites)] = typeof(HashSet<FactorioObject>),
            [nameof(ProjectPreferences.targetTechnology)] = typeof(Technology),
            [nameof(ProjectPreferences.iconScale)] = typeof(float),
            [nameof(ProjectPreferences.maxMilestonesPerTooltipLine)] = typeof(int),
            [nameof(ProjectPreferences.showMilestoneOnInaccessible)] = typeof(bool),
        },
        [typeof(ProjectModuleTemplate)] = new() {
            [nameof(ProjectModuleTemplate.template)] = typeof(ModuleTemplate),
            [nameof(ProjectModuleTemplate.icon)] = typeof(FactorioObject),
            [nameof(ProjectModuleTemplate.name)] = typeof(string),
            [nameof(ProjectModuleTemplate.filterEntities)] = typeof(List<Entity>),
            [nameof(ProjectModuleTemplate.autoApplyToNewRows)] = typeof(bool),
            [nameof(ProjectModuleTemplate.autoApplyIfIncompatible)] = typeof(bool),
        },
        [typeof(ProjectPage)] = new() {
            [nameof(ProjectPage.icon)] = typeof(FactorioObject),
            [nameof(ProjectPage.name)] = typeof(string),
            [nameof(ProjectPage.guid)] = typeof(Guid),
            [nameof(ProjectPage.contentType)] = typeof(Type),
            [nameof(ProjectPage.content)] = typeof(ProjectPageContents),
        },
        [typeof(Summary)] = new() {
            [nameof(Summary.showOnlyIssues)] = typeof(bool),
        },
        [typeof(AutoPlannerGoal)] = new() {
            [nameof(AutoPlannerGoal.item)] = typeof(Goods),
            [nameof(AutoPlannerGoal.amount)] = typeof(float),
        },
        [typeof(BeaconOverrideConfiguration)] = new() {
            [nameof(BeaconOverrideConfiguration.beacon)] = typeof(IObjectWithQuality<EntityBeacon>),
            [nameof(BeaconOverrideConfiguration.beaconCount)] = typeof(int),
            [nameof(BeaconOverrideConfiguration.beaconModule)] = typeof(IObjectWithQuality<Module>),
        },
    };

    private readonly HashSet<Type> queuedTypes = [.. AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
        .Where(t => typeof(ModelObject).IsAssignableFrom(t) && !t.IsAbstract)];
    private readonly Queue<Type> queue;

    public SerializationTreeChangeDetection() => queue = new(queuedTypes);

    [Fact]
    // Walk all serialized types (starting with all concrete ModelObjects), and check which types are serialized and the types of their
    // properties. Changes to this list may result in save/load issues, so require an extra step (modifying the above dictionary initializer)
    // to ensure those changes are intentional.
    // 
    // If this test fails, and the change is unintentional or the next steps are not obvious, consult Docs/Architecture/Serialization.md,
    // probably starting in the section 'Guarding Tests' or 'Handling property type changes'.
    public void TreeHasNotChanged() {
        while (queue.TryDequeue(out Type type)) {
            Assert.True(propertyTree.Remove(type, out var expectedProperties), $"Serializing new type {MakeTypeName(type)}. Add `[typeof({MakeTypeName(type)})] = new() {{ /*properties*/ }},` to the propertyTree initializer.");

            var constructorWritable = SerializationTypeValidation.FindConstructor(type).GetParameters().Select(p => p.Name);
            if (typeof(ModelObject).IsAssignableFrom(type)) {
                // Skip the parent/owner parameter
                constructorWritable = constructorWritable.Skip(1);
            }
            HashSet<string> constructorParameters = [.. constructorWritable];

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
                if (property.GetCustomAttribute<ObsoleteAttribute>() != null || property.GetCustomAttribute<SkipSerializationAttribute>() != null) {
                    continue;
                }

                Type propertyType = property.PropertyType;

                if (property.GetSetMethod() != null || constructorParameters.Contains(property.Name)) {
                    // Writable properties are always serialized
                    AssertPropertyType(type, expectedProperties, property, propertyType);
                    QueueSerializedType(propertyType);
                }
                else if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(ReadOnlyCollection<>)
                    && ValueSerializer.IsValueSerializerSupported(propertyType.GenericTypeArguments[0])) {

                    // ReadOnlyCollection<T> is serialized as T
                    AssertPropertyType(type, expectedProperties, property, propertyType);
                    QueueSerializedType(propertyType.GenericTypeArguments[0]);
                }
                else if (propertyType.IsAssignableTo(typeof(ModelObject))) {
                    // Read-only model objects are serialized in-place.
                    AssertPropertyType(type, expectedProperties, property, propertyType);
                    QueueSerializedType(propertyType); // Only for consistency; the queue is initialized with all ModelObjects.
                }
                else {
                    var interfaces = propertyType.GetInterfaces().Where(i => i.IsGenericType).DistinctBy(i => i.GetGenericTypeDefinition()).ToList();
                    if (interfaces.FirstOrDefault(i => i.GetGenericTypeDefinition() == typeof(IDictionary<,>)) is Type iDictionary) {
                        if (ValueSerializer.IsValueSerializerSupported(iDictionary.GenericTypeArguments[0])
                            && ValueSerializer.IsValueSerializerSupported(iDictionary.GenericTypeArguments[1])) {

                            var types = iDictionary.GenericTypeArguments;
                            AssertPropertyType(type, expectedProperties, property, propertyType);
                            QueueSerializedType(types[0]);
                            QueueSerializedType(types[1]);
                        }
                    }
                    else if (interfaces.FirstOrDefault(i => i.GetGenericTypeDefinition() == typeof(ICollection<>)) is Type iCollection) {
                        if (ValueSerializer.IsValueSerializerSupported(iCollection.GenericTypeArguments[0])) {
                            var types = iCollection.GenericTypeArguments;
                            AssertPropertyType(type, expectedProperties, property, propertyType);
                            QueueSerializedType(types[0]);
                        }
                    }
                }
            }

            if (expectedProperties.Keys.FirstOrDefault() is string missingProperty) {
                Assert.Fail($"Expected to serialize property {MakeTypeName(type)}.{missingProperty}. Remove its entry from the propertyTree initializer if it is obsolete or no longer supported.");
            }
        }

        if (propertyTree.Keys.FirstOrDefault() is Type missingType) {
            Assert.Fail($"Expected to serialize type {MakeTypeName(missingType)}. Remove its entry from the propertyTree initializer if it is obsolete or no longer supported.");
        }
    }

    // Not explictly called, but you can call this from the debugger to generate the initializer as a string.
    public string BuildPropertyTree() {
        StringBuilder result = new("    private static readonly Dictionary<Type, Dictionary<string, Type>> propertyTree = new() {\r\n");
        HashSet<Type> queuedTypes = [.. this.queuedTypes];
        Queue<Type> queue = new(queuedTypes);
        while (queue.TryDequeue(out Type type)) {
            result.AppendLine($"        [typeof({MakeTypeName(type)})] = new() {{");
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
                if (property.GetCustomAttribute<ObsoleteAttribute>() != null || property.GetCustomAttribute<SkipSerializationAttribute>() != null) {
                    continue;
                }

                var constructorWritable = SerializationTypeValidation.FindConstructor(type).GetParameters().Select(p => p.Name);
                if (typeof(ModelObject).IsAssignableFrom(type)) {
                    // Skip the parent/owner parameter
                    constructorWritable = constructorWritable.Skip(1);
                }
                HashSet<string> constructorParameters = [.. constructorWritable];

                Type propertyType = property.PropertyType;

                if (property.GetSetMethod() != null || constructorParameters.Contains(property.Name)) {
                    // Writable properties are always serialized
                    addPropertyType(type, property, propertyType);
                    queueSerializedType(propertyType);
                }
                else if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(ReadOnlyCollection<>)
                    && ValueSerializer.IsValueSerializerSupported(propertyType.GenericTypeArguments[0])) {

                    // ReadOnlyCollection<T> is serialized as T
                    addPropertyType(type, property, propertyType);
                    queueSerializedType(propertyType.GenericTypeArguments[0]);
                }
                else if (propertyType.IsAssignableTo(typeof(ModelObject))) {
                    // Read-only model objects are serialized in-place
                    addPropertyType(type, property, propertyType);
                    queueSerializedType(propertyType);
                }
                else {
                    var interfaces = propertyType.GetInterfaces().Where(i => i.IsGenericType).DistinctBy(i => i.GetGenericTypeDefinition()).ToList();
                    if (interfaces.FirstOrDefault(i => i.GetGenericTypeDefinition() == typeof(IDictionary<,>)) is Type iDictionary) {
                        if (ValueSerializer.IsValueSerializerSupported(iDictionary.GenericTypeArguments[0])
                            && ValueSerializer.IsValueSerializerSupported(iDictionary.GenericTypeArguments[1])) {

                            var types = iDictionary.GenericTypeArguments;
                            addPropertyType(type, property, propertyType);
                            queueSerializedType(types[0]);
                            queueSerializedType(types[1]);
                        }
                    }
                    else if (interfaces.FirstOrDefault(i => i.GetGenericTypeDefinition() == typeof(ICollection<>)) is Type iCollection) {
                        if (ValueSerializer.IsValueSerializerSupported(iCollection.GenericTypeArguments[0])) {
                            var types = iCollection.GenericTypeArguments;
                            addPropertyType(type, property, propertyType);
                            queueSerializedType(types[0]);
                        }
                    }
                }
            }
            result.AppendLine("        },");
        }

        return result.AppendLine("    };").ToString();

        void addPropertyType(Type type, PropertyInfo property, Type propertyType)
            => result.AppendLine($"            [nameof({MakeTypeName(type)}.{property.Name})] = typeof({MakeTypeName(propertyType)}),");

        void queueSerializedType(Type serializationType) {
            if ((serializationType.GetCustomAttribute<SerializableAttribute>() != null && serializationType.FullName.StartsWith("Yafc.")) || serializationType.IsAssignableTo(typeof(ModelObject))) {
                if (!serializationType.IsAbstract && queuedTypes.Add(serializationType)) {
                    queue.Enqueue(serializationType);
                }
            }
        }
    }

    /// <summary>
    /// Assert that <paramref name="property"/> is expected to be serialized and has the expected type.
    /// </summary>
    private static void AssertPropertyType(Type type, Dictionary<string, Type> expectedProperties, PropertyInfo property, Type propertyType) {
        Assert.True(expectedProperties.Remove(property.Name, out Type expectedPropertyType),
            $"Serializing a new property '{MakeTypeName(type)}.{property.Name}'. If this is intentional, add `[nameof({MakeTypeName(type)}.{property.Name})] = typeof({MakeTypeName(property.PropertyType)}),` to the propertyTree initializer, under [typeof({MakeTypeName(type)})].");
        Assert.True(expectedPropertyType == propertyType,
            $"The type of '{MakeTypeName(type)}.{property.Name}' has changed from {MakeTypeName(expectedPropertyType)} to {MakeTypeName(propertyType)}. If this is intentional, update the property type in the propertyTree initializer, under [typeof({MakeTypeName(type)})].");
    }

    /// <summary>
    /// If appropriate, queue <paramref name="serializationType"/> for inspection of its property types.
    /// Native and native-like types do not get inspected, and each type is inspected at most once.
    /// </summary>
    private void QueueSerializedType(Type serializationType) {
        if ((serializationType.GetCustomAttribute<SerializableAttribute>() != null && serializationType.FullName.StartsWith("Yafc."))
            || (serializationType.IsAssignableTo(typeof(ModelObject)) && !serializationType.IsAbstract)) {

            if (queuedTypes.Add(serializationType)) {
                queue.Enqueue(serializationType);
            }
        }
    }

    private static string MakeTypeName(Type type) => SerializationTypeValidation.MakeTypeName(type);
}
