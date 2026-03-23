/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
                return new byte[size]; // rely on standard array zeroing

            if (ar.Length >= size)
                return ar;

            byte[] rb = new byte[size];
            ar.CopyTo(rb.AsSpan(size - ar.Length));
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
            ar.CopyTo(rb.AsSpan());
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
            if (ar == null) return null;
            if (ar.Length < 1) return ar;
            if (startIndex > ar.Length - 1) return null;

            if (startIndex + length > ar.Length)
            {
                length = ar.Length - startIndex;
            }

            return ar.AsSpan(startIndex, length).ToArray();
        }

        /// <summary>
        /// Substring int-dimensional byte arrays from and till the end
        /// </summary>
        /// <param name="ar"></param>
        /// <param name="startIndex"></param>
        /// <returns></returns>
        public static byte[] Substring(this byte[] ar, int startIndex)
        {
            if (ar == null) return null;
            if (ar.Length < 1) return ar;
            if (startIndex > ar.Length - 1) return null;

            return ar.AsSpan(startIndex).ToArray();
        }

        /// <summary>
        /// Works only for int-dimesional arrays only
        /// </summary>
        /// <param name="ar"></param>
        /// <returns></returns>
        public static byte[] CloneArray(this byte[] ar)
        {
            if (ar == null) return null;
            if (ar.Length < 1) return Array.Empty<byte>();

            byte[] rb = GC.AllocateUninitializedArray<byte>(ar.Length);
            ar.CopyTo(rb.AsSpan());
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
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyInside(this byte[] destArray, int destOffset, byte[] srcArray, int srcOffset, int quantity)
        {
            srcArray.AsSpan(srcOffset, quantity).CopyTo(destArray.AsSpan(destOffset));
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
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyInside(this byte[] destArray, int destOffset, byte[] srcArray)
        {
            srcArray.CopyTo(destArray.AsSpan(destOffset));
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
            byte[] ret;
            if (destArray.Length < (destOffset + srcArray.Length))
            {
                ret = new byte[destOffset + srcArray.Length];
            }
            else
            {
                ret = new byte[destArray.Length];
            }

            destArray.CopyTo(ret.AsSpan());
            srcArray.CopyTo(ret.AsSpan(destOffset));

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

            int index = array.AsSpan().IndexOfAnyExcept(elementToRemove);
            if (index < 0) return Array.Empty<byte>();

            return array.AsSpan(index).ToArray();
        }

        /// <summary>
        /// Array.Reverse is the same fast, but reverses by reference the parameter-array, what is not acceptable
        /// </summary>
        /// <param name="ar"></param>
        /// <returns></returns>
        public static byte[] Reverse(this byte[] ar)
        {
            if (ar == null || ar.Length == 0) return ar;

            byte[] ret = GC.AllocateUninitializedArray<byte>(ar.Length);
            ar.CopyTo(ret.AsSpan());
            Array.Reverse(ret);
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
            if (ar1 == null || ar1.Length == 0) return ar2?.CloneArray() ?? Array.Empty<byte>();
            if (ar2 == null || ar2.Length == 0) return ar1.CloneArray();

            byte[] ret = GC.AllocateUninitializedArray<byte>(ar1.Length + ar2.Length);
            ar1.CopyTo(ret.AsSpan());
            ar2.CopyTo(ret.AsSpan(ar1.Length));

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
            return new byte[] { ar1, ar2 };
        }

        /// <summary>
        /// FOR OPTIMITATION LIKE Concat(this byte[] ar1, byte ar2)
        /// </summary>
        /// <param name="ar1"></param>
        /// <param name="ar2"></param>
        /// <returns></returns>
        public static byte[] Concat(this byte ar1, byte[] ar2)
        {
            if (ar2 == null || ar2.Length == 0) return new byte[] { ar1 };
            byte[] ret = GC.AllocateUninitializedArray<byte>(1 + ar2.Length);
            ret[0] = ar1;
            ar2.CopyTo(ret.AsSpan(1));
            return ret;
        }

        public static byte[] Concat(this byte[] ar1, byte ar2)
        {
            if (ar1 == null || ar1.Length == 0) return new byte[] { ar2 };

            byte[] ret = GC.AllocateUninitializedArray<byte>(ar1.Length + 1);
            ar1.CopyTo(ret.AsSpan());
            ret[^1] = ar2; // ^1 is index from end

            return ret;
        }

        /// <summary>
        /// Fast when necessary to concat many arrays
        /// Example: byte[] s = new byte[] { 1, 2, 3 }; s.ConcatMany(new byte[] { 2, 3, 4 }, new byte[] { 3, 7, 8 }, new byte[] { 9, 10, 11 });
        /// Also: ((byte[])null).ConcatMany(new byte[] { 2, 3, 4 }, new byte[] { 3, 7, 8 }, new byte[] { 9, 10, 11 });
        /// </summary>
        /// <param name="ar1"></param>
        /// <param name="arrays"></param>
        /// <returns></returns>
        public static byte[] ConcatMany(this byte[] ar1, params byte[][] arrays)
        {
            long len = ar1?.Length ?? 0;
            foreach (var data in arrays)
            {
                if (data != null) len += data.Length;
            }

            if (len > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(len));

            byte[] ret = GC.AllocateUninitializedArray<byte>((int)len);
            int offset = 0;

            if (ar1 != null && ar1.Length > 0)
            {
                ar1.CopyTo(ret.AsSpan(offset));
                offset += ar1.Length;
            }

            foreach (byte[] data in arrays)
            {
                if (data == null || data.Length == 0) continue;
                data.CopyTo(ret.AsSpan(offset));
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
            long len = ar1?.Length ?? 0;
            foreach (var data in arrays)
            {
                if (data != null) len += data.Length;
            }

            byte[] ret = GC.AllocateUninitializedArray<byte>((int)len);
            int offset = 0;

            if (ar1 != null && ar1.Length > 0)
            {
                ar1.CopyTo(ret.AsSpan(offset));
                offset += ar1.Length;
            }

            foreach (byte[] data in arrays)
            {
                if (data == null || data.Length == 0) continue;
                data.CopyTo(ret.AsSpan(offset));
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
            long len = 0;
            foreach (var data in arrays)
            {
                if (data != null) len += data.Length;
            }

            byte[] ret = GC.AllocateUninitializedArray<byte>((int)len);
            int offset = 0;

            foreach (byte[] data in arrays)
            {
                if (data == null || data.Length == 0) continue;
                data.CopyTo(ret.AsSpan(offset));
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
            //List<byte[]> xbts = new List<byte[]>();
            //xbts.Add(DataTypes.DataTypesConvertor.ConvertValue(indexNumber, typeof(byte)));
            List<byte[]> xbts = [DataTypes.DataTypesConvertor.ConvertValue(indexNumber, typeof(byte))];
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
            List<byte[]> xbts = [];// new List<byte[]>();
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

            //List<byte[]> xbts = new List<byte[]>();
            //xbts.Add(DataTypes.DataTypesConvertor.ConvertValue(par1, par1.GetType()));
            List<byte[]> xbts = [DataTypes.DataTypesConvertor.ConvertValue(par1, par1.GetType())];
            if (pars != null)
                foreach (var prop in pars)
                    xbts.Add(DataTypes.DataTypesConvertor.ConvertValue(prop, prop.GetType()));

            return xbts.Concat();
        }

        #endregion

        /// <summary>
        /// If not found returns -1
        /// </summary>
        /// <param name="ar"></param>
        /// <param name="search"></param>
        /// <param name="en"></param>
        /// <returns></returns>
        public static int indexOfStringInByteArray(this byte[] ar, string search, Encoding en)
        {
            if (ar == null || ar.Length == 0 || string.IsNullOrEmpty(search)) return -1;
            byte[] sr = en.GetBytes(search);
            return ar.AsSpan().IndexOf(sr);
        }

        /// <summary>
        /// If not found returns -1
        /// </summary>
        /// <param name="ar"></param>
        /// <param name="search"></param>
        /// <param name="en"></param>
        /// <returns></returns>
        public static int IndexOfStringInByteArray(this byte[] ar, string search, Encoding en)
        {
            if (ar == null || ar.Length == 0 || string.IsNullOrEmpty(search)) return -1;
            byte[] sr = en.GetBytes(search);
            return ar.AsSpan().IndexOf(sr);
        }

        /// <summary>
        /// Searches Start index of the byte[] pattern inside of the byte array
        /// If not found returns -1
        /// </summary>
        /// <param name="ar"></param>
        /// <param name="search"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOfByteArray(this byte[] ar, byte[] search)
        {
            if (ar == null || search == null || ar.Length == 0 || search.Length == 0)
                return -1;

            return ar.AsSpan().IndexOf(search);
        }

        /// <summary>
        /// Searches Start index of the byte[] pattern inside of the byte array
        /// If not found returns -1
        /// </summary>
        /// <param name="ar"></param>
        /// <param name="search"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOfByteArray(this ReadOnlySpan<byte> ar, ReadOnlySpan<byte> search)
        {
            if (ar.IsEmpty || search.IsEmpty) return -1;
            return ar.IndexOf(search);
        }


        //0x0801 
        //Big Endian - First comes higer: 0x08 0x01 = 2049
        //Little Endian - First comes lower 0x01 0x08 = 2049

        #region "Conversions Bytes To Other"

        #region "Single byte"
        /// <summary>
        /// From 1 byte array returns byte
        /// </summary>        
        public static byte To_Byte(this byte[] value) => value[0];

        ///// <summary>
        ///// From 1 byte array returns byte
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static byte To_Byte(this ReadOnlySpan<byte> value) => value[0];
        #endregion

        #region "Single byte ?"
        /// <summary>
        /// From 2 bytes array returns byte?
        /// If array length is not equal to 2 bytes returns null
        /// </summary>        
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
        public static DateTime To_DateTime(this byte[] value)
        {
            return new DateTime((long)value.To_UInt64_BigEndian());

        }

        /// <summary>
        /// DON'T use it (only for compatibility reasons described in docu from [20120922])
        /// BigEndian 8 bytes tries to convert to Ticks
        /// </summary>        
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
        public static DateTime? To_DateTime_NULL(this byte[] value)
        {
            if (value == null || value.Length != 9 || value[0] == 0)
                return null;

            return new DateTime((long)(new byte[] { value[1], value[2], value[3], value[4], value[5], value[6], value[7], value[8] }.To_UInt64_BigEndian()));
            //return new DateTime(value.To_Int64_BigEndian());

        }
        //public static DateTime? To_DateTime_NULL(this byte[] value) => (value == null) ? null : To_DateTime_NULL(value.AsSpan());

        ///// <summary>
        ///// Returns DateTime? from 9-byte array
        ///// If array is not equal 9 bytes returns null
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //private static DateTime? To_DateTime_NULL(this ReadOnlySpan<byte> value)
        //{
        //    if (value.Length != 9 || value[0] == 0) return null;
        //    return new DateTime((long)value.Slice(1, 8).To_UInt64_BigEndian());
        //}
        #endregion

        #region "Boolean"
        /// <summary>
        /// Returns bool from 1-byte array
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
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

            return (System.Text.Encoding.Unicode.GetChars(new byte[] { value[1], value[2] })[0]);

        }
        #endregion

        //#region "Char"
        ///// <summary>
        ///// Converts 2 bytes byte[] into Unicode char
        ///// </summary>
        ////[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static char To_Char(this byte[] value) => To_Char(value.AsSpan());

        ///// <summary>
        ///// Converts 2 bytes byte[] into Unicode char
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //private static char To_Char(this ReadOnlySpan<byte> value) => MemoryMarshal.Cast<byte, char>(value)[0];
        //#endregion

        //#region "Char ?"
        ///// <summary>
        ///// Converts 3 bytes byte[] into Unicode char?
        ///// </summary>   
        ////[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static char? To_Char_NULL(this byte[] value) => (value == null) ? null : To_Char_NULL(value.AsSpan());

        ///// <summary>
        ///// Converts 3 bytes byte[] into Unicode char?
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //private static char? To_Char_NULL(this ReadOnlySpan<byte> value)
        //{
        //    if (value.Length != 3 || value[0] == 0) return null;
        //    return MemoryMarshal.Cast<byte, char>(value.Slice(1, 2))[0];
        //}
        //#endregion

        #region "SByte"
        /// <summary>
        /// Converts 1 byte array into sbyte
        /// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte To_SByte(this byte[] value) => To_SByte(value.AsSpan());

        /// <summary>
        /// Converts 1 byte array into sbyte
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static sbyte To_SByte(this ReadOnlySpan<byte> value) => (sbyte)(value[0] + sbyte.MinValue);
        #endregion

        #region "SByte ?"
        /// <summary>
        /// Converts 2 bytes array into sbyte?
        /// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte? To_SByte_NULL(this byte[] value) => (value == null) ? null : To_SByte_NULL(value.AsSpan());

        /// <summary>
        /// Converts 2 bytes array into sbyte?
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static sbyte? To_SByte_NULL(this ReadOnlySpan<byte> value)
        {
            if (value.Length != 2 || value[0] == 0) return null;
            return (sbyte)(value[1] + sbyte.MinValue);
        }
        #endregion

        #region "Int16"
        /// <summary>
        /// From 2 bytes array which is in BigEndian order (highest byte first, lowest last) makes short.
        /// If array not equal 2 bytes throws exception. (-32,768 to 32,767)
        /// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short To_Int16_BigEndian(this byte[] value)
        {
            return (short)((value).To_UInt16_BigEndian() + short.MinValue);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static short To_Int16_BigEndian(this byte[] value) => To_Int16_BigEndian(value.AsSpan());        

        ///// <summary>
        ///// From 2 bytes array which is in BigEndian order (highest byte first, lowest last) makes short.
        ///// If array not equal 2 bytes throws exception. (-32,768 to 32,767)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //private static short To_Int16_BigEndian(this ReadOnlySpan<byte> value) => (short)(BinaryPrimitives.ReadUInt16BigEndian(value) + short.MinValue);

        /// <summary>
        /// From 2 bytes array which is in LittleEndian order (lowest byte first, highest last) makes short.
        /// If array not equal 2 bytes throws exception. (-32,768 to 32,767)
        /// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short To_Int16_LittleEndian(this byte[] value)
        {
            return (short)((value).To_UInt16_LittleEndian() + short.MinValue);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static short To_Int16_LittleEndian(this byte[] value) => To_Int16_LittleEndian(value.AsSpan());

        ///// <summary>
        ///// From 2 bytes array which is in LittleEndian order (lowest byte first, highest last) makes short.
        ///// If array not equal 2 bytes throws exception. (-32,768 to 32,767)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //private static short To_Int16_LittleEndian(this ReadOnlySpan<byte> value) => (short)(BinaryPrimitives.ReadUInt16LittleEndian(value) + short.MinValue);
        #endregion

        #region "Int16 ?"
        /// <summary>
        /// From 3 bytes array which is in BigEndian order (highest byte first, lowest last) makes short?.
        /// If array not equal 3 bytes returns null. (-32,768 to 32,767)
        /// </summary>
        public static short? To_Int16_BigEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 3 || value[0] == 0)
                return null;

            return (short?)((value).To_UInt16_BigEndian_NULL() + short.MinValue);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static short? To_Int16_BigEndian_NULL(this byte[] value) => (value == null) ? null : To_Int16_BigEndian_NULL(value.AsSpan());

        ///// <summary>
        ///// From 3 bytes array which is in BigEndian order (highest byte first, lowest last) makes short?.
        ///// If array not equal 3 bytes returns null. (-32,768 to 32,767)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static short? To_Int16_BigEndian_NULL(this ReadOnlySpan<byte> value)
        //{
        //    if (value.Length != 3 || value[0] == 0) return null;
        //    return (short)(BinaryPrimitives.ReadUInt16BigEndian(value.Slice(1)) + short.MinValue);
        //}

        /// <summary>
        /// From 3 bytes array which is in LittleEndian order (lowest byte first, highest last) makes short.
        /// If array not equal 3 bytes returns null. (-32,768 to 32,767)
        /// </summary>
        public static short? To_Int16_LittleEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 3 || value[0] == 0)
                return null;

            return (short?)((value).To_UInt16_LittleEndian_NULL() + short.MinValue);
        }
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static short? To_Int16_LittleEndian_NULL(this byte[] value) => (value == null) ? null : To_Int16_LittleEndian_NULL(value.AsSpan());

        ///// <summary>
        ///// From 3 bytes array which is in LittleEndian order (lowest byte first, highest last) makes short.
        ///// If array not equal 3 bytes returns null. (-32,768 to 32,767)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static short? To_Int16_LittleEndian_NULL(this ReadOnlySpan<byte> value)
        //{
        //    if (value.Length != 3 || value[0] == 0) return null;
        //    return (short)(BinaryPrimitives.ReadUInt16LittleEndian(value.Slice(1)) + short.MinValue);
        //}
        #endregion

        #region "UInt16"
        /// <summary>
        /// From 2 bytes array which is in BigEndian order (highest byte first, lowest last) makes ushort.
        /// If array not equal 2 bytes throws exception. (0 to 65,535)
        /// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort To_UInt16_BigEndian(this byte[] value)
        {
            return (ushort)(value[0] << 8 | value[1]);
        }
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static ushort To_UInt16_BigEndian(this byte[] value) => To_UInt16_BigEndian(value.AsSpan());

        ///// <summary>
        ///// From 2 bytes array which is in BigEndian order (highest byte first, lowest last) makes ushort.
        ///// If array not equal 2 bytes throws exception. (0 to 65,535)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static ushort To_UInt16_BigEndian(this ReadOnlySpan<byte> value) => BinaryPrimitives.ReadUInt16BigEndian(value);

        /// <summary>
        /// From 2 bytes array which is in LittleEndian order (lowest byte first, highest last) makes ushort.
        /// If array not equal 2 bytes throws exception. (0 to 65,535)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort To_UInt16_LittleEndian(this byte[] value)
        {
            return (ushort)(value[1] << 8 | value[0]);
        }
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static ushort To_UInt16_LittleEndian(this byte[] value) => To_UInt16_LittleEndian(value.AsSpan());

        ///// <summary>
        ///// From 2 bytes array which is in LittleEndian order (lowest byte first, highest last) makes ushort.
        ///// If array not equal 2 bytes throws exception. (0 to 65,535)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static ushort To_UInt16_LittleEndian(this ReadOnlySpan<byte> value) => BinaryPrimitives.ReadUInt16LittleEndian(value);
        #endregion

        #region "UInt16 ?"
        /// <summary>
        /// From 3 bytes array which is in BigEndian order (highest byte first, lowest last) makes ushort?.
        /// If array not equal 3 bytes returns null. (0 to 65,535)
        /// </summary>
        public static ushort? To_UInt16_BigEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 3 || value[0] == 0)
                return null;

            return (ushort)(value[1] << 8 | value[2]);
        }
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static ushort? To_UInt16_BigEndian_NULL(this byte[] value) => (value == null) ? null : To_UInt16_BigEndian_NULL(value.AsSpan());

        ///// <summary>
        ///// From 3 bytes array which is in BigEndian order (highest byte first, lowest last) makes ushort?.
        ///// If array not equal 3 bytes returns null. (0 to 65,535)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static ushort? To_UInt16_BigEndian_NULL(this ReadOnlySpan<byte> value)
        //{
        //    if (value.Length != 3 || value[0] == 0) return null;
        //    return BinaryPrimitives.ReadUInt16BigEndian(value.Slice(1));
        //}

        /// <summary>
        /// From 3 bytes array which is in LittleEndian order (lowest byte first, highest last) makes ushort?.
        /// If array not equal 3 bytes returns null. (0 to 65,535)
        /// </summary>
        public static ushort? To_UInt16_LittleEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 3 || value[0] == 0)
                return null;

            return (ushort)(value[2] << 8 | value[1]);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static ushort? To_UInt16_LittleEndian_NULL(this byte[] value) => (value == null) ? null : To_UInt16_LittleEndian_NULL(value.AsSpan());

        ///// <summary>
        ///// From 3 bytes array which is in LittleEndian order (lowest byte first, highest last) makes ushort?.
        ///// If array not equal 3 bytes returns null. (0 to 65,535)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static ushort? To_UInt16_LittleEndian_NULL(this ReadOnlySpan<byte> value)
        //{
        //    if (value.Length != 3 || value[0] == 0) return null;
        //    return BinaryPrimitives.ReadUInt16LittleEndian(value.Slice(1));
        //}
        #endregion

        #region "Int32"
        /// <summary>
        /// From 4 bytes array which is in BigEndian order (highest byte first, lowest last) makes int.
        /// If array not equal 4 bytes throws exception. (-2,147,483,648 to 2,147,483,647)
        /// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int To_Int32_BigEndian(this byte[] value)
        {          
            return (int)((value).To_UInt32_BigEndian() + int.MinValue);
        }
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static int To_Int32_BigEndian(this byte[] value) => To_Int32_BigEndian(value.AsSpan());

        ///// <summary>
        ///// From 4 bytes array which is in BigEndian order (highest byte first, lowest last) makes int.
        ///// If array not equal 4 bytes throws exception. (-2,147,483,648 to 2,147,483,647)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static int To_Int32_BigEndian(this ReadOnlySpan<byte> value) => (int)(BinaryPrimitives.ReadUInt32BigEndian(value) + int.MinValue);

        /// <summary>
        /// From 4 bytes array which is in LittleEndian order (lowest byte first, highest last) makes int.
        /// If array not equal 4 bytes throws exception. (-2,147,483,648 to 2,147,483,647)
        /// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int To_Int32_LittleEndian(this byte[] value)
        {
            return (int)((value).To_UInt32_LittleEndian() + int.MinValue);
        }
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static int To_Int32_LittleEndian(this byte[] value) => To_Int32_LittleEndian(value.AsSpan());

        ///// <summary>
        ///// From 4 bytes array which is in LittleEndian order (lowest byte first, highest last) makes int.
        ///// If array not equal 4 bytes throws exception. (-2,147,483,648 to 2,147,483,647)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static int To_Int32_LittleEndian(this ReadOnlySpan<byte> value) => (int)(BinaryPrimitives.ReadUInt32LittleEndian(value) + int.MinValue);
        #endregion

        #region "Int32?"
        /// <summary>
        /// From 5 bytes array which is in BigEndian order (highest byte first, lowest last) makes int.
        /// If array is not equal 5 bytes returns null. Range is (-2,147,483,648 to 2,147,483,647)
        /// </summary>
        public static int? To_Int32_BigEndian_NULL(this byte[] value)
        {

            if (value == null || value.Length != 5 || value[0] == 0)
                return null;

            return (int?)((new byte[] { value[1], value[2], value[3], value[4] }).To_UInt32_BigEndian() + int.MinValue);

            //return (int)((value.Substring(1)).To_UInt32_BigEndian() + int.MinValue);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static int? To_Int32_BigEndian_NULL(this byte[] value) => (value == null) ? null : To_Int32_BigEndian_NULL(value.AsSpan());

        ///// <summary>
        ///// From 5 bytes array which is in BigEndian order (highest byte first, lowest last) makes int.
        ///// If array is not equal 5 bytes returns null. Range is (-2,147,483,648 to 2,147,483,647)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static int? To_Int32_BigEndian_NULL(this ReadOnlySpan<byte> value)
        //{
        //    if (value.Length != 5 || value[0] == 0) return null;
        //    return (int)(BinaryPrimitives.ReadUInt32BigEndian(value.Slice(1)) + int.MinValue);
        //}

        /// <summary>
        /// From 5 bytes array which is in LittleEndian order (lowest byte first, highest last) makes int.
        /// If array not equal 5 bytes returns null. (-2,147,483,648 to 2,147,483,647)
        /// </summary>
        public static int? To_Int32_LittleEndian_NULL(this byte[] value)
        {

            if (value == null || value.Length != 5 || value[0] == 0)
                return null;

            return (int?)((new byte[] { value[1], value[2], value[3], value[4] }).To_UInt32_LittleEndian() + int.MinValue);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static int? To_Int32_LittleEndian_NULL(this byte[] value) => (value == null) ? null : To_Int32_LittleEndian_NULL(value.AsSpan());

        ///// <summary>
        ///// From 5 bytes array which is in LittleEndian order (lowest byte first, highest last) makes int.
        ///// If array not equal 5 bytes returns null. (-2,147,483,648 to 2,147,483,647)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static int? To_Int32_LittleEndian_NULL(this ReadOnlySpan<byte> value)
        //{
        //    if (value.Length != 5 || value[0] == 0) return null;
        //    return (int)(BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(1)) + int.MinValue);
        //}
        #endregion

        #region "UInt32"
        /// <summary>
        /// From 4 bytes array which is in BigEndian order (highest byte first, lowest last) makes uint.
        /// If array not equal 4 bytes throws exception. (0 to 4.294.967.295)
        /// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint To_UInt32_BigEndian(this byte[] value)
        {          
            return (uint)(value[0] << 24 | value[1] << 16 | value[2] << 8 | value[3]);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static uint To_UInt32_BigEndian(this byte[] value) => To_UInt32_BigEndian(value.AsSpan());

        ///// <summary>
        ///// From 4 bytes array which is in BigEndian order (highest byte first, lowest last) makes uint.
        ///// If array not equal 4 bytes throws exception. (0 to 4.294.967.295)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static uint To_UInt32_BigEndian(this ReadOnlySpan<byte> value) => BinaryPrimitives.ReadUInt32BigEndian(value);

        /// <summary>
        /// From 4 bytes array which is in LittleEndian order (lowest byte first, highest last) makes uint.
        /// If array not equal 4 bytes throws exception. (0 to 4.294.967.295)
        /// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint To_UInt32_LittleEndian(this byte[] value)
        {
            return (uint)(value[3] << 24 | value[2] << 16 | value[1] << 8 | value[0]);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static uint To_UInt32_LittleEndian(this byte[] value) => To_UInt32_LittleEndian(value.AsSpan());

        ///// <summary>
        ///// From 4 bytes array which is in LittleEndian order (lowest byte first, highest last) makes uint.
        ///// If array not equal 4 bytes throws exception. (0 to 4.294.967.295)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static uint To_UInt32_LittleEndian(this ReadOnlySpan<byte> value) => BinaryPrimitives.ReadUInt32LittleEndian(value);
        #endregion

        #region "UInt32 ?"
        /// <summary>
        /// From 5 bytes array which is in BigEndian order (highest byte first, lowest last) makes uint?.
        /// If array not equal 5 bytes returns null. (0 to 4.294.967.295)
        /// </summary>
        public static uint? To_UInt32_BigEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 5 || value[0] == 0)
                return null;

            return (uint)(value[1] << 24 | value[2] << 16 | value[3] << 8 | value[4]);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static uint? To_UInt32_BigEndian_NULL(this byte[] value) => (value == null) ? null : To_UInt32_BigEndian_NULL(value.AsSpan());

        ///// <summary>
        ///// From 5 bytes array which is in BigEndian order (highest byte first, lowest last) makes uint?.
        ///// If array not equal 5 bytes returns null. (0 to 4.294.967.295)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static uint? To_UInt32_BigEndian_NULL(this ReadOnlySpan<byte> value)
        //{
        //    if (value.Length != 5 || value[0] == 0) return null;
        //    return BinaryPrimitives.ReadUInt32BigEndian(value.Slice(1));
        //}

        /// <summary>
        /// From 5 bytes array which is in LittleEndian order (lowest byte first, highest last) makes uint?.
        /// If array not equal 5 bytes returns null. (0 to 4.294.967.295)
        /// </summary>
        public static uint? To_UInt32_LittleEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 5 || value[0] == 0)
                return null;

            return (uint)(value[4] << 24 | value[3] << 16 | value[2] << 8 | value[1]);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static uint? To_UInt32_LittleEndian_NULL(this byte[] value) => (value == null) ? null : To_UInt32_LittleEndian_NULL(value.AsSpan());

        ///// <summary>
        ///// From 5 bytes array which is in LittleEndian order (lowest byte first, highest last) makes uint?.
        ///// If array not equal 5 bytes returns null. (0 to 4.294.967.295)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static uint? To_UInt32_LittleEndian_NULL(this ReadOnlySpan<byte> value)
        //{
        //    if (value.Length != 5 || value[0] == 0) return null;
        //    return BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(1));
        //}
        #endregion

        #region "Int64"
        /// <summary>
        /// From 8 bytes array which is in BigEndian order (highest byte first, lowest last) makes long.
        /// If array not equal 8 bytes throws exception. (-9.223.372.036.854.775.808 bis 9.223.372.036.854.775.807)
        /// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long To_Int64_BigEndian(this byte[] value)
        {           
            return (long)((value).To_UInt64_BigEndian() - (ulong)Math.Abs(long.MinValue + 1) - 1);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static long To_Int64_BigEndian(this byte[] value) => To_Int64_BigEndian(value.AsSpan());

        ///// <summary>
        ///// From 8 bytes array which is in BigEndian order (highest byte first, lowest last) makes long.
        ///// If array not equal 8 bytes throws exception. (-9.223.372.036.854.775.808 bis 9.223.372.036.854.775.807)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static long To_Int64_BigEndian(this ReadOnlySpan<byte> value) => (long)(BinaryPrimitives.ReadUInt64BigEndian(value) - (ulong)Math.Abs(long.MinValue + 1) - 1);

        /// <summary>
        /// From 8 bytes array which is in LittleEndian order (lowest byte first, highest last) makes long.
        /// If array not equal 8 bytes throws exception. (-9.223.372.036.854.775.808 bis 9.223.372.036.854.775.807)
        /// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long To_Int64_LittleEndian(this byte[] value)
        {
            return (long)((value).To_UInt64_LittleEndian() - (ulong)Math.Abs(long.MinValue + 1) - 1);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static long To_Int64_LittleEndian(this byte[] value) => To_Int64_LittleEndian(value.AsSpan());

        ///// <summary>
        ///// From 8 bytes array which is in LittleEndian order (lowest byte first, highest last) makes long.
        ///// If array not equal 8 bytes throws exception. (-9.223.372.036.854.775.808 bis 9.223.372.036.854.775.807)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static long To_Int64_LittleEndian(this ReadOnlySpan<byte> value) => (long)(BinaryPrimitives.ReadUInt64LittleEndian(value) - (ulong)Math.Abs(long.MinValue + 1) - 1);
        #endregion

        #region "Int64 ?"
        /// <summary>
        /// From 9 bytes array which is in BigEndian order (highest byte first, lowest last) makes long.
        /// If array not equal 9 bytes return null. Range (-9.223.372.036.854.775.808 bis 9.223.372.036.854.775.807)
        /// </summary>
        public static long? To_Int64_BigEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 9 || value[0] == 0)
                return null;

            return (long?)((new byte[] { value[1], value[2], value[3], value[4], value[5], value[6], value[7], value[8] }).To_UInt64_BigEndian() - (ulong)Math.Abs(long.MinValue + 1) - 1);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static long? To_Int64_BigEndian_NULL(this byte[] value) => (value == null) ? null : To_Int64_BigEndian_NULL(value.AsSpan());

        ///// <summary>
        ///// From 9 bytes array which is in BigEndian order (highest byte first, lowest last) makes long.
        ///// If array not equal 9 bytes return null. Range (-9.223.372.036.854.775.808 bis 9.223.372.036.854.775.807)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static long? To_Int64_BigEndian_NULL(this ReadOnlySpan<byte> value)
        //{
        //    if (value.Length != 9 || value[0] == 0) return null;
        //    return (long)(BinaryPrimitives.ReadUInt64BigEndian(value.Slice(1)) - (ulong)Math.Abs(long.MinValue + 1) - 1);
        //}

        /// <summary>
        /// From 9 bytes array which is in LittleEndian order (lowest byte first, highest last) makes long.
        /// If array not equal 9 bytes returns null. Range (-9.223.372.036.854.775.808 bis 9.223.372.036.854.775.807)
        /// </summary>
        public static long? To_Int64_LittleEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 9 || value[0] == 0)
                return null;

            return (long?)((new byte[] { value[1], value[2], value[3], value[4], value[5], value[6], value[7], value[8] }).To_UInt64_LittleEndian() - (ulong)Math.Abs(long.MinValue + 1) - 1);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static long? To_Int64_LittleEndian_NULL(this byte[] value) => (value == null) ? null : To_Int64_LittleEndian_NULL(value.AsSpan());

        ///// <summary>
        ///// From 9 bytes array which is in LittleEndian order (lowest byte first, highest last) makes long.
        ///// If array not equal 9 bytes returns null. Range (-9.223.372.036.854.775.808 bis 9.223.372.036.854.775.807)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static long? To_Int64_LittleEndian_NULL(this ReadOnlySpan<byte> value)
        //{
        //    if (value.Length != 9 || value[0] == 0) return null;
        //    return (long)(BinaryPrimitives.ReadUInt64LittleEndian(value.Slice(1)) - (ulong)Math.Abs(long.MinValue + 1) - 1);
        //}
        #endregion

        #region "UInt64"
        /// <summary>
        /// From dynamic byte array (up to 8 bytes) stored in BigEndian format creates ulong value, 
        /// note if given byte array bigger then 8 bytes - then calcualtion will start from 0
        /// </summary>
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

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static ulong DynamicLength_To_UInt64_BigEndian(this byte[] value) => DynamicLength_To_UInt64_BigEndian(value.AsSpan());

        ///// <summary>
        ///// From dynamic byte array (up to 8 bytes) stored in BigEndian format creates ulong value, 
        ///// note if given byte array bigger then 8 bytes - then calcualtion will start from 0
        ///// </summary>
        //public static ulong DynamicLength_To_UInt64_BigEndian(this ReadOnlySpan<byte> value)
        //{
        //    ulong res = 0;
        //    int vl = value.Length;
        //    for (int i = 0; i < vl; i++)
        //    {
        //        res += (ulong)value[i] << ((vl - 1 - i) * 8);
        //    }
        //    return res;
        //}

        /// <summary>
        /// From 8 bytes array which is in BigEndian order (highest byte first, lowest last) makes ulong.
        /// If array not equal 8 bytes throws exception. (0 to 18,446,744,073,709,551,615)
        /// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong To_UInt64_BigEndian(this byte[] value)
        {           
            return (ulong)(((ulong)value[0] << 56) + ((ulong)value[1] << 48) + ((ulong)value[2] << 40) + ((ulong)value[3] << 32) + ((ulong)value[4] << 24) + ((ulong)value[5] << 16) + ((ulong)value[6] << 8) + (ulong)value[7]);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static ulong To_UInt64_BigEndian(this byte[] value) => To_UInt64_BigEndian(value.AsSpan());

        ///// <summary>
        ///// From 8 bytes array which is in BigEndian order (highest byte first, lowest last) makes ulong.
        ///// If array not equal 8 bytes throws exception. (0 to 18,446,744,073,709,551,615)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static ulong To_UInt64_BigEndian(this ReadOnlySpan<byte> value) => BinaryPrimitives.ReadUInt64BigEndian(value);

        /// <summary>
        /// From 8 bytes array which is in LittleEndian order (lowest byte first, highest last) makes ulong.
        /// If array not equal 8 bytes throws exception. (0 to 18,446,744,073,709,551,615)
        /// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong To_UInt64_LittleEndian(this byte[] value)
        {            
            return (ulong)(((ulong)value[7] << 56) + ((ulong)value[6] << 48) + ((ulong)value[5] << 40) + ((ulong)value[4] << 32) + ((ulong)value[3] << 24) + ((ulong)value[2] << 16) + ((ulong)value[1] << 8) + (ulong)value[0]);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static ulong To_UInt64_LittleEndian(this byte[] value) => To_UInt64_LittleEndian(value.AsSpan());

        ///// <summary>
        ///// From 8 bytes array which is in LittleEndian order (lowest byte first, highest last) makes ulong.
        ///// If array not equal 8 bytes throws exception. (0 to 18,446,744,073,709,551,615)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static ulong To_UInt64_LittleEndian(this ReadOnlySpan<byte> value) => BinaryPrimitives.ReadUInt64LittleEndian(value);
        #endregion

        #region "UInt64 ?"
        /// <summary>
        /// From 9 bytes array which is in BigEndian order (highest byte first, lowest last) makes ulong?.
        /// If array is not equal 9 bytes returns null. (0 to 18,446,744,073,709,551,615)
        /// </summary>
        public static ulong? To_UInt64_BigEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 9 || value[0] == 0)
                return null;

            return (ulong)(((ulong)value[1] << 56) + ((ulong)value[2] << 48) + ((ulong)value[3] << 40) + ((ulong)value[4] << 32) + ((ulong)value[5] << 24) + ((ulong)value[6] << 16) + ((ulong)value[7] << 8) + (ulong)value[8]);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static ulong? To_UInt64_BigEndian_NULL(this byte[] value) => (value == null) ? null : To_UInt64_BigEndian_NULL(value.AsSpan());

        ///// <summary>
        ///// From 9 bytes array which is in BigEndian order (highest byte first, lowest last) makes ulong?.
        ///// If array is not equal 9 bytes returns null. (0 to 18,446,744,073,709,551,615)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static ulong? To_UInt64_BigEndian_NULL(this ReadOnlySpan<byte> value)
        //{
        //    if (value.Length != 9 || value[0] == 0) return null;
        //    return BinaryPrimitives.ReadUInt64BigEndian(value.Slice(1));
        //}

        /// <summary>
        /// From 9 bytes array which is in LittleEndian order (lowest byte first, highest last) makes ulong?.
        /// If array is not equal 9 bytes returns null. (0 to 18,446,744,073,709,551,615)
        /// </summary>
        public static ulong? To_UInt64_LittleEndian_NULL(this byte[] value)
        {
            if (value == null || value.Length != 9 || value[0] == 0)
                return null;

            return (ulong)(((ulong)value[8] << 56) + ((ulong)value[7] << 48) + ((ulong)value[6] << 40) + ((ulong)value[5] << 32) + ((ulong)value[4] << 24) + ((ulong)value[3] << 16) + ((ulong)value[2] << 8) + (ulong)value[1]);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static ulong? To_UInt64_LittleEndian_NULL(this byte[] value) => (value == null) ? null : To_UInt64_LittleEndian_NULL(value.AsSpan());

        ///// <summary>
        ///// From 9 bytes array which is in LittleEndian order (lowest byte first, highest last) makes ulong?.
        ///// If array is not equal 9 bytes returns null. (0 to 18,446,744,073,709,551,615)
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static ulong? To_UInt64_LittleEndian_NULL(this ReadOnlySpan<byte> value)
        //{
        //    if (value.Length != 9 || value[0] == 0) return null;
        //    return BinaryPrimitives.ReadUInt64LittleEndian(value.Slice(1));
        //}
        #endregion

        #region "Decimal"

        //        private static readonly decimal[] Pow10decimal =
        //{
        //    1m, 10m, 100m, 1000m, 10000m, 100000m,
        //    1000000m, 10000000m, 100000000m,
        //    1000000000m, 10000000000m, 100000000000m,
        //    1000000000000m, 10000000000000m, 100000000000000m,
        //    1000000000000000m, 10000000000000000m,
        //    100000000000000000m, 1000000000000000000m,
        //    10000000000000000000m, 100000000000000000000m,
        //    1000000000000000000000m, 10000000000000000000000m,
        //    100000000000000000000000m, 1000000000000000000000000m,
        //    10000000000000000000000000m, 100000000000000000000000000m,
        //    1000000000000000000000000000m,
        //    10000000000000000000000000000m // Index 28 safety fallback
        //};

        //private static readonly decimal[] Pow10decimal = CreatePow10decimal();

        //private static decimal[] CreatePow10decimal()
        //{
        //    const int max = 28; // decimal precision limit

        //    var arr = new decimal[max + 1];
        //    arr[0] = 1m;

        //    for (int i = 1; i <= max; i++)
        //    {
        //        arr[i] = arr[i - 1] * 10m;
        //    }

        //    return arr;
        //}

        /// <summary>
        /// Converts sortable byte[15] to decimal
        /// </summary>
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

        /// <summary>
        /// Converts sortable byte[15] to decimal
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static decimal To_Decimal_BigEndian(this ReadOnlySpan<byte> input)
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
            0
                });
            }
            else
            {
                decimalValuePart = new decimal(new int[4]
                {
            (int)(~((input[9]) << 24 | (input[10]) << 16 | (input[11]) << 8 | (input[12]))),
            (int)(~((input[5]) << 24 | (input[6]) << 16 | (input[7]) << 8 | (input[8]))),
            (int)(~((input[1]) << 24 | (input[2]) << 16 | (input[3]) << 8 | (input[4]))),
            0
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
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal? To_Decimal_BigEndian_NULL(this byte[] input) => (input == null) ? null : To_Decimal_BigEndian_NULL(input.AsSpan());

        /// <summary>
        /// Converts sortable byte[16] to decimal? if byte array length is not 16 returns null
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static decimal? To_Decimal_BigEndian_NULL(this ReadOnlySpan<byte> input)
        {
            if (input.Length != 16 || input[0] == 0)
                return null;

            return To_Decimal_BigEndian(input.Slice(1));
        }
        #endregion

        #region "Double"

        const short BCNT_DOUBLE = 9;
        const short ENEG_DOUBLE = 324;
        const short EPOS_DOUBLE = 308;

        //private static readonly double[] Pow10double = CreatePow10double();

        //private static double[] CreatePow10double()
        //{
        //    var arr = new double[EPOS_DOUBLE + ENEG_DOUBLE + 1]; // [-324..308]
        //    int offset = ENEG_DOUBLE;

        //    for (int i = -ENEG_DOUBLE; i <= EPOS_DOUBLE; i++)
        //    {
        //        arr[i + offset] = Math.Pow(10, i);
        //    }

        //    return arr;
        //}

        /// <summary>
        /// Converts sortable byte[9] to double
        /// </summary>    
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double To_Double_BigEndian(this byte[] input) => To_Double_BigEndian(input.AsSpan());

        /// <summary>
        /// Converts sortable byte[9] to double
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double To_Double_BigEndian(this ReadOnlySpan<byte> input)
        {
            bool isPositive = (input[0] & 0x80) != 0;
            int exp = ((input[0] & 0x7F) << 8) | input[1];

            // Safely extract the 7-byte mantissa
            ulong value = 0;
            value |= (ulong)input[2] << 48;
            value |= (ulong)input[3] << 40;
            value |= (ulong)input[4] << 32;
            value |= (ulong)input[5] << 24;
            value |= (ulong)input[6] << 16;
            value |= (ulong)input[7] << 8;
            value |= (ulong)input[8];

            if (isPositive)
            {
                exp -= ENEG_DOUBLE;
            }
            else
            {
                value = (~value) & 0x00FFFFFFFFFFFFFFUL;
                exp = EPOS_DOUBLE - exp;
            }

            if (value == 0) return 0.0;

            // Count digits (same logic as yours)
            int digits =
                value >= 10000000000000000UL ? 17 : value >= 1000000000000000UL ? 16 : value >= 100000000000000UL ? 15 :
                value >= 10000000000000UL ? 14 : value >= 1000000000000UL ? 13 : value >= 100000000000UL ? 12 :
                value >= 10000000000UL ? 11 : value >= 1000000000UL ? 10 : value >= 100000000UL ? 9 :
                value >= 10000000UL ? 8 : value >= 1000000UL ? 7 : value >= 100000UL ? 6 :
                value >= 10000UL ? 5 : value >= 1000UL ? 4 : value >= 100UL ? 3 :
                value >= 10UL ? 2 : 1;

            int finalExp = exp - (digits - 1);

            // ALLOCATION FREE Exact .NET Double parsing (Bypasses the 53-bit double cast precision loss)
            Span<byte> utf8Text = stackalloc byte[32];

            // Write: "[Sign][Mantissa]E[FinalExp]" into UTF8 Span
            int offset = 0;
            if (!isPositive) utf8Text[offset++] = (byte)'-';

            Utf8Formatter.TryFormat(value, utf8Text.Slice(offset), out int written);
            offset += written;

            utf8Text[offset++] = (byte)'E';

            Utf8Formatter.TryFormat(finalExp, utf8Text.Slice(offset), out written);
            offset += written;

            // Parse exactly as the old code did, but without strings
            Utf8Parser.TryParse(utf8Text.Slice(0, offset), out double result, out _);

            return result;
        }

        #endregion

        #region "Double ?"

        /// <summary>
        /// Converts sortable byte[10] to double?
        /// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double? To_Double_BigEndian_NULL(this byte[] input) => (input == null) ? null : To_Double_BigEndian_NULL(input.AsSpan());

        /// <summary>
        /// Converts sortable byte[10] to double?
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double? To_Double_BigEndian_NULL(this ReadOnlySpan<byte> input)
        {
            if (input.Length != 10 || input[0] == 0)
                return null;

            return To_Double_BigEndian(input.Slice(1));
        }

        #endregion

        #region "Float"

        const short ENEG_FLOAT = 45;
        const short EPOS_FLOAT = 38;
        const short SDIG_FLOAT = 7;
        const short BCNT_FLOAT = 4;

        //private static readonly float[] Pow10float = CreatePow10float();

        //private static float[] CreatePow10float()
        //{
        //    var arr = new float[ENEG_FLOAT + EPOS_FLOAT + 1]; // [-45..38]
        //    int offset = ENEG_FLOAT;

        //    for (int i = -ENEG_FLOAT; i <= EPOS_FLOAT; i++)
        //    {
        //        arr[i + offset] = (float)Math.Pow(10, i);
        //    }

        //    return arr;
        //}

        /// <summary>
        /// Converts sortable byte[4] to float
        /// </summary>        
        public static float To_Float_BigEndian(this byte[] input) => To_Float_BigEndian(input.AsSpan());

        /// <summary>
        /// Converts sortable byte[4] to float
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float To_Float_BigEndian(this ReadOnlySpan<byte> input)
        {
            bool isPositive = (input[0] & 128) > 0;
            int exp = input[0] & 127;

            uint floatNumber =
                ((uint)input[1] << 16) |
                ((uint)input[2] << 8) |
                input[3];

            if (isPositive)
            {
                exp = exp - ENEG_FLOAT;
            }
            else
            {
                floatNumber = (~floatNumber) & 0xFFFFFF;
                exp = EPOS_FLOAT - exp;
            }

            if (floatNumber == 0 && exp == -45) return isPositive ? 0f : -0f; // Edge case protection

            // To Perfectly simulate: float.TryParse("-1.234000E+5")
            // We write to stackalloc byte array and parse via Utf8Parser (No Allocations)
            Span<byte> strBuffer = stackalloc byte[32];
            int writerIndex = 0;

            if (!isPositive)
            {
                strBuffer[writerIndex++] = (byte)'-';
            }

            // 7-digit value guarantee: pad left with zeros if somehow < 1000000 
            // (Though your format requires 7 digits, StandardFormat 'D7' accommodates this)
            Span<byte> numberBuffer = stackalloc byte[16];
            Utf8Formatter.TryFormat(floatNumber, numberBuffer, out int numWritten, new System.Buffers.StandardFormat('D', 7));

            strBuffer[writerIndex++] = numberBuffer[0];
            strBuffer[writerIndex++] = (byte)'.';

            for (int i = 1; i < numWritten; i++)
            {
                strBuffer[writerIndex++] = numberBuffer[i];
            }

            strBuffer[writerIndex++] = (byte)'E';
            strBuffer[writerIndex++] = exp >= 0 ? (byte)'+' : (byte)'-';

            Utf8Formatter.TryFormat(Math.Abs(exp), strBuffer.Slice(writerIndex), out int expWritten);
            writerIndex += expWritten;

            // Extract using exact IEEE 754 tie-breaking core parser
            Utf8Parser.TryParse(strBuffer.Slice(0, writerIndex), out float result, out _);

            return result;
        }

        #endregion

        #region "Float ?"

        /// <summary>
        /// Converts sortable byte[5] to float?
        /// </summary>          
        public static float? To_Float_BigEndian_NULL(this byte[] input) => (input == null) ? null : To_Float_BigEndian_NULL(input.AsSpan());

        /// <summary>
        /// Converts sortable byte[5] to float?
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float? To_Float_BigEndian_NULL(this ReadOnlySpan<byte> input)
        {
            if (input.Length != 5 || input[0] == 0) return null;           

            return To_Float_BigEndian(input.Slice(1));            
        }

        #endregion

        #region "Double array"

        /// <summary>
        /// Converts double[] to byte[]. Reversed DoubleArrayToByteArray
        /// </summary>
        /// <param name="byteArray"></param>
        /// <returns></returns>
        public static double[] ByteArrayToDoubleArray(this byte[] byteArray)
        {
            int doubleArrayLength = byteArray.Length / sizeof(double);
            double[] doubleArray = new double[doubleArrayLength];
            Buffer.BlockCopy(byteArray, 0, doubleArray, 0, byteArray.Length);
            return doubleArray;
        }

        ////[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static double[] ByteArrayToDoubleArray(this byte[] byteArray)
        //{
        //    return MemoryMarshal.Cast<byte, double>(byteArray).ToArray();
        //}
        #endregion

        #endregion  //End of byte[] to others

        #region "Conversions Other to Bytes"

        #region "Double array"

        /// <summary>
        /// Converts byte[] to double[]. Reversed ByteArrayToDoubleArray
        /// </summary>
        /// <param name="doubleArray"></param>
        /// <returns></returns>
        public static byte[] DoubleArrayToByteArray(this double[] doubleArray)
        {
            int byteArrayLength = doubleArray.Length * sizeof(double);
            byte[] byteArray = new byte[byteArrayLength];
            Buffer.BlockCopy(doubleArray, 0, byteArray, 0, byteArrayLength);
            return byteArray;
        }
        ////[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static byte[] DoubleArrayToByteArray(this double[] doubleArray)
        //{
        //    return MemoryMarshal.Cast<double, byte>(doubleArray).ToArray();
        //}
        #endregion

        #region "Single byte"

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            if (value == null) return new byte[] { 0, 0 };
            return new byte[] { 1, (byte)value };
        }

        #endregion

        #region "DateTime"

        /// <summary>
        /// DateTime to byte[8] big-endian.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_9_bytes_array(this DateTime? value)
        {
            if (value == null) return new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            return ((ulong?)((DateTime)value).Ticks).To_9_bytes_array_BigEndian();
        }

        #endregion

        #region "Boolean"

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_1_byte_array(this bool value)
        {
            return new byte[] { value ? (byte)1 : (byte)0 };
        }

        #endregion

        #region "Boolean ?"

        /// <summary>
        /// Returns 1 byte which represents bool?
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_1_byte_array(this bool? value)
        {
            if (value == null) return new byte[] { 2 };
            return new byte[] { (bool)value ? (byte)1 : (byte)0 };
        }

        #endregion

        #region "Char"

        /// <summary>
        /// Converts char into byte[2] Unicode representation
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_3_byte_array(this char? value)
        {
            if (value == null) return new byte[] { 0, 0, 0 };
            return new byte[] { 1 }.Concat(System.Text.Encoding.Unicode.GetBytes(new char[] { (char)value }));
        }
        #endregion

        #region "SByte"

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_1_byte_array(this sbyte value)
        {
            return new byte[] { (byte)(value - sbyte.MinValue) };
        }
        #endregion

        #region "SByte ?"

        /// <summary>
        /// Converts sbyte? into 2 byte array
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_2_byte_array(this sbyte? value)
        {
            if (value == null) return new byte[] { 0, 0 };
            return new byte[] { 1, (byte)((sbyte)value - sbyte.MinValue) };
        }
        #endregion

        #region "Int16"
        /// <summary>
        /// From Int16 to 2 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_2_bytes_array_BigEndian(this short value)
        {
            ushort val1 = (ushort)(value - short.MinValue);

            return new byte[]
            {
                (byte) (val1 >> 8),
                (byte)  val1
            };

            //byte[] res = new byte[2];
            //BinaryPrimitives.WriteUInt16BigEndian(res, (ushort)(value - short.MinValue));
            //return res;
        }

        /// <summary>
        /// From Int16 to 2 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_2_bytes_array_LittleEndian(this short value)
        {
            ushort val1 = (ushort)(value - short.MinValue);

            return new byte[]
            {
                (byte)  val1,
                (byte) (val1 >> 8)
            };

            //byte[] res = new byte[2];
            //BinaryPrimitives.WriteUInt16LittleEndian(res, (ushort)(value - short.MinValue));
            //return res;
        }
        #endregion

        #region "Int16 ?"
        /// <summary>
        /// From Int16? to 3 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

            //if (value == null) return new byte[] { 0, 0, 0 };
            //byte[] res = new byte[3];
            //res[0] = 1;
            //BinaryPrimitives.WriteUInt16BigEndian(res.AsSpan(1), (ushort)(value - short.MinValue));
            //return res;
        }

        /// <summary>
        /// From Int16? to 3 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

            //if (value == null) return new byte[] { 0, 0, 0 };
            //byte[] res = new byte[3];
            //res[0] = 1;
            //BinaryPrimitives.WriteUInt16LittleEndian(res.AsSpan(1), (ushort)(value - short.MinValue));
            //return res;
        }
        #endregion

        #region "UInt16"
        /// <summary>
        /// From UInt16 to 2 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_2_bytes_array_BigEndian(this ushort value)
        {
            return new byte[]
           {
                (byte) (value >> 8),
                (byte) value
           };

            //byte[] res = new byte[2];
            //BinaryPrimitives.WriteUInt16BigEndian(res, value);
            //return res;
        }

        /// <summary>
        /// From UInt16 to 2 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_2_bytes_array_LittleEndian(this ushort value)
        {
            return new byte[]
              {
                    (byte) value,
                    (byte) (value >> 8)
              };

            //byte[] res = new byte[2];
            //BinaryPrimitives.WriteUInt16LittleEndian(res, value);
            //return res;
        }
        #endregion

        #region "UInt16 ?"
        /// <summary>
        /// From UInt16? to 3 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

            //if (value == null) return new byte[] { 0, 0, 0 };
            //byte[] res = new byte[3];
            //res[0] = 1;
            //BinaryPrimitives.WriteUInt16BigEndian(res.AsSpan(1), (ushort)value);
            //return res;
        }

        /// <summary>
        /// From UInt16? to 3 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

            //if (value == null) return new byte[] { 0, 0, 0 };
            //byte[] res = new byte[3];
            //res[0] = 1;
            //BinaryPrimitives.WriteUInt16LittleEndian(res.AsSpan(1), (ushort)value);
            //return res;
        }
        #endregion

        #region "Int32"
        /// <summary>
        /// From Int32 to 4 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_4_bytes_array_BigEndian(this int value)
        {
            uint val1 = (uint)(value - int.MinValue);

            return new byte[]
            {
                (byte)(val1 >> 24),
                (byte)(val1 >> 16),
                (byte)(val1 >> 8),
                (byte) val1
            };

            //byte[] res = new byte[4];
            //BinaryPrimitives.WriteUInt32BigEndian(res, (uint)(value - int.MinValue));
            //return res;
        }

        /// <summary>
        /// From Int32 to 4 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_4_bytes_array_LittleEndian(this int value)
        {
            uint val1 = (uint)(value - int.MinValue);

            return new byte[]
            {
                (byte) val1 ,
                (byte)(val1 >> 8),
                (byte)(val1 >> 16),
                (byte)(val1 >> 24),
            };

            //byte[] res = new byte[4];
            //BinaryPrimitives.WriteUInt32LittleEndian(res, (uint)(value - int.MinValue));
            //return res;
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
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

            //if (value == null) return new byte[] { 0, 0, 0, 0, 0 };
            //byte[] res = new byte[5];
            //res[0] = 1;
            //BinaryPrimitives.WriteUInt32BigEndian(res.AsSpan(1), (uint)(value - int.MinValue));
            //return res;
        }

        /// <summary>
        /// From Int32 to 4 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_5_bytes_array_LittleEndian(this int? value)
        {
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

            //if (value == null) return new byte[] { 0, 0, 0, 0, 0 };
            //byte[] res = new byte[5];
            //res[0] = 1;
            //BinaryPrimitives.WriteUInt32LittleEndian(res.AsSpan(1), (uint)(value - int.MinValue));
            //return res;
        }
        #endregion

        #region "UInt32"
        /// <summary>
        /// From UInt32 to 4 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_4_bytes_array_BigEndian(this uint value)
        {
            return new byte[]
           {
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte) value
           };

            //byte[] res = new byte[4];
            //BinaryPrimitives.WriteUInt32BigEndian(res, value);
            //return res;
        }

        /// <summary>
        /// From UInt32 to 4 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_4_bytes_array_LittleEndian(this uint value)
        {
            return new byte[]
            {
                (byte) value ,
                (byte)(value >> 8),
                (byte)(value >> 16),
                (byte)(value >> 24),
            };

            //byte[] res = new byte[4];
            //BinaryPrimitives.WriteUInt32LittleEndian(res, value);
            //return res;
        }
        #endregion

        #region "UInt32 ?"
        /// <summary>
        /// From UInt32? to 5 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

            //if (value == null) return new byte[] { 0, 0, 0, 0, 0 };
            //byte[] res = new byte[5];
            //res[0] = 1;
            //BinaryPrimitives.WriteUInt32BigEndian(res.AsSpan(1), (uint)value);
            //return res;
        }

        /// <summary>
        /// From UInt32? to 5 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

            //if (value == null) return new byte[] { 0, 0, 0, 0, 0 };
            //byte[] res = new byte[5];
            //res[0] = 1;
            //BinaryPrimitives.WriteUInt32LittleEndian(res.AsSpan(1), (uint)value);
            //return res;
        }
        #endregion

        #region "Int64"
        /// <summary>
        /// From Int64 to 8 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_8_bytes_array_BigEndian(this long value)
        {
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

            //byte[] res = new byte[8];
            //BinaryPrimitives.WriteUInt64BigEndian(res, (ulong)(value - long.MinValue));
            //return res;
        }

        /// <summary>
        /// From Int64 to 8 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_8_bytes_array_LittleEndian(this long value)
        {
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

            //byte[] res = new byte[8];
            //BinaryPrimitives.WriteUInt64LittleEndian(res, (ulong)(value - long.MinValue));
            //return res;
        }
        #endregion

        #region "Int64 ?"
        /// <summary>
        /// From Int64? to 9 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

            //if (value == null) return new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            //byte[] res = new byte[9];
            //res[0] = 1;
            //BinaryPrimitives.WriteUInt64BigEndian(res.AsSpan(1), (ulong)(value - long.MinValue));
            //return res;
        }

        /// <summary>
        /// From Int64? to 9 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

            //if (value == null) return new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            //byte[] res = new byte[9];
            //res[0] = 1;
            //BinaryPrimitives.WriteUInt64LittleEndian(res.AsSpan(1), (ulong)(value - long.MinValue));
            //return res;
        }
        #endregion

        #region "UInt64"
        /// <summary>
        /// From UInt64 to 8 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_8_bytes_array_BigEndian(this ulong value)
        {
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

            //byte[] res = new byte[8];
            //BinaryPrimitives.WriteUInt64BigEndian(res, value);
            //return res;
        }

        /// <summary>
        /// From UInt64 to 8 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_8_bytes_array_LittleEndian(this ulong value)
        {
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

            //byte[] res = new byte[8];
            //BinaryPrimitives.WriteUInt64LittleEndian(res, value);
            //return res;
        }
        #endregion

        #region "UInt64 ?"
        /// <summary>
        /// From UInt64? to 9 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

            //if (value == null) return new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            //byte[] res = new byte[9];
            //res[0] = 1;
            //BinaryPrimitives.WriteUInt64BigEndian(res.AsSpan(1), (ulong)value);
            //return res;
        }

        /// <summary>
        /// From UInt64? to 9 bytes array with LittleEndian order (lowest byte first, highest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

            //if (value == null) return new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            //byte[] res = new byte[9];
            //res[0] = 1;
            //BinaryPrimitives.WriteUInt64LittleEndian(res.AsSpan(1), (ulong)value);
            //return res;
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

        /// <summary>
        /// Converts  double to sortable byte[9]
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static byte[] To_9_bytes_array_BigEndian(this double input)
        {
            Span<byte> buffer = stackalloc byte[9];
            Write_9_bytes_array_BigEndian(input, buffer);
            return buffer.ToArray(); // single unavoidable allocation
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Write_9_bytes_array_BigEndian(double input, Span<byte> output)
        {
            if (double.IsNaN(input) || double.IsInfinity(input))
            {
                throw new ArgumentException("NaN and Infinity are not supported by DBreeze lexicographical indices.");
            }

            bool isPositive = input >= 0;
            double abs = Math.Abs(input);

            if (abs == 0)
            {
                output.Clear();
                ushort zeroServicePart = (ushort)(ENEG_DOUBLE + 0x8000);
                output[0] = (byte)(zeroServicePart >> 8);
                output[1] = (byte)zeroServicePart;
                return;
            }

            Span<byte> utf8Text = stackalloc byte[32];

            // Match old .NET Framework 15-significant digit limit limit. 
            // We use 'E14' (1 digit before dot + 14 after = 15 total digits)
            Utf8Formatter.TryFormat(abs, utf8Text, out int written, new System.Buffers.StandardFormat('E', 14));

            ulong doubleNumber = 0;
            int index = 0;

            // Read first digit
            doubleNumber = (ulong)(utf8Text[index++] - '0');
            index++; // Skip the '.'

            // Read remaining 14 digits
            for (int i = 0; i < 14; i++)
            {
                doubleNumber = (doubleNumber * 10) + (ulong)(utf8Text[index++] - '0');
            }
                       
            doubleNumber = doubleNumber * 10;

            index++; // Skip the 'E'

            // Parse Exponent
            Utf8Parser.TryParse(utf8Text.Slice(index), out short exp, out _);

            ushort servicePart;
            if (isPositive)
            {
                servicePart = (ushort)(ENEG_DOUBLE + exp + 0x8000);
            }
            else
            {
                servicePart = (ushort)(EPOS_DOUBLE - exp);
                doubleNumber = (~doubleNumber) & 0x00FFFFFFFFFFFFFFUL;
            }

            output[0] = (byte)(servicePart >> 8);
            output[1] = (byte)servicePart;
            output[2] = (byte)(doubleNumber >> 48);
            output[3] = (byte)(doubleNumber >> 40);
            output[4] = (byte)(doubleNumber >> 32);
            output[5] = (byte)(doubleNumber >> 24);
            output[6] = (byte)(doubleNumber >> 16);
            output[7] = (byte)(doubleNumber >> 8);
            output[8] = (byte)doubleNumber;
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
            // Required heap allocation due to signature
            byte[] result = new byte[10];

            if (input == null)
                return result; // all zeros (same semantics as old)

            result[0] = 1;

            Write_9_bytes_array_BigEndian(input.Value, result.AsSpan(1));
            return result;
        }

        #endregion

        #region "Float"
              
        /// <summary>
        ///  Converts float to sortable byte[4]
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static byte[] To_4_bytes_array_BigEndian(this float input)
        {
            Span<byte> buffer = stackalloc byte[4];
            Write_4_bytes_array_BigEndian(input, buffer);
            return buffer.ToArray(); // single allocation
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Write_4_bytes_array_BigEndian(float input, Span<byte> output)
        {
            float abs = Math.Abs(input);

            Span<byte> strBuffer = stackalloc byte[32];
            Utf8Formatter.TryFormat(abs, strBuffer, out int written, new System.Buffers.StandardFormat('E', 6));
                       
            strBuffer = strBuffer.Slice(0, written);

            // Find 'E'
            int eIndex = strBuffer.IndexOf((byte)'E');
            Span<byte> mantissaSpan = strBuffer.Slice(0, eIndex);
            Span<byte> expSpan = strBuffer.Slice(eIndex + 1);

            // Calculate floatNumber (Mantissa) completely allocation-free
            uint floatNumber = 0;
            uint multiplier = 1000000;

            for (int i = 0; i < mantissaSpan.Length; i++)
            {
                byte b = mantissaSpan[i];
                if (b == (byte)'.') continue; // skip dot

                floatNumber += (uint)(b - '0') * multiplier;
                multiplier /= 10;
            }

            // Calculate exponent
            bool expIsNegative = expSpan[0] == (byte)'-';
            int exp = 0;
            int expMultiplier = 1;

            // Formatter outputs exponent like +01, -02
            for (int i = expSpan.Length - 1; i >= 1; i--)
            {
                exp += (expSpan[i] - '0') * expMultiplier;
                expMultiplier *= 10;
            }

            if (expIsNegative) exp = -exp;

            ushort servicePart;
            if (input >= 0)
            {
                servicePart = (ushort)(ENEG_FLOAT + exp + 0x80);
            }
            else
            {
                servicePart = (ushort)(EPOS_FLOAT - exp);
                floatNumber = ~floatNumber; // Bitwise NOT
            }

            output[0] = (byte)servicePart;
            output[1] = (byte)(floatNumber >> 16);
            output[2] = (byte)(floatNumber >> 8);
            output[3] = (byte)floatNumber;
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
            byte[] result = new byte[5];

            if (input == null)
                return result;

            result[0] = 1;

            Write_4_bytes_array_BigEndian(input.Value, result.AsSpan(1));
            return result;
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

        //private static byte[] TruncateUTF8(string text, int maxSizeInBytes)
        //{
        //    if (text == null) return null;

        //    byte[] bt = System.Text.Encoding.UTF8.GetBytes(text);
        //    if (bt.Length <= maxSizeInBytes) return bt;

        //    //Last byte is represented with 1 byte (ASCII range character)
        //    if (bt[maxSizeInBytes - 1] < 128)
        //        return bt.AsSpan(0, maxSizeInBytes).ToArray();

        //    int toRemove = 0;

        //    //computing how much to remove
        //    for (int i = maxSizeInBytes - 1; i >= 0; i--)
        //    {
        //        toRemove++;

        //        if ((bt[i] & 64) == 64)
        //        {
        //            //Calculating quantity of higher bits
        //            int qb = 2;
        //            int b = 0x20;

        //            for (int j = 1; j < 5; j++)
        //            {
        //                if ((bt[i] & b) == b)
        //                {
        //                    qb++;
        //                    b >>= 1;
        //                }
        //                else
        //                    break;
        //            }

        //            if (toRemove == qb)
        //                toRemove = 0;

        //            break;
        //        }
        //    }
        //    return bt.AsSpan(0, maxSizeInBytes - toRemove).ToArray();
        //}

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

            byte[] text = null;

            if (isASCII)
                text = System.Text.Encoding.ASCII.GetBytes(value);
            else
                text = System.Text.Encoding.UTF8.GetBytes(value);

            if (text.Length > fixedSize)
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToBase64String(this byte[] dBytes) => System.Convert.ToBase64String(dBytes);

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
            if (dBytes == null || dBytes.Length == 0) return String.Empty;
            if (string.IsNullOrEmpty(replaceWith)) return BitConverter.ToString(dBytes);
            return ("-" + BitConverter.ToString(dBytes)).Replace("-", replaceWith).Trim();
        }

        /// <summary>
        /// Generates byte[] from given Hex 1F0000000020. Backward function is ToHexFromByteArray
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] ToByteArrayFromHex(this string str)
        {
            if (string.IsNullOrEmpty(str)) return null;
            return Convert.FromHexString(str);
        }

        /// <summary>
        /// Generates Hex 1F0000000020 from byte[]. Backward function is ToByteArrayFromHex/ToByteArrayFromHex
        /// </summary>
        /// <param name="dBytes"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToHexFromByteArray(this byte[] dBytes) => dBytes.ToBytesString();

        /// <summary>
        /// To pure HEX string without delimiters
        /// </summary>
        /// <param name="dBytes"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToBytesString(this byte[] dBytes)
        {
            if (dBytes == null || dBytes.Length == 0) return String.Empty;
            return Convert.ToHexString(dBytes);
        }

        /// <summary>
        /// Convert Byte To Hex string
        /// </summary>
        /// <param name="dByte"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToHex(this byte dByte) => Convert.ToHexString(new ReadOnlySpan<byte>(ref dByte));

        /// <summary>
        /// Converts BytesArray to String Representation: 00-00-00-00-128-12-214-00-00-20.
        /// Where replaceWith = "-"
        /// </summary>
        /// <param name="dBytes"></param>
        /// <param name="replaceWith"></param>
        /// <returns></returns>
        public static string ToBytesStringDec(this byte[] dBytes, string replaceWith)
        {
            if (dBytes == null || dBytes.Length == 0) return String.Empty;
            if (string.IsNullOrEmpty(replaceWith)) replaceWith = "-";

            StringBuilder sb = new StringBuilder();
            foreach (var bt in dBytes)
            {
                sb.Append(bt.ToString()).Append(replaceWith);
            }

            if (sb.Length > 0)
                return sb.ToString(0, sb.Length - replaceWith.Length);
            else
                return String.Empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToAsciiString(this byte[] dBytes) => (dBytes == null) ? String.Empty : System.Text.Encoding.ASCII.GetString(dBytes);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToUTF8String(this byte[] dBytes) => (dBytes == null) ? String.Empty : System.Text.Encoding.UTF8.GetString(dBytes);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToUnicodeString(this byte[] dBytes) => (dBytes == null) ? String.Empty : System.Text.Encoding.Unicode.GetString(dBytes);

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

            return new byte[8]
            {
                (byte)((bt >> 7) & 1),
                (byte)((bt >> 6) & 1),
                (byte)((bt >> 5) & 1),
                (byte)((bt >> 4) & 1),
                (byte)((bt >> 3) & 1),
                (byte)((bt >> 2) & 1),
                (byte)((bt >> 1) & 1),
                (byte)(bt & 1)
            };
        }

        #endregion

        #region "CRC16"

        /// <summary>
        /// Returns byte representation of Crc16
        /// </summary>
        /// <param name="ar"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] Get_CRC16_AsByteArray(this byte[] ar)
        {
            return Crc16.ComputeChecksumBytes(ar.AsSpan());
        }

        private static class Crc16
        {
            const ushort polynomial = 0xA001;
            private static readonly ushort[] table = new ushort[256];

            static Crc16()
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
                            value = (ushort)((value >> 1) ^ polynomial);
                        else
                            value >>= 1;
                        temp >>= 1;
                    }
                    table[i] = value;
                }
            }

            public static ushort ComputeChecksum(ReadOnlySpan<byte> bytes)
            {
                ushort crc = 0;
                for (int i = 0; i < bytes.Length; ++i)
                {
                    byte index = (byte)(crc ^ bytes[i]);
                    crc = (ushort)((crc >> 8) ^ table[index]);
                }
                return crc;
            }

            public static byte[] ComputeChecksumBytes(ReadOnlySpan<byte> bytes)
            {
                ushort crc = ComputeChecksum(bytes);
                return new byte[] { (byte)(crc >> 8), (byte)(crc & 0x00ff) };
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
            if (bt == null || bt.Length == 0) return null;

            byte[] ret = new byte[bt.Length];
            bool toAdd = true;

            for (int i = bt.Length - 1; i >= 0; i--)
            {
                if (i == 0 && toAdd && bt[0] == 255) return null;

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
        /// Adds + 1 bit
        /// The same as BytesAction_GoOneBitUp_NoArrayGrow_BigEndian but array grows
        /// </summary>
        /// <param name="bt"></param>
        /// <returns></returns>
        public static byte[] BytesAction_GoOneBitUp_ArrayGrows_BigEndian(this byte[] bt)
        {
            if (bt == null || bt.Length == 0) return null;

            byte[] ret = new byte[bt.Length];
            bool toAdd = true;

            for (int i = bt.Length - 1; i >= 0; i--)
            {
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
            if (bt == null || bt.Length == 0) return null;

            int btLen = bt.Length;
            bt = bt.RemoveLeadingElement(0);

            if (bt.Length == 0) return null;
            if (bt.Length == 1 && bt[0] == 0) return null;

            byte[] ret = new byte[btLen];
            bool toExtract = true;

            for (int i = bt.Length - 1; i >= 0; i--)
            {
                if (toExtract)
                {
                    if (i == 0 && bt[i] == 0) return null;

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

        /// <summary>
        /// <para>BytesAction_GoUpNextByteStart_NoArrayGrow_BigEndian</para>
        /// <para>Returns: had {255}    -> null</para>
        /// <para>Returns: had {255, 0} -> null</para>
        /// <para>Returns: bt=null || bt.Length less then 2  -> null</para>
        /// <para>Returns: had {254, 0} -> {255, 0}</para>
        /// <para>Returns: had {120, 115, 147} -> {120, 116, 0}</para>
        /// </summary>
        /// <param name="bt"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public static byte[] BytesAction_GoUpNextByteStart_NoArrayGrow_BigEndian(this byte[] bt, int index)
        {
            if (bt == null || bt.Length < 2) return null;

            if (index <= 0 || index >= bt.Length) index = bt.Length - 1;

            byte[] ret = new byte[bt.Length];
            bool toAdd = true;

            for (int i = bt.Length - 1; i >= 0; i--)
            {
                if (i == 0 && toAdd && bt[0] == 255) return null;

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
            if (bt == null || bt.Length < 2) return null;

            if (index <= 0 || index >= bt.Length) index = bt.Length - 1;

            byte[] ret = new byte[bt.Length];
            bool toExtract = true;

            for (int i = bt.Length - 1; i >= 0; i--)
            {
                if (i == 0 && toExtract && bt[0] == 0) return null;

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

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static bool _IfDynamicDataPointerIsEmpty(this byte[] initPtr)
        //{
        //    if (initPtr == null || initPtr.Length != 16) return true;
        //    return !initPtr.AsSpan(0, 8).ContainsAnyExcept((byte)0);
        //}

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static bool _IfPointerIsEmpty(this byte[] ptr, ushort DefaultPointerLen)
        //{
        //    if (ptr == null || ptr.Length < DefaultPointerLen) return true;
        //    return !ptr.AsSpan(0, DefaultPointerLen).ContainsAnyExcept((byte)0);
        //}

        #endregion

        #region "Array Compare"

        /// <summary>
        /// Compares 2 byte arrays
        /// </summary>
        /// <param name="ar"></param>
        /// <param name="compareArray"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool _Equals(this byte[] ar, byte[] compareArray)
        {
            if (ar == null && compareArray == null) return true;
            if (ar == null || compareArray == null) return false;
            return ar.AsSpan().SequenceEqual(compareArray);
        }

        /// <summary>
        /// Compares 2 byte arrays
        /// </summary>
        /// <param name="b1"></param>
        /// <param name="b2"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool _ByteArrayEquals(this byte[] b1, byte[] b2)
        {
            if (b1 == b2) return true;
            if (b1 == null || b2 == null) return false;
            return b1.AsSpan().SequenceEqual(b2);
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
            if (b1 == null || b2 == null) return -1;
            int commonPrefix = b1.AsSpan().CommonPrefixLength(b2);
            return commonPrefix < b1.Length ? commonPrefix : -2;
        }

        /// <summary>
        /// String Comparation Point of view:
        /// "AAA" less then "AAAA"
        /// "AB" more then "AAAA"
        /// </summary>
        /// <param name="array"></param>
        /// <param name="arrayToCompare"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IfStringArraySmallerThen(this byte[] array, byte[] arrayToCompare)
        {
            return (array ?? Array.Empty<byte>()).AsSpan().SequenceCompareTo(arrayToCompare ?? Array.Empty<byte>()) < 0;
        }

        /// <summary>
        /// String Comparation Point of view:
        /// "AAA" less then "AAAA"
        /// "AB" more then "AAAA"
        /// </summary>
        /// <param name="array"></param>
        /// <param name="arrayToCompare"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IfStringArraySmallerOrEqualThen(this byte[] array, byte[] arrayToCompare)
        {
            return (array ?? Array.Empty<byte>()).AsSpan().SequenceCompareTo(arrayToCompare ?? Array.Empty<byte>()) <= 0;
        }

        /// <summary>
        /// String Comparation Point of view:
        /// "AAA" less then "AAAA"
        /// "AB" more then "AAAA"
        /// </summary>
        /// <param name="array"></param>
        /// <param name="arrayToCompare"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IfStringArrayBiggerThen(this byte[] array, byte[] arrayToCompare)
        {
            return (array ?? Array.Empty<byte>()).AsSpan().SequenceCompareTo(arrayToCompare ?? Array.Empty<byte>()) > 0;
        }

        /// <summary>
        /// String Comparation Point of view:
        /// "AAA" less then "AAAA"
        /// "AB" more then "AAAA"
        /// </summary>
        /// <param name="array"></param>
        /// <param name="arrayToCompare"></param>
        /// <returns></returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IfStringArrayBiggerOrEqualThen(this byte[] array, byte[] arrayToCompare)
        {
            return (array ?? Array.Empty<byte>()).AsSpan().SequenceCompareTo(arrayToCompare ?? Array.Empty<byte>()) >= 0;
        }

        public static bool IfStringArrayStartsWith(this byte[] array, byte[] startsWith)
        {
            if (array == null && startsWith == null) return true;
            if (array == null || startsWith == null) return false;
            return array.AsSpan().StartsWith(startsWith);
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
            // Fast-path utilizing span-based SIMD acceleration if they are byte arrays
            if (x is byte[] arrX && y is byte[] arrY)
                return arrX.AsSpan().SequenceCompareTo(arrY.AsSpan());

            int min = Math.Min(x.Count, y.Count);
            for (int index = 0; index < min; index++)
            {
                int result = x[index].CompareTo(y[index]);
                if (result != 0)
                    return result;
            }
            return x.Count.CompareTo(y.Count);
        }
    }
}
