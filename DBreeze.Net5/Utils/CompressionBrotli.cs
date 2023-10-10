#if NET6FUNC
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DBreeze.Utils
{
    public static class CompressionBrotli
    {
        //
        // Summary:
        //     Doesn't use Try catch inside only for NET6 and higher
        //
        // Parameters:
        //   data:
        //
        //   compressionLevel:
        public static byte[] CompressBytesBrotli(this byte[] data, CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            using MemoryStream memoryStream = new MemoryStream();
            using (BrotliStream brotliStream = new BrotliStream(memoryStream, compressionLevel))
            {
                brotliStream.WriteAsync(data, 0, data.Length);
            }

            return memoryStream.ToArray();
        }

        //
        // Summary:
        //     Doesn't use Try catch inside only for NET6 and higher
        //
        // Parameters:
        //   data:
        public static byte[] DecompressBytesBrotli(this byte[] data)
        {
            using MemoryStream stream = new MemoryStream(data);
            using MemoryStream memoryStream = new MemoryStream();
            using (BrotliStream brotliStream = new BrotliStream(stream, CompressionMode.Decompress))
            {
                brotliStream.CopyTo(memoryStream);
            }

            return memoryStream.ToArray();
        }

        //
        // Parameters:
        //   bytes:
        //
        //   compressionLevel:
        //
        //   cancel:
        public static async Task<byte[]> CompressBytesAsyncBrotli(byte[] bytes, CompressionLevel compressionLevel = CompressionLevel.Optimal, CancellationToken cancel = default(CancellationToken))
        {
            using MemoryStream outputStream = new MemoryStream();
            using (BrotliStream compressionStream = new BrotliStream(outputStream, compressionLevel))
            {
                await compressionStream.WriteAsync(bytes, 0, bytes.Length, cancel);
            }

            return outputStream.ToArray();
        }

        //
        // Parameters:
        //   originalFileName:
        //
        //   compressedFileName:
        //
        //   compressionLevel:
        public static void CompressFileBrotli(string originalFileName, string compressedFileName, CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            CompressFileAsyncBrotli(originalFileName, compressedFileName, default(CancellationToken), compressionLevel).GetAwaiter().GetResult();
        }

        //
        // Parameters:
        //   originalFileName:
        //
        //   compressedFileName:
        //
        //   cancel:
        //
        //   compressionLevel:
        public static async Task CompressFileAsyncBrotli(string originalFileName, string compressedFileName, CancellationToken cancel = default(CancellationToken), CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            using FileStream originalStream = File.Open(originalFileName, FileMode.Open);
            using FileStream compressedStream = File.Create(compressedFileName);
            await CompressStreamAsyncBrotli(originalStream, compressedStream, cancel, compressionLevel);
        }

        //
        // Parameters:
        //   originalStream:
        //
        //   compressedStream:
        public static void CompressStreamBrotli(Stream originalStream, Stream compressedStream, CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            CompressStreamAsyncBrotli(originalStream, compressedStream, default(CancellationToken), compressionLevel).GetAwaiter().GetResult();
        }

        //
        // Parameters:
        //   originalStream:
        //
        //   compressedStream:
        //
        //   cancel:
        //
        //   compressionLevel:
        public static async Task CompressStreamAsyncBrotli(Stream originalStream, Stream compressedStream, CancellationToken cancel = default(CancellationToken), CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            using BrotliStream compressor = new BrotliStream(compressedStream, compressionLevel);
            await originalStream.CopyToAsync(compressor, cancel);
        }

        //
        // Parameters:
        //   bytes:
        //
        //   cancel:
        public static async Task<byte[]> DecompressBytesAsyncBrotli(byte[] bytes, CancellationToken cancel = default(CancellationToken))
        {
            using MemoryStream inputStream = new MemoryStream(bytes);
            using MemoryStream outputStream = new MemoryStream();
            using (BrotliStream compressionStream = new BrotliStream(inputStream, CompressionMode.Decompress))
            {
                await compressionStream.CopyToAsync(outputStream, cancel);
            }

            return outputStream.ToArray();
        }

        //
        // Parameters:
        //   compressedFileName:
        //
        //   outputFileName:
        public static void DecompressFileBrotli(string compressedFileName, string outputFileName)
        {
            DecompressFileAsyncBrotli(compressedFileName, outputFileName).GetAwaiter().GetResult();
        }

        //
        // Parameters:
        //   compressedFileName:
        //
        //   outputFileName:
        //
        //   cancel:
        public static async Task DecompressFileAsyncBrotli(string compressedFileName, string outputFileName, CancellationToken cancel = default(CancellationToken))
        {
            using FileStream compressedFileStream = File.Open(compressedFileName, FileMode.Open);
            using FileStream outputFileStream = File.Create(outputFileName);
            await DecompressStreamAsyncBrotli(compressedFileStream, outputFileStream, cancel);
        }

        //
        // Parameters:
        //   compressedStream:
        //
        //   outputStream:
        public static void DecompressStreamBrotli(Stream compressedStream, Stream outputStream)
        {
            DecompressStreamAsyncBrotli(compressedStream, outputStream).GetAwaiter().GetResult();
        }

        //
        // Parameters:
        //   compressedStream:
        //
        //   outputStream:
        //
        //   cancel:
        public static async Task DecompressStreamAsyncBrotli(Stream compressedStream, Stream outputStream, CancellationToken cancel = default(CancellationToken))
        {
            using BrotliStream decompressor = new BrotliStream(compressedStream, CompressionMode.Decompress);
            await decompressor.CopyToAsync(outputStream, cancel);
        }
    }
}
#endif