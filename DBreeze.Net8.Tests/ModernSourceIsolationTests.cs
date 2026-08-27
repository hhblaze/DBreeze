using System.Text.RegularExpressions;
using System.Xml.Linq;

internal static class ModernSourceIsolationTests
{
    private static readonly string[] ForbiddenSymbols =
    {
        "NET8_0",
        "NET8_0_OR_GREATER",
        "NET8_OR_GREATER",
    };

    private static readonly Regex ForbiddenDirective = new(
        @"^\s*#(?:if|elif)\b[^\r\n]*\b(?:NET8_0(?:_OR_GREATER)?|NET8_OR_GREATER)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly string[] ModernOwnedSources =
    {
        @"LianaTrie\LianaTrie.cs",
        @"LianaTrie\LTrieRootNode.cs",
        @"Storage\StorageLayer.cs",
    };

    internal static void ValidateCurrentRepository() => Validate(ResolveRepositoryRoot(null));

    internal static void Validate(string repositoryRoot)
    {
        repositoryRoot = ValidateRepositoryRoot(repositoryRoot);
        VerifyDetectorContracts();
        VerifySourceTrees(repositoryRoot);
        VerifyNet8Project(repositoryRoot);
        VerifyDeployer(repositoryRoot);
    }

    internal static string ResolveRepositoryRoot(string explicitRoot)
    {
        var candidates = new List<string>();
        if (!String.IsNullOrWhiteSpace(explicitRoot))
            candidates.Add(explicitRoot);

        string environmentRoot = Environment.GetEnvironmentVariable("DBREEZE_REPOSITORY_ROOT");
        if (!String.IsNullOrWhiteSpace(environmentRoot))
            candidates.Add(environmentRoot);

        candidates.Add(Environment.CurrentDirectory);
        candidates.Add(AppContext.BaseDirectory);

        foreach (string candidate in candidates)
        {
            DirectoryInfo directory;
            try
            {
                directory = new DirectoryInfo(Path.GetFullPath(candidate));
            }
            catch
            {
                continue;
            }

            while (directory != null)
            {
                if (IsRepositoryRoot(directory.FullName))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "DBreeze repository root was not found. Supply --repository-root or DBREEZE_REPOSITORY_ROOT.");
    }

    private static string ValidateRepositoryRoot(string repositoryRoot)
    {
        if (String.IsNullOrWhiteSpace(repositoryRoot))
            throw new ArgumentException("Repository root is required.", nameof(repositoryRoot));

        string fullPath = Path.GetFullPath(repositoryRoot);
        if (!IsRepositoryRoot(fullPath))
            throw new DirectoryNotFoundException("Not a DBreeze repository root: " + fullPath);
        return fullPath;
    }

    private static bool IsRepositoryRoot(string path) =>
        File.Exists(Path.Combine(path, "DBreeze", "DBreeze.csproj")) &&
        File.Exists(Path.Combine(path, "DBreeze.Net8", "DBreeze.Net8.csproj"));

    private static void VerifySourceTrees(string repositoryRoot)
    {
        var violations = new List<string>();
        foreach (string sourceRootName in new[] { "DBreeze", "DBreeze.Net8" })
        {
            string sourceRoot = Path.Combine(repositoryRoot, sourceRootName);
            foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (ContainsPathSegment(file, sourceRoot, "bin") ||
                    ContainsPathSegment(file, sourceRoot, "obj"))
                {
                    continue;
                }

                string source = File.ReadAllText(file);
                Match match = ForbiddenDirective.Match(source);
                if (match.Success)
                {
                    int line = 1;
                    for (int index = 0; index < match.Index; index++)
                    {
                        if (source[index] == '\n')
                            line++;
                    }
                    violations.Add(Path.GetRelativePath(repositoryRoot, file) + ":" + line +
                        " contains " + match.Value.Trim());
                }
            }
        }

        if (violations.Count != 0)
        {
            throw new InvalidDataException(
                "Exact Net8 preprocessor directives are forbidden in DBreeze library source:\n" +
                String.Join("\n", violations));
        }
    }

    private static bool ContainsPathSegment(string file, string root, string segment) =>
        Path.GetRelativePath(root, file)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(value => String.Equals(value, segment, StringComparison.OrdinalIgnoreCase));

    private static void VerifyNet8Project(string repositoryRoot)
    {
        string projectPath = Path.Combine(repositoryRoot, "DBreeze.Net8", "DBreeze.Net8.csproj");
        XDocument project = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);

        string[] manualSymbols = project.Descendants()
            .Where(element => element.Name.LocalName == "DefineConstants")
            .SelectMany(element => element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(IsForbiddenSymbol)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (manualSymbols.Length != 0)
        {
            throw new InvalidDataException(
                "DBreeze.Net8.csproj manually defines target-framework symbols: " +
                String.Join(", ", manualSymbols));
        }

        string[] includes = project.Descendants()
            .Where(element => element.Name.LocalName == "Compile")
            .Select(element => (string)element.Attribute("Include"))
            .Where(value => !String.IsNullOrWhiteSpace(value))
            .Select(NormalizeProjectPath)
            .ToArray();

        foreach (string relativeSource in ModernOwnedSources)
        {
            string localPath = Path.Combine(repositoryRoot, "DBreeze.Net8",
                relativeSource.Replace('\\', Path.DirectorySeparatorChar));
            if (!File.Exists(localPath))
                throw new FileNotFoundException("Modern-owned source is missing.", localPath);

            string forbiddenLink = NormalizeProjectPath(@"..\DBreeze\" + relativeSource);
            if (includes.Contains(forbiddenLink, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("DBreeze.Net8.csproj still links legacy source: " + forbiddenLink);
        }
    }

    private static string NormalizeProjectPath(string value) =>
        value.Replace('/', '\\').Trim();

    private static void VerifyDeployer(string repositoryRoot)
    {
        string deployerPath = Path.Combine(repositoryRoot, "Deployment", "Deployer", "Deployer", "Program.cs");
        string invocation = File.ReadLines(deployerPath).SingleOrDefault(line =>
            line.Contains("Utils.Compile", StringComparison.Ordinal) &&
            line.Contains(@"DBreeze.Net8\DBreeze.Net8.csproj", StringComparison.Ordinal));
        if (invocation == null)
            throw new InvalidDataException("The Net8 deployer compile invocation was not found or is not unique.");

        string argumentsText = invocation.Substring(invocation.IndexOf("Utils.Compile(", StringComparison.Ordinal) +
            "Utils.Compile(".Length);
        int closing = argumentsText.LastIndexOf(')');
        if (closing < 0)
            throw new InvalidDataException("The Net8 deployer compile invocation is malformed.");

        string[] arguments = SplitTopLevelArguments(argumentsText.Substring(0, closing));
        if (arguments.Length != 7)
            throw new InvalidDataException("Unexpected Net8 deployer argument count: " + arguments.Length);

        string[] compilationSymbols = ExtractStringLiterals(arguments[4])
            .SelectMany(value => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        string[] forbidden = compilationSymbols.Where(IsForbiddenSymbol).Distinct(StringComparer.Ordinal).ToArray();
        if (forbidden.Length != 0)
        {
            throw new InvalidDataException(
                "The Net8 deployer manually supplies target-framework compilation symbols: " +
                String.Join(", ", forbidden));
        }
    }

    private static bool IsForbiddenSymbol(string symbol) =>
        ForbiddenSymbols.Contains(symbol, StringComparer.Ordinal);

    private static string[] SplitTopLevelArguments(string text)
    {
        var arguments = new List<string>();
        int start = 0;
        int depth = 0;
        bool inString = false;
        bool verbatim = false;

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (inString)
            {
                if (!verbatim && character == '\\')
                {
                    index++;
                    continue;
                }
                if (character != '"')
                    continue;
                if (verbatim && index + 1 < text.Length && text[index + 1] == '"')
                {
                    index++;
                    continue;
                }
                inString = false;
                continue;
            }

            if (character == '"')
            {
                inString = true;
                verbatim = index > 0 && text[index - 1] == '@';
            }
            else if (character == '(')
            {
                depth++;
            }
            else if (character == ')')
            {
                depth--;
            }
            else if (character == ',' && depth == 0)
            {
                arguments.Add(text.Substring(start, index - start).Trim());
                start = index + 1;
            }
        }

        if (inString || depth != 0)
            throw new InvalidDataException("Malformed C# argument list.");
        arguments.Add(text.Substring(start).Trim());
        return arguments.ToArray();
    }

    private static IEnumerable<string> ExtractStringLiterals(string expression)
    {
        foreach (Match match in Regex.Matches(expression, "@?\"(?:\"\"|\\\\.|[^\"])*\""))
        {
            string literal = match.Value;
            bool verbatim = literal.StartsWith("@\"", StringComparison.Ordinal);
            int prefix = verbatim ? 2 : 1;
            string value = literal.Substring(prefix, literal.Length - prefix - 1);
            yield return verbatim ? value.Replace("\"\"", "\"") : Regex.Unescape(value);
        }
    }

    private static void VerifyDetectorContracts()
    {
        foreach (string directive in new[]
                 {
                     "#if NET8_0",
                     " #elif NET8_0_OR_GREATER",
                     "#if NET8_OR_GREATER && RELEASE",
                 })
        {
            if (!ForbiddenDirective.IsMatch(directive))
                throw new InvalidOperationException("Isolation detector missed: " + directive);
        }

        foreach (string allowed in new[] { "#if NET8_HOST", "// #if NET8_0", "#if NET6_0" })
        {
            if (ForbiddenDirective.IsMatch(allowed))
                throw new InvalidOperationException("Isolation detector rejected: " + allowed);
        }
    }
}
