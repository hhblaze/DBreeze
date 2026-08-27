using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DBreeze.Storage
{
    internal static class CommittedReadDiagnostics
    {
        private const int Stripes = 64;
        private const int Width = 16;
        private static readonly long[] Counters = new long[Stripes * Width];
        private static long _mappedVirtualBytes;
        private static int _enabled;

        private static int Offset(int counter) =>
            (Environment.CurrentManagedThreadId & (Stripes - 1)) * Width + counter;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsEnabled() => Volatile.Read(ref _enabled) != 0;

        internal static void SetEnabled(bool enabled) =>
            Volatile.Write(ref _enabled, enabled ? 1 : 0);

        internal static void TransactionFastHit() { if (IsEnabled()) Counters[Offset(0)]++; }
        internal static void TransactionFastMiss() { if (IsEnabled()) Counters[Offset(1)]++; }
        internal static void ReservationBypass() { if (IsEnabled()) Counters[Offset(2)]++; }
        internal static void SharedRead() { if (IsEnabled()) Counters[Offset(3)]++; }
        internal static void ContendedRead() { if (IsEnabled()) Counters[Offset(4)]++; }
        internal static void WindowHit() { if (IsEnabled()) Counters[Offset(5)]++; }

        internal static void RandomAccessRead(int bytes)
        {
            if (!IsEnabled()) return;
            int offset = Offset(6);
            Counters[offset]++;
            Counters[offset + 1] += bytes;
        }

        internal static void MappedRead(int bytes)
        {
            if (!IsEnabled()) return;
            int offset = Offset(8);
            Counters[offset]++;
            Counters[offset + 1] += bytes;
        }

        internal static void MappedFallback() { if (IsEnabled()) Counters[Offset(10)]++; }
        internal static void MappedCreateFailure() { if (IsEnabled()) Counters[Offset(11)]++; }
        internal static void ChangeMappedVirtualBytes(long bytes) =>
            Interlocked.Add(ref _mappedVirtualBytes, bytes);

        internal static long[] GetDiagnostics()
        {
            var result = new long[13];
            for (int stripe = 0; stripe < Stripes; stripe++)
            {
                int offset = stripe * Width;
                for (int counter = 0; counter < 12; counter++)
                    result[counter] += Volatile.Read(ref Counters[offset + counter]);
            }
            result[12] = Interlocked.Read(ref _mappedVirtualBytes);
            return result;
        }
    }

    internal static class CommittedMappedReadBudgetRegistry
    {
        internal const long TableLimitBytes = 64L * 1024 * 1024 * 1024;
        internal const long ConfigurationLimitBytes = 256L * 1024 * 1024 * 1024;

        private static readonly ConditionalWeakTable<DBreezeConfiguration, Budget> Budgets =
            new ConditionalWeakTable<DBreezeConfiguration, Budget>();

        internal static Budget Get(DBreezeConfiguration configuration) =>
            Budgets.GetValue(configuration, static _ => new Budget());

        internal sealed class Budget
        {
            private readonly object _sync = new object();
            private long _reservedBytes;

            internal bool TryReserve(long bytes)
            {
                if (bytes <= 0 || bytes > TableLimitBytes)
                    return false;

                lock (_sync)
                {
                    if (_reservedBytes > ConfigurationLimitBytes - bytes)
                        return false;
                    _reservedBytes += bytes;
                    CommittedReadDiagnostics.ChangeMappedVirtualBytes(bytes);
                    return true;
                }
            }

            internal void Release(long bytes)
            {
                if (bytes == 0)
                    return;
                lock (_sync)
                {
                    _reservedBytes -= bytes;
                    CommittedReadDiagnostics.ChangeMappedVirtualBytes(-bytes);
                }
            }
        }
    }
}
