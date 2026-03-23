using System;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TesterNet6.ByteConversion
{
    internal static class ByteConversionNew
    {

        public static bool _ByteArrayEquals(this byte[] b1, byte[] b2)
        {
            if (b1 == b2) return true;
            if (b1 == null || b2 == null) return false;
            return b1.AsSpan().SequenceEqual(b2);
        }

        /// <summary>
        /// String Comparation Point of view:
        /// "AAA" less then "AAAA"
        /// "AB" more then "AAAA"
        /// </summary>
        /// <param name="array"></param>
        /// <param name="arrayToCompare"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IfStringArraySmallerOrEqualThen(this byte[] array, byte[] arrayToCompare)
        {
            return (array ?? Array.Empty<byte>()).AsSpan().SequenceCompareTo(arrayToCompare ?? Array.Empty<byte>()) <= 0;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] ToByteArrayFromHex(this string str)
        {
            if (string.IsNullOrEmpty(str)) return null;
            return Convert.FromHexString(str);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_9_bytes_array_BigEndian(this long? value)
        {
            if (value == null) return new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            byte[] res = new byte[9];
            res[0] = 1;
            BinaryPrimitives.WriteUInt64BigEndian(res.AsSpan(1), (ulong)(value - long.MinValue));
            return res;
        }

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong To_UInt64_BigEndian(this byte[] value) => To_UInt64_BigEndian(value.AsSpan());

        /// <summary>
        /// From 8 bytes array which is in BigEndian order (highest byte first, lowest last) makes ulong.
        /// If array not equal 8 bytes throws exception. (0 to 18,446,744,073,709,551,615)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong To_UInt64_BigEndian(this ReadOnlySpan<byte> value) => BinaryPrimitives.ReadUInt64BigEndian(value);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] To_8_bytes_array_BigEndian(this ulong value)
        {
            byte[] res = new byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(res, value);
            return res;
        }




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



        private static readonly decimal[] Pow10decimal = CreatePow10decimal();

        private static decimal[] CreatePow10decimal()
        {
            const int max = 28; // decimal precision limit
            var arr = new decimal[max + 1];
            arr[0] = 1m;
            for (int i = 1; i <= max; i++)
            {
                arr[i] = arr[i - 1] * 10m;
            }
            return arr;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal To_Decimal_BigEndian(this byte[] input) => To_Decimal_BigEndian(input.AsSpan());

        public static decimal To_Decimal_BigEndian(this ReadOnlySpan<byte> input)
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

        ///// <summary>
        ///// Converts sortable byte[15] to decimal
        ///// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveOptimization)]
        //public static decimal To_Decimal_BigEndian(this ReadOnlySpan<byte> input)
        //{
        //    // Bypass bounds checks by pulling the direct memory reference
        //    ref byte src = ref MemoryMarshal.GetReference(input);

        //    bool isPositive = (src & 0x80) != 0;

        //    // Platform-independent BigEndian intrinsic reads mapped directly to memory without slices
        //    uint hi = BinaryPrimitives.ReadUInt32BigEndian(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref src, 1), 4));
        //    uint mid = BinaryPrimitives.ReadUInt32BigEndian(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref src, 5), 4));
        //    uint lo = BinaryPrimitives.ReadUInt32BigEndian(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref src, 9), 4));

        //    if (!isPositive)
        //    {
        //        lo = ~lo;
        //        mid = ~mid;
        //        hi = ~hi;
        //    }

        //    decimal decimalValuePart = new decimal((int)lo, (int)mid, (int)hi, false, 0);

        //    byte b13 = Unsafe.Add(ref src, 13);
        //    byte b14 = Unsafe.Add(ref src, 14);

        //    byte lastDigit = (byte)(b13 >> 3);
        //    if (!isPositive)
        //        lastDigit = (byte)((~lastDigit) & 0x1F);

        //    byte scale = (byte)(((b13 & 0x03) << 3) + (b14 >> 5));
        //    byte numOfDigits = (byte)(b14 & 0x1F);

        //    if (numOfDigits < 28)
        //    {
        //        int idx = 28 - numOfDigits;
        //        // Native array data access to completely drop array boundary checks
        //        ref decimal powRef = ref MemoryMarshal.GetArrayDataReference(Pow10decimal);
        //        decimalValuePart = decimal.Truncate(decimalValuePart / Unsafe.Add(ref powRef, idx));
        //    }
        //    else if (numOfDigits == 29)
        //    {
        //        decimalValuePart = decimalValuePart * 10m + lastDigit;
        //    }

        //    // Stackalloc operates fully on stack memory, saving garbage collection pauses
        //    Span<int> bits = stackalloc int[4];
        //    decimal.GetBits(decimalValuePart, bits);

        //    return new decimal(bits[0], bits[1], bits[2], !isPositive, scale);
        //}
        //public static decimal To_Decimal_BigEndian(this ReadOnlySpan<byte> input)
        //{
        //    bool isPositive = (input[0] & 0x80) != 0;

        //    uint hi = BinaryPrimitives.ReadUInt32BigEndian(input.Slice(1, 4));
        //    uint mid = BinaryPrimitives.ReadUInt32BigEndian(input.Slice(5, 4));
        //    uint lo = BinaryPrimitives.ReadUInt32BigEndian(input.Slice(9, 4));

        //    if (!isPositive)
        //    {
        //        lo = ~lo;
        //        mid = ~mid;
        //        hi = ~hi;
        //    }

        //    // Initialize unscaled integer mantissa
        //    decimal decimalValuePart = new decimal((int)lo, (int)mid, (int)hi, false, 0);

        //    byte lastDigit = (byte)(input[13] >> 3);
        //    if (!isPositive)
        //        lastDigit = (byte)((~lastDigit) & 0x1F);

        //    byte scale = (byte)(((input[13] & 0x03) << 3) + (input[14] >> 5));
        //    byte numOfDigits = (byte)(input[14] & 0x1F);

        //    if (numOfDigits < 28)
        //    {
        //        int idx = 28 - numOfDigits;
        //        if ((uint)idx < Pow10decimal.Length)
        //        {
        //            // CRITICAL FIX: decimal.Truncate guarantees the result has 0 scale, 
        //            // matching the old Math.Floor behavior. Standard division allocates scale bits.
        //            decimalValuePart = decimal.Truncate(decimalValuePart / Pow10decimal[idx]);
        //        }
        //    }

        //    if (numOfDigits == 29)
        //    {
        //        decimalValuePart = decimalValuePart * 10m + lastDigit;
        //    }

        //    Span<int> bits = stackalloc int[4];
        //    decimal.GetBits(decimalValuePart, bits);

        //    return new decimal(bits[0], bits[1], bits[2], !isPositive, scale);
        //}


        public static byte[] To_15_bytes_array_BigEndian(this decimal input)
        {
            byte[] result = new byte[15];

            // Bypass bounds checks for writing to the instantiated byte array
            ref byte dst = ref MemoryMarshal.GetArrayDataReference(result);

            Span<int> decBits = stackalloc int[4];
            decimal.GetBits(input, decBits);

            int hi = decBits[2];
            int mid = decBits[1];
            int lo = decBits[0];
            int flags = decBits[3];

            bool isPositive = (flags & unchecked((int)0x80000000)) == 0;
            byte scale = (byte)(flags >> 16);

            decimal value = new decimal(lo, mid, hi, false, 0);

            // Required explicitly to maintain precision loss bounds matches with old DB
            byte numDigits;
            unchecked { numDigits = (byte)(Math.Log10((double)value) + 1); }

            bool expPositive = numDigits > scale;
            byte exp = (byte)(30 + numDigits - 1 - scale);

            if (!isPositive) exp = (byte)(~exp & 0x3F);

            byte lastDigit = 0;

            if (numDigits == 29)
            {
                lastDigit = (byte)(value % 10m);
                value = decimal.Truncate(value / 10m);
            }
            else if (numDigits < 28)
            {
                // Dropped bounds checking here natively
                ref decimal powRef = ref MemoryMarshal.GetArrayDataReference(Pow10decimal);
                value *= Unsafe.Add(ref powRef, 28 - numDigits);
            }

            decimal.GetBits(value, decBits);

            int vHi = decBits[2];
            int vMid = decBits[1];
            int vLo = decBits[0];

            if (!isPositive)
            {
                vHi = ~vHi;
                vMid = ~vMid;
                vLo = ~vLo;
            }

            dst = isPositive
                ? (byte)(0x80 + (expPositive ? 0x40 : 0) + (exp & 0x3F))
                : (byte)((expPositive ? 0 : 0x40) + (exp & 0x3F));

            // Intrinsic BigEndian unrolled direct memory writes, completely bound-check free
            BinaryPrimitives.WriteInt32BigEndian(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref dst, 1), 4), vHi);
            BinaryPrimitives.WriteInt32BigEndian(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref dst, 5), 4), vMid);
            BinaryPrimitives.WriteInt32BigEndian(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref dst, 9), 4), vLo);

            // Fast-path index writes
            Unsafe.Add(ref dst, 13) = isPositive
                ? (byte)((lastDigit << 3) | (scale >> 3))
                : (byte)(((~lastDigit) << 3) | (scale >> 3));

            Unsafe.Add(ref dst, 14) = (byte)((scale << 5) | numDigits);

            return result;
        }
    

    //public static byte[] To_15_bytes_array_BigEndian(this decimal input)
    //{
    //    byte[] result = new byte[15];
    //    Write_15_bytes_array_BigEndian(input, result);
    //    return result;
    //}

    //[MethodImpl(MethodImplOptions.AggressiveInlining)]
    //private static void Write_15_bytes_array_BigEndian(decimal input, Span<byte> output)
    //{
    //    Span<int> decBits = stackalloc int[4];
    //    decimal.GetBits(input, decBits);

    //    bool isPositive = (decBits[3] & unchecked((int)0x80000000)) == 0;
    //    byte scale = (byte)(decBits[3] >> 16);

    //    decimal value = new decimal(decBits[0], decBits[1], decBits[2], false, 0);

    //    // CRITICAL FIX: We MUST keep the (double) cast and Math.Log10. 
    //    // Old data in the database relies on IEEE-754 precision loss at 28/29 digits 
    //    // to assign the correct `numOfDigits`. Do not "optimize" this line.
    //    byte numDigits;
    //    unchecked
    //    {
    //        numDigits = (byte)(Math.Log10((double)value) + 1);
    //    }

    //    bool expPositive = numDigits > scale;

    //    byte exp = (byte)(30 + numDigits - 1 - scale);
    //    if (!isPositive)
    //        exp = (byte)(~exp & 0x3F);

    //    byte lastDigit = 0;

    //    if (numDigits == 29)
    //    {
    //        lastDigit = (byte)(value % 10m);
    //        // CRITICAL FIX: decimal.Truncate guarantees integer truncation inside the mantissa
    //        // matching the old Math.Floor behavior, unlike `value /= 10m` which increments internal scale.
    //        value = decimal.Truncate(value / 10m);
    //    }

    //    if (numDigits < 28)
    //    {
    //        // Array length is safely bounded assuming numDigits byte casting never creates negatives via Log10 behavior
    //        value *= Pow10decimal[28 - numDigits];
    //    }

    //    decimal.GetBits(value, decBits);

    //    // Byte 0: header
    //    output[0] = isPositive
    //        ? (byte)(0x80 + (expPositive ? 0x40 : 0) + (exp & 0x3F))
    //        : (byte)((expPositive ? 0 : 0x40) + (exp & 0x3F));

    //    // Bytes 1-12: write hi/mid/lo using .NET 8 intrinsics
    //    WriteInt32(output.Slice(1, 4), decBits[2], isPositive);
    //    WriteInt32(output.Slice(5, 4), decBits[1], isPositive);
    //    WriteInt32(output.Slice(9, 4), decBits[0], isPositive);

    //    // Byte 13: last digit & top of scale
    //    output[13] = isPositive
    //        ? (byte)((lastDigit << 3) | (scale >> 3))
    //        : (byte)(((~lastDigit) << 3) | (scale >> 3));

    //    // Byte 14: bottom of scale & numDigits
    //    output[14] = (byte)((scale << 5) | numDigits);
    //}

    //[MethodImpl(MethodImplOptions.AggressiveInlining)]
    //private static void WriteInt32(Span<byte> dest, int value, bool positive)
    //{
    //    if (!positive) value = ~value;
    //    BinaryPrimitives.WriteInt32BigEndian(dest, value);
    //}












    const short ENEG_FLOAT = 45;
        const short EPOS_FLOAT = 38;
        const short BCNT_FLOAT = 4;


        public static float To_Float_BigEndian(this byte[] input) => To_Float_BigEndian(input.AsSpan());

        /// <summary>
        /// Converts sortable byte[4] to float safely recreating the string for parser
        /// </summary>
        public static float To_Float_BigEndian(this ReadOnlySpan<byte> input)
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


        /// <summary>
        ///  Converts float to sortable byte[4]
        /// </summary>
        public static byte[] To_4_bytes_array_BigEndian(this float input)
        {
            byte[] buffer = new byte[BCNT_FLOAT];
            Write_4_bytes_array_BigEndian(input, buffer);
            return buffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Write_4_bytes_array_BigEndian(float input, Span<byte> output)
        {
            float abs = Math.Abs(input);

            Span<byte> strBuffer = stackalloc byte[32];
            Utf8Formatter.TryFormat(abs, strBuffer, out int written, new System.Buffers.StandardFormat('E', 6));

            // -> THE CRITICAL FIX: Slice to the exact valid string length! <-
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


















        const short BCNT_DOUBLE = 9;
        const short ENEG_DOUBLE = 324;
        const short EPOS_DOUBLE = 308;

        /// <summary>
        /// Converts sortable byte[9] to double using zero-allocation Utf8Parser
        /// </summary>
        public static double To_Double_BigEndian(this byte[] input) => To_Double_BigEndian(input.AsSpan());

        public static double To_Double_BigEndian(this ReadOnlySpan<byte> input)
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

        public static byte[] To_9_bytes_array_BigEndian_first(this double input)
        {
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

            return new byte[] {
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
        }

        public static byte[] To_9_bytes_array_BigEndian(this double input)
        {
            byte[] buffer = new byte[9]; // Single unavoidable allocation
            Write_9_bytes_array_BigEndian(input, buffer);
            return buffer;
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

            // FIX: Match old .NET Framework 15-significant digit limit limit. 
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

            // FIX: Replicate the .NET Framework bug where the 16th digit was padded as exactly '0'
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
    }
}
