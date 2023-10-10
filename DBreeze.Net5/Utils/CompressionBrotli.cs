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
        public static byte[] CompressBytesBrotliDBreeze(this byte[] data, CompressionLevel compressionLevel = CompressionLevel.Optimal)
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
        public static byte[] DecompressBytesBrotliDBreeze(this byte[] data)
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
        public static async Task<byte[]> CompressBytesAsyncBrotliDBreeze(byte[] bytes, CompressionLevel compressionLevel = CompressionLevel.Optimal, CancellationToken cancel = default(CancellationToken))
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
        public static void CompressFileBrotliDBreeze(string originalFileName, string compressedFileName, CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            CompressFileAsyncBrotliDBreeze(originalFileName, compressedFileName, default(CancellationToken), compressionLevel).GetAwaiter().GetResult();
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
        public static async Task CompressFileAsyncBrotliDBreeze(string originalFileName, string compressedFileName, CancellationToken cancel = default(CancellationToken), CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            using FileStream originalStream = File.Open(originalFileName, FileMode.Open);
            using FileStream compressedStream = File.Create(compressedFileName);
            await CompressStreamAsyncBrotliDBreeze(originalStream, compressedStream, cancel, compressionLevel);
        }

        //
        // Parameters:
        //   originalStream:
        //
        //   compressedStream:
        public static void CompressStreamBrotliDBreeze(Stream originalStream, Stream compressedStream, CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            CompressStreamAsyncBrotliDBreeze(originalStream, compressedStream, default(CancellationToken), compressionLevel).GetAwaiter().GetResult();
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
        public static async Task CompressStreamAsyncBrotliDBreeze(Stream originalStream, Stream compressedStream, CancellationToken cancel = default(CancellationToken), CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            using BrotliStream compressor = new BrotliStream(compressedStream, compressionLevel);
            await originalStream.CopyToAsync(compressor, cancel);
        }

        //
        // Parameters:
        //   bytes:
        //
        //   cancel:
        public static async Task<byte[]> DecompressBytesAsyncBrotliDBreeze(byte[] bytes, CancellationToken cancel = default(CancellationToken))
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
        public static void DecompressFileBrotliDBreeze(string compressedFileName, string outputFileName)
        {
            DecompressFileAsyncBrotliDBreeze(compressedFileName, outputFileName).GetAwaiter().GetResult();
        }

        //
        // Parameters:
        //   compressedFileName:
        //
        //   outputFileName:
        //
        //   cancel:
        public static async Task DecompressFileAsyncBrotliDBreeze(string compressedFileName, string outputFileName, CancellationToken cancel = default(CancellationToken))
        {
            using FileStream compressedFileStream = File.Open(compressedFileName, FileMode.Open);
            using FileStream outputFileStream = File.Create(outputFileName);
            await DecompressStreamAsyncBrotliDBreeze(compressedFileStream, outputFileStream, cancel);
        }

        //
        // Parameters:
        //   compressedStream:
        //
        //   outputStream:
        public static void DecompressStreamBrotliDBreeze(Stream compressedStream, Stream outputStream)
        {
            DecompressStreamAsyncBrotliDBreeze(compressedStream, outputStream).GetAwaiter().GetResult();
        }

        //
        // Parameters:
        //   compressedStream:
        //
        //   outputStream:
        //
        //   cancel:
        public static async Task DecompressStreamAsyncBrotliDBreeze(Stream compressedStream, Stream outputStream, CancellationToken cancel = default(CancellationToken))
        {
            using BrotliStream decompressor = new BrotliStream(compressedStream, CompressionMode.Decompress);
            await decompressor.CopyToAsync(outputStream, cancel);
        }
    }
}
#endif