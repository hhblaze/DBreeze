using System.Runtime;
using System.Runtime.InteropServices;

namespace DBreeze.Net8.Benchmarks;

internal static class AuditWorker
{
    internal static int Run(string[] args)
    {
        try
        {
            AuditWorkerOptions options = AuditWorkerOptions.Parse(args);
            switch (options.Action)
            {
                case "api":
                    AuditPersistence.WriteJson(options.OutputPath, AuditApiCatalog.Create(options.Variant));
                    break;
                case "correctness":
                    AuditPersistence.WriteJson(options.OutputPath, AuditCorrectnessSuite.Run(options));
                    break;
                case "performance":
                    AuditPersistence.WriteJson(options.OutputPath, AuditPerformanceSuite.Run(options));
                    break;
                default:
                    throw new InvalidOperationException("Unknown audit worker action.");
            }
            Console.WriteLine($"PASS audit-worker {options.Action} {options.Variant}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }
}
