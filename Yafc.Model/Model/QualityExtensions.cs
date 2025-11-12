using System.Diagnostics.CodeAnalysis;

namespace Yafc.Model;

public static class QualityExtensions {
    // This method cannot be declared on the interface.
    public static void Deconstruct<T>(this IObjectWithQuality<T> value, out T obj, out Quality quality) where T : FactorioObject {
        obj = value.target;
        quality = value.quality;
    }

    public static float GetCraftingSpeed(this IObjectWithQuality<EntityCrafter> crafter) => crafter.target.CraftingSpeed(crafter.quality);

    public static float GetPower(this IObjectWithQuality<Entity> entity) => entity.target.Power(entity.quality);

    public static float GetBeaconEfficiency(this IObjectWithQuality<EntityBeacon> beacon) => beacon.target.BeaconEfficiency(beacon.quality);

    public static float GetAttractorEfficiency(this IObjectWithQuality<EntityAttractor> attractor)
        => attractor.target.Efficiency(attractor.quality);

    public static float StormPotentialPerTick(this IObjectWithQuality<EntityAttractor> attractor)
        => attractor.target.StormPotentialPerTick(attractor.quality);

    public static bool IsSourceResource(this IObjectWithQuality<Goods>? goods) => (goods?.target).IsSourceResource();
    /// <summary>
    /// Gets the string <c>target.name + "@" + quality.name</c>.
    /// </summary>
    public static string QualityName(this IObjectWithQuality<FactorioObject> obj) => obj.target.name + "@" + obj.quality.name;

    public static IObjectWithQuality<Goods>? mainProduct(this IObjectWithQuality<RecipeOrTechnology>? recipe) =>
        recipe?.target.mainProduct.With(recipe.quality);

    public static bool CanAcceptModule(this IObjectWithQuality<RecipeOrTechnology>? recipe, Module module) =>
        recipe?.target.CanAcceptModule(module) ?? false;
    public static bool CanAcceptModule(this IObjectWithQuality<RecipeOrTechnology>? recipe, IObjectWithQuality<Module> module) =>
        recipe?.target.CanAcceptModule(module.target) ?? false;

    public static IObjectWithQuality<Item>? FuelResult(this IObjectWithQuality<Goods>? goods) =>
        (goods?.target as Item)?.fuelResult.With(goods!.quality); // null-forgiving: With is not called if goods is null.

    /// <summary>
    /// Gets the <see cref="IObjectWithQuality{T}"/> representing a <see cref="FactorioObject"/> with an attached <see cref="Quality"/> modifier.
    /// </summary>
    /// <typeparam name="T">The type of the quality-oblivious object. This must be <see cref="FactorioObject"/> or a derived type.</typeparam>
    /// <param name="target">The quality-oblivious object to be converted, or <see langword="null"/> to return <see langword="null"/>.</param>
    /// <param name="quality">The desired quality. If <paramref name="target"/> does not accept quality modifiers (e.g. fluids or technologies),
    /// the return value will have normal quality regardless of the value of this parameter. This parameter must still be a valid
    /// <see cref="Quality"/> (not <see langword="null"/>), even if it will be ignored. </param>
    /// <remarks>This automatically checks for invalid combinations, such as rare <see cref="Fluid"/>s. It will instead return a normal quality
    /// object in that case.</remarks>
    /// <returns>A valid quality-aware object, respecting the limitations of what objects can exist at non-normal quality, or
    /// <see langword="null"/> if <paramref name="target"/> was <see langword="null"/>.</returns>
    [return: NotNullIfNotNull(nameof(target))]
    public static IObjectWithQuality<T>? With<T>(this T? target, Quality quality) where T : FactorioObject => ObjectWithQuality.Get(target, quality);

    /// <summary>
    /// Gets the <see cref="IObjectWithQuality{T}"/> with the same <see cref="IObjectWithQuality{T}.target"/> and the specified
    /// <see cref="Quality"/>.
    /// </summary>
    /// <typeparam name="T">The corresponding quality-oblivious type. This must be <see cref="FactorioObject"/> or a derived type.</typeparam>
    /// <param name="target">An object containing the desired <see cref="IObjectWithQuality{T}.target"/>, or <see langword="null"/> to return
    /// <see langword="null"/>.</param>
    /// <param name="quality">The desired quality. If <paramref name="target"/>.<see cref="IObjectWithQuality{T}.target">target</see> does not
    /// accept quality modifiers (e.g. fluids or technologies), the return value will have normal quality regardless of the value of this
    /// parameter.</param>
    /// <remarks>This automatically checks for invalid combinations, such as rare <see cref="Fluid"/>s. It will instead return a normal quality
    /// object in that case.</remarks>
    /// <returns>A valid quality-aware object, respecting the limitations of what objects can exist at non-normal quality, or
    /// <see langword="null"/> if <paramref name="target"/> was <see langword="null"/>.</returns>
    [return: NotNullIfNotNull(nameof(target))]
    public static IObjectWithQuality<T>? With<T>(this IObjectWithQuality<T>? target, Quality quality) where T : FactorioObject
        => ObjectWithQuality.Get(target?.target, quality);

    /// <summary>
    /// Tests whether an <see cref="IObjectWithQuality{T}"/> can be converted to one with a different generic parameter.
    /// </summary>
    /// <typeparam name="T">The desired type parameter for the <see cref="IObjectWithQuality{T}"/> conversion to be tested.</typeparam>
    /// <param name="target">The input <see cref="IObjectWithQuality{T}"/> to be tested.</param>
    /// <returns><see langword="true"/> if the conversion is possible, or <see langword="false"/> if it was not.</returns>
    public static bool Is<T>([NotNullWhen(true)] this IObjectWithQuality<FactorioObject>? target) where T : FactorioObject => target?.target is T;
}
