using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TesterNet6
{
    internal static class ProtobufExtension
    {
        public static T DeserializeProtobuf<T>(this byte[] data)
        {
            T val = default(T);
            using MemoryStream memoryStream = new MemoryStream(data);
            val = Serializer.Deserialize<T>((Stream)memoryStream);
            memoryStream.Close();
            return val;
        }

        public static object DeserializeProtobuf(byte[] data, Type T)
        {
            object obj = null;
            using MemoryStream memoryStream = new MemoryStream(data);
            obj = Serializer.NonGeneric.Deserialize(T, (Stream)memoryStream);
            memoryStream.Close();
            return obj;
        }

        //
        // Summary:
        //     Serialize object using protobuf serializer
        //
        // Parameters:
        //   data:
        public static byte[] SerializeProtobuf(this object data)
        {
            byte[] array = null;
            using MemoryStream memoryStream = new MemoryStream();
            Serializer.NonGeneric.Serialize(memoryStream, data);
            array = memoryStream.ToArray();
            memoryStream.Close();
            return array;
        }
    }
}
