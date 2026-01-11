/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using DBreeze.Exceptions;
using System.Text.Json;

namespace DBreeze.Utils
{
    public static class JavascriptSerializator
    {
        private static readonly JsonSerializerOptions _defaultSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true, // Compatibility with System.Web.Script.Serialization.JavaScriptSerializer
                                                //PropertyNamingPolicy = JsonNamingPolicy.CamelCase, 
                                                // WriteIndented = true, // For pretty-printing, usually for debugging, not production
                                                // Add other options as needed, e.g., converters, reference handling
            /*
             Feature	            JavaScriptSerializer (Default)	                    System.Text.Json (Default)	System.Text.Json with JsonNamingPolicy.CamelCase	System.Text.Json with PropertyNameCaseInsensitive = true	System.Text.Json with CamelCase & CaseInsensitive
            Serialization Naming	As-is (e.g., MyProperty -> {"MyProperty":...} )	    As-is (e.g., MyProperty -> {"MyProperty":...} )	CamelCase (e.g., MyProperty -> {"myProperty":...} )	As-is (e.g., MyProperty -> {"MyProperty":...} )	CamelCase (e.g., MyProperty -> {"myProperty":...} )
            Deserialization Case	Case-Insensitive	                                Case-Sensitive	Case-Sensitive (but understands the policy for mapping)	Case-Insensitive	Case-Insensitive
             */
        };

        /// <summary>
        /// Serializes object to JSON using .NET System.Text.Json.JsonSerializer
        /// </summary>
        /// <param name="objectForSerialization"></param>
        /// <returns></returns>
        public static string SerializeMJSON(this object objectForSerialization)
        {
            try
            {

                // If objectForSerialization is null, JsonSerializer.Serialize will return the string "null"
                // which is consistent with JavaScriptSerializer.
                return JsonSerializer.Serialize(objectForSerialization, _defaultSerializerOptions);
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.MJSON_SERIALIZATION_ERROR, ex);
            }

        }

        /// <summary>
        /// Deserializes object from JSON string using .NET System.Text.Json.JsonSerializer
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="str"></param>
        /// <returns></returns>
        public static T DeserializeMJSON<T>(this string str)
        {
            try
            {
                if (string.IsNullOrEmpty(str))
                {
                    // JavaScriptSerializer would throw ArgumentException for empty string,
                    // or ArgumentNullException for null.
                    // JsonSerializer.Deserialize<T>(null) throws ArgumentNullException.
                    // JsonSerializer.Deserialize<T>("") throws JsonException.
                    // You might want to decide if an empty or null string should return default(T)
                    // or let the serializer throw. Current behavior matches letting it throw.
                    // If you want to return default(T) for null/empty:
                    //return default(T);
                }
                return JsonSerializer.Deserialize<T>(str, _defaultSerializerOptions);
               
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.MJSON_DESERIALIZATION_ERROR, ex);
            }

        }
    }
}
