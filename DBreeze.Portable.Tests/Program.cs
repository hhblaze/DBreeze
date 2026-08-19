using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using DBreeze;
using DBreeze.Programmers;
using DBreeze.Storage;

internal static class Program
{
    private const string HashOnlyArgument = "--compat-hash";

    private static int Main(string[] args)
    {
        bool hashOnly = args.Any(arg => String.Equals(arg, HashOnlyArgument, StringComparison.OrdinalIgnoreCase));
        string root = Path.Combine(Path.GetTempPath(), "DBreeze-PortableTests", Guid.NewGuid().ToString("N"));

        try
        {
            string restoredTable = RestoreLegacyFiles(root, validateProgress: !hashOnly);
            string hash = ComputeSha256(restoredTable);

            if (hashOnly)
                Console.WriteLine(hash);
            else
            {
                RestoreStandardIbp(root);
                Console.WriteLine("PASS PortableBackupRestorerAcceptsLegacyFileNames");
                Console.WriteLine("SHA256 " + hash);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string RestoreLegacyFiles(string root, bool validateProgress)
    {
        string backup = Path.Combine(root, "legacy-backup");
        string destination = Path.Combine(root, "legacy-destination");
        Directory.CreateDirectory(backup);

        File.WriteAllBytes(
            Path.Combine(backup, "dbreeze_ibp_20000101000002.custom"),
            BuildBackupWriteRecord(1, 0, 64, new byte[] { 0x22 }));
        File.WriteAllBytes(
            Path.Combine(backup, "dbreeze_ibp_20000101000001"),
            BuildBackupWriteRecord(1, 0, 64, new byte[] { 0x11 }));
        File.WriteAllBytes(
            Path.Combine(backup, "unrelated-data.bin"),
            new byte[2 * 1024 * 1024]);

        var progressValues = new List<int>();
        bool finished = false;
        int finishedProgress = -1;
        var restorer = CreateRestorer(backup, destination);
        restorer.OnRestore += progress =>
        {
            if (progress.Finished)
            {
                finished = true;
                finishedProgress = progress.ReadinessInProcent;
            }
            else
                progressValues.Add(progress.ReadinessInProcent);
        };
        restorer.StartRestoration();

        string table = Path.Combine(destination, "1");
        byte[] bytes = File.ReadAllBytes(table);
        Assert(bytes.Length == 65 && bytes[64] == 0x22,
            "Legacy backup files were not restored in ordinal filename order.");
        if (validateProgress)
        {
            Assert(progressValues.Contains(50),
                "Unrelated files affected Portable backup restoration progress.");
            Assert(finished && finishedProgress == 100,
                "Portable backup restoration did not report completed 100% progress.");
        }

        return table;
    }

    private static void RestoreStandardIbp(string root)
    {
        string backup = Path.Combine(root, "standard-backup");
        string destination = Path.Combine(root, "standard-destination");
        Directory.CreateDirectory(backup);
        File.WriteAllBytes(
            Path.Combine(backup, "dbreeze_ibp_20000101000003.ibp"),
            BuildBackupWriteRecord(2, 0, 7, new byte[] { 0x33 }));

        CreateRestorer(backup, destination).StartRestoration();

        byte[] bytes = File.ReadAllBytes(Path.Combine(destination, "2"));
        Assert(bytes.Length == 8 && bytes[7] == 0x33,
            "Portable backup restoration no longer accepts standard .ibp files.");
    }

    private static BackupRestorer CreateRestorer(string backup, string destination)
    {
        var configuration = new DBreezeConfiguration
        {
            FSFactory = new FSFactory(),
        };
        return new BackupRestorer(configuration)
        {
            BackupFolder = backup,
            DataBaseFolder = destination,
        };
    }

    private static byte[] BuildBackupWriteRecord(ulong fileNumber, byte type, long offset, byte[] payload)
    {
        byte[] record = new byte[4 + 17 + payload.Length];
        WriteUInt32BigEndian(record, 0, checked((uint)(17 + payload.Length)));
        WriteUInt64BigEndian(record, 4, fileNumber);
        record[12] = type;
        WriteUInt64BigEndian(record, 13, unchecked((ulong)offset) ^ 0x8000000000000000UL);
        Buffer.BlockCopy(payload, 0, record, 21, payload.Length);
        return record;
    }

    private static void WriteUInt32BigEndian(byte[] destination, int offset, uint value)
    {
        destination[offset] = (byte)(value >> 24);
        destination[offset + 1] = (byte)(value >> 16);
        destination[offset + 2] = (byte)(value >> 8);
        destination[offset + 3] = (byte)value;
    }

    private static void WriteUInt64BigEndian(byte[] destination, int offset, ulong value)
    {
        for (int index = 7; index >= 0; index--)
        {
            destination[offset + index] = (byte)value;
            value >>= 8;
        }
    }

    private static string ComputeSha256(string fileName)
    {
        using (var sha256 = SHA256.Create())
        using (var stream = File.OpenRead(fileName))
            return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", String.Empty);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
