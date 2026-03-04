/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/


using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.Utils;
using DBreeze.Exceptions;

using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.IO;
using System.Xml.Serialization;
using System.Xml;

namespace DBreeze.Utils
{
    /*
     using System;
using System.Collections.Generic;
using System.Xml.Serialization;

[XmlRoot("Order")]
public class Order
{
    [XmlAttribute("id")]
    public int Id { get; set; }

    [XmlElement("CustomerName")]
    public string CustomerName { get; set; }

    [XmlElement("OrderDate")]
    public DateTime OrderDate { get; set; }

    [XmlArray("Items")]
    [XmlArrayItem("Item")]
    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    [XmlAttribute("sku")]
    public string SKU { get; set; }

    [XmlElement("ProductName")]
    public string ProductName { get; set; }

    [XmlElement("Quantity")]
    public int Quantity { get; set; }

    [XmlElement("Price")]
    public decimal Price { get; set; }
}
     */

    public static class XmlSerializator
    {
#if NETFRAMEWORK || NETCOREAPP3_1 || NETSTANDARD2_1 || NET6FUNC || NETPORTABLE_1

        // Serializer cache (critical for performance)
        private static readonly ConcurrentDictionary<Type, XmlSerializer> _serializerCache
            = new ConcurrentDictionary<Type, XmlSerializer>();

        private static XmlSerializer GetSerializer(Type type)
        {
            XmlSerializer serializer;
            if (!_serializerCache.TryGetValue(type, out serializer))
            {
                serializer = new XmlSerializer(type);
                serializer = _serializerCache.GetOrAdd(type, serializer);
            }
            return serializer;
        }

        /// <summary>
        /// High-performance XML serialization
        /// </summary>
        internal static string SerializeXml(this object obj)
        {
            if (obj is null)
                throw new ArgumentNullException(nameof(obj));

            var serializer = GetSerializer(obj.GetType());

            var settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                Encoding = Encoding.UTF8,
                Indent = false
            };

            using (var sw = new StringWriter())
            {
                using (var writer = XmlWriter.Create(sw, settings))
                {
                    serializer.Serialize(writer, obj);
#if !NETPORTABLE
                writer.Close();
#endif
                }

                return sw.ToString();
            }

        }
#endif

        /// <summary>
        /// Special support old DBreeze schemas
        /// </summary>
        /// <param name="objectForSerialization"></param>
        /// <returns></returns>
        internal static string SerializeXml_List(this List<string> objectForSerialization)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var el in objectForSerialization)
            {
                sb.Append("<string>" + el + "</string>\n");
            }
            return sb.ToString();
        }


#if NETFRAMEWORK || NETCOREAPP3_1 || NETSTANDARD2_1 || NET6FUNC || NETPORTABLE_1

        ///// <summary>
        ///// High-performance XML deserialization
        ///// </summary>
        //public static T DeserializeXml<T>(this string xml)
        //{
        //    if (string.IsNullOrWhiteSpace(xml))
        //        throw new ArgumentException("XML input is null or empty.", nameof(xml));

        //    var serializer = GetSerializer(typeof(T));

        //    using var sr = new StringReader(xml);
        //    var result = serializer.Deserialize(sr);

        //    return (T)result!;
        //}

        internal static T DeserializeXml<T>(this string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                throw new ArgumentException("XML input is null or empty.", nameof(xml));

            var serializer = GetSerializer(typeof(T));

            using (var sr = new StringReader(xml))
            {
                object result = serializer.Deserialize(sr);
                return (T)result;
            }
        }
#endif

        /// <summary>
        /// Special support old DBreeze schemas
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="str"></param>
        /// <returns></returns>
        internal static List<string> DeserializeXml_List<T>(this string str)
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



    //internal static class XmlSerializator1
    //{
    //    /// <summary>
    //    /// Serializes object to XML string
    //    /// </summary>
    //    /// <param name="objectForSerialization"></param>
    //    /// <returns></returns>
    //    //public static string SerializeXml(this object objectForSerialization)
    //    internal static string SerializeXml(this List<string> objectForSerialization)
    //    {
    //        StringBuilder sb = new StringBuilder();
    //        foreach (var el in objectForSerialization)
    //        {
    //            sb.Append("<string>" + el + "</string>\n");
    //        }
    //        return sb.ToString();

    //        //System.Xml.Serialization.XmlSerializer xs = new System.Xml.Serialization.XmlSerializer(objectForSerialization.GetType());

    //        //string r = String.Empty;

    //        //using (System.IO.StringWriter wr = new System.IO.StringWriter())
    //        //{
    //        //    xs.Serialize(wr, objectForSerialization);
    //        //    r = wr.GetStringBuilder().ToString();
    //        //}

    //        //return r;


    //    }

    //    /// <summary>
    //    /// Deserializes object from XML string
    //    /// </summary>
    //    /// <typeparam name="T"></typeparam>
    //    /// <param name="str"></param>
    //    /// <returns></returns>
    //    internal static List<string> DeserializeXml<T>(this string str)
    //    //public static T DeserializeXml<T>(this string str)
    //    {
    //        List<string> r = new List<string>();
    //        Regex regex = new Regex("<string>(.*)</string>");
    //        var mtch = regex.Matches(str);
    //        for (int i = 0; i < mtch.Count; i++)
    //        {
    //            r.Add(mtch[i].Groups[1].Value);
    //        }
    //        return r;

    //        //System.Xml.Serialization.XmlSerializer xs = new System.Xml.Serialization.XmlSerializer(typeof(T));

    //        //object r = null;
    //        //using (System.IO.StringReader sr = new System.IO.StringReader(str))
    //        //{
    //        //    r = xs.Deserialize(new System.IO.StringReader(str));
    //        //}

    //        //return (T)r;

    //    }
    //}
}



 
//  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
//  It's free software for those who think that it should be free.
//

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
