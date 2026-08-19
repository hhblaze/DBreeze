using System.Reflection;
using System.Text;
using DBreeze;

namespace DBreeze.Net8.Benchmarks;

internal static class ApiSurfaceProbe
{
    private static readonly Type[] RootTypes =
    {
        typeof(DBreezeEngine),
        typeof(DBreezeRemoteEngine),
        typeof(Scheme),
        typeof(DBreezeResources),
    };

    internal static int Run(string[] args)
    {
        try
        {
            int surfaceIndex = Array.FindIndex(args,
                static arg => String.Equals(arg, "--api-surface", StringComparison.OrdinalIgnoreCase));
            if (surfaceIndex >= 0)
            {
                string output = ReadPath(args, surfaceIndex, "--api-surface");
                WriteSurface(output);
                Console.WriteLine("PASS api-surface");
                return 0;
            }

            int compareIndex = Array.FindIndex(args,
                static arg => String.Equals(arg, "--api-compare", StringComparison.OrdinalIgnoreCase));
            if (compareIndex < 0 || compareIndex + 2 >= args.Length)
                throw new ArgumentException("--api-compare requires left and right manifest paths.", nameof(args));

            Compare(Path.GetFullPath(args[compareIndex + 1]), Path.GetFullPath(args[compareIndex + 2]));
            Console.WriteLine("PASS api-compare");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    private static void WriteSurface(string output)
    {
        string[] lines = RootTypes
            .SelectMany(EnumerateTypeTree)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static line => line, StringComparer.Ordinal)
            .ToArray();

        string parent = Path.GetDirectoryName(output)
            ?? throw new InvalidOperationException("API manifest must have a parent directory.");
        Directory.CreateDirectory(parent);
        File.WriteAllLines(output, lines, new UTF8Encoding(false));
    }

    private static IEnumerable<string> EnumerateTypeTree(Type type)
    {
        yield return "T " + FormatType(type);

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        foreach (ConstructorInfo constructor in type.GetConstructors(flags).Where(IsVisible))
            yield return $"C {FormatType(type)}({FormatParameters(constructor.GetParameters())})";

        foreach (MethodInfo method in type.GetMethods(flags).Where(IsVisible).Where(static method => !method.IsSpecialName))
        {
            string genericArguments = method.IsGenericMethodDefinition
                ? "<" + String.Join(",", method.GetGenericArguments().Select(static argument => argument.Name)) + ">"
                : String.Empty;
            yield return $"M {FormatType(type)}.{method.Name}{genericArguments}({FormatParameters(method.GetParameters())}):{FormatType(method.ReturnType)}";
        }

        foreach (PropertyInfo property in type.GetProperties(flags).Where(IsVisible))
        {
            string access = String.Join(",", new[]
            {
                IsVisible(property.GetMethod) ? "get" : null,
                IsVisible(property.SetMethod) ? "set" : null,
            }.Where(static item => item != null));
            yield return $"P {FormatType(type)}.{property.Name}[{FormatParameters(property.GetIndexParameters())}]:{FormatType(property.PropertyType)} {{{access}}}";
        }

        foreach (FieldInfo field in type.GetFields(flags).Where(IsVisible))
            yield return $"F {FormatType(type)}.{field.Name}:{FormatType(field.FieldType)}";

        foreach (EventInfo eventInfo in type.GetEvents(flags).Where(IsVisible))
            yield return $"E {FormatType(type)}.{eventInfo.Name}:{FormatType(eventInfo.EventHandlerType)}";

        foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                     .Where(static nested => nested.IsNestedPublic || nested.IsNestedFamily || nested.IsNestedFamORAssem))
        {
            foreach (string line in EnumerateTypeTree(nested))
                yield return line;
        }
    }

    private static bool IsVisible(MethodBase member) =>
        member != null && (member.IsPublic || member.IsFamily || member.IsFamilyOrAssembly);

    private static bool IsVisible(FieldInfo member) =>
        member.IsPublic || member.IsFamily || member.IsFamilyOrAssembly;

    private static bool IsVisible(PropertyInfo member) => IsVisible(member.GetMethod) || IsVisible(member.SetMethod);

    private static bool IsVisible(EventInfo member) => IsVisible(member.AddMethod) || IsVisible(member.RemoveMethod);

    private static string FormatParameters(ParameterInfo[] parameters) => String.Join(",", parameters.Select(parameter =>
        (parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : String.Empty) +
        FormatType(parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType() : parameter.ParameterType) +
        (parameter.IsOptional ? "=" + FormatDefault(parameter.DefaultValue) : String.Empty)));

    private static string FormatDefault(object value) => value switch
    {
        null => "null",
        string text => "\"" + text + "\"",
        char character => "'" + character + "'",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? String.Empty,
    };

    private static string FormatType(Type type)
    {
        if (type.IsArray)
            return FormatType(type.GetElementType()) + "[]";
        if (type.IsGenericParameter)
            return "`" + type.Name;
        if (!type.IsGenericType)
            return type.FullName ?? type.Name;

        string name = type.GetGenericTypeDefinition().FullName ?? type.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0)
            name = name.Substring(0, tick);
        return name + "<" + String.Join(",", type.GetGenericArguments().Select(FormatType)) + ">";
    }

    private static void Compare(string left, string right)
    {
        string[] leftLines = File.ReadAllLines(left);
        string[] rightLines = File.ReadAllLines(right);
        string[] onlyLeft = leftLines.Except(rightLines, StringComparer.Ordinal).ToArray();
        string[] onlyRight = rightLines.Except(leftLines, StringComparer.Ordinal).ToArray();
        if (onlyLeft.Length == 0 && onlyRight.Length == 0)
            return;

        throw new InvalidDataException(
            "Public/protected Engine API differs.\nOnly left:\n" + String.Join("\n", onlyLeft) +
            "\nOnly right:\n" + String.Join("\n", onlyRight));
    }

    private static string ReadPath(string[] args, int optionIndex, string option)
    {
        if (optionIndex + 1 >= args.Length || String.IsNullOrWhiteSpace(args[optionIndex + 1]))
            throw new ArgumentException(option + " requires an output path.", nameof(args));
        return Path.GetFullPath(args[optionIndex + 1]);
    }
}
