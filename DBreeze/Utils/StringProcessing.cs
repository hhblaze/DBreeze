/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who thinks that it should be free.
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
            return Encoding.ASCII.GetBytes(text);
        }

        public static byte[] To_UTF8Bytes(this string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        public static byte[] To_UnicodeBytes(this string text)
        {
            return Encoding.Unicode.GetBytes(text);
        }
    }
}
