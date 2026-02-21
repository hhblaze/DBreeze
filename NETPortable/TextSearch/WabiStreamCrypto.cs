using DBreeze.Utils;
using System;

namespace DBreeze.TextSearch
{
    public interface ITextStreamCrypto
    {
        byte[] TextEncrypt(string text);
        string TextDecrypt(byte[] encryptedText);
    }

    public class WabiStreamCrypto: ITextStreamCrypto
    {
        public string TextDecrypt(byte[] encryptedText)
        {
            return encryptedText.ToUTF8String();
        }

        public byte[] TextEncrypt(string text)
        {
            return text.To_UTF8Bytes();
        }

        /// <summary>
        /// CAP
        /// </summary>
        /// <param name="input"></param>
        /// <param name="encrypt"></param>
        /// <returns></returns>
        public string TextEncryptor(string input, bool encrypt = true)
        {
            return input;
        }
    }
}
