namespace DBreeze.Net8.Benchmarks;

internal sealed class AuditRunLayout
{
    private const string MarkerName = ".dbreeze-audit-owned";

    internal AuditRunLayout(string rootPath, string runId)
    {
        RootPath = Path.GetFullPath(rootPath);
        RunId = runId;
        ValidateLeafName(runId, nameof(runId));
        ScratchRoot = Path.Combine(RootPath, "scratch");
        ScratchDirectory = Path.Combine(ScratchRoot, runId);
        ReportsDirectory = Path.Combine(RootPath, "reports", runId);
        MarkerPath = Path.Combine(ScratchDirectory, MarkerName);
    }

    internal string RootPath { get; }
    internal string RunId { get; }
    internal string ScratchRoot { get; }
    internal string ScratchDirectory { get; }
    internal string ReportsDirectory { get; }
    internal string MarkerPath { get; }

    internal void Create()
    {
        if (Directory.Exists(ScratchDirectory) || Directory.Exists(ReportsDirectory))
            throw new IOException($"Run directory already exists and will not be overwritten: {RunId}");
        Directory.CreateDirectory(ScratchDirectory);
        Directory.CreateDirectory(ReportsDirectory);
        File.WriteAllText(MarkerPath, RunId + Environment.NewLine);
    }

    internal void CleanupScratch()
    {
        if (!Directory.Exists(ScratchDirectory))
            return;
        string resolvedScratch = EnsureUnderRoot(ScratchDirectory, ScratchRoot);
        if (!File.Exists(MarkerPath) || !String.Equals(File.ReadAllText(MarkerPath).Trim(), RunId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing to delete unmarked scratch directory: {resolvedScratch}");
        }
        Directory.Delete(resolvedScratch, recursive: true);
    }

    internal static void DeleteOwnedChild(string childPath, string ownerRoot)
    {
        if (!Directory.Exists(childPath))
            return;
        string resolved = EnsureUnderRoot(childPath, ownerRoot);
        if (String.Equals(resolved.TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(ownerRoot).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to delete the owner root itself.");
        }
        Directory.Delete(resolved, recursive: true);
    }

    internal static string EnsureUnderRoot(string path, string root)
    {
        string resolvedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        string resolvedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        string prefix = resolvedRoot + Path.DirectorySeparatorChar;
        if (!resolvedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path escapes the owned root: {resolvedPath}");
        return resolvedPath;
    }

    internal static void ValidateLeafName(string value, string parameter)
    {
        if (String.IsNullOrWhiteSpace(value) || value is "." or ".." ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Value must be a single valid directory name.", parameter);
        }
    }
}
