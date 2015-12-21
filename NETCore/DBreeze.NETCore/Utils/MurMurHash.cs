/***** BEGIN LICENSE BLOCK *****
 * Version: MPL 1.1/GPL 2.0/LGPL 2.1
 *
 * The contents of this file are subject to the Mozilla Public License Version
 * 1.1 (the "License"); you may not use this file except in compliance with
 * the License. You may obtain a copy of the License at
 * http://www.mozilla.org/MPL/
 *
 * Software distributed under the License is distributed on an "AS IS" basis,
 * WITHOUT WARRANTY OF ANY KIND, either express or implied. See the License
 * for the specific language governing rights and limitations under the
 * License.
 *
 * The Original Code is HashTableHashing.SuperFastHash.
 *
 * The Initial Developer of the Original MurmurHash2 Code is
 * Davy Landman.
 * Portions created by the Initial Developer are Copyright (C) 2009
 * the Initial Developer. All Rights Reserved.
 *
 * Contributor(s):
 * Thomas Kejser - turning this code into SQL Server CLR version 
 *                 and adding MurmurHash3 implementation based on C++ source
 *
 * Alternatively, the contents of this file may be used under the terms of
 * either the GNU General Public License Version 2 or later (the "GPL"), or
 * the GNU Lesser General Public License Version 2.1 or later (the "LGPL"),
 * in which case the provisions of the GPL or the LGPL are applicable instead
 * of those above. If you wish to allow use of your version of this file only
 * under the terms of either the GPL or the LGPL, and not to allow others to
 * use your version of this file under the terms of the MPL, indicate your
 * decision by deleting the provisions above and replace them with the notice
 * and other provisions required by the GPL or the LGPL. If you do not delete
 * the provisions above, a recipient may use your version of this file under
 * the terms of any one of the MPL, the GPL or the LGPL.
 *
 * ***** END LICENSE BLOCK ***** */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DBreeze.Utils;

namespace DBreeze.Utils.Hash
{
    public static class MurMurHash
    {
        //const UInt32 seed = 42; /* Define your own seed here */

        /// <summary>
        /// 128 bit mixed MurMurhash3
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static byte[] MixedMurMurHash3_128(byte[] data)
        {
            return ((((ulong)MurmurHash3(data, 42)) << 32) | ((ulong)MurmurHash3(data, 37))).To_8_bytes_array_BigEndian()
                .Concat(
                ((((ulong)MurmurHash3(data, 26)) << 32) | ((ulong)MurmurHash3(data, 7))).To_8_bytes_array_BigEndian()
                );
        }

        /// <summary>
        /// 64 bit mixed MurMurhash3
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static ulong MixedMurMurHash3_64(byte[] data)
        {            
                return (((ulong)MurmurHash3(data, 42)) << 32) | ((ulong)MurmurHash3(data, 37));
        }

        /// <summary>
        /// 32 bit
        /// </summary>
        /// <param name="data"></param>
        /// <param name="seed"></param>
        /// <returns></returns>
        public static uint MurmurHash3(byte[] data, uint seed = 42)
        {
            const uint c1 = 0xcc9e2d51;
            const uint c2 = 0x1b873593;


            int curLength = data.Length;    /* Current position in byte array */
            int length = curLength;   /* the const length we need to fix tail */
            uint h1 = seed;
            uint k1 = 0;

            /* body, eat stream a 32-bit int at a time */
            int currentIndex = 0;
            while (curLength >= 4)
            {
                /* Get four bytes from the input into an UInt32 */
                k1 = (uint)(data[currentIndex++]
                  | data[currentIndex++] << 8
                  | data[currentIndex++] << 16
                  | data[currentIndex++] << 24);

                /* bitmagic hash */
                k1 *= c1;
                k1 = rotl32(k1, 15);
                k1 *= c2;

                h1 ^= k1;
                h1 = rotl32(h1, 13);
                h1 = h1 * 5 + 0xe6546b64;
                curLength -= 4;
            }

            /* tail, the reminder bytes that did not make it to a full int */
            /* (this switch is slightly more ugly than the C++ implementation 
             * because we can't fall through) */
            switch (curLength)
            {
                case 3:
                    k1 = (uint)(data[currentIndex++]
                      | data[currentIndex++] << 8
                      | data[currentIndex++] << 16);
                    k1 *= c1;
                    k1 = rotl32(k1, 15);
                    k1 *= c2;
                    h1 ^= k1;
                    break;
                case 2:
                    k1 = (uint)(data[currentIndex++]
                      | data[currentIndex++] << 8);
                    k1 *= c1;
                    k1 = rotl32(k1, 15);
                    k1 *= c2;
                    h1 ^= k1;
                    break;
                case 1:
                    k1 = data[currentIndex++];
                    k1 *= c1;
                    k1 = rotl32(k1, 15);
                    k1 *= c2;
                    h1 ^= k1;
                    break;
            };

            // finalization, magic chants to wrap it all up
            h1 ^= (uint)length;
            h1 = fmix(h1);

            unchecked
            {
                return h1;
            }
        }


        //public static uint MurmurHash2(byte[] data, uint seed = 42))
        //{
        //    const uint m = 0x5bd1e995;
        //    const int r = 24;

        //    int length = (int)data.Length;
        //    if (length == 0)
        //        return 0;
        //    uint h = seed ^ (uint)length;
        //    int currentIndex = 0;
        //    while (length >= 4)
        //    {
        //        uint k = (uint)(data[currentIndex++]
        //          | data[currentIndex++] << 8
        //          | data[currentIndex++] << 16
        //          | data[currentIndex++] << 24);
        //        k *= m;
        //        k ^= k >> r;
        //        k *= m;

        //        h *= m;
        //        h ^= k;
        //        length -= 4;
        //    }
        //    switch (length)
        //    {
        //        case 3:
        //            h ^= (ushort)(data[currentIndex++]
        //              | data[currentIndex++] << 8);
        //            h ^= (uint)(data[currentIndex] << 16);
        //            h *= m;
        //            break;
        //        case 2:
        //            h ^= (ushort)(data[currentIndex++]
        //              | data[currentIndex] << 8);
        //            h *= m;
        //            break;
        //        case 1:
        //            h ^= data[currentIndex];
        //            h *= m;
        //            break;
        //        default:
        //            break;
        //    }

        //    // Do a few final mixes of the hash to ensure the last few
        //    // bytes are well-incorporated.

        //    h ^= h >> 13;
        //    h *= m;
        //    h ^= h >> 15;

        //    /* Interface back to SQL server */
        //    unchecked
        //    {
        //        return h;
        //    }
        //}



        private static uint rotl32(uint x, byte r)
        {
            return (x << r) | (x >> (32 - r));
        }

        private static uint fmix(uint h)
        {
            h ^= h >> 16;
            h *= 0x85ebca6b;
            h ^= h >> 13;
            h *= 0xc2b2ae35;
            h ^= h >> 16;
            return h;
        }



    }
}
