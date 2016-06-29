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
    public class DbUTF8
    {
        string _text = String.Empty;

        public DbUTF8(string text)
        {
            _text = text;
        }

        public DbUTF8(byte[] btText)
        {
            if (btText == null)
                _text = null;
            else
                _text = System.Text.Encoding.UTF8.GetString(btText);            
        }
        
        public byte[] GetBytes()
        {
            if (_text == null)
                return null;

            return System.Text.Encoding.UTF8.GetBytes(_text);
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

        public static implicit operator DbUTF8(string value)
        {
            return new DbUTF8(value);
        }
        /******************/
    }
}
