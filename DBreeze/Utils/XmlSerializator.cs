/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who thinks that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.Utils;
using DBreeze.Exceptions;

namespace DBreeze.Utils
{
    
    public static class XmlSerializator
    {
        /// <summary>
        /// Serializes object to XML string
        /// </summary>
        /// <param name="objectForSerialization"></param>
        /// <returns></returns>
        public static string SerializeXml(this object objectForSerialization)
        {
            try
            {
                System.Xml.Serialization.XmlSerializer xs = new System.Xml.Serialization.XmlSerializer(objectForSerialization.GetType());

                string r = String.Empty;

                using (System.IO.StringWriter wr = new System.IO.StringWriter())
                {
                    xs.Serialize(wr, objectForSerialization);
                    r = wr.GetStringBuilder().ToString();
                    wr.Close();
                }

                return r;
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.XML_SERIALIZATION_ERROR,ex);                  
            }

        }

        /// <summary>
        /// Deserializes object from XML string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="str"></param>
        /// <returns></returns>
        public static T DeserializeXml<T>(this string str)
        {
            try
            {
                System.Xml.Serialization.XmlSerializer xs = new System.Xml.Serialization.XmlSerializer(typeof(T));

                object r = null;
                using (System.IO.StringReader sr = new System.IO.StringReader(str))
                {
                    r = xs.Deserialize(new System.IO.StringReader(str));
                    sr.Close();
                }

                return (T)r;
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.XML_DESERIALIZATION_ERROR, ex);                
            }

        }
    }
}
