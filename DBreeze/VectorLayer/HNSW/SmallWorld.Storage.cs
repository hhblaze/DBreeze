/*
  Copyright https://github.com/wlou/HNSW.Net MIT License
  Copyright (C) 2012 dbreeze.tiesky.com / Oleksiy Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
*/
#if NET6FUNC || NET472
using DBreeze.DataTypes;
using DBreeze.Tries;
using DBreeze.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace DBreeze.HNSW
{
    internal partial class SmallWorld<TItem, TDistance>
    {
        internal interface IStorage<TItem, TDistance>
        {            
            TItem GetItem(long externalId, Func<long, TItem> f);
            void AddItem(long externalId, int bucketId, int id, TItem item);
            string TableName { get; set; }
            DBreeze.Transactions.Transaction tran { get; set; }
            bool FlushNodes(int bucketId, Dictionary<int, Node> nodes);
            void FlushBucket(Bucket bucket);
            NodeDB GetDBNode(int bucketId, int nodeId);
            List<BucketDB> GetBuckets();
            void ClearItemsCache();
            //TItem NormalizeVector(TItem vector);
            void FlushAddItems(bool externalTableForVectorsAvailable);

            Func<TItem, TItem, TDistance> GetDistanceFunction();
            Func<TItem, TItem> GetNormalizeFunction();

            /// <summary>
            /// Sets TurboQuant configuration for quantized storage.
            /// </summary>
            void SetTurboQuantParams(TurboQuantParams tqp);
        }

        internal class DBStorage
        {
            /*
             DBreeze scheme
              2- Key: new byte[] {2, (int)bucketId, (int)nodeId}
                 Value: NodeDB (maxLevel, externalId, connections on all levels)
              3- Key: new byte[] {3, (int)bucketId}
                 Value: BucketDB (entryPoint info, MaxLevel)
              4- Key: new byte[] {4, (long)externalId }
                 Value: (int)bucketId, (int)nodeId
                     //var bucketId = Value.Substring(0, 4).To_Int32_BigEndian();
                     //var nodeId = Value.Substring(4, 4).To_Int32_BigEndian();
              5- Key: new byte[] {5, (long)externalId}
                 Value: GZIPed TItem (vector self)
              6- Key: new byte[] {6, (long)externalId}
                 Value: MSE quantized data: [dimension:2 bytes][bitWidth:1 byte][norm:4/8 bytes][indices:dim bytes]
              7- Key: new byte[] {7, (long)externalId}
                 Value: InnerProduct quantized data: [dim:2][bitWidth:1][norm:4/8][residualNorm:4/8][mseIndices:dim bytes][qjlSigns:dim bytes]
             */


            public DBreeze.Transactions.Transaction tran { get; set; }

            protected ReaderWriterLockSlim _sync = new ReaderWriterLockSlim();
            
            /// <summary>
            /// 
            /// </summary>
            public string TableName { get; set; } = string.Empty;

            /// <summary>
            /// true, if something was saved
            /// </summary>
            /// <param name="bucketId"></param>
            /// <param name="nodes"></param>
            /// <returns></returns>
            public bool FlushNodes(int bucketId, Dictionary<int, Node> nodes)
            {
                
                bool changed = false;
                foreach(var n in nodes.Where(r=>r.Value.Changed).OrderBy(r=>r.Key))
                {
                    var dbNode = new NodeDB
                    {
                        Connections = n.Value.Connections.CloneByExpressionTree(),
                        ExternalId = n.Value.ExternalId,
                        Id = n.Value.Id,
                        MaxLevel = n.Value.MaxLevel,
                        Deleted = n.Value.Deleted,
                        ProtectedConnections = n.Value.ProtectedConnections ?? new Dictionary<int, HashSet<int>>()
                    };
                    byte[] btNode = dbNode.BiserEncoder().Encode();

                    tran.Insert<byte[], byte[]>(this.TableName, 2.ToIndex(bucketId, n.Key), btNode);

                    changed = true; 
                }

                return changed;
            }
             
            public NodeDB GetDBNode(int bucketId, int nodeId)
            {
                var row = tran.Select<byte[], byte[]>(this.TableName, 2.ToIndex(bucketId, nodeId));
                return NodeDB.BiserDecode(row.Value);
            }

            public void FlushBucket(Bucket bucket)
            {
                var dbBucket = new BucketDB()
                {
                    BucketId = bucket.BucketId,
                    EntryPointId = bucket.Graph.entryPoint.Id,
                    Count = bucket.Graph.Count,
                    DeletedCount = bucket.DeletedCount
                };

                byte[] btBucket = dbBucket.BiserEncoder().Encode();

                tran.Insert<byte[], byte[]>(this.TableName, 3.ToIndex(bucket.BucketId), btBucket);
            }

            public List<BucketDB> GetBuckets()
            {
                List<BucketDB> ret = new List<BucketDB>();
                foreach (var row in tran.SelectForwardFromTo<byte[], byte[]> (this.TableName, 3.ToIndex((int)0),true,3.ToIndex(int.MaxValue),false))
                {
                    ret.Add(BucketDB.BiserDecode(row.Value));
                }
                return ret;
            }
        }

        /// <summary>
        /// TurboQuant configuration, set by Composer.
        /// When null, standard full-precision storage is used.
        /// </summary>
        internal TurboQuantParams _tqp = null;

        internal class SmallWorldStorageF : DBStorage, IStorage<float[], float>
        {
            /// <summary>
            /// TurboQuant configuration for this storage instance.
            /// Set by Composer during initialization.
            /// </summary>
            internal TurboQuantParams _tqp = null;

            public void SetTurboQuantParams(TurboQuantParams tqp)
            {
                _tqp = tqp;
            }

            public Func<float[], float[], float> GetDistanceFunction()
            {
                if (CosineDistance.IsHardwareAccelerated())
                    return CosineDistance.DistanceForUnits;
                else
                    return CosineDistance.DistanceForUnitsSimple;

            }

            public Func<float[], float[]> GetNormalizeFunction()
            {
                if (CosineDistance.IsHardwareAccelerated())
                    return CosineDistance.NormalizeVector;
                else
                    return CosineDistance.NormalizeSimple;
            }


            ConcurrentDictionary<long, float[]> itemsCache = new ConcurrentDictionary<long, float[]>();
            List<(long externalId, int bucketId, int id)> addedItems = new List<(long externalId, int bucketId, int id)>();

            float[] SmallWorld<TItem, TDistance>.IStorage<float[], float>.GetItem(long externalId, Func<long, float[]> f=null)
            {
                if(f != null)
                    return f(externalId); //TurboQuant compression works only for locally stored vectors

                if (!itemsCache.TryGetValue(externalId, out var item))
                {   
                    if (f != null)
                    {
                        item = f(externalId);
                        if(item == null)
                            return null;
                        itemsCache[externalId] = item;
                    }
                    else
                    {
                        // Try quantized data first (key prefix 6 or 7)
                        if (_tqp != null && _tqp.IsEnabled)
                        {
                            item = TryDequantizeF(externalId);
                            if (item != null)
                            {
                                itemsCache[externalId] = item;
                                return item;
                            }
                        }

                        // Fall back to full-precision GZIPed storage (key prefix 5)
                        var row = tran.Select<byte[], byte[]>(this.TableName, 5.ToIndex(externalId));
                        if (!row.Exists)
                            return null;
                        var bt = DecompressF(row.Value);
                        itemsCache[externalId] = bt;
                        return bt;
                    }
                }

                return item;
            }

            /// <summary>
            /// Tries to dequantize a float[] vector from quantized storage.
            /// Tries key prefix 6 (MSE) or 7 (InnerProduct) based on TurboQuant mode.
            /// </summary>
            private float[] TryDequantizeF(long externalId)
            {
                if (_tqp.Mode == eTurboQuantMode.MSE)
                {
                    var row = tran.Select<byte[], byte[]>(this.TableName, 6.ToIndex(externalId));
                    if (!row.Exists)
                        return null;
                    return DecompressQuantizedMseBitPackF(row.Value, _tqp);
                    //return DecompressQuantizedMseF(row.Value, _tqp);
                }
                else if (_tqp.Mode == eTurboQuantMode.InnerProduct)
                {
                    var row = tran.Select<byte[], byte[]>(this.TableName, 7.ToIndex(externalId));
                    if (!row.Exists)
                        return null;
                    return DecompressQuantizedProdBitPackF(row.Value, _tqp);
                    //return DecompressQuantizedProdF(row.Value, _tqp);
                }
                return null;
            }

            public void ClearItemsCache()
            {
                itemsCache.Clear();
            }

            public void FlushAddItems(bool externalTableForVectorsAvailable)
            {
                /*
                   DBreeze scheme         
                   4- Key: new byte[] {4, (long)externalId }
                      Value: (int)bucketId, (int)nodeId
                   5- Key: new byte[] {5, (long)externalId}
                      Value: GZIPed compressed TItem (vector self)
                   6- Key: new byte[] {6, (long)externalId}
                      Value: MSE quantized data (see CompressQuantizedMseF)
                   7- Key: new byte[] {7, (long)externalId}
                      Value: InnerProduct quantized data (see CompressQuantizedProdF)
                */

                foreach(var el in addedItems)
                {
                    var item = itemsCache[el.externalId];

                   
                    if (!externalTableForVectorsAvailable)
                    {
                        if (_tqp != null && _tqp.IsEnabled) //TurboQuant compression works only for locally stored vectors
                        {
                            // Store quantized representation instead of full-precision GZIP
                            if (_tqp.Mode == eTurboQuantMode.MSE)
                            {
                                //byte[] qData = CompressQuantizedMseF(item, _tqp);
                                byte[] qData = CompressQuantizedMseBitPackF(item, _tqp);
                                tran.Insert<byte[], byte[]>(this.TableName, 6.ToIndex(el.externalId), qData);

                                //// Remove full-precision storage if it existed
                                //var oldRow = tran.Select<byte[], byte[]>(this.TableName, 5.ToIndex(el.externalId));
                                //if (oldRow.Exists)
                                //    tran.RemoveKey(this.TableName, 5.ToIndex(el.externalId));
                            }
                            else if (_tqp.Mode == eTurboQuantMode.InnerProduct)
                            {
                                //byte[] qData = CompressQuantizedProdF(item, _tqp);
                                byte[] qData = CompressQuantizedProdBitPackF(item, _tqp);
                                tran.Insert<byte[], byte[]>(this.TableName, 7.ToIndex(el.externalId), qData);

                                //// Remove full-precision storage if it existed
                                //var oldRow = tran.Select<byte[], byte[]>(this.TableName, 5.ToIndex(el.externalId));
                                //if (oldRow.Exists)
                                //    tran.RemoveKey(this.TableName, 5.ToIndex(el.externalId));
                            }
                        }
                        else
                        {                           
                            tran.Insert<byte[], byte[]>(this.TableName, 5.ToIndex(el.externalId), CompressF(item));
                        }
                        
                    }                

                    tran.Insert<byte[], byte[]>(this.TableName, 4.ToIndex(el.externalId), el.bucketId.To_4_bytes_array_BigEndian().Concat(el.id.To_4_bytes_array_BigEndian()));
                }
                addedItems.Clear();
            }


            /// <summary>
            /// item should be already normalized
            /// </summary>
            /// <param name="externalId"></param>
            /// <param name="bucketId"></param>
            /// <param name="id"></param>
            /// <param name="item"></param>           
            public void AddItem(long externalId, int bucketId, int id, float[] item)
            {
                itemsCache[externalId] = item;

                this._sync.EnterWriteLock();                
                addedItems.Add((externalId, bucketId, id));
                this._sync.ExitWriteLock();               
            }
        }

        #region Quantized Compression Utilities for float[]

        /// <summary>
        /// Packs an array of byte values (each bounded by 'bits') into a dense bit-stream.
        /// </summary>
        private static byte[] PackBits(byte[] elements, int dim, int bits)
        {
            if (bits == 8) return elements;
            int packedLen = (dim * bits + 7) / 8;
            byte[] packed = new byte[packedLen];
            int bitPos = 0;

            for (int i = 0; i < dim; i++)
            {
                int val = elements[i] & ((1 << bits) - 1);
                int byteIdx = bitPos >> 3;
                int bitOffset = bitPos & 7;

                packed[byteIdx] |= (byte)(val << bitOffset);
                if (bitOffset + bits > 8)
                {
                    packed[byteIdx + 1] |= (byte)(val >> (8 - bitOffset));
                }
                bitPos += bits;
            }
            return packed;
        }

        /// <summary>
        /// Unpacks a dense bit-stream back into an array of bytes.
        /// </summary>
        private static byte[] UnpackBits(byte[] data, int offset, int dim, int bits)
        {
            byte[] unpacked = new byte[dim];
            if (bits == 8)
            {
                Buffer.BlockCopy(data, offset, unpacked, 0, dim);
                return unpacked;
            }

            int bitPos = 0;
            int mask = (1 << bits) - 1;

            for (int i = 0; i < dim; i++)
            {
                int byteIdx = offset + (bitPos >> 3);
                int bitOffset = bitPos & 7;

                int val = data[byteIdx] >> bitOffset;
                if (bitOffset + bits > 8)
                {
                    val |= (data[byteIdx + 1] << (8 - bitOffset));
                }

                unpacked[i] = (byte)(val & mask);
                bitPos += bits;
            }
            return unpacked;
        }

        /// <summary>
        /// Packs an array of -1/+1 signs into exactly 1 bit per element.
        /// </summary>
        private static byte[] PackQjlSigns(sbyte[] signs, int dim)
        {
            int packedLen = (dim + 7) / 8;
            byte[] packed = new byte[packedLen];

            for (int i = 0; i < dim; i++)
            {
                if (signs[i] > 0)
                {
                    packed[i >> 3] |= (byte)(1 << (i & 7));
                }
            }
            return packed;
        }

        /// <summary>
        /// Unpacks 1-bit flags back into an array of -1/+1 sbytes.
        /// </summary>
        private static sbyte[] UnpackQjlSigns(byte[] data, int offset, int dim)
        {
            sbyte[] signs = new sbyte[dim];

            for (int i = 0; i < dim; i++)
            {
                int byteIdx = offset + (i >> 3);
                bool isPositive = (data[byteIdx] & (1 << (i & 7))) != 0;
                signs[i] = isPositive ? (sbyte)1 : (sbyte)-1;
            }
            return signs;
        }

        /// <summary>
        /// Compresses a float[] vector into MSE quantized and bit-packed byte format.
        /// Format: [dim:2 bytes LE][bitWidth:1 byte][norm:4 bytes float LE][packedIndices]
        /// </summary>
        internal static byte[] CompressQuantizedMseBitPackF(float[] vector, TurboQuantParams tqp)
        {
            int dim = vector.Length;
            byte[] indices = TurboQuantMath.QuantizeMseF(vector, dim, tqp.BitWidth, tqp.RandomSeed, out float norm);

            byte[] packedIndices = PackBits(indices, dim, tqp.BitWidth);

            byte[] data = new byte[2 + 1 + 4 + packedIndices.Length];
            int offset = 0;

            // Dimension (ushort, little-endian)
            data[offset++] = (byte)(dim & 0xFF);
            data[offset++] = (byte)((dim >> 8) & 0xFF);

            // Bit-width
            data[offset++] = (byte)tqp.BitWidth;

            // Norm (float, strictly little-endian)
            byte[] normBytes = BitConverter.GetBytes(norm);
            if (BitConverter.IsLittleEndian)
            {
                data[offset++] = normBytes[0];
                data[offset++] = normBytes[1];
                data[offset++] = normBytes[2];
                data[offset++] = normBytes[3];
            }
            else
            {
                data[offset++] = normBytes[3];
                data[offset++] = normBytes[2];
                data[offset++] = normBytes[1];
                data[offset++] = normBytes[0];
            }

            // Packed Indices
            Buffer.BlockCopy(packedIndices, 0, data, offset, packedIndices.Length);

            return data;
        }

        /// <summary>
        /// Decompresses bit-packed MSE quantized data back to float[].
        /// </summary>
        internal static float[] DecompressQuantizedMseBitPackF(byte[] data, TurboQuantParams tqp)
        {
            int offset = 0;
            // Dimension (little-endian)
            int dim = data[offset] | (data[offset + 1] << 8);
            offset += 2;

            int bitWidth = data[offset++];

            // Norm (float, strictly little-endian)
            byte[] normBytes = new byte[4];
            if (BitConverter.IsLittleEndian)
            {
                normBytes[0] = data[offset];
                normBytes[1] = data[offset + 1];
                normBytes[2] = data[offset + 2];
                normBytes[3] = data[offset + 3];
            }
            else
            {
                normBytes[0] = data[offset + 3];
                normBytes[1] = data[offset + 2];
                normBytes[2] = data[offset + 1];
                normBytes[3] = data[offset];
            }
            float norm = BitConverter.ToSingle(normBytes, 0);
            offset += 4;

            byte[] indices = UnpackBits(data, offset, dim, bitWidth);

            return TurboQuantMath.DequantizeMseF(indices, dim, bitWidth, tqp.RandomSeed, norm);
        }

        /// <summary>
        /// Compresses a float[] vector into InnerProduct bit-packed byte format.
        /// Format: [dim:2 LE][bitWidth:1][norm:4 LE][residualNorm:4 LE][packedMseIndices][packedQjlSigns]
        /// </summary>
        internal static byte[] CompressQuantizedProdBitPackF(float[] vector, TurboQuantParams tqp)
        {
            int dim = vector.Length;
            TurboQuantMath.QuantizeProdSafeF(vector, dim, tqp.BitWidth, tqp.RandomSeed,
                out byte[] mseIndices, out sbyte[] qjlSigns, out float residualNorm, out float norm);

            int mseBits = Math.Max(1, tqp.BitWidth - 1);

            byte[] packedMse = PackBits(mseIndices, dim, mseBits);
            byte[] packedQjl = PackQjlSigns(qjlSigns, dim);

            byte[] data = new byte[2 + 1 + 4 + 4 + packedMse.Length + packedQjl.Length];
            int offset = 0;

            // Dimension (little-endian)
            data[offset++] = (byte)(dim & 0xFF);
            data[offset++] = (byte)((dim >> 8) & 0xFF);

            // Bit-width
            data[offset++] = (byte)tqp.BitWidth;

            // Norm (float, strictly little-endian)
            byte[] normBytes = BitConverter.GetBytes(norm);
            if (BitConverter.IsLittleEndian)
            {
                data[offset++] = normBytes[0];
                data[offset++] = normBytes[1];
                data[offset++] = normBytes[2];
                data[offset++] = normBytes[3];
            }
            else
            {
                data[offset++] = normBytes[3];
                data[offset++] = normBytes[2];
                data[offset++] = normBytes[1];
                data[offset++] = normBytes[0];
            }

            // Residual norm (float, strictly little-endian)
            byte[] resNormBytes = BitConverter.GetBytes(residualNorm);
            if (BitConverter.IsLittleEndian)
            {
                data[offset++] = resNormBytes[0];
                data[offset++] = resNormBytes[1];
                data[offset++] = resNormBytes[2];
                data[offset++] = resNormBytes[3];
            }
            else
            {
                data[offset++] = resNormBytes[3];
                data[offset++] = resNormBytes[2];
                data[offset++] = resNormBytes[1];
                data[offset++] = resNormBytes[0];
            }

            // Packed MSE indices
            Buffer.BlockCopy(packedMse, 0, data, offset, packedMse.Length);
            offset += packedMse.Length;

            // Packed QJL signs
            Buffer.BlockCopy(packedQjl, 0, data, offset, packedQjl.Length);

            return data;
        }

        /// <summary>
        /// Decompresses InnerProduct bit-packed data back to float[].
        /// </summary>
        internal static float[] DecompressQuantizedProdBitPackF(byte[] data, TurboQuantParams tqp)
        {
            int offset = 0;

            // Dimension (little-endian)
            int dim = data[offset] | (data[offset + 1] << 8);
            offset += 2;

            int bitWidth = data[offset++];

            // Norm (float, strictly little-endian)
            byte[] normBytes = new byte[4];
            if (BitConverter.IsLittleEndian)
            {
                normBytes[0] = data[offset];
                normBytes[1] = data[offset + 1];
                normBytes[2] = data[offset + 2];
                normBytes[3] = data[offset + 3];
            }
            else
            {
                normBytes[0] = data[offset + 3];
                normBytes[1] = data[offset + 2];
                normBytes[2] = data[offset + 1];
                normBytes[3] = data[offset];
            }
            float norm = BitConverter.ToSingle(normBytes, 0);
            offset += 4;

            // Residual norm (float, strictly little-endian)
            byte[] resNormBytes = new byte[4];
            if (BitConverter.IsLittleEndian)
            {
                resNormBytes[0] = data[offset];
                resNormBytes[1] = data[offset + 1];
                resNormBytes[2] = data[offset + 2];
                resNormBytes[3] = data[offset + 3];
            }
            else
            {
                resNormBytes[0] = data[offset + 3];
                resNormBytes[1] = data[offset + 2];
                resNormBytes[2] = data[offset + 1];
                resNormBytes[3] = data[offset];
            }
            float residualNorm = BitConverter.ToSingle(resNormBytes, 0);
            offset += 4;

            int mseBits = Math.Max(1, bitWidth - 1);
            int packedMseLen = (dim * mseBits + 7) / 8;

            byte[] mseIndices = UnpackBits(data, offset, dim, mseBits);
            offset += packedMseLen;

            sbyte[] qjlSigns = UnpackQjlSigns(data, offset, dim);

            return TurboQuantMath.DequantizeProdF(mseIndices, qjlSigns, dim, bitWidth, tqp.RandomSeed, norm, residualNorm);
        }

        #endregion

        //#region Quantized Compression Utilities for float[]

        ///// <summary>
        ///// Compresses a float[] vector into MSE quantized byte format.
        ///// Format: [dim:2 bytes LE][bitWidth:1 byte][norm:4 bytes float LE][indices:dim bytes]
        ///// </summary>
        //internal static byte[] CompressQuantizedMseF(float[] vector, TurboQuantParams tqp)
        //{
        //    int dim = vector.Length;
        //    byte[] indices = TurboQuantMath.QuantizeMseF(vector, dim, tqp.BitWidth, tqp.RandomSeed, out float norm);

        //    // Format: dim(2) + bitWidth(1) + norm(4) + indices(dim)
        //    byte[] data = new byte[2 + 1 + 4 + dim];
        //    int offset = 0;

        //    // Dimension (ushort, little-endian)
        //    data[offset++] = (byte)(dim & 0xFF);
        //    data[offset++] = (byte)((dim >> 8) & 0xFF);

        //    // Bit-width
        //    data[offset++] = (byte)tqp.BitWidth;

        //    // Norm (float, strictly little-endian)
        //    byte[] normBytes = BitConverter.GetBytes(norm);
        //    if (BitConverter.IsLittleEndian)
        //    {
        //        data[offset++] = normBytes[0];
        //        data[offset++] = normBytes[1];
        //        data[offset++] = normBytes[2];
        //        data[offset++] = normBytes[3];
        //    }
        //    else
        //    {
        //        data[offset++] = normBytes[3];
        //        data[offset++] = normBytes[2];
        //        data[offset++] = normBytes[1];
        //        data[offset++] = normBytes[0];
        //    }

        //    // Indices (dim bytes)
        //    Buffer.BlockCopy(indices, 0, data, offset, dim);

        //    return data;
        //}

        ///// <summary>
        ///// Decompresses MSE quantized data back to float[].
        ///// </summary>
        //internal static float[] DecompressQuantizedMseF(byte[] data, TurboQuantParams tqp)
        //{
        //    int offset = 0;
        //    // Dimension (little-endian)
        //    int dim = data[offset] | (data[offset + 1] << 8);
        //    offset += 2;

        //    int bitWidth = data[offset++];

        //    // Norm (float, strictly little-endian)
        //    byte[] normBytes = new byte[4];
        //    if (BitConverter.IsLittleEndian)
        //    {
        //        normBytes[0] = data[offset];
        //        normBytes[1] = data[offset + 1];
        //        normBytes[2] = data[offset + 2];
        //        normBytes[3] = data[offset + 3];
        //    }
        //    else
        //    {
        //        normBytes[0] = data[offset + 3];
        //        normBytes[1] = data[offset + 2];
        //        normBytes[2] = data[offset + 1];
        //        normBytes[3] = data[offset];
        //    }
        //    float norm = BitConverter.ToSingle(normBytes, 0);
        //    offset += 4;

        //    byte[] indices = new byte[dim];
        //    Buffer.BlockCopy(data, offset, indices, 0, dim);

        //    return TurboQuantMath.DequantizeMseF(indices, dim, bitWidth, tqp.RandomSeed, norm);
        //}

        ///// <summary>
        ///// Compresses a float[] vector into InnerProduct quantized byte format.
        ///// Format: [dim:2 LE][bitWidth:1][norm:4 LE][residualNorm:4 LE][mseIndices:dim bytes][qjlSigns:dim bytes]
        ///// </summary>
        //internal static byte[] CompressQuantizedProdF(float[] vector, TurboQuantParams tqp)
        //{
        //    int dim = vector.Length;
        //    // Make a copy since QuantizeProdSafeF doesn't modify the original
        //    TurboQuantMath.QuantizeProdSafeF(vector, dim, tqp.BitWidth, tqp.RandomSeed,
        //        out byte[] mseIndices, out sbyte[] qjlSigns, out float residualNorm, out float norm);

        //    // Format: dim(2) + bitWidth(1) + norm(4) + residualNorm(4) + mseIndices(dim) + qjlSigns(dim)
        //    byte[] data = new byte[2 + 1 + 4 + 4 + dim + dim];
        //    int offset = 0;

        //    // Dimension (little-endian)
        //    data[offset++] = (byte)(dim & 0xFF);
        //    data[offset++] = (byte)((dim >> 8) & 0xFF);

        //    // Bit-width
        //    data[offset++] = (byte)tqp.BitWidth;

        //    // Norm (float, strictly little-endian)
        //    byte[] normBytes = BitConverter.GetBytes(norm);
        //    if (BitConverter.IsLittleEndian)
        //    {
        //        data[offset++] = normBytes[0];
        //        data[offset++] = normBytes[1];
        //        data[offset++] = normBytes[2];
        //        data[offset++] = normBytes[3];
        //    }
        //    else
        //    {
        //        data[offset++] = normBytes[3];
        //        data[offset++] = normBytes[2];
        //        data[offset++] = normBytes[1];
        //        data[offset++] = normBytes[0];
        //    }

        //    // Residual norm (float, strictly little-endian)
        //    byte[] resNormBytes = BitConverter.GetBytes(residualNorm);
        //    if (BitConverter.IsLittleEndian)
        //    {
        //        data[offset++] = resNormBytes[0];
        //        data[offset++] = resNormBytes[1];
        //        data[offset++] = resNormBytes[2];
        //        data[offset++] = resNormBytes[3];
        //    }
        //    else
        //    {
        //        data[offset++] = resNormBytes[3];
        //        data[offset++] = resNormBytes[2];
        //        data[offset++] = resNormBytes[1];
        //        data[offset++] = resNormBytes[0];
        //    }

        //    // MSE indices
        //    Buffer.BlockCopy(mseIndices, 0, data, offset, dim);
        //    offset += dim;

        //    // QJL signs (convert sbyte to byte, bit pattern strictly preserved)
        //    for (int i = 0; i < dim; i++)
        //        data[offset + i] = (byte)qjlSigns[i]; // -1 → 0xFF, +1 → 0x01

        //    return data;
        //}

        ///// <summary>
        ///// Decompresses InnerProduct quantized data back to float[].
        ///// </summary>
        //internal static float[] DecompressQuantizedProdF(byte[] data, TurboQuantParams tqp)
        //{
        //    int offset = 0;
        //    // Dimension (little-endian)
        //    int dim = data[offset] | (data[offset + 1] << 8);
        //    offset += 2;

        //    int bitWidth = data[offset++];

        //    // Norm (float, strictly little-endian)
        //    byte[] normBytes = new byte[4];
        //    if (BitConverter.IsLittleEndian)
        //    {
        //        normBytes[0] = data[offset];
        //        normBytes[1] = data[offset + 1];
        //        normBytes[2] = data[offset + 2];
        //        normBytes[3] = data[offset + 3];
        //    }
        //    else
        //    {
        //        normBytes[0] = data[offset + 3];
        //        normBytes[1] = data[offset + 2];
        //        normBytes[2] = data[offset + 1];
        //        normBytes[3] = data[offset];
        //    }
        //    float norm = BitConverter.ToSingle(normBytes, 0);
        //    offset += 4;

        //    // Residual norm (float, strictly little-endian)
        //    byte[] resNormBytes = new byte[4];
        //    if (BitConverter.IsLittleEndian)
        //    {
        //        resNormBytes[0] = data[offset];
        //        resNormBytes[1] = data[offset + 1];
        //        resNormBytes[2] = data[offset + 2];
        //        resNormBytes[3] = data[offset + 3];
        //    }
        //    else
        //    {
        //        resNormBytes[0] = data[offset + 3];
        //        resNormBytes[1] = data[offset + 2];
        //        resNormBytes[2] = data[offset + 1];
        //        resNormBytes[3] = data[offset];
        //    }
        //    float residualNorm = BitConverter.ToSingle(resNormBytes, 0);
        //    offset += 4;

        //    byte[] mseIndices = new byte[dim];
        //    Buffer.BlockCopy(data, offset, mseIndices, 0, dim);
        //    offset += dim;

        //    sbyte[] qjlSigns = new sbyte[dim];
        //    for (int i = 0; i < dim; i++)
        //        qjlSigns[i] = (sbyte)data[offset + i];

        //    return TurboQuantMath.DequantizeProdF(mseIndices, qjlSigns, dim, bitWidth, tqp.RandomSeed, norm, residualNorm);
        //}

        //#endregion



        /// <summary>
        /// 
        /// </summary>
        internal class SmallWorldStorageD : DBStorage, IStorage<double[], double>
        {
            /// <summary>
            /// TurboQuant configuration for this storage instance.
            /// Set by Composer during initialization.
            /// </summary>
            internal TurboQuantParams _tqp = null;

            public void SetTurboQuantParams(TurboQuantParams tqp)
            {
                _tqp = tqp;
            }

            public Func<double[], double[], double> GetDistanceFunction()
            {
                if (CosineDistance.IsHardwareAccelerated())
                    return CosineDistance.DistanceForUnits;
                else
                    return CosineDistance.DistanceForUnitsSimple;
            }

            public Func<double[], double[]> GetNormalizeFunction()
            {
                if (CosineDistance.IsHardwareAccelerated())
                    return CosineDistance.NormalizeVector;
                else
                    return CosineDistance.NormalizeSimple;
            }

            ConcurrentDictionary<long, double[]> itemsCache = new ConcurrentDictionary<long, double[]>();
            List<(long externalId, int bucketId, int id)> addedItems = new List<(long externalId, int bucketId, int id)>();

            double[] SmallWorld<TItem, TDistance>.IStorage<double[], double>.GetItem(long externalId, Func<long, double[]> f)
            {
                if (f != null)
                    return f(externalId);

                if (!itemsCache.TryGetValue(externalId, out var item))
                {
                    if (f != null)
                    {
                        item = f(externalId);
                        itemsCache[externalId] = item;
                    }
                    else
                    {
                        var row = tran.Select<byte[], byte[]>(this.TableName, 5.ToIndex(externalId));
                        var bt = DecompressD(row.Value);
                        itemsCache[externalId] = bt;
                        return bt;
                    }
                }

                return item;
            }

            public void ClearItemsCache()
            {
                itemsCache.Clear();
            }

            public double[] NormalizeVector(double[] vector)
            {
                return CosineDistance.NormalizeVector(vector);
            }

            

            public void FlushAddItems(bool externalTableForVectorsAvailable)
            {
                /*
                   DBreeze scheme         
                   4- Key: new byte[] {4, (long)externalId }
                      Value: (int)bucketId, (int)nodeId
                   5- Key: new byte[] {5, (long)externalId}
                      Value: GZIPed compressed TItem (vector self)
                */
                foreach (var el in addedItems)
                {
                    var item = itemsCache[el.externalId];

                    if (!externalTableForVectorsAvailable)
                    {
                        tran.Insert<byte[], byte[]>(this.TableName, 5.ToIndex(el.externalId), CompressD(item));
                    }
                
                    tran.Insert<byte[], byte[]>(this.TableName, 4.ToIndex(el.externalId), el.bucketId.To_4_bytes_array_BigEndian().Concat(el.id.To_4_bytes_array_BigEndian()));
                }

                addedItems.Clear();
            }


            /// <summary>
            /// item should be already normalized
            /// </summary>
            /// <param name="externalId"></param>
            /// <param name="bucketId"></param>
            /// <param name="id"></param>
            /// <param name="item"></param>           
            public void AddItem(long externalId, int bucketId, int id, double[] item)
            {
                itemsCache[externalId] = item;

                this._sync.EnterWriteLock();
                addedItems.Add((externalId, bucketId, id));
                this._sync.ExitWriteLock();
            }
                     

        }

        internal partial class NodeDB
        {
            public List<List<int>> Connections { get; set; } = new List<List<int>>();
            public int MaxLevel { get; set; } = 0;
            public int Id { get; set; } = 0;
            public long ExternalId { get; set; } = 0;
            public bool Deleted { get; set; } = false;
            public Dictionary<int, HashSet<int>> ProtectedConnections { get; set; } = new Dictionary<int, HashSet<int>>();
        }

        internal partial class NodeDB : Biser.IEncoder
        {
            public Biser.Encoder BiserEncoder(Biser.Encoder existingEncoder = null)
            {
                Biser.Encoder encoder = new Biser.Encoder(existingEncoder);


                encoder.Add(Connections, (r1) => {
                    encoder.Add(r1, (r2) => {
                        encoder.Add(r2);
                    });
                });
                encoder.Add(MaxLevel);
                encoder.Add(Id);
                encoder.Add(ExternalId);
                encoder.Add(Deleted);

                // Encode ProtectedConnections
                encoder.Add(ProtectedConnections, (r3) => {
                    encoder.Add(r3.Key);
                    encoder.Add(r3.Value, (r4) => {
                        encoder.Add(r4);
                    });
                });
               

                return encoder;
            }


            public static NodeDB BiserDecode(byte[] enc = null, Biser.Decoder extDecoder = null)
            {
                Biser.Decoder decoder = null;
                if (extDecoder == null)
                {
                    if (enc == null || enc.Length == 0)
                        return null;
                    decoder = new Biser.Decoder(enc);
                }
                else
                {
                    if (extDecoder.CheckNull())
                        return null;
                    else
                        decoder = extDecoder;
                }

                NodeDB m = new NodeDB();

                m.Connections = decoder.CheckNull() ? null : new System.Collections.Generic.List<System.Collections.Generic.List<System.Int32>>();
                if (m.Connections != null)
                {
                    decoder.GetCollection(() => {
                        var pvar1 = decoder.CheckNull() ? null : new System.Collections.Generic.List<System.Int32>();
                        if (pvar1 != null)
                        {
                            decoder.GetCollection(() => {
                                var pvar2 = decoder.GetInt();
                                return pvar2;
                            }, pvar1, true);
                        }
                        return pvar1;
                    }, m.Connections, true);
                }
                m.MaxLevel = decoder.GetInt();
                m.Id = decoder.GetInt();
                m.ExternalId = decoder.GetLong();
                m.Deleted = decoder.GetBool();

                // Decode ProtectedConnections
                m.ProtectedConnections = decoder.CheckNull() ? null : new System.Collections.Generic.Dictionary<System.Int32, System.Collections.Generic.HashSet<System.Int32>>();
                if (m.ProtectedConnections != null)
                {
                    decoder.GetCollection(() =>
                    {
                        var pvar3 = decoder.GetInt();
                        return pvar3;
                    },
                () =>
                {
                    var pvar4 = decoder.CheckNull() ? null : new System.Collections.Generic.HashSet<System.Int32>();
                    if (pvar4 != null)
                    {
                        decoder.GetCollection(() =>
                        {
                            var pvar5 = decoder.GetInt();
                            return pvar5;
                        }, pvar4, true);
                    }
                    return pvar4;
                }, m.ProtectedConnections, true);
                }


                return m;
            }
        }


        internal partial class BucketDB {

            public int BucketId { get; set; }
            public int EntryPointId { get; set; }
            public int Count { get; set; }
            public int DeletedCount { get; set; }
        }

        internal partial class BucketDB : Biser.IEncoder
        {


            public Biser.Encoder BiserEncoder(Biser.Encoder existingEncoder = null)
            {
                Biser.Encoder encoder = new Biser.Encoder(existingEncoder);


                encoder.Add(BucketId);
                encoder.Add(EntryPointId);
                encoder.Add(Count);
                encoder.Add(DeletedCount);

                return encoder;
            }


            public static BucketDB BiserDecode(byte[] enc = null, Biser.Decoder extDecoder = null)
            {
                Biser.Decoder decoder = null;
                if (extDecoder == null)
                {
                    if (enc == null || enc.Length == 0)
                        return null;
                    decoder = new Biser.Decoder(enc);
                }
                else
                {
                    if (extDecoder.CheckNull())
                        return null;
                    else
                        decoder = extDecoder;
                }

                BucketDB m = new BucketDB();



                m.BucketId = decoder.GetInt();
                m.EntryPointId = decoder.GetInt();
                m.Count = decoder.GetInt();
                m.DeletedCount = decoder.GetInt();


                return m;
            }


        }
    }
    
}
#endif