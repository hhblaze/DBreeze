/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBreeze.Utils
{
    public static class BytesProcessing
    {
                

        /// <summary>
        /// Enlarges byte array till given size filling with 0 from start the rest of the length.
        /// Ex: byte[] a = new byte[] {1,2,3}; a.EnlargeByteArray_BigEndian(6) = new byte[] {0,0,0,1,2,3};
        /// If array for enlargement equals null new byte[size] will be returned, if array for enlargement length more or equal size then the same array will be returned.
        /// </summary>
        /// <param name="ar"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static byte[] EnlargeByteArray_BigEndian(this byte[] ar, int size)
        {
            if (ar == null)
                return new byte[size];

            if (ar.Length >= size)
                return ar;

            byte[] rb = new byte[size];
            Buffer.BlockCopy(ar, 0, rb, size - ar.Length, ar.Length);
            return rb;
        }

        /// <summary>
        /// Enlarges byte array till given size filling with 0 after values of the supplied array.
        /// Ex: byte[] a = new byte[] {1,2,3}; a.EnlargeByteArray_LittleEndian(6) = new byte[] {1,2,3,0,0,0};
        /// If array for enlargement equals null new byte[size] will be returned, if array for enlargement length more or equal size then the same array will be returned.
        /// </summary>
        /// <param name="ar"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static byte[] EnlargeByteArray_LittleEndian(this byte[] ar, int size)
        {
            if (ar == null)
                return new byte[size];

            if (ar.Length >= size)
                return ar;

            byte[] rb = new byte[size];
            Buffer.BlockCopy(ar, 0, rb, 0, ar.Length);
            return rb;
        }


        /// <summary>
        /// Substring int-dimensional byte arrays
        /// </summary>
        /// <param name="ar"></param>
        /// <param name="startIndex"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static byte[] Substring(this byte[] ar, int startIndex, int length)
        {
            //return substringByteArray(ar, startIndex, length);

            if (ar == null)
                return null;

            if (ar.Length < 1)
                return ar;

            if (startIndex > ar.Length - 1)
                return null;

            if (startIndex + length > ar.Length)
            {
                //we make length till the end of array
                length = ar.Length - startIndex;
            }

            byte[] ret = new byte[length];


            Buffer.BlockCopy(ar, startIndex, ret, 0, length);

            //int len = startIndex + length;
            //int j = 0;
            //for (int i = startIndex; i < len; i++)
            //{
            //    ret[j] = ar[i];
            //    j++;
            //}

            return ret;
        }

       

        /// <summary>
        /// Substring int-dimensional byte arrays from and till the end
        /// </summary>
        /// <param name="ar"></param>
        /// <param name="startIndex"></param>
        /// <returns></returns>
        public static byte[] Substring(this byte[] ar, int startIndex)
        {
            //if (ar == null)
            //    return null;

            //return substringByteArray(ar, startIndex, ar.Length);


            int length = ar.Length;

            if (ar == null)
                return null;

            if (ar.Length < 1)
                return ar;

            if (startIndex > ar.Length - 1)
                return null;

            if (startIndex + length > ar.Length)
            {
                //we make length till the end of array
                length = ar.Length - startIndex;
            }

            byte[] ret = new byte[length];


            Buffer.BlockCopy(ar, startIndex, ret, 0, length);

            return ret;
        }

        /// <summary>
        /// Works only for int-dimesional arrays only
        /// </summary>
        /// <param name="ar"></param>
        /// <returns></returns>
        public static byte[] CloneArray(this byte[] ar)
        {
            byte[] rb = null;

            if (ar == null)
                return null;

            if (ar.Length < 1)
                return new byte[] { };

            rb = new byte[ar.Length];

            Buffer.BlockCopy(ar, 0, rb, 0, ar.Length);

            return rb;
        }

        /// <summary>
        /// Copies one array (source) into another (destination extension).
        /// <para>Destination array is taken as this</para>
        /// </summary>
        /// <param name="destArray"></param>
        /// <param name="destOffset"></param>
        /// <param name="srcArray"></param>
        /// <param name="srcOffset"></param>
        /// <param name="quantity"></param>
        public static void CopyInside(this byte[] destArray, int destOffset, byte[] srcArray, int srcOffset, int quantity)
        {
            Buffer.BlockCopy(srcArray, srcOffset, destArray, destOffset, quantity);
        }

        /// <summary>
        /// Copies fully one array (source) into another (destination extension). Extra parameter destination offset.
        /// <para>Doesn't return anything, but changes destination array by reference.</para>
        /// <para>Can Throw exception if destArray.Length less then (destOffset + srcArray.Length)</para>
        /// <para>, for this use CopyInsideArrayCanGrow</para>
        /// </summary>
        /// <param name="destArray"></param>
        /// <param name="destOffset"></param>
        /// <param name="srcArray"></param>
        public static void CopyInside(this byte[] destArray, int destOffset, byte[] srcArray)
        {
            Buffer.BlockCopy(srcArray, 0, destArray, destOffset, srcArray.Length);
        }


        /// <summary>
        /// Will return finally created array 
        /// <para>byte[] b = new byte[] { 1, 2, 3 };</para>
        /// <para>byte[] v = b.CopyInsideArrayCanGrow(1, new byte[] { 5, 6, 7 });</para>
        /// <para>will return v = byte[] { 1, 5, 6, 7 }</para>
        /// </summary>
        /// <param name="destArray"></param>
        /// <param name="destOffset"></param>
        /// <param name="srcArray"></param>
        /// <returns></returns>
        public static byte[] CopyInsideArrayCanGrow(this byte[] destArray, int destOffset, byte[] srcArray)
        {
            byte[] ret = null;

            if (destArray.Length < (destOffset + srcArray.Length))
            {
                ret = new byte[destOffset + srcArray.Length];                
            }
            else
            {
                ret = new byte[destArray.Length];                
            }

            Buffer.BlockCopy(destArray, 0, ret, 0, destArray.Length);
            Buffer.BlockCopy(srcArray, 0, ret, destOffset, srcArray.Length);

            return ret;
        }

        
        /// <summary>
        /// Removes leading element from the array.
        /// Never returns null, but can return new byte[] {} (Length=0)
        /// </summary>
        /// <param name="array"></param>
        /// <param name="elementToRemove"></param>
        /// <returns></returns>
        public static byte[] RemoveLeadingElement(this byte[] array, byte elementToRemove)
        {
            if (array == null || array.Length == 0)
                return array;

            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] != elementToRemove)
                {
                    return array.Substring(i);
                }
            }

            return new byte[] { };
        }


        /// <summary>
        /// Array.Reverse is the same fast, but reverses by reference the parameter-array, what is not acceptable
        /// </summary>
        /// <param name="ar"></param>
        /// <returns></returns>
        public static byte[] Reverse(this byte[] ar)
        {
            if (ar == null || ar.Length == 0)
                return ar;

            byte[] ret = new byte[ar.Length];
            int j = 0;
            for (int i = (ar.Length - 1); i >= 0; i--)
            {
                ret[j] = ar[i];
                j++;
            }
            return ret;
        }


        #region "Bytes concatenation"
      

        /// <summary>
        /// Fastest Method. Works only for int-dimesional arrays only. 
        /// When necessary to concat many arrays use ConcatMany
        /// </summary>
        /// <param name="ar1"></param>
        /// <param name="ar2"></param>
        /// <returns></returns>
        public static byte[] Concat(this byte[] ar1, byte[] ar2)
        {
            if (ar1 == null)
                ar1 = new byte[] { };
            if (ar2 == null)
                ar2 = new byte[] { };

            byte[] ret = null;

            ret = new byte[ar1.Length + ar2.Length];

            Buffer.BlockCopy(ar1, 0, ret, 0, ar1.Length);
            Buffer.BlockCopy(ar2, 0, ret, ar1.Length, ar2.Length);

            return ret;
        }

        /// <summary>
        /// FOR OPTIMITATION LIKE Concat(this byte[] ar1, byte ar2)
        /// </summary>
        /// <param name="ar1"></param>
        /// <param name="ar2"></param>
        /// <returns></returns>
        public static byte[] Concat(this byte ar1, byte ar2)
        {
            return (new byte[] { ar1 }).Concat(new byte[] { ar2 });
        }

        /// <summary>
        /// FOR OPTIMITATION LIKE Concat(this byte[] ar1, byte ar2)
        /// </summary>
        /// <param name="ar1"></param>
        /// <param name="ar2"></param>
        /// <returns></returns>
        public static byte[] Concat(this byte ar1, byte[] ar2)
        {
            return (new byte[] { ar1 }).Concat(ar2);
        }

       

        public static byte[] Concat(this byte[] ar1, byte ar2)
        {
            //return ar1.Concat(new byte[] { ar2 });

            //After optimization

            if (ar1 == null || ar1.Length == 0)
                return new byte[] { ar2 };

            byte[] ret = null;

            ret = new byte[ar1.Length + 1];
            Buffer.BlockCopy(ar1, 0, ret, 0, ar1.Length);
            ret[ret.Length - 1] = ar2;

            return ret;
        }

        ///// <summary>
        ///// Fastest Method (the same as Concat). Works only for int-dimesional arrays only
        ///// </summary>
        ///// <param name="ar1"></param>
        ///// <param name="ar2"></param>
        ///// <returns></returns>
        //public static byte[] _ConcatByteArray(this byte[] ar1, byte[] ar2)
        //{
        //    if (ar1 == null)
        //        ar1 = new byte[] { };
        //    if (ar2 == null)
        //        ar2 = new byte[] { };
        //    byte[] ret = new byte[ar1.Length + ar2.Length];

        //    Buffer.BlockCopy(ar1, 0, ret, 0, ar1.Length);
        //    Buffer.BlockCopy(ar2, 0, ret, ar1.Length, ar2.Length);

        //    return ret;
        //}

        /// <summary>
        /// Fast when necessary to concat many arrays
        /// Example: byte[] s = new byte[] { 1, 2, 3 }; s.ConcatMany(new byte[] { 2, 3, 4 }, new byte[] { 3, 7, 8 }, new byte[] { 9, 10, 11 });
        /// Also: ((byte[])null).ConcatMany(new byte[] { 2, 3, 4 }, new byte[] { 3, 7, 8 }, new byte[] { 9, 10, 11 });
        /// </summary>
        /// <param name="ar1"></param>
        /// <param name="ar2"></param>
        /// <returns></returns>
        public static byte[] ConcatMany(this byte[] ar1, params byte[][] arrays)
        {
            if (ar1 == null)
                ar1 = new byte[] { };

            //Faster then arrays.Sum(x => (x == null) ? 0 : x.Length)
            long len = 0;
            foreach (var data in arrays)
            {
                if (data == null)
                    continue;
                len += data.Length;
            }

            //byte[] ret = new byte[ar1.Length + arrays.Sum(x => (x == null) ? 0 : x.Length)];
            byte[] ret = new byte[ar1.Length + len];
            int offset = 0;

            Buffer.BlockCopy(ar1, 0, ret, offset, ar1.Length);
            offset += ar1.Length;

            foreach (byte[] data in arrays)
            {
                if (data == null) //faster than foreach (byte[] data in arrays.Where(r=>r != null))
                    continue;

                Buffer.BlockCopy(data, 0, ret, offset, data.Length);
                offset += data.Length;
            }
            return ret;

        }

        /// <summary>
        /// Concats many byte arrays
        /// </summary>
        /// <param name="ar1"></param>
        /// <param name="arrays"></param>
        /// <returns></returns>
        public static byte[] ConcatMany(this byte[] ar1, IList<byte[]> arrays)
        {
            if (ar1 == null)
                ar1 = new byte[] { };

            //Faster then arrays.Sum(x => (x == null) ? 0 : x.Length)
            long len = 0;
            foreach (var data in arrays)
            {
                if (data == null)
                    continue;
                len += data.Length;
            }

            //byte[] ret = new byte[ar1.Length + arrays.Sum(x => (x == null) ? 0 : x.Length)];
            byte[] ret = new byte[ar1.Length + len];
            int offset = 0;

            Buffer.BlockCopy(ar1, 0, ret, offset, ar1.Length);
            offset += ar1.Length;

            foreach (byte[] data in arrays)
            {
                if (data == null) //faster than foreach (byte[] data in arrays.Where(r=>r != null))
                    continue;

                Buffer.BlockCopy(data, 0, ret, offset, data.Length);
                offset += data.Length;
            }
            return ret;

        }

        /// <summary>
        /// Concats all arrays into one byte[]
        /// </summary>
        /// <param name="arrays"></param>
        /// <returns></returns>
        public static byte[] Concat(this IList<byte[]> arrays)
        {
            //Faster then arrays.Sum(x => (x == null) ? 0 : x.Length)
            long len = 0;
            foreach (var data in arrays)
            {
                if (data == null)
                    continue;
                len += data.Length;
            }

            byte[] ret = new byte[len];
            int offset = 0;

            foreach (byte[] data in arrays)
            {
                if (data == null) //faster than foreach (byte[] data in arrays.Where(r=>r != null))
                    continue;

                Buffer.BlockCopy(data, 0, ret, offset, data.Length);
                offset += data.Length;
            }
            return ret;
        }

        /// <summary>
        /// Concept of the objects storage (read docu from 20170321)
        /// Concatenates byte representing index and other elements, converted to byte[] using DBreeze converters, sequentially.
        /// </summary>
        /// <param name="indexNumber">Index number (one byte from 1-255)</param>
        /// <param name="pars">Parts of the index to be converted to byte[]</param>
        /// <returns></returns>
        public static byte[] ToIndex(this int indexNumber, params object[] pars)
        {
            if (indexNumber < 1 || indexNumber > 255)
                throw new Exception("DBreezeIndex: 1-255 is an allowed index region!");
            return ToIndex((byte)indexNumber, pars);
        }

        /// <summary>
        /// Concept of the objects storage (read docu from 20170321)
        /// Concatenates byte representing index and other elements, converted to byte[] using DBreeze converters, sequentially.
        /// </summary>
        /// <param name="indexNumber">Index number (one byte from 1-255)</param>
        /// <param name="pars">Parts of the index to be converted to byte[]</param>
        /// <returns></returns>
        public static byte[] ToIndex(this byte indexNumber, params object[] pars)
        {
            if (indexNumber < 1)
                throw new Exception("DBreezeIndex: 1-255 is an allowed index region!");

            if (pars == null || pars.Length < 1)
                return new byte[] { indexNumber };
            List<byte[]> xbts = new List<byte[]>();
            xbts.Add(DataTypes.DataTypesConvertor.ConvertValue(indexNumber, typeof(byte)));
            foreach (var prop in pars)
                xbts.Add(DataTypes.DataTypesConvertor.ConvertValue(prop, prop.GetType()));

            return xbts.Concat();
        }

        /// <summary>
        /// Concatenates converted to byte[] elements sequentially. 
        /// DBreeze converters are used.
        /// </summary>
        /// <param name="pars"></param>
        /// <returns></returns>
        public static byte[] ToBytes(params object[] pars)
        {
            if (pars == null || pars.Length < 1)
                return null;
            List<byte[]> xbts = new List<byte[]>();
            foreach (var prop in pars)
                xbts.Add(DataTypes.DataTypesConvertor.ConvertValue(prop, prop.GetType()));

            return xbts.Concat();
        }

        /// <summary>
        /// Concatenates converted to byte[] elements sequentially. 
        /// DBreeze converters are used.
        /// </summary>
        /// <param name="par1"></param>
        /// <param name="pars"></param>
        /// <returns></returns>
        public static byte[] ToBytes(this object par1, params object[] pars)
        {
            if (par1 == null)
                return null;

            List<byte[]> xbts = new List<byte[]>();
            xbts.Add(DataTypes.DataTypesConvertor.ConvertValue(par1, par1.GetType()));
            if (pars != null)
                foreach (var prop in pars)
                    xbts.Add(DataTypes.DataTypesConvertor.ConvertValue(prop, prop.GetType()));

            return xbts.Concat();
        }

        ///// <summary>
        ///// Slower then _ConcatByteArray. Linq concat is used. Yield return is used. Be sure byte arrays are safely boxed. ToArray() is used to get byte[] from IEnumerable.
        ///// Can have less memory consumption in compare with _ConcatByteArray before GC.
        ///// Example: byte[] s = new byte[] { 1, 2, 3 }; s._LinqConcatB(new byte[] { 2, 3, 4 }, new byte[] { 3, 7, 8 });
        ///// </summary>
        ///// <param name="ar1"></param>
        ///// <param name="arrays"></param>
        ///// <returns></returns>
        //public static byte[] _LinqConcatByteArrays(this byte[] ar1, params byte[][] arrays)
        //{
        //    if(ar1 == null)
        //        ar1 = new byte[] { };
        //    return ar1.Concat(_LinqConcatByteArraysIE(arrays).ToArray());
        //}

        ///// <summary>
        ///// Slower then _ConcatByteArray. Linq concat is used. Yield return is used. Be sure byte arrays are safely boxed. ToArray() is used to get byte[] from IEnumerable.
        ///// Can have less memory consumption in compare with _ConcatByteArray before GC.
        ///// Example: _LinqConcatB1(new byte[] { 2, 3, 4 }, new byte[] { 3, 7, 8 });
        ///// 
        ///// </summary>
        ///// <param name="arrays"></param>
        ///// <returns></returns>
        //public static byte[] _LinqConcatByteArrays(params byte[][] arrays)
        //{
        //    return _LinqConcatByteArraysIE(arrays).ToArray();
        //}

        ///// <summary>
        ///// Same speed as _ConcatByteArray, but returns not byte[]. Converting can take from 10% of time. Cant't be used in recursion before converting to byte[]. Linq concat is used. Yield return is used. result will be IEnumerable of byte
        ///// Can have less memory consumption in compare with _ConcatByteArray before GC.
        /////  _LinqConcatIE(new byte[] { 2, 3, 4 }, new byte[] { 3, 7, 8 });
        ///// </summary>
        ///// <param name="arrays"></param>
        ///// <returns></returns>
        //public static IEnumerable<byte> _LinqConcatByteArraysIE(params byte[][] arrays)
        //{
        //    foreach (byte[] a in arrays)
        //    {
        //        if (a == null)
        //            continue;
        //        foreach (byte b in a)
        //            yield return b;
        //    }
        //}
        #endregion



        private static byte[] substringByteArray(byte[] ar, int startIndex, int length)
        {
            if (ar == null)
                return null;

            if (ar.Length < 1)
                return ar;

            if (startIndex > ar.Length - 1)
                return null;

            if (startIndex + length > ar.Length)
            {
                //we make length till the end of array
                length = ar.Length - startIndex;
            }

            byte[] ret = new byte[length];


            Buffer.BlockCopy(ar, startIndex, ret, 0, length);

           


            return ret;
        }



        /// <summary>
        /// If not found returns -1
        /// </summary>
        /// <param name="ar"></param>
        /// <param name="search"></param>
        /// <param name="en"></param>
        /// <returns></returns>
        public static int indexOfStringInByteArray(this byte[] ar, string search, Encoding en)
        {
            if (ar.Length < search.Length)
                return -1;

            byte[] sr = en.GetBytes(search);
            int returnIndex = -1;

            for (int i = 0; i < ar.Length - search.Length + 1; i++)
            {
                for (int j = 0; j < sr.Length; j++)
                {
                    if (ar[i + j] != sr[j])
                    {
                        returnIndex = -1;
                        break;
                    }
                    returnIndex++;
                }

                if (returnIndex != -1)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Searches Start index of the byte[] pattern inside of the byte array
        /// If not found returns -1
        /// </summary>
        /// <param name="ar"></param>
        /// <param name="search"></param>
        /// <param name="en"></param>
        /// <returns></returns>
        public static int IndexOfByteArray(this byte[] ar, byte[] search)
        {
            if (ar == null || search == null || ar.Length == 0 || search.Length == 0)
                return -1;

            if (ar.Length < search.Length)
                return -1;
                        
            int returnIndex = -1;

            for (int i = 0; i < ar.Length - search.Length + 1; i++)
            {
                for (int j = 0; j < search.Length; j++)
                {
                    if (ar[i + j] != search[j])
                    {
                        returnIndex = -1;
                        break;
                    }
                    returnIndex++;
                }

                if (returnIndex != -1)
                    return i;
            }

            return -1;
        }



        //0x0801 
        //Big Endian - First comes higer: 0x08 0x01 = 2049
        //Little Endian - First comes lower 0x01 0x08 = 2049



        #region "Conversions Bytes To Other"

        #region "Single byte"

        /// <summary>
        /// From 1 byte array returns byte
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte To_Byte(this byte[] value)
        {
            return value[0];

        }
        #endregion

        #region "Single byte ?"

        /// <summary>
        /// From 2 bytes array returns byte?
        /// If array length is not equal to 2 bytes returns null
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte? To_Byte_NULL(this byte[] value)
        {
            if (value == null || value.Length != 2 || value[0] == 0)
                return null;

            return value[1];

        }
        #endregion

        #region "DateTime"

        /// <summary>
        /// 8-byte array tries to convert to DateTime
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static DateTime To_DateTime(this byte[] value)
        {
            return new DateTime((long)value.To_UInt64_BigEndian());

        }

        /// <summary>
        /// DON't use it (only for compatibility reasons described in docu from [20120922])
        /// BigEndian 8 bytes tries to convert to Ticks
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static DateTime To_DateTime_zCompatibility(this byte[] value)
        {
            return new DateTime(value.To_Int64_BigEndian());

        }
        #endregion

        #region "DateTime ?"

        /// <summary>
        /// Returns DateTime? from 9-byte array
        /// If array is not equal 9 bytes returns null
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static DateTime? To_DateTime_NULL(this byte[] value)
        {
            if (value == null || value.Length != 9 || value[0] == 0)
                return null;

            return new DateTime((long)(new byte[] { value[1], value[2], value[3], value[4], value[5], value[6], value[7], value[8] }.To_UInt64_BigEndian()));
            //return new DateTime(value.To_Int64_BigEndian());

        }
        #endregion

        #region "Boolean"

        public static bool To_Bool(this byte[] value)
        {
            return (value[0] == 1);
           
        }
        #endregion

        #region "Boolean?"

        /// <summary>
        /// Returns bool? from 1-byte array
        /// if value length != 1 returns null.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static bool? To_Bool_NULL(this byte[] value)
        {
            if (value == null || value.Length != 1 || value[0] == 2)
                return null;

            return (value[0] == 1);

        }
        #endregion

        #region "Char"

        /// <summary>
        /// Converts 2 bytes byte[] into Unicode char
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static char To_Char(this byte[] value)
        {
            return (System.Text.Encoding.Unicode.GetChars(value)[0]);

        }
        #endregion

        #region "Char ?"

        /// <summary>
        /// Converts 3 bytes byte[] into Unicode char?
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static char? To_Char_NULL(this byte[] value)
        {
            if (value == null || value.Length != 3 || value[0] == 0)
                return null;

            return (System.Text.Encoding.Unicode.GetChars(new byte[] {value[1],value[2]})[0]);

        }
        #endregion

        #region "SByte"

        /// <summary>
        /// Converts 1 byte array into sbyte
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static sbyte To_SByte(this byte[] value)
        {
            return (sbyte)(value[0] + sbyte.MinValue);
        }
        #endregion

        #region "SByte ?"

        /// <summary>
        /// Converts 2 bytes array into sbyte?
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static sbyte? To_SByte_NULL(this byte[] value)
        {
            if (value == null || value.Length != 2 || value[0] == 0)
                return null;

            return (sbyte)(value[1] + sbyte.MinValue);
        }
        #endregion

        #region "Int16"

        /// <summary>
        /// From 2 bytes array which is in BigEndian order (highest byte first, lowest last) makes short.
        /// If array not equal 2 bytes throws exception. (-32,768 to 32,767)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static short To_Int16_BigEndian(this byte[] value)
        {
            //if (!BitConverter.IsLittleEndian)
            //{
            //    return BitConverter.ToInt16(value, 0);
            //}
            //else
            //{

            //    return BitConverter.ToInt16(value.Reverse(), 0);
            //}

            return (short)((value).To_UInt16_BigEndian() + short.MinValue);
        }

        /// <summary>
        /// From 2 bytes array which is in LittleEndian order (lowest byte first, highest last) makes short.
        /// If array not equal 2 bytes throws exception. (-32,768 to 32,767)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static short To_Int16_LittleEndian(this byte[] value)
        {
            //if (!BitConverter.IsLittleEndian)
            //{
            //    return BitConverter.ToInt16(value.Reverse(), 0);
            //}
            //else
            //{
            //    return BitConverter.ToInt16(value, 0);
            //}

            return (short)((value).To_UInt16_LittleEndian() + short.MinValue);
        }
        #endregion

        #region "Int16 ?"

        /// <summary>
        /// From 3 bytes array which is in BigEndian order (highest byte first, lowest last) makes short?.
        /// If array not equal 3 bytes returns null. (-32,768 to 32,767)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static short? To_Int16_BigEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 3 || value[0] == 0)
                return null;

            return (short?)((value).To_UInt16_BigEndian_NULL() + short.MinValue);
        }

        /// <summary>
        /// From 3 bytes array which is in LittleEndian order (lowest byte first, highest last) makes short.
        /// If array not equal 3 bytes returns null. (-32,768 to 32,767)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static short? To_Int16_LittleEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 3 || value[0] == 0)
                return null;

            return (short?)((value).To_UInt16_LittleEndian_NULL() + short.MinValue);
        }
        #endregion

        #region "UInt16"
        /// <summary>
        /// From 2 bytes array which is in BigEndian order (highest byte first, lowest last) makes ushort.
        /// If array not equal 2 bytes throws exception. (0 to 65,535)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static ushort To_UInt16_BigEndian(this byte[] value)
        {
            //if (!BitConverter.IsLittleEndian)
            //{
            //    return BitConverter.ToUInt16(value, 0);
            //}
            //else
            //{
            //    return BitConverter.ToUInt16(value.Reverse(), 0);
            //}

            return (ushort)(value[0] << 8 | value[1]);
        }

        /// <summary>
        /// From 2 bytes array which is in LittleEndian order (lowest byte first, highest last) makes ushort.
        /// If array not equal 2 bytes throws exception. (0 to 65,535)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static ushort To_UInt16_LittleEndian(this byte[] value)
        {
            //if (!BitConverter.IsLittleEndian)
            //{
            //    return BitConverter.ToUInt16(value.Reverse(), 0);
            //}
            //else
            //{
            //    return BitConverter.ToUInt16(value, 0);
            //}

            return (ushort)(value[1] << 8 | value[0]);
        }
        #endregion

        #region "UInt16 ?"
        /// <summary>
        /// From 3 bytes array which is in BigEndian order (highest byte first, lowest last) makes ushort?.
        /// If array not equal 3 bytes returns null. (0 to 65,535)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static ushort? To_UInt16_BigEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 3 || value[0] == 0)
                return null;

            return (ushort)(value[1] << 8 | value[2]);
        }

        /// <summary>
        /// From 3 bytes array which is in LittleEndian order (lowest byte first, highest last) makes ushort?.
        /// If array not equal 3 bytes returns null. (0 to 65,535)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static ushort? To_UInt16_LittleEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 3 || value[0] == 0)
                return null;

            return (ushort)(value[2] << 8 | value[1]);
        }
        #endregion

        #region "Int32"
        /// <summary>
        /// From 4 bytes array which is in BigEndian order (highest byte first, lowest last) makes int.
        /// If array not equal 4 bytes throws exception. (-2,147,483,648 to 2,147,483,647)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static int To_Int32_BigEndian(this byte[] value)
        {
            //if (!BitConverter.IsLittleEndian)
            //{
            //    return BitConverter.ToInt32(value, 0);
            //}
            //else
            //{
            //    return BitConverter.ToInt32(value.Reverse(), 0);
            //}

            return (int)((value).To_UInt32_BigEndian() + int.MinValue);
        }

        /// <summary>
        /// From 4 bytes array which is in LittleEndian order (lowest byte first, highest last) makes int.
        /// If array not equal 4 bytes throws exception. (-2,147,483,648 to 2,147,483,647)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static int To_Int32_LittleEndian(this byte[] value)
        {
            //if (!BitConverter.IsLittleEndian)
            //{
            //    return BitConverter.ToInt32(value.Reverse(), 0);
            //}
            //else
            //{
            //    return BitConverter.ToInt32(value, 0);
            //}

            return (int)((value).To_UInt32_LittleEndian() + int.MinValue);
        }
        #endregion

        #region "Int32?"
        /// <summary>
        /// From 5 bytes array which is in BigEndian order (highest byte first, lowest last) makes int.
        /// If array is not equal 5 bytes returns null. Range is (-2,147,483,648 to 2,147,483,647)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static int? To_Int32_BigEndian_NULL(this byte[] value)
        {           

            if (value == null || value.Length != 5 || value[0] == 0)
                return null;

            return (int?)((new byte[] { value[1], value[2], value[3], value[4] }).To_UInt32_BigEndian() + int.MinValue);

            //return (int)((value.Substring(1)).To_UInt32_BigEndian() + int.MinValue);
        }

        /// <summary>
        /// From 5 bytes array which is in LittleEndian order (lowest byte first, highest last) makes int.
        /// If array not equal 5 bytes returns null. (-2,147,483,648 to 2,147,483,647)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static int? To_Int32_LittleEndian_NULL(this byte[] value)
        {

            if (value == null || value.Length != 5 || value[0] == 0)
                return null;

            return (int?)((new byte[] {value[1],value[2],value[3],value[4]}).To_UInt32_LittleEndian() + int.MinValue);
        }
        #endregion

        #region "UInt32"
        /// <summary>
        /// From 4 bytes array which is in BigEndian order (highest byte first, lowest last) makes uint.
        /// If array not equal 4 bytes throws exception. (0 to 4.294.967.295)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static uint To_UInt32_BigEndian(this byte[] value)
        {
            //if (!BitConverter.IsLittleEndian)
            //{
            //    return BitConverter.ToUInt32(value, 0);
            //}
            //else
            //{
            //    return BitConverter.ToUInt32(value.Reverse(), 0);
            //}

            return (uint)(value[0] << 24 | value[1] << 16 | value[2] << 8 | value[3]);
        }

        /// <summary>
        /// From 4 bytes array which is in LittleEndian order (lowest byte first, highest last) makes uint.
        /// If array not equal 4 bytes throws exception. (0 to 4.294.967.295)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static uint To_UInt32_LittleEndian(this byte[] value)
        {
            //if (!BitConverter.IsLittleEndian)
            //{
            //    return BitConverter.ToUInt32(value.Reverse(), 0);
            //}
            //else
            //{
            //    return BitConverter.ToUInt32(value, 0);
            //}

            return (uint)(value[3] << 24 | value[2] << 16 | value[1] << 8 | value[0]);
        }
        #endregion

        #region "UInt32 ?"
        /// <summary>
        /// From 5 bytes array which is in BigEndian order (highest byte first, lowest last) makes uint?.
        /// If array not equal 5 bytes returns null. (0 to 4.294.967.295)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static uint? To_UInt32_BigEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 5 || value[0] == 0)
                return null;

            return (uint)(value[1] << 24 | value[2] << 16 | value[3] << 8 | value[4]);
        }

        /// <summary>
        /// From 5 bytes array which is in LittleEndian order (lowest byte first, highest last) makes uint?.
        /// If array not equal 5 bytes returns null. (0 to 4.294.967.295)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static uint? To_UInt32_LittleEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 5 || value[0] == 0)
                return null;

            return (uint)(value[4] << 24 | value[3] << 16 | value[2] << 8 | value[1]);
        }
        #endregion
        
        #region "Int64"

       

        /// <summary>
        /// From 8 bytes array which is in BigEndian order (highest byte first, lowest last) makes long.
        /// If array not equal 8 bytes throws exception. (-9.223.372.036.854.775.808 bis 9.223.372.036.854.775.807)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static long To_Int64_BigEndian(this byte[] value)
        {
            //if (!BitConverter.IsLittleEndian)
            //{
            //    return BitConverter.ToInt64(value, 0);
            //}
            //else
            //{
            //    return BitConverter.ToInt64(value.Reverse(), 0);
            //}

            return (long)((value).To_UInt64_BigEndian() - (ulong)Math.Abs(long.MinValue + 1) - 1);
        }

        /// <summary>
        /// From 8 bytes array which is in LittleEndian order (lowest byte first, highest last) makes long.
        /// If array not equal 8 bytes throws exception. (-9.223.372.036.854.775.808 bis 9.223.372.036.854.775.807)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static long To_Int64_LittleEndian(this byte[] value)
        {
            //if (!BitConverter.IsLittleEndian)
            //{
            //    return BitConverter.ToInt64(value.Reverse(), 0);
            //}
            //else
            //{
            //    return BitConverter.ToInt64(value, 0);
            //}

            return (long)((value).To_UInt64_LittleEndian() - (ulong)Math.Abs(long.MinValue + 1) - 1);
        }
        #endregion

        #region "Int64 ?"


        /// <summary>
        /// From 9 bytes array which is in BigEndian order (highest byte first, lowest last) makes long.
        /// If array not equal 9 bytes return null. Range (-9.223.372.036.854.775.808 bis 9.223.372.036.854.775.807)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static long? To_Int64_BigEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 9 || value[0] == 0)
                return null;

            return (long?)((new byte[] { value[1], value[2], value[3], value[4], value[5], value[6], value[7], value[8] }).To_UInt64_BigEndian() - (ulong)Math.Abs(long.MinValue + 1) - 1);
        }

        /// <summary>
        /// From 9 bytes array which is in LittleEndian order (lowest byte first, highest last) makes long.
        /// If array not equal 9 bytes returns null. Range (-9.223.372.036.854.775.808 bis 9.223.372.036.854.775.807)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static long? To_Int64_LittleEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 9 || value[0] == 0)
                return null;

            return (long?)((new byte[] { value[1], value[2], value[3], value[4], value[5], value[6], value[7], value[8]}).To_UInt64_LittleEndian() - (ulong)Math.Abs(long.MinValue + 1) - 1);
        }
        #endregion

        #region "UInt64"

        /// <summary>
        /// From dynamic byte array (up to 8 bytes) stored in BigEndian format creates ulong value, 
        /// note if given byte array bigger then 8 bytes - then calcualtion will start from 0
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static ulong DynamicLength_To_UInt64_BigEndian(this byte[] value)
        {
            ulong res = 0;
            int vl = value.Length;
            for (int i = 0; i < vl; i++)
            {
                res += (ulong)value[i] << ((vl - 1 - i) * 8);
            }

            return res;
        }

        /// <summary>
        /// From 8 bytes array which is in BigEndian order (highest byte first, lowest last) makes ulong.
        /// If array not equal 8 bytes throws exception. (0 to 18,446,744,073,709,551,615)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static ulong To_UInt64_BigEndian(this byte[] value)
        {
            //if (!BitConverter.IsLittleEndian)
            //{
            //    return BitConverter.ToUInt64(value, 0);
            //}
            //else
            //{
            //    return BitConverter.ToUInt64(value.Reverse(), 0);
            //}

            return (ulong)(((ulong)value[0] << 56) + ((ulong)value[1] << 48) + ((ulong)value[2] << 40) + ((ulong)value[3] << 32) + ((ulong)value[4] << 24) + ((ulong)value[5] << 16) + ((ulong)value[6] << 8) + (ulong)value[7]);
        }

        /// <summary>
        /// From 8 bytes array which is in LittleEndian order (lowest byte first, highest last) makes ulong.
        /// If array not equal 8 bytes throws exception. (0 to 18,446,744,073,709,551,615)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static ulong To_UInt64_LittleEndian(this byte[] value)
        {
            //if (!BitConverter.IsLittleEndian)
            //{
            //    return BitConverter.ToUInt64(value.Reverse(), 0);
            //}
            //else
            //{
            //    return BitConverter.ToUInt64(value, 0);
            //}
            return (ulong)(((ulong)value[7] << 56) + ((ulong)value[6] << 48) + ((ulong)value[5] << 40) + ((ulong)value[4] << 32) + ((ulong)value[3] << 24) + ((ulong)value[2] << 16) + ((ulong)value[1] << 8) + (ulong)value[0]);
        }
        #endregion

        #region "UInt64 ?"

        
        /// <summary>
        /// From 9 bytes array which is in BigEndian order (highest byte first, lowest last) makes ulong?.
        /// If array is not equal 9 bytes returns null. (0 to 18,446,744,073,709,551,615)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static ulong? To_UInt64_BigEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 9 || value[0] == 0)
                return null;

            return (ulong)(((ulong)value[1] << 56) + ((ulong)value[2] << 48) + ((ulong)value[3] << 40) + ((ulong)value[4] << 32) + ((ulong)value[5] << 24) + ((ulong)value[6] << 16) + ((ulong)value[7] << 8) + (ulong)value[8]);
        }

        /// <summary>
        /// From 9 bytes array which is in LittleEndian order (lowest byte first, highest last) makes ulong?.
        /// If array is not equal 9 bytes returns null. (0 to 18,446,744,073,709,551,615)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static ulong? To_UInt64_LittleEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 9 || value[0] == 0)
                return null;

            return (ulong)(((ulong)value[8] << 56) + ((ulong)value[7] << 48) + ((ulong)value[6] << 40) + ((ulong)value[5] << 32) + ((ulong)value[4] << 24) + ((ulong)value[3] << 16) + ((ulong)value[2] << 8) + (ulong)value[1]);
        }
        #endregion

        #region "Decimal"

        /// <summary>
        /// Converts sortable byte[15] to decimal
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static decimal To_Decimal_BigEndian(this byte[] input)
        {
            // is Value positive
            bool blIsPositive = ((input[0] & 128) > 0);

            decimal decimalValuePart = 0M;
            // read actual decimal value (without lastDigit)
            if (blIsPositive)
            {
                decimalValuePart = new decimal(new int[4] 
                { 
                    (int)(input[9] << 24 | input[10] << 16 | input[11] << 8 | input[12]),
                    (int)(input[5] << 24 | input[6] << 16 | input[7] << 8 | input[8]),
                    (int)(input[1] << 24 | input[2] << 16 | input[3] << 8 | input[4]),
                    (int)0
                });
            }
            else
            {
                decimalValuePart = new decimal(new int[4] 
                { 
                    (int)(~((input[9]) << 24 | (input[10]) << 16 | (input[11]) << 8 | (input[12]))),
                    (int)(~((input[5]) << 24 | (input[6]) << 16 | (input[7]) << 8 | (input[8]))),
                    (int)(~((input[1]) << 24 | (input[2]) << 16 | (input[3]) << 8 | (input[4]))),
                    (int)0
                });
            }

            // last value, cutted if 29 digits value
            byte lastDigit = (byte)(input[13] >> 3);
            if (!blIsPositive) lastDigit = (byte)((~lastDigit) & 0x1F);

            // number of Digits (from original Decimal information)
            byte numOfDigits = (byte)(input[14] & 0x1F);

            // scale (fractal size of value, from original Decimal information)
            byte scale = (byte)(((input[13] & 0x03) << 3) + (input[14] >> 5));

            if (numOfDigits < 28)
            {
                decimalValuePart = Math.Floor(decimalValuePart / (decimal)Math.Pow(10, 28 - numOfDigits));
            }

            if (numOfDigits == 29)
            {
                decimalValuePart = (decimalValuePart * 10) + lastDigit;
            }

            int[] decArray = decimal.GetBits(decimalValuePart);

            return new decimal(new int[4] 
            {
                decArray[0],
                decArray[1],
                decArray[2],
                (int)((blIsPositive ? 0 : (1 << 31)) + (scale << 16))
            });
        }
        #endregion

        #region "Decimal ?"

        /// <summary>
        /// Converts sortable byte[16] to decimal? if byte array length is not 16 returns null
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static decimal? To_Decimal_BigEndian_NULL(this byte[] input)
        {
            if (input == null || input.Length != 16 || input[0] == 0)
                return null;

            // is Value positive
            bool blIsPositive = ((input[1] & 128) > 0);

            decimal decimalValuePart = 0M;
            // read actual decimal value (without lastDigit)
            if (blIsPositive)
            {
                decimalValuePart = new decimal(new int[4] 
                { 
                    (int)(input[10] << 24 | input[11] << 16 | input[12] << 8 | input[13]),
                    (int)(input[6] << 24 | input[7] << 16 | input[8] << 8 | input[9]),
                    (int)(input[2] << 24 | input[3] << 16 | input[4] << 8 | input[5]),
                    (int)0
                });
            }
            else
            {
                decimalValuePart = new decimal(new int[4] 
                { 
                    (int)(~((input[10]) << 24 | (input[11]) << 16 | (input[12]) << 8 | (input[13]))),
                    (int)(~((input[6]) << 24 | (input[7]) << 16 | (input[8]) << 8 | (input[9]))),
                    (int)(~((input[2]) << 24 | (input[3]) << 16 | (input[4]) << 8 | (input[5]))),
                    (int)0
                });
            }

            // last value, cutted if 29 digits value
            byte lastDigit = (byte)(input[14] >> 3);
            if (!blIsPositive) lastDigit = (byte)((~lastDigit) & 0x1F);

            // number of Digits (from original Decimal information)
            byte numOfDigits = (byte)(input[15] & 0x1F);

            // scale (fractal size of value, from original Decimal information)
            byte scale = (byte)(((input[14] & 0x03) << 3) + (input[15] >> 5));

            if (numOfDigits < 28)
            {
                decimalValuePart = Math.Floor(decimalValuePart / (decimal)Math.Pow(10, 28 - numOfDigits));
            }

            if (numOfDigits == 29)
            {
                decimalValuePart = (decimalValuePart * 10) + lastDigit;
            }

            int[] decArray = decimal.GetBits(decimalValuePart);

            return new decimal(new int[4] 
            {
                decArray[0],
                decArray[1],
                decArray[2],
                (int)((blIsPositive ? 0 : (1 << 31)) + (scale << 16))
            });
        }
        #endregion

        #region "Double"

        /// <summary>
        /// Converts sortable byte[9] to double
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static double To_Double_BigEndian(this byte[] input)
        {
            bool blIsPositive = ((input[0] & 128) > 0);
            int exp = ((input[0] & 127) << 8) | (input[1]);
            byte[] numberArray = new byte[8] { 0, 0, 0, 0, 0, 0, 0, 0 };
            System.Buffer.BlockCopy(input, 2, numberArray, 1, 7);
           // ulong doubleNumber = TypeConversions.ByteArrayToULong(numberArray);

            ulong doubleNumber = (ulong)(((ulong)numberArray[0] << 56) + ((ulong)numberArray[1] << 48) + ((ulong)numberArray[2] << 40) + ((ulong)numberArray[3] << 32) + ((ulong)numberArray[4] << 24) + ((ulong)numberArray[5] << 16) + ((ulong)numberArray[6] << 8) + (ulong)numberArray[7]);

            if (blIsPositive)
            {
                exp = exp - ENEG_DOUBLE;
            }
            else
            {
                doubleNumber = (ulong)((~doubleNumber) & 0xFFFFFFFFFFFFFF);
                exp = EPOS_DOUBLE - exp;
            }

            string doubleString = doubleNumber.ToString();
            string resultDouble = String.Concat
                (
                    blIsPositive ? string.Empty : "-",
                    doubleString.Substring(0, 1),
                    ".",
                    doubleString.Substring(1),
                    "E",
                    ((exp >= 0) ? "+" : string.Empty),
                    exp
                );

            double result = 0.0D;
            double.TryParse(resultDouble, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out result);

            return result;
        }

        #endregion

        #region "Double ?"

        /// <summary>
        /// Converts sortable byte[10] to double?
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static double? To_Double_BigEndian_NULL(this byte[] input)
        {
            if (input == null || input.Length != 10 || input[0] == 0)
                return null;

            bool blIsPositive = ((input[1] & 128) > 0);
            int exp = ((input[1] & 127) << 8) | (input[2]);
            byte[] numberArray = new byte[8] { 0, 0, 0, 0, 0, 0, 0, 0 };
            System.Buffer.BlockCopy(input, 3, numberArray, 1, 7);
            // ulong doubleNumber = TypeConversions.ByteArrayToULong(numberArray);

            ulong doubleNumber = (ulong)(((ulong)numberArray[0] << 56) + ((ulong)numberArray[1] << 48) + ((ulong)numberArray[2] << 40) + ((ulong)numberArray[3] << 32) + ((ulong)numberArray[4] << 24) + ((ulong)numberArray[5] << 16) + ((ulong)numberArray[6] << 8) + (ulong)numberArray[7]);

            if (blIsPositive)
            {
                exp = exp - ENEG_DOUBLE;
            }
            else
            {
                doubleNumber = (ulong)((~doubleNumber) & 0xFFFFFFFFFFFFFF);
                exp = EPOS_DOUBLE - exp;
            }

            string doubleString = doubleNumber.ToString();
            string resultDouble = String.Concat
                (
                    blIsPositive ? string.Empty : "-",
                    doubleString.Substring(0, 1),
                    ".",
                    doubleString.Substring(1),
                    "E",
                    ((exp >= 0) ? "+" : string.Empty),
                    exp
                );

            double result = 0.0D;
            double.TryParse(resultDouble, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out result);

            return result;
        }

        #endregion

        #region "Float"

        /// <summary>
        /// Converts sortable byte[4] to float
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static float To_Float_BigEndian(this byte[] input)
        {
            bool blIsPositive = ((input[0] & 128) > 0);
            int exp = input[0] & 127;
            input[0] = 0;
            uint floatNumber = (uint)(input[0] << 24 | input[1] << 16 | input[2] << 8 | input[3]);

            if (blIsPositive)
            {
                exp = exp - ENEG_FLOAT;
            }
            else
            {
                floatNumber = (uint)((~floatNumber) & 0xFFFFFF);
                exp = EPOS_FLOAT - exp;
            }

            // as value allways must be 7 digits, then string allways will be 7 symbols long
            string floatString = floatNumber.ToString();

            string resultFloat = String.Concat
                (
                    blIsPositive ? string.Empty : "-",
                    floatString.Substring(0, 1),
                    ".",
                    floatString.Substring(1),
                    "E",
                    (exp >= 0) ? "+" : string.Empty,
                    exp
                );

            float result = float.NaN;
            float.TryParse(resultFloat, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out result);

            return result;
        }

        #endregion

        #region "Float ?"

        /// <summary>
        /// Converts sortable byte[5] to float?
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static float? To_Float_BigEndian_NULL(this byte[] input)
        {
            if (input == null || input.Length != 5 || input[0] == 0)
                return null;

            input = new byte[] { input[1], input[2], input[3], input[4] };

            bool blIsPositive = ((input[0] & 128) > 0);
            int exp = input[0] & 127;
            input[0] = 0;
            uint floatNumber = (uint)(input[0] << 24 | input[1] << 16 | input[2] << 8 | input[3]);

            if (blIsPositive)
            {
                exp = exp - ENEG_FLOAT;
            }
            else
            {
                floatNumber = (uint)((~floatNumber) & 0xFFFFFF);
                exp = EPOS_FLOAT - exp;
            }

            // as value allways must be 7 digits, then string allways will be 7 symbols long
            string floatString = floatNumber.ToString();

            string resultFloat = String.Concat
                (
                    blIsPositive ? string.Empty : "-",
                    floatString.Substring(0, 1),
                    ".",
                    floatString.Substring(1),
                    "E",
                    (exp >= 0) ? "+" : string.Empty,
                    exp
                );

            float result = float.NaN;
            float.TryParse(resultFloat, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out result);

            return result;
        }

        #endregion

        #endregion  //End of byte[] to others

        #region "Conversions Other to Bytes"

        #region "Single byte"

        public static byte[] To_1_byte_array(this byte value)
        {
            return new byte[] { value };
        }

        #endregion

        #region "Single byte ?"

        /// <summary>
        /// Returns 2 byte array which represents byte?
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_2_byte_array(this byte? value)
        {
            if (value == null)
                return new byte[] { 0, 0 };

            return new byte[] { 1, (byte)value };
        }

        #endregion

        #region "DateTime"

        /// <summary>
        /// DateTime to byte[8] big-endian.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_8_bytes_array(this DateTime value)
        {
            return ((ulong)(value.Ticks)).To_8_bytes_array_BigEndian();
        }

        /// <summary>
        /// DON't use it (only for compatibility resasons described in docu from [20120922])
        /// DateTime to byte[8] big-endian.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_8_bytes_array_zCompatibility(this DateTime value)
        {
            return value.Ticks.To_8_bytes_array_BigEndian();
        }

        #endregion

        #region "DateTime ?"

        /// <summary>
        /// DateTime? to byte[9] big-endian.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_9_bytes_array(this DateTime? value)
        {
            if (value == null)
                return new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            
            return ((ulong?)((DateTime)value).Ticks).To_9_bytes_array_BigEndian();
        }

        #endregion

        #region "Boolean"

        public static byte[] To_1_byte_array(this bool value)
        {
            if (value) return new byte[] { 1 };

            return new byte[] { 0 };
        }

        #endregion

        #region "Boolean ?"

        /// <summary>
        /// Returns 1 byte which represents bool?
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_1_byte_array(this bool? value)
        {
            if (value == null)
                return new byte[] { 2 };

            if ((bool)value) return new byte[] { 1 };

            return new byte[] { 0 };
        }

        #endregion

        #region "Char"

        /// <summary>
        /// Converts char into byte[2] Unicode representation
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_2_byte_array(this char value)
        {
            return System.Text.Encoding.Unicode.GetBytes(new char[] { value });

        }
        #endregion

        #region "Char ?"

        /// <summary>
        /// Converts char? into byte[3] Unicode representation
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_3_byte_array(this char? value)
        {
            if (value == null)
                return new byte[] { 0, 0, 0 };

            return new byte[] {1}.Concat(System.Text.Encoding.Unicode.GetBytes(new char[] { (char)value }));

        }
        #endregion

        #region "SByte"

        public static byte[] To_1_byte_array(this sbyte value)
        {
            return new byte[] 
            { 
                (byte)(value - sbyte.MinValue)
            };
        }
        #endregion

        #region "SByte ?"

        /// <summary>
        /// Converts sbyte? into 2 byte array
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_2_byte_array(this sbyte? value)
        {
            if (value == null)
                return new byte[] { 0, 0 };

            return new byte[] 
            { 
                1,
                (byte)((sbyte)value - sbyte.MinValue)
            };
        }
        #endregion

        #region "Int16"
        /// <summary>
        /// From Int16 to 2 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_2_bytes_array_BigEndian(this short value)
        {

            //if (!BitConverter.IsLittleEndian)
            //{
            //    return BitConverter.GetBytes(value);
            //}
            //else
            //{
            //    byte[] bt = BitConverter.GetBytes(value);
            //    Array.Reverse(bt, 0, bt.Length);
            //    return bt;

            //    //return BitConverter.GetBytes(value).Reverse().ToArray();
            //}

            ushort val1 = (ushort)(value - short.MinValue);

            return new byte[] 
            { 
                (byte) (val1 >> 8), 
                (byte)  val1
            };

            //return ((ushort)(value - short.MinValue)).To_2_bytes_array_BigEndian();

        }

        /// <summary>
        /// From Int16 to 2 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_2_bytes_array_LittleEndian(this short value)
        {

            //if (!BitConverter.IsLittleEndian)
            //{
            //    byte[] bt = BitConverter.GetBytes(value);
            //    Array.Reverse(bt, 0, bt.Length);
            //    return bt;

            //    //return BitConverter.GetBytes(value).Reverse().ToArray();

            //}
            //else
            //{
            //    return BitConverter.GetBytes(value);
            //}

            ushort val1 = (ushort)(value - short.MinValue);

            return new byte[] 
            { 
                (byte)  val1,
                (byte) (val1 >> 8)                
            };

           // return ((ushort)(value - short.MinValue)).To_2_bytes_array_LittleEndian();
        }
        #endregion

        #region "Int16 ?"
        /// <summary>
        /// From Int16? to 3 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_3_bytes_array_BigEndian(this short? value)
        {

            if (value == null)
                return new byte[] { 0, 0, 0 };

            ushort val1 = (ushort)(value - short.MinValue);

            return new byte[] 
            { 
                1,
                (byte) (val1 >> 8), 
                (byte)  val1
            };

        }

        /// <summary>
        /// From Int16? to 3 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_3_bytes_array_LittleEndian(this short? value)
        {

            if (value == null)
                return new byte[] { 0, 0, 0 };

            ushort val1 = (ushort)(value - short.MinValue);

            return new byte[] 
            { 
                1,
                (byte)  val1,
                (byte) (val1 >> 8)                
            };
        }
        #endregion

        #region "UInt16"
        /// <summary>
        /// From UInt16 to 2 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_2_bytes_array_BigEndian(this ushort value)
        {

            //if (!BitConverter.IsLittleEndian)
            //{
            //    return BitConverter.GetBytes(value);
            //}
            //else
            //{
            //    byte[] bt = BitConverter.GetBytes(value);
            //    Array.Reverse(bt, 0, bt.Length);
            //    return bt;
            //    //return BitConverter.GetBytes(value).Reverse().ToArray();
            //}

            return new byte[] 
            { 
                (byte) (value >> 8), 
                (byte) value
            };
        }

        /// <summary>
        /// From UInt16 to 2 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_2_bytes_array_LittleEndian(this ushort value)
        {

            //if (!BitConverter.IsLittleEndian)
            //{
            //    byte[] bt = BitConverter.GetBytes(value);
            //    Array.Reverse(bt, 0, bt.Length);
            //    return bt;
            //    //return BitConverter.GetBytes(value).Reverse().ToArray();

            //}
            //else
            //{
            //    return BitConverter.GetBytes(value);
            //}

            return new byte[] 
            { 
                (byte) value,
                (byte) (value >> 8)                
            };
        }
        #endregion

        #region "UInt16 ?"
        /// <summary>
        /// From UInt16? to 3 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_3_bytes_array_BigEndian(this ushort? value)
        {
            if (value == null)
                return new byte[] { 0, 0, 0 };

            return new byte[] 
            { 
                1,
                (byte) (value >> 8), 
                (byte) value
            };
        }

        /// <summary>
        /// From UInt16? to 3 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_3_bytes_array_LittleEndian(this ushort? value)
        {
            if (value == null)
                return new byte[] { 0, 0, 0 };

            return new byte[] 
            { 
                1,
                (byte) value,
                (byte) (value >> 8)                
            };
        }
        #endregion

        #region "Int32"
        /// <summary>
        /// From Int32 to 4 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_4_bytes_array_BigEndian(this int value)
        {

            //if (!BitConverter.IsLittleEndian)
            //{
            //    return BitConverter.GetBytes(value);
            //}
            //else
            //{
            //    byte[] bt = BitConverter.GetBytes(value);
            //    Array.Reverse(bt, 0, bt.Length);
            //    return bt;

            //    //return BitConverter.GetBytes(value).Reverse().ToArray();
            //}

            uint val1 = (uint)(value - int.MinValue);

            return new byte[] 
            { 
                (byte)(val1 >> 24), 
                (byte)(val1 >> 16), 
                (byte)(val1 >> 8), 
                (byte) val1 
            };

           // return ((uint)(value - int.MinValue)).To_4_bytes_array_BigEndian();
        }

        /// <summary>
        /// From Int32 to 4 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_4_bytes_array_LittleEndian(this int value)
        {

            //if (!BitConverter.IsLittleEndian)
            //{
            //    byte[] bt = BitConverter.GetBytes(value);
            //    Array.Reverse(bt, 0, bt.Length);
            //    return bt;
            //    //return BitConverter.GetBytes(value).Reverse().ToArray();

            //}
            //else
            //{
            //    return BitConverter.GetBytes(value);
            //}
            uint val1 = (uint)(value - int.MinValue);

            return new byte[] 
            {  
                (byte) val1 ,
                (byte)(val1 >> 8), 
                (byte)(val1 >> 16), 
                (byte)(val1 >> 24), 
            };

            //return ((uint)(value - int.MinValue)).To_4_bytes_array_LittleEndian();
        }
        #endregion

        #region "Int32?"
        /// <summary>
        /// From Int32? to 5 bytes array with BigEndian order (highest byte first, lowest last).   
        /// When first byte is 0 then the whole value is NULL
        /// When first byte is 1 then value can be converted
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_5_bytes_array_BigEndian(this int? value)
        {           

            if (value == null)
                return new byte[] { 0, 0, 0, 0, 0 };    //first byte is 0 when value is NULL

            uint val1 = (uint)(value - int.MinValue);

            return new byte[] 
            { 
                1,
                (byte)(val1 >> 24), 
                (byte)(val1 >> 16), 
                (byte)(val1 >> 8), 
                (byte) val1 
            };

            // return ((uint)(value - int.MinValue)).To_4_bytes_array_BigEndian();
        }

        /// <summary>
        /// From Int32 to 4 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_5_bytes_array_LittleEndian(this int? value)
        {

            //if (!BitConverter.IsLittleEndian)
            //{
            //    byte[] bt = BitConverter.GetBytes(value);
            //    Array.Reverse(bt, 0, bt.Length);
            //    return bt;
            //    //return BitConverter.GetBytes(value).Reverse().ToArray();

            //}
            //else
            //{
            //    return BitConverter.GetBytes(value);
            //}

            if (value == null)
                return new byte[] { 0, 0, 0, 0, 0 };    //first byte is 0 when value is NULL

            uint val1 = (uint)(value - int.MinValue);

            return new byte[] 
            {  
                1,
                (byte) val1 ,
                (byte)(val1 >> 8), 
                (byte)(val1 >> 16), 
                (byte)(val1 >> 24), 
            };

            //return ((uint)(value - int.MinValue)).To_4_bytes_array_LittleEndian();
        }
        #endregion

        #region "UInt32"
        /// <summary>
        /// From UInt32 to 4 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_4_bytes_array_BigEndian(this uint value)
        {

            //if (!BitConverter.IsLittleEndian)
            //{
            //    return BitConverter.GetBytes(value);
            //}
            //else
            //{
            //    byte[] bt = BitConverter.GetBytes(value);
            //    Array.Reverse(bt, 0, bt.Length);
            //    return bt;
            //    //return BitConverter.GetBytes(value).Reverse().ToArray();
            //}

            return new byte[] 
            { 
                (byte)(value >> 24), 
                (byte)(value >> 16), 
                (byte)(value >> 8), 
                (byte) value 
            };
        }

        /// <summary>
        /// From UInt32 to 4 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_4_bytes_array_LittleEndian(this uint value)
        {

            //if (!BitConverter.IsLittleEndian)
            //{
            //    byte[] bt = BitConverter.GetBytes(value);
            //    Array.Reverse(bt, 0, bt.Length);
            //    return bt;

            //    //return BitConverter.GetBytes(value).Reverse().ToArray();

            //}
            //else
            //{
            //    return BitConverter.GetBytes(value);
            //}

            return new byte[] 
            {  
                (byte) value ,
                (byte)(value >> 8), 
                (byte)(value >> 16), 
                (byte)(value >> 24), 
            };
        }
        #endregion

        #region "UInt32 ?"
        /// <summary>
        /// From UInt32? to 5 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_5_bytes_array_BigEndian(this uint? value)
        {
            if (value == null)
                return new byte[] { 0, 0, 0, 0, 0 };    

            return new byte[] 
            { 
                1,
                (byte)(value >> 24), 
                (byte)(value >> 16), 
                (byte)(value >> 8), 
                (byte) value 
            };
        }

        /// <summary>
        /// From UInt32? to 5 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_5_bytes_array_LittleEndian(this uint? value)
        {
            if (value == null)
                return new byte[] { 0, 0, 0, 0, 0 }; 

            return new byte[] 
            {  
                1,
                (byte) value ,
                (byte)(value >> 8), 
                (byte)(value >> 16), 
                (byte)(value >> 24), 
            };
        }
        #endregion

        #region "Int64"
        /// <summary>
        /// From Int64 to 8 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_8_bytes_array_BigEndian(this long value)
        {

            //if (!BitConverter.IsLittleEndian)
            //{
            //    return BitConverter.GetBytes(value);
            //}
            //else
            //{
            //    byte[] bt = BitConverter.GetBytes(value);
            //    Array.Reverse(bt, 0, bt.Length);
            //    return bt;

            //    //return BitConverter.GetBytes(value).Reverse().ToArray();
            //}

            ulong val1 = (ulong)(value - long.MinValue);

            return new byte[] 
            { 
                (byte)(val1 >> 56), 
                (byte)(val1 >> 48), 
                (byte)(val1 >> 40), 
                (byte)(val1 >> 32), 
                (byte)(val1 >> 24), 
                (byte)(val1 >> 16), 
                (byte)(val1 >> 8), 
                (byte) val1
            };

            //return ((ulong)(value - long.MinValue)).To_8_bytes_array_BigEndian();
        }

        /// <summary>
        /// From Int64 to 8 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_8_bytes_array_LittleEndian(this long value)
        {

            //if (!BitConverter.IsLittleEndian)
            //{
            //    byte[] bt = BitConverter.GetBytes(value);
            //    Array.Reverse(bt, 0, bt.Length);
            //    return bt;

            //    //return BitConverter.GetBytes(value).Reverse().ToArray();

            //}
            //else
            //{
            //    return BitConverter.GetBytes(value);
            //}

            ulong val1 = (ulong)(value - long.MinValue);

            return new byte[] 
            {                 
                (byte) val1,
                (byte)(val1 >> 8), 
                (byte)(val1 >> 16), 
                (byte)(val1 >> 24), 
                (byte)(val1 >> 32), 
                (byte)(val1 >> 40), 
                (byte)(val1 >> 48), 
                (byte)(val1 >> 56), 
            };

            //return ((ulong)(value - long.MinValue)).To_8_bytes_array_LittleEndian();
        }
        #endregion

        #region "Int64 ?"
        /// <summary>
        /// From Int64? to 9 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_9_bytes_array_BigEndian(this long? value)
        {
            if (value == null)
                return new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            ulong val1 = (ulong)(value - long.MinValue);

            return new byte[] 
            { 
                1,
                (byte)(val1 >> 56), 
                (byte)(val1 >> 48), 
                (byte)(val1 >> 40), 
                (byte)(val1 >> 32), 
                (byte)(val1 >> 24), 
                (byte)(val1 >> 16), 
                (byte)(val1 >> 8), 
                (byte) val1
            };
        }

        /// <summary>
        /// From Int64? to 9 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_9_bytes_array_LittleEndian(this long? value)
        {
            if (value == null)
                return new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            ulong val1 = (ulong)(value - long.MinValue);

            return new byte[] 
            {        
                1,
                (byte) val1,
                (byte)(val1 >> 8), 
                (byte)(val1 >> 16), 
                (byte)(val1 >> 24), 
                (byte)(val1 >> 32), 
                (byte)(val1 >> 40), 
                (byte)(val1 >> 48), 
                (byte)(val1 >> 56), 
            };

        }
        #endregion

        #region "UInt64"
        /// <summary>
        /// From UInt64 to 8 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_8_bytes_array_BigEndian(this ulong value)
        {

            //if (!BitConverter.IsLittleEndian)
            //{
            //    return BitConverter.GetBytes(value);
            //}
            //else
            //{
            //    byte[] bt = BitConverter.GetBytes(value);
            //    Array.Reverse(bt, 0, bt.Length);
            //    return bt;

            //    //return BitConverter.GetBytes(value).Reverse().ToArray();
            //}

            return new byte[] 
            { 
                (byte)(value >> 56), 
                (byte)(value >> 48), 
                (byte)(value >> 40), 
                (byte)(value >> 32), 
                (byte)(value >> 24), 
                (byte)(value >> 16), 
                (byte)(value >> 8), 
                (byte) value
            };
        }

        /// <summary>
        /// From UInt64 to 8 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_8_bytes_array_LittleEndian(this ulong value)
        {

            //if (!BitConverter.IsLittleEndian)
            //{
            //    byte[] bt = BitConverter.GetBytes(value);
            //    Array.Reverse(bt, 0, bt.Length);
            //    return bt;

            //    //return BitConverter.GetBytes(value).Reverse().ToArray();

            //}
            //else
            //{
            //    return BitConverter.GetBytes(value);
            //}

            return new byte[] 
            {                 
                (byte) value,
                (byte)(value >> 8), 
                (byte)(value >> 16), 
                (byte)(value >> 24), 
                (byte)(value >> 32), 
                (byte)(value >> 40), 
                (byte)(value >> 48), 
                (byte)(value >> 56), 
            };
        }
        #endregion        
        
        #region "UInt64 ?"
        /// <summary>
        /// From UInt64? to 9 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_9_bytes_array_BigEndian(this ulong? value)
        {
            if (value == null)
                return new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            return new byte[] 
            { 
                1,
                (byte)(value >> 56), 
                (byte)(value >> 48), 
                (byte)(value >> 40), 
                (byte)(value >> 32), 
                (byte)(value >> 24), 
                (byte)(value >> 16), 
                (byte)(value >> 8), 
                (byte) value
            };
        }

        /// <summary>
        /// From UInt64? to 9 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_9_bytes_array_LittleEndian(this ulong? value)
        {

            if (value == null)
                return new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            return new byte[] 
            {                
                1,
                (byte) value,
                (byte)(value >> 8), 
                (byte)(value >> 16), 
                (byte)(value >> 24), 
                (byte)(value >> 32), 
                (byte)(value >> 40), 
                (byte)(value >> 48), 
                (byte)(value >> 56), 
            };
        }
        #endregion     

        #region "Decimal"

        const short BCNT_DECIMAL = 15;

        /// <summary>
        /// Converts  decimal to sortable byte[15] 
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static byte[] To_15_bytes_array_BigEndian(this decimal input)
        {
            int[] decArray = decimal.GetBits(input);

            // sign of value
            bool blIsPositive = ((decArray[3] & 0x80000000) == 0);

            // scale size (number of digits in fractal part)
            byte scale = (byte)(decArray[3] >> 16);

            // decimal part - value without decimal point
            decimal decimalValuePart = new decimal(new int[4] { decArray[0], decArray[1], decArray[2], 0 });

            // number of digits in decimal part
            byte numOfDigits = (byte)(Math.Log10((double)decimalValuePart) + 1);

            // is Exponent positive (is abs value > 1)
            bool blIsExpPositive = (numOfDigits > scale);

            // exponent value. If exponent negative, exp value will be round-over as negative: -1=255. -2=254 etc
            byte exp = (byte)(30 + numOfDigits - 1 - scale);
            if (!blIsPositive) exp = (byte)(~exp & 0x3F);

            // last digit for saving in new byte separate (if number is 29 digits long)
            // if 29 digits then remove last digit (as it is stored on lastDigit variable)
            byte lastDigit = 0;
            if (numOfDigits == 29)
            {
                lastDigit = (byte)(decimalValuePart % 10);
                decimalValuePart = Math.Floor(decimalValuePart / 10);
            }

            // if number of digits less than 28 then fill 0-s at the end to get the same size for all values
            if (numOfDigits < 28) decimalValuePart *= (decimal)Math.Pow(10, 28 - numOfDigits);

            // get bits again from New value
            decArray = decimal.GetBits(decimalValuePart);

            byte[] resultArray = new byte[BCNT_DECIMAL];

            // if negative value then need to store number value in inverse
            if (blIsPositive)
            {
                resultArray = new byte[BCNT_DECIMAL] 
                {
                    (byte)(128 + (blIsExpPositive ? 64 : 0) + (exp & 0x3F)),
                    (byte)(decArray[2] >> 24), (byte)(decArray[2] >> 16), (byte)(decArray[2] >> 8), (byte)decArray[2],
                    (byte)(decArray[1] >> 24), (byte)(decArray[1] >> 16), (byte)(decArray[1] >> 8), (byte)decArray[1],
                    (byte)(decArray[0] >> 24), (byte)(decArray[0] >> 16), (byte)(decArray[0] >> 8), (byte)decArray[0],
                    (byte)((lastDigit << 3) + (byte)(scale >> 3)),
                    (byte)((scale << 5) + numOfDigits)
                };
            }
            else
            {
                resultArray = new byte[BCNT_DECIMAL] 
                {
                    (byte)((blIsExpPositive ? 0 : 64) + (exp & 0x3F)),
                    (byte)(~decArray[2] >> 24), (byte)(~decArray[2] >> 16), (byte)(~decArray[2] >> 8), (byte)~decArray[2],
                    (byte)(~decArray[1] >> 24), (byte)(~decArray[1] >> 16), (byte)(~decArray[1] >> 8), (byte)~decArray[1],
                    (byte)(~decArray[0] >> 24), (byte)(~decArray[0] >> 16), (byte)(~decArray[0] >> 8), (byte)~decArray[0],
                    (byte)((~lastDigit << 3) + (byte)(scale >> 3)),
                    (byte)((scale << 5) + numOfDigits)
                };
            }

            return resultArray;
        }

        #endregion

        #region "Decimal ?"
        
        /// <summary>
        /// Converts  decimal? to sortable byte[16] 
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static byte[] To_16_bytes_array_BigEndian(this decimal? input)
        {
            if (input == null)
                return new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            int[] decArray = decimal.GetBits((decimal)input);

            // sign of value
            bool blIsPositive = ((decArray[3] & 0x80000000) == 0);

            // scale size (number of digits in fractal part)
            byte scale = (byte)(decArray[3] >> 16);

            // decimal part - value without decimal point
            decimal decimalValuePart = new decimal(new int[4] { decArray[0], decArray[1], decArray[2], 0 });

            // number of digits in decimal part
            byte numOfDigits = (byte)(Math.Log10((double)decimalValuePart) + 1);

            // is Exponent positive (is abs value > 1)
            bool blIsExpPositive = (numOfDigits > scale);

            // exponent value. If exponent negative, exp value will be round-over as negative: -1=255. -2=254 etc
            byte exp = (byte)(30 + numOfDigits - 1 - scale);
            if (!blIsPositive) exp = (byte)(~exp & 0x3F);

            // last digit for saving in new byte separate (if number is 29 digits long)
            // if 29 digits then remove last digit (as it is stored on lastDigit variable)
            byte lastDigit = 0;
            if (numOfDigits == 29)
            {
                lastDigit = (byte)(decimalValuePart % 10);
                decimalValuePart = Math.Floor(decimalValuePart / 10);
            }

            // if number of digits less than 28 then fill 0-s at the end to get the same size for all values
            if (numOfDigits < 28) decimalValuePart *= (decimal)Math.Pow(10, 28 - numOfDigits);

            // get bits again from New value
            decArray = decimal.GetBits(decimalValuePart);

            //byte[] resultArray = new byte[BCNT_DECIMAL];
            byte[] resultArray = null;

            // if negative value then need to store number value in inverse
            if (blIsPositive)
            {
                resultArray = new byte[16] 
                {
                    1,
                    (byte)(128 + (blIsExpPositive ? 64 : 0) + (exp & 0x3F)),
                    (byte)(decArray[2] >> 24), (byte)(decArray[2] >> 16), (byte)(decArray[2] >> 8), (byte)decArray[2],
                    (byte)(decArray[1] >> 24), (byte)(decArray[1] >> 16), (byte)(decArray[1] >> 8), (byte)decArray[1],
                    (byte)(decArray[0] >> 24), (byte)(decArray[0] >> 16), (byte)(decArray[0] >> 8), (byte)decArray[0],
                    (byte)((lastDigit << 3) + (byte)(scale >> 3)),
                    (byte)((scale << 5) + numOfDigits)
                };
            }
            else
            {
                resultArray = new byte[16] 
                {
                    1,
                    (byte)((blIsExpPositive ? 0 : 64) + (exp & 0x3F)),
                    (byte)(~decArray[2] >> 24), (byte)(~decArray[2] >> 16), (byte)(~decArray[2] >> 8), (byte)~decArray[2],
                    (byte)(~decArray[1] >> 24), (byte)(~decArray[1] >> 16), (byte)(~decArray[1] >> 8), (byte)~decArray[1],
                    (byte)(~decArray[0] >> 24), (byte)(~decArray[0] >> 16), (byte)(~decArray[0] >> 8), (byte)~decArray[0],
                    (byte)((~lastDigit << 3) + (byte)(scale >> 3)),
                    (byte)((scale << 5) + numOfDigits)
                };
            }

            return resultArray;
        }

        #endregion

        #region "Double"

        const short BCNT_DOUBLE = 9;
        const short ENEG_DOUBLE = 324;
        const short EPOS_DOUBLE = 308;

        /// <summary>
        /// Converts  double to sortable byte[9]
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static byte[] To_9_bytes_array_BigEndian(this double input)
        {
            //byte[] resultArray = new byte[BCNT_DOUBLE];
            string[] listOfDoubleParts = Math.Abs(input).ToString("0.000000000000000E+0").Split('E');

            char[] doubleChars = listOfDoubleParts[0].ToCharArray(1, listOfDoubleParts[0].Length - 1);
            doubleChars[0] = listOfDoubleParts[0][0];
            ulong doubleNumber = 0L;
            ulong[] ulongPowerListReverse = new ulong[16] {
                1000000000000000,
                100000000000000,
                10000000000000,
                1000000000000,
                100000000000,
                10000000000,
                1000000000,
                100000000,
                10000000,
                1000000,
                100000,
                10000,
                1000,
                100,
                10,
                1
            };
            for (var i = doubleChars.Length - 1; i >= 0; i--)
            {
                doubleNumber += (ulong)(doubleChars[i] & 0x0F) * ulongPowerListReverse[i];
            }

            Int16 exp = 0;
            doubleChars = listOfDoubleParts[1].ToCharArray(1, listOfDoubleParts[1].Length - 1);
            Int16[] ushortPowerList = new Int16[5] {
                1,
                10,
                100,
                1000,
                10000
            };
            int len = doubleChars.Length - 1;
            for (var i = len; i >= 0; i--)
            {
                exp += (Int16)((doubleChars[i] & 0x0F) * ushortPowerList[len - i]);
            }

            ushort servicePart = 0;

            //Int16 exp = Convert.ToInt16(listOfDoubleParts[1]);
            if (listOfDoubleParts[1][0] == '-') exp = (Int16)(-exp);

            if (input >= 0)
            {
                servicePart = (ushort)(ENEG_DOUBLE + exp + 0x8000);
            }
            else
            {
                servicePart = (ushort)(EPOS_DOUBLE - exp);
                doubleNumber = (ulong)(~doubleNumber);
            }

            byte[] resultArray = new byte[] {
                (byte)(servicePart >> 8),
                (byte)servicePart,
                (byte)(doubleNumber >> 48), 
                (byte)(doubleNumber >> 40), 
                (byte)(doubleNumber >> 32), 
                (byte)(doubleNumber >> 24), 
                (byte)(doubleNumber >> 16), 
                (byte)(doubleNumber >> 8), 
                (byte)doubleNumber
            };

            return resultArray;
        }

        #endregion

        #region "Double ?"

        /// <summary>
        /// Converts double to sortable byte[10]
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static byte[] To_10_bytes_array_BigEndian(this double? input)
        {

            if (input == null)
                return new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0};
                       

            //byte[] resultArray = new byte[BCNT_DOUBLE];
            string[] listOfDoubleParts = Math.Abs((double)input).ToString("0.000000000000000E+0").Split('E');

            char[] doubleChars = listOfDoubleParts[0].ToCharArray(1, listOfDoubleParts[0].Length - 1);
            doubleChars[0] = listOfDoubleParts[0][0];
            ulong doubleNumber = 0L;
            ulong[] ulongPowerListReverse = new ulong[16] {
                1000000000000000,
                100000000000000,
                10000000000000,
                1000000000000,
                100000000000,
                10000000000,
                1000000000,
                100000000,
                10000000,
                1000000,
                100000,
                10000,
                1000,
                100,
                10,
                1
            };
            for (var i = doubleChars.Length - 1; i >= 0; i--)
            {
                doubleNumber += (ulong)(doubleChars[i] & 0x0F) * ulongPowerListReverse[i];
            }

            Int16 exp = 0;
            doubleChars = listOfDoubleParts[1].ToCharArray(1, listOfDoubleParts[1].Length - 1);
            Int16[] ushortPowerList = new Int16[5] {
                1,
                10,
                100,
                1000,
                10000
            };
            int len = doubleChars.Length - 1;
            for (var i = len; i >= 0; i--)
            {
                exp += (Int16)((doubleChars[i] & 0x0F) * ushortPowerList[len - i]);
            }

            ushort servicePart = 0;

            //Int16 exp = Convert.ToInt16(listOfDoubleParts[1]);
            if (listOfDoubleParts[1][0] == '-') exp = (Int16)(-exp);

            if (input >= 0)
            {
                servicePart = (ushort)(ENEG_DOUBLE + exp + 0x8000);
            }
            else
            {
                servicePart = (ushort)(EPOS_DOUBLE - exp);
                doubleNumber = (ulong)(~doubleNumber);
            }

            byte[] resultArray = new byte[] {
                1,
                (byte)(servicePart >> 8),
                (byte)servicePart,
                (byte)(doubleNumber >> 48), 
                (byte)(doubleNumber >> 40), 
                (byte)(doubleNumber >> 32), 
                (byte)(doubleNumber >> 24), 
                (byte)(doubleNumber >> 16), 
                (byte)(doubleNumber >> 8), 
                (byte)doubleNumber
            };

            return resultArray;
        }

        #endregion

        #region "Float"

        const short ENEG_FLOAT = 45;
        const short EPOS_FLOAT = 38;
        const short SDIG_FLOAT = 7;
        const short BCNT_FLOAT = 4;

        /// <summary>
        ///  Converts float to sortable byte[4]
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static byte[] To_4_bytes_array_BigEndian(this float input)
        {
            byte[] resultArray = new byte[BCNT_FLOAT];

            string[] listOfFloatParts = Math.Abs(input).ToString("0.000000E+0").Split('E');

            char[] floatChars = listOfFloatParts[0].ToCharArray(1, listOfFloatParts[0].Length - 1);
            floatChars[0] = listOfFloatParts[0][0];
            uint floatNumber = 0;
            uint[] uintPowerListReverse = new uint[7] {
                1000000,
                100000,
                10000,
                1000,
                100,
                10,
                1
            };
            for (var i = floatChars.Length - 1; i >= 0; i--)
            {
                floatNumber += (uint)(floatChars[i] & 0x0F) * uintPowerListReverse[i];
            }

            Int16 exp = 0;
            floatChars = listOfFloatParts[1].ToCharArray(1, listOfFloatParts[1].Length - 1);
            Int16[] ushortPowerList = new Int16[5] {
                1,
                10,
                100,
                1000,
                10000
            };
            int len = floatChars.Length - 1;
            for (var i = len; i >= 0; i--)
            {
                exp += (Int16)((floatChars[i] & 0x0F) * ushortPowerList[len - i]);
            }

            ushort servicePart = 0;

            if (listOfFloatParts[1][0] == '-') exp = (Int16)(-exp);

            if (input >= 0)
            {
                servicePart = (ushort)(ENEG_FLOAT + exp + 0x80);
            }
            else
            {
                servicePart = (ushort)(EPOS_FLOAT - exp);
                floatNumber = (uint)(~floatNumber);
            }

            resultArray = new byte[] {
                (byte)servicePart,
                (byte)(floatNumber >> 16), 
                (byte)(floatNumber >> 8), 
                (byte)floatNumber
            };

            return resultArray;
        }

        #endregion

        #region "Float ?"

      
        /// <summary>
        ///  Converts float? to sortable byte[5]
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static byte[] To_5_bytes_array_BigEndian(this float? input)
        {
            if (input == null)
                return new byte[] { 0, 0, 0, 0, 0 };

            byte[] resultArray = null;

            string[] listOfFloatParts = Math.Abs((float)input).ToString("0.000000E+0").Split('E');

            char[] floatChars = listOfFloatParts[0].ToCharArray(1, listOfFloatParts[0].Length - 1);
            floatChars[0] = listOfFloatParts[0][0];
            uint floatNumber = 0;
            uint[] uintPowerListReverse = new uint[7] {
                1000000,
                100000,
                10000,
                1000,
                100,
                10,
                1
            };
            for (var i = floatChars.Length - 1; i >= 0; i--)
            {
                floatNumber += (uint)(floatChars[i] & 0x0F) * uintPowerListReverse[i];
            }

            Int16 exp = 0;
            floatChars = listOfFloatParts[1].ToCharArray(1, listOfFloatParts[1].Length - 1);
            Int16[] ushortPowerList = new Int16[5] {
                1,
                10,
                100,
                1000,
                10000
            };
            int len = floatChars.Length - 1;
            for (var i = len; i >= 0; i--)
            {
                exp += (Int16)((floatChars[i] & 0x0F) * ushortPowerList[len - i]);
            }

            ushort servicePart = 0;

            if (listOfFloatParts[1][0] == '-') exp = (Int16)(-exp);

            if (input >= 0)
            {
                servicePart = (ushort)(ENEG_FLOAT + exp + 0x80);
            }
            else
            {
                servicePart = (ushort)(EPOS_FLOAT - exp);
                floatNumber = (uint)(~floatNumber);
            }

            resultArray = new byte[] {
                1,
                (byte)servicePart,
                (byte)(floatNumber >> 16), 
                (byte)(floatNumber >> 8), 
                (byte)floatNumber
            };

            return resultArray;
        }

        #endregion

        #endregion //End of others to byte[]

        /// <summary>
        /// Truncates UTF-8 strign up to special maxSizeInBytes due to UTF-8 specification. 
        /// </summary>
        /// <param name="text"></param>
        /// <param name="maxSizeInBytes"></param>
        /// <returns></returns>
        private static byte[] TruncateUTF8(string text, int maxSizeInBytes)
        {
            if (text == null)
                return null;

            byte[] bt = System.Text.Encoding.UTF8.GetBytes(text);

            if (bt.Length <= maxSizeInBytes)
                return bt;

            //Last byte is represented with 1 byte (ASCII range character)
            if (bt[maxSizeInBytes - 1] < 128)
                return bt.Substring(0, maxSizeInBytes);

            int toRemove = 0;

            //computing how much to remove
            for (int i = maxSizeInBytes - 1; i >= 0; i--)
            {
                toRemove++;

                if ((bt[i] & 64) == 64)
                {
                    //Calculating quantity of higher bits
                    int qb = 2;
                    int b = 0x20;

                    for (int j = 1; j < 5; j++)
                    {
                        if ((bt[i] & b) == b)
                        {
                            qb++;
                            b >>= 1;
                        }
                        else
                            break;
                    }


                    if (toRemove == qb)
                        toRemove = 0;

                    break;
                }
            }
            return bt.Substring(0, maxSizeInBytes - toRemove);
        }

        #region "DB columns compatible strings"
        
                

        /// <summary>
        /// Converts your text into byte[], which can be used as column of fixedSize+2. 
        /// <para>(2 bytes are always added to your fixedSize value, determination of actual text size and NULL flag)</para>
        /// <para>String can be null</para>
        /// Will return byte[] with the length fixedSize+2 which can be stored as column.
        /// <para>If text.Length after convertion (using ASCII or UTF8) overexceeds fixedSize, text will be truncated</para>
        /// </summary>
        /// <param name="value">any string, can be NULL</param>
        /// <param name="fixedSize">reservation space(returned byte[] will be of fixedSize+2)</param>
        /// <param name="isASCII">if true, text will be presented as ASCII, otherwise as UTF-8</param>
        /// <returns></returns>
        public static byte[] To_FixedSizeColumn(this string value, short fixedSize, bool isASCII)
        {
            if (fixedSize < 4)
            {
                if (isASCII && fixedSize < 1)
                {
                    throw new Exception("Fixed Size must be minimum 1");
                }
                else
                    throw new Exception("Fixed Size must be minimum 4 for UTF-8 text");
               
            }
            if (value == null)
                return UInt16.MaxValue.To_2_bytes_array_BigEndian().EnlargeByteArray_LittleEndian(fixedSize + 2);
            
            byte[] text=null;

            if (isASCII)
                text = System.Text.Encoding.ASCII.GetBytes(value);
            else
                text = System.Text.Encoding.UTF8.GetBytes(value);

            if(text.Length>fixedSize)
            {
                //Truncating Text

                if (isASCII)
                {
                    text = text.Substring(0, fixedSize);
                }
                else
                {
                    //Truncating UTF-8 text
                    text = TruncateUTF8(value, fixedSize);
                }               
                
            }

            return ((ushort)text.Length).To_2_bytes_array_BigEndian().Concat(text).EnlargeByteArray_LittleEndian(fixedSize + 2);
        }

      
        /// <summary>
        /// takes byte[] created by To_FixedSizeColumn and restores string value from it.
        /// <para>byte[] must be of length fixedSize(which you gave in To_FixedSizeColumn) + 2</para>
        /// </summary>
        /// <param name="value"></param>
        /// <param name="isASCII">if true, text was presented as ASCII, otherwise as UTF-8</param>
        /// <returns></returns>
        public static string From_FixedSizeColumn(this byte[] value, bool isASCII)
        {
            if (value == null || value.Length < 2)
                return null;

            ushort size = (new byte[] { value[0], value[1] }).To_UInt16_BigEndian();

            if (size == UInt16.MaxValue)
                return null;

            if (isASCII)
                return System.Text.Encoding.ASCII.GetString(value.Substring(2, (int)size));
            else
                return System.Text.Encoding.UTF8.GetString(value.Substring(2, (int)size));
        }

        #endregion


        #region "Bytes To String"

        /// <summary>
        /// Creates a Base64string from byte array. Good for hashes.
        /// </summary>
        /// <param name="dBytes"></param>
        /// <returns></returns>
        public static string ToBase64String(this byte[] dBytes)
        {
            return System.Convert.ToBase64String(dBytes);
        }

        /// <summary>
        /// Converts BytesArray to String Representation: 00-00-00-00-1F-00-00-00-00-20.
        /// If array is null or 0 length - returns String.Empty.
        /// If replaceWith is String.Empty returns such view 00-00-00-00-1F-00-00-00-00-20.
        /// Otherwise takes such view (-00-00-00-00-1F-00-00-00-00-20) and replaces "-" with replaceWith also calls Trim().      
        /// </summary>
        /// <param name="dBytes"></param>
        /// <param name="replaceWith"></param>
        /// <returns></returns>
        public static string ToBytesString(this byte[] dBytes, string replaceWith)
        {
            if (dBytes == null)
                return String.Empty;
            if (dBytes.Count() == 0)
                return String.Empty;
            
           
            if (replaceWith == String.Empty)
                return BitConverter.ToString(dBytes);

            return ("-" + BitConverter.ToString(dBytes)).Replace("-", replaceWith).Trim();
        }

        /// <summary>
        /// Used by ToBytesString
        /// </summary>
        static readonly char[] _hexDigits = "0123456789ABCDEF".ToCharArray();

        /// <summary>
        /// Generates byte[] from given Hex 1F0000000020. Backward function is ToHexFromByteArray
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static byte[] ToByteArrayFromHex(this string str)
        {
            if (String.IsNullOrEmpty(str))
                return null;

            byte[] tr = new byte[str.Length / 2];
            int j = 0;
            int d = 0;
            for (int i = 0; i < str.Length; i += 2)
            {
                d = str[i] - 48;
                d = d > 9 ? d - 7 : d;
                tr[j] = (byte)(d * 16);
                d = str[i + 1] - 48;
                d = d > 9 ? d - 7 : d;
                tr[j] += (byte)d;
                j++;
            }
            return tr;
        }

        /// <summary>
        /// Generates Hex 1F0000000020 from byte[]. Backward function is ToByteArrayFromHex/ToByteArrayFromHex
        /// </summary>
        /// <param name="dBytes"></param>
        /// <returns></returns>
        public static string ToHexFromByteArray(this byte[] dBytes)
        {
            return dBytes.ToBytesString();
        }

        /// <summary>
        /// To pure HEX string without delimiters
        /// </summary>
        /// <param name="dBytes"></param>
        /// <returns></returns>
        public static string ToBytesString(this byte[] dBytes)
        {
            if (dBytes == null || dBytes.Length == 0)
                return String.Empty;


            ////fastest performance (3.2 x faster then BitConverter) needs _hexDigits
            char[] digits = new char[dBytes.Length * 2];
            int d1, d2;
            for (int i = 0; i < dBytes.Length; i++)
            {
                d2 = dBytes[i] % 16;
                d1 = dBytes[i] / 16;               
                digits[2 * i] = _hexDigits[d1];
                digits[2 * i + 1] = _hexDigits[d2];
            }
            return new string(digits);

            //TEsting code
            //byte[] bt = new byte[1000];
            //Random rnd = new Random();
            //rnd.NextBytes(bt);

            //for (int i = 0; i < 100000; i++)
            //{
            //    bt.ToBytesString();

            //    //Console.WriteLine(b.ToBytesString());
            //}

            //and

            //byte[] b = new byte[1];
            //for (int i = 0; i < 256; i++)
            //{
            //    b[0] = (byte)i;

            //    Console.WriteLine(b.ToBytesString());
            //}



            //////fastest performance (3x faster then BitConverter) needs _hexDigits
            //char[] digits = new char[dBytes.Length * 2];
            //for (int i = 0; i < dBytes.Length; i++)
            //{
            //    int d1, d2;
            //    d1 = Math.DivRem(dBytes[i], 16, out d2);
            //    digits[2 * i] = _hexDigits[d1];
            //    digits[2 * i + 1] = _hexDigits[d2];
            //}
            //return new string(digits);


            ////fastest performance (2x faster then BitConverter)            
            //char[] c=new char[dBytes.Length*2];
         
            //byte b;
            //for (int i = 0; i < dBytes.Length; i++)
            //{
            //    b = ((byte)(dBytes[i] >> 4));
            //    c[i * 2] = (char)(b > 9 ? b + 0x37 : b + 0x30);
            //    b = ((byte)(dBytes[i] & 0xF));
            //    c[i * 2 + 1] = (char)(b > 9 ? b + 0x37 : b + 0x30);
            //}
            
            //return new string(c);




            //return BitConverter.ToString(dBytes).Replace("-", String.Empty);


            //StringBuilder sb = new StringBuilder();
            //foreach (byte b in dBytes)
            //    sb.Append(b.ToString("X2"));

            //return sb.ToString();

            //StringBuilder sb = new StringBuilder();
            //foreach(var b in dBytes)
            //    sb.Append(BitConverter.ToString(new byte[] {b}));

            //return sb.ToString();

            //bad performance
            //return System.Text.RegularExpressions.Regex.Replace(BitConverter.ToString(dBytes), "-", "");


        }

        /// <summary>
        /// Convert Byte To Hex string
        /// </summary>
        /// <param name="dByte"></param>
        /// <returns></returns>
        public static string ToHex(this byte dByte)
        {
            //if (dByte == null)
            //    return String.Empty;

            return BitConverter.ToString(new byte[] { dByte });
        }

        /// <summary>
        /// Converts BytesArray to String Representation: 00-00-00-00-128-12-214-00-00-20.
        /// Where replaceWith = "-"
        /// </summary>
        /// <param name="dBytes"></param>
        /// <param name="replaceWith"></param>
        /// <returns></returns>
        public static string ToBytesStringDec(this byte[] dBytes, string replaceWith)
        {
            if (dBytes == null || dBytes.Length == 0)
                return String.Empty;

            if (replaceWith == String.Empty)
                replaceWith = "-";

            StringBuilder sb = new StringBuilder();
            foreach (var bt in dBytes)
            {
                sb.Append(bt.ToString() + replaceWith);
            }

            if (sb.Length > 0)
                return sb.ToString().Substring(0, sb.Length - replaceWith.Length);
            else
                return String.Empty;
        }

        public static string ToAsciiString(this byte[] dBytes)
        {
            return (dBytes == null) ? String.Empty : System.Text.Encoding.ASCII.GetString(dBytes);
        }

        public static string ToUTF8String(this byte[] dBytes)
        {
            return (dBytes == null) ? String.Empty : System.Text.Encoding.UTF8.GetString(dBytes);
        }

        public static string ToUnicodeString(this byte[] dBytes)
        {
            return (dBytes == null) ? String.Empty : System.Text.Encoding.Unicode.GetString(dBytes);
        }

        #endregion

        #region "Byte to bits"

        /// <summary>
        /// BigEndian
        /// </summary>
        /// <param name="bt"></param>
        /// <returns></returns>
        public static byte[] ToBitArray(this byte bt)
        {
              //255 111 111 11
              //128 100 100 00  8 bit
              //127 111 111 1  
              //64  100 000 0   7 bit
              //63  111 111 
              //32  100 000     6 bit
              //31  111 11
              //16  100 00      5 bit
              //15  111 1
              // 8  100 0       4 bit
              // 7  111
              // 4  100         3 bit
              // 3  11
              // 2  10          2 bit
              // 1  1           1 bit
              // 0  0           

            byte[] ret = new byte[8];          


            ret[0] = (byte)((bt >> 7) & 1);
            ret[1] = (byte)((bt >> 6) & 1);
            ret[2] = (byte)((bt >> 5) & 1);
            ret[3] = (byte)((bt >> 4) & 1);
            ret[4] = (byte)((bt >> 3) & 1);
            ret[5] = (byte)((bt >> 2) & 1);
            ret[6] = (byte)((bt >> 1) & 1);
            ret[7] = (byte)(bt & 1);

            return ret;
        }

        #endregion

        #region "CRC16"

        /// <summary>
        /// Returns byte representation of Crc16
        /// </summary>
        /// <param name="ar"></param>
        /// <returns></returns>
        public static byte[] Get_CRC16_AsByteArray(this byte[] ar)
        {
            return Crc16.ComputeChecksumBytes(ar);
        }


        ///// <summary>
        ///// Returns ushort representation of Crc16
        ///// </summary>
        ///// <param name="ar"></param>
        ///// <returns></returns>
        //public static ushort ToCrc16ushort(this byte[] ar)
        //{
        //    return Crc16.ComputeChecksum(ar);
        //}

        private static class Crc16
        {
            const ushort polynomial = 0xA001;
            private static bool IsInitialized = false;
            private static ushort[] table = new ushort[256];
            private static object lock_IsInitialized = new object();

            public static ushort ComputeChecksum(byte[] bytes)
            {
                if (!Crc16.IsInitialized)
                {
                    lock (lock_IsInitialized)
                    {
                        if (!Crc16.IsInitialized)
                            Crc16.InitializeMe();
                    }
                }

                ushort crc = 0;
                for (int i = 0; i < bytes.Length; ++i)
                {
                    byte index = (byte)(crc ^ bytes[i]);
                    crc = (ushort)((crc >> 8) ^ Crc16.table[index]);
                }
                return crc;
            }

            public static byte[] ComputeChecksumBytes(byte[] bytes)
            {
                ushort crc = ComputeChecksum(bytes);
                return new byte[] { (byte)(crc >> 8), (byte)(crc & 0x00ff) };
            }


            private static void InitializeMe()
            {
                ushort value;
                ushort temp;
                for (ushort i = 0; i < table.Length; ++i)
                {
                    value = 0;
                    temp = i;
                    for (byte j = 0; j < 8; ++j)
                    {
                        if (((value ^ temp) & 0x0001) != 0)
                        {
                            value = (ushort)((value >> 1) ^ polynomial);
                        }
                        else
                        {
                            value >>= 1;
                        }
                        temp >>= 1;
                    }
                    Crc16.table[i] = value;
                }


                Crc16.IsInitialized = true;
            }
        }

        #endregion


        #region "Extra Manipilations"
        /// <summary>
        /// Adds byte[] + 1 bit.
        /// Returns: had {255}    -> null
        /// Returns: had {15,255} -> {16,0} 
        /// Returns: had {15,248} -> {15,249} 
        /// Returns: bt=null || bt.Length == 0 -> null
        /// </summary>
        /// <param name="bt"></param>
        /// <returns></returns>
        public static byte[] BytesAction_GoOneBitUp_NoArrayGrow_BigEndian(this byte[] bt)
        {
            byte[] ret = null;

            if (bt == null || bt.Length == 0)
                return null;

            ret = new byte[bt.Length];

            bool toAdd = true;

            for (int i = bt.Length - 1; i >= 0; i--)
            {
                /* OPTIMIZATION CHECK LATER
                if (bt[0] == 255)
                {
                    if (toAdd)
                    {
                        if (i == 0)
                        {
                            return null;
                        }
                        else
                        {
                            ret[i] = 0;
                            toAdd = true;
                        }
                    }
                    else
                    {
                        ret[i] = bt[i];
                    }
                }
                else
                {
                    if (toAdd)
                    {
                        toAdd = false;
                        ret[i] = (byte)(bt[i] + 1);
                    }
                    else
                        ret[i] = bt[i];
                }
                 */


                if (i == 0 && toAdd && bt[0] == 255)
                    return null;

                if (toAdd && bt[i] == 255)
                {
                    ret[i] = 0;
                    toAdd = true;
                }
                else if (toAdd && bt[i] < 255)
                {
                    toAdd = false;
                    ret[i] = (byte)(bt[i] + 1);
                }
                else
                    ret[i] = bt[i];

            }

            return ret;
        }


        ///// <summary>
        ///// O N_COUNT_TEST
        ///// </summary>
        ///// <param name="key"></param>
        ///// <returns></returns>
        //public static byte[] BytesAction_GoOneBitUp_ArrayGrows_BigEndian(this byte[] bt)
        //{
        //    byte[] ret;

        //    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        //    sw.Start();
        //    ret = BytesAction_GoOneBitUp_ArrayGrows_BigEndian1(bt);
        //    sw.Stop();

        //    Performance.StatisticalVariables.BytesAction_GoOneBitUp_ArrayGrows_BigEndian += sw.ElapsedTicks;
        //    Performance.StatisticalVariables.BytesAction_GoOneBitUp_ArrayGrows_BigEndian_CNT++;

        //    return ret;
        //}

        /// <summary>
        /// Adds + 1 bit
        /// The same as BytesAction_GoOneBitUp_NoArrayGrow_BigEndian but array grows
        /// </summary>
        /// <param name="bt"></param>
        /// <returns></returns>
        public static byte[] BytesAction_GoOneBitUp_ArrayGrows_BigEndian(this byte[] bt)
        {
            byte[] ret = null;

            if (bt == null || bt.Length == 0)
                return null;

            ret = new byte[bt.Length];

            bool toAdd = true;

            for (int i = bt.Length - 1; i >= 0; i--)
            {
                /* OPTIMIZATION CHECK LATER
                 * BETTER START if(toAdd) etc, look on bitDown
                 * 
                if (bt[0] == 255)
                {
                    if (toAdd)
                    {
                        if (i == 0)
                        {
                            ret[0] = 0;
                            return new byte[] { 1 }.Concat(ret);
                        }
                        else
                        {
                            ret[i] = 0;
                            toAdd = true;
                        }
                    }
                    else
                    {
                        ret[i] = bt[i];
                    }
                }
                else
                {
                    if (toAdd)
                    {
                        toAdd = false;
                        ret[i] = (byte)(bt[i] + 1);
                    }
                    else
                        ret[i] = bt[i];
                }
                 */


                if (i == 0 && toAdd && bt[0] == 255)
                {
                    ret[0] = 0;
                    return new byte[] { 1 }.Concat(ret);
                }


                if (toAdd && bt[i] == 255)
                {
                    ret[i] = 0;
                    toAdd = true;
                }
                else if (toAdd && bt[i] < 255)
                {
                    toAdd = false;
                    ret[i] = (byte)(bt[i] + 1);
                }
                else
                    ret[i] = bt[i];

            }

            return ret;
        }

        /// <summary>
        /// Extracts 1 bit
        /// Returns: {0} -> null
        /// Returns: {0,0,0,0} -> null
        /// Returns: {254} -> {253}
        /// Returns: {1} -> {0}
        /// Returns: {121,456} -> {121,455}
        /// Returns: {121,0} -> {120,255}
        /// </summary>
        /// <param name="bt"></param>
        /// <returns></returns>
        public static byte[] BytesAction_GoOneBitDown_NoArrayGrow_BigEndian(this byte[] bt)
        {
            byte[] ret = null;

            if (bt == null || bt.Length == 0)
                return null;

            int btLen = bt.Length;

            bt = bt.RemoveLeadingElement(0);

            if (bt.Length == 0)
                return null;

            if (bt.Length == 1 && bt[0] == 0)
                return null;

            ret = new byte[btLen];

            bool toExtract = true;


            for (int i = bt.Length - 1; i >= 0; i--)
            {
                if (toExtract)
                {
                    if (i == 0 && bt[i] == 0)
                        return null;

                    if (bt[i] == 0)
                    {
                        ret[i] = 255;
                    }
                    else
                    {
                        toExtract = false;
                        ret[i] = (byte)(bt[i] - 1);
                    }
                }
                else
                {
                    ret[i] = bt[i];
                }

            }

            return ret;
        }

        ///// <summary>
        ///// O N_COUNT_TEST
        ///// </summary>
        ///// <param name="key"></param>
        ///// <returns></returns>
        //public static byte[] BytesAction_GoUpNextByteStart_NoArrayGrow_BigEndian(this byte[] bt, int index)
        //{
        //    byte[] ret;

        //    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        //    sw.Start();
        //    ret = BytesAction_GoUpNextByteStart_NoArrayGrow_BigEndian1(bt,index);
        //    sw.Stop();

        //    Performance.StatisticalVariables.BytesAction_GoUpNextByteStart_NoArrayGrow_BigEndian += sw.ElapsedTicks;
        //    Performance.StatisticalVariables.BytesAction_GoUpNextByteStart_NoArrayGrow_BigEndian_CNT++;

        //    return ret;
        //}

        /// <summary>
        /// <para>BytesAction_GoUpNextByteStart_NoArrayGrow_BigEndian</para>
        /// <para>Returns: had {255}    -> null</para>
        /// <para>Returns: had {255, 0} -> null</para>
        /// <para>Returns: bt=null || bt.Length less then 2  -> null</para>
        /// <para>Returns: had {254, 0} -> {255, 0}</para>
        /// <para>Returns: had {120, 115, 147} -> {120, 116, 0}</para>
        /// </summary>
        /// <param name="bt"></param>
        /// <returns></returns>
        public static byte[] BytesAction_GoUpNextByteStart_NoArrayGrow_BigEndian(this byte[] bt, int index)
        {
            byte[] ret = null;

            if (bt == null || bt.Length < 2)
                return null;

            if (index == 0 || index >= bt.Length)
                index = bt.Length - 1;

            ret = new byte[bt.Length];

            bool toAdd = true;

            for (int i = bt.Length - 1; i >= 0; i--)
            {
                if (i == 0 && toAdd && bt[0] == 255)
                    return null;

                if (i >= index)
                {
                    ret[i] = 0;
                }
                else
                {
                    if (toAdd && bt[i] == 255)
                    {
                        ret[i] = 0;
                        toAdd = true;
                    }
                    else if (toAdd && bt[i] < 255)
                    {
                        toAdd = false;
                        ret[i] = (byte)(bt[i] + 1);
                    }
                    else
                        ret[i] = bt[i];
                }

            }

            return ret;
        }

        /// <summary>
        /// BytesAction_GoDownNextByteStart_NoArrayGrow_BigEndian</para>
        /// <para>Returns: had {0} or {254} or {255}  -> null</para>
        /// <para>Returns:{0,124}  -> null</para>
        /// <para>Returns:{12,124}  -> {11,255}</para>
        /// </summary>
        /// <param name="bt"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public static byte[] BytesAction_GoDownNextByteStart_NoArrayGrow_BigEndian(this byte[] bt, int index)
        {
            byte[] ret = null;

            if (bt == null || bt.Length < 2)
                return null;

            if (index == 0 || index >= bt.Length)
                index = bt.Length - 1;

            ret = new byte[bt.Length];

            bool toExtract = true;

            for (int i = bt.Length - 1; i >= 0; i--)
            {
                if (i == 0 && toExtract && bt[0] == 0)
                    return null;

                if (i >= index)
                {
                    ret[i] = 255;
                }
                else
                {
                    if (toExtract && bt[i] == 0)
                    {
                        ret[i] = 255;
                        toExtract = true;
                    }
                    else if (toExtract && bt[i] > 0)
                    {
                        toExtract = false;
                        ret[i] = (byte)(bt[i] - 1);
                    }
                    else
                        ret[i] = bt[i];
                }

            }

            return ret;
        }


        ///// <summary>
        ///// Extracts byte[] - 1. BytesAction_GoOneBitDown_NoArrayGrow_BigEndian
        ///// Returns: had {255}    -> 254
        ///// Returns: had {0}    -> null
        ///// Returns: had {15,0} -> {14,255} 
        ///// Returns: had {15,248} -> {15,247} 
        ///// Returns: bt=null || bt.Length == 0 -> null
        ///// </summary>
        ///// <param name="bt"></param>
        ///// <returns></returns>
        //public static byte[] BytesAction_GoOneBitDown_NoArrayGrow_BigEndian(this byte[] bt)
        //{
        //    byte[] ret = null;

        //    if (bt == null || bt.Length == 0)
        //        return null;

        //    ret = new byte[bt.Length];

        //    bool toExtract = true;

        //    for (int i = bt.Length - 1; i >= 0; i--)
        //    {
        //        if (i == 0 && toExtract && bt[0] == 0)
        //            return null;

        //        if (toExtract && bt[i] == 0)
        //        {
        //            ret[i] = 255;
        //            toExtract = true;
        //        }
        //        else if (toExtract && bt[i] > 0)
        //        {
        //            toExtract = false;
        //            ret[i] = (byte)(bt[i] - 1);
        //        }
        //        else
        //            ret[i] = bt[i];

        //    }

        //    return ret;
        //}
        #endregion

        #region "EmptyPointerCheck"

        public static bool _IfDynamicDataPointerIsEmpty(this byte[] initPtr)
        {
            if (initPtr == null || initPtr.Length != 16)   //8 bytes pointer + 4 bytes DataBlockSize + 4 bytes Length
                return true;

            return (initPtr[0] | initPtr[1] | initPtr[2] | initPtr[3] | initPtr[4] | initPtr[5] | initPtr[6] | initPtr[7]) == 0;
        }

        public static bool _IfPointerIsEmpty(this byte[] ptr, ushort DefaultPointerLen)
        {
            if (ptr == null || ptr.Length < DefaultPointerLen)
                return true;

            switch (DefaultPointerLen)
            {
                case 5:     //Gives ability to allocate file up to 1 terrabyte (1.099.511.627.775)
                    return (ptr[0] | ptr[1] | ptr[2] | ptr[3] | ptr[4]) == 0;
                case 8:     //UINT64.Max
                    return (ptr[0] | ptr[1] | ptr[2] | ptr[3] | ptr[4] | ptr[5] | ptr[6] | ptr[7]) == 0;
                case 4:     //4GB
                    return (ptr[0] | ptr[1] | ptr[2] | ptr[3]) == 0;
                case 3:     //17MB
                    return (ptr[0] | ptr[1] | ptr[2]) == 0;
                case 6:     //281 Terrabytes (281.474.976.710.655)
                    return (ptr[0] | ptr[1] | ptr[2] | ptr[3] | ptr[4] | ptr[5]) == 0;
                case 7:      //72 Petabytes (72.057.594.037.927.935)
                    return (ptr[0] | ptr[1] | ptr[2] | ptr[3] | ptr[4] | ptr[5] | ptr[6]) == 0;
                case 2:      //65 KB
                    return (ptr[0] | ptr[1]) == 0;
                default:
                    return ptr._ByteArrayEquals(new byte[DefaultPointerLen]);
            }
        }

        //public static bool _IfDynamicDataPointerIsEmpty(this byte[] initPtr)
        //{
        //    if (initPtr == null || initPtr.Length != 16)   //8 bytes pointer + 4 bytes DataBlockSize + 4 bytes Length
        //        return true;

        //    return !(
        //                   initPtr[7] != 0
        //                   ||
        //                   initPtr[6] != 0
        //                   ||
        //                   initPtr[5] != 0
        //                   ||
        //                   initPtr[4] != 0
        //                   ||
        //                   initPtr[3] != 0
        //                   ||
        //                   initPtr[2] != 0
        //                   ||
        //                   initPtr[1] != 0
        //                   ||
        //                   initPtr[0] != 0
        //                   );

        //}

        ///// <summary>
        ///// Checks if byte array contains all nulls then true
        ///// </summary>
        ///// <param name="ptr"></param>
        ///// <param name="DefaultEmptyPointer"></param>
        ///// <returns></returns>
        //public static bool _IfPointerIsEmpty(this byte[] ptr, ushort DefaultPointerLen)
        //{
        //    if (ptr == null || ptr.Length < DefaultPointerLen)
        //        return true;

        //    //Executes 52 ms
        //    #region "Settign up delegate"
        //    switch (DefaultPointerLen)
        //    {
        //        case 5:     //Gives ability to allocate file up to 1 terrabyte (1.099.511.627.775)
        //            return !(
        //                   ptr[4] != 0
        //                   ||
        //                   ptr[3] != 0
        //                   ||
        //                   ptr[2] != 0
        //                   ||
        //                   ptr[1] != 0
        //                   ||
        //                   ptr[0] != 0
        //                   );
        //        case 4:     //4GB
        //            return !(
        //                  ptr[3] != 0
        //                  ||
        //                  ptr[2] != 0
        //                  ||
        //                  ptr[1] != 0
        //                  ||
        //                  ptr[0] != 0
        //                  );

        //        case 3:     //17MB
        //            return !(
        //                  ptr[2] != 0
        //                  ||
        //                  ptr[1] != 0
        //                  ||
        //                  ptr[0] != 0
        //                  );

        //        case 6:     //281 Terrabytes (281.474.976.710.655)
        //            return !(
        //                   ptr[5] != 0
        //                   ||
        //                   ptr[4] != 0
        //                   ||
        //                   ptr[3] != 0
        //                   ||
        //                   ptr[2] != 0
        //                   ||
        //                   ptr[1] != 0
        //                   ||
        //                   ptr[0] != 0
        //                   );

        //        case 7:     //72 Petabytes (72.057.594.037.927.935)
        //            return !(
        //                   ptr[6] != 0
        //                   ||
        //                   ptr[5] != 0
        //                   ||
        //                   ptr[4] != 0
        //                   ||
        //                   ptr[3] != 0
        //                   ||
        //                   ptr[2] != 0
        //                   ||
        //                   ptr[1] != 0
        //                   ||
        //                   ptr[0] != 0
        //                   );
        //        case 2:   //65 KB
        //            return !(
        //                  ptr[1] != 0
        //                  ||
        //                  ptr[0] != 0
        //                  );
        //        default:
        //            return ptr._ByteArrayEquals(new byte[DefaultPointerLen]);

        //    }

        //    #endregion
        //}
        #endregion


        #region "Array Compare"

        /// <summary>
        /// <para>USE _ByteArrayEquals</para>
        /// If both arrays are null, returns true. Checks nulls also. Uses SequenceEqual
        /// </summary>
        /// <param name="ar"></param>
        /// <param name="compareArray"></param>
        /// <returns></returns>
        public static bool _Equals(this byte[] ar, byte[] compareArray)
        {
            if (ar == null && compareArray == null)
                return true;

            if (ar == null)
                return false;

            if (compareArray == null)
                return false;

            if (ar.Length != compareArray.Length)
                return false;

            return ar.SequenceEqual(compareArray);
        }

        /// <summary>
        /// Managed way compares 2 bytes array. Uses for loop and extra checks like null, length before
        /// </summary>
        /// <param name="b1"></param>
        /// <param name="b2"></param>
        /// <returns></returns>
        public static bool _ByteArrayEquals(this byte[] b1, byte[] b2)
        {
            //Works correctly
            if (b1 == b2) return true;      //if both arrays are null returns true, if byte arrays have same content returns false, cause checking instances
            if (b1 == null || b2 == null) return false;
            if (b1.Length != b2.Length) return false;
            for (int i = 0; i < b1.Length; i++)
            {
                if (b1[i] != b2[i]) return false;
            }
            return true;

            ///////////////////////////////////////////////////////////unmanaged way
            //if (b1 == b2) return true;
            //if (b1 == null || b2 == null) return false;
            //unsafe
            //{
            //    if (b1.Length != b2.Length)
            //        return false;

            //    int n = b1.Length;

            //    fixed (byte* p1 = b1, p2 = b2)
            //    {
            //        byte* ptr1 = p1;
            //        byte* ptr2 = p2;

            //        while (n-- > 0)
            //        {
            //            if (*ptr1++ != *ptr2++)
            //                return false;
            //        }
            //    }

            //    return true;
            //}
            ////////////////////////////////////////////////////////////////////////

        }
              

        /// <summary>
        /// Returns index where equality is broken.
        /// -2 if equal
        /// -1 if not comparable (null or so)
        /// </summary>
        /// <param name="b1"></param>
        /// <param name="b2"></param>
        /// <returns></returns>
        public static int _ByteArrayEquals_EqualityBrokenIndex(this byte[] b1, byte[] b2)
        {
            //Works correctly
            // if (b1 == b2) return -1;      //if both arrays are null returns true, if byte arrays have same content returns false, cause checking instances
            if (b1 == null || b2 == null) return -1;
            //if (b1.Length != b2.Length) return false;
            for (int i = 0; i < b1.Length; i++)
            {
                if (b1[i] != b2[i]) return i;
            }
            return -2;
        }




        /// <summary>
        /// String Comparation Point of view:
        /// "AAA" less then "AAAA"
        /// "AB" more then "AAAA"
        /// </summary>
        /// <param name="array"></param>
        /// <param name="arrayToCompare"></param>
        /// <returns></returns>
        public static bool IfStringArraySmallerThen(this byte[] array, byte[] arrayToCompare)
        {
            if (array == null)
                array = new byte[0];

            if (arrayToCompare == null)
                arrayToCompare = new byte[0];

            int minLen = array.Length;
            if (arrayToCompare.Length < minLen)
                minLen = arrayToCompare.Length;

            for (int i = 0; i < minLen; i++)
            {
                if (array[i] < arrayToCompare[i])
                    return true;

                if (array[i] > arrayToCompare[i])
                    return false;
            }

            //If keys were equal to that point, then 
            if (array.Length == arrayToCompare.Length)    //keys are equal
                return false;

            if (array.Length > arrayToCompare.Length)
                return false;

            return true;
        }

        /// <summary>
        /// String Comparation Point of view:
        /// "AAA" less then "AAAA"
        /// "AB" more then "AAAA"
        /// </summary>
        /// <param name="array"></param>
        /// <param name="arrayToCompare"></param>
        /// <returns></returns>
        public static bool IfStringArraySmallerOrEqualThen(this byte[] array, byte[] arrayToCompare)
        {
            if (array == null)
                array = new byte[0];

            if (arrayToCompare == null)
                arrayToCompare = new byte[0];

            int minLen = array.Length;
            if (arrayToCompare.Length < minLen)
                minLen = arrayToCompare.Length;

            for (int i = 0; i < minLen; i++)
            {
                if (array[i] < arrayToCompare[i])
                    return true;

                if (array[i] > arrayToCompare[i])
                    return false;
            }

            //If keys were equal to that point, then 
            if (array.Length == arrayToCompare.Length)    //keys are equal
                return true;

            if (array.Length > arrayToCompare.Length)
                return false;

            return true;
        }


        /// <summary>
        /// String Comparation Point of view:
        /// "AAA" less then "AAAA"
        /// "AB" more then "AAAA"
        /// </summary>
        /// <param name="array"></param>
        /// <param name="arrayToCompare"></param>
        /// <returns></returns>
        public static bool IfStringArrayBiggerThen(this byte[] array, byte[] arrayToCompare)
        {
            if (array == null)
                array = new byte[0];

            if (arrayToCompare == null)
                arrayToCompare = new byte[0];

            int minLen = array.Length;
            if (arrayToCompare.Length < minLen)
                minLen = arrayToCompare.Length;

            for (int i = 0; i < minLen; i++)
            {
                if (array[i] > arrayToCompare[i])
                    return true;

                if (array[i] < arrayToCompare[i])
                    return false;
            }

            //If keys were equal to that point, then 
            if (array.Length == arrayToCompare.Length)    //keys are equal
                return false;

            if (array.Length < arrayToCompare.Length)
                return false;

            return true;
        }


        /// <summary>
        /// String Comparation Point of view:
        /// "AAA" less then "AAAA"
        /// "AB" more then "AAAA"
        /// </summary>
        /// <param name="array"></param>
        /// <param name="arrayToCompare"></param>
        /// <returns></returns>
        public static bool IfStringArrayBiggerOrEqualThen(this byte[] array, byte[] arrayToCompare)
        {
            if (array == null)
                array = new byte[0];

            if (arrayToCompare == null)
                arrayToCompare = new byte[0];

            int minLen = array.Length;
            if (arrayToCompare.Length < minLen)
                minLen = arrayToCompare.Length;

            for (int i = 0; i < minLen; i++)
            {
                if (array[i] > arrayToCompare[i])
                    return true;

                if (array[i] < arrayToCompare[i])
                    return false;
            }

            //If keys were equal to that point, then 
            if (array.Length == arrayToCompare.Length)    //keys are equal
                return true;

            if (array.Length < arrayToCompare.Length)
                return false;

            return true;
        }


        public static bool IfStringArrayStartsWith(this byte[] array, byte[] startsWith)
        {
            if (array == null && startsWith == null)
                return true;

            if (array.Length == 0 && startsWith.Length == 0)
                return true;

            if (startsWith.Length > array.Length)
                return false;

            for (int i = 0; i < startsWith.Length; i++)
            {
                if (array[i] != startsWith[i])
                    return false;
            }

            return true;
        }


        #endregion
    }

    /// <summary>
    /// Sorting of byte[]
    ///  foreach (var r1 in input.OrderBy(x => x, new ByteListComparer())) Debug.WriteLine(r1.ToBytesString());
    /// </summary>
    public class ByteListComparer : IComparer<IList<byte>>
    {
        /*
         *  List<byte[]> input = new List<byte[]>(){
                new byte[] { 1, 2, 4 }, 
                new byte[] { 1, 2, 3 },
                new byte[] { 1, 2, 3, 5 }
                };

             foreach (var r1 in input.OrderBy(x => x, new ByteListComparer()))
                Debug.WriteLine(r1.ToBytesString());

            Ret:
            010203
            01020305
            010204
         */
        public int Compare(IList<byte> x, IList<byte> y)
        {
            int result;
            int min = Math.Min(x.Count, y.Count);
            for (int index = 0; index < min; index++)
            {
                result = x[index].CompareTo(y[index]);
                if (result != 0)
                    return result;
            }
            return x.Count.CompareTo(y.Count);
        }
    }
}
