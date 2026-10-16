using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using DBreeze.LianaTrie;
using DBreeze.Utils;

namespace DBreeze.Storage
{
    internal readonly struct ValueRecordHeader
    {
        internal readonly int Size;
        internal readonly int KeySize;
        internal readonly int ValueSize;
        internal readonly bool IsNull;
        internal int RecordSize => checked(Size + KeySize + ValueSize);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ValueRecordHeader(ReadOnlySpan<byte> data)
        {
            if (data.Length < 7)
                throw new InvalidOperationException("Truncated value record header.");
            Size = data[0] == 0 ? 7 : data[0] == 1 ? 11 :
                throw new InvalidOperationException("Invalid value record protocol.");
            KeySize = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2));
            IsNull = (data[3] & 0x80) != 0;
            uint size = IsNull ? 0 : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(3, 4));
            if (size > Int32.MaxValue)
                throw new InvalidOperationException("Stored value length exceeds the maximum supported array size.");
            ValueSize = (int)size;
        }
    }

    internal static class CommittedValueRead
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool NeedsRefresh(LTrieRow row, long epoch) =>
            epoch == 0 || row.CommittedValueEpoch != epoch || row.ValueStartPointer < 0;

        internal static void Refresh(LTrieRow row, CommittedRecordRead reader)
        {
            long record = checked((long)row.RecordPointer.DynamicLength_To_UInt64_BigEndian());
            Span<byte> probe = stackalloc byte[11];
            ReadOnlySpan<byte> header = probe;
            if (!reader.TryReadInto(record, probe))
                header = reader.Read(record, probe.Length);
            if (header.Length < 7)
                throw new InvalidOperationException("Truncated value record header.");
            int headerSize = header[0] == 0 ? 7 : header[0] == 1 ? 11 :
                throw new InvalidOperationException("Invalid value record protocol.");
            if (header.Length < headerSize)
                throw new InvalidOperationException("Truncated value record header.");
            row.ValueIsNull = (header[3] & 0x80) != 0;
            row.ValueFullLength = row.ValueIsNull ? 0 : BinaryPrimitives.ReadUInt32BigEndian(header.Slice(3, 4));
            if (row.ValueFullLength > Int32.MaxValue)
                throw new InvalidOperationException("Stored value length exceeds the maximum supported array size.");
            row.ValueStartPointer = checked(record + headerSize + BinaryPrimitives.ReadUInt16BigEndian(header.Slice(1, 2)));
            row.CommittedValueEpoch = reader.Epoch;
            DurabilityTestHooks.Hit("read.record-header");
        }

        // Returns false when null/empty/out-of-range completes without a payload.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool Range(LTrieRow row, bool full, uint start, uint length,
            out long offset, out int count, out byte[] completed)
        {
            offset = row.ValueStartPointer;
            count = 0;
            completed = null;
            if (row.ValueIsNull)
                return false;
            uint available = row.ValueFullLength;
            if (available == 0)
            {
                completed = Array.Empty<byte>();
                return false;
            }
            if (full)
            {
                count = checked((int)available);
                return true;
            }
            if (length == 0)
            {
                completed = Array.Empty<byte>();
                return false;
            }
            if (start > available)
                return false;
            length = Math.Min(length, available - start);
            if (length == 0)
            {
                completed = Array.Empty<byte>();
                return false;
            }
            offset = checked(offset + start);
            count = checked((int)length);
            return true;
        }

        internal static byte[] Fallback(IStorage storage, LTrieRow row, bool full, uint start, uint length)
        {
            using var reader = storage.BeginRecordRead(true, row.PreferCommittedPointRead);
            row.EnsureStorageIncarnation(storage.StorageFixTime);
            if (NeedsRefresh(row, reader.Epoch))
                Refresh(row, reader);
            return Range(row, full, start, length, out long offset, out int count, out byte[] completed)
                ? reader.Read(offset, count) : completed;
        }

        internal static byte[] ExpandFallback(IStorage storage, long record, byte[] data,
            long epoch, ref ValueRecordHeader header)
        {
            using var reader = storage.BeginRecordRead(true, countHint: header.RecordSize);
            if (epoch == 0 || reader.Epoch != epoch)
            {
                data = reader.Read(record, 111);
                header = new ValueRecordHeader(data);
            }
            DurabilityTestHooks.Hit("read.record-header");
            return data == null || header.RecordSize > data.Length ? reader.Read(record, header.RecordSize, record) : data;
        }

        // Used only after a publication invalidates an eager probe. Normal
        // short-key reads already own all key bytes and allocate no probe array.
        internal static byte[] RefreshKey(CommittedRecordRead reader, long record, ref ValueRecordHeader header)
        {
            byte[] data = reader.Read(record, 111);
            header = new ValueRecordHeader(data);
            int keyEnd = checked(header.Size + header.KeySize);
            return keyEnd > data.Length ? reader.Read(record, keyEnd) : data;
        }

        internal static byte[] PayloadFallback(IStorage storage, long record, long epoch,
            ref ValueRecordHeader header, out byte[] refreshedKey)
        {
            using var reader = storage.BeginRecordRead(true, countHint: header.ValueSize);
            refreshedKey = epoch == 0 || reader.Epoch != epoch ? RefreshKey(reader, record, ref header) : null;
            DurabilityTestHooks.Hit("read.record-header");
            return header.IsNull ? null : header.ValueSize == 0 ? Array.Empty<byte>() :
                reader.Read(checked(record + header.Size + header.KeySize), header.ValueSize, record);
        }
    }
}
