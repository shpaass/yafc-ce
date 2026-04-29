using Yafc.Model;
using Yafc.UI;

namespace Yafc;

/// <summary>
/// Extension methods that bridge domain objects (<see cref="FactorioObject"/>) to their
/// UI representation (<see cref="Icon"/>), keeping the domain layer free of UI concerns.
/// This bridge currently lives in the app layer because <c>Yafc.UI</c> does not reference
/// <c>Yafc.Model</c>; move it once dependency boundaries are reworked.
/// </summary>
public static class FactorioObjectIconExtensions {
    /// <summary>
    /// Returns the <see cref="Icon"/> value associated with this <see cref="FactorioObject"/>.
    /// The domain object stores this as an integer handle (<see cref="FactorioObject.iconId"/>);
    /// this method converts it to the <see cref="Icon"/> enum for rendering.
    /// </summary>
    public static Icon GetIcon(this FactorioObject obj) => (Icon)obj.iconId;
}
