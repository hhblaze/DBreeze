using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using DBreeze;
using DBreeze.Storage;
#if PORTABLE_HOST
using DBreeze.Programmers;
#endif

internal static class StorageTestSupport
{
    internal const int HeaderSize = 64;

    internal static string TargetName
    {
        get
        {
#if NET6_HOST
            return "Net6";
#elif NETSTANDARD_HOST
            return "NetStandard-consumer";
#elif NETCOREAPP_HOST
            return "NetCoreApp3.1";
#elif NETFRAMEWORK_HOST
            return ".NET Framework 4.7.2";
#elif PORTABLE_HOST
            return "Portable/Profile111";
#elif NET8_HOST
            return "Net8-modern";
#else
            return "Unknown";
#endif
        }
    }

    internal static DBreezeConfiguration CreateConfiguration()
    {
        var configuration = new DBreezeConfiguration
        {
            Storage = DBreezeConfiguration.eStorage.DISK,
        };
#if PORTABLE_HOST
        configuration.FSFactory = new FSFactory();
#endif
        return configuration;
    }

    internal static BackupRestorer CreateRestorer(string backup, string destination)
    {
#if PORTABLE_HOST
        DBreezeConfiguration configuration = CreateConfiguration();
        return new BackupRestorer(configuration)
        {
            BackupFolder = backup,
            DataBaseFolder = destination,
        };
#else
        return new BackupRestorer
        {
            BackupFolder = backup,
            DataBaseFolder = destination,
        };
#endif
    }

    internal static string CreateRoot(string scenario)
    {
        string configuredRoot = Environment.GetEnvironmentVariable("DBREEZE_TEST_ROOT");
        string baseRoot = String.IsNullOrWhiteSpace(configuredRoot)
            ? @"D:\Temp\DbreezeDbTest"
            : Path.GetFullPath(configuredRoot);
        string root = Path.Combine(baseRoot, "storage-contracts", TargetName.Replace(' ', '_'), scenario,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    internal static byte[] Bytes(int length, int seed)
    {
        byte[] result = new byte[length];
        new Random(seed).NextBytes(result);
        return result;
    }

    internal static long DecodePointer(byte[] pointer)
    {
        ulong value = 0;
        for (int index = 0; index < pointer.Length; index++)
            value = (value << 8) | pointer[index];
        return checked((long)value);
    }

    internal static byte[] Int64BigEndian(long value)
    {
        ulong encoded = unchecked((ulong)value) ^ 0x8000000000000000UL;
        byte[] result = new byte[8];
        for (int index = 7; index >= 0; index--)
        {
            result[index] = (byte)encoded;
            encoded >>= 8;
        }
        return result;
    }

    internal static string Sha256(string path)
    {
        using (SHA256 hash = SHA256.Create())
        using (Stream stream = File.OpenRead(path))
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", String.Empty);
    }

    internal static long DatabaseSize(string root)
    {
        long result = 0;
        if (!Directory.Exists(root))
            return result;
        foreach (string path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            result += new FileInfo(path).Length;
        return result;
    }

    internal static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    internal static void AssertBytes(byte[] expected, byte[] actual, string message)
    {
        if (expected == null || actual == null || expected.Length != actual.Length)
            throw new InvalidOperationException(message + " Length mismatch.");
        for (int index = 0; index < expected.Length; index++)
        {
            if (expected[index] != actual[index])
                throw new InvalidOperationException(message + " First mismatch at " + index + ".");
        }
    }

    internal static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException("Expected " + typeof(TException).FullName + ".");
    }

    internal static void DeleteRoot(string root)
    {
        if (String.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return;
        string full = Path.GetFullPath(root);
        string leaf = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Guid marker;
        if (!Guid.TryParseExact(leaf, "N", out marker))
            throw new InvalidOperationException("Refusing to delete an unmarked storage-contract directory: " + full);
        Directory.Delete(full, true);
    }
}
