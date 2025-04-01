#if NET6FUNC || NET472
// <copyright file="SmallWorld.Utils.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace DBreeze.HNSW
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text;

    /// <content>
    /// The part with the auxiliary tools for hnsw algorithm.
    /// </content>
    internal partial class SmallWorld<TItem, TDistance>
    {
        /// <summary>
        /// Distance is Lower Than.
        /// </summary>
        /// <param name="x">Left argument.</param>
        /// <param name="y">Right argument.</param>
        /// <returns>True if x &lt; y.</returns>
        [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "By Design")]
        public static bool DLt(TDistance x, TDistance y)
        {
            return x.CompareTo(y) < 0;
        }

        /// <summary>
        /// Distance is Greater Than.
        /// </summary>
        /// <param name="x">Left argument.</param>
        /// <param name="y">Right argument.</param>
        /// <returns>True if x &gt; y.</returns>
        [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "By Design")]
        public static bool DGt(TDistance x, TDistance y)
        {
            return x.CompareTo(y) > 0;
        }

        /// <summary>
        /// Distances are Equal.
        /// </summary>
        /// <param name="x">Left argument.</param>
        /// <param name="y">Right argument.</param>
        /// <returns>True if x == y.</returns>
        [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "By Design")]
        public static bool DEq(TDistance x, TDistance y)
        {
            return x.CompareTo(y) == 0;
        }

        /// <summary>
        /// Runs breadth first search.
        /// </summary>
        /// <param name="entryPoint">The entry point.</param>
        /// <param name="level">The level of the graph where to run BFS.</param>
        /// <param name="visitAction">The action to perform on each node.</param>
        internal static void BFS(Node entryPoint, int level, Action<Node> visitAction)
        {
            var visitedIds = new HashSet<int>();
            var expansionQueue = new Queue<Node>(new[] { entryPoint });

            //while (expansionQueue.Any())
            while (expansionQueue.Count>0)
            {
                var currentNode = expansionQueue.Dequeue();
                if (!visitedIds.Contains(currentNode.Id))
                {
                    visitAction(currentNode);
                    visitedIds.Add(currentNode.Id);
                    foreach (var neighbour in currentNode.GetConnections(level))
                    {
                        expansionQueue.Enqueue(neighbour);
                    }
                }
            }
        }

        //public static byte[] CompressF(float[] data)
        //{            
        //    byte[] byteArray = new byte[data.Length * sizeof(float)];
        //    Buffer.BlockCopy(data, 0, byteArray, 0, byteArray.Length);

        //    using (var outputStream = new MemoryStream())
        //    {
        //        using (var compressionStream = new BrotliStream(outputStream, CompressionLevel.Fastest))
        //        {
        //            compressionStream.Write(byteArray, 0, byteArray.Length);
        //        }
        //        return outputStream.ToArray();
        //    }
        //}

        public static byte[] CompressF(float[] data)
        {
            byte[] byteArray = new byte[data.Length * sizeof(float)];
            Buffer.BlockCopy(data, 0, byteArray, 0, byteArray.Length);

            using (var outputStream = new MemoryStream())
            {
                using (var compressionStream = new GZipStream(outputStream, CompressionLevel.Fastest))
                {
                    compressionStream.Write(byteArray, 0, byteArray.Length);
                }
                return outputStream.ToArray();
            }
        }

        public static float[] DecompressF(byte[] compressedData)
        {
            using (var inputStream = new MemoryStream(compressedData))
            using (var decompressionStream = new GZipStream(inputStream, CompressionMode.Decompress))
            using (var outputStream = new MemoryStream())
            {
                decompressionStream.CopyTo(outputStream);

                byte[] byteArray = outputStream.ToArray();
                float[] floatArray = new float[byteArray.Length / sizeof(float)];
                Buffer.BlockCopy(byteArray, 0, floatArray, 0, byteArray.Length);

                return floatArray;
            }
        }

        //public static float[] DecompressF(byte[] compressedData)
        //{
        //    using (var inputStream = new MemoryStream(compressedData))
        //    using (var decompressionStream = new BrotliStream(inputStream, CompressionMode.Decompress))
        //    using (var outputStream = new MemoryStream())
        //    {
        //        decompressionStream.CopyTo(outputStream);

        //        byte[] byteArray = outputStream.ToArray();
        //        float[] floatArray = new float[byteArray.Length / sizeof(float)];
        //        Buffer.BlockCopy(byteArray, 0, floatArray, 0, byteArray.Length);

        //        return floatArray;
        //    }
        //}

        public static byte[] CompressD(double[] data)
        {           
            byte[] byteArray = new byte[data.Length * sizeof(double)];
            Buffer.BlockCopy(data, 0, byteArray, 0, byteArray.Length);

            using (var outputStream = new MemoryStream())
            {
                using (var compressionStream = new GZipStream(outputStream, CompressionLevel.Fastest))
                {
                    compressionStream.Write(byteArray, 0, byteArray.Length);
                }
                return outputStream.ToArray();
            }
        }

        public static double[] DecompressD(byte[] compressedData)
        {
            using (var inputStream = new MemoryStream(compressedData))
            using (var decompressionStream = new GZipStream(inputStream, CompressionMode.Decompress))
            using (var outputStream = new MemoryStream())
            {
                decompressionStream.CopyTo(outputStream);

                byte[] byteArray = outputStream.ToArray();
                double[] doubleArray = new double[byteArray.Length / sizeof(double)];
                Buffer.BlockCopy(byteArray, 0, doubleArray, 0, byteArray.Length);

                return doubleArray;
            }

        }

        //public static class BrotliStringSerializer
        //{
        //    public static byte[] Serialize(List<string> strings)
        //    {
        //        using (var ms = new MemoryStream())
        //        using (var writer = new BinaryWriter(ms))
        //        {
        //            foreach (var s in strings)
        //            {                       
        //                var compressed = CompressString(s);
        //                writer.Write(compressed.Length);
        //                writer.Write(compressed);
        //            }
        //            return ms.ToArray();
        //        }
        //    }

        //    public static IEnumerable<string> Deserialize(byte[] data)
        //    {
        //        using (var ms = new MemoryStream(data))
        //        using (var reader = new BinaryReader(ms))
        //        {
        //            while (ms.Position < ms.Length)
        //            {
                       
        //                var compressedSize = reader.ReadInt32();                        
        //                var compressedBytes = reader.ReadBytes(compressedSize);                        
        //                yield return DecompressString(compressedBytes);
        //            }
        //        }
        //    }

        //    private static byte[] CompressString(string input)
        //    {
        //        var bytes = Encoding.UTF8.GetBytes(input);

        //        using (var output = new MemoryStream())
        //        {
        //            using (var compressor = new BrotliStream(output, CompressionLevel.Fastest))
        //            {
        //                compressor.Write(bytes, 0, bytes.Length);
        //            }
        //            return output.ToArray();
        //        }
        //    }

        //    private static string DecompressString(byte[] compressed)
        //    {
        //        using (var input = new MemoryStream(compressed))
        //        using (var output = new MemoryStream())
        //        {
        //            using (var decompressor = new BrotliStream(input, CompressionMode.Decompress))
        //            {
        //                decompressor.CopyTo(output);
        //            }
        //            return Encoding.UTF8.GetString(output.ToArray());
        //        }
        //    }
        //}


    }
}
#endif