using System;
using System.IO;
using Yafc.I18n;
using Yafc.Model;
using Yafc.Parser;
using Yafc.UI;

namespace Yafc;

public static class Program {
    internal static bool hasOverriddenFont { get; private set; }

    private static void Main(string[] args) {
        YafcLib.RegisterDefaultAnalysis();
        Ui.Start();

        // This must happen before Preferences.Instance, where we load the prefs file and the requested translation.
        FactorioDataSource.LoadYafcLocale("en");

        string? overrideFont = Preferences.Instance.overrideFont;
        FontFile? overriddenFontFile = null;

        try {
            if (!string.IsNullOrEmpty(overrideFont) && File.Exists(overrideFont)) {
                overriddenFontFile = new FontFile(overrideFont);
            }
        }
        catch (Exception ex) {
            Console.Error.WriteException(ex);
        }

        string baseFileName = "Roboto";
        if (WelcomeScreen.languageMapping.TryGetValue(Preferences.Instance.language, out LanguageInfo? language)) {
            if (Font.FilesExist(language.BaseFontName)) {
                baseFileName = language.BaseFontName;
            }
        }

        hasOverriddenFont = overriddenFontFile != null;
        Font.header = new Font(overriddenFontFile ?? new FontFile($"Data/{baseFileName}-Light.ttf"), 2f);
        var regular = overriddenFontFile ?? new FontFile($"Data/{baseFileName}-Regular.ttf");
        Font.subheader = new Font(regular, 1.5f);
        Font.productionTableHeader = new Font(regular, 1.23f);
        Font.text = new Font(regular, 1f);

        ProjectDefinition? cliProject = CommandLineParser.ParseArgs(args);

        if (CommandLineParser.errorOccured || CommandLineParser.helpRequested) {
            Console.WriteLine(LSs.YafcWithVersion.L(YafcLib.version.ToString(3)));
            Console.WriteLine();

            if (CommandLineParser.errorOccured) {
                Console.WriteLine(LSs.CommandLineError.L(CommandLineParser.lastError));
                Console.WriteLine();
                Environment.ExitCode = 1;
            }

            CommandLineParser.PrintHelp();
        }
        else {
            _ = new WelcomeScreen(cliProject);
            Ui.MainLoop();
        }
    }
}
