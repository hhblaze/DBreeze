using System;
using System.IO;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || EqualsArgument(args[0], "--storage-contracts"))
            {
                StorageContractSuite.RunAll();
                return 0;
            }

            if (EqualsArgument(args[0], "--compat-create"))
            {
                RequireArguments(args, 3);
                StorageCompatibility.Create(args[1], Int32.Parse(args[2]));
                return 0;
            }

            if (EqualsArgument(args[0], "--compat-verify"))
            {
                RequireArguments(args, 3);
                StorageCompatibility.Verify(args[1], Int32.Parse(args[2]));
                return 0;
            }

            if (EqualsArgument(args[0], "--compat-extend"))
            {
                RequireArguments(args, 3);
                StorageCompatibility.Extend(args[1], Int32.Parse(args[2]));
                return 0;
            }

            if (EqualsArgument(args[0], "--performance"))
            {
                if (args.Length != 3 && args.Length != 4)
                    throw new ArgumentException("Expected root, records and optional comma-separated scenarios after --performance.");
                StoragePerformance.Run(args[1], Int32.Parse(args[2]), args.Length == 4 ? args[3] : null);
                return 0;
            }

            throw new ArgumentException("Unknown argument: " + args[0]);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static bool EqualsArgument(string value, string expected)
    {
        return String.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static void RequireArguments(string[] args, int count)
    {
        if (args.Length != count)
            throw new ArgumentException("Expected " + (count - 1) + " value argument(s) after " + args[0] + ".");
    }
}
