using DBreeze.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace TesterNet6
{
    internal static class TestEncryptedTextSearch
    {
        public static void TestEncryption()
        {
            //PathToDatabase = @"D:\Temp\DBVector";
            string _tblText = "tbltextSearch";

            
            //var a = DBreeze.TextSearch.WabiStreamCrypto.GenerateKey();
            //e.g. a = ("D47A20DDB561C0D0964960738DE8647EB8D5179FAF9472B118AEB4548FC0B3B6", "066A9BF9AC98706DFC74198AA5553419")
            DBreeze.TextSearch.WabiStreamCrypto wsc = new DBreeze.TextSearch.WabiStreamCrypto
                ("D47A20DDB561C0D0964960738DE8647EB8D5179FAF9472B118AEB4548FC0B3B6", "066A9BF9AC98706DFC74198AA5553419");

            Func<string, bool, string> encryptorF = wsc.TextEncryptor;

            Func<string, string> reverseString = (a) =>
            {
                return new string(a.ToLower().Reverse().ToArray());
            };

            //Testing encrpytion:
            //
            string h1 = encryptorF("Hello", true);
            string h2 = encryptorF("ello", true);
            string h3 = encryptorF("aHal", true);

            string h4 = encryptorF("Deer", true);
            string h5 = encryptorF("Deer,", true);

            string a = encryptorF(reverseString("Привет"), true);
            string b = encryptorF(reverseString("ривет"), true);

            string c = reverseString(encryptorF(a, false));
            string d = reverseString(encryptorF(b, false));

            //return;

            Program.DBEngine.Scheme.DeleteTable(_tblText);

            //using (var tran = Program.DBEngine.GetTransaction())
            //{
            //    tran.TextInsert(_tblText, ((long)1).ToBytes(), "Hello my dear deer, feel at home on the edge of the forest", fullMatchWords: "[GROUP_SAAB]");

            //    tran.TextInsert(_tblText, ((long)2).ToBytes(), "Привет, мой дорогой олень, чувствуй себя как дома на опушке леса", fullMatchWords: "[GROUP_SAAB]");

            //    tran.Commit();
            //}

            //using (var tran = Program.DBEngine.GetTransaction())
            //{
            //    var ts = tran.TextSearch(_tblText);
            //    //var ts = tran.TextSearch(_tblText, textEncryptor: null);

            //    foreach (var el in ts.Block("deer", fullMatchWords: "[GROUP_SAAB]").GetDocumentIDs())
            //    {
            //        Debug.WriteLine(el.To_Int64_BigEndian());
            //    }

            //    foreach (var el in ts.Block("deer home ello", fullMatchWords: "[GROUP_SAAB]").GetDocumentIDs())
            //    {
            //        Debug.WriteLine(el.To_Int64_BigEndian());
            //    }

            //    foreach (var el in ts.Block("дорог пушке", fullMatchWords: "[GROUP_SAAB]").GetDocumentIDs())
            //    {
            //        Debug.WriteLine(el.To_Int64_BigEndian());
            //    }

            //}


            using (var tran = Program.DBEngine.GetTransaction())
            {
                tran.TextInsert(_tblText, ((long)1).ToBytes(), "Hello my dear deer, feel at home on the edge of the forest", fullMatchWords: "[GROUP_SAAB]",
                    textEncryptor: wsc);

                //tran.TextInsert(_tblText, ((long)1).ToBytes(), "Hello my dear deer ,", fullMatchWords: "[GROUP_SAAB]",
                //   textEncryptor: wsc);

                //tran.TextInsert(_tblText, ((long)1).ToBytes(), "Hello my dear ,deer,", fullMatchWords: "[GROUP_SAAB]",
                //  textEncryptor: null);


                //tran.TextAppend

                tran.TextInsert(_tblText, ((long)2).ToBytes(), "Привет, мой дорогой олень, чувствуй себя как дома на опушке леса", fullMatchWords: "[GROUP_SAAB]",
                    textEncryptor: wsc);

                tran.Commit();
            }

            //encryptorF = null;

            //using (var tran = Program.DBEngine.GetTransaction())
            //{
            //    foreach (var el in tran.TextGetDocumentsSearchables(_tblText, new HashSet<byte[]> { ((long)1).ToBytes(), ((long)2).ToBytes() }, textEncryptor: wsc))
            //    {

            //    }
            //}


            //using (var tran = Program.DBEngine.GetTransaction())
            //{
            //    tran.TextInsert(_tblText,((long)1).ToBytes(), "Whatever, Hello my dear deer, feel at home on the edge of the forest", fullMatchWords: "[GROUP_SAAB]",
            //        textEncryptor: encryptorF);

            //    //tran.TextInsert(_tblText, ((long)1).ToBytes(), "Привет, мой дорогой олень, чувствуй себя как дома на опушке леса", fullMatchWords: "[GROUP_SAAB]",
            //    //    textEncryptor: encryptorF);

            //    tran.Commit();
            //}



            using (var tran = Program.DBEngine.GetTransaction())
            {
                var ts = tran.TextSearch(_tblText, textEncryptor: wsc);
                //var ts = tran.TextSearch(_tblText, textEncryptor: null);

                foreach (var el in ts.Block("deer", fullMatchWords: "[GROUP_SAAB]").GetDocumentIDs())
                {
                    Debug.WriteLine(el.To_Int64_BigEndian());
                }

                foreach (var el in ts.Block("deer home ello", fullMatchWords: "[GROUP_SAAB]").GetDocumentIDs())
                {
                    Debug.WriteLine(el.To_Int64_BigEndian());
                }

                foreach (var el in ts.Block("дорог пушке", fullMatchWords: "[GROUP_SAAB]").GetDocumentIDs())
                {
                    Debug.WriteLine(el.To_Int64_BigEndian());
                }

            }

            //using (var tran = Program.DBEngine.GetTransaction())
            //{
            //    tran.TextRemoveAll(_tblText, ((long)2).ToBytes(), textEncryptor: null);
            //    tran.Commit();
            //}

            //using (var tran = Program.DBEngine.GetTransaction())
            //{
            //    var ts = tran.TextSearch(_tblText, textEncryptor: wsc);
            //    //var ts = tran.TextSearch(_tblText, textEncryptor: null);

            //    foreach (var el in ts.Block("deer", fullMatchWords: "[GROUP_SAAB]").GetDocumentIDs())
            //    {
            //        Debug.WriteLine(el.To_Int64_BigEndian());
            //    }

            //    foreach (var el in ts.Block("deer home ello", fullMatchWords: "[GROUP_SAAB]").GetDocumentIDs())
            //    {
            //        Debug.WriteLine(el.To_Int64_BigEndian());
            //    }

            //    foreach (var el in ts.Block("дорог пушке", fullMatchWords: "[GROUP_SAAB]").GetDocumentIDs())
            //    {
            //        Debug.WriteLine(el.To_Int64_BigEndian());
            //    }

            //}

            //using (var tran = Program.DBEngine.GetTransaction())
            //{
            //    tran.TextInsert(_tblText, 1.ToBytes(), "Hello my dear deer, feel at home on the edge of the forest",
            //        textEncryptor: encryptorF);

            //    tran.TextInsert(_tblText, 2.ToBytes(), "Привет, мой дорогой олень, чувствуй себя как дома на опушке леса",
            //        textEncryptor: encryptorF);

            //    tran.Commit();
            //}

            //using (var tran = Program.DBEngine.GetTransaction())
            //{
            //    var ts = tran.TextSearch(_tblText, textEncryptor: encryptorF);
            //    foreach (var el in ts.Block("deer").GetDocumentIDs())
            //    {
            //        Debug.WriteLine(el.To_Int16_BigEndian());
            //    }

            //    foreach (var el in ts.Block("deer home ello").GetDocumentIDs())
            //    {
            //        Debug.WriteLine(el.To_Int16_BigEndian());
            //    }

            //    foreach (var el in ts.Block("дорог пушке").GetDocumentIDs())
            //    {
            //        Debug.WriteLine(el.To_Int16_BigEndian());
            //    }

            //}

        }


        //public static class WabiSecurity
        //{
        //    // 32 bytes for AES-256. 
        //    // IN PRODUCTION: Load this from Azure KeyVault, Environment Variables, or a secure config.
        //    // Do not hardcode this in a real application.
        //    private static readonly byte[] Key = Encoding.UTF8.GetBytes("12345678901234567890123456789012");

        //    // We use a static empty IV (16 bytes of zeros) to ensure Deterministic Encryption.
        //    // This ensures "Hello" always encrypts to the same string, allowing DB lookups.
        //    private static readonly byte[] StaticIV = new byte[16];

        //    public static string TextEncryptor(string input, bool encrypt = true)
        //    {
        //        if (string.IsNullOrWhiteSpace(input))
        //            return input;

        //        try
        //        {
        //            return encrypt ? DoEncryption(input) : DoDecryption(input);
        //        }
        //        catch
        //        {
        //            // In a search engine context, if decryption fails (bad data), 
        //            // usually returning the original input or null is safer than crashing.
        //            return input;
        //        }
        //    }

        //    private static string DoEncryption(string plainText)
        //    {
        //        using Aes aes = Aes.Create();
        //        aes.Key = Key;
        //        aes.IV = StaticIV;
        //        aes.Mode = CipherMode.CBC;
        //        aes.Padding = PaddingMode.PKCS7;

        //        // Create the encryptor
        //        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

        //        // Convert string to bytes
        //        byte[] inputBytes = Encoding.UTF8.GetBytes(plainText);

        //        // Encrypt
        //        byte[] encryptedBytes = encryptor.TransformFinalBlock(inputBytes, 0, inputBytes.Length);

        //        // Return Base64 (Standard compact text representation)
        //        return Convert.ToBase64String(encryptedBytes);
        //    }

        //    private static string DoDecryption(string cipherText)
        //    {
        //        using Aes aes = Aes.Create();
        //        aes.Key = Key;
        //        aes.IV = StaticIV;
        //        aes.Mode = CipherMode.CBC;
        //        aes.Padding = PaddingMode.PKCS7;

        //        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

        //        // Convert Base64 back to bytes
        //        byte[] cipherBytes = Convert.FromBase64String(cipherText);

        //        // Decrypt
        //        byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        //        // Return string
        //        return Encoding.UTF8.GetString(plainBytes);
        //    }
        //}
    }
}
