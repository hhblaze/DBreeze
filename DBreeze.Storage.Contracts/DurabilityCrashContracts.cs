using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using DBreeze;
using DBreeze.DataTypes;
using DBreeze.Storage;
using DBreeze.Transactions;

internal static class DurabilityCrashContracts
{
    private const string TableA = "durability_a";
    private const string TableB = "durability_b";
    private const string StressTableA = "kill_stress_a";
    private const string StressTableB = "kill_stress_b";
    private static Action<string> armedHandler;

    internal static void RunAll()
    {
        if (!HooksAvailable())
            throw new InvalidOperationException(
                "Crash contracts require -p:DBreezeDurabilityTestHooks=true on the referenced DBreeze project.");

        RunSingle("storage.rollback.written", 1, false, false);
        RunSingle("storage.rollback.flushed", 1, false, false);
        RunSingle("storage.active-marker.written", 1, false, false);
        RunSingle("storage.active-marker.flushed", 1, false, false);
        RunSingle("storage.data.written", 1, false, false);
        RunSingle("storage.data.flushed", 1, false, false);
        RunSingle("storage.zero-marker.written", 1, false, true);
        RunSingle("storage.zero-marker.flushed", 1, false, true);

        RunSingle("backup.flushed", 1, true, false);
        RunSingle("backup.flushed", 2, true, false);
        RunSingle("backup.flushed", 3, true, true);

        RunSingleRecoveryCrash("recovery.data.flushed");
        RunSingleRecoveryCrash("recovery.marker.flushed");

        RunMulti("transaction.participant-prepared", 1, false);
        RunMulti("transaction.participant-prepared", 2, false);
        RunMulti("journal.before-commit-marker", 1, false);
        RunMulti("journal.committed", 1, true);
        RunMulti("journal.participant-finalized", 1, true);
        RunMulti("journal.participant-finalized", 2, true);
        RunMulti("journal.removed", 1, true);
        RunMultiRecoveryCrash();
        RunKillStress();

        Console.WriteLine("PASS DurabilityCrashContracts target=" + StorageTestSupport.TargetName);
    }

    internal static void RunWorker(string mode, string root, string checkpoint, int occurrence)
    {
        if (String.Equals(mode, "single", StringComparison.Ordinal))
        {
            SingleWorker(root, checkpoint, occurrence, false);
            return;
        }
        if (String.Equals(mode, "single-backup", StringComparison.Ordinal))
        {
            SingleWorker(root, checkpoint, occurrence, true);
            return;
        }
        if (String.Equals(mode, "single-recover", StringComparison.Ordinal))
        {
            Arm(checkpoint, occurrence);
            OpenSingle(root);
            throw new InvalidOperationException("Recovery checkpoint was not reached: " + checkpoint);
        }
        if (String.Equals(mode, "multi", StringComparison.Ordinal))
        {
            MultiWorker(root, checkpoint, occurrence);
            return;
        }
        if (String.Equals(mode, "multi-recover", StringComparison.Ordinal))
        {
            Arm(checkpoint, occurrence);
            using (DBreezeEngine engine = CreateEngine(root)) { }
            throw new InvalidOperationException("Committed recovery checkpoint was not reached: " + checkpoint);
        }
        if (String.Equals(mode, "kill-stress", StringComparison.Ordinal))
        {
            KillStressWorker(root);
            return;
        }
        throw new ArgumentException("Unknown durability worker mode: " + mode);
    }

    private static void RunSingle(string checkpoint, int occurrence, bool backup, bool allowNew)
    {
        string root = StorageTestSupport.CreateRoot("crash-single");
        try
        {
            RunCrashingChild(backup ? "single-backup" : "single", root, checkpoint, occurrence);
            bool isNew = VerifySingle(root);
            StorageTestSupport.Assert(allowNew || !isNew,
                "Single-table state crossed commit point at " + checkpoint + ".");
            if (String.Equals(checkpoint, "storage.zero-marker.flushed", StringComparison.Ordinal))
                StorageTestSupport.Assert(isNew, "Durable zero marker did not preserve committed state.");

            if (backup)
            {
                bool backupIsNew = RestoreAndVerifySingle(root);
                StorageTestSupport.Assert(occurrence == 3 ? backupIsNew : !backupIsNew,
                    "Backup state does not match its durability barrier occurrence " + occurrence + ".");
            }
            Console.WriteLine("PASS CrashSingle " + checkpoint + "#" + occurrence);
        }
        finally
        {
            StorageTestSupport.DeleteRoot(root);
        }
    }

    private static void RunSingleRecoveryCrash(string checkpoint)
    {
        string root = StorageTestSupport.CreateRoot("crash-recovery");
        try
        {
            RunCrashingChild("single", root, "storage.data.flushed", 1);
            RunCrashingChild("single-recover", root, checkpoint, 1);
            StorageTestSupport.Assert(!VerifySingle(root),
                "Repeated crash during rollback recovery did not converge to old state at " + checkpoint + ".");
            Console.WriteLine("PASS CrashRecovery " + checkpoint);
        }
        finally
        {
            StorageTestSupport.DeleteRoot(root);
        }
    }

    private static void RunMulti(string checkpoint, int occurrence, bool expectNew)
    {
        string root = StorageTestSupport.CreateRoot("crash-multi");
        try
        {
            RunCrashingChild("multi", root, checkpoint, occurrence);
            VerifyMulti(root, expectNew ? 2 : 1);
            Console.WriteLine("PASS CrashMulti " + checkpoint + "#" + occurrence);
        }
        finally
        {
            StorageTestSupport.DeleteRoot(root);
        }
    }

    private static void RunMultiRecoveryCrash()
    {
        string root = StorageTestSupport.CreateRoot("crash-multi-recovery");
        try
        {
            RunCrashingChild("multi", root, "journal.committed", 1);
            RunCrashingChild("multi-recover", root, "journal.recovery-participant-finalized", 1);
            VerifyMulti(root, 2);
            Console.WriteLine("PASS CrashMulti repeated startup recovery");
        }
        finally
        {
            StorageTestSupport.DeleteRoot(root);
        }
    }

    private static void RunKillStress()
    {
        string root = StorageTestSupport.CreateRoot("kill-stress");
        try
        {
            for (int round = 0; round < 6; round++)
            {
                int acknowledgedBefore = ReadAcknowledgements(root).Count;
                using (Process child = Process.Start(CreateWorkerStart("kill-stress", root, "none", 1)))
                {
                    string acknowledgementPath = Path.Combine(root, "ack.bin");
                    Stopwatch wait = Stopwatch.StartNew();
                    int target = acknowledgedBefore + 12 + round * 3;
                    while (wait.ElapsedMilliseconds < 15000)
                    {
                        long length = File.Exists(acknowledgementPath) ? new FileInfo(acknowledgementPath).Length : 0;
                        if (length / 4 >= target)
                            break;
                        if (child.HasExited)
                            throw new InvalidOperationException("Kill-stress worker exited before Process.Kill.");
                        Thread.Sleep(2);
                    }
                    if (child.HasExited)
                        throw new InvalidOperationException("Kill-stress worker exited unexpectedly.");
                    child.Kill();
                    if (!child.WaitForExit(10000))
                        throw new TimeoutException("Kill-stress worker did not terminate.");
                }

                VerifyKillStress(root);
            }
            Console.WriteLine("PASS Process.Kill durable acknowledgement stress");
        }
        finally
        {
            StorageTestSupport.DeleteRoot(root);
        }
    }

    private static void SingleWorker(string root, string checkpoint, int occurrence, bool backup)
    {
        string table = Path.Combine(root, "1");
        using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
        {
            if (backup)
                configuration.Backup.BackupFolderName = Path.Combine(root, "backup");
            StorageLayer storage = new StorageLayer(table, new TrieSettings(), configuration);
            byte[] original = StorageTestSupport.Bytes(32768, 7101);
            long start = StorageTestSupport.DecodePointer(storage.Table_WriteToTheEnd(original));
            storage.Commit();
            File.WriteAllText(Path.Combine(root, "position.txt"), start.ToString(System.Globalization.CultureInfo.InvariantCulture));

            Arm(checkpoint, occurrence);
            storage.Table_WriteByOffset(start, StorageTestSupport.Bytes(original.Length, 7102));
            storage.Commit();
            throw new InvalidOperationException("Single-table checkpoint was not reached: " + checkpoint);
        }
    }

    private static void MultiWorker(string root, string checkpoint, int occurrence)
    {
        using (DBreezeEngine engine = CreateEngine(root))
        {
            using (Transaction transaction = engine.GetTransaction())
            {
                transaction.SynchronizeTables(TableA, TableB);
                transaction.Insert<int, int>(TableA, 1, 1);
                transaction.Insert<int, int>(TableB, 1, 1);
                transaction.Commit();
            }

            Arm(checkpoint, occurrence);
            using (Transaction transaction = engine.GetTransaction())
            {
                transaction.SynchronizeTables(TableA, TableB);
                transaction.Insert<int, int>(TableA, 1, 2);
                transaction.Insert<int, int>(TableB, 1, 2);
                transaction.Commit();
            }
        }
        throw new InvalidOperationException("Multi-table checkpoint was not reached: " + checkpoint);
    }

    private static void KillStressWorker(string root)
    {
        Directory.CreateDirectory(root);
        string acknowledgementPath = Path.Combine(root, "ack.bin");
        List<int> acknowledged = ReadAcknowledgements(root);
        int generation = acknowledged.Count == 0 ? 1 : acknowledged[acknowledged.Count - 1] + 1;
        using (FileStream acknowledgement = new FileStream(acknowledgementPath, FileMode.Append, FileAccess.Write,
            FileShare.Read, 4096, FileOptions.WriteThrough))
        using (DBreezeEngine engine = CreateEngine(Path.Combine(root, "db")))
        {
            while (true)
            {
                using (Transaction transaction = engine.GetTransaction())
                {
                    transaction.SynchronizeTables(StressTableA, StressTableB);
                    transaction.Insert<int, int>(StressTableA, generation, generation);
                    transaction.Insert<int, int>(StressTableB, generation, generation);
                    transaction.Commit();
                }

                byte[] record = BitConverter.GetBytes(generation);
                acknowledgement.Write(record, 0, record.Length);
                acknowledgement.Flush(true);
                generation++;
            }
        }
    }

    private static void VerifyKillStress(string root)
    {
        List<int> acknowledged = ReadAcknowledgements(root);
        Dictionary<int, int> first = new Dictionary<int, int>();
        Dictionary<int, int> second = new Dictionary<int, int>();
        using (DBreezeEngine engine = CreateEngine(Path.Combine(root, "db")))
        using (Transaction transaction = engine.GetTransaction())
        {
            foreach (Row<int, int> row in transaction.SelectForward<int, int>(StressTableA))
                first.Add(row.Key, row.Value);
            foreach (Row<int, int> row in transaction.SelectForward<int, int>(StressTableB))
                second.Add(row.Key, row.Value);
        }

        StorageTestSupport.Assert(first.Count == second.Count,
            "Process.Kill left a different number of rows in paired tables.");
        foreach (KeyValuePair<int, int> row in first)
        {
            int other;
            StorageTestSupport.Assert(second.TryGetValue(row.Key, out other) && other == row.Value,
                "Process.Kill split paired tables at generation " + row.Key + ".");
        }
        foreach (int generation in acknowledged)
        {
            int firstValue;
            int secondValue;
            StorageTestSupport.Assert(first.TryGetValue(generation, out firstValue)
                && second.TryGetValue(generation, out secondValue)
                && firstValue == generation && secondValue == generation,
                "A durably acknowledged transaction is missing: " + generation + ".");
        }
    }

    private static List<int> ReadAcknowledgements(string root)
    {
        List<int> result = new List<int>();
        string path = Path.Combine(root, "ack.bin");
        if (!File.Exists(path))
            return result;
        byte[] bytes;
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete))
        {
            int length = checked((int)(stream.Length - stream.Length % 4));
            bytes = new byte[length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0)
                    break;
                offset += read;
            }
            if (offset != bytes.Length)
                Array.Resize(ref bytes, offset - offset % 4);
        }
        for (int offset = 0; offset < bytes.Length; offset += 4)
            result.Add(BitConverter.ToInt32(bytes, offset));
        return result;
    }

    private static bool VerifySingle(string root)
    {
        long start = Int64.Parse(File.ReadAllText(Path.Combine(root, "position.txt")),
            System.Globalization.CultureInfo.InvariantCulture);
        byte[] oldValue = StorageTestSupport.Bytes(32768, 7101);
        byte[] newValue = StorageTestSupport.Bytes(32768, 7102);
        byte[] actual;
        using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
        {
            StorageLayer storage = new StorageLayer(Path.Combine(root, "1"), new TrieSettings(), configuration);
            actual = storage.Table_Read(true, start, oldValue.Length);
            storage.Table_Dispose();
        }

        if (Equal(oldValue, actual))
            return false;
        if (Equal(newValue, actual))
            return true;
        throw new InvalidOperationException("Single-table crash produced a torn old/new state.");
    }

    private static bool RestoreAndVerifySingle(string root)
    {
        string destination = Path.Combine(root, "restored");
        BackupRestorer restorer = StorageTestSupport.CreateRestorer(Path.Combine(root, "backup"), destination);
        restorer.OnRestore += delegate { };
        restorer.StartRestoration();
        File.Copy(Path.Combine(root, "position.txt"), Path.Combine(destination, "position.txt"));
        return VerifySingle(destination);
    }

    private static void VerifyMulti(string root, int expected)
    {
        using (DBreezeEngine engine = CreateEngine(root))
        using (Transaction transaction = engine.GetTransaction())
        {
            Row<int, int> a = transaction.Select<int, int>(TableA, 1);
            Row<int, int> b = transaction.Select<int, int>(TableB, 1);
            StorageTestSupport.Assert(a.Exists && b.Exists,
                "Crash recovery lost a multi-table row. Exists=" + a.Exists + "/" + b.Exists + ".");
            StorageTestSupport.Assert(a.Value == expected && b.Value == expected,
                "Multi-table crash recovery produced split-brain state: " + a.Value + "/" + b.Value + ".");
        }
    }

    private static void OpenSingle(string root)
    {
        using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
        {
            StorageLayer storage = new StorageLayer(Path.Combine(root, "1"), new TrieSettings(), configuration);
            storage.Table_Dispose();
        }
    }

    private static DBreezeEngine CreateEngine(string root)
    {
        DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration();
        configuration.DBreezeDataFolderName = root;
        return new DBreezeEngine(configuration);
    }

    private static void Arm(string checkpoint, int occurrence)
    {
        Type type = typeof(StorageLayer).Assembly.GetType("DBreeze.Storage.DurabilityTestHooks", true);
        FieldInfo field = type.GetField("Handler", BindingFlags.Static | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException("DBREEZE_DURABILITY_TEST_HOOKS is not enabled.");
        int seen = 0;
        armedHandler = delegate(string hit)
        {
            if (!String.Equals(hit, checkpoint, StringComparison.Ordinal))
                return;
            seen++;
            if (seen == occurrence)
                Environment.FailFast("DBreeze durability checkpoint " + checkpoint + "#" + occurrence);
        };
        field.SetValue(null, armedHandler);
    }

    private static bool HooksAvailable()
    {
        Type type = typeof(StorageLayer).Assembly.GetType("DBreeze.Storage.DurabilityTestHooks", false);
        return type != null && type.GetField("Handler", BindingFlags.Static | BindingFlags.NonPublic) != null;
    }

    private static void RunCrashingChild(string mode, string root, string checkpoint, int occurrence)
    {
        ProcessStartInfo start = CreateWorkerStart(mode, root, checkpoint, occurrence);
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using (Process child = Process.Start(start))
        {
            string stdout = child.StandardOutput.ReadToEnd();
            string stderr = child.StandardError.ReadToEnd();
            if (!child.WaitForExit(30000))
            {
                child.Kill();
                throw new TimeoutException("Durability worker timed out at " + checkpoint + ".");
            }
            if (child.ExitCode == 0)
                throw new InvalidOperationException("Worker did not crash at " + checkpoint + ".\n" + stdout + stderr);
        }
    }

    private static ProcessStartInfo CreateWorkerStart(string mode, string root, string checkpoint, int occurrence)
    {
        string processPath = Process.GetCurrentProcess().MainModule.FileName;
        string assemblyPath = Assembly.GetExecutingAssembly().Location;
        bool dotnetHost = String.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet",
            StringComparison.OrdinalIgnoreCase);
        string arguments = (dotnetHost ? Quote(assemblyPath) + " " : String.Empty)
            + "--durability-crash-worker " + Quote(mode) + " " + Quote(root) + " " + Quote(checkpoint) + " " + occurrence;
        ProcessStartInfo start = new ProcessStartInfo(processPath, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.EnvironmentVariables["DOTNET_DbgEnableMiniDump"] = "0";
        start.EnvironmentVariables["COMPlus_DbgEnableMiniDump"] = "0";
        return start;
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static bool Equal(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length)
            return false;
        for (int index = 0; index < left.Length; index++)
            if (left[index] != right[index])
                return false;
        return true;
    }
}
