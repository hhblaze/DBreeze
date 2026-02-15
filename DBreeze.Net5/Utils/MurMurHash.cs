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
using System.IO;
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

        /// <summary>
        /// BIt compatible with MixedMurMurHash3_128.
        /// <para>Usage: </para>
        /// <para>using var memoryStream = new MemoryStream(byteArray);</para>
        /// <para>MixedMurMurHash3_128_Stream(memoryStream)</para>
        /// <para>using var fileStream = File.OpenRead("bigfile");</para>
        /// <para>MixedMurMurHash3_128_Stream(fileStream)</para>
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static byte[] MixedMurMurHash3_128_Stream(Stream stream)
        {
            const uint c1 = 3432918353u;
            const uint c2 = 461845907u;

            uint h1 = 42u;
            uint h2 = 37u;
            uint h3 = 26u;
            uint h4 = 7u;

            long totalLength = 0;

            byte[] buffer = new byte[1024 * 1024];
            byte[] tail = new byte[4];
            int tailLength = 0;

            int bytesRead;

            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                totalLength += bytesRead;
                int offset = 0;

                if (tailLength > 0)
                {
                    while (tailLength < 4 && offset < bytesRead)
                        tail[tailLength++] = buffer[offset++];

                    if (tailLength == 4)
                    {
                        ProcessBlockAll(tail, 0, ref h1, ref h2, ref h3, ref h4, c1, c2);
                        tailLength = 0;
                    }
                }

                while (offset + 4 <= bytesRead)
                {
                    ProcessBlockAll(buffer, offset, ref h1, ref h2, ref h3, ref h4, c1, c2);
                    offset += 4;
                }

                while (offset < bytesRead)
                    tail[tailLength++] = buffer[offset++];
            }

            if (tailLength > 0)
            {
                uint k1 = 0;

                switch (tailLength)
                {
                    case 3: k1 ^= (uint)(tail[2] << 16); goto case 2;
                    case 2: k1 ^= (uint)(tail[1] << 8); goto case 1;
                    case 1:
                        k1 ^= tail[0];
                        k1 *= c1;
                        k1 = Rotl32(k1, 15);
                        k1 *= c2;

                        h1 ^= k1;
                        h2 ^= k1;
                        h3 ^= k1;
                        h4 ^= k1;
                        break;
                }
            }

            h1 ^= (uint)totalLength;
            h2 ^= (uint)totalLength;
            h3 ^= (uint)totalLength;
            h4 ^= (uint)totalLength;

            h1 = Fmix(h1);
            h2 = Fmix(h2);
            h3 = Fmix(h3);
            h4 = Fmix(h4);

            ulong part1 = ((ulong)h1 << 32) | h2;
            ulong part2 = ((ulong)h3 << 32) | h4;

            return part1.ToBytes(part2);

        }

        private static void ProcessBlockAll(
            byte[] data,
            int offset,
            ref uint h1,
            ref uint h2,
            ref uint h3,
            ref uint h4,
            uint c1,
            uint c2)
        {
            uint k1 =
                (uint)(data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24));

            k1 *= c1;
            k1 = Rotl32(k1, 15);
            k1 *= c2;

            h1 ^= k1;
            h2 ^= k1;
            h3 ^= k1;
            h4 ^= k1;

            h1 = Rotl32(h1, 13); h1 = h1 * 5 + 3864292196u;
            h2 = Rotl32(h2, 13); h2 = h2 * 5 + 3864292196u;
            h3 = Rotl32(h3, 13); h3 = h3 * 5 + 3864292196u;
            h4 = Rotl32(h4, 13); h4 = h4 * 5 + 3864292196u;
        }

        private static uint Rotl32(uint x, int r)
        {
            return (x << r) | (x >> (32 - r));
        }

        private static uint Fmix(uint h)
        {
            h ^= h >> 16;
            h *= 2246822507u;
            h ^= h >> 13;
            h *= 3266489909u;
            h ^= h >> 16;
            return h;
        }

    }
}
