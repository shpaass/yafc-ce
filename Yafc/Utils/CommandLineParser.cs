using System;
using System.IO;
using System.Linq;
using Yafc.I18n;

namespace Yafc;

/// <summary>
/// The role of this class is to handle things related to launching Yafc from the command line.<br/>
/// That includes handling Windows commands after associating the Yafc executable with <c>.yafc</c> files,<br/>
/// because Windows essentially runs <c>./Yafc.exe path/to/file </c> when you click on the file or use the
/// Open-With functionality.
/// </summary>
public static class CommandLineParser {
    public static string lastError { get; private set; } = string.Empty;
    public static bool helpRequested { get; private set; }

    public static bool errorOccured => !string.IsNullOrEmpty(lastError);

    public static ProjectDefinition? ParseArgs(string[] args) {
        ProjectDefinition projectDefinition = new ProjectDefinition();

        if (args.Length == 0) {
            return projectDefinition;
        }

        if (args.Length == 1 && !args[0].StartsWith("--")) {
            return LoadProjectFromPath(Path.GetFullPath(args[0]));
        }

        if (!args[0].StartsWith("--")) {
            projectDefinition.dataPath = args[0];
            if (!Directory.Exists(projectDefinition.dataPath)) {
                lastError = LSs.CommandLineErrorPathDoesNotExist.L(LSs.FolderTypeData, projectDefinition.modsPath);
                return null;
            }
        }

        for (int i = string.IsNullOrEmpty(projectDefinition.dataPath) ? 0 : 1; i < args.Length; i++) {
            switch (args[i]) {
                case "--mods-path":
                    if (i + 1 < args.Length && !IsKnownParameter(args[i + 1])) {
                        projectDefinition.modsPath = args[++i];

                        if (!Directory.Exists(projectDefinition.modsPath)) {
                            lastError = LSs.CommandLineErrorPathDoesNotExist.L(LSs.FolderTypeMods, projectDefinition.modsPath);
                            return null;
                        }
                    }
                    else {
                        lastError = LSs.MissingCommandLineArgument.L("--mods-path");
                        return null;
                    }
                    break;

                case "--project-file":
                    if (i + 1 < args.Length && !IsKnownParameter(args[i + 1])) {
                        projectDefinition.path = args[++i];
                        string? directory = Path.GetDirectoryName(projectDefinition.path);

                        if (!Directory.Exists(directory)) {
                            lastError = LSs.CommandLineErrorPathDoesNotExist.L(LSs.FolderTypeProject, projectDefinition.path);
                            return null;
                        }
                    }
                    else {
                        lastError = LSs.MissingCommandLineArgument.L("--project-file");
                        return null;
                    }
                    break;

                case "--net-production":
                    projectDefinition.netProduction = true;
                    break;

                case "--help":
                    helpRequested = true;
                    break;

                default:
                    lastError = LSs.CommandLineErrorUnknownArgument.L(args[i]);
                    return null;
            }
        }

        return projectDefinition;
    }

    public static void PrintHelp() => Console.WriteLine(LSs.ConsoleHelpMessage);

    /// <summary>
    /// Loads the project from the given path. <br/>
    /// If the project has not been opened before, then fetches other settings (like mods-folder) from the most-recently opened project.
    /// </summary>
    private static ProjectDefinition? LoadProjectFromPath(string fullPathToProject) {
        // Putting this part as a separate method makes reading the parent method easier.

        bool previouslyOpenedProjectMatcher(ProjectDefinition candidate) {
            bool pathIsNotEmpty = !string.IsNullOrEmpty(candidate.path);

            return pathIsNotEmpty && string.Equals(fullPathToProject, Path.GetFullPath(candidate.path!), StringComparison.OrdinalIgnoreCase);
        }

        ProjectDefinition[] recentProjects = Preferences.Instance.recentProjects;
        ProjectDefinition? projectToOpen = recentProjects.FirstOrDefault(previouslyOpenedProjectMatcher);

        if (projectToOpen == null && recentProjects.Length > 0) {
            ProjectDefinition donor = recentProjects[0];
            projectToOpen = new ProjectDefinition(donor.dataPath, donor.modsPath, fullPathToProject, donor.expensive, donor.netProduction);
        }

        return projectToOpen;
    }

    private static bool IsKnownParameter(string arg) => arg is "--mods-path" or "--project-file" or "--help";
}
