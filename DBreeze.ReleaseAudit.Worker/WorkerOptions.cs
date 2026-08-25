using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DBreeze.ReleaseAudit.Worker
{
    internal sealed class WorkerOptions
    {
        internal string Action;
        internal string Variant;
        internal string Framework;
        internal string Output;
        internal string Root;
        internal string Profile = "smoke";
        internal int MaxRecords = 1000000;
        internal int MaxTextRecords = 10000;
        internal int MaxVectorRecords = 10000;
        internal int Round;
        internal string Scenarios;
        internal string Operations;

        internal static WorkerOptions Parse(string[] args)
        {
            var result = new WorkerOptions();
            var values = new Dictionary<string, Action<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "--action", delegate(string value) { result.Action = value.ToLowerInvariant(); } },
                { "--variant", delegate(string value) { result.Variant = value.ToLowerInvariant(); } },
                { "--framework", delegate(string value) { result.Framework = value.ToLowerInvariant(); } },
                { "--output", delegate(string value) { result.Output = Path.GetFullPath(value); } },
                { "--root", delegate(string value) { result.Root = Path.GetFullPath(value); } },
                { "--profile", delegate(string value) { result.Profile = value.ToLowerInvariant(); } },
                { "--max-records", delegate(string value) { result.MaxRecords = ParseLimit(value, "--max-records", 1000000); } },
                { "--max-text-records", delegate(string value) { result.MaxTextRecords = ParseLimit(value, "--max-text-records", 10000); } },
                { "--max-vector-records", delegate(string value) { result.MaxVectorRecords = ParseLimit(value, "--max-vector-records", 10000); } },
                { "--round", delegate(string value) { result.Round = Int32.Parse(value, CultureInfo.InvariantCulture); } },
                { "--scenarios", delegate(string value) { result.Scenarios = value; } }
                ,{ "--operations", delegate(string value) { result.Operations = value; } }
            };
            for (int i = 0; i < args.Length; i++)
            {
                Action<string> setter;
                if (!values.TryGetValue(args[i], out setter) || ++i == args.Length)
                    throw new ArgumentException("Unknown or incomplete worker option: " + args[Math.Min(i, args.Length - 1)]);
                setter(args[i]);
            }
            if (String.IsNullOrEmpty(result.Action) || String.IsNullOrEmpty(result.Variant) ||
                String.IsNullOrEmpty(result.Framework) || String.IsNullOrEmpty(result.Output))
                throw new ArgumentException("--action, --variant, --framework and --output are required.");
            if (!String.Equals(result.Profile, "smoke", StringComparison.Ordinal) &&
                !String.Equals(result.Profile, "full", StringComparison.Ordinal))
                throw new ArgumentException("--profile must be smoke or full.");
            return result;
        }

        private static int ParseLimit(string value, string name, int maximum)
        {
            int parsed;
            if (!Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) || parsed < 1 || parsed > maximum)
                throw new ArgumentOutOfRangeException(name, value, "Expected 1.." + maximum.ToString(CultureInfo.InvariantCulture));
            return parsed;
        }
    }
}
