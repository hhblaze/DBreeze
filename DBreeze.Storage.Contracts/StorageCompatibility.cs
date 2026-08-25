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
