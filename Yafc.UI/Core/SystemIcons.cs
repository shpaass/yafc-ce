namespace Yafc.UI;

/// <summary>
/// Holds the set of game-sourced system icons (e.g. fuel, warning, hand) that are
/// rendered from Factorio's own sprite files during the data loading phase.
/// These are distinct from the built-in YAFC vector icons defined in <see cref="Icon"/>.
/// </summary>
public static class SystemIcons {
    /// <summary>
    /// The icon used to indicate a missing or invalid fuel source.
    /// Set during data loading; no UI consumer yet (pre-existing).
    /// </summary>
    public static Icon NoFuelIcon { get; private set; }

    /// <summary>
    /// The icon used to display a general warning.
    /// Set during data loading; no UI consumer yet (pre-existing).
    /// </summary>
    public static Icon WarningIcon { get; private set; }

    /// <summary>The hand/grab icon used to mark certain special goods (e.g. void energy).</summary>
    public static Icon HandIcon { get; private set; }

    /// <summary>
    /// Initializes the set of game-sourced system icons after they are rendered from Factorio assets.
    /// </summary>
    public static void Initialize(Icon noFuelIcon, Icon warningIcon, Icon handIcon) {
        NoFuelIcon = noFuelIcon;
        WarningIcon = warningIcon;
        HandIcon = handIcon;
    }
}

