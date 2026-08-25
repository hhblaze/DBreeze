using System.Globalization;

namespace DBreeze.Net8.Benchmarks;

internal sealed class ReleaseAuditOptions
{
    internal const string DefaultBaselineCommit = "a83424e2fa742ec05a8e4a359562d3f3a5e008c8";
    internal string CurrentRepository { get; private set; }
    internal string BaselineRepository { get; private set; } = @"D:\VS\DBreezeRealm_copy\DBreeze";
    internal string ExpectedBaseline { get; private set; } = DefaultBaselineCommit;
    internal string Root { get; private set; } = @"D:\Temp\DbreezeDbTest";
    internal string Report { get; private set; }
    internal string RunId { get; private set; }
    internal string Profile { get; private set; } = "full";
    internal int BudgetMinutes { get; private set; } = 60;
    internal int MaxRecords { get; private set; } = 1_000_000;
    internal int MaxTextRecords { get; private set; } = 10_000;
    internal int MaxVectorRecords { get; private set; } = 10_000;
    internal bool AllowDirtyCurrent { get; private set; }
    internal bool KeepDatabases { get; private set; }

    internal static ReleaseAuditOptions Parse(string[] args)
    {
        var options = new ReleaseAuditOptions();
        bool budgetSpecified = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--release-audit": case "--compare-all": break;
                case "--profile": options.Profile = Value(args, ref i, "--profile").ToLowerInvariant(); break;
                case "--baseline-repo": options.BaselineRepository = Path.GetFullPath(Value(args, ref i, "--baseline-repo")); break;
                case "--current-repo": options.CurrentRepository = Path.GetFullPath(Value(args, ref i, "--current-repo")); break;
                case "--expected-baseline": options.ExpectedBaseline = Value(args, ref i, "--expected-baseline"); break;
                case "--root": options.Root = Path.GetFullPath(Value(args, ref i, "--root")); break;
                case "--report": options.Report = Path.GetFullPath(Value(args, ref i, "--report")); break;
                case "--run-id": options.RunId = Value(args, ref i, "--run-id"); break;
                case "--budget-minutes": options.BudgetMinutes = Limit(args, ref i, "--budget-minutes", 1, 60); budgetSpecified = true; break;
                case "--max-records": options.MaxRecords = Limit(args, ref i, "--max-records", 1, 1_000_000); break;
                case "--max-text-records": options.MaxTextRecords = Limit(args, ref i, "--max-text-records", 1, 10_000); break;
                case "--max-vector-records": options.MaxVectorRecords = Limit(args, ref i, "--max-vector-records", 1, 10_000); break;
                case "--allow-dirty-current": options.AllowDirtyCurrent = true; break;
                case "--keep-databases": options.KeepDatabases = true; break;
                default: throw new ArgumentException("Unknown release-audit option: " + args[i]);
            }
        }
        if (options.Profile is not ("full" or "smoke")) throw new ArgumentException("--profile must be full or smoke.");
        if (!budgetSpecified && options.Profile == "smoke") options.BudgetMinutes = 5;
        options.CurrentRepository ??= AuditRepositoryLocator.FindCurrentRepository();
        options.Report ??= Path.Combine(options.CurrentRepository, "Documentation", "Audit", "DBreeze_Release_Audit.html");
        options.RunId ??= DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-release-audit";
        AuditRunLayout.ValidateLeafName(options.RunId, "--run-id");
        return options;
    }

    internal string ReproductionCommand =>
        $"dotnet run --project DBreeze.Net8.Benchmarks -c Release -- --release-audit --profile {Profile} " +
        $"--baseline-repo \"{BaselineRepository}\" --expected-baseline {ExpectedBaseline} --root \"{Root}\" " +
        $"--budget-minutes {BudgetMinutes} --max-records {MaxRecords} --max-text-records {MaxTextRecords} " +
        $"--max-vector-records {MaxVectorRecords} --report \"{Report}\"" + (AllowDirtyCurrent ? " --allow-dirty-current" : String.Empty);

    private static string Value(string[] args, ref int index, string option)
    {
        if (++index == args.Length || String.IsNullOrWhiteSpace(args[index])) throw new ArgumentException(option + " requires a value.");
        return args[index];
    }
    private static int Limit(string[] args, ref int index, string option, int minimum, int maximum)
    {
        int value;
        string text = Value(args, ref index, option);
        if (!Int32.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(option, text, $"Expected {minimum}..{maximum}.");
        return value;
    }
}
