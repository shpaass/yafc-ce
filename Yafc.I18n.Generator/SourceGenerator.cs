using System.Text.RegularExpressions;

namespace Yafc.I18n.Generator;

internal partial class SourceGenerator {
    private static readonly Dictionary<string, int> localizationKeys = [];

    private static void Main() {
        // Find the solution root directory
        string rootDirectory = Environment.CurrentDirectory;
        while (!Directory.Exists(Path.Combine(rootDirectory, ".git"))) {
            rootDirectory = Path.GetDirectoryName(rootDirectory)!;
        }
        Environment.CurrentDirectory = rootDirectory;

        HashSet<string> keys = [];
        HashSet<string> referencedKeys = [];

        using MemoryStream classesMemory = new(), stringsMemory = new();
        using (StreamWriter classes = new(classesMemory, leaveOpen: true), strings = new(stringsMemory, leaveOpen: true)) {

            // Always generate the LocalizableString and LocalizableString0 classes
            classes.WriteLine("""
            using System.Diagnostics.CodeAnalysis;

            namespace Yafc.I18n;

            #nullable enable

            /// <summary>
            /// The base class for YAFC's localizable UI strings.
            /// </summary>
            public abstract class LocalizableString {
                private protected readonly string key;

                private protected LocalizableString(string key) => this.key = key;

                /// <summary>
                /// Localize this string using an arbitrary number of parameters. Insufficient parameters will cause the localization to fail,
                /// and excess parameters will be ignored.
                /// </summary>
                /// <param name="args">An array of parameter values.</param>
                /// <returns>The localized string</returns>
                public string Localize(params object[] args) => LocalisedStringParser.ParseKey(key, args) ?? "Key not found: " + key;
            }

            /// <summary>
            /// A localizable UI string that needs 0 parameters for localization.
            /// These strings will implicitly localize when appropriate.
            /// </summary>
            public sealed class LocalizableString0 : LocalizableString {
                internal LocalizableString0(string key) : base(key) { }

                /// <summary>
                /// Localize this string.
                /// </summary>
                /// <returns>The localized string</returns>
                public string L() => LocalisedStringParser.ParseKey(key, []) ?? "Key not found: " + key;

                /// <summary>
                /// Implicitly localizes a zero-parameter localizable string.
                /// </summary>
                /// <param name="lString">The zero-parameter string to be localized</param>
                [return: NotNullIfNotNull(nameof(lString))]
                public static implicit operator string?(LocalizableString0? lString) => lString?.L();
            }
            """);

            HashSet<int> declaredArities = [0];

            // Generate the beginning of the LSs class
            strings.WriteLine("""
            namespace Yafc.I18n;

            /// <summary>
            /// A class containing localizable strings for each key defined in the English localization file. This name should be read as
            /// <c>LocalizableStrings</c>. It is aggressively abbreviated to help keep lines at a reasonable length.
            /// </summary>
            /// <remarks>This class is auto-generated. To add new localizable strings, add them to Yafc/Data/locale/en/yafc.cfg
            /// and build the solution.</remarks>
            public static class LSs {
            """);

            // For each key in locale/en/*.*
            foreach (string file in Directory.EnumerateFiles(Path.Combine(rootDirectory, "Yafc/Data/locale/en"))) {
                using Stream stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                foreach (var (category, key, v) in FactorioLocalization.Read(stream)) {
                    string value = v; // iteration variables are read-only; make value writable.
                    int parameterCount = 0;
                    foreach (Match match in FindParameters().Matches(value)) {
                        parameterCount = Math.Max(parameterCount, int.Parse(match.Groups[1].Value));
                    }

                    // If we haven't generated it yet, generate the LocalizableString<parameterCount> class
                    if (declaredArities.Add(parameterCount)) {
                        classes.WriteLine($$"""

                        /// <summary>
                        /// A localizable string that needs {{parameterCount}} parameters for localization.
                        /// </summary>
                        public sealed class LocalizableString{{parameterCount}} : LocalizableString {
                            internal LocalizableString{{parameterCount}}(string key) : base(key) { }
                        
                            /// <summary>
                            /// Localize this string.
                            /// </summary>
                        {{string.Join(Environment.NewLine, Enumerable.Range(1, parameterCount).Select(n => $"    /// <param name=\"p{n}\">The value to use for parameter __{n}__ when localizing this string.</param>"))}}
                            /// <returns>The localized string</returns>
                            public string L({{string.Join(", ", Enumerable.Range(1, parameterCount).Select(n => $"object p{n}"))}})
                                => LocalisedStringParser.ParseKey(key, [{{string.Join(", ", Enumerable.Range(1, parameterCount).Select(n => $"p{n}"))}}]) ?? "Key not found: " + key;
                        }
                        """);
                    }

                    string pascalCasedKey = string.Join("", key.Split('-').Select(s => char.ToUpperInvariant(s[0]) + s[1..]));
                    keys.Add(key);

                    foreach (Match match in FindReferencedKeys().Matches(value)) {
                        referencedKeys.Add(match.Groups[1].Value);
                    }

                    if (value.Length > 70) {
                        value = value[..70] + "...";
                    }
                    value = value.Replace("&", "&amp;").Replace("<", "&lt;");

                    // Generate the read-only PascalCasedKeyName field.
                    strings.WriteLine($$"""
                            /// <summary>
                            /// Gets a string that will localize to a value resembling "{{value}}"
                            /// </summary>
                        """);
#if DEBUG
                    strings.WriteLine($"    public static LocalizableString{parameterCount} {pascalCasedKey} {{ get; }} = new(\"{category}.{key}\");");
#else
                    // readonly fields are much smaller than read-only properties, but VS doesn't provide inline reference counts for them.
                    strings.WriteLine($"    public static readonly LocalizableString{parameterCount} {pascalCasedKey} = new(\"{category}.{key}\");");
#endif
                }
            }

            foreach (string? undefinedKey in referencedKeys.Except(keys)) {
                strings.WriteLine($"#error Found a reference to __YAFC__{undefinedKey}__, which is not defined.");
            }
            // end of class LLs
            strings.WriteLine("}");
        }

        ReplaceIfChanged("Yafc.I18n/LocalizableStringClasses.g.cs", classesMemory);
        ReplaceIfChanged("Yafc.I18n/LocalizableStrings.g.cs", stringsMemory);
    }

    // Replace the files only if the new content is different than the old content.
    private static void ReplaceIfChanged(string filePath, MemoryStream newContent) {
        newContent.Position = 0;
        if (!File.Exists(filePath) || File.ReadAllText(filePath) != new StreamReader(newContent, leaveOpen: true).ReadToEnd()) {
            File.WriteAllBytes(filePath, newContent.ToArray());
        }
    }

    [GeneratedRegex("__(\\d+)__")]
    private static partial Regex FindParameters();
    [GeneratedRegex("__YAFC__([a-zA-Z0-9_-]+)__")]
    private static partial Regex FindReferencedKeys();
}
