/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBreeze.Utils
{
    public static class StringProcessing
    {
        public static byte[] To_AsciiBytes(this string text)
        {
            return Encoding.UTF8.GetBytes(text);
            //return Encoding.ASCII.GetBytes(text);
        }

        public static byte[] To_UTF8Bytes(this string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        public static byte[] To_UnicodeBytes(this string text)
        {
            return Encoding.Unicode.GetBytes(text);
        }


        public static string UTF8_GetString(this byte[] btText)
        {
            return btText == null ? null : System.Text.Encoding.UTF8.GetString(btText, 0, btText.Length);
        }
        public static string Unicode_GetString(this byte[] btText)
        {
            return btText == null ? null : System.Text.Encoding.Unicode.GetString(btText, 0, btText.Length);
        }
        public static string Ascii_GetString(this byte[] btText)
        {
            //HERE UTF8 is correct
            return btText == null ? null : System.Text.Encoding.UTF8.GetString(btText, 0, btText.Length);
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

            var regex = new System.Text.RegularExpressions.Regex(String.Join("|", replaceWith.Keys.Select(k => System.Text.RegularExpressions.Regex.Escape(k))));
            return regex.Replace(input, m => replaceWith[m.Value]);
        }

    }
}
