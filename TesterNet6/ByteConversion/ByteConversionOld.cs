using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TesterNet6.ByteConversion
{
    internal static class ByteConversionOld
    {

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

        }


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





        const short BCNT_DECIMAL = 15;


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












        const short ENEG_FLOAT = 45;
        const short EPOS_FLOAT = 38;
        const short SDIG_FLOAT = 7;
        const short BCNT_FLOAT = 4;

        public static float To_Float_BigEndian(this byte[] input)
        {
            bool blIsPositive = ((input[0] & 128) > 0);
            int exp = input[0] & 127;

            // REMOVED: input[0] = 0;

            // FIX: Just ignore input[0] entirely and shift the remaining 3 bytes
            uint floatNumber = (uint)(input[1] << 16 | input[2] << 8 | input[3]);

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

            // Pad left with zeros if floatString is shorter than 7 characters, 
            // which happens sometimes depending on the stored mantissa.
            // (Note: Optional, but highly recommended, otherwise .Substring(1) can throw an exception if length is 1)
            if (floatString.Length < 7)
            {
                floatString = floatString.PadLeft(7, '0');
            }

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











        const short BCNT_DOUBLE = 9;
        const short ENEG_DOUBLE = 324;
        const short EPOS_DOUBLE = 308;

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
    }
}
