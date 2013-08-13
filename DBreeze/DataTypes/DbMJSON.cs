/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who thinks that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.Utils;

namespace DBreeze.DataTypes
{
    /// <summary>
    /// <para>Microsoft JSON</para>
    /// <para></para>
    /// <para>Possible to make like this, without class instantiating:</para>
    /// <para>List&lt;string&gt; aa = new List&lt;string&gt;();</para>
    /// <para>aa.Add("test1")</para>
    /// <para>aa.Add("test2")</para>
    /// <para>-----inside of transaction---------</para>
    /// <para>tran.Insert&lt;uint, DbMJSON&lt;List&lt;string&gt;&gt;&gt;("t1", i, aa);</para>
    /// <para>-----------------------------------</para>
    /// <para>foreach (var row in tran.SelectForward&lt;uint, DbMJSON&lt;List&lt;string&gt;&gt;&gt;("t1"))</para>
    /// <para>Console.WriteLine("K: {0}; V: {1}", row.Key.ToString(), (row.Value == null) ? "NULL" : row.Value.Get.Count().ToString());</para>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class DbMJSON<T>:IDBConvertable
    {
        /*
         * tran.Insert<uint, DbXML<List<string>>>("t1", i, aa);
         * foreach (var row in tran.SelectForward<uint, DbXML<List<string>>>("t1"))
         * Console.WriteLine("K: {0}; V: {1}", row.Key.ToString(), (row.Value == null) ? "NULL" : row.Value.Get.Count().ToString());
         */
        string serialized = String.Empty;        

        public DbMJSON(T obj)
        {          
            serialized = obj.SerializeMJSON();
        }

        //Needed!
        public DbMJSON()
        {
        }

        //Explicit implementation, only visible via IDBConvertable
        byte[] IDBConvertable.GetBytes()
        {
            return System.Text.Encoding.UTF8.GetBytes(serialized);
        }

        //Explicit implementation, only visible via IDBConvertable
        void IDBConvertable.SetBytes(byte[] bt)
        {
            serialized = System.Text.Encoding.UTF8.GetString(bt);
        }

        /// <summary>
        /// Returns serialized string representing the internal object
        /// </summary>
        public string SerializedObject
        {
            get
            {
                return serialized;
            }
        }

        /// <summary>
        /// Gets deserialized object
        /// </summary>
        public T Get
        {
            get
            {
                if (serialized == String.Empty)
                    return default(T);

                return serialized.DeserializeMJSON<T>();
            }
        }

        public static implicit operator DbMJSON<T>(T value)
        {
            return new DbMJSON<T>(value);
            //return new DbXML<T>((T)((object)value));
        }
    }
}
