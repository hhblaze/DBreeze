using System.Diagnostics;
using System.Threading;

namespace DBreeze.Storage
{
    /// <summary>
    /// Disabled-by-default counters used by the release performance audit. They are deliberately
    /// internal and do not participate in the storage or public API contract.
    /// </summary>
    internal static class WritePathDiagnostics
    {
        static int _enabled;
        static long _writeCalls;
        static long _writeBytes;
        static long _durableFlushes;
        static long _durableFlushMicroseconds;
        static long _rollbackWriteCalls;
        static long _journalFlushes;
        static long _mappingCreates;
        static long _mappingDisposes;

        internal static bool Enabled => Volatile.Read(ref _enabled) != 0;

        internal static void SetEnabled(bool enabled) => Volatile.Write(ref _enabled, enabled ? 1 : 0);

        internal static void Write(int bytes, bool rollback)
        {
            if (!Enabled) return;
            Interlocked.Increment(ref _writeCalls);
            Interlocked.Add(ref _writeBytes, bytes);
            if (rollback) Interlocked.Increment(ref _rollbackWriteCalls);
        }

        internal static long FlushStarted() => Enabled ? Stopwatch.GetTimestamp() : 0;

        internal static void FlushFinished(long started, bool journal)
        {
            if (started == 0) return;
            long elapsed = Stopwatch.GetTimestamp() - started;
            Interlocked.Increment(ref _durableFlushes);
            Interlocked.Add(ref _durableFlushMicroseconds,
                (long)(elapsed * 1_000_000.0 / Stopwatch.Frequency));
            if (journal) Interlocked.Increment(ref _journalFlushes);
        }

        internal static void MappingCreated()
        {
            if (Enabled) Interlocked.Increment(ref _mappingCreates);
        }

        internal static void MappingDisposed()
        {
            if (Enabled) Interlocked.Increment(ref _mappingDisposes);
        }

        internal static long[] GetDiagnostics() => new[]
        {
            Volatile.Read(ref _writeCalls),
            Volatile.Read(ref _writeBytes),
            Volatile.Read(ref _durableFlushes),
            Volatile.Read(ref _durableFlushMicroseconds),
            Volatile.Read(ref _rollbackWriteCalls),
            Volatile.Read(ref _journalFlushes),
            Volatile.Read(ref _mappingCreates),
            Volatile.Read(ref _mappingDisposes),
        };
    }
}
