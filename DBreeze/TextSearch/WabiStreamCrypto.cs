using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DBreeze.Utils;

namespace DBreeze.TextSearch
{
    public class WabiStreamCrypto
    {
        //// 32 bytes for AES-256
        private byte[] Key = null;
        //private byte[] Key = Encoding.UTF8.GetBytes("12345678901234567890123456789012");

        //// Fixed IV allows the 'Keystream' to be deterministic
        private byte[] IV = null;
        //private byte[] IV = new byte[16];

        public WabiStreamCrypto(string key, string iv)
        {
            Key = key.ToByteArrayFromHex();
            IV = iv.ToByteArrayFromHex();
        }

        public WabiStreamCrypto(byte[] key, byte[] iv)
        {
            Key = key;
            IV = iv;
        }

        //#if NET35 || NETr40
        //public class AesKeyInfo
        //        {
        //            public string Key { get; set; }
        //            public string IV { get; set; }
        //        }

        //        public static AesKeyInfo GenerateKey()
        //        {
        //            using (Aes aes = Aes.Create())
        //            {
        //                return new AesKeyInfo { IV = aes.IV.ToHexFromByteArray(), Key = aes.Key.ToHexFromByteArray() };
        //            }
        //        }
        //#else
        //        public static (string key, string IV) GenerateKey()
        //        {
        //            using (Aes aes = Aes.Create())
        //            {
        //                return (aes.Key.ToHexFromByteArray(), aes.IV.ToHexFromByteArray());
        //            }
        //        }

        //#endif

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


        public string TextEncryptor(string input, bool encrypt = true)
        {
#if NET35 || NETr40
           if (string.IsNullOrEmpty(input)) return input;
#else
            if (string.IsNullOrWhiteSpace(input)) return input;

#endif


            // In a Stream Cipher (XOR based), Encryption and Decryption are the EXACT same operation.
            // A ^ Key = B
            // B ^ Key = A
            byte[] resultBytes = Transform(input, encrypt);

            if (encrypt)
            {
                // Must use Hex (or raw bytes) for WABI StartsWith to work. 
                // Base64 distorts prefixes.
                return resultBytes.ToUTF8String();
                //return resultBytes.ToHexFromByteArray();
                //return Convert.ToHexString(resultBytes);
            }
            else
            {
                return Encoding.UTF8.GetString(resultBytes);
            }
        }

        private byte[] Transform(string input, bool isEncrypting)
        {
            byte[] inputBytes;

            if (isEncrypting)
                inputBytes = Encoding.UTF8.GetBytes(input);
            else
            {
                inputBytes = input.To_UTF8Bytes();
                //inputBytes = input.ToByteArrayFromHex();
                //inputBytes = Convert.FromHexString(input);
            }



            byte[] outputBytes = new byte[inputBytes.Length];

            // We use AES in ECB mode to generate a secure "Keystream" based on a Counter.
            // This is safe because we are using the output as a mask, not encrypting blocks directly.
            using (Aes aes = Aes.Create())
            {
                aes.Key = Key;
                aes.Mode = CipherMode.ECB; // ECB is used ONLY to generate the keystream
                aes.Padding = PaddingMode.None;

                using (var encryptor = aes.CreateEncryptor())
                {
                    byte[] counterBlock = new byte[16];
                    byte[] keystreamBlock = new byte[16];

                    // Copy Static IV to counter
                    Array.Copy(IV, counterBlock, 16);

                    int processed = 0;
                    while (processed < inputBytes.Length)
                    {
                        // 1. Generate 16 bytes of random noise (Keystream)
                        encryptor.TransformBlock(counterBlock, 0, 16, keystreamBlock, 0);

                        // 2. XOR the input with the Keystream
                        int remaining = inputBytes.Length - processed;
                        int toProcess = Math.Min(remaining, 16);

                        for (int i = 0; i < toProcess; i++)
                        {
                            outputBytes[processed + i] = (byte)(inputBytes[processed + i] ^ keystreamBlock[i]);
                        }

                        // 3. Increment Counter for next block (Standard AES-CTR logic)
                        IncrementCounter(counterBlock);
                        processed += 16;
                    }
                }
            }

            return outputBytes;
        }

        // Helper to increment the 16-byte counter (Big Endian logic)
        private void IncrementCounter(byte[] counter)
        {
            for (int i = counter.Length - 1; i >= 0; i--)
            {
                if (++counter[i] != 0) break; // If no overflow, we are done
            }
        }
    }
}
