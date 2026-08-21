using System.Reflection;
using System.Security.Cryptography;
using DBreeze;

namespace DBreeze.Net8.Benchmarks;

internal static class AuditApiCatalog
{
    internal static AuditApiManifest Create(string variant)
    {
        Assembly assembly = typeof(DBreezeEngine).Assembly;
        Type[] exportedTypes = assembly.GetExportedTypes();
        Type[] includedTypes = exportedTypes.Where(static type => !IsVectorType(type)).ToArray();
        var records = new List<AuditApiRecord>();

        foreach (Type type in includedTypes.OrderBy(FormatType, StringComparer.Ordinal))
            EnumerateType(type, records);

        AuditApiRecord[] distinct = records
            .GroupBy(static record => record.Id, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static record => record.Id, StringComparer.Ordinal)
            .ToArray();

        return new AuditApiManifest
        {
            Variant = variant,
            AssemblyVersion = assembly.GetName().Version?.ToString() ?? String.Empty,
            AssemblySha256 = ComputeSha256(assembly.Location),
            ExportedTypeCount = exportedTypes.Length,
            IncludedTypeCount = includedTypes.Length,
            ExcludedVectorTypeCount = exportedTypes.Length - includedTypes.Length,
            Records = distinct.ToList(),
        };
    }

    internal static AuditApiComparison Compare(AuditApiManifest baseline, AuditApiManifest current)
    {
        var baselineIds = baseline.Records.Select(static record => record.Id)
            .ToHashSet(StringComparer.Ordinal);
        var currentIds = current.Records.Select(static record => record.Id)
            .ToHashSet(StringComparer.Ordinal);
        string[] missing = baselineIds.Except(currentIds, StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        string[] added = currentIds.Except(baselineIds, StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        string[] unmapped = current.Records.Where(static record => !record.Mapped)
            .Select(static record => record.Id).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        return new AuditApiComparison
        {
            BaselineRecordCount = baseline.Records.Count,
            CurrentRecordCount = current.Records.Count,
            MappedRecordCount = current.Records.Count - unmapped.Length,
            UnmappedRecordCount = unmapped.Length,
            BackwardCompatible = missing.Length == 0,
            CompleteCoverage = unmapped.Length == 0,
            MissingRecords = missing.ToList(),
            AddedRecords = added.ToList(),
            UnmappedRecords = unmapped.ToList(),
        };
    }

    private static void EnumerateType(Type type, ICollection<AuditApiRecord> records)
    {
        string formattedType = FormatType(type);
        Add(records, $"T public {FormatTypeKind(type)} {formattedType}{FormatGenericConstraints(type.GetGenericArguments())}",
            "type", type, null);

        if (type.BaseType != null && type.BaseType != typeof(Object) && !IsVectorType(type.BaseType))
            Add(records, $"B {formattedType}:{FormatType(type.BaseType)}", "base", type, null);

        foreach (Type interfaceType in type.GetInterfaces().Where(static item => !IsVectorType(item))
                     .OrderBy(FormatType, StringComparer.Ordinal))
        {
            Add(records, $"I {formattedType}:{FormatType(interfaceType)}", "interface", type, null);
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.DeclaredOnly;

        foreach (ConstructorInfo constructor in type.GetConstructors(flags))
        {
            Add(records, $"C public {formattedType}({FormatParameters(constructor.GetParameters())})",
                "constructor", type, ".ctor");
        }

        foreach (MethodInfo method in type.GetMethods(flags)
                     .Where(static method => !method.IsSpecialName && !IsVectorMember(method)))
        {
            string genericArguments = method.IsGenericMethodDefinition
                ? "<" + String.Join(",", method.GetGenericArguments().Select(static argument => argument.Name)) + ">"
                : String.Empty;
            string staticMarker = method.IsStatic ? "static " : String.Empty;
            Add(records,
                $"M public {staticMarker}{formattedType}.{method.Name}{genericArguments}({FormatParameters(method.GetParameters())}):{FormatType(method.ReturnType)}{FormatGenericConstraints(method.GetGenericArguments())}",
                "method", type, method.Name);
        }

        foreach (PropertyInfo property in type.GetProperties(flags).Where(static property => !IsVectorType(property.PropertyType)))
        {
            string access = String.Join(",", new[]
            {
                property.GetMethod?.IsPublic == true ? FormatAccessor(property.GetMethod, "get") : null,
                property.SetMethod?.IsPublic == true ? FormatAccessor(property.SetMethod, "set") : null,
            }.Where(static item => item != null));
            Add(records,
                $"P {formattedType}.{property.Name}[{FormatParameters(property.GetIndexParameters())}]:{FormatType(property.PropertyType)} {{{access}}}",
                "property", type, property.Name);
        }

        foreach (FieldInfo field in type.GetFields(flags).Where(static field => !IsVectorType(field.FieldType)))
        {
            string modifiers = (field.IsStatic ? "static " : String.Empty) +
                (field.IsLiteral ? "const " : field.IsInitOnly ? "readonly " : String.Empty);
            string value = field.IsLiteral ? "=" + FormatDefault(field.GetRawConstantValue()) : String.Empty;
            Add(records, $"F public {modifiers}{formattedType}.{field.Name}:{FormatType(field.FieldType)}{value}",
                field.IsLiteral || type.IsEnum ? "constant" : "field", type, field.Name);
        }

        foreach (EventInfo eventInfo in type.GetEvents(flags).Where(static eventInfo =>
                     eventInfo.EventHandlerType == null || !IsVectorType(eventInfo.EventHandlerType)))
        {
            string access = String.Join(",", new[]
            {
                eventInfo.AddMethod?.IsPublic == true ? "public add" : null,
                eventInfo.RemoveMethod?.IsPublic == true ? "public remove" : null,
            }.Where(static item => item != null));
            Add(records, $"E {formattedType}.{eventInfo.Name}:{FormatType(eventInfo.EventHandlerType)} {{{access}}}",
                "event", type, eventInfo.Name);
        }

        foreach (Type nested in type.GetNestedTypes(BindingFlags.Public)
                     .Where(static nested => nested.IsNestedPublic && !IsVectorType(nested)))
        {
            EnumerateType(nested, records);
        }
    }

    private static void Add(ICollection<AuditApiRecord> records, string id, string kind, Type type, string member)
    {
        (string scenario, string mode, bool mapped) = Classify(type, member, kind);
        records.Add(new AuditApiRecord
        {
            Id = id,
            Kind = kind,
            DeclaringType = FormatType(type),
            CoverageScenario = scenario,
            CoverageMode = mode,
            Mapped = mapped,
        });
    }

    private static (string Scenario, string Mode, bool Mapped) Classify(Type type, string member, string kind)
    {
        string fullName = type.FullName ?? type.Name;
        string ns = type.Namespace ?? String.Empty;
        if (kind is "type" or "base" or "interface" or "constant")
            return ("api-metadata", "metadata", true);
        if (ns == "DBreeze.Exceptions")
            return ("api-metadata", "metadata", true);
        if (fullName == "DBreeze.Scheme" || fullName == "DBreeze.DBreezeEngine" ||
            fullName == "DBreeze.DBreezeRemoteEngine" || fullName == "DBreeze.DBreezeConfiguration" ||
            ns == "DBreeze.SchemeInternal")
            return ("engine-scheme", "runtime", true);
        if (fullName == "DBreeze.DBreezeResources" ||
            fullName.StartsWith("DBreeze.DBreezeResources+", StringComparison.Ordinal))
            return ("text-resources", "runtime", true);
        if (fullName.StartsWith("DBreeze.DBreezeConfiguration+TextSearchConfiguration", StringComparison.Ordinal))
            return ("text-resources", "runtime", true);
        if (fullName == "DBreeze.Transactions.Transaction")
        {
            if (member != null && (member.StartsWith("SelectForward", StringComparison.Ordinal) ||
                                   member.StartsWith("SelectBackward", StringComparison.Ordinal) ||
                                   member.StartsWith("Multi_Select", StringComparison.Ordinal)))
                return ("transaction-traversal", "runtime", true);
            if (member != null && member.StartsWith("Text", StringComparison.Ordinal))
                return ("text-resources", "runtime", true);
            return ("transaction-crud", "runtime", true);
        }
        if (ns == "DBreeze.Transactions")
            return ("transaction-concurrency", "regression", true);
        if (fullName == "DBreeze.DataTypes.NestedTable" || fullName.StartsWith("DBreeze.DataTypes.Row", StringComparison.Ordinal))
            return ("nested-row", "runtime", true);
        if (ns == "DBreeze.DataTypes")
            return ("data-types-utils", "runtime", true);
        if (ns == "DBreeze.Objects")
            return ("collections-objects", "runtime", true);
        if (ns == "DBreeze.TextSearch")
            return ("text-resources", "runtime", true);
        if (ns == "DBreeze.Storage" || ns == "DBreeze.Storage.RemoteInstance")
            return ("storage-backup-remote", "regression", true);
        if (ns == "DBreeze.LianaTrie" || ns == "DBreeze.LianaTrie.Iterations" || ns == "DBreeze.Tries")
            return ("liana-trie", "regression", true);
        if (ns == "DBreeze.Utils" || ns == "DBreeze.Utils.Hash")
            return ("data-types-utils", "runtime", true);
        if (ns == "DBreeze.Diagnostic" || ns == "DBreeze.DataStructures")
            return ("diagnostic-structures", "regression", true);
        return ("unmapped", "none", false);
    }

    private static bool IsVectorMember(MethodInfo method)
    {
        return method.Name.StartsWith("Vectors", StringComparison.Ordinal) ||
               IsVectorType(method.ReturnType) || method.GetParameters().Any(static parameter => IsVectorType(parameter.ParameterType));
    }

    private static bool IsVectorType(Type type)
    {
        if (type == null)
            return false;
        if (type.IsByRef || type.IsArray || type.IsPointer)
            return IsVectorType(type.GetElementType());
        if (type.IsGenericType && type.GetGenericArguments().Any(static argument => IsVectorType(argument)))
            return true;
        string fullName = type.FullName ?? type.Name;
        return (type.Namespace?.StartsWith("DBreeze.HNSW", StringComparison.Ordinal) ?? false) ||
               (type.Namespace?.StartsWith("DBreeze.VectorLayer", StringComparison.Ordinal) ?? false) ||
               fullName.Contains("VectorTableParameters", StringComparison.Ordinal) ||
               fullName.Contains("Vectorlayer", StringComparison.OrdinalIgnoreCase) ||
               fullName.Contains("TurboQuant", StringComparison.Ordinal) ||
               fullName.Contains("KNN", StringComparison.Ordinal);
    }

    private static string FormatAccessor(MethodInfo method, string name) =>
        (method.IsStatic ? "static " : String.Empty) + "public " + name;

    private static string FormatParameters(ParameterInfo[] parameters) => String.Join(",", parameters.Select(parameter =>
        (parameter.GetCustomAttribute<ParamArrayAttribute>() != null ? "params " : String.Empty) +
        (parameter.IsOut ? "out " : parameter.ParameterType.IsByRef && parameter.IsIn ? "in " :
            parameter.ParameterType.IsByRef ? "ref " : String.Empty) +
        FormatType(parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType() : parameter.ParameterType) +
        (parameter.IsOptional ? "=" + FormatDefault(parameter.DefaultValue) : String.Empty)));

    private static string FormatDefault(object value) => value switch
    {
        null => "null",
        Missing _ => "missing",
        string text => "\"" + text.Replace("\"", "\\\"") + "\"",
        char character => "'" + character + "'",
        bool flag => flag ? "true" : "false",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? String.Empty,
    };

    private static string FormatType(Type type)
    {
        if (type == null)
            return "null";
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
                .Select(FormatType).OrderBy(static constraint => constraint, StringComparer.Ordinal));
            if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0 &&
                (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0)
                constraints.Add("new()");
            if (constraints.Count != 0)
                clauses.Add($" where {argument.Name}:{String.Join(",", constraints)}");
        }
        return String.Concat(clauses);
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
