using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.IO;

namespace DBreeze.Utils
{
    public static partial class Biser
    {
        /// <summary>
        /// Biser.Encoder
        /// </summary>
        public class Encoder
        {
            MemoryStream ms = null;
            bool externalEncoderExists = false;

            //long addedLength = 0; 

            public Encoder(Encoder existingEncoder = null)
            {
                //writing into existing memory stream, adding before 4 bytes for the size
                if (existingEncoder != null)
                {
                    externalEncoderExists = true;
                    ms = existingEncoder.ms;

                    ms.WriteByte(0); //when adding within another decoder , indicating that it's not null
                                     //addedLength++;
                }
                else
                {

                    ms = new MemoryStream();
                }
            }

            public Encoder(IEncoder obj) :
          this(existingEncoder: null)
            {
                this.Add(obj);
            }

            public byte[] Encode()
            {
                if (externalEncoderExists)
                {
                    return null;
                }
                else
                {
                    byte[] res = null;
                    res = ms.ToArray();
#if !NETPORTABLE
                    ms.Close();
#endif                  
                    ms.Dispose();
                    return res;
                }
            }

            //public long Length
            //{
            //    get { return addedLength; }
            //}

            int GetVarintBytes(ulong value)
            {
                var pos = 0;
                byte byteVal;
                do
                {
                    byteVal = (byte)(value & 0x7f);
                    value >>= 7;

                    if (value != 0)
                    {
                        byteVal |= 0x80;
                    }
                    ms.WriteByte(byteVal);
                    //addedLength += ms.Position == ms.Length ? 1 : 0;             
                    pos++;

                } while (value != 0);

                return pos;

            }

            public Encoder Add(DateTime value)
            {
                return Add(value.Ticks);
            }

            public Encoder Add(DateTime? value)
            {
                if (value == null)
                {
                    ms.Write(new byte[] { 1 }, 0, 1);
                    return this;
                }
                else
                    ms.Write(new byte[] { 0 }, 0, 1);

                return Add(((DateTime)value).Ticks);
            }

            #region "JS add"
            public Encoder JSAdd(IJSEncoder item)
            {
                if (item == null)
                {
                    ms.WriteByte(1);
                    return this;
                }

                item.BiserJSEncoder(this);
                return this;
            }

            public Encoder JSAdd(long value)
            {
                if (value > 999999999999999 || value < -999999999999999)
                    throw new Exception("Biser.Encoder.JSAdd(long) value is out of range -999999999999999...999999999999999 ");

                Add(value);
                return this;
            }
            public Encoder JSAdd(ulong value)
            {
                return JSAdd((long)value);
            }
            public Encoder JSAdd(int value)
            {
                return JSAdd((long)value);
            }
            public Encoder JSAdd(uint value)
            {
                return JSAdd((long)value);
            }
            public Encoder JSAdd(DateTime value)
            {
                if (value.Kind != DateTimeKind.Utc)
                    return JSAdd(Convert.ToInt64(value.ToUniversalTime().Subtract(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds));
                return JSAdd(Convert.ToInt64(value
                    .Subtract(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds));
            }
            public Encoder JSAdd(float value)
            {
                return JSAdd((double)value);
            }
            public Encoder JSAdd(decimal value)
            {
                return JSAdd((double)value);
            }
            public Encoder JSAdd(double value)
            {
                //BitConverter.IsLittleEndian
                ms.Write(new byte[] { (byte)(BitConverter.IsLittleEndian ? 0 : 1) }, 0, 1);
                ms.Write(BitConverter.GetBytes(value), 0, 8);
                return this;
            }
            public Encoder JSAdd(string value)
            {
                List<long> lst = new List<long>();
                foreach (var c in value)
                    lst.Add((long)c);
                this.Add(lst, r => { this.JSAdd(r); });

                return this;
            }
            public Encoder JSAdd(bool value)
            {
                ms.Write(new byte[] { (byte)(value ? 1 : 0) }, 0, 1);

                return this;
            }
            public Encoder JSAdd(byte[] value)
            {
                if (value == null || value.Length < 1)
                    JSAdd((long)0);
                else
                {
                    JSAdd((long)value.Length);
                    ms.Write(value, 0, value.Length);
                }

                return this;
            }
            #endregion

            public Encoder Add(long value)
            {
                GetVarintBytes((ulong)Biser.EncodeZigZag(value, 64));
                return this;
            }

            public Encoder Add(long? value)
            {
                if (value == null)
                {
                    ms.Write(new byte[] { 1 }, 0, 1);
                    return this;
                }
                else
                    ms.Write(new byte[] { 0 }, 0, 1);

                return Add((long)value);
            }

            public Encoder Add(ulong value)
            {
                GetVarintBytes(value);
                return this;
            }

            public Encoder Add(ulong? value)
            {
                if (value == null)
                {
                    ms.Write(new byte[] { 1 }, 0, 1);
                    return this;
                }
                else
                    ms.Write(new byte[] { 0 }, 0, 1);

                return Add((ulong)value);
            }

            public Encoder Add(int value)
            {
                GetVarintBytes((ulong)Biser.EncodeZigZag(value, 32));
                return this;
            }

            public Encoder Add(int? value)
            {
                if (value == null)
                {
                    ms.Write(new byte[] { 1 }, 0, 1);
                    return this;
                }
                else
                    ms.Write(new byte[] { 0 }, 0, 1);

                return Add((int)value);
            }

            public Encoder Add(short value)
            {
                GetVarintBytes((ulong)Biser.EncodeZigZag(value, 16));
                return this;
            }

            public Encoder Add(short? value)
            {
                if (value == null)
                {
                    ms.Write(new byte[] { 1 }, 0, 1);
                    return this;
                }
                else
                    ms.Write(new byte[] { 0 }, 0, 1);

                return Add((short)value);
            }

            public Encoder Add(uint value)
            {
                GetVarintBytes((ulong)value);
                return this;
            }

            public Encoder Add(uint? value)
            {
                if (value == null)
                {
                    ms.Write(new byte[] { 1 }, 0, 1);
                    return this;
                }
                else
                    ms.Write(new byte[] { 0 }, 0, 1);

                return Add((uint)value);
            }

            public Encoder Add(ushort value)
            {
                GetVarintBytes((ulong)value);
                return this;
            }

            public Encoder Add(ushort? value)
            {
                if (value == null)
                {
                    ms.Write(new byte[] { 1 }, 0, 1);
                    return this;
                }
                else
                    ms.Write(new byte[] { 0 }, 0, 1);

                return Add((ushort)value);
            }



            public Encoder Add(sbyte value)
            {
                ms.Write(new byte[] { (byte)value }, 0, 1);

                return this;
            }

            public Encoder Add(sbyte? value)
            {
                if (value == null)
                {
                    ms.Write(new byte[] { 1 }, 0, 1);
                    return this;
                }
                else
                    ms.Write(new byte[] { 0 }, 0, 1);

                return Add((sbyte)value);
            }

            public Encoder Add(byte value)
            {
                ms.Write(new byte[] { value }, 0, 1);

                return this;
            }

            public Encoder Add(byte? value)
            {
                if (value == null)
                {
                    ms.Write(new byte[] { 1 }, 0, 1);
                    return this;
                }
                else
                    ms.Write(new byte[] { 0 }, 0, 1);

                return Add((byte)value);
            }

            public Encoder Add(bool value)
            {
                ms.Write(new byte[] { (byte)(value ? 1 : 0) }, 0, 1);
                return this;
            }

            public Encoder Add(bool? value)
            {
                if (value == null)
                {
                    ms.Write(new byte[] { 1 }, 0, 1);
                    return this;
                }
                else
                    ms.Write(new byte[] { 0 }, 0, 1);

                return Add((bool)value);
            }

            public Encoder Add(char value)
            {
                return Add(value.ToString());
            }

            public Encoder Add(char? value)
            {
                if (value == null)
                {
                    ms.Write(new byte[] { 1 }, 0, 1);
                    return this;
                }
                else
                    ms.Write(new byte[] { 0 }, 0, 1);

                return Add((char)value);
            }


            public Encoder Add(float value)
            {
                //Little and BigEndian compliant      
                GetVarintBytes(BitConverter.ToUInt32(BitConverter.GetBytes(value), 0));
                return this;
            }


            public Encoder Add(float? value)
            {
                if (value == null)
                {
                    ms.Write(new byte[] { 1 }, 0, 1);
                    return this;
                }
                else
                    ms.Write(new byte[] { 0 }, 0, 1);

                return Add((float)value);
            }

            public Encoder Add(double value)
            {
                //Little and BigEndian compliant            
                GetVarintBytes(BitConverter.ToUInt64(BitConverter.GetBytes(value), 0));
                return this;
            }


            public Encoder Add(double? value)
            {
                //Little and BigEndian compliant 
                if (value == null)
                {
                    ms.Write(new byte[] { 1 }, 0, 1);
                    return this;
                }
                else
                    ms.Write(new byte[] { 0 }, 0, 1);

                return Add((double)value);
            }

            public Encoder Add(decimal value)
            {
                var bits = Decimal.GetBits(value); //4 int[]
                Add(bits[0]);
                Add(bits[1]);
                Add(bits[2]);
                Add(bits[3]);

                return this;
            }

            public Encoder Add(decimal? value)
            {
                if (value == null)
                {
                    ms.Write(new byte[] { 1 }, 0, 1);
                    return this;
                }
                else
                    ms.Write(new byte[] { 0 }, 0, 1);

                return Add((decimal)value);
            }

            public Encoder Add(string value)
            {

                if (value == null)
                {
                    ms.Write(new byte[] { 1 }, 0, 1);
                    return this;
                }
                else if (value.Length == 0)
                {
                    ms.Write(new byte[] { 2 }, 0, 1);
                    //addedLength += 1;
                    return this;
                }

                return Add(value.To_UTF8Bytes());
            }

            public Encoder Add(Guid guid)
            {
                return Add(guid.ToByteArray());
            }

            public Encoder Add(Guid? guid)
            {
                if (guid == null)
                {
                    ms.Write(new byte[] { 1 }, 0, 1);
                    return this;
                }

                return Add(((Guid)guid).ToByteArray());
            }

            public Encoder Add(byte[] value)
            {
                if (value == null)
                {
                    ms.Write(new byte[] { 1 }, 0, 1);
                    //addedLength += 1;
                    return this;
                }
                else if (value.Length == 0)
                {
                    ms.Write(new byte[] { 2 }, 0, 1);
                    //addedLength += 1;
                    return this;
                }

                ms.Write(new byte[] { 0 }, 0, 1);
                GetVarintBytes((ulong)(uint)value.Length);
                ms.Write(value, 0, value.Length);
                //addedLength += 1 + value.Length;
                return this;
            }


            public Encoder Add(IEncoder item)
            {
                if (item == null)
                {
                    ms.WriteByte(1);
                    //addedLength++;
                    return this;
                }

                item.BiserEncoder(this);
                return this;
            }



            public Encoder Add<T>(IEnumerable<T> items, Action<T> f)   //either well-known type or IEncoder
            {
                if (items == null)
                {
                    ms.Write(new byte[] { 1 }, 0, 1);
                    //addedLength += 1;
                    return this;
                }
                ms.Write(new byte[] { 0, 0 }, 0, 2); //first byte means not null, second - reservation for minimal length representation
                                                     //addedLength += 2;
                long ip = ms.Position - 1; //from current position will be written elements
                long len = 0;

                var lenBefore = ms.Length;
                foreach (var item in items)
                {
                    if (item == null)
                    {
                        ms.WriteByte(1);
                        //addedLength++;
                        continue;
                    }
                    else
                    {
                        f(item);
                    }
                }

                len += ms.Length - lenBefore;

                var cp = ms.Position; //restore point

                var intLenSize = (len > 268435455) ? 5 : (len > 2097151) ? 4 : (len > 16383) ? 3 : (len > 127) ? 2 : 1;  //4 bytes max for collections and groups

                if (intLenSize > 1)
                {
                    var tmp = new byte[intLenSize - 1];
                    ms.Position = ip + 1;
                    ms.Read(tmp, 0, tmp.Length);
                    ms.Position = ip;
                    GetVarintBytes((ulong)len);
                    ms.Position = cp;
                    ms.Write(tmp, 0, tmp.Length);
                }
                else
                {
                    ms.Position = ip;
                    GetVarintBytes((ulong)len);
                    ms.Position = cp;
                }

                return this;
            }

        }//eoc
    }
}
