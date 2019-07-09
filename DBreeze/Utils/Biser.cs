/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.

  This class uses parts of code from https://github.com/topas/VarintBitConverter. That is published under BSD license [27.06.2016].
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;

namespace DBreeze.Utils
{
    /// <summary>
    /// Custom binary serializer of well known types
    /// </summary>
    public static partial class Biser
    {
        public interface IEncoder
        {
            Biser.Encoder BiserEncoder(Biser.Encoder existingEncoder = null);
        }

        public interface IJSEncoder
        {
            Biser.Encoder BiserJSEncoder(Encoder existingEncoder = null);
        }

        public interface IJsonEncoder
        {
            void BiserJsonEncode(JsonEncoder encoder);
        }

        #region "Main: GetVarintBytes, ZigZag "

        /// <summary>
        /// Uses protobuf concepts
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] GetVarintBytes(ulong value)
        {
            var buffer = new byte[10];
            var pos = 0;
            byte byteVal;
            do
            {
                byteVal = (byte)(value & 0x7f);
                value >>= 7;

                if (value != 0)
                {
                    byteVal |= 0x80;
                }

                buffer[pos++] = byteVal;

            } while (value != 0);

            var result = new byte[pos];
            Buffer.BlockCopy(buffer, 0, result, 0, pos);

            return result;
        }

        /// <summary>
        /// ToTarget
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="sizeBites"></param>
        /// <returns></returns>
        public static ulong ToTarget(byte[] bytes, int sizeBites)
        {
            int shift = 0;
            ulong result = 0;

            foreach (ulong byteValue in bytes)
            {
                ulong tmp = byteValue & 0x7f;
                result |= tmp << shift;

                if (shift > sizeBites)
                {
                    throw new ArgumentOutOfRangeException("bytes", "Byte array is too large.");
                }

                if ((byteValue & 0x80) != 0x80)
                {
                    return result;
                }

                shift += 7;
            }

            throw new ArgumentException("Cannot decode varint from byte array.", "bytes");
        }


        public static long EncodeZigZag(long value, int bitLength)
        {
            return (value << 1) ^ (value >> (bitLength - 1));
        }

        public static long DecodeZigZag(ulong value)
        {
            if ((value & 0x1) == 0x1)
                return (-1 * ((long)(value >> 1) + 1));

            return (long)(value >> 1);
        }

       
        #endregion


        /// <summary>
        /// Proto encoding of Dictionary [string, List[byte[]]].
        /// Hashset can be null, after decoding istantiated, zero-length hashset will be returned in this case.
        /// </summary>
        /// <param name="d"></param>
        /// <param name="compression"></param>
        /// <returns></returns>
        public static byte[] Encode_DICT_PROTO_STRING_BYTEARRAYHASHSET(this IDictionary<string, List<byte[]>> d, Compression.eCompressionMethod compression = Compression.eCompressionMethod.NoCompression)
        {
            if (d == null || d.Count == 0)
                return null;

            List<byte[]> ar = new List<byte[]>();
            int size = 0;
            byte[] tar = null;
            byte[] tar1 = null;

            foreach (var el in d)
            {
                //Setting key
                tar = el.Key.To_UTF8Bytes();
                tar1 = GetVarintBytes((uint)tar.Length);
                ar.Add(tar1);//length of key
                ar.Add(tar);//key self
                size += tar1.Length;
                size += tar.Length;

                //Setting count of hashset
                tar = GetVarintBytes((uint)(el.Value == null ? 0 : el.Value.Count));
                ar.Add(tar);
                size += tar.Length;
                //Hashset
                if (el.Value != null)
                {
                    foreach (var evl in el.Value)
                    {
                        //size of byte array
                        tar = GetVarintBytes((uint)(evl == null ? 0 : evl.Length));
                        ar.Add(tar);
                        size += tar.Length;
                        //byte array self
                        if (evl != null && evl.Length > 0)
                        {
                            ar.Add(evl);
                            size += evl.Length;
                        }
                    }
                }

            }

            byte[] encB = new byte[size];
            int pt = 0;
            foreach (var el in ar)
            {
                Buffer.BlockCopy(el, 0, encB, pt, el.Length);
                pt += el.Length;
            }

            switch (compression)
            {
                case Compression.eCompressionMethod.Gzip:
                    encB = encB.GZip_Compress();
                    break;
            }

            return encB;
        }
        
        /// <summary>
        /// Decodes byte[] into  Dictionary [string, List[byte[]]]
        /// </summary>
        /// <param name="encB"></param>
        /// <param name="retD"></param>
        /// <param name="compression"></param>
        /// <returns></returns>
        public static void Decode_DICT_PROTO_STRING_BYTEARRAYHASHSET(this byte[] encB, IDictionary<string, List<byte[]>> retD, Compression.eCompressionMethod compression = Compression.eCompressionMethod.NoCompression)
        {
            if (encB == null || encB.Length < 1)
                return;

            switch (compression)
            {
                case Compression.eCompressionMethod.Gzip:
                    encB = encB.GZip_Decompress();
                    break;
            }

            byte mode = 0;
            byte[] sizer = new byte[4];
            int size = 0;

            uint keyLength = 0;
            string key = "";
            uint valCnt = 0;
            uint lenBa = 0;

            Action ClearSizer = () =>
            {
                sizer[0] = 0;
                sizer[1] = 0;
                sizer[2] = 0;
                sizer[3] = 0;

                size = 0;
            };

            //0 - reading key
            //1 - HashSet Count
            //2 - reading Hashset elements one by one            

            byte el = 0;
            int i = 0;
            int hc = 0; //Hashset grabbed count
            List<byte[]> mhs = null;

            while (i < encB.Length)
            {
                el = encB[i];

                switch (mode)
                {
                    case 0:

                        if ((el & 0x80) > 0)
                        {
                            sizer[size] = el;
                            size++;
                        }
                        else
                        {
                            hc = 0;
                            mode = 1;
                            sizer[size] = el;
                            size++;
                            keyLength = ToUInt32(sizer);
                            //key = System.Text.Encoding.UTF8.GetString(encB.Substring(i + 1, (int)keyLength));
                            key = encB.Substring(i + 1, (int)keyLength).UTF8_GetString();
                            i += (int)keyLength + 1;
                            ClearSizer();
                            continue;
                        }

                        break;
                    case 1:
                        //HashSet Count
                        if ((el & 0x80) > 0)
                        {
                            sizer[size] = el;
                            size++;
                        }
                        else
                        {
                            mode = 2;
                            sizer[size] = el;
                            size++;
                            valCnt = ToUInt32(sizer);
                            ClearSizer();

                            if (valCnt == 0)
                            {
                                retD.Add(key, new List<byte[]>());
                                mode = 0;
                            }
                        }
                        break;
                    case 2:
                        if ((el & 0x80) > 0)
                        {
                            sizer[size] = el;
                            size++;
                        }
                        else
                        {
                            sizer[size] = el;
                            size++;
                            if (hc == 0)
                                mhs = new List<byte[]>();
                            lenBa = ToUInt32(sizer);                            
                            mhs.Add(encB.Substring(i + 1, (int)lenBa));
                            i += (int)lenBa + 1;                          
                            hc++;
                            ClearSizer();

                            if (valCnt == hc)
                            {
                                mode = 0;
                                retD.Add(key, mhs);
                            }
                            continue;
                           
                        }
                        break;
                }
                i++;
            }

            return;
        }
        
        /// <summary>
        /// Proto encoding of Dictionary [string, HashSet[uint]].
        /// Hashset can be null, after decoding istantiated, zero-length hashset will be returned in this case.
        /// </summary>
        /// <param name="d"></param>
        /// <param name="compression"></param>
        /// <returns></returns>
        public static byte[] Encode_DICT_PROTO_STRING_UINTHASHSET(this IDictionary<string, HashSet<uint>> d, Compression.eCompressionMethod compression = Compression.eCompressionMethod.NoCompression)
        {
            if (d == null || d.Count == 0)
                return null;

            List<byte[]> ar = new List<byte[]>();            
            int size = 0;
            byte[] tar = null;
            byte[] tar1 = null;
            
            foreach (var el in d)
            {
                //Setting key
                tar = el.Key.To_UTF8Bytes();
                tar1 = GetVarintBytes((uint)tar.Length);
                ar.Add(tar1);//length of key
                ar.Add(tar);//key self
                size += tar1.Length;
                size += tar.Length;

                //Setting count of hashset
                tar = GetVarintBytes((uint)(el.Value == null ? 0 : el.Value.Count));
                ar.Add(tar);    
                size += tar.Length;
                //Hashset
                if (el.Value != null)
                {
                    foreach (var evl in el.Value)
                    {
                        tar = GetVarintBytes(evl);
                        ar.Add(tar);
                        size += tar.Length;
                    }
                }
                
            }

            byte[] encB = new byte[size];
            int pt = 0;
            foreach (var el in ar)
            {
                Buffer.BlockCopy(el, 0, encB, pt, el.Length);
                pt += el.Length;
            }

            switch (compression)
            {
                case Compression.eCompressionMethod.Gzip:
                    encB = encB.GZip_Compress();
                    break;
            }

            return encB;
        }

        /// <summary>
        /// Decodes byte[] into  Dictionary [string, HashSet[uint]]
        /// </summary>
        /// <param name="encB"></param>
        /// <param name="retD"></param>
        /// <param name="compression"></param>
        /// <returns></returns>
        public static void Decode_DICT_PROTO_STRING_UINTHASHSET(this byte[] encB, IDictionary<string, HashSet<uint>> retD, Compression.eCompressionMethod compression = Compression.eCompressionMethod.NoCompression)
        {
            if (encB == null || encB.Length < 1)
                return;

            switch (compression)
            {
                case Compression.eCompressionMethod.Gzip:
                    encB = encB.GZip_Decompress();
                    break;
            }

            byte mode = 0;
            byte[] sizer = new byte[4];
            int size = 0;

            uint keyLength = 0;
            string key = "";
            uint valCnt = 0;                      

            Action ClearSizer = () =>
            {
                sizer[0] = 0;
                sizer[1] = 0;
                sizer[2] = 0;
                sizer[3] = 0;

                size = 0;
            };

            //0 - reading key
            //1 - HashSet Count
            //2 - reading Hashset elements one by one            

            byte el = 0;
            int i = 0;
            int hc = 0; //Hashset grabbed count
            HashSet<uint> mhs = null;

            while (i < encB.Length)
            {
                el = encB[i];

                switch (mode)
                {
                    case 0:
                        
                        if ((el & 0x80) > 0)
                        {
                            sizer[size] = el;
                            size++;
                        }
                        else
                        {
                            hc = 0;
                            mode = 1;
                            sizer[size] = el;
                            size++;
                            keyLength = ToUInt32(sizer);
                            //key = System.Text.Encoding.UTF8.GetString(encB.Substring(i + 1, (int)keyLength));
                            key = encB.Substring(i + 1, (int)keyLength).UTF8_GetString();
                            i += (int)keyLength+1;
                            ClearSizer();
                            continue;
                        }

                        break;
                    case 1:
                        //HashSet Count
                        if ((el & 0x80) > 0)
                        {
                            sizer[size] = el;
                            size++;
                        }
                        else
                        {
                            mode = 2;
                            sizer[size] = el;
                            size++;
                            valCnt = ToUInt32(sizer);
                            ClearSizer();

                            if (valCnt == 0)
                            {
                                retD.Add(key, new HashSet<uint>());
                                mode = 0;
                            }
                        }
                        break;
                    case 2:
                        if ((el & 0x80) > 0)
                        {
                            sizer[size] = el;
                            size++;
                        }
                        else
                        {                            
                            sizer[size] = el;
                            size++;                                                    
                            if (hc == 0)
                                mhs = new HashSet<uint>();
                            mhs.Add(ToUInt32(sizer));
                            hc++;
                            ClearSizer();
                            
                            if (valCnt == hc)
                            {                                
                                mode = 0;
                                retD.Add(key, mhs);
                            }                            
                        }
                        break;
                }
                i++;
            }

            return;
        }

        /// <summary>
        /// Proto encoding of Dictionary [uint, byte[]].
        /// Use when key is less then 4 bytes (less then 268mln).
        /// Value byte[0] will be presented as null after decoding
        /// </summary>
        /// <param name="d"></param>
        /// <param name="compression">compression method extra applied to the outgoing byte array</param>
        /// <returns></returns>
        public static byte[] Encode_DICT_PROTO_UINT_BYTEARRAY(this IDictionary<uint, byte[]> d, Compression.eCompressionMethod compression = Compression.eCompressionMethod.NoCompression)
        {
            if (d == null || d.Count == 0)
                return null;
                                    
            List<byte[]> ar = new List<byte[]>();
            ulong size = 0;
            byte[] tar = null;

            foreach (var el in d)
            {
                //Setting key
                tar = GetVarintBytes(el.Key);
                size += (ulong)tar.Length;
                ar.Add(tar);
                //Setting length of value
                tar = el.Value == null ? new byte[] { 0 } : GetVarintBytes((uint)el.Value.Length); //Supporting 0 length, will be null then
                size += (ulong)tar.Length;
                ar.Add(tar);
                //Setting value
                size += (ulong)(el.Value == null ? 0 : el.Value.Length);
                if (el.Value != null && el.Value.Length > 0)
                    ar.Add(el.Value);
            }

            byte[] encB = new byte[size];
            int pt = 0;
            foreach (var el in ar)
            {
                Buffer.BlockCopy(el, 0, encB, pt, el.Length);
                pt += el.Length;
            }

            switch (compression)
            {
                case Compression.eCompressionMethod.Gzip:
                    encB = encB.GZip_Compress();                    
                    break;
            }

            return encB;
        }

        /// <summary>
        /// Used when parameter is encoded with Encode_DICT_PROTO_UINT_BYTEARRAY.
        /// Returns Dictionary [uint, byte[]]
        /// </summary>
        /// <param name="encB"></param>
        /// <param name="retD">Instantiated Dictionary must be supplied and will be returned filled</param>        
        /// <param name="compression">compression method supplied by Encode_DICT_PROTO_UINT_BYTEARRAY</param>
        public static void Decode_DICT_PROTO_UINT_BYTEARRAY(this byte[] encB, IDictionary<uint, byte[]> retD, Compression.eCompressionMethod compression = Compression.eCompressionMethod.NoCompression)
        {            
            if (encB == null || encB.Length < 1)
                return;

            switch (compression)
            {
                case Compression.eCompressionMethod.Gzip:                    
                    encB = encB.GZip_Decompress();                    
                    break;
            }
            

            byte mode = 0;
            byte[] sizer = new byte[4];
            int size = 0;

            uint key = 0;
            uint valLen = 0;
            byte[] val = null;
            int valCnt = 0;

            Action ClearSizer = () =>
            {
                sizer[0] = 0;
                sizer[1] = 0;
                sizer[2] = 0;
                sizer[3] = 0;

                size = 0;
            };

            //0 - reading key
            //1 - reading size of value
            //2 - reading value
            foreach (byte el in encB)
            {
                switch (mode)
                {
                    case 0:
                        //Key, Size of BT //
                        if ((el & 0x80) > 0)
                        {
                            sizer[size] = el;
                            size++;
                        }
                        else
                        {
                            mode = 1;
                            sizer[size] = el;
                            size++;
                            key = ToUInt32(sizer);
                            ClearSizer();
                        }

                        break;
                    case 1:
                        //Value Size
                        if ((el & 0x80) > 0)
                        {
                            sizer[size] = el;
                            size++;
                        }
                        else
                        {
                            mode = 2;
                            sizer[size] = el;
                            size++;
                            valLen = ToUInt32(sizer);
                            ClearSizer();

                            if (valLen == 0)
                            {
                                retD.Add(key, null);
                                mode = 0;
                                break;
                            }

                            val = new byte[valLen];
                            valCnt = 0;
                        }
                        break;
                    case 2:
                        val[valCnt] = el;
                        valCnt++;
                        if (valCnt == valLen)
                        {
                            retD.Add(key, val);
                            mode = 0;
                            break;
                        }
                        break;
                }
            }

            return;
        }

        /// <summary>
        /// null elements equal to new byte[0]
        /// </summary>
        /// <param name="d"></param>
        /// <returns></returns>
        public static byte[] Encode_PROTO_ListByteArray(IList<byte[]> d)
        {
            //null element of the list is not allowed (the same as in protobuf), byte[0]  means String.Empty

            if (d == null || d.Count == 0)
                return new byte[0];

            byte[] tar1 = null;            
            using (MemoryStream ms = new MemoryStream()) 
            {

                foreach (var el in d)
                {
                    //Setting key
                    ms.Write(new byte[] { 10 }, 0, 1); //complex Type - 10     
                    if (el == null || el.Length == 0)
                        ms.Write(new byte[] { 0 }, 0, 1);// 0 length element
                    else
                    {                        
                        tar1 = Biser.GetVarintBytes((ulong)el.Length);
                        ms.Write(tar1, 0, tar1.Length);//length of key
                        ms.Write(el, 0, el.Length);//key self                    
                    }
                }

                tar1 = ms.ToArray();
#if !NETPORTABLE
                    ms.Close();
#endif
            }

            return tar1;
        }

        /// <summary>
        /// null elements equal to new byte[0]
        /// </summary>
        /// <param name="encB"></param>
        /// <returns></returns>
        public static List<byte[]> Decode_PROTO_ListByteArray(byte[] encB)
        {

            //null element of the list is not allowed (the same as in protobuf), byte[0]  means String.Empty

            if (encB == null)
                return null;

            List<byte[]> ret = new List<byte[]>();

            if (encB.Length == 0)
                return ret;

            List<byte[]> ar = new List<byte[]>();
            int i = 0;
            int mode = 0;

            byte[] sizer = new byte[4];
            int size = 0;
            uint valCnt = 0;
            byte el = 0;
            byte[] key = new byte[0];

            Action ClearSizer = () =>
            {
                sizer[0] = 0;
                sizer[1] = 0;
                sizer[2] = 0;
                sizer[3] = 0;

                size = 0;
            };

            while (i < encB.Length)
            {
                el = encB[i];

                switch (mode)
                {
                    case 0:
                        //Always delimiter 10
                        mode = 1;
                        break;
                    case 1:
                        //Reading length of the next text
                        if ((el & 0x80) > 0)
                        {
                            sizer[size] = el;
                            size++;
                        }
                        else
                        {
                            mode = 0;
                            sizer[size] = el;
                            size++;
                            valCnt = DBreeze.Utils.Biser.ToUInt32(sizer);
                            ClearSizer();
                            if (valCnt > 0)
                            {
                                key = encB.Substring(i + 1, (int)valCnt);
                                i += (int)valCnt;
                                ret.Add(key);
                            }
                            else
                                ret.Add(new byte[0]);
                        }
                        break;
                }
                i++;
            }

            return ret;
        }


        //https://github.com/topas/VarintBitConverter/blob/master/src/VarintBitConverter/VarintBitConverter.cs


        ///// <summary>
        ///// Returns the specified byte value as varint encoded array of bytes.   
        ///// </summary>
        ///// <param name="value">Byte value</param>
        ///// <returns>Varint array of bytes.</returns>
        //static byte[] GetVarintBytes(byte value)
        //{
        //    return GetVarintBytes((ulong)value);
        //}

        ///// <summary>
        ///// Returns the specified 16-bit signed value as varint encoded array of bytes.   
        ///// </summary>
        ///// <param name="value">16-bit signed value</param>
        ///// <returns>Varint array of bytes.</returns>
        //static byte[] GetVarintBytes(short value)
        //{
        //    var zigzag = EncodeZigZag(value, 16);
        //    return GetVarintBytes((ulong)zigzag);
        //}

        ///// <summary>
        ///// Returns the specified 16-bit unsigned value as varint encoded array of bytes.   
        ///// </summary>
        ///// <param name="value">16-bit unsigned value</param>
        ///// <returns>Varint array of bytes.</returns>
        //static byte[] GetVarintBytes(ushort value)
        //{
        //    return GetVarintBytes((ulong)value);
        //}

        ///// <summary>
        ///// Returns the specified 32-bit signed value as varint encoded array of bytes.   
        ///// </summary>
        ///// <param name="value">32-bit signed value</param>
        ///// <returns>Varint array of bytes.</returns>
        //static byte[] GetVarintBytes(int value)
        //{
        //    var zigzag = EncodeZigZag(value, 32);
        //    return GetVarintBytes((ulong)zigzag);
        //}

        /// <summary>
        /// Returns the specified 32-bit unsigned value as varint encoded array of bytes.
        /// Uses protobuf concepts
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        static byte[] GetVarintBytes(uint value)
        {
            return GetVarintBytes((ulong)value);
        }
       

        /// <summary>
        /// Returns the specified 64-bit signed value as varint encoded array of bytes.   
        /// </summary>
        /// <param name="value">64-bit signed value</param>
        /// <returns>Varint array of bytes.</returns>
        public static byte[] GetVarintBytes(long value)
        {
            var zigzag = EncodeZigZag(value, 64);
            return GetVarintBytes((ulong)zigzag);
        }

       

        ///// <summary>
        ///// Returns byte value from varint encoded array of bytes.
        ///// </summary>
        ///// <param name="bytes">Varint encoded array of bytes.</param>
        ///// <returns>Byte value</returns>
        //static byte ToByte(byte[] bytes)
        //{
        //    return (byte)ToTarget(bytes, 8);
        //}

        ///// <summary>
        ///// Returns 16-bit signed value from varint encoded array of bytes.
        ///// </summary>
        ///// <param name="bytes">Varint encoded array of bytes.</param>
        ///// <returns>16-bit signed value</returns>
        //static short ToInt16(byte[] bytes)
        //{
        //    var zigzag = ToTarget(bytes, 16);
        //    return (short)DecodeZigZag(zigzag);
        //}

        ///// <summary>
        ///// Returns 16-bit usigned value from varint encoded array of bytes.
        ///// </summary>
        ///// <param name="bytes">Varint encoded array of bytes.</param>
        ///// <returns>16-bit usigned value</returns>
        //static ushort ToUInt16(byte[] bytes)
        //{
        //    return (ushort)ToTarget(bytes, 16);
        //}

        ///// <summary>
        ///// Returns 32-bit signed value from varint encoded array of bytes.
        ///// </summary>
        ///// <param name="bytes">Varint encoded array of bytes.</param>
        ///// <returns>32-bit signed value</returns>
        //public static int ToInt32(byte[] bytes)
        //{
        //    var zigzag = ToTarget(bytes, 32);
        //    return (int)DecodeZigZag(zigzag);
        //}

        /// <summary>
        /// Uses protobuf concepts
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        static uint ToUInt32(byte[] bytes)
        {
            return (uint)ToTarget(bytes, 32);
        }

        ///// <summary>
        ///// Returns 64-bit signed value from varint encoded array of bytes.
        ///// </summary>
        ///// <param name="bytes">Varint encoded array of bytes.</param>
        ///// <returns>64-bit signed value</returns>
        //public static long ToInt64(byte[] bytes)
        //{
        //    var zigzag = ToTarget(bytes, 64);
        //    return DecodeZigZag(zigzag);
        //}

        ///// <summary>
        ///// Returns 64-bit unsigned value from varint encoded array of bytes.
        ///// </summary>
        ///// <param name="bytes">Varint encoded array of bytes.</param>
        ///// <returns>64-bit unsigned value</returns>
        //static ulong ToUInt64(byte[] bytes)
        //{
        //    return ToTarget(bytes, 64);
        //}








    }//EO Class
}//EO N
