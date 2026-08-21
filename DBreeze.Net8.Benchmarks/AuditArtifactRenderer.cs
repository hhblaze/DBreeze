namespace DBreeze.Net8.Benchmarks;

internal static class AuditArtifactRenderer
{
    internal static int Run(string[] args)
    {
        try
        {
            string resultsPath = RequiredValue(args, "--render-audit");
            AuditRunReport report = AuditPersistence.ReadJson<AuditRunReport>(resultsPath);
            var confirmationKeys = report.Performance
                .Where(static item => item.ConfirmationRun)
                .Select(static item => item.Category + "|" + item.Scenario + "|" + item.Workers)
                .ToHashSet(StringComparer.Ordinal);

            var originalVerdicts = report.Performance.ToDictionary(
                static item => item.Category + "|" + item.Scenario + "|" + item.Workers,
                static item => (item.SpeedGatePassed, item.AllocationGatePassed),
                StringComparer.Ordinal);
            report.Performance = AuditPerformanceComparer.Compare(report.Measurements, confirmationKeys);
            foreach (AuditPerformanceComparison item in report.Performance)
            {
                string key = item.Category + "|" + item.Scenario + "|" + item.Workers;
                if (originalVerdicts.TryGetValue(key, out var verdict) &&
                    verdict != (item.SpeedGatePassed, item.AllocationGatePassed))
                {
                    throw new InvalidDataException($"Re-render changed the measured gate verdict for {key}.");
                }
            }

            string reportPath = OptionalValue(args, "--report") ?? report.Metadata.HtmlReportPath;
            if (String.IsNullOrWhiteSpace(reportPath))
                throw new ArgumentException("--report is required when results.json has no HtmlReportPath.");
            report.Metadata.HtmlReportPath = Path.GetFullPath(reportPath);

            string resultsFullPath = Path.GetFullPath(resultsPath);
            string reportsDirectory = Path.GetDirectoryName(resultsFullPath)
                ?? throw new InvalidOperationException("results.json must have a parent directory.");
            AuditPersistence.WriteJson(resultsFullPath, report);
            AuditReportArtifacts.WritePerformanceCsv(Path.Combine(reportsDirectory, "performance.csv"), report);
            AuditHtmlReportWriter.Write(report.Metadata.HtmlReportPath, report);
            Console.WriteLine($"Audit artifacts rendered from {resultsFullPath}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 2;
        }
    }

    private static string RequiredValue(string[] args, string option) =>
        OptionalValue(args, option) ?? throw new ArgumentException($"{option} requires a value.");

    private static string OptionalValue(string[] args, string option)
    {
        for (int index = 0; index < args.Length; index++)
        {
            if (!String.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
                continue;
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                return null;
            return args[index + 1];
        }
        return null;
    }
}
