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

namespace DBreeze.Utils
{
    public static class CustomSerializator
    {

        /*
         * For JSON http://json.codeplex.com/
            CustomSerializator.Serializator = JsonConvert.SerializeObject;
            CustomSerializator.Deserializator = JsonConvert.DeserializeObject;
         
         */
        /// <summary>
        /// Must be setup to be used together with DbCustomSerializer
        /// </summary>
        public static Func<object, string> Serializator = null;

        /// <summary>
        /// Must be setup to be used together with DbCustomSerializer
        /// </summary>
        public static Func<string, Type, object> Deserializator = null;

        /// <summary>
        /// Into byte array serializator can be supplied (Usually Protobuf.NET)
        /// </summary>
        public static Func<object, byte[]> ByteArraySerializator = null;
        /// <summary>
        /// From byte[] deserializator can be used (Usually Protobuf.NET)
        /// </summary>
        public static Func<byte[], Type, object> ByteArrayDeSerializator = null;


        /// <summary>
        /// Serializes object to JSON from Microsoft
        /// </summary>
        /// <param name="objectForSerialization"></param>
        /// <returns></returns>
        public static string SerializeCustom(this object objectForSerialization)
        {
            try
            {
                return Serializator(objectForSerialization);               
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.CUSTOM_SERIALIZATION_ERROR, ex);
            }

        }
             

        /// <summary>
        /// Deserializes object from Microsoft JSON string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="str"></param>
        /// <returns></returns>
        public static T DeserializeCustom<T>(this string str)
        {
            try
            {

                return (T)Deserializator(str,typeof(T));                
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.CUSTOM_DESERIALIZATION_ERROR, ex);
            }

        }

    }
}
