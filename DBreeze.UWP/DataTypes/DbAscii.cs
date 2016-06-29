/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBreeze.DataTypes
{
    /// <summary>
    /// String.Empty is will return NULL
    /// </summary>
    public class DbAscii
    {
        string _text = String.Empty;

        public DbAscii(string text)
        {
            _text = text;
        }

        public DbAscii(byte[] btText)
        {
            if (btText == null)
                _text = null;
            else
                _text = System.Text.Encoding.ASCII.GetString(btText);            
        }
        
        public byte[] GetBytes()
        {
            if (_text == null)
                return null;

            return System.Text.Encoding.ASCII.GetBytes(_text);
        }

        /// <summary>
        /// Returns string from the object
        /// </summary>
        public string Get
        {
            get
            {
                return this._text;
            }
            set
            {
                this._text = value;
            }
        }

        /*They both needed to achive effect of string*/
        public override string ToString()
        {
            return _text;
        }

        public static implicit operator DbAscii(string value)
        {
            return new DbAscii(value);
        }
        /******************/
    }
}
