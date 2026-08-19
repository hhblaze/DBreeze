using BenchmarkDotNet.Attributes;
using DBreeze;
using DBreeze.Storage;
using DBreeze.Storage.RemoteInstance;

namespace DBreeze.Net8.Benchmarks;

[MemoryDiagnoser]
[ThreadingDiagnoser]
[MedianColumn]
public class StorageBenchmarks
{
    private string _root;
    private StorageLayer _readStorage;
    private StorageLayer _updateStorage;
    private StorageLayer _appendStorage;
    private DBreezeConfiguration _readConfiguration;
    private DBreezeConfiguration _updateConfiguration;
    private DBreezeConfiguration _appendConfiguration;
    private byte[] _updateValue;
    private byte[] _appendValue;
    private long _dataStart;
    private long[] _randomReadOffsets;
    private long[] _localReadOffsets;
    private string _backupFolder;
    private string _restoreFolder;
    private string _remoteFolder;
    private StorageLayer _remoteStorage;
    private DBreezeConfiguration _remoteConfiguration;
    private RemoteTablesHandler _remoteHandler;
    private byte[] _remoteValue;

    [GlobalSetup]
    public void Setup()
    {
        string configuredRoot = Environment.GetEnvironmentVariable("DBREEZE_BENCHMARK_ROOT");
        _root = Path.Combine(
            string.IsNullOrWhiteSpace(configuredRoot) ? Path.GetTempPath() : Path.GetFullPath(configuredRoot),
            "DBreeze.Net8.StorageBenchmarks",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _readConfiguration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK };
        _readStorage = new StorageLayer(Path.Combine(_root, "1"), new TrieSettings(), _readConfiguration);
        byte[] readData = new byte[1024 * 1024];
        new Random(100).NextBytes(readData);
        _dataStart = DecodePointer(_readStorage.Table_WriteToTheEnd(readData));
        _readStorage.Commit();
        _randomReadOffsets = new long[1_024];
        _localReadOffsets = new long[1_024];
        var readRandom = new Random(103);
        for (int i = 0; i < _randomReadOffsets.Length; i++)
        {
            _randomReadOffsets[i] = _dataStart + readRandom.Next(readData.Length - 64);
            _localReadOffsets[i] = _dataStart + ((i * 61) & 0x0FFF);
        }

        _updateConfiguration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK };
        _updateStorage = new StorageLayer(Path.Combine(_root, "2"), new TrieSettings(), _updateConfiguration);
        _updateStorage.Table_WriteToTheEnd(readData);
        _updateStorage.Commit();
        _updateValue = new byte[128];
        new Random(101).NextBytes(_updateValue);

        _appendValue = new byte[64 * 1024];
        new Random(102).NextBytes(_appendValue);
        _remoteValue = new byte[4 * 1024 * 1024 + 137];
        new Random(104).NextBytes(_remoteValue);

        _backupFolder = Path.Combine(_root, "backup");
        string backupSource = Path.Combine(_root, "backup-source");
        Directory.CreateDirectory(backupSource);
        using (var backupConfiguration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK })
        {
            backupConfiguration.Backup.BackupFolderName = _backupFolder;
            var backupStorage = new StorageLayer(Path.Combine(backupSource, "3"), new TrieSettings(), backupConfiguration);
            backupStorage.Table_WriteToTheEnd(readData);
            backupStorage.Commit();
            backupStorage.Table_Dispose();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        CleanupAppendIteration();
        CleanupRemoteIteration();
        _readStorage?.Table_Dispose();
        _updateStorage?.Table_Dispose();
        _readConfiguration?.Dispose();
        _updateConfiguration?.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [IterationSetup(Target = nameof(SequentialAppendAndCommit))]
    public void SetupAppendIteration()
    {
        CleanupAppendIteration();
        _appendConfiguration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK };
        string appendDirectory = Path.Combine(_root, "append", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(appendDirectory);
        _appendStorage = new StorageLayer(
            Path.Combine(appendDirectory, "4"),
            new TrieSettings(),
            _appendConfiguration);
    }

    [IterationCleanup(Target = nameof(SequentialAppendAndCommit))]
    public void CleanupAppendIteration()
    {
        string path = _appendStorage?.Table_FileName;
        _appendStorage?.Table_Dispose();
        _appendStorage = null;
        _appendConfiguration?.Dispose();
        _appendConfiguration = null;
        if (!string.IsNullOrEmpty(path))
        {
            string directory = Path.GetDirectoryName(path);
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [IterationSetup(Target = nameof(IncrementalBackupRestore))]
    public void SetupRestoreIteration()
    {
        _restoreFolder = Path.Combine(_root, "restore", Guid.NewGuid().ToString("N"));
    }

    [IterationCleanup(Target = nameof(IncrementalBackupRestore))]
    public void CleanupRestoreIteration()
    {
        if (!string.IsNullOrEmpty(_restoreFolder) && Directory.Exists(_restoreFolder))
            Directory.Delete(_restoreFolder, true);
        _restoreFolder = null;
    }

    [IterationSetup(Target = nameof(LargeRemoteRoundTrip))]
    public void SetupRemoteIteration()
    {
        CleanupRemoteIteration();
        _remoteFolder = Path.Combine(_root, "remote", Guid.NewGuid().ToString("N"));
        _remoteHandler = new RemoteTablesHandler(_remoteFolder);
        _remoteConfiguration = new DBreezeConfiguration
        {
            Storage = DBreezeConfiguration.eStorage.RemoteInstance,
            RICommunicator = new BenchmarkCommunicator(_remoteHandler),
        };
        _remoteStorage = new StorageLayer(Path.Combine("nested", "5"), new TrieSettings(), _remoteConfiguration);
    }

    [IterationCleanup(Target = nameof(LargeRemoteRoundTrip))]
    public void CleanupRemoteIteration()
    {
        _remoteStorage?.Table_Dispose();
        _remoteStorage = null;
        _remoteConfiguration?.Dispose();
        _remoteConfiguration = null;
        _remoteHandler?.Dispose();
        _remoteHandler = null;
        if (!string.IsNullOrEmpty(_remoteFolder) && Directory.Exists(_remoteFolder))
            Directory.Delete(_remoteFolder, true);
        _remoteFolder = null;
    }

    [Benchmark(OperationsPerInvoke = 1_000)]
    public int CommittedRead()
    {
        int checksum = 0;
        for (int i = 0; i < 1_000; i++)
            checksum += _readStorage.Table_Read(true, _dataStart + ((i * 997) & 0x7FFFF), 4096)[0];
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = 8)]
    public int EightThreadCommittedRead()
    {
        int checksum = 0;
        Parallel.For(0, 8, worker =>
        {
            byte[] value = _readStorage.Table_Read(true, _dataStart + worker * 4096, 4096);
            Interlocked.Add(ref checksum, value[0]);
        });
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = 1_024)]
    public int Random64ByteRead()
    {
        int checksum = 0;
        for (int i = 0; i < _randomReadOffsets.Length; i++)
            checksum += _readStorage.Table_Read(true, _randomReadOffsets[i], 64)[0];
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = 1_024)]
    public int Local64ByteRead()
    {
        int checksum = 0;
        for (int i = 0; i < _localReadOffsets.Length; i++)
            checksum += _readStorage.Table_Read(true, _localReadOffsets[i], 64)[0];
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = 1_024)]
    public int EightThread64ByteRead()
    {
        int checksum = 0;
        Parallel.For(0, 8, worker =>
        {
            int localChecksum = 0;
            int first = worker * 128;
            for (int i = first; i < first + 128; i++)
                localChecksum += _readStorage.Table_Read(true, _localReadOffsets[i], 64)[0];
            Interlocked.Add(ref checksum, localChecksum);
        });
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = 100)]
    public void RandomUpdateAndCommit()
    {
        for (int i = 0; i < 100; i++)
            _updateStorage.Table_WriteByOffset(64 + i * 257, _updateValue);
        _updateStorage.Commit();
    }

    [Benchmark(OperationsPerInvoke = 128)]
    public void SequentialAppendAndCommit()
    {
        for (int i = 0; i < 128; i++)
        {
            _appendStorage.Table_WriteToTheEnd(_appendValue);
            _appendStorage.Commit();
        }
    }

    [Benchmark(OperationsPerInvoke = 64)]
    public void IncrementalBackupRestore()
    {
        for (int i = 0; i < 64; i++)
        {
            var restorer = new BackupRestorer
            {
                BackupFolder = _backupFolder,
                DataBaseFolder = Path.Combine(_restoreFolder, i.ToString("D2")),
            };
            // Older DBreeze releases invoke this event without a null check.
            restorer.OnRestore += delegate { };
            restorer.StartRestoration();
        }
    }

    [Benchmark(OperationsPerInvoke = 16)]
    public int LargeRemoteRoundTrip()
    {
        int checksum = 0;
        for (int i = 0; i < 16; i++)
        {
            long position = DecodePointer(_remoteStorage.Table_WriteToTheEnd(_remoteValue));
            _remoteStorage.Commit();
            byte[] result = _remoteStorage.Table_Read(true, position, _remoteValue.Length);
            checksum += result[0] + result[result.Length - 1];
        }
        return checksum;
    }

    private static long DecodePointer(byte[] pointer)
    {
        ulong value = 0;
        foreach (byte item in pointer)
            value = (value << 8) | item;
        return checked((long)value);
    }

    private sealed class BenchmarkCommunicator : IRemoteInstanceCommunicator
    {
        private readonly RemoteTablesHandler _handler;

        public BenchmarkCommunicator(RemoteTablesHandler handler) => _handler = handler;

        public byte[] Send(byte[] data) => _handler.ParseProtocol(data);
    }
}
