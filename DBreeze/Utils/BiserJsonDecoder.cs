/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.

  https://github.com/hhblaze/Biser
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;

namespace DBreeze.Utils
{
    public static partial class Biser
    {
        /// <summary>
        /// Biser JSON decoder
        /// </summary>
        public class JsonDecoder
        {
            internal string encoded = null;
            long len = 0;
            StringBuilder sb = null;
            internal int encPos = -1;
            JsonSettings jsonSettings = null;

            public JsonDecoder(string encoded, JsonSettings settings = null)
            {
                jsonSettings = (settings == null) ? new JsonSettings() : settings;

                this.encoded = encoded;
                if (encoded == null || encoded.Length == 0)
                    return;
                sb = new StringBuilder();
                len = encoded.Length;

            }

            bool CheckSkip(char c)
            {
                return (c == ' ' || c == '\t' || c == '\n' || c == '\r');
            }

            public bool CheckNull()
            {
                bool ret = false;
                bool start = false;
                while (true)
                {
                    this.encPos++;
                    if (this.encPos >= len)
                        break;
                    var c = this.encoded[this.encPos];
                    if (CheckSkip(c))
                        continue;

                    if (!start)
                        if (c == ',')
                            continue;
                        else start = true;

                    if (!ret)
                    {
                        if (c == 'n'
                            //&&
                            //this.encoded[this.encPos + 1] == 'u'
                            // &&
                            //this.encoded[this.encPos + 2] == 'l'
                            // &&
                            //this.encoded[this.encPos + 3] == 'l'
                            )
                        {
                            ret = true;
                            this.encPos += 3;
                        }
                        else
                        {
                            this.encPos--;
                            return false;
                        }
                    }

                    if (c == ',' || c == ']' || c == '}')
                    {
                        this.encPos--;
                        break;
                    }

                }

                return ret;
            }

            string GetNumber(bool checkNull)
            {
                if (checkNull && CheckNull())
                    return null;
                bool start = false;
#if NET35
                sb.Length = 0;
#else
                sb.Clear();
#endif


                while (true)
                {
                    this.encPos++;
                    if (this.encPos >= len)
                        break;
                    var c = this.encoded[this.encPos];
                    if (CheckSkip(c))
                        continue;

                    if (!start)
                        if (c == ',')
                            continue;
                        else start = true;

                    if (c == ',' || c == ']' || c == '}')
                    {
                        this.encPos--;
                        break;
                    }
                    sb.Append(c);
                }

                return sb.ToString();

            }

            string GetBoolean(bool checkNull)
            {
                if (checkNull && CheckNull())
                    return null;
                bool start = false;

#if NET35
                sb.Length = 0;
#else
                sb.Clear();
#endif

                while (true)
                {
                    this.encPos++;
                    if (this.encPos >= len)
                        break;
                    var c = this.encoded[this.encPos];
                    if (CheckSkip(c))
                        continue;

                    if (!start)
                        if (c == ',')
                            continue;
                        else start = true;

                    if (c == ',' || c == ']' || c == '}')
                    {
                        this.encPos--;
                        break;
                    }
                    sb.Append(c);
                }

                return sb.ToString();

            }

            /// <summary>
            /// Must be used as a default call, while analyzing Dictionary key or the Class property
            /// </summary>
            public void SkipValue()
            {
                bool start = true;
                char d = ' '; //default for number
                char o = ' ';
                int cnt = 0;
                while (true)
                {
                    this.encPos++;
                    if (this.encPos >= len)
                        break;
                    var c = this.encoded[this.encPos];

                    if (CheckSkip(c))
                        continue;

                    if (start)
                    {
                        if (c == '\"')
                        {
                            d = '\"';
                            o = '\"';
                        }
                        else if (c == '[')
                        {
                            d = '[';
                            o = ']';
                        }
                        else if (c == '{')
                        {
                            d = '{';
                            o = '}';
                        }
                        else if (c == 'n') //null
                        {
                            this.encPos += 3;
                            return;
                        }

                        start = false;
                    }
                    else
                    {
                        if (d == ' ' && (c == ',' || c == '}' || c == ']'))
                        {
                            this.encPos--;
                            return;
                        }
                        else if (d == '\"')
                        {
                            if (c == '\\')
                            {
                                this.encPos++;
                                continue;
                            }
                            else if (c == o)
                                return;
                        }
                        else if (d == '[' || d == '{')
                        {
                            if (c == d)
                            {
                                cnt++;
                            }
                            else if (c == o)
                            {
                                if (cnt == 0)
                                    return;
                                else
                                    cnt--;
                            }
                        }
                    }

                }
            }


            /// <summary>
            /// Skips :
            /// </summary>
            void SkipDelimiter()
            {
                while (true)
                {
                    this.encPos++;
                    if (this.encPos >= len)
                        break;
                    var c = this.encoded[this.encPos];

                    if (c == ':')
                        return;
                    else
                        continue;
                }
            }


            string GetStr(bool checkNull = true)
            {
                if (checkNull && CheckNull())
                    return null;
#if NET35
                sb.Length = 0;
#else
                sb.Clear();
#endif

                bool state = false; //0 - before strting, 1 - inSTring
                while (true)
                {
                    this.encPos++;
                    if (this.encPos >= len)
                        break;
                    var c = this.encoded[this.encPos];

                    if (state)
                    {
                        if (c == '\\')
                        {
                            this.encPos++;
                            c = this.encoded[this.encPos];
                        }
                        else if (c == '\"')
                            break;

                        sb.Append(c);
                    }
                    else
                    {
                        if (c == '}')//probably end of object, that even didn't start
                            return String.Empty;
                        else if (c == '\"')
                            state = true;

                        continue;
                    }

                }

                return sb.ToString();
            }



            /// <summary>
            /// Returns Key, Value must be retrieved extra
            /// </summary>
            /// <typeparam name="K">Dictionary Key type</typeparam>
            /// <returns></returns>
            public IEnumerable<K> GetDictionary<K>(bool checkNull = false)
            {
                if (checkNull && this.CheckNull())
                {
                }
                else
                {

                    char eoc = '}'; //end of collection
                    char soc = '{'; //start of collection

                    int state = 0; //collection start                
                    string s;
                    while (true)
                    {
                        this.encPos++;
                        if (this.encPos >= len)
                            break;
                        var c = this.encoded[this.encPos];

                        if (CheckSkip(c))
                            continue;
                        if (c == ',')
                            continue;
                        if (c == eoc)
                            break;
                        if (state == 0)
                        {
                            if (c == soc)
                                state = 1; //In collection
                        }
                        else
                        {
                            this.encPos--;
                        }

                        if (state == 1)
                        {
                            s = GetStr(false);
                            SkipDelimiter();
                            yield return (K)Convert.ChangeType(s, typeof(K));
                        }
                    }
                }

            }//eof 


            public IEnumerable<int> GetList(bool checkNull = false)
            {
                if (checkNull && this.CheckNull())
                {
                }
                else
                {

                    char eoc = ']'; //end of collection
                    char soc = '['; //start of collection

                    int state = 0; //collection start                

                    while (true)
                    {
                        this.encPos++;
                        if (this.encPos >= len)
                            break;
                        var c = this.encoded[this.encPos];

                        if (CheckSkip(c))
                            continue;
                        if (c == ',')
                            continue;
                        if (c == eoc)
                            break;
                        if (state == 0)
                        {
                            if (c == soc)
                                state = 1; //In collection
                        }
                        else
                        {
                            this.encPos--;
                        }

                        if (state == 1)
                        {
                            yield return 1;
                        }
                    }
                }

            }//eof 







            public DateTime GetDateTime()
            {
                return ParseDateTime();
            }

            public DateTime? GetDateTime_NULL()
            {
                if (CheckNull())
                    return null;
                return ParseDateTime();
            }

            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            DateTime ParseDateTime()
            {
                ulong v;
                DateTime rdt;
                string s;
                switch (this.jsonSettings.DateFormat)
                {
                    case JsonSettings.DateTimeStyle.Default:
                        //var tt3f = jsts1.P17.ToUniversalTime().Subtract(new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds * 10000;   
                        /*"\/Date(13257180000000000)\/"*/
                        s = GetStr(false);
                        // StringBuilder dsb = new StringBuilder();
#if NET35
                        sb.Length = 0;
#else
                        sb.Clear();
#endif
                        for (int i = 6; i < s.Length - 2; i++)
                            sb.Append(s[i]);
                        //v = Convert.ToUInt64(s.Substring(0, s.Length - 2).Replace("/Date(", "")) / 10000;
                        v = Convert.ToUInt64(sb.ToString()) / 10000;
                        //time if not UTC must be brought to UTC, stored in UTC and restored in UTC
                        rdt = epoch.AddMilliseconds(v);
                        return DateTime.SpecifyKind(rdt, DateTimeKind.Utc);

                    case JsonSettings.DateTimeStyle.EpochTime:
                        //var tt3f = jsts1.P17.ToUniversalTime().Subtract(new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds * 10000;   
                        /*"P17":13257818550000000*/
                        v = Convert.ToUInt64(GetNumber(false)) / 10000;
                        //time if not UTC must be brought to UTC, stored in UTC and restored in UTC
                        rdt = epoch.AddMilliseconds(v);
                        return DateTime.SpecifyKind(rdt, DateTimeKind.Utc);
                    case JsonSettings.DateTimeStyle.ISO:
                    case JsonSettings.DateTimeStyle.Javascript:
                        /*
                         * Encoder
                         * new DateTime(2018, 6, 5, 17,44,15,443, DateTimeKind.Local).ToString("o"); //Encoder ISO "2018-06-05T17:44:15.4430000Z" or "2018-06-05T17:44:15.4430000+02:00"
                         */
                        s = GetStr(false);
                        return DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind);

                }

                return DateTime.MinValue;
            }

            public TimeSpan GetTimeSpan()
            {
                var s = GetStr(true);
                return s == null ? new TimeSpan() : (TimeSpan)TimeSpan.Parse(s);
            }


            public int GetInt()
            {
                return Int32.Parse(GetNumber(false));
            }

            public int? GetInt_NULL()
            {
                var v = GetNumber(true);
                return v == null ? null : (int?)Int32.Parse(v);
            }

            public long GetLong()
            {
                return Int64.Parse(GetNumber(false));
            }

            public long? GetLong_NULL()
            {
                var v = GetNumber(true);
                return v == null ? null : (long?)Int64.Parse(v);

            }

            public ulong GetULong()
            {
                return UInt64.Parse(GetNumber(false));
            }

            public ulong? GetULong_NULL()
            {
                var v = GetNumber(true);
                return v == null ? null : (ulong?)UInt64.Parse(v);

            }

            public uint GetUInt()
            {
                return UInt32.Parse(GetNumber(false));
            }

            public uint? GetUInt_NULL()
            {
                var v = GetNumber(true);
                return v == null ? null : (uint?)UInt32.Parse(v);
            }

            public short GetShort()
            {
                return short.Parse(GetNumber(false));
            }

            public short? GetShort_NULL()
            {
                var v = GetNumber(true);
                return v == null ? null : (short?)short.Parse(v);
            }

            public ushort GetUShort()
            {
                return ushort.Parse(GetNumber(false));
            }

            public ushort? GetUShort_NULL()
            {
                var v = GetNumber(true);
                return v == null ? null : (ushort?)ushort.Parse(v);
            }

            public bool GetBool()
            {
                var v = GetBoolean(false);
                return v.Equals("true", StringComparison.OrdinalIgnoreCase) ? true : false;
            }

            public bool? GetBool_NULL()
            {
                var v = GetBoolean(true);
                return v == null ? null : (bool?)(v.Equals("true", StringComparison.OrdinalIgnoreCase) ? true : false);
            }

            public sbyte GetSByte()
            {
                return sbyte.Parse(GetNumber(false));
            }

            public sbyte? GetSByte_NULL()
            {
                var v = GetNumber(true);
                return v == null ? null : (sbyte?)sbyte.Parse(v);
            }

            public byte GetByte()
            {
                return byte.Parse(GetNumber(false));
            }

            public byte? GetByte_NULL()
            {
                var v = GetNumber(true);
                return v == null ? null : (byte?)byte.Parse(v);
            }

            public float GetFloat()
            {
                return float.Parse(GetNumber(false), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture);
            }

            public float? GetFloat_NULL()
            {
                var v = GetNumber(true);
                return v == null ? null : (float?)float.Parse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture);
            }

            public double GetDouble()
            {
                return double.Parse(GetNumber(false), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture);
            }

            public double? GetDouble_NULL()
            {
                var v = GetNumber(true);
                return v == null ? null : (double?)double.Parse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture);

            }

            public decimal GetDecimal()
            {
                return decimal.Parse(GetNumber(false), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture);
            }

            public decimal? GetDecimal_NULL()
            {
                var v = GetNumber(true);
                return v == null ? null : (decimal?)decimal.Parse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture);
            }

            public char GetChar()
            {
                return GetStr(false)[0];
            }

            public char? GetChar_NULL()
            {
                var v = GetStr(true);
                return v == null ? null : (char?)v[0];

            }

            public string GetString()
            {
                return GetStr(true);
            }

            public byte[] GetByteArray()
            {
                var v = GetStr(true);
                return v == null ? null : Convert.FromBase64String(v);
            }

            public Guid GetGuid()
            {
                var v = GetStr(false);
                return new Guid(v);
            }

            public Guid? GetGuid_NULL()
            {
                var v = GetStr(true);
                return v == null ? null : (Guid?)(new Guid(v));
            }

        }//eoc
    }
}
