using Yafc.I18n;
using Yafc.UI;

namespace Yafc;

public class AboutScreen : WindowUtility {
    public const string Github = "https://github.com/have-fun-was-taken/yafc-ce";

    public AboutScreen(Window parent) : base(ImGuiUtils.DefaultScreenPadding) => Create(LSs.AboutYafc, 50, parent);

    protected override void BuildContents(ImGui gui) {
        gui.allocator = RectAllocator.Center;
        gui.BuildText(LSs.FullName, new TextBlockDisplayStyle(Font.header, Alignment: RectAlignment.Middle));
        gui.BuildText(LSs.AboutCommunityEdition, TextBlockDisplayStyle.Centered);
        gui.BuildText(LSs.AboutCopyrightShadow, TextBlockDisplayStyle.Centered);
        gui.BuildText(LSs.AboutCopyrightCommunity, TextBlockDisplayStyle.Centered);
        gui.allocator = RectAllocator.LeftAlign;
        gui.AllocateSpacing(1.5f);

        gui.BuildText(LSs.AboutCopyleftGpl3, TextBlockDisplayStyle.WrappedText);

        gui.BuildText(LSs.AboutWarrantyDisclaimer, TextBlockDisplayStyle.WrappedText);

        using (gui.EnterRow(0.3f)) {
            gui.BuildText(LSs.AboutFullLicenseText);
            BuildLink(gui, "https://gnu.org/licenses/gpl-3.0.html");
        }

        using (gui.EnterRow(0.3f)) {
            gui.BuildText(LSs.AboutGithubPage);
            BuildLink(gui, Github);
        }

        gui.AllocateSpacing(1.5f);
        gui.BuildText(LSs.AboutLibraries, Font.subheader);
        BuildLink(gui, "https://dotnet.microsoft.com/", LSs.AboutDotNetCore);

        using (gui.EnterRow(0.3f)) {
            BuildLink(gui, "https://libsdl.org/index.php", "Simple DirectMedia Layer 2.0");
            gui.BuildText(LSs.AboutAnd);
            BuildLink(gui, "https://github.com/flibitijibibo/SDL2-CS", "SDL2-CS");
        }

        using (gui.EnterRow(0.3f)) {
            gui.BuildText(LSs.AboutSdl2Libraries);
            BuildLink(gui, "http://libpng.org/pub/png/libpng.html", "libpng,");
            BuildLink(gui, "http://libjpeg.sourceforge.net/", "libjpeg,");
            BuildLink(gui, "https://freetype.org", "libfreetype");
            gui.BuildText(LSs.AboutAnd);
            BuildLink(gui, "https://zlib.net/", "zlib");
        }

        using (gui.EnterRow(0.3f)) {
            gui.BuildText("Google");
            BuildLink(gui, "https://developers.google.com/optimization", "OR-Tools,");
            BuildLink(gui, "https://fonts.google.com/specimen/Roboto", LSs.AboutRobotoFontFamily);
            BuildLink(gui, "https://fonts.google.com/noto", LSs.AboutNotoSansFamily);
            gui.BuildText(LSs.AboutAnd);
            BuildLink(gui, "https://material.io/resources/icons", LSs.AboutMaterialDesignIcon);
        }

        using (gui.EnterRow(0.3f)) {
            BuildLink(gui, "https://lua.org/", "Lua 5.2");
            gui.BuildText(LSs.AboutPlus);
            BuildLink(gui, "https://github.com/pkulchenko/serpent", LSs.AboutSerpentLibrary);
            gui.BuildText(LSs.AboutAndSmallBits);
            BuildLink(gui, "https://github.com/NLua", "NLua");
        }

        using (gui.EnterRow(0.3f)) {
            BuildLink(gui, "https://wiki.factorio.com/", LSs.AboutFactorioWiki);
            gui.BuildText(LSs.AboutAnd);
            BuildLink(gui, "https://lua-api.factorio.com/latest/", LSs.AboutFactorioLuaApi);
        }

        gui.AllocateSpacing(1.5f);
        gui.allocator = RectAllocator.Center;
        gui.BuildText(LSs.AboutFactorioTrademarkDisclaimer);
        BuildLink(gui, "https://factorio.com/");
    }

    private static void BuildLink(ImGui gui, string url, string? text = null) {
        if (gui.BuildLink(text ?? url)) {
            Ui.VisitLink(url);
        }
    }
}
