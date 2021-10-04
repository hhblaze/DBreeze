/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBreeze.Utils
{
    /// <summary>
    /// Set of string processing extensions
    /// </summary>
    public static class StringProcessing
    {
        /// <summary>
        /// Encoding.ASCII.GetBytes
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static byte[] To_AsciiBytes(this string text)
        {
            return Encoding.ASCII.GetBytes(text);
        }

        /// <summary>
        /// Encoding.UTF8.GetBytes
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static byte[] To_UTF8Bytes(this string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        /// <summary>
        /// Encoding.Unicode.GetBytes
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static byte[] To_UnicodeBytes(this string text)
        {   
            return Encoding.Unicode.GetBytes(text);
        }

        /// <summary>
        /// UTF8.GetString
        /// </summary>
        /// <param name="btText"></param>
        /// <returns></returns>
        public static string UTF8_GetString(this byte[] btText)
        {
            return btText == null ? null : System.Text.Encoding.UTF8.GetString(btText, 0, btText.Length);
        }

        /// <summary>
        /// Unicode.GetString
        /// </summary>
        /// <param name="btText"></param>
        /// <returns></returns>
        public static string Unicode_GetString(this byte[] btText)
        {
            return btText == null ? null : System.Text.Encoding.Unicode.GetString(btText, 0, btText.Length);
        }

        /// <summary>
        /// ASCII.GetString
        /// </summary>
        /// <param name="btText"></param>
        /// <returns></returns>
        public static string Ascii_GetString(this byte[] btText)
        {
            //HERE UTF8 is correct
            return btText == null ? null : System.Text.Encoding.ASCII.GetString(btText, 0, btText.Length);
        }

        /// <summary>
        /// <para>Will efficiently replace multiple string by supplied templates.</para>
        /// var replacements = new Dictionary&lt;string, string&gt;()
        /// {
        ///   {"big","hot"},
        ///   {"mac","dog"}
        /// };       
        /// </summary>
        /// <param name="input"></param>
        /// <param name="replaceWith"></param>
        /// <returns></returns>
        public static string ReplaceMultiple(this string input, Dictionary<string, string> replaceWith)
        {
            if (input == null || replaceWith == null || replaceWith.Count < 1)
                return input;

            replaceWith = replaceWith.OrderByDescending(r => r.Key.Length).ToDictionary(r => r.Key, r => r.Value);

            System.Text.RegularExpressions.Regex regex = null;
#if NET35
            //|| NETr40   //The same must be use for .NET 4.0
            regex = new System.Text.RegularExpressions.Regex(String.Join("|", replaceWith.Keys.Select(k => System.Text.RegularExpressions.Regex.Escape(k)).Cast<string>().ToArray() ));

#else
            regex = new System.Text.RegularExpressions.Regex(String.Join("|", replaceWith.Keys.Select(k => System.Text.RegularExpressions.Regex.Escape(k))));
                        
#endif

            return regex.Replace(input, m => replaceWith[m.Value]);

        }

        /// <summary>
        /// Compares two strings, when applyStr2ToStr1 is true and strings are differnt then str1 = str2 assignment.
        /// </summary>
        /// <param name="str1"></param>
        /// <param name="str2"></param>
        /// <param name="applyStr2ToStr1">Default false. str2 will be assigned to str1 when there is a difference in comparation</param>
        /// <returns>Returns result of comparation of two strings</returns>
        public static bool StringCompare(this string str1, string str2, bool applyStr2ToStr1 = false)
        {
            var cmp = (str1 ?? string.Empty) != (str2 ?? string.Empty);
            if (applyStr2ToStr1 && !cmp)
                str1 = str2;
            return cmp;
        }
    }
}
