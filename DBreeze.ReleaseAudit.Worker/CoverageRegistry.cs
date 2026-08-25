using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DBreeze.ReleaseAudit.Protocol;

namespace DBreeze.ReleaseAudit.Worker
{
    internal sealed class CoverageRegistry
    {
        private readonly object _sync = new object();
        private readonly List<MethodInfo> _methods = ApiCatalog.FocusedMethods();
        private readonly Dictionary<string, CoverageEntry> _entries = new Dictionary<string, CoverageEntry>(StringComparer.Ordinal);

        internal CoverageRegistry()
        {
            foreach (MethodInfo method in _methods)
            {
                string id = ApiCatalog.CanonicalId(method);
                _entries.Add(Key(id, "single"), New(id, "single"));
                _entries.Add(Key(id, "parallel"), New(id, "parallel"));
            }
        }

        internal MethodInfo Method(string declaringType, string name, int parameterCount, string discriminator)
        {
            MethodInfo[] candidates = _methods.Where(delegate(MethodInfo method)
            {
                if (!String.Equals(method.DeclaringType.FullName, declaringType, StringComparison.Ordinal) ||
                    !String.Equals(method.Name, name, StringComparison.Ordinal) || method.GetParameters().Length != parameterCount)
                    return false;
                if (String.IsNullOrEmpty(discriminator)) return true;
                return String.Join("|", method.GetParameters().Select(delegate(ParameterInfo parameter)
                { return ApiCatalog.FormatType(parameter.ParameterType); }).ToArray()).IndexOf(discriminator, StringComparison.Ordinal) >= 0;
            }).ToArray();
            if (candidates.Length != 1)
                throw new InvalidOperationException("Coverage selector resolved " + candidates.Length + " methods: " +
                    declaringType + "." + name + "/" + parameterCount + "/" + discriminator);
            return candidates[0];
        }

        internal void Execute(string mode, MethodInfo method, string evidence, Action action)
        {
            CoverageEntry entry = Get(mode, method);
            lock (_sync)
            {
                entry.Attempts++;
                entry.Evidence = Append(entry.Evidence, evidence);
            }
            action();
            lock (_sync) entry.Successes++;
        }

        internal T Execute<T>(string mode, MethodInfo method, string evidence, Func<T> action)
        {
            CoverageEntry entry = Get(mode, method);
            lock (_sync)
            {
                entry.Attempts++;
                entry.Evidence = Append(entry.Evidence, evidence);
            }
            T result = action();
            lock (_sync) entry.Successes++;
            return result;
        }

        internal List<CoverageEntry> Snapshot()
        {
            lock (_sync)
            {
                return _entries.Values.OrderBy(delegate(CoverageEntry item) { return item.MemberId; }, StringComparer.Ordinal)
                    .ThenBy(delegate(CoverageEntry item) { return item.Mode; }, StringComparer.Ordinal)
                    .Select(delegate(CoverageEntry item)
                    {
                        return new CoverageEntry
                        {
                            MemberId = item.MemberId,
                            Mode = item.Mode,
                            Attempts = item.Attempts,
                            Successes = item.Successes,
                            Evidence = item.Evidence
                        };
                    }).ToList();
            }
        }

        private CoverageEntry Get(string mode, MethodInfo method)
        {
            string id = ApiCatalog.CanonicalId(method.IsGenericMethod && !method.IsGenericMethodDefinition
                ? method.GetGenericMethodDefinition() : method);
            CoverageEntry entry;
            if (!_entries.TryGetValue(Key(id, mode), out entry))
                throw new InvalidOperationException("Method is outside focused coverage manifest: " + id + " / " + mode);
            return entry;
        }

        private static CoverageEntry New(string id, string mode)
        {
            return new CoverageEntry { MemberId = id, Mode = mode, Evidence = String.Empty };
        }

        private static string Key(string id, string mode) { return mode + "\n" + id; }

        private static string Append(string current, string value)
        {
            if (String.IsNullOrEmpty(current)) return value;
            if (current.IndexOf(value, StringComparison.Ordinal) >= 0) return current;
            return current + "; " + value;
        }
    }
}
