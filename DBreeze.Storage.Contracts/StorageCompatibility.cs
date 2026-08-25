using System;
using System.IO;
using DBreeze;
using DBreeze.Storage;

internal static class StorageCompatibility
{
    private const string TableName = "1";

    internal static void Create(string root, int seed)
    {
        Directory.CreateDirectory(root);
        string table = Path.Combine(root, TableName);
        byte[] payload = StorageTestSupport.Bytes(64 * 1024 + 31, seed);
        using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
        {
            var storage = new StorageLayer(table, new TrieSettings(), configuration);
            long offset = StorageTestSupport.DecodePointer(storage.Table_WriteToTheEnd(payload));
            StorageTestSupport.Assert(offset == StorageTestSupport.HeaderSize, "Unexpected initial payload offset.");
            storage.Commit();
            byte[] patch = StorageTestSupport.Bytes(777, seed + 1);
            storage.Table_WriteByOffset(offset + 4093, patch);
            storage.TransactionalCommit();
            storage.TransactionalCommitIsFinished();
            storage.Table_Dispose();
        }
        WriteManifest(root, seed, false);
        Console.WriteLine("COMPAT CREATE target=" + StorageTestSupport.TargetName + " sha256=" + StorageTestSupport.Sha256(table));
    }

    internal static void Verify(string root, int seed)
    {
        string table = Path.Combine(root, TableName);
        byte[] expected = Expected(seed, File.Exists(Path.Combine(root, "extended.marker")));
        string beforeHash = StorageTestSupport.Sha256(table);
        long beforeLength = new FileInfo(table).Length;
        using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
        {
            var storage = new StorageLayer(table, new TrieSettings(), configuration);
            StorageTestSupport.AssertBytes(expected,
                storage.Table_Read(true, StorageTestSupport.HeaderSize, expected.Length), "Compatibility payload differs.");
            storage.Table_Dispose();
        }
        StorageTestSupport.Assert(beforeLength == new FileInfo(table).Length && beforeHash == StorageTestSupport.Sha256(table),
            "Read-only compatibility verification changed the data file.");
        Console.WriteLine("COMPAT VERIFY target=" + StorageTestSupport.TargetName + " sha256=" + beforeHash);
    }

    internal static void Extend(string root, int seed)
    {
        Verify(root, seed);
        string table = Path.Combine(root, TableName);
        byte[] extension = StorageTestSupport.Bytes(8193, seed + 2);
        using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
        {
            var storage = new StorageLayer(table, new TrieSettings(), configuration);
            long offset = StorageTestSupport.DecodePointer(storage.Table_WriteToTheEnd(extension));
            StorageTestSupport.Assert(offset == StorageTestSupport.HeaderSize + 64 * 1024 + 31,
                "Compatibility extension offset changed.");
            storage.Commit();
            storage.Table_Dispose();
        }
        File.WriteAllText(Path.Combine(root, "extended.marker"), seed.ToString());
        WriteManifest(root, seed, true);
        Verify(root, seed);
        Console.WriteLine("COMPAT EXTEND target=" + StorageTestSupport.TargetName);
    }

    internal static void PrepareRollback(string root, int seed)
    {
        Directory.CreateDirectory(root);
        string table = Path.Combine(root, TableName);
        byte[] payload = StorageTestSupport.Bytes(64 * 1024 + 31, seed);
        using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
        {
            var storage = new StorageLayer(table, new TrieSettings(), configuration);
            long offset = StorageTestSupport.DecodePointer(storage.Table_WriteToTheEnd(payload));
            storage.Commit();
            storage.Table_WriteByOffset(offset + 4093, StorageTestSupport.Bytes(777, seed + 1));
            storage.TransactionalCommit();
            storage.Table_Dispose();
        }
        Console.WriteLine("COMPAT ACTIVE ROLLBACK target=" + StorageTestSupport.TargetName
            + " rhp=" + StorageTestSupport.Sha256(table + ".rhp")
            + " rol=" + StorageTestSupport.Sha256(table + ".rol"));
    }

    internal static void VerifyRollbackRecovered(string root, int seed)
    {
        string table = Path.Combine(root, TableName);
        byte[] expected = StorageTestSupport.Bytes(64 * 1024 + 31, seed);
        using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
        {
            var storage = new StorageLayer(table, new TrieSettings(), configuration);
            StorageTestSupport.AssertBytes(expected,
                storage.Table_Read(true, StorageTestSupport.HeaderSize, expected.Length),
                "Cross-version active rollback did not restore the old state.");
            storage.Table_Dispose();
        }
        StorageTestSupport.AssertBytes(StorageTestSupport.Int64BigEndian(0), File.ReadAllBytes(table + ".rhp"),
            "Recovered rollback marker is not the legacy big-endian zero.");
        Console.WriteLine("COMPAT ROLLBACK VERIFY target=" + StorageTestSupport.TargetName);
    }

    internal static void CreateBackup(string source, string backup, int seed)
    {
        Directory.CreateDirectory(source);
        using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
        {
            configuration.Backup.BackupFolderName = backup;
            var storage = new StorageLayer(Path.Combine(source, TableName), new TrieSettings(), configuration);
            byte[] payload = StorageTestSupport.Bytes(64 * 1024 + 31, seed);
            long offset = StorageTestSupport.DecodePointer(storage.Table_WriteToTheEnd(payload));
            storage.Commit();
            storage.Table_WriteByOffset(offset + 4093, StorageTestSupport.Bytes(777, seed + 1));
            storage.Commit();
            storage.Table_Dispose();
        }
        Console.WriteLine("COMPAT BACKUP CREATE target=" + StorageTestSupport.TargetName);
    }

    internal static void RestoreBackup(string backup, string destination, int seed)
    {
        BackupRestorer restorer = StorageTestSupport.CreateRestorer(backup, destination);
        restorer.OnRestore += delegate { };
        restorer.StartRestoration();
        byte[] expected = Expected(seed, false);
        using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
        {
            var storage = new StorageLayer(Path.Combine(destination, TableName), new TrieSettings(), configuration);
            StorageTestSupport.AssertBytes(expected,
                storage.Table_Read(true, StorageTestSupport.HeaderSize, expected.Length),
                "Cross-version restored backup differs.");
            storage.Table_Dispose();
        }
        Console.WriteLine("COMPAT BACKUP RESTORE target=" + StorageTestSupport.TargetName);
    }

    internal static void RunCorruption(string scenario, string root, int seed)
    {
        Directory.CreateDirectory(root);
        string table = Path.Combine(root, TableName);
        byte[] original = StorageTestSupport.Bytes(64 * 1024 + 31, seed);
        byte[] updated = StorageTestSupport.Bytes(original.Length, seed + 1);
        ushort pointerLength = scenario == "overflow-offset" ? (ushort)8 : (ushort)5;
        using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
        {
            TrieSettings settings = new TrieSettings { POINTER_LENGTH = pointerLength };
            var storage = new StorageLayer(table, settings, configuration);
            long offset = StorageTestSupport.DecodePointer(storage.Table_WriteToTheEnd(original));
            storage.Commit();
            storage.Table_WriteByOffset(offset, updated);
            storage.TransactionalCommit();
            storage.Table_Dispose();
        }

        byte[] rollback;
        byte[] marker;
        if (scenario == "short-rhp")
        {
            rollback = new byte[0];
            marker = new byte[] { 1, 2, 3, 4 };
        }
        else if (scenario == "marker-outside")
        {
            rollback = RollbackRecord(StorageTestSupport.HeaderSize, original, pointerLength);
            marker = StorageTestSupport.Int64BigEndian(rollback.Length + 10L);
        }
        else if (scenario == "truncated-header")
        {
            rollback = new byte[] { 1, 0, 0, 0, 0 };
            marker = StorageTestSupport.Int64BigEndian(rollback.Length);
        }
        else if (scenario == "truncated-payload")
        {
            rollback = RollbackRecord(StorageTestSupport.HeaderSize, new byte[16], pointerLength);
            Array.Resize(ref rollback, rollback.Length - 12);
            marker = StorageTestSupport.Int64BigEndian(rollback.Length);
        }
        else if (scenario == "unknown-protocol")
        {
            rollback = new byte[] { 99 };
            marker = StorageTestSupport.Int64BigEndian(rollback.Length);
        }
        else if (scenario == "overflow-offset")
        {
            rollback = RollbackRecord(UInt64.MaxValue, new byte[] { 7 }, pointerLength);
            marker = StorageTestSupport.Int64BigEndian(rollback.Length);
        }
        else if (scenario == "partial-then-unknown")
        {
            byte[] prefix = new byte[16];
            Buffer.BlockCopy(original, 0, prefix, 0, prefix.Length);
            byte[] first = RollbackRecord(StorageTestSupport.HeaderSize, prefix, pointerLength);
            rollback = new byte[first.Length + 1];
            Buffer.BlockCopy(first, 0, rollback, 0, first.Length);
            rollback[rollback.Length - 1] = 99;
            marker = StorageTestSupport.Int64BigEndian(rollback.Length);
        }
        else if (scenario == "large-partial-then-unknown")
        {
            byte[] prefix = new byte[16 * 1024];
            Buffer.BlockCopy(original, 0, prefix, 0, prefix.Length);
            byte[] first = RollbackRecord(StorageTestSupport.HeaderSize, prefix, pointerLength);
            rollback = new byte[first.Length + 1];
            Buffer.BlockCopy(first, 0, rollback, 0, first.Length);
            rollback[rollback.Length - 1] = 99;
            marker = StorageTestSupport.Int64BigEndian(rollback.Length);
        }
        else
        {
            throw new ArgumentException("Unknown corruption scenario: " + scenario);
        }

        File.WriteAllBytes(table + ".rol", rollback);
        File.WriteAllBytes(table + ".rhp", marker);
        string outcome = "success";
        try
        {
            using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
            {
                TrieSettings settings = new TrieSettings { POINTER_LENGTH = pointerLength };
                var storage = new StorageLayer(table, settings, configuration);
                storage.Table_Dispose();
            }
        }
        catch (Exception)
        {
            outcome = "error";
        }

        // A historical constructor failure can leave FileShare.None handles alive
        // until this oracle process exits. The parent compares file bytes afterwards.
        string result = "outcome=" + outcome + Environment.NewLine;
        File.WriteAllText(Path.Combine(root, "corruption.result"), result);
        Console.WriteLine("COMPAT CORRUPTION " + scenario + " target=" + StorageTestSupport.TargetName
            + " outcome=" + outcome);
    }

    private static byte[] RollbackRecord(long offset, byte[] data, int pointerLength)
    {
        return RollbackRecord(unchecked((ulong)offset), data, pointerLength);
    }

    private static byte[] RollbackRecord(ulong offset, byte[] data, int pointerLength)
    {
        byte[] record = new byte[1 + pointerLength + 4 + data.Length];
        record[0] = 1;
        for (int index = pointerLength; index >= 1; index--)
        {
            record[index] = (byte)offset;
            offset >>= 8;
        }
        uint length = (uint)data.Length;
        int lengthOffset = 1 + pointerLength;
        record[lengthOffset] = (byte)(length >> 24);
        record[lengthOffset + 1] = (byte)(length >> 16);
        record[lengthOffset + 2] = (byte)(length >> 8);
        record[lengthOffset + 3] = (byte)length;
        Buffer.BlockCopy(data, 0, record, lengthOffset + 4, data.Length);
        return record;
    }

    private static byte[] Expected(int seed, bool extended)
    {
        byte[] original = StorageTestSupport.Bytes(64 * 1024 + 31, seed);
        byte[] patch = StorageTestSupport.Bytes(777, seed + 1);
        Buffer.BlockCopy(patch, 0, original, 4093, patch.Length);
        if (!extended)
            return original;
        byte[] extension = StorageTestSupport.Bytes(8193, seed + 2);
        byte[] result = new byte[original.Length + extension.Length];
        Buffer.BlockCopy(original, 0, result, 0, original.Length);
        Buffer.BlockCopy(extension, 0, result, original.Length, extension.Length);
        return result;
    }

    private static void WriteManifest(string root, int seed, bool extended)
    {
        string table = Path.Combine(root, TableName);
        File.WriteAllText(Path.Combine(root, "compat.manifest"),
            "seed=" + seed + Environment.NewLine +
            "extended=" + extended + Environment.NewLine +
            "length=" + new FileInfo(table).Length + Environment.NewLine +
            "sha256=" + StorageTestSupport.Sha256(table) + Environment.NewLine);
    }
}
