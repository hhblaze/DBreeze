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

namespace DBreeze.DataTypes
{
    public static class DataTypesConvertor
    {       

        public static Type TYPE_BYTE_ARRAY = typeof(byte[]);
        public static Type TYPE_INT = typeof(int);
        public static Type TYPE_INT_NULL = typeof(int?);
        public static Type TYPE_UINT = typeof(uint);
        public static Type TYPE_UINT_NULL = typeof(uint?);
        public static Type TYPE_LONG = typeof(long);
        public static Type TYPE_LONG_NULL = typeof(long?);
        public static Type TYPE_ULONG = typeof(ulong);
        public static Type TYPE_ULONG_NULL = typeof(ulong?);
        public static Type TYPE_SHORT = typeof(short);
        public static Type TYPE_SHORT_NULL = typeof(short?);
        public static Type TYPE_USHORT = typeof(ushort);
        public static Type TYPE_USHORT_NULL = typeof(ushort?);
        public static Type TYPE_BYTE = typeof(byte);
        public static Type TYPE_BYTE_NULL = typeof(byte?);
        public static Type TYPE_SBYTE = typeof(sbyte);
        public static Type TYPE_SBYTE_NULL = typeof(sbyte?);
        public static Type TYPE_DATETIME = typeof(DateTime);
        public static Type TYPE_DATETIME_NULL = typeof(DateTime?);
        
        public static Type TYPE_DOUBLE = typeof(double);
        public static Type TYPE_DOUBLE_NULL = typeof(double?);
        public static Type TYPE_FLOAT = typeof(float);
        public static Type TYPE_FLOAT_NULL = typeof(float?);
        public static Type TYPE_DECIMAL = typeof(decimal);
        public static Type TYPE_DECIMAL_NULL = typeof(decimal?);
        
        public static Type TYPE_STRING = typeof(string);
        public static Type TYPE_DB_UTF8 = typeof(DbUTF8);
        public static Type TYPE_DB_ASCII = typeof(DbAscii);
        public static Type TYPE_DB_UNICODE = typeof(DbUnicode);
        
        public static Type TYPE_BOOL = typeof(bool);
        public static Type TYPE_BOOL_NULL = typeof(bool?);
        
        public static Type TYPE_OBJECT = typeof(object);

        public static Type TYPE_CHAR = typeof(char);
        public static Type TYPE_CHAR_NULL = typeof(char?);

        public static Type TYPE_GUID = typeof(Guid);        

        //static Type TYPE_DBINTABLE = typeof(DbInTable);


        ////Slower then original
        //public static byte[] ConvertValue<TData>(TData data)
        //{
        //    //byte[] ret = null;

        //    if (data == null)
        //        return null;

        //    Type td = typeof(TData);
        //    string std = td.ToString();



        //    try
        //    {
        //        switch (td.Name)
        //        {
        //            case "DbMJSON`1":
        //            case "DbCustomSerializer`1":
        //            case "DbXML`1":
        //                return ((IDBConvertable)((object)data)).GetBytes();
        //        }

        //        switch (std)
        //        {
        //            case "System.Byte[]":
        //                return ((byte[])((object)data));
        //            case "System.UInt64":
        //            case "System.Nullable`1[System.UInt64]":
        //                return ((ulong)((object)data)).To_8_bytes_array_BigEndian();
        //            case "System.DateTime":
        //            case "System.Nullable`1[System.DateTime]":
        //                return ((ulong)(((DateTime)((object)data)).Ticks)).To_8_bytes_array_BigEndian();
        //            case "System.String":
        //                return new DbUTF8((string)((object)data)).GetBytes();
        //            case "System.UInt32":
        //            case "System.Nullable`1[System.UInt32]":
        //                return ((uint)((object)data)).To_4_bytes_array_BigEndian();
        //            case "System.Decimal":
        //            case "System.Nullable`1[System.Decimal]":
        //                return ((decimal)((object)data)).To_15_bytes_array_BigEndian();
        //            case "System.Double":
        //            case "System.Nullable`1[System.Double]":
        //                return ((double)((object)data)).To_9_bytes_array_BigEndian();
        //            case "System.Single":
        //            case "System.Nullable`1[System.Single]":
        //                return ((float)((object)data)).To_4_bytes_array_BigEndian();
        //            case "System.Int32":
        //            case "System.Nullable`1[System.Int32]":
        //                return ((int)((object)data)).To_4_bytes_array_BigEndian();
        //            case "System.Int64":
        //            case "System.Nullable`1[System.Int64]":
        //                return ((long)((object)data)).To_8_bytes_array_BigEndian();
        //            case "System.Int16":
        //            case "System.Nullable`1[System.Int16]":
        //                return ((short)((object)data)).To_2_bytes_array_BigEndian();
        //            case "System.UInt16":
        //            case "System.Nullable`1[System.UInt16]":
        //                return ((ushort)((object)data)).To_2_bytes_array_BigEndian();
        //            case "DBreeze.DataTypes.DbAscii":
        //                return ((DbAscii)((object)data)).GetBytes();
        //            case "DBreeze.DataTypes.DbUnicode":
        //                return ((DbUnicode)((object)data)).GetBytes();
        //            case "DBreeze.DataTypes.DbUTF8":
        //                return ((DbUTF8)((object)data)).GetBytes();
        //            case "System.Byte":
        //            case "System.Nullable`1[System.Byte]":
        //                return new byte[] { ((byte)((object)data)) };
        //            case "System.SByte":
        //            case "System.Nullable`1[System.SByte]":
        //                return ((sbyte)((object)data)).To_1_byte_array();
        //            case "System.Boolean":
        //            case "System.Nullable`1[System.Boolean]":
        //                return ((bool)((object)data)).To_1_byte_array();
        //        }

        //        throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.UNSUPPORTED_DATATYPE, td.ToString(), null);

              
        //    }
        //    catch (Exception ex)
        //    {
        //        throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.UNSUPPORTED_DATATYPE_VALUE, String.Concat(td.ToString(), " ", data.ToString()), ex);
        //    }

        //    //return ret;
        //}

        

        public static byte[] ConvertValue<TData>(TData data)
        {
            byte[] ret = null;

            if (data == null)
                return null;

            Type td = typeof(TData);

            try
            {
               

                if (td == TYPE_BYTE_ARRAY)
                {
                    ret = ((byte[])((object)data));
                }
                else if (td.Name == "DbMJSON`1" || td.Name == "DbCustomSerializer`1" || td.Name == "DbXML`1")
                {                    
                    return ((IDBConvertable)((object)data)).GetBytes();
                }
                else if (td == TYPE_ULONG)
                {
                    ret = ((ulong)((object)data)).To_8_bytes_array_BigEndian();
                }
                else if (td == TYPE_ULONG_NULL)
                {
                    ret = ((ulong?)((object)data)).To_9_bytes_array_BigEndian();
                }
                else if (td == TYPE_DATETIME)
                {
                    ret = ((DateTime)((object)data)).To_8_bytes_array();                    
                }
                else if (td == TYPE_DATETIME_NULL)
                {
                    ret = ((DateTime?)((object)data)).To_9_bytes_array();                    
                }
                else if (td == TYPE_STRING)
                {
                    ret = new DbUTF8((string)((object)data)).GetBytes();
                }
                else if (td == TYPE_UINT)
                {
                    ret = ((uint)((object)data)).To_4_bytes_array_BigEndian();
                }
                else if (td == TYPE_UINT_NULL)
                {
                    ret = ((uint?)((object)data)).To_5_bytes_array_BigEndian();
                }
                else if (td == TYPE_DECIMAL)
                {
                    ret = ((decimal)((object)data)).To_15_bytes_array_BigEndian();
                }
                else if (td == TYPE_DECIMAL_NULL)
                {
                    ret = ((decimal?)((object)data)).To_16_bytes_array_BigEndian();
                }
                else if (td == TYPE_DOUBLE)
                {
                    ret = ((double)((object)data)).To_9_bytes_array_BigEndian();
                }
                else if (td == TYPE_DOUBLE_NULL)
                {
                    ret = ((double?)((object)data)).To_10_bytes_array_BigEndian();
                }
                else if (td == TYPE_FLOAT)
                {
                    ret = ((float)((object)data)).To_4_bytes_array_BigEndian();
                }
                else if (td == TYPE_FLOAT_NULL)
                {
                    ret = ((float?)((object)data)).To_5_bytes_array_BigEndian();
                }
                else if (td == TYPE_INT)
                {
                    ret = ((int)((object)data)).To_4_bytes_array_BigEndian();
                }
                else if (td == TYPE_INT_NULL)
                {
                    ret = ((int?)((object)data)).To_5_bytes_array_BigEndian();
                }
                else if (td == TYPE_LONG)
                {
                    ret = ((long)((object)data)).To_8_bytes_array_BigEndian();
                }
                else if (td == TYPE_LONG_NULL)
                {
                    ret = ((long?)((object)data)).To_9_bytes_array_BigEndian();
                }
                else if (td == TYPE_SHORT)
                {
                    ret = ((short)((object)data)).To_2_bytes_array_BigEndian();
                }
                else if (td == TYPE_SHORT_NULL)
                {
                    ret = ((short?)((object)data)).To_3_bytes_array_BigEndian();
                }
                else if (td == TYPE_USHORT)
                {
                    ret = ((ushort)((object)data)).To_2_bytes_array_BigEndian();
                }
                else if (td == TYPE_USHORT_NULL)
                {
                    ret = ((ushort?)((object)data)).To_3_bytes_array_BigEndian();
                }
                else if (td == TYPE_DB_ASCII)
                {
                    ret = ((DbAscii)((object)data)).GetBytes();
                }
                else if (td == TYPE_DB_UNICODE)
                {
                    ret = ((DbUnicode)((object)data)).GetBytes();
                }
                else if (td == TYPE_DB_UTF8)
                {
                    ret = ((DbUTF8)((object)data)).GetBytes();
                }
                else if (td == TYPE_BYTE)
                {
                    ret = ((byte)((object)data)).To_1_byte_array();                    
                }
                else if (td == TYPE_BYTE_NULL)
                {
                    ret = ((byte?)((object)data)).To_2_byte_array();                    
                }
                else if (td == TYPE_SBYTE)
                {
                    ret = ((sbyte)((object)data)).To_1_byte_array();
                }
                else if (td == TYPE_SBYTE_NULL)
                {
                    ret = ((sbyte?)((object)data)).To_2_byte_array();
                }
                else if (td == TYPE_BOOL)
                {
                    ret = ((bool)((object)data)).To_1_byte_array();
                }
                else if (td == TYPE_BOOL_NULL)
                {
                    ret = ((bool?)((object)data)).To_1_byte_array();
                }
                else if (td == TYPE_CHAR)
                {
                    ret = ((char)((object)data)).To_2_byte_array();
                }
                else if (td == TYPE_CHAR_NULL)
                {
                    ret = ((char?)((object)data)).To_3_byte_array();
                }
                else if (td == TYPE_GUID)
                {
                    ret = ((Guid)((object)data)).ToByteArray();                 
                } 
                else if (td == TYPE_OBJECT)
                {
                    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.UNSUPPORTED_DATATYPE, td.ToString(), null);
                }
                else if (td.IsEnum)
                {
                    var enumtype = Enum.GetUnderlyingType(td);

                    if (enumtype == TYPE_INT)
                    {
                        ret = ((int)((object)data)).To_4_bytes_array_BigEndian();
                    }
                    else if (enumtype == TYPE_LONG)
                    {
                        ret = ((long)((object)data)).To_8_bytes_array_BigEndian();
                    }
                    else if (enumtype == TYPE_UINT)
                    {
                        ret = ((uint)((object)data)).To_4_bytes_array_BigEndian();
                    }
                    else if (enumtype == TYPE_ULONG)
                    {
                        ret = ((ulong)((object)data)).To_8_bytes_array_BigEndian();
                    }
                    else if (enumtype == TYPE_USHORT)
                    {
                        ret = ((ushort)((object)data)).To_2_bytes_array_BigEndian();
                    }
                    else if (enumtype == TYPE_SHORT)
                    {
                        ret = ((short)((object)data)).To_2_bytes_array_BigEndian();
                    }
                    else if (enumtype == TYPE_BYTE)
                    {
                        ret = ((byte)((object)data)).To_1_byte_array();  
                    }
                    else if (enumtype == TYPE_SBYTE)
                    {
                        ret = ((sbyte)((object)data)).To_1_byte_array();
                    } 
                    else
                        throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.UNSUPPORTED_DATATYPE, td.ToString(), null);
                }
                else
                {
                    //Trying byte serialization for unknown object, in case if byte serializer is set
                    if (CustomSerializator.ByteArraySerializator != null)
                        return CustomSerializator.ByteArraySerializator(data);

                    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.UNSUPPORTED_DATATYPE, td.ToString(), null);
                }
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.UNSUPPORTED_DATATYPE_VALUE, String.Concat(td.ToString(), " ", data.ToString()), ex);
            }

            return ret;
        }


     
        



        public static byte[] ConvertKey<TData>(TData data)
        {           

            byte[] ret = null;

            Type td = typeof(TData);
            
            if (data == null)
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.KEY_CANT_BE_NULL, td.ToString(), null);

            try
            {
                if (td == TYPE_BYTE_ARRAY)
                {
                    ret = ((byte[])((object)data));
                }                
                else if (td == TYPE_ULONG)
                {
                    ret = ((ulong)((object)data)).To_8_bytes_array_BigEndian();
                }
                else if (td == TYPE_DATETIME)
                {
                    ret = ((DateTime)((object)data)).To_8_bytes_array();                    
                }
                else if (td == TYPE_STRING)
                {
                    ret = new DbUTF8((string)((object)data)).GetBytes();
                }
                else if (td == TYPE_UINT)
                {
                    ret = ((uint)((object)data)).To_4_bytes_array_BigEndian();
                }
                else if (td == TYPE_DECIMAL)
                {
                    ret = ((decimal)((object)data)).To_15_bytes_array_BigEndian();
                }
                else if (td == TYPE_INT)
                {
                    ret = ((int)((object)data)).To_4_bytes_array_BigEndian();
                }                
                else if (td == TYPE_LONG)
                {
                    ret = ((long)((object)data)).To_8_bytes_array_BigEndian();
                }
                else if (td == TYPE_DOUBLE)
                {
                    ret = ((double)((object)data)).To_9_bytes_array_BigEndian();
                }
                else if (td == TYPE_FLOAT)
                {
                    ret = ((float)((object)data)).To_4_bytes_array_BigEndian();
                }  
                else if (td == TYPE_SHORT)
                {
                    ret = ((short)((object)data)).To_2_bytes_array_BigEndian();
                }
                else if (td == TYPE_USHORT)
                {
                    ret = ((ushort)((object)data)).To_2_bytes_array_BigEndian();
                }             
                else if (td == TYPE_DB_ASCII)
                {
                    ret = ((DbAscii)((object)data)).GetBytes();
                }
                else if (td == TYPE_DB_UNICODE)
                {
                    ret = ((DbUnicode)((object)data)).GetBytes();
                }
                else if (td == TYPE_DB_UTF8)
                {
                    ret = ((DbUTF8)((object)data)).GetBytes();
                }
                else if (td == TYPE_BYTE)
                {
                    ret = ((byte)((object)data)).To_1_byte_array();                    
                }
                else if (td == TYPE_SBYTE)
                {
                    ret = ((sbyte)((object)data)).To_1_byte_array();
                }
                else if (td == TYPE_CHAR)
                {
                    ret = ((char)((object)data)).To_2_byte_array();
                }
                else if (td == TYPE_GUID)
                {
                    ret = ((Guid)((object)data)).ToByteArray();
                } 
                else if (td.IsEnum)
                {
                    var enumtype = Enum.GetUnderlyingType(td);

                    if (enumtype == TYPE_INT)
                    {
                        ret = ((int)((object)data)).To_4_bytes_array_BigEndian();
                    }
                    else if (enumtype == TYPE_LONG)
                    {
                        ret = ((long)((object)data)).To_8_bytes_array_BigEndian();
                    }
                    else if (enumtype == TYPE_UINT)
                    {
                        ret = ((uint)((object)data)).To_4_bytes_array_BigEndian();
                    }
                    else if (enumtype == TYPE_ULONG)
                    {
                        ret = ((ulong)((object)data)).To_8_bytes_array_BigEndian();
                    }
                    else if (enumtype == TYPE_USHORT)
                    {
                        ret = ((ushort)((object)data)).To_2_bytes_array_BigEndian();
                    }
                    else if (enumtype == TYPE_SHORT)
                    {
                        ret = ((short)((object)data)).To_2_bytes_array_BigEndian();
                    }
                    else if (enumtype == TYPE_BYTE)
                    {
                        ret = ((byte)((object)data)).To_1_byte_array(); 
                    }
                    else if (enumtype == TYPE_SBYTE)
                    {
                        ret = ((sbyte)((object)data)).To_1_byte_array();
                    }
                    else
                        throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.UNSUPPORTED_DATATYPE, td.ToString(), null);
                }
                else
                {
                    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.UNSUPPORTED_DATATYPE, td.ToString(), null);
                }
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.UNSUPPORTED_DATATYPE_VALUE, String.Concat(td.ToString(), " ", data.ToString()), ex);
            }
            
            //Key can be of byte[0]
            //if (ret == null || ret.Length == 0)
            //    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.KEY_CANT_BE_NULL, td.ToString(), null);

            return ret;
        }


        /// <summary>
        /// CONVERTING FROM byte[] to the generic type
        /// </summary>
        /// <typeparam name="TData"></typeparam>
        /// <param name="dt"></param>
        /// <returns></returns>
        public static TData ConvertBack<TData>(byte[] dt)
        {
            //OPT all to switch case

            if (dt == null)
                return default(TData);

            Type td = typeof(TData);

           

            if (td == TYPE_BYTE_ARRAY)
            {
                return (TData)((object)dt);
            }
            else if (td == TYPE_ULONG)
            {
                return (TData)((object)dt.To_UInt64_BigEndian());
            }
            else if (td == TYPE_ULONG_NULL)
            {
                return (TData)((object)dt.To_UInt64_BigEndian_NULL());
            }
            else if (td == TYPE_DATETIME)
            {
                return (TData)((object)dt.To_DateTime());                
            }
            else if (td == TYPE_DATETIME_NULL)
            {
                return (TData)((object)dt.To_DateTime_NULL());                
            }
            else if (td == TYPE_STRING)
            {             
                return (TData)((object)(new DbUTF8(dt)).Get);
            }
            else if (td.Name.Equals("DbMJSON`1") || td.Name.Equals("DbCustomSerializer`1") || td.Name.Equals("DbXML`1"))
            {
                object o = Activator.CreateInstance(td);
                ((IDBConvertable)o).SetBytes(dt);
                return (TData)o;
            }   
            else if (td == TYPE_UINT)
            {
                return (TData)((object)dt.To_UInt32_BigEndian());
            }
            else if (td == TYPE_UINT_NULL)
            {
                return (TData)((object)dt.To_UInt32_BigEndian_NULL());
            }
            else if (td == TYPE_DECIMAL)
            {
                return (TData)((object)(dt.To_Decimal_BigEndian()));
            }
            else if (td == TYPE_DECIMAL_NULL)
            {
                return (TData)((object)(dt.To_Decimal_BigEndian_NULL()));
            }
            else if (td == TYPE_INT)
            {
                return (TData)((object)dt.To_Int32_BigEndian());                
            }
            else if (td == TYPE_INT_NULL)
            {
                return (TData)((object)dt.To_Int32_BigEndian_NULL());
            }
            else if (td == TYPE_DOUBLE)
            {
                return (TData)((object)(dt.To_Double_BigEndian()));
            }
            else if (td == TYPE_DOUBLE_NULL)
            {
                return (TData)((object)(dt.To_Double_BigEndian_NULL()));
            }
            else if (td == TYPE_FLOAT)
            {
                return (TData)((object)(dt.To_Float_BigEndian()));
            }
            else if (td == TYPE_FLOAT_NULL)
            {
                return (TData)((object)(dt.To_Float_BigEndian_NULL()));
            }
            else if (td == TYPE_LONG)
            {
                return (TData)((object)dt.To_Int64_BigEndian()); 
            }
            else if (td == TYPE_LONG_NULL)
            {
                return (TData)((object)dt.To_Int64_BigEndian_NULL());
            } 
            else if (td == TYPE_SHORT)
            {
                return (TData)((object)dt.To_Int16_BigEndian());
            }
            else if (td == TYPE_SHORT_NULL)
            {
                return (TData)((object)dt.To_Int16_BigEndian_NULL());
            }
            else if (td == TYPE_USHORT)
            {
                return (TData)((object)dt.To_UInt16_BigEndian());
            }
            else if (td == TYPE_USHORT_NULL)
            {
                return (TData)((object)dt.To_UInt16_BigEndian_NULL());
            }
            else if (td == TYPE_DB_ASCII)
            {
                //checked
                return (TData)((object)new DbAscii(dt));
            }
            else if (td == TYPE_DB_UNICODE)
            {
                //checked
                return (TData)((object)new DbUnicode(dt));
            }
            else if (td == TYPE_DB_UTF8)
            {
                //checked
                return (TData)((object)new DbUTF8(dt));
            }
            else if (td == TYPE_BYTE)
            {
                return (TData)((object)dt.To_Byte());               
            }
            else if (td == TYPE_BYTE_NULL)
            {
                return (TData)((object)dt.To_Byte_NULL());
            }
            else if (td == TYPE_SBYTE)
            {
                return (TData)((object)dt.To_SByte());                
            }
            else if (td == TYPE_SBYTE_NULL)
            {
                return (TData)((object)dt.To_SByte_NULL());
            }
            else if (td == TYPE_BOOL)
            {
                return (TData)((object)dt.To_Bool());                      
            }
            else if (td == TYPE_BOOL_NULL)
            {
                return (TData)((object)dt.To_Bool_NULL());
            }
            else if (td == TYPE_CHAR)
            {
                return (TData)((object)dt.To_Char());   
            }
            else if (td == TYPE_CHAR_NULL)
            {
                return (TData)((object)dt.To_Char_NULL());
            }
            else if (td == TYPE_GUID)
            {
                return (TData)((object)new Guid(dt));                
            } 
            else if (td == TYPE_OBJECT)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.UNSUPPORTED_DATATYPE, td.ToString(), null);
            }          
            else if (td.IsEnum)
            {
                var enumtype = Enum.GetUnderlyingType(td);

                if (enumtype == TYPE_INT)
                {
                    return (TData)((object)dt.To_Int32_BigEndian());         
                }
                else if (enumtype == TYPE_LONG)
                {
                    return (TData)((object)dt.To_Int64_BigEndian()); 
                }
                else if (enumtype == TYPE_UINT)
                {
                    return (TData)((object)dt.To_UInt32_BigEndian());
                }
                else if (enumtype == TYPE_ULONG)
                {
                    return (TData)((object)dt.To_UInt64_BigEndian());
                }
                else if (enumtype == TYPE_USHORT)
                {
                    return (TData)((object)dt.To_UInt16_BigEndian());
                }
                else if (enumtype == TYPE_SHORT)
                {
                    return (TData)((object)dt.To_Int16_BigEndian());
                }
                else if (enumtype == TYPE_BYTE)
                {
                    return (TData)((object)dt.To_Byte());    
                }
                else if (enumtype == TYPE_SBYTE)
                {
                    return (TData)((object)dt.To_SByte()); 
                }
                else
                    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.UNSUPPORTED_DATATYPE, td.ToString(), null);
            }
            else
            {
                //Trying byte deserialization for unknown object, in case if byte serializer is set
                if (CustomSerializator.ByteArrayDeSerializator != null)
                    return (TData)CustomSerializator.ByteArrayDeSerializator(dt, td);

                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.UNSUPPORTED_DATATYPE, td.ToString(), null);
            }

            //return default(TData);
        }

               

    }
}
