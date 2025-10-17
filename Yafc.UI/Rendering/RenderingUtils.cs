using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using SDL2;

namespace Yafc.UI;

public static class RenderingUtils {
    public static readonly IntPtr cursorCaret = SDL.SDL_CreateSystemCursor(SDL.SDL_SystemCursor.SDL_SYSTEM_CURSOR_IBEAM);
    public static readonly IntPtr cursorArrow = SDL.SDL_CreateSystemCursor(SDL.SDL_SystemCursor.SDL_SYSTEM_CURSOR_ARROW);
    public static readonly IntPtr cursorHand = SDL.SDL_CreateSystemCursor(SDL.SDL_SystemCursor.SDL_SYSTEM_CURSOR_HAND);
    public static readonly IntPtr cursorHorizontalResize = SDL.SDL_CreateSystemCursor(SDL.SDL_SystemCursor.SDL_SYSTEM_CURSOR_SIZEWE);
    public const byte SEMITRANSPARENT = 100;

    private static SDL.SDL_Color ColorFromHex(int hex) => new SDL.SDL_Color { r = (byte)(hex >> 16), g = (byte)(hex >> 8), b = (byte)hex, a = 255 };

    public static readonly SDL.SDL_Color Black = new SDL.SDL_Color { a = 255 };
    public static readonly SDL.SDL_Color White = new SDL.SDL_Color { r = 255, g = 255, b = 255, a = 255 };
    public static readonly SDL.SDL_Color MutedGrey = new SDL.SDL_Color { r = 0xA0, g = 0xA0, b = 0xA0, a = 255 };
    public static readonly SDL.SDL_Color BlackTransparent = new SDL.SDL_Color { a = SEMITRANSPARENT };
    public static readonly SDL.SDL_Color WhiteTransparent = new SDL.SDL_Color { r = 255, g = 255, b = 255, a = SEMITRANSPARENT };


    // Color scheme definition - organized by SchemeColor enum order for clarity
    private static readonly SDL.SDL_Color[] LightModeScheme = BuildLightModeScheme();
    private static readonly SDL.SDL_Color[] DarkModeScheme = BuildDarkModeScheme();

    private static SDL.SDL_Color[] BuildLightModeScheme() {
        // Automatically determine array size from the largest enum value
        var maxEnumValue = Enum.GetValues<SchemeColor>().Max();
        var colors = new SDL.SDL_Color[(int)maxEnumValue + 1];

        // Local helper function to set colors cleanly
        void Set(SchemeColor schemeColor, SDL.SDL_Color color) => colors[(int)schemeColor] = color;

        // Special colors (SchemeColor.None through SchemeColor.Reserved1)
        Set(SchemeColor.None, default);
        Set(SchemeColor.TextSelection, new SDL.SDL_Color { b = 255, g = 128, a = 60 });
        Set(SchemeColor.Link, ColorFromHex(0x0645AD));
        Set(SchemeColor.Reserved1, ColorFromHex(0x1b5e20));

        // Pure colors (always the same regardless of theme)
        Set(SchemeColor.PureBackground, White);
        Set(SchemeColor.PureForeground, Black);
        Set(SchemeColor.Source, White);
        Set(SchemeColor.SourceFaint, WhiteTransparent);

        // Background group - main UI background colors
        Set(SchemeColor.Background, ColorFromHex(0xf4f4f4));        // Light grey background
        Set(SchemeColor.BackgroundAlt, White);                      // White alternative
        Set(SchemeColor.BackgroundText, Black);                     // Black text on background
        Set(SchemeColor.BackgroundTextFaint, BlackTransparent);     // Faded black text
        Set(SchemeColor.BackgroundIcon, Black);                     // Black icons on background
        Set(SchemeColor.BackgroundIconAlt, ColorFromHex(0x666666)); // Grey icons on background

        // Primary group - main accent colors (cyan/teal)
        Set(SchemeColor.Primary, ColorFromHex(0x26c6da));           // Light cyan
        Set(SchemeColor.PrimaryAlt, ColorFromHex(0x0095a8));        // Darker cyan
        Set(SchemeColor.PrimaryText, Black);                        // Black text on primary
        Set(SchemeColor.PrimaryTextFaint, BlackTransparent);        // Faded black text
        Set(SchemeColor.PrimaryIcon, ColorFromHex(0x26c6da));       // Cyan icons
        Set(SchemeColor.PrimaryIconAlt, ColorFromHex(0x0095a8));    // Darker cyan icons

        // Secondary group - warning/attention colors (orange)
        Set(SchemeColor.Secondary, ColorFromHex(0xff9800));         // Orange - used for solver issues
        Set(SchemeColor.SecondaryAlt, ColorFromHex(0xc66900));      // Darker orange
        Set(SchemeColor.SecondaryText, Black);                      // Black text on secondary
        Set(SchemeColor.SecondaryTextFaint, BlackTransparent);      // Faded black text
        Set(SchemeColor.SecondaryIcon, ColorFromHex(0xff9800));     // Orange icons
        Set(SchemeColor.SecondaryIconAlt, ColorFromHex(0xc66900));  // Darker orange icons

        // Error group - critical error colors (red)
        Set(SchemeColor.Error, ColorFromHex(0xbf360c));             // Bright red - critical issues
        Set(SchemeColor.ErrorAlt, ColorFromHex(0x870000));          // Dark red - deadlock/overproduction
        Set(SchemeColor.ErrorText, White);                          // White text on error
        Set(SchemeColor.ErrorTextFaint, WhiteTransparent);          // Faded white text
        Set(SchemeColor.ErrorIcon, ColorFromHex(0xf44336));         // Bright red icons
        Set(SchemeColor.ErrorIconAlt, ColorFromHex(0xd32f2f));      // Darker red icons

        // Grey group - neutral/disabled colors
        Set(SchemeColor.Grey, ColorFromHex(0xe4e4e4));              // Light grey
        Set(SchemeColor.GreyAlt, ColorFromHex(0xc4c4c4));           // Medium grey
        Set(SchemeColor.GreyText, Black);                           // Black text on grey
        Set(SchemeColor.GreyTextFaint, BlackTransparent);           // Faded black text
        Set(SchemeColor.GreyIcon, ColorFromHex(0x757575));          // Medium grey icons
        Set(SchemeColor.GreyIconAlt, ColorFromHex(0x616161));       // Darker grey icons

        // Magenta group - overproduction indication
        Set(SchemeColor.Magenta, ColorFromHex(0xbd33a4));           // Magenta - overproduction
        Set(SchemeColor.MagentaAlt, ColorFromHex(0x8b008b));        // Dark magenta
        Set(SchemeColor.MagentaText, Black);                        // Black text on magenta
        Set(SchemeColor.MagentaTextFaint, BlackTransparent);        // Faded black text
        Set(SchemeColor.MagentaIcon, ColorFromHex(0xe91e63));       // Bright magenta icons
        Set(SchemeColor.MagentaIconAlt, ColorFromHex(0xc2185b));    // Darker magenta icons

        // Green group - success/positive colors
        Set(SchemeColor.Green, ColorFromHex(0x6abf69));             // Light green
        Set(SchemeColor.GreenAlt, ColorFromHex(0x388e3c));          // Dark green
        Set(SchemeColor.GreenText, Black);                          // Black text on green
        Set(SchemeColor.GreenTextFaint, BlackTransparent);          // Faded black text
        Set(SchemeColor.GreenIcon, ColorFromHex(0x4caf50));         // Bright green for icons
        Set(SchemeColor.GreenIconAlt, ColorFromHex(0x388e3c));      // Darker green for icons

        // Warning group - for warning icons like "!" triangle
        Set(SchemeColor.Warning, ColorFromHex(0xff9800));           // Orange background for warnings
        Set(SchemeColor.WarningAlt, ColorFromHex(0xc66900));        // Darker orange
        Set(SchemeColor.WarningText, Black);                        // Black text on warning
        Set(SchemeColor.WarningTextFaint, BlackTransparent);        // Faded black text
        Set(SchemeColor.WarningIcon, ColorFromHex(0xff9800));       // Bright orange for warning icons
        Set(SchemeColor.WarningIconAlt, ColorFromHex(0xf57c00));    // Darker orange for warning icons

        // Tagged row colors - for row highlighting/categorization
        // Green tag
        Set(SchemeColor.TagColorGreen, ColorFromHex(0xe8ffe8));
        Set(SchemeColor.TagColorGreenAlt, ColorFromHex(0xefffef));
        Set(SchemeColor.TagColorGreenText, ColorFromHex(0x56ad65));
        Set(SchemeColor.TagColorGreenTextFaint, ColorFromHex(0x0));
        Set(SchemeColor.TagColorGreenIcon, ColorFromHex(0x4caf50));        // Bright green for icons
        Set(SchemeColor.TagColorGreenIconAlt, ColorFromHex(0x388e3c));     // Darker green for icons

        // Yellow tag
        Set(SchemeColor.TagColorYellow, ColorFromHex(0xffffe8));
        Set(SchemeColor.TagColorYellowAlt, ColorFromHex(0xffffef));
        Set(SchemeColor.TagColorYellowText, ColorFromHex(0x8c8756));
        Set(SchemeColor.TagColorYellowTextFaint, ColorFromHex(0x0));
        Set(SchemeColor.TagColorYellowIcon, ColorFromHex(0xffeb3b));       // Bright yellow for icons
        Set(SchemeColor.TagColorYellowIconAlt, ColorFromHex(0xfbc02d));    // Darker yellow for icons

        // Red tag
        Set(SchemeColor.TagColorRed, ColorFromHex(0xffe8e8));
        Set(SchemeColor.TagColorRedAlt, ColorFromHex(0xffefef));
        Set(SchemeColor.TagColorRedText, ColorFromHex(0xaa5555));
        Set(SchemeColor.TagColorRedTextFaint, ColorFromHex(0x0));
        Set(SchemeColor.TagColorRedIcon, ColorFromHex(0xf44336));          // Bright red for icons
        Set(SchemeColor.TagColorRedIconAlt, ColorFromHex(0xd32f2f));       // Darker red for icons

        // Blue tag
        Set(SchemeColor.TagColorBlue, ColorFromHex(0xe8efff));
        Set(SchemeColor.TagColorBlueAlt, ColorFromHex(0xeff4ff));
        Set(SchemeColor.TagColorBlueText, ColorFromHex(0x526ea5));
        Set(SchemeColor.TagColorBlueTextFaint, ColorFromHex(0x0));
        Set(SchemeColor.TagColorBlueIcon, ColorFromHex(0x2196f3));         // Bright blue for icons
        Set(SchemeColor.TagColorBlueIconAlt, ColorFromHex(0x1976d2));      // Darker blue for icons

        return colors;
    }

    private static SDL.SDL_Color[] BuildDarkModeScheme() {
        // Automatically determine array size from the largest enum value
        var maxEnumValue = Enum.GetValues<SchemeColor>().Max();
        var colors = new SDL.SDL_Color[(int)maxEnumValue + 1];

        // Local helper function to set colors cleanly
        void Set(SchemeColor schemeColor, SDL.SDL_Color color) => colors[(int)schemeColor] = color;

        // Special colors (SchemeColor.None through SchemeColor.Reserved1)
        Set(SchemeColor.None, default);
        Set(SchemeColor.TextSelection, new SDL.SDL_Color { b = 255, g = 128, a = 120 });
        Set(SchemeColor.Link, ColorFromHex(0xff9800));
        Set(SchemeColor.Reserved1, ColorFromHex(0x1b5e20));

        // Pure colors (always the same regardless of theme)
        Set(SchemeColor.PureBackground, Black);
        Set(SchemeColor.PureForeground, White);
        Set(SchemeColor.Source, White);
        Set(SchemeColor.SourceFaint, WhiteTransparent);

        // Background group - main UI background colors
        Set(SchemeColor.Background, ColorFromHex(0x141414));        // Dark grey background
        Set(SchemeColor.BackgroundAlt, Black);                      // Black alternative
        Set(SchemeColor.BackgroundText, White);                     // White text on background
        Set(SchemeColor.BackgroundTextFaint, WhiteTransparent);     // Faded white text
        Set(SchemeColor.BackgroundIcon, White);                     // White icons on dark background
        Set(SchemeColor.BackgroundIconAlt, ColorFromHex(0xcccccc)); // Light grey icons on dark background

        // Primary group - main accent colors (cyan/teal)
        Set(SchemeColor.Primary, ColorFromHex(0x006978));           // Dark cyan
        Set(SchemeColor.PrimaryAlt, ColorFromHex(0x0097a7));        // Lighter cyan
        Set(SchemeColor.PrimaryText, White);                        // White text on primary
        Set(SchemeColor.PrimaryTextFaint, WhiteTransparent);        // Faded white text
        Set(SchemeColor.PrimaryIcon, ColorFromHex(0x4dd0e1));       // Bright cyan icons for dark mode
        Set(SchemeColor.PrimaryIconAlt, ColorFromHex(0x26c6da));    // Lighter cyan icons for dark mode

        // Secondary group - warning/attention colors (orange)
        Set(SchemeColor.Secondary, ColorFromHex(0x5b2800));         // Dark orange - used for solver issues
        Set(SchemeColor.SecondaryAlt, ColorFromHex(0x8c5100));      // Lighter orange
        Set(SchemeColor.SecondaryText, White);                      // White text on secondary
        Set(SchemeColor.SecondaryTextFaint, WhiteTransparent);      // Faded white text
        Set(SchemeColor.SecondaryIcon, ColorFromHex(0xff9800));     // Bright orange icons for dark mode
        Set(SchemeColor.SecondaryIconAlt, ColorFromHex(0xffa726));  // Lighter orange icons for dark mode

        // Error group - critical error colors (red)
        Set(SchemeColor.Error, ColorFromHex(0xbf360c));             // Bright red - critical issues
        Set(SchemeColor.ErrorAlt, ColorFromHex(0x870000));          // Dark red - deadlock/overproduction
        Set(SchemeColor.ErrorText, White);                          // White text on error
        Set(SchemeColor.ErrorTextFaint, WhiteTransparent);          // Faded white text
        Set(SchemeColor.ErrorIcon, ColorFromHex(0xef5350));         // Bright red icons for dark mode
        Set(SchemeColor.ErrorIconAlt, ColorFromHex(0xf44336));      // Lighter red icons for dark mode

        // Grey group - neutral/disabled colors
        Set(SchemeColor.Grey, ColorFromHex(0x343434));              // Dark grey
        Set(SchemeColor.GreyAlt, ColorFromHex(0x545454));           // Lighter grey
        Set(SchemeColor.GreyText, White);                           // White text on grey
        Set(SchemeColor.GreyTextFaint, WhiteTransparent);           // Faded white text
        Set(SchemeColor.GreyIcon, ColorFromHex(0xbdbdbd));          // Light grey icons for dark mode
        Set(SchemeColor.GreyIconAlt, ColorFromHex(0x9e9e9e));       // Medium grey icons for dark mode

        // Magenta group - overproduction indication
        Set(SchemeColor.Magenta, ColorFromHex(0x8b008b));           // Dark magenta - overproduction
        Set(SchemeColor.MagentaAlt, ColorFromHex(0xbd33a4));        // Light magenta
        Set(SchemeColor.MagentaText, Black);                        // Black text on magenta
        Set(SchemeColor.MagentaTextFaint, BlackTransparent);        // Faded black text
        Set(SchemeColor.MagentaIcon, ColorFromHex(0xf06292));       // Bright magenta icons for dark mode
        Set(SchemeColor.MagentaIconAlt, ColorFromHex(0xe91e63));    // Lighter magenta icons for dark mode

        // Green group - success/positive colors
        Set(SchemeColor.Green, ColorFromHex(0x00600f));             // Dark green
        Set(SchemeColor.GreenAlt, ColorFromHex(0x00701a));          // Lighter green
        Set(SchemeColor.GreenText, Black);                          // Black text on green
        Set(SchemeColor.GreenTextFaint, BlackTransparent);          // Faded black text
        Set(SchemeColor.GreenIcon, ColorFromHex(0x66bb6a));         // Bright green for dark mode icons
        Set(SchemeColor.GreenIconAlt, ColorFromHex(0x4caf50));      // Lighter green for dark mode icons

        // Warning group - for warning icons like "!" triangle
        Set(SchemeColor.Warning, ColorFromHex(0x5b2800));           // Dark orange background for warnings
        Set(SchemeColor.WarningAlt, ColorFromHex(0x8c5100));        // Lighter orange
        Set(SchemeColor.WarningText, White);                        // White text on warning
        Set(SchemeColor.WarningTextFaint, WhiteTransparent);        // Faded white text
        Set(SchemeColor.WarningIcon, ColorFromHex(0xf36a00));       // Bright orange for warning icons in dark mode
        Set(SchemeColor.WarningIconAlt, ColorFromHex(0xff9300));    // Lighter orange for warning icons in dark mode

        // Tagged row colors - for row highlighting/categorization
        // Green tag
        Set(SchemeColor.TagColorGreen, ColorFromHex(0x0d261f));
        Set(SchemeColor.TagColorGreenAlt, ColorFromHex(0x050f0c));
        // Set(SchemeColor.TagColorGreenText, ColorFromHex(0x226355));
        Set(SchemeColor.TagColorGreenText, MutedGrey);
        Set(SchemeColor.TagColorGreenTextFaint, ColorFromHex(0x0));
        Set(SchemeColor.TagColorGreenIcon, ColorFromHex(0x66bb6a));        // Bright green for dark mode icons
        Set(SchemeColor.TagColorGreenIconAlt, ColorFromHex(0x4caf50));     // Lighter green for dark mode icons

        // Yellow tag
        Set(SchemeColor.TagColorYellow, ColorFromHex(0x28260b));
        Set(SchemeColor.TagColorYellowAlt, ColorFromHex(0x191807));
        // Set(SchemeColor.TagColorYellowText, ColorFromHex(0x5b582a));
        Set(SchemeColor.TagColorYellowText, MutedGrey);
        Set(SchemeColor.TagColorYellowTextFaint, ColorFromHex(0x0));
        Set(SchemeColor.TagColorYellowIcon, ColorFromHex(0xffee58));       // Bright yellow for dark mode icons
        Set(SchemeColor.TagColorYellowIconAlt, ColorFromHex(0xffeb3b));    // Lighter yellow for dark mode icons

        // Red tag
        Set(SchemeColor.TagColorRed, ColorFromHex(0x270c0c));
        Set(SchemeColor.TagColorRedAlt, ColorFromHex(0x190808));
        // Set(SchemeColor.TagColorRedText, ColorFromHex(0x922626));
        Set(SchemeColor.TagColorRedText, MutedGrey);
        Set(SchemeColor.TagColorRedTextFaint, ColorFromHex(0x0));
        Set(SchemeColor.TagColorRedIcon, ColorFromHex(0xef5350));          // Bright red for dark mode icons
        Set(SchemeColor.TagColorRedIconAlt, ColorFromHex(0xf44336));       // Lighter red for dark mode icons

        // Blue tag
        Set(SchemeColor.TagColorBlue, ColorFromHex(0x0c0c27));
        Set(SchemeColor.TagColorBlueAlt, ColorFromHex(0x080819));
        // Set(SchemeColor.TagColorBlueText, ColorFromHex(0x2626ab));
        Set(SchemeColor.TagColorBlueText, MutedGrey);
        Set(SchemeColor.TagColorBlueTextFaint, ColorFromHex(0x0));
        Set(SchemeColor.TagColorBlueIcon, ColorFromHex(0x42a5f5));         // Bright blue for dark mode icons
        Set(SchemeColor.TagColorBlueIconAlt, ColorFromHex(0x2196f3));      // Lighter blue for dark mode icons

        return colors;
    }

    private static SDL.SDL_Color[] SchemeColors = LightModeScheme;

    public static void SetColorScheme(bool darkMode) {
        RenderingUtils.darkMode = darkMode;
        SchemeColors = darkMode ? DarkModeScheme : LightModeScheme;
        Ui.ColorSchemeChanged();
        byte col = darkMode ? (byte)0 : (byte)255;
        _ = SDL.SDL_SetSurfaceColorMod(CircleSurface, col, col, col);
    }

    public static SDL.SDL_Color ToSdlColor(this SchemeColor color) => SchemeColors[(int)color];

    public static SDL.SDL_Color ToSdlColor(this (SchemeColor color, bool drawTransparent) color) {
        if (color.drawTransparent) {
            return SchemeColors[(int)color.color] with { a = SEMITRANSPARENT };
        }
        return SchemeColors[(int)color.color];
    }

    public static unsafe ref SDL.SDL_Surface AsSdlSurface(IntPtr ptr) => ref Unsafe.AsRef<SDL.SDL_Surface>((void*)ptr);

    public static readonly IntPtr CircleSurface;
    private static readonly SDL.SDL_Rect CircleTopLeft, CircleTopRight, CircleBottomLeft, CircleBottomRight, CircleTop, CircleBottom, CircleLeft, CircleRight;
    public static bool darkMode { get; private set; }

    static unsafe RenderingUtils() {
        nint surfacePtr = SDL.SDL_CreateRGBSurfaceWithFormat(0, 32, 32, 0, SDL.SDL_PIXELFORMAT_RGBA8888);
        ref var surface = ref AsSdlSurface(surfacePtr);

        const int circleSize = 32;
        const float center = (circleSize - 1) / 2f;

        uint* pixels = (uint*)surface.pixels;

        for (int x = 0; x < 32; x++) {
            for (int y = 0; y < 32; y++) {
                float dx = (center - x) / center;
                float dy = (center - y) / center;
                float distance = MathF.Sqrt((dx * dx) + (dy * dy));
                *pixels++ = 0xFFFFFF00 | (distance >= 1f ? 0 : (uint)MathUtils.Round(38 * (1f - distance)));
            }
        }

        const int halfCircle = (circleSize / 2) - 1;
        const int halfStride = circleSize - halfCircle;

        CircleTopLeft = new SDL.SDL_Rect { x = 0, y = 0, w = halfCircle, h = halfCircle };
        CircleTopRight = new SDL.SDL_Rect { x = halfStride, y = 0, w = halfCircle, h = halfCircle };
        CircleBottomLeft = new SDL.SDL_Rect { x = 0, y = halfStride, w = halfCircle, h = halfCircle };
        CircleBottomRight = new SDL.SDL_Rect { x = halfStride, y = halfStride, w = halfCircle, h = halfCircle };
        CircleTop = new SDL.SDL_Rect { x = halfCircle, y = 0, w = 2, h = halfCircle };
        CircleBottom = new SDL.SDL_Rect { x = halfCircle, y = halfStride, w = 2, h = halfCircle };
        CircleLeft = new SDL.SDL_Rect { x = 0, y = halfCircle, w = halfCircle, h = 2 };
        CircleRight = new SDL.SDL_Rect { x = halfStride, y = halfCircle, w = halfCircle, h = 2 };
        CircleSurface = surfacePtr;
        _ = SDL.SDL_SetSurfaceColorMod(CircleSurface, 0, 0, 0);
    }

    public struct BlitMapping(SDL.SDL_Rect texture, SDL.SDL_Rect position) {
        public SDL.SDL_Rect position = position;
        public SDL.SDL_Rect texture = texture;
    }

    public static void GetBorderParameters(float unitsToPixels, RectangleBorder border, out int top, out int side, out int bottom) {
        if (border == RectangleBorder.Full) {
            top = MathUtils.Round(unitsToPixels * 0.5f);
            side = MathUtils.Round(unitsToPixels);
            bottom = MathUtils.Round(unitsToPixels * 2f);
        }
        else {
            top = MathUtils.Round(unitsToPixels * 0.2f);
            side = MathUtils.Round(unitsToPixels * 0.3f);
            bottom = MathUtils.Round(unitsToPixels * 0.5f);
        }
    }

    public static void GetBorderBatch(SDL.SDL_Rect position, int shadowTop, int shadowSide, int shadowBottom, [NotNull] ref BlitMapping[]? result) {
        if (result == null || result.Length != 8) {
            Array.Resize(ref result, 8);
        }

        SDL.SDL_Rect rect = new SDL.SDL_Rect { h = shadowTop, x = position.x - shadowSide, y = position.y - shadowTop, w = shadowSide };
        result[0] = new BlitMapping(CircleTopLeft, rect);
        rect.x = position.x;
        rect.w = position.w;
        result[1] = new BlitMapping(CircleTop, rect);
        rect.x += rect.w;
        rect.w = shadowSide;
        result[2] = new BlitMapping(CircleTopRight, rect);
        rect.y = position.y;
        rect.h = position.h;
        result[3] = new BlitMapping(CircleRight, rect);
        rect.y += rect.h;
        rect.h = shadowBottom;
        result[4] = new BlitMapping(CircleBottomRight, rect);
        rect.x = position.x;
        rect.w = position.w;
        result[5] = new BlitMapping(CircleBottom, rect);
        rect.x -= shadowSide;
        rect.w = shadowSide;
        result[6] = new BlitMapping(CircleBottomLeft, rect);
        rect.y = position.y;
        rect.h = position.h;
        result[7] = new BlitMapping(CircleLeft, rect);
    }
}
