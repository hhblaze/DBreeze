namespace DBreeze.Storage
{
    internal static class StorageReadExtensions
    {
        internal static byte[] Table_ReadRecordContinuation(this IStorage storage, bool useCache,
            long recordOffset, long offset, int quantity)
        {
            if (useCache && storage is StorageLayer layer)
                return layer.Table_ReadRecordContinuation(true, recordOffset, offset, quantity);
            return storage.Table_Read(useCache, offset, quantity);
        }
    }
}
