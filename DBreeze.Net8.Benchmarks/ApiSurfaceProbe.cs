using System.Reflection;
using System.Text;
using DBreeze;

namespace DBreeze.Net8.Benchmarks;

internal static class ApiSurfaceProbe
{
    private static IEnumerable<Type> SurfaceTypes => typeof(DBreezeEngine).Assembly.GetExportedTypes();

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

            int compatibilityIndex = Array.FindIndex(args,
                static arg => String.Equals(arg, "--api-compatible", StringComparison.OrdinalIgnoreCase));
            if (compatibilityIndex >= 0)
            {
                Compare(ReadComparisonPaths(args, compatibilityIndex, "--api-compatible"), exact: false);
                Console.WriteLine("PASS api-compatible");
                return 0;
            }

            int compareIndex = Array.FindIndex(args,
                static arg => String.Equals(arg, "--api-compare", StringComparison.OrdinalIgnoreCase));
            Compare(ReadComparisonPaths(args, compareIndex, "--api-compare"), exact: true);
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
        string[] lines = SurfaceTypes
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
        string formattedType = FormatType(type);
        yield return $"T {FormatVisibility(type)} {FormatTypeKind(type)} {formattedType}{FormatGenericConstraints(type.GetGenericArguments())}";

        if (type.BaseType != null)
            yield return $"B {formattedType}:{FormatType(type.BaseType)}";

        foreach (Type interfaceType in type.GetInterfaces().OrderBy(FormatType, StringComparer.Ordinal))
            yield return $"I {formattedType}:{FormatType(interfaceType)}";

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        foreach (ConstructorInfo constructor in type.GetConstructors(flags).Where(IsVisible))
            yield return $"C {FormatVisibility(constructor)} {FormatType(type)}({FormatParameters(constructor.GetParameters())})";

        foreach (MethodInfo method in type.GetMethods(flags).Where(IsVisible).Where(static method => !method.IsSpecialName))
        {
            string genericArguments = method.IsGenericMethodDefinition
                ? "<" + String.Join(",", method.GetGenericArguments().Select(static argument => argument.Name)) + ">"
                : String.Empty;
            string staticMarker = method.IsStatic ? "static " : String.Empty;
            yield return $"M {FormatVisibility(method)} {staticMarker}{formattedType}.{method.Name}{genericArguments}({FormatParameters(method.GetParameters())}):{FormatType(method.ReturnType)}{FormatGenericConstraints(method.GetGenericArguments())}";
        }

        foreach (PropertyInfo property in type.GetProperties(flags).Where(IsVisible))
        {
            string access = String.Join(",", new[]
            {
                IsVisible(property.GetMethod) ? FormatAccessor(property.GetMethod, "get") : null,
                IsVisible(property.SetMethod) ? FormatAccessor(property.SetMethod, "set") : null,
            }.Where(static item => item != null));
            yield return $"P {formattedType}.{property.Name}[{FormatParameters(property.GetIndexParameters())}]:{FormatType(property.PropertyType)} {{{access}}}";
        }

        foreach (FieldInfo field in type.GetFields(flags).Where(IsVisible))
        {
            string modifiers = (field.IsStatic ? "static " : String.Empty) +
                (field.IsLiteral ? "const " : field.IsInitOnly ? "readonly " : String.Empty);
            string value = field.IsLiteral ? "=" + FormatDefault(field.GetRawConstantValue()) : String.Empty;
            yield return $"F {FormatVisibility(field)} {modifiers}{formattedType}.{field.Name}:{FormatType(field.FieldType)}{value}";
        }

        foreach (EventInfo eventInfo in type.GetEvents(flags).Where(IsVisible))
        {
            string access = String.Join(",", new[]
            {
                IsVisible(eventInfo.AddMethod) ? FormatAccessor(eventInfo.AddMethod, "add") : null,
                IsVisible(eventInfo.RemoveMethod) ? FormatAccessor(eventInfo.RemoveMethod, "remove") : null,
            }.Where(static item => item != null));
            yield return $"E {formattedType}.{eventInfo.Name}:{FormatType(eventInfo.EventHandlerType)} {{{access}}}";
        }

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

    private static string FormatVisibility(Type type) => type.IsPublic || type.IsNestedPublic ? "public" :
        type.IsNestedFamily ? "protected" :
        type.IsNestedFamORAssem ? "protected internal" : "non-public";

    private static string FormatVisibility(MethodBase member) => member.IsPublic ? "public" :
        member.IsFamily ? "protected" :
        member.IsFamilyOrAssembly ? "protected internal" : "non-public";

    private static string FormatVisibility(FieldInfo member) => member.IsPublic ? "public" :
        member.IsFamily ? "protected" :
        member.IsFamilyOrAssembly ? "protected internal" : "non-public";

    private static string FormatParameters(ParameterInfo[] parameters) => String.Join(",", parameters.Select(parameter =>
        (parameter.GetCustomAttribute<ParamArrayAttribute>() != null ? "params " : String.Empty) +
        (parameter.IsOut ? "out " : parameter.ParameterType.IsByRef && parameter.IsIn ? "in " :
            parameter.ParameterType.IsByRef ? "ref " : String.Empty) +
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
        if (type.IsByRef)
            return FormatType(type.GetElementType()) + "&";
        if (type.IsPointer)
            return FormatType(type.GetElementType()) + "*";
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

    private static string FormatTypeKind(Type type)
    {
        string kind = type.IsInterface ? "interface" : type.IsEnum ? "enum" :
            typeof(MulticastDelegate).IsAssignableFrom(type.BaseType) ? "delegate" :
            type.IsValueType ? "struct" : "class";
        string modifiers = type.IsAbstract && type.IsSealed ? "static " :
            type.IsAbstract && !type.IsInterface ? "abstract " :
            type.IsSealed && !type.IsValueType && !type.IsEnum ? "sealed " : String.Empty;
        return modifiers + kind;
    }

    private static string FormatGenericConstraints(IEnumerable<Type> genericArguments)
    {
        var clauses = new List<string>();
        foreach (Type argument in genericArguments.Where(static argument => argument.IsGenericParameter))
        {
            var constraints = new List<string>();
            GenericParameterAttributes attributes = argument.GenericParameterAttributes &
                GenericParameterAttributes.SpecialConstraintMask;
            if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                constraints.Add("class");
            if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                constraints.Add("struct");
            constraints.AddRange(argument.GetGenericParameterConstraints()
                .Where(static constraint => constraint != typeof(ValueType))
                .Select(FormatType)
                .OrderBy(static constraint => constraint, StringComparer.Ordinal));
            if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0 &&
                (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0)
            {
                constraints.Add("new()");
            }
            if (constraints.Count != 0)
                clauses.Add($" where {argument.Name}:{String.Join(",", constraints)}");
        }
        return String.Concat(clauses);
    }

    private static string FormatAccessor(MethodInfo accessor, string name) =>
        FormatVisibility(accessor) + " " + (accessor.IsStatic ? "static " : String.Empty) + name;

    private static void Compare((string Left, string Right) paths, bool exact)
    {
        string[] leftLines = File.ReadAllLines(paths.Left);
        string[] rightLines = File.ReadAllLines(paths.Right);
        string[] onlyLeft = leftLines.Except(rightLines, StringComparer.Ordinal).ToArray();
        string[] onlyRight = rightLines.Except(leftLines, StringComparer.Ordinal).ToArray();
        if (onlyLeft.Length == 0 && (!exact || onlyRight.Length == 0))
        {
            if (onlyRight.Length != 0)
                Console.WriteLine("API additions requiring review:\n" + String.Join("\n", onlyRight));
            return;
        }

        throw new InvalidDataException(
            "Public/protected DBreeze API is incompatible.\nOnly left:\n" + String.Join("\n", onlyLeft) +
            "\nOnly right:\n" + String.Join("\n", onlyRight));
    }

    private static (string Left, string Right) ReadComparisonPaths(string[] args, int optionIndex, string option)
    {
        if (optionIndex < 0 || optionIndex + 2 >= args.Length)
            throw new ArgumentException(option + " requires left and right manifest paths.", nameof(args));
        return (Path.GetFullPath(args[optionIndex + 1]), Path.GetFullPath(args[optionIndex + 2]));
    }

    private static string ReadPath(string[] args, int optionIndex, string option)
    {
        if (optionIndex + 1 >= args.Length || String.IsNullOrWhiteSpace(args[optionIndex + 1]))
            throw new ArgumentException(option + " requires an output path.", nameof(args));
        return Path.GetFullPath(args[optionIndex + 1]);
    }
}
