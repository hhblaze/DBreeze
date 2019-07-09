/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBreeze.Utils
{
    public static class Compression
    {
        public enum eCompressionMethod
        {
            NoCompression = 0,
            Gzip = 1
        }

        /// <summary>
        /// In Memory Compression with Gzip
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static byte[] GZip_Compress(this byte[] data)
        {
            byte[] res = null;
            MemoryStream ms = null;
            System.IO.Compression.GZipStream gz = null;

            using (ms = new MemoryStream())
            {
                using (gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Compress, false))
                {
                    gz.Write(data, 0, data.Length);                   
                }

                res = ms.ToArray();               
            }

            return res;
        }

        ///// <summary>
        ///// In Memory GZip Decompressor 
        ///// </summary>
        ///// <param name="data"></param>
        ///// <returns></returns>
        //public static byte[] GZip_Decompress(this byte[] data)
        //{
        //    int length = 100000; //10Kb
        //    byte[] Ob = new byte[length];
        //    byte[] result = null;                        

        //    using (var ms = new MemoryStream(data))
        //    {
        //        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Decompress))
        //        {
        //            int a = 0;
        //            while ((a = gz.Read(Ob, 0, length)) > 0)
        //            {
        //                if (a == length)                            
        //                    result = result.Concat(Ob);
        //                else                            
        //                    result = result.Concat(Ob.Substring(0, a));
        //            }                    
        //        }                
        //    }

        //    return result;
        //}

        /// <summary>
        /// In Memory GZip Decompressor. Fastest implementation at the moment
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static byte[] GZip_Decompress(this byte[] data)
        {
            int length = 100000;
            byte[] Ob = new byte[length];
            byte[] result = null;
            List<byte[]> fl = new List<byte[]>();
            long fLen = 0;

            using (var ms = new MemoryStream(data))
            {
                using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Decompress))
                {
                    int a = 0;
                    while ((a = gz.Read(Ob, 0, length)) > 0)
                    {
                        if (a == length)
                            fl.Add(Ob);
                        else
                            fl.Add(Ob.Substring(0, a));

                        fLen += a;
                        Ob = new byte[length];
                    }
                    //gz.Close();
                }
                //ms.Close();

                if (fLen > 0)
                {
                    result = new byte[fLen];
                    int offset = 0;
                    foreach (var el in fl)
                    {
                        Buffer.BlockCopy(el, 0, result, offset, el.Length);
                        offset += el.Length;
                    }
                }
            }

            return result;
        }
    }
}
