using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using DBreeze;
using DBreeze.ReleaseAudit.Protocol;

namespace DBreeze.ReleaseAudit.Worker
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            WorkerOptions options = null;
            WorkerReport report = null;
            try
            {
                options = WorkerOptions.Parse(args);
                report = CreateReport(options);
                Dispatch(options, report);
                report.Succeeded = report.Cases.TrueForAll(delegate(CaseResult item) { return item.Succeeded; });
                report.CompletedUtc = DateTime.UtcNow;
                WireJson.Write(options.Output, report);
                return report.Succeeded ? 0 : 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                if (report != null && options != null)
                {
                    report.Succeeded = false;
                    report.Failure = exception.ToString();
                    report.CompletedUtc = DateTime.UtcNow;
                    try { WireJson.Write(options.Output, report); } catch { }
                }
                return 1;
            }
        }

        private static WorkerReport CreateReport(WorkerOptions options)
        {
            Assembly assembly = typeof(DBreezeEngine).Assembly;
            return new WorkerReport
            {
                Variant = options.Variant,
                Framework = options.Framework,
                Runtime = Environment.Version + " / " + GetFrameworkDescription(),
                AssemblyPath = assembly.Location,
                AssemblySha256 = Sha256(assembly.Location),
                StartedUtc = DateTime.UtcNow
            };
        }

        private static void Dispatch(WorkerOptions options, WorkerReport report)
        {
            if (String.Equals(options.Action, "api", StringComparison.Ordinal))
            {
                report.AssemblyApi = ApiCatalog.CreateAssemblyManifest();
                report.FocusedApi = ApiCatalog.CreateFocusedManifest();
                int methodCount = ApiCatalog.FocusedMethods().Count;
                report.Cases.Add(new CaseResult
                {
                    Id = "focused-api-count",
                    Category = "api",
                    Mode = "metadata",
                    Succeeded = methodCount == 85,
                    SemanticValue = methodCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Detail = "Expected 79 Transaction + 6 Scheme public declared methods."
                });
                return;
            }
            if (String.Equals(options.Action, "correctness", StringComparison.Ordinal))
            {
                CorrectnessSuite.Run(options, report);
                return;
            }
            if (String.Equals(options.Action, "fixture-create", StringComparison.Ordinal) ||
                String.Equals(options.Action, "fixture-verify", StringComparison.Ordinal) ||
                String.Equals(options.Action, "fixture-extend", StringComparison.Ordinal) ||
                String.Equals(options.Action, "fixture-verify-extended", StringComparison.Ordinal) ||
                String.Equals(options.Action, "backup-create", StringComparison.Ordinal) ||
                String.Equals(options.Action, "backup-restore", StringComparison.Ordinal) ||
                String.Equals(options.Action, "journal-prepare", StringComparison.Ordinal) ||
                String.Equals(options.Action, "journal-recover", StringComparison.Ordinal))
            {
                CompatibilitySuite.Run(options, report);
                return;
            }
            if (String.Equals(options.Action, "performance", StringComparison.Ordinal))
            {
                PerformanceSuite.Run(options, report);
                return;
            }
            if (String.Equals(options.Action, "self-test", StringComparison.Ordinal))
            {
                WorkerSelfTests.Run(options, report);
                return;
            }
            throw new ArgumentException("Unknown --action: " + options.Action);
        }

        private static string Sha256(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", String.Empty).ToLowerInvariant();
        }

        private static string GetFrameworkDescription()
        {
            object value = typeof(object).Assembly.GetCustomAttribute(typeof(System.Runtime.Versioning.TargetFrameworkAttribute));
            var attribute = value as System.Runtime.Versioning.TargetFrameworkAttribute;
            return attribute == null ? typeof(object).Assembly.FullName : attribute.FrameworkName;
        }
    }
}
