/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using DBreeze.Utils;


namespace DBreeze.TextSearch
{
    /// <summary>
    /// Word aligned bitmap index
    /// </summary>
    internal class WABI
    {

        byte[] bt = null;
        byte currentProtocol = 1;

        /// <summary>
        /// 
        /// </summary>
        public WABI()
        {
        }

        /// <summary>
        /// Must be supplied CompressedByteArray taken from GetCompressedByteArray function
        /// </summary>
        /// <param name="array"></param>
        public WABI(byte[] array)
        {
            if (array == null || array.Length == 0)
                return;

            if (array.Length < 2)
                throw new InvalidDataException("DBreeze.TextSearch: invalid WABI payload");

            //First byte is SByte showing by module(ABS) version of the protocol
            //if <0 then compressed
            int payloadLength = array.Length - 2;
            bt = new byte[payloadLength];
            if (payloadLength != 0)
                Buffer.BlockCopy(array, 2, bt, 0, payloadLength);
            if (array[1] == 1)
                bt = bt.GZip_Decompress();

        }


        /// <summary>
        /// Working byte[]
        /// </summary>
        /// <returns></returns>
        public byte[] GetUncompressedByteArray()
        {
            if (bt == null || bt.Length == 0)
                return new byte[0];

            return bt;
        }

        /// <summary>
        /// With extra protocol definition, ready for save into DB
        /// </summary>
        /// <returns></returns>
        public byte[] GetCompressedByteArray()
        {
            if (bt == null || bt.Length == 0)
                return null;

            //Compression is currently off, cause the whole dataBlock will be compressed and while searching we don't need to decompress every found word's WAH again
            //Compressing if more then 100 bytes
            //if (bt.Length > 100)
            //{
            //    byte[] tbt = bt.CompressGZip();

            //    if(bt.Length<=tbt.Length)
            //        return new byte[] { currentProtocol }.ConcatMany(new byte[] { 0 }, bt);

            //    return new byte[] { currentProtocol }.ConcatMany(new byte[] { 1 }, tbt);
            //}


            byte[] result = new byte[checked(bt.Length + 2)];
            result[0] = currentProtocol;
            result[1] = 0;
            Buffer.BlockCopy(bt, 0, result, 2, bt.Length);
            return result;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="index"></param>
        /// <param name="value"></param>
        public void Add(int index, bool value)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException("index");

            int byteNumber = index / 8;
            int rest = index % 8;

            int btLen = 0;
            if (bt != null)
                btLen = bt.Length;

            // Clearing a non-existing bit is a no-op. In particular, deleting a large missing
            // document must never expand the persisted bitmap.
            if (!value && byteNumber >= btLen)
                return;

            if (byteNumber >= btLen)
                Resize(byteNumber + 1);

            byte mask = (byte)(1 << rest);

            if (value)
                bt[byteNumber] |= mask; // set to 1
            else
            {
                bt[byteNumber] &= (byte)~mask;  // Set to zero
                TrimTrailingZeros();
            }

            //bool isSet = (bytes[byteIndex] & mask) != 0;
            //int bitInByteIndex = bitIndex % 8;
            //int byteIndex = bitIndex / 8;
            //byte mask = (byte)(1 << bitInByteIndex);
            //bool isSet = (bytes[byteIndex] & mask) != 0;
            //// set to 1
            //bytes[byteIndex] |= mask;
            //// Set to zero
            //bytes[byteIndex] &= ~mask;
            //// Toggle
            //bytes[byteIndex] ^= mask;            
        }

        /// <summary>
        /// Applies many bit changes with a single capacity calculation. Index enumeration is
        /// consumed exactly once.
        /// </summary>
        public void Add(IEnumerable<int> indexes, bool value)
        {
            if (indexes == null)
                return;

            if (value)
            {
                var materialized = indexes as ICollection<int>;
                if (materialized == null)
                    materialized = new List<int>(indexes);

                int maxIndex = -1;
                foreach (int index in materialized)
                {
                    if (index < 0)
                        throw new ArgumentOutOfRangeException("indexes");
                    if (index > maxIndex)
                        maxIndex = index;
                }

                if (maxIndex < 0)
                    return;

                int requiredLength = checked((maxIndex / 8) + 1);
                if (bt == null || bt.Length < requiredLength)
                    Resize(requiredLength);

                foreach (int index in materialized)
                    bt[index / 8] |= (byte)(1 << (index % 8));
            }
            else
            {
                int length = bt == null ? 0 : bt.Length;
                if (length == 0)
                    return;

                foreach (int index in indexes)
                {
                    if (index < 0)
                        throw new ArgumentOutOfRangeException("indexes");
                    int byteNumber = index / 8;
                    if (byteNumber < length)
                        bt[byteNumber] &= (byte)~(1 << (index % 8));
                }

                TrimTrailingZeros();
            }
        }

        public bool IsEmpty
        {
            get { return bt == null || bt.Length == 0; }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="len"></param>
        void Resize(int len)
        {
            byte[] btNew = new byte[len];
            if (bt == null)
            {
                bt = btNew;
                return;
            }

            Buffer.BlockCopy(bt, 0, btNew, 0, Math.Min(bt.Length, len));

            bt = btNew;
            return;
        }

        void TrimTrailingZeros()
        {
            if (bt == null)
                return;

            int length = bt.Length;
            while (length > 0 && bt[length - 1] == 0)
                length--;

            if (length == bt.Length)
                return;
            if (length == 0)
            {
                bt = null;
                return;
            }

            Resize(length);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public bool Contains(int index)
        {
            int btLen = 0;
            if (bt != null)
                btLen = bt.Length;

            if (btLen < 1)
                return false;

            int byteNumber = Convert.ToInt32(index / 8);

            if (byteNumber > (btLen - 1))
                return false;

            int rest = index % 8;
            byte mask = (byte)(1 << rest);
            return (bt[byteNumber] & mask) != 0;
        }

        ///// <summary>
        ///// Using OR logic: 1|1 = 1|0 = 1; 0|0 = 0
        ///// </summary>
        ///// <param name="indexesToMerge"></param>
        ///// <returns></returns>
        //public static byte[] MergeAllUncompressedIntoOne(List<byte[]> indexesToMerge)
        //{
        //    //if (indexesToMerge == null || indexesToMerge.Count() < 1)
        //    //    return null;
        //    int MaxLenght = indexesToMerge.Max(r => r.Length);
        //    byte[] res = new byte[MaxLenght];

        //    foreach (var bt in indexesToMerge)
        //    {
        //        for (int i = 0; i < bt.Length; i++)
        //        {
        //            res[i] |= bt[i];
        //        }

        //    }

        //    return res;
        //}

        /// <summary>
        /// Using AND logic: 1 and 1 = 1; 1 and 0 = 0; 0 and 0 = 0
        /// </summary>
        /// <param name="arraysToMerge"></param>
        /// <returns></returns>
        public static byte[] MergeByAndLogic(List<byte[]> arraysToMerge)
        {
            if (arraysToMerge == null || arraysToMerge.Count == 0)
                return null;

            int minLength = Int32.MaxValue;
            for (int i = 0; i < arraysToMerge.Count; i++)
            {
                byte[] current = arraysToMerge[i];
                if (current == null || current.Length == 0)
                    return null;
                if (current.Length < minLength)
                    minLength = current.Length;
            }

            if (arraysToMerge.Count == 1)
                return arraysToMerge[0];

            byte[] result = new byte[minLength];
            Buffer.BlockCopy(arraysToMerge[0], 0, result, 0, minLength);
            for (int arrayIndex = 1; arrayIndex < arraysToMerge.Count; arrayIndex++)
                for (int i = 0; i < minLength; i++)
                    result[i] &= arraysToMerge[arrayIndex][i];

            return TrimResult(result);
        }

        /// <summary>
        /// Using OR logic: 1or1 = 1or0 = 1; 0or0 = 0
        /// </summary>
        /// <param name="arraysToMerge"></param>
        /// <returns></returns>
        public static byte[] MergeByOrLogic(List<byte[]> arraysToMerge)
        {
            if (arraysToMerge == null || arraysToMerge.Count == 0)
                return null;

            int maxLength = 0;
            byte[] only = null;
            int nonEmptyCount = 0;
            for (int i = 0; i < arraysToMerge.Count; i++)
            {
                byte[] current = arraysToMerge[i];
                if (current == null || current.Length == 0)
                    continue;
                nonEmptyCount++;
                only = current;
                if (current.Length > maxLength)
                    maxLength = current.Length;
            }

            if (maxLength == 0)
                return null;
            if (nonEmptyCount == 1)
                return only;

            byte[] result = new byte[maxLength];
            for (int arrayIndex = 0; arrayIndex < arraysToMerge.Count; arrayIndex++)
            {
                byte[] current = arraysToMerge[arrayIndex];
                if (current == null)
                    continue;
                for (int i = 0; i < current.Length; i++)
                    result[i] |= current[i];
            }

            return TrimResult(result);
        }

        /// <summary>
        /// Using XOR logic: 1xor1 = 0; 0xor0 = 0; 1xor0 = 1
        /// </summary>
        /// <param name="arraysToMerge"></param>
        /// <returns></returns>
        public static byte[] MergeByXorLogic(List<byte[]> arraysToMerge)
        {
            if (arraysToMerge == null || arraysToMerge.Count == 0)
                return null;

            int maxLength = 0;
            byte[] only = null;
            int nonEmptyCount = 0;
            for (int i = 0; i < arraysToMerge.Count; i++)
            {
                byte[] current = arraysToMerge[i];
                if (current == null || current.Length == 0)
                    continue;
                nonEmptyCount++;
                only = current;
                if (current.Length > maxLength)
                    maxLength = current.Length;
            }

            if (maxLength == 0)
                return null;
            if (nonEmptyCount == 1)
                return only;

            byte[] result = new byte[maxLength];
            for (int arrayIndex = 0; arrayIndex < arraysToMerge.Count; arrayIndex++)
            {
                byte[] current = arraysToMerge[arrayIndex];
                if (current == null)
                    continue;
                for (int i = 0; i < current.Length; i++)
                    result[i] ^= current[i];
            }

            return TrimResult(result);
        }

        /// <summary>
        /// Using EXCLUDE logic: 1notin1 = 0; 1notin0 = 1; 0notin0 = 0; 0notin1 = 0;
        /// </summary>
        /// <param name="array1"></param>
        /// <param name="array2"></param>
        /// <returns></returns>
        public static byte[] MergeByExcludeLogic(byte[] array1, byte[] array2)
        {
            if (array1 == null || array1.Length == 0)
                return null;
            if (array2 == null || array2.Length == 0)
                return array1;
            byte[] result = new byte[array1.Length];

            int overlap = Math.Min(array1.Length, array2.Length);
            for (int i = 0; i < overlap; i++)
                result[i] = (byte)(array1[i] & ~array2[i]);
            if (overlap < array1.Length)
                Buffer.BlockCopy(array1, overlap, result, overlap, array1.Length - overlap);

            return TrimResult(result);
        }

        static byte[] TrimResult(byte[] value)
        {
            int length = value.Length;
            while (length > 0 && value[length - 1] == 0)
                length--;

            if (length == 0)
                return null;
            if (length == value.Length)
                return value;

            byte[] result = new byte[length];
            Buffer.BlockCopy(value, 0, result, 0, length);
            return result;
        }

        /// <summary>
        /// Technical if already in DB
        /// </summary>
        public bool ExistsInDB = false;

        ///// <summary>
        ///// Returns first added document first (sort by ID asc)
        ///// </summary>
        ///// <param name="indexesToCheck"></param>
        ///// <returns></returns>
        //public static IEnumerable<uint> TextSearch_AND_logic(List<byte[]> indexesToCheck)
        //{
        //    int MinLenght = indexesToCheck.Min(r => r.Length);
        //    byte res = 0;
        //    uint docId = 0;
        //    byte mask = 0;

        //    for (int i = 0; i < MinLenght; i++)
        //    {
        //        res = 255;
        //        foreach (var wah in indexesToCheck)
        //        {
        //            res &= wah[i];
        //        }

        //        for (int j = 0; j < 8; j++)
        //        {
        //            mask = (byte)(1 << j);

        //            if ((res & mask) != 0)
        //                yield return docId;

        //            docId++;
        //        }
        //    }
        //}

        /// <summary>
        /// Returns last added documents first
        /// </summary>
        /// <param name="indexesToCheck"></param>
        /// <returns></returns>
        public static IEnumerable<uint> TextSearch_AND_logic(List<byte[]> indexesToCheck)
        {
            if (indexesToCheck != null && indexesToCheck.Count > 0)
            {
                int MinLenght = indexesToCheck.Min(r => r == null ? 0 : r.Length);
                if (MinLenght != 0)
                {
                    byte res = 0;
                    uint docId = Convert.ToUInt32(MinLenght * 8) - 1;
                    byte mask = 0;

                    for (int i = MinLenght - 1; i >= 0; i--)
                    {
                        res = 255;
                        foreach (var wah in indexesToCheck)
                        {
                            res &= wah[i];
                        }

                        for (int j = 7; j >= 0; j--)
                        {
                            mask = (byte)(1 << j);

                            if ((res & mask) != 0)
                                yield return (uint)docId;

                            docId--;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="indexesToCheck"></param>
        /// <param name="docStart">when 0 not counted</param>
        /// <param name="docStop">when 0 not counted</param>
        /// <param name="descending"></param>
        /// <returns></returns>
        public static IEnumerable<uint> TextSearch_AND_logic(List<byte[]> indexesToCheck, int docStart=0, int docStop=0, bool descending = true)
        {
            if (indexesToCheck != null && indexesToCheck.Count > 0)
            {
                int MinLenght = indexesToCheck.Min(r => r == null ? 0 : r.Length);

                if (MinLenght != 0)
                {
                    byte res = 0;
                    uint docId = 0;
                    byte mask = 0;

                    int start = 0;
                    int stop = 0;

                    if (descending)
                    {                      
                        start = MinLenght - 1;
                        stop = 0;

                        if (docStart > 0)
                        {
                            start = docStart / 8;
                            if (start > MinLenght - 1)
                                start = MinLenght - 1;
                        }

                        if (docStop > 0)
                            stop = docStop / 8;

                        if(stop <= start && start <= MinLenght - 1 && stop <= MinLenght - 1)
                        {
                            docId = Convert.ToUInt32((start + 1) * 8) - 1;
                            for (int i = start; i >= stop; i--)
                            {                              

                                res = 255;
                                foreach (var wah in indexesToCheck)
                                {
                                    res &= wah[i];
                                }

                                if (i == start && docStart > 0)
                                {
                                    for (int j = 7; j >= 0; j--)
                                    {
                                        if (docId > docStart)
                                        {
                                            docId--;
                                            continue;
                                        }

                                        mask = (byte)(1 << j);

                                        if ((res & mask) != 0)
                                        {
                                            if (docId < docStop)
                                                break;

                                            yield return (uint)docId;
                                        }

                                        docId--;
                                    }
                                }
                                else if(i == stop && docStop > 0)
                                {
                                    for (int j = 7; j >= 0; j--)
                                    {
                                        if (docId < docStop)
                                            break;

                                        mask = (byte)(1 << j);

                                        if ((res & mask) != 0)
                                            yield return (uint)docId;

                                        docId--;
                                    }
                                }
                                else
                                { 
                                    for (int j = 7; j >= 0; j--)
                                    {
                                        mask = (byte)(1 << j);

                                        if ((res & mask) != 0)
                                            yield return (uint)docId;

                                        docId--;
                                    }
                                }

                            }
                        }
                    }
                    else
                    {//ASCENDING
                        start = 0;
                        stop = MinLenght - 1;

                        if (docStart > 0)
                            start = docStart / 8;

                        if (docStop > 0)
                        {
                            stop = docStop / 8;
                            if (MinLenght - 1 < stop)
                                stop = MinLenght - 1;
                        }

                        if (start <= stop && stop <= MinLenght - 1 && start <= MinLenght - 1)
                        {
                            docId = Convert.ToUInt32((start + 1) * 8) - 8;
                            for (int i = start; i <= stop; i++)
                            {
                                

                                res = 255;
                                foreach (var wah in indexesToCheck)
                                {
                                    res &= wah[i];
                                }

                                if (i == start && docStart > 0)
                                {
                                    for (int j = 0; j <= 7; j++)
                                    {
                                        if (docId < docStart)
                                        {
                                            docId++;
                                            continue;
                                        }

                                        mask = (byte)(1 << j);

                                        if ((res & mask) != 0)
                                        {
                                            if (docId > docStop)
                                                break;
                                            yield return (uint)docId;
                                        }

                                        docId++;
                                    }
                                }
                                else if (i == stop && docStop > 0)
                                {
                                    for (byte j = 0; j <= 7; j++)
                                    {
                                        if (docId > docStop)
                                            break;

                                        mask = (byte)(1 << j);                                       

                                        if ((res & mask) != 0)
                                            yield return (uint)docId;

                                        docId++;
                                    }
                                   
                                }
                                else
                                {
                                    for (int j = 0; j <= 7; j++)
                                    {
                                        mask = (byte)(1 << j);

                                        if ((res & mask) != 0)
                                            yield return (uint)docId;

                                        docId++;
                                    }
                                }
                            }
                        }
                    }



                }
            }
        }


        ///// <summary>
        ///// SOrt by ID desc
        ///// </summary>
        ///// <param name="indexesToCheck"></param>
        ///// <param name="maximalReturnQuantity"></param>
        ///// <returns></returns>
        //public static IEnumerable<uint> TextSearch_OR_logic(List<byte[]> indexesToCheck, int maximalReturnQuantity)
        //{
        //    int MaxLenght = indexesToCheck.Max(r => r.Length);
        //    uint docId = 0;
        //    byte mask = 0;
        //    int added = 0;
        //    int[] el = new int[8];

        //    SortedDictionary<int, List<uint>> d = new SortedDictionary<int, List<uint>>();
        //    List<uint> docLst = null;

        //    for (int i = 0; i < MaxLenght; i++)
        //    {
        //        foreach (var wah in indexesToCheck)
        //        {
        //            if (i > (wah.Length - 1))
        //                continue;

        //            for (int j = 0; j < 8; j++)
        //            {
        //                mask = (byte)(1 << j);
        //                if ((wah[i] & mask) != 0)
        //                    el[j] += 1;
        //            }
        //        }

        //        //Here we analyze el array
        //        for (int j = 0; j < 8; j++)
        //        {
        //            //el[j] contains quantity of occurance
        //            if (el[j] > 0)
        //            {
        //                if (!d.TryGetValue(el[j], out docLst))
        //                    docLst = new List<uint>();

        //                added++;
        //                docLst.Add(docId);

        //                d[el[j]] = docLst;
        //            }

        //            el[j] = 0;
        //            docId++;
        //        }

        //        if (added > maximalReturnQuantity)
        //            break;
        //    }

        //    foreach (var ret in d.OrderByDescending(r => r.Key))
        //        foreach (var docs in ret.Value)
        //            yield return docs;
        //}


        /// <summary>
        /// 
        /// </summary>
        /// <param name="indexesToCheck"></param>
        /// <param name="maximalReturnQuantity"></param>
        /// <returns></returns>
        public static IEnumerable<uint> TextSearch_OR_logic(List<byte[]> indexesToCheck, int maximalReturnQuantity)
        {
            int MaxLenght = indexesToCheck.Max(r => r.Length);
            uint docId = Convert.ToUInt32(MaxLenght * 8) - 1;
            byte mask = 0;
            int added = 0;
            int[] el = new int[8];

            SortedDictionary<int, List<uint>> d = new SortedDictionary<int, List<uint>>();
            List<uint> docLst = null;

            for (int i = MaxLenght - 1; i >= 0; i--)
            {
                foreach (var wah in indexesToCheck)
                {
                    if (i > (wah.Length - 1))
                        continue;

                    //for (int j = 0; j < 8; j++)
                    for (int j = 7; j >= 0; j--)
                    {
                        mask = (byte)(1 << j);
                        if ((wah[i] & mask) != 0)
                            el[j] += 1;
                    }
                }

                //Here we analyze el array
                //for (int j = 0; j < 8; j++)
                for (int j = 7; j >= 0; j--)
                {
                    //el[j] contains quantity of occurance
                    if (el[j] > 0)
                    {
                        if (!d.TryGetValue(el[j], out docLst))
                            docLst = new List<uint>();

                        added++;
                        yield return docId;
                        //docLst.Add(docId);

                        d[el[j]] = docLst;
                    }

                    el[j] = 0;
                    docId--;
                }

                if (added > maximalReturnQuantity)
                    break;
            }

            //foreach (var ret in d.OrderByDescending(r => r.Key))
            //    foreach (var docs in ret.Value)
            //        yield return docs;
        }

    }
}

