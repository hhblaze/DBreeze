using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using DBreeze;
using DBreeze.ReleaseAudit.Protocol;

namespace DBreeze.ReleaseAudit.Worker
{
    internal static class ApiCatalog
    {
        internal static List<ApiMember> CreateAssemblyManifest()
        {
            var records = new List<ApiMember>();
            Type[] types = typeof(DBreezeEngine).Assembly.GetExportedTypes();
            Array.Sort(types, delegate(Type left, Type right)
            {
                return StringComparer.Ordinal.Compare(FormatType(left), FormatType(right));
            });
            foreach (Type type in types)
                Enumerate(type, records, true);
            return records.GroupBy(delegate(ApiMember item) { return item.Id; }, StringComparer.Ordinal)
                .Select(delegate(IGrouping<string, ApiMember> group) { return group.First(); })
                .OrderBy(delegate(ApiMember item) { return item.Id; }, StringComparer.Ordinal).ToList();
        }

        internal static List<ApiMember> CreateFocusedManifest()
        {
            var records = new List<ApiMember>();
            Enumerate(typeof(DBreeze.Transactions.Transaction), records, false);
            Enumerate(typeof(DBreeze.Scheme), records, false);
            return records.GroupBy(delegate(ApiMember item) { return item.Id; }, StringComparer.Ordinal)
                .Select(delegate(IGrouping<string, ApiMember> group) { return group.First(); })
                .OrderBy(delegate(ApiMember item) { return item.Id; }, StringComparer.Ordinal).ToList();
        }

        internal static List<MethodInfo> FocusedMethods()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
            return new[] { typeof(DBreeze.Transactions.Transaction), typeof(DBreeze.Scheme) }
                .SelectMany(delegate(Type type) { return type.GetMethods(flags); })
                .Where(delegate(MethodInfo method) { return !method.IsSpecialName; })
                .OrderBy(CanonicalId, StringComparer.Ordinal).ToList();
        }

        internal static string CanonicalId(MemberInfo member)
        {
            Type declaringType = member.DeclaringType;
            string owner = FormatType(declaringType);
            var method = member as MethodInfo;
            if (method != null)
            {
                string generic = FormatGenericDeclaration(method.GetGenericArguments());
                return "M " + Visibility(method) + " " + Static(method) + owner + "." + method.Name + generic +
                    "(" + FormatParameters(method.GetParameters()) + "):" + FormatType(method.ReturnType) +
                    FormatGenericConstraints(method.GetGenericArguments());
            }
            var constructor = member as ConstructorInfo;
            if (constructor != null)
                return "C " + Visibility(constructor) + " " + owner + "(" + FormatParameters(constructor.GetParameters()) + ")";
            var property = member as PropertyInfo;
            if (property != null)
            {
                var accessors = new List<string>();
                if (property.GetMethod != null && IsVisible(property.GetMethod))
                    accessors.Add(Visibility(property.GetMethod) + " get");
                if (property.SetMethod != null && IsVisible(property.SetMethod))
                    accessors.Add(Visibility(property.SetMethod) + " set");
                return "P " + owner + "." + property.Name + "[" + FormatParameters(property.GetIndexParameters()) + "]:" +
                    FormatType(property.PropertyType) + " {" + String.Join(",", accessors.ToArray()) + "}";
            }
            var field = member as FieldInfo;
            if (field != null)
                return "F " + Visibility(field) + " " + (field.IsStatic ? "static " : String.Empty) + owner + "." +
                    field.Name + ":" + FormatType(field.FieldType);
            var eventInfo = member as EventInfo;
            if (eventInfo != null)
                return "E " + owner + "." + eventInfo.Name + ":" + FormatType(eventInfo.EventHandlerType);
            var type = member as Type;
            if (type != null)
                return "T " + Visibility(type) + " " + TypeKind(type) + " " + FormatType(type) +
                    FormatGenericConstraints(type.GetGenericArguments());
            throw new NotSupportedException(member.MemberType.ToString());
        }

        private static void Enumerate(Type type, ICollection<ApiMember> records, bool includeProtected)
        {
            Add(records, CanonicalId(type), type, "type");
            if (type.BaseType != null && type.BaseType != typeof(Object))
                Add(records, "B " + FormatType(type) + ":" + FormatType(type.BaseType), type, "base");
            foreach (Type interfaceType in type.GetInterfaces().OrderBy(FormatType, StringComparer.Ordinal))
                Add(records, "I " + FormatType(type) + ":" + FormatType(interfaceType), type, "interface");
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly;
            if (includeProtected)
                flags |= BindingFlags.NonPublic;

            foreach (ConstructorInfo constructor in type.GetConstructors(flags).Where(IsVisible))
                Add(records, CanonicalId(constructor), type, "constructor");
            foreach (MethodInfo method in type.GetMethods(flags).Where(delegate(MethodInfo value)
                     { return !value.IsSpecialName && IsVisible(value); }))
                Add(records, CanonicalId(method), type, "method");
            foreach (PropertyInfo property in type.GetProperties(flags).Where(delegate(PropertyInfo value)
                     { return IsVisible(value.GetMethod) || IsVisible(value.SetMethod); }))
                Add(records, CanonicalId(property), type, "property");
            foreach (FieldInfo field in type.GetFields(flags).Where(IsVisible))
                Add(records, CanonicalId(field), type, "field");
            foreach (EventInfo eventInfo in type.GetEvents(flags).Where(delegate(EventInfo value)
                     { return IsVisible(value.AddMethod) || IsVisible(value.RemoveMethod); }))
                Add(records, CanonicalId(eventInfo), type, "event");
            if (includeProtected)
            {
                foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Where(delegate(Type value)
                         { return value.IsNestedPublic || value.IsNestedFamily || value.IsNestedFamORAssem; }))
                    Enumerate(nested, records, true);
            }
        }

        private static void Add(ICollection<ApiMember> records, string id, Type type, string kind)
        {
            records.Add(new ApiMember { Id = id, DeclaringType = FormatType(type), Kind = kind });
        }

        private static bool IsVisible(MethodBase method)
        {
            return method != null && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);
        }

        private static bool IsVisible(FieldInfo field)
        {
            return field != null && (field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly);
        }

        private static string Visibility(MethodBase method)
        {
            return method.IsPublic ? "public" : method.IsFamily ? "protected" : "protected internal";
        }

        private static string Visibility(FieldInfo field)
        {
            return field.IsPublic ? "public" : field.IsFamily ? "protected" : "protected internal";
        }

        private static string Visibility(Type type)
        {
            return type.IsNested
                ? (type.IsNestedPublic ? "public" : type.IsNestedFamily ? "protected" : "protected internal")
                : "public";
        }

        private static string Static(MethodInfo method) { return method.IsStatic ? "static " : String.Empty; }

        private static string FormatParameters(ParameterInfo[] parameters)
        {
            return String.Join(",", parameters.Select(delegate(ParameterInfo parameter)
            {
                Type type = parameter.ParameterType;
                string modifier = parameter.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length != 0 ? "params " :
                    parameter.IsOut ? "out " : type.IsByRef && parameter.IsIn ? "in " : type.IsByRef ? "ref " : String.Empty;
                if (type.IsByRef)
                    type = type.GetElementType();
                return modifier + FormatType(type) + (parameter.IsOptional ? "=" + FormatDefault(parameter.DefaultValue) : String.Empty);
            }).ToArray());
        }

        private static string FormatDefault(object value)
        {
            if (value == null) return "null";
            if (value == Missing.Value) return "missing";
            if (value is string) return "\"" + ((string)value).Replace("\"", "\\\"") + "\"";
            if (value is char) return "'" + value + "'";
            if (value is bool) return (bool)value ? "true" : "false";
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty;
        }

        internal static string FormatType(Type type)
        {
            if (type == null) return "null";
            if (type.IsByRef) return FormatType(type.GetElementType()) + "&";
            if (type.IsPointer) return FormatType(type.GetElementType()) + "*";
            if (type.IsArray) return FormatType(type.GetElementType()) + "[]";
            if (type.IsGenericParameter) return "`" + type.Name;
            if (!type.IsGenericType) return type.FullName ?? type.Name;
            string name = type.GetGenericTypeDefinition().FullName ?? type.Name;
            int tick = name.IndexOf('`');
            if (tick >= 0) name = name.Substring(0, tick);
            return name + "<" + String.Join(",", type.GetGenericArguments().Select(FormatType).ToArray()) + ">";
        }

        private static string FormatGenericDeclaration(Type[] arguments)
        {
            return arguments.Length == 0 ? String.Empty : "<" + String.Join(",", arguments.Select(delegate(Type value)
            { return value.Name; }).ToArray()) + ">";
        }

        private static string FormatGenericConstraints(IEnumerable<Type> arguments)
        {
            var clauses = new List<string>();
            foreach (Type argument in arguments.Where(delegate(Type value) { return value.IsGenericParameter; }))
            {
                var constraints = new List<string>();
                GenericParameterAttributes attributes = argument.GenericParameterAttributes;
                if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0) constraints.Add("class");
                if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0) constraints.Add("struct");
                constraints.AddRange(argument.GetGenericParameterConstraints().Select(FormatType));
                if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0 &&
                    (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0) constraints.Add("new()");
                if (constraints.Count != 0)
                    clauses.Add(" where " + argument.Name + ":" + String.Join("&", constraints.ToArray()));
            }
            return String.Concat(clauses.ToArray());
        }

        private static string TypeKind(Type type)
        {
            string kind = type.IsInterface ? "interface" : type.IsEnum ? "enum" :
                typeof(MulticastDelegate).IsAssignableFrom(type.BaseType) ? "delegate" : type.IsValueType ? "struct" : "class";
            string modifier = type.IsAbstract && type.IsSealed ? "static " : type.IsAbstract && !type.IsInterface ? "abstract " :
                type.IsSealed && !type.IsValueType && !type.IsEnum ? "sealed " : String.Empty;
            return modifier + kind;
        }
    }
}
