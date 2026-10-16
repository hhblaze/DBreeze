using System.Runtime.CompilerServices;

namespace DBreeze.Storage
{
    internal static class StorageReadExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static byte[] ReadCommittedPayload(this IStorage storage, long record, long epoch,
            bool point, ref ValueRecordHeader header, out byte[] refreshedKey)
        {
            if (storage is StorageLayer layer)
                return layer.ReadCommittedPayload(record, epoch, point, ref header, out refreshedKey);
            if (storage is DBreeze.LianaTrie.NestedTableStorage nested)
                return nested.ReadCommittedPayload(record, epoch, ref header, out refreshedKey);
            return CommittedValueRead.PayloadFallback(storage, record, epoch, ref header, out refreshedKey);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static byte[] ExpandCommittedRecord(this IStorage storage, long record, byte[] data,
            long epoch, bool point, ref ValueRecordHeader header)
        {
            if (storage is StorageLayer layer)
                return layer.ExpandCommittedRecord(record, data, epoch, point, ref header);
            if (storage is DBreeze.LianaTrie.NestedTableStorage nested)
                return nested.ExpandCommittedRecord(record, data, epoch, ref header);
            return CommittedValueRead.ExpandFallback(storage, record, data, epoch, ref header);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static byte[] ReadCommittedValue(this IStorage storage, DBreeze.LianaTrie.LTrieRow row,
            bool point, bool full, uint start, uint length)
        {
            if (storage is StorageLayer layer)
                return layer.ReadCommittedValue(row, point, full, start, length);
            if (storage is DBreeze.LianaTrie.NestedTableStorage nested)
                return nested.ReadCommittedValue(row, full, start, length);
            return CommittedValueRead.Fallback(storage, row, full, start, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static byte[] ReadRecordProbe(this IStorage storage, bool useCache,
            long offset, int count, out long epoch)
        {
            if (useCache)
            {
                if (storage is StorageLayer layer)
                    return layer.ReadCommittedProbe(offset, count, out epoch);
                if (storage is DBreeze.LianaTrie.NestedTableStorage nested)
                    return nested.ReadCommittedProbe(offset, count, out epoch);
            }
            epoch = 0;
            return storage.Table_Read(useCache, offset, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static CommittedRecordRead BeginRecordRead(this IStorage storage,
            bool useCache, bool point = false, int countHint = 111)
        {
            if (useCache)
            {
                if (storage is StorageLayer layer)
                    return layer.BeginRecordRead(point, countHint);
                if (storage is DBreeze.LianaTrie.NestedTableStorage nested)
                    return nested.BeginRecordRead(point, countHint);
            }
            return new CommittedRecordRead(storage, useCache);
        }

        internal static byte[] Table_ReadRecordContinuation(this IStorage storage, bool useCache,
            long recordOffset, long offset, int quantity)
        {
            if (useCache && storage is StorageLayer layer)
                return layer.Table_ReadRecordContinuation(true, recordOffset, offset, quantity);
            return storage.Table_Read(useCache, offset, quantity);
        }
    }
}
