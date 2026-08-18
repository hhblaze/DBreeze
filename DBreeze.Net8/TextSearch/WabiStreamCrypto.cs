using System;
using System.Security.Cryptography;
using System.Text;
using DBreeze.Utils;

namespace DBreeze.TextSearch
{
    public interface ITextStreamCrypto
    {
        byte[] TextEncrypt(string text);
        string TextDecrypt(byte[] encryptedText);
    }

    /// <summary>
    /// Deterministic AES-CTR transform used by encrypted TextSearch tables.
    /// </summary>
    /// <remarks>
    /// The fixed IV is required to preserve encrypted prefixes for prefix search. Consequently,
    /// this transform intentionally does not hide repeated prefixes and does not authenticate the
    /// ciphertext. Use it only for the TextSearch storage protocol, not as a general-purpose AEAD.
    /// </remarks>
    public class WabiStreamCrypto:ITextStreamCrypto
    {
        private readonly byte[] Key;
        private readonly byte[] IV;

        public WabiStreamCrypto(string key, string iv)
        {
            if (key == null)
                throw new ArgumentNullException("key");
            if (iv == null)
                throw new ArgumentNullException("iv");

            byte[] keyBytes = key.ToByteArrayFromHex();
            byte[] ivBytes = iv.ToByteArrayFromHex();
            Validate(keyBytes, ivBytes);
            Key = (byte[])keyBytes.Clone();
            IV = (byte[])ivBytes.Clone();
        }

        public WabiStreamCrypto(byte[] key, byte[] iv)
        {
            Validate(key, iv);
            Key = (byte[])key.Clone();
            IV = (byte[])iv.Clone();
        }

        static void Validate(byte[] key, byte[] iv)
        {
            if (key == null)
                throw new ArgumentNullException("key");
            if (iv == null)
                throw new ArgumentNullException("iv");
            if (key.Length != 16 && key.Length != 24 && key.Length != 32)
                throw new ArgumentException("AES key must contain 16, 24 or 32 bytes.", "key");
            if (iv.Length != 16)
                throw new ArgumentException("AES-CTR IV must contain exactly 16 bytes.", "iv");
        }

        public class AesKeyInfo
        {
            public string Key { get; set; }
            public string IV { get; set; }
        }

        public static AesKeyInfo GenerateKey()
        {
            using (Aes aes = Aes.Create())
            {
                return new AesKeyInfo { IV = aes.IV.ToHexFromByteArray(), Key = aes.Key.ToHexFromByteArray() };
            }
        }

        public byte[] TextEncrypt(string inputText)
        {
            // In a Stream Cipher (XOR based), Encryption and Decryption are the EXACT same operation.
            // A ^ Key = B
            // B ^ Key = A
            if (inputText == null)
                throw new ArgumentNullException("inputText");
            byte[] result = new byte[Encoding.UTF8.GetByteCount(inputText)];
            Encoding.UTF8.GetBytes(inputText.AsSpan(), result.AsSpan());
            TransformInPlace(result);
            return result;
        }

        public string TextDecrypt(byte[] encryptedText)
        {
            if (encryptedText == null)
                return null;
            byte[] result = (byte[])encryptedText.Clone();
            TransformInPlace(result);
            return Encoding.UTF8.GetString(result);
        }

        private void TransformInPlace(Span<byte> data)
        {
            if (data.IsEmpty)
                return;

            // We use AES in ECB mode to generate a secure "Keystream" based on a Counter.
            // This is safe because we are using the output as a mask, not encrypting blocks directly.
            using Aes aes = Aes.Create();
            aes.Key = Key;

            Span<byte> counterBlock = stackalloc byte[16];
            Span<byte> keystreamBlock = stackalloc byte[16];
            IV.CopyTo(counterBlock);

            int processed = 0;
            while (processed < data.Length)
            {
                aes.EncryptEcb(counterBlock, keystreamBlock, PaddingMode.None);
                int toProcess = Math.Min(data.Length - processed, 16);
                for (int i = 0; i < toProcess; i++)
                    data[processed + i] ^= keystreamBlock[i];

                IncrementCounter(counterBlock);
                processed += toProcess;
            }
        }








        public string Helper_TextEncryptor_HEX(string input, bool encrypt = true)
        {
#if NET35 || NETr40
           if (string.IsNullOrEmpty(input)) return input;
#else
            if (string.IsNullOrWhiteSpace(input)) return input;

#endif


            // In a Stream Cipher (XOR based), Encryption and Decryption are the EXACT same operation.
            // A ^ Key = B
            // B ^ Key = A
            byte[] resultBytes;
            if (encrypt)
            {
                resultBytes = new byte[Encoding.UTF8.GetByteCount(input)];
                Encoding.UTF8.GetBytes(input.AsSpan(), resultBytes.AsSpan());
            }
            else
                resultBytes = Convert.FromHexString(input);
            TransformInPlace(resultBytes);

            if (encrypt)
            {
                // Must use Hex (or raw bytes) for WABI StartsWith to work. 
                // Base64 distorts prefixes.
                //return resultBytes.ToUTF8String();
                return resultBytes.ToHexFromByteArray();
                //return Convert.ToHexString(resultBytes);
            }
            else
            {
                return Encoding.UTF8.GetString(resultBytes);
            }
        }

        // Helper to increment the 16-byte counter (Big Endian logic)
        private static void IncrementCounter(Span<byte> counter)
        {
            for (int i = counter.Length - 1; i >= 0; i--)
            {
                if (++counter[i] != 0) break; // If no overflow, we are done
            }
        }

        
    }
}
