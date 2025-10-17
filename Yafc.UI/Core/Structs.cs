namespace Yafc.UI;

public enum SchemeColor {
    // Special colors
    None,
    TextSelection,
    Link,
    Reserved1,
    // Pure colors
    PureBackground, // White with light theme, black with dark
    PureForeground, // Black with light theme, white with dark
    Source, // Always white
    SourceFaint,
    // Background group
    Background,
    BackgroundAlt,
    BackgroundText,
    BackgroundTextFaint,
    BackgroundIcon,
    BackgroundIconAlt,
    // Primary group
    Primary,
    PrimaryAlt,
    PrimaryText,
    PrimaryTextFaint,
    PrimaryIcon,
    PrimaryIconAlt,
    // Secondary group
    Secondary,
    SecondaryAlt,
    SecondaryText,
    SecondaryTextFaint,
    SecondaryIcon,
    SecondaryIconAlt,
    // Error group
    Error,
    ErrorAlt,
    ErrorText,
    ErrorTextFaint,
    ErrorIcon,
    ErrorIconAlt,
    // Grey group
    Grey,
    GreyAlt,
    GreyText,
    GreyTextFaint,
    GreyIcon,
    GreyIconAlt,
    // Magenta group (indicate overproduction)
    Magenta,
    MagentaAlt,
    MagentaText,
    MagentaTextFaint,
    MagentaIcon,
    MagentaIconAlt,
    // Green group
    Green,
    GreenAlt,
    GreenText,
    GreenTextFaint,
    GreenIcon,
    GreenIconAlt,
    // Warning group (for warning icons like "!" triangle)
    Warning,
    WarningAlt,
    WarningText,
    WarningTextFaint,
    WarningIcon,
    WarningIconAlt,
    // Tagged row colors
    TagColorGreen,
    TagColorGreenAlt,
    TagColorGreenText,
    TagColorGreenTextFaint,
    TagColorGreenIcon,
    TagColorGreenIconAlt,
    TagColorYellow,
    TagColorYellowAlt,
    TagColorYellowText,
    TagColorYellowTextFaint,
    TagColorYellowIcon,
    TagColorYellowIconAlt,
    TagColorRed,
    TagColorRedAlt,
    TagColorRedText,
    TagColorRedTextFaint,
    TagColorRedIcon,
    TagColorRedIconAlt,
    TagColorBlue,
    TagColorBlueAlt,
    TagColorBlueText,
    TagColorBlueTextFaint,
    TagColorBlueIcon,
    TagColorBlueIconAlt
}

public enum SchemeColorGroup {
    Pure = SchemeColor.PureBackground,
    Background = SchemeColor.Background,
    Primary = SchemeColor.Primary,
    Secondary = SchemeColor.Secondary,
    Error = SchemeColor.Error,
    Grey = SchemeColor.Grey,
    Magenta = SchemeColor.Magenta,
    Green = SchemeColor.Green,
    TagColorGreen = SchemeColor.TagColorGreen,
    TagColorYellow = SchemeColor.TagColorYellow,
    TagColorRed = SchemeColor.TagColorRed,
    TagColorBlue = SchemeColor.TagColorBlue,
}

public enum RectangleBorder {
    None,
    Thin,
    Full
}

public enum Icon {
    None,
    Check,
    CheckBoxCheck,
    CheckBoxEmpty,
    Close,
    Edit,
    Folder,
    FolderOpen,
    Help,
    NewFolder,
    Plus,
    Refresh,
    Search,
    Settings,
    Time,
    Upload,
    RadioEmpty,
    RadioCheck,
    Error,
    Warning,
    Add,
    DropDown,
    ChevronDown,
    ChevronUp,
    ChevronRight,
    Menu,
    ArrowRight,
    ArrowDownRight,
    Empty,
    DarkMode,
    Copy,
    Delete,
    OpenNew,
    StarEmpty,
    StarFull,

    FirstCustom,
}

public enum RectAlignment {
    Full,
    MiddleLeft,
    Middle,
    MiddleFullRow,
    MiddleRight,
    UpperCenter
}

public readonly struct Padding {
    public readonly float left, right, top, bottom;

    public Padding(float allOffsets) => top = bottom = left = right = allOffsets;

    public Padding(float leftRight, float topBottom) {
        left = right = leftRight;
        top = bottom = topBottom;
    }

    public Padding(float left, float right, float top, float bottom) {
        this.left = left;
        this.right = right;
        this.top = top;
        this.bottom = bottom;
    }
}
