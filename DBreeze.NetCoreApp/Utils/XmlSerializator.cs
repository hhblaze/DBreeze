/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.Utils;
using DBreeze.Exceptions;

using System.Text.RegularExpressions;

namespace DBreeze.Utils
{

    internal static class XmlSerializator
    {
        /// <summary>
        /// Serializes object to XML string
        /// </summary>
        /// <param name="objectForSerialization"></param>
        /// <returns></returns>
        //public static string SerializeXml(this object objectForSerialization)
        public static string SerializeXml(this List<string> objectForSerialization)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var el in objectForSerialization)
            {
                sb.Append("<string>" + el + "</string>\n");
            }
            return sb.ToString();

            //System.Xml.Serialization.XmlSerializer xs = new System.Xml.Serialization.XmlSerializer(objectForSerialization.GetType());

            //string r = String.Empty;

            //using (System.IO.StringWriter wr = new System.IO.StringWriter())
            //{
            //    xs.Serialize(wr, objectForSerialization);
            //    r = wr.GetStringBuilder().ToString();
            //}

            //return r;


        }

        /// <summary>
        /// Deserializes object from XML string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="str"></param>
        /// <returns></returns>
        public static List<string> DeserializeXml<T>(this string str)
        //public static T DeserializeXml<T>(this string str)
        {
            List<string> r = new List<string>();
            Regex regex = new Regex("<string>(.*)</string>");
            var mtch = regex.Matches(str);
            for (int i = 0; i < mtch.Count; i++)
            {
                r.Add(mtch[i].Groups[1].Value);
            }
            return r;

            //System.Xml.Serialization.XmlSerializer xs = new System.Xml.Serialization.XmlSerializer(typeof(T));

            //object r = null;
            //using (System.IO.StringReader sr = new System.IO.StringReader(str))
            //{
            //    r = xs.Deserialize(new System.IO.StringReader(str));
            //}

            //return (T)r;

        }
    }
}



///* 
//  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
//  It's a free software for those, who think that it should be free.
//*/

//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

//using DBreeze.Utils;
//using DBreeze.Exceptions;

//namespace DBreeze.Utils
//{

//    public static class XmlSerializator
//    {
//        /// <summary>
//        /// Serializes object to XML string
//        /// </summary>
//        /// <param name="objectForSerialization"></param>
//        /// <returns></returns>
//        public static string SerializeXml(this object objectForSerialization)
//        {
//            System.Xml.Serialization.XmlSerializer xs = new System.Xml.Serialization.XmlSerializer(objectForSerialization.GetType());

//            string r = String.Empty;

//            using (System.IO.StringWriter wr = new System.IO.StringWriter())
//            {
//                xs.Serialize(wr, objectForSerialization);
//                r = wr.GetStringBuilder().ToString();
//            }

//            return r;

//            //try
//            //{
//            //    System.Xml.Serialization.XmlSerializer xs = new System.Xml.Serialization.XmlSerializer(objectForSerialization.GetType());

//            //    string r = String.Empty;

//            //    using (System.IO.StringWriter wr = new System.IO.StringWriter())
//            //    {
//            //        xs.Serialize(wr, objectForSerialization);
//            //        r = wr.GetStringBuilder().ToString();                    
//            //    }

//            //    return r;
//            //}
//            //catch (Exception ex)
//            //{
//            //    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.XML_SERIALIZATION_ERROR,ex);                  
//            //}

//        }

//        /// <summary>
//        /// Deserializes object from XML string
//        /// </summary>
//        /// <typeparam name="T"></typeparam>
//        /// <param name="str"></param>
//        /// <returns></returns>
//        public static T DeserializeXml<T>(this string str)
//        {
//            System.Xml.Serialization.XmlSerializer xs = new System.Xml.Serialization.XmlSerializer(typeof(T));

//            object r = null;
//            using (System.IO.StringReader sr = new System.IO.StringReader(str))
//            {
//                r = xs.Deserialize(new System.IO.StringReader(str));
//            }

//            return (T)r;

//            //try
//            //{
//            //    System.Xml.Serialization.XmlSerializer xs = new System.Xml.Serialization.XmlSerializer(typeof(T));

//            //    object r = null;
//            //    using (System.IO.StringReader sr = new System.IO.StringReader(str))
//            //    {
//            //        r = xs.Deserialize(new System.IO.StringReader(str));                    
//            //    }

//            //    return (T)r;
//            //}
//            //catch (Exception ex)
//            //{
//            //    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.XML_DESERIALIZATION_ERROR, ex);                
//            //}

//        }
//    }
//}
