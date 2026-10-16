using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DBreeze.Storage
{
    // A record can require several reads (header, long key, payload). Borrow the
    // backend's existing read admission once, so all bytes and the extent's epoch
    // belong to the same committed view. No extra lock or heap lease is created.
    internal readonly struct CommittedRecordRead : IDisposable
    {
        private readonly FSR _disk;
        private readonly MSR _memory;
        private readonly IStorage _fallback;
        private readonly bool _useCache;
        private readonly bool _shared;
        private readonly bool _mapped;
        private readonly bool _ownsAdmission;
        internal readonly long Epoch;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal CommittedRecordRead(FSR disk, bool point, int countHint)
        {
            this = default;
            _disk = disk;
            disk.EnterRecordRead(point, countHint, out _shared, out _mapped);
            _ownsAdmission = true;
            Epoch = disk.CommittedReadEpoch;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal CommittedRecordRead(MSR memory)
        {
            this = default;
            _memory = memory;
            memory.EnterRecordRead();
            _ownsAdmission = true;
            Epoch = memory.CommittedReadEpoch;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal CommittedRecordRead(IStorage storage, bool useCache)
        {
            this = default;
            _fallback = storage;
            _useCache = useCache;
            if (useCache && storage is RISR remote)
            {
                Monitor.Enter(remote.RecordReadGate);
                _ownsAdmission = true;
                Epoch = remote.CommittedReadEpoch;
            }
        }

        // Cold-path adapters borrow an admission already held by the backend.
        internal CommittedRecordRead(FSR disk, bool shared, bool mapped, long epoch)
        {
            this = default;
            _disk = disk; _shared = shared; _mapped = mapped; Epoch = epoch;
        }

        internal CommittedRecordRead(MSR memory, long epoch)
        {
            this = default;
            _memory = memory; Epoch = epoch;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal byte[] Read(long offset, int count, long recordOffset = -1)
        {
            if (_disk != null)
                return _disk.ReadRecord(offset, count, _shared, _mapped, recordOffset);
            if (_memory != null)
                return _memory.ReadRecord(offset, count);
            return _fallback.Table_Read(_useCache, offset, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryReadInto(long offset, scoped Span<byte> destination)
        {
            if (_disk != null)
                return _disk.TryReadRecordInto(offset, destination, _shared, _mapped);
            return _memory != null && _memory.TryReadRecordInto(offset, destination);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (!_ownsAdmission)
                return;
            if (_disk != null)
                _disk.ExitRecordRead(_shared);
            else if (_memory != null)
                _memory.ExitRecordRead();
            else if (_useCache && _fallback is RISR remote)
                Monitor.Exit(remote.RecordReadGate);
        }
    }
}
