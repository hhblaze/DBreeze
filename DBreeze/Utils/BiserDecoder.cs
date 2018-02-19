using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBreeze.Utils
{
    public static partial class Biser
    {
        /// <summary>
        /// Biser.Decoder
        /// </summary>
        public class Decoder
        {
            internal byte[] encoded = null;

            /// <summary>
            /// Quantity of bytes needed to form latest varint
            /// </summary>
            internal byte qb = 0;

            //MemoryStream ms = null;
            internal int encPos = -1;

            Decoder rootDecoder = null;
            bool externalDecoderExists = false;
            Decoder activeDecoder = null; //the one who fills up collection
            /// <summary>
            /// true in case if object is null
            /// </summary>
            public bool IsNull = false;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="encoded"></param>
            public Decoder(byte[] encoded)
            {

                this.encoded = encoded;
                if (encoded == null || encoded.Length == 0)
                    return;

                this.rootDecoder = this;
                this.activeDecoder = this;
            }

            /// <summary>
            /// 
            /// </summary>
            /// <param name="decoder"></param>
            public Decoder(Decoder decoder, bool isCollection = false)
            {
                this.rootDecoder = decoder.rootDecoder;
                externalDecoderExists = true;

                if (!isCollection)
                {
                    var prot = this.rootDecoder.GetDigit();
                    if (prot == 1)
                        IsNull = true;
                }
            }


            ulong GetDigit() //Gets next digit from the byte[] stream, this function works only from root decoder
            {
                int shift = 0;
                ulong result = 0;
                ulong byteValue = 0;
                qb = 0;

                while (true)
                {
                    if (!this.activeDecoder.collectionIsFinished && this.activeDecoder.collectionShiftToPass < this.activeDecoder.collectionShift)
                        byteValue = this.activeDecoder.collectionBuffer[this.activeDecoder.collectionShiftToPass++];
                    else
                    {
                        encPos++;
                        byteValue = (ulong)encoded[encPos]; //Automatically will throw exception in case if index is out of range   
                    }

                    result |= (byteValue & 0x7f) << shift;
                    qb++;
                    if ((byteValue & 0x80) != 0x80)
                        return result;

                    shift += 7;
                }
            }



            byte[] Read(int length)
            {
                this.rootDecoder.encPos++;
                byte[] bt = new byte[length];
                Buffer.BlockCopy(this.rootDecoder.encoded, this.rootDecoder.encPos, bt, 0, length);
                this.rootDecoder.encPos += length - 1;
                return bt;
            }


            int collectionShift = 0;
            int collectionPos = 0;
            int collectionShiftToPass = 0;
            byte[] collectionBuffer = new byte[3];
            bool collectionIsFinished = true;
            int collectionLength = 0;

            /// <summary>
            /// Is used for checking next collection on null, before getting one of the itterators.
            /// </summary>
            /// <returns>true if null</returns>
            public bool CheckNull()
            {
                return !(this.rootDecoder.GetDigit() == 0);
            }

            /// <summary>
            /// Universal, but a bit slower (because of the yield returns) than those ones with IDictionary or IList
            /// </summary>
            /// <param name="isNullChecked"></param>
            /// <returns></returns>
            public IEnumerable<Decoder> GetCollection(bool isNullChecked = false)
            {
                ulong prot = 0;
                if (!isNullChecked)
                    prot = this.rootDecoder.GetDigit();

                if (prot == 0)
                {
                    collectionLength = (int)((uint)this.rootDecoder.GetDigit());
                    collectionPos = 0;
                    collectionShift = 0;
                    collectionShiftToPass = 0;

                    int cp = this.rootDecoder.encPos;

                    if (this.rootDecoder.qb > 1)
                    {
                        collectionShift = this.rootDecoder.qb - 1;
                        collectionShiftToPass = 0;
                        this.rootDecoder.encPos = cp + collectionLength - 1;
                        collectionBuffer = Read(collectionShift);
                        this.rootDecoder.encPos = cp;
                        collectionPos += collectionShift;
                    }

                    collectionIsFinished = false;

                    Decoder oldDecoder = null;
                    if (externalDecoderExists)
                    {
                        oldDecoder = this.rootDecoder.activeDecoder;
                        this.rootDecoder.activeDecoder = this;
                    }

                    while (!collectionIsFinished)
                    {
                        yield return this;

                        if ((this.rootDecoder.encPos - (cp - collectionShift)) == collectionLength)
                        {
                            collectionIsFinished = true;
                            if (collectionShift > 0)
                                this.rootDecoder.encPos += collectionShift;
                            if (externalDecoderExists)
                                this.rootDecoder.activeDecoder = oldDecoder;
                        }
                    }
                }

            } //eof

            /// <summary>
            /// 
            /// </summary>
            /// <typeparam name="K"></typeparam>
            /// <param name="fk"></param>
            /// <param name="lst"></param>
            /// <param name="isNullChecked"></param>
            public void GetCollection<K>(Func<K> fk, IList<K> lst, bool isNullChecked = false)
            {
                GetCollection(fk, fk, null, lst, isNullChecked);
            }

            /// <summary>
            /// 
            /// </summary>
            /// <typeparam name="K"></typeparam>
            /// <typeparam name="V"></typeparam>
            /// <param name="fk"></param>
            /// <param name="fv"></param>
            /// <param name="dict"></param>
            /// <param name="isNullChecked"></param>
            public void GetCollection<K, V>(Func<K> fk, Func<V> fv, IDictionary<K, V> dict, bool isNullChecked = false)
            {
                GetCollection(fk, fv, dict, null, isNullChecked);
            }

            void GetCollection<K, V>(Func<K> fk, Func<V> fv, IDictionary<K, V> dict, IList<K> lst, bool isNullChecked = false)
            {
                ulong prot = 0;
                if (!isNullChecked)
                    prot = this.rootDecoder.GetDigit();

                if (prot == 0)
                {
                    if (!collectionIsFinished)
                    {
                        Decoder nDecoder = new Decoder(this, true);
                        nDecoder.GetCollection(fk, fv, dict, lst, isNullChecked);
                        return;
                    }

                    collectionLength = (int)((uint)this.rootDecoder.GetDigit());
                    collectionPos = 0;
                    collectionShift = 0;
                    collectionShiftToPass = 0;

                    int cp = this.rootDecoder.encPos;

                    if (this.rootDecoder.qb > 1)
                    {
                        collectionShift = this.rootDecoder.qb - 1;
                        collectionShiftToPass = 0;
                        this.rootDecoder.encPos = cp + collectionLength - 1;
                        collectionBuffer = Read(collectionShift);
                        this.rootDecoder.encPos = cp;
                        collectionPos += collectionShift;
                    }

                    collectionIsFinished = false;

                    Decoder oldDecoder = null;
                    if (externalDecoderExists)
                    {
                        oldDecoder = this.rootDecoder.activeDecoder;
                        this.rootDecoder.activeDecoder = this;
                    }

                    while (true)
                    {
                        if (lst == null)
                            dict.Add(fk(), fv());
                        else
                            lst.Add(fk());

                        if ((this.rootDecoder.encPos - (cp - collectionShift)) == collectionLength)
                        {
                            collectionIsFinished = true;
                            if (collectionShift > 0)
                                this.rootDecoder.encPos += collectionShift;
                            if (externalDecoderExists)
                                this.rootDecoder.activeDecoder = oldDecoder;
                            break;
                        }
                    }
                }


            } //eof


            public DateTime GetDateTime()
            {
                return new DateTime(Biser.DecodeZigZag(this.rootDecoder.GetDigit()));
            }

            public DateTime? GetDateTime_NULL()
            {
                if (this.rootDecoder.GetDigit() == 1)
                    return null;
                return GetDateTime();
            }


            public long GetLong()
            {
                return Biser.DecodeZigZag(this.rootDecoder.GetDigit());
            }

            public long? GetLong_NULL()
            {
                if (this.rootDecoder.GetDigit() == 1)
                    return null;
                return GetLong();

            }

            public ulong GetULong()
            {
                return this.rootDecoder.GetDigit();
            }

            public ulong? GetULong_NULL()
            {
                if (this.rootDecoder.GetDigit() == 1)
                    return null;
                return GetULong();
            }

            public int GetInt()
            {
                return (int)Biser.DecodeZigZag(this.rootDecoder.GetDigit());
            }

            public int? GetInt_NULL()
            {
                if (this.rootDecoder.GetDigit() == 1)
                    return null;
                return GetInt();
            }

            public uint GetUInt()
            {
                return (uint)this.rootDecoder.GetDigit();
            }

            public uint? GetUInt_NULL()
            {
                if (this.rootDecoder.GetDigit() == 1)
                    return null;
                return GetUInt();
            }

            public short GetShort()
            {
                return (short)Biser.DecodeZigZag(this.rootDecoder.GetDigit());
            }

            public short? GetShort_NULL()
            {
                if (this.rootDecoder.GetDigit() == 1)
                    return null;
                return GetShort();
            }

            public ushort GetUShort()
            {
                return (ushort)Biser.DecodeZigZag(this.rootDecoder.GetDigit());
            }

            public ushort? GetUShort_NULL()
            {
                if (this.rootDecoder.GetDigit() == 1)
                    return null;
                return GetUShort();
            }

            public bool GetBool()
            {
                return Read(1)[0] == 1;
            }

            public bool? GetBool_NULL()
            {
                if (this.rootDecoder.GetDigit() == 1)
                    return null;

                return GetBool();
            }

            public sbyte GetSByte()
            {
                return (sbyte)Read(1)[0];
            }

            public sbyte? GetSByte_NULL()
            {
                if (this.rootDecoder.GetDigit() == 1)
                    return null;
                return GetSByte();
            }

            public byte GetByte()
            {
                return Read(1)[0];
            }

            public byte? GetByte_NULL()
            {
                if (this.rootDecoder.GetDigit() == 1)
                    return null;
                return GetByte();
            }

            public float GetFloat()
            {
                var subRet = this.rootDecoder.GetDigit();
                var ret = BitConverter.ToSingle(BitConverter.GetBytes(subRet), 0);
                return ret;
            }

            public float? GetFloat_NULL()
            {
                if (this.rootDecoder.GetDigit() == 1)
                    return null;
                return GetFloat();
            }

            public double GetDouble()
            {
                var subRet = this.rootDecoder.GetDigit();
                var ret = BitConverter.ToDouble(BitConverter.GetBytes(subRet), 0);
                return ret;
            }

            public double? GetDouble_NULL()
            {
                if (this.rootDecoder.GetDigit() == 1)
                    return null;
                return GetDouble();

            }

            public decimal GetDecimal()
            {
                int[] bits = new int[4];
                bits[0] = (int)Biser.DecodeZigZag(this.rootDecoder.GetDigit());
                bits[1] = (int)Biser.DecodeZigZag(this.rootDecoder.GetDigit());
                bits[2] = (int)Biser.DecodeZigZag(this.rootDecoder.GetDigit());
                bits[3] = (int)Biser.DecodeZigZag(this.rootDecoder.GetDigit());
                return new decimal(bits);
            }

            public decimal? GetDecimal_NULL()
            {
                if (this.rootDecoder.GetDigit() == 1)
                    return null;
                return GetDecimal();
            }

            public char GetChar()
            {
                return GetString().ToCharArray()[0];
            }

            public char? GetChar_NULL()
            {
                if (this.rootDecoder.GetDigit() == 1)
                    return null;
                return GetChar();

            }

            public string GetString()
            {
                var bt = GetByteArray();
                if (bt == null)
                    return null;
                if (bt.Length == 0)
                    return "";
                return bt.UTF8_GetString();
            }


            public byte[] GetByteArray()
            {
                //0 - with length, 1 - null, 2 - zero length
                byte[] ret = null;

                var prot = this.rootDecoder.GetDigit();
                switch (prot)
                {
                    case 2:
                        ret = new byte[0];
                        break;
                    case 0:
                        ret = Read((int)((uint)this.rootDecoder.GetDigit()));
                        break;
                }

                return ret;
            }

        }//eoc Decoder

    }
}
