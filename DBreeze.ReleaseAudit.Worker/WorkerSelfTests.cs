using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DBreeze.ReleaseAudit.Protocol;

namespace DBreeze.ReleaseAudit.Worker
{
    internal static class WorkerSelfTests
    {
        internal static void Run(WorkerOptions options, WorkerReport report)
        {
            List<ApiMember> focused = ApiCatalog.CreateFocusedManifest();
            List<string> methodIds = ApiCatalog.FocusedMethods().Select(ApiCatalog.CanonicalId).ToList();
            Add(report, "canonical-method-count", methodIds.Count == 85, methodIds.Count.ToString());
            Add(report, "canonical-no-assembly-identity", methodIds.All(delegate(string id)
            { return id.IndexOf("Version=", StringComparison.Ordinal) < 0 && id.IndexOf("PublicKeyToken=", StringComparison.Ordinal) < 0; }), "canonical IDs omit runtime assembly identity");
            Add(report, "canonical-vector-members", methodIds.Count(delegate(string id) { return id.IndexOf(".Vectors", StringComparison.Ordinal) >= 0; }) == 8,
                "vector-methods=" + methodIds.Count(delegate(string id) { return id.IndexOf(".Vectors", StringComparison.Ordinal) >= 0; }));
            Add(report, "canonical-optional-ref-out", methodIds.Any(delegate(string id) { return id.IndexOf("=false", StringComparison.Ordinal) >= 0; }) &&
                methodIds.Any(delegate(string id) { return id.IndexOf("out ", StringComparison.Ordinal) >= 0; }), "optional/default and out markers present");
            Add(report, "focused-properties-constructors", focused.Any(delegate(ApiMember item) { return item.Kind == "property"; }) &&
                focused.Any(delegate(ApiMember item) { return item.Kind == "constructor"; }), "focused manifest includes properties and constructors");
            string root = Path.GetFullPath(options.Root ?? Path.GetTempPath());
            string child = Path.GetFullPath(Path.Combine(root, "child"));
            Add(report, "path-containment", child.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase), child);
        }

        private static void Add(WorkerReport report, string id, bool succeeded, string detail)
        {
            report.Cases.Add(new CaseResult { Id = id, Category = "self-test", Mode = "metadata", Succeeded = succeeded, SemanticValue = detail });
        }
    }
}
