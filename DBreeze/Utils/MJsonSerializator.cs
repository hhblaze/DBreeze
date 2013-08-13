/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who thinks that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

//ref system.servicemodel.web
//ref system.runtime.serialization
//using System.Runtime.Serialization.Json;
//using System.IO; 

using DBreeze.Utils;
using DBreeze.Exceptions;

namespace DBreeze.Utils
{
    public static class JavascriptSerializator
    {
        /// <summary>
        /// Serializes object to JSON from Microsoft
        /// </summary>
        /// <param name="objectForSerialization"></param>
        /// <returns></returns>
        public static string SerializeMJSON(this object objectForSerialization)
        {
            try
            {
                System.Web.Script.Serialization.JavaScriptSerializer s = new System.Web.Script.Serialization.JavaScriptSerializer();

                return s.Serialize(objectForSerialization);
                
                //DataContractJsonSerializer s = new DataContractJsonSerializer(objectForSerialization.GetType());
                //using (MemoryStream stream1 = new MemoryStream())
                //{
                //    s.WriteObject(stream1, objectForSerialization);
                //    return Encoding.UTF8.GetString(stream1.GetBuffer());
                //}
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.MJSON_SERIALIZATION_ERROR, ex);
            }

        }

        /// <summary>
        /// Deserializes object from Microsoft JSON string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="str"></param>
        /// <returns></returns>
        public static T DeserializeMJSON<T>(this string str)
        {
            try
            {

                //DataContractJsonSerializer s = new DataContractJsonSerializer(typeof(T));

                //using (MemoryStream stream1 = new MemoryStream(Encoding.UTF8.GetBytes(str)))
                //{
                //    T obj = (T)s.ReadObject(stream1);
                //    return obj;
                //}

                System.Web.Script.Serialization.JavaScriptSerializer s = new System.Web.Script.Serialization.JavaScriptSerializer();
                return s.Deserialize<T>(str);
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.MJSON_DESERIALIZATION_ERROR, ex);
            }

        }
    }
}
