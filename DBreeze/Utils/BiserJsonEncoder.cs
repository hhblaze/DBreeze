/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.

  https://github.com/hhblaze/Biser
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;

namespace DBreeze.Utils
{
    public static partial class Biser
    {
        /// <summary>
        /// Biser JSON Encoder
        /// </summary>
        public class JsonEncoder
        {
            JsonSettings jsonSettings = null;

            StringBuilder sb = null;
            string finished = null;
            char lastchar = '{';

            public JsonEncoder(JsonSettings settings = null)
            {
                jsonSettings = (settings == null) ? new JsonSettings() : settings;

                sb = new StringBuilder();
                sb.Append("{"); //Always start as an object
            }

            public JsonEncoder(IJsonEncoder obj, JsonSettings settings = null)
                :this(settings)
            {
                if(obj != null)
                    obj.BiserJsonEncode(this);
            }


            void AddProp(string str)
            {
                if (lastchar != '{' && lastchar != '[' && lastchar != ',')
                    sb.Append(",\"");
                else
                    sb.Append("\"");

                //sb.Append(str.Replace("\"", "\\\""));
                foreach (var ch in str)
                {
                    if (ch == '\"')
                        sb.Append('\\');
                    else if (ch == '\\')
                        sb.Append('\\');
                    sb.Append(ch);
                }
                sb.Append("\":");
                //AddStr(str);
                //sb.Append(":");
                lastchar = ':';
            }


            void AddStr(string str)
            {

                sb.Append("\"");
                // sb.Append(str.Replace("\"", "\\\""));

                foreach (var ch in str)
                {
                    if (ch == '\"')
                        sb.Append('\\');
                    else if (ch == '\\')
                        sb.Append('\\');
                    sb.Append(ch);
                }
                sb.Append("\"");
            }

            void AddNull()
            {
                sb.Append("null");
            }

            public string GetJSON(JsonSettings.JsonStringStyle style)
            {
                this.jsonSettings.JsonStringFormat = style;
                return GetJSON();
            }

            public string GetJSON()
            {
                if (finished == null)
                {
                    sb.Append("}");
                    finished = sb.ToString();
                }

                if (this.jsonSettings.JsonStringFormat == JsonSettings.JsonStringStyle.Prettify)
                {
                    return Prettify();
                }
                else
                    return finished;

            }

            string prettified = null;
            string Prettify()
            {
                if (prettified != null)
                    return prettified;

                StringBuilder sbp = new StringBuilder();
                int tabs = 0;
                bool instr = false;
                char prevchar = ',';

                foreach (var el in finished)
                {

                    if (!instr && (el == ' ' || el == '\t' || el == '\r' || el == '\n'))
                        continue;

                    if (!instr && el == ',')
                    {
                        sbp.Append(el);
                    }
                    else if (!instr && (el == '[' || el == '{'))
                    {
                        if (tabs != 0)
                        {
                            sbp.Append('\n');
                            DrawTabs(tabs, sbp);
                        }
                        tabs++;

                        sbp.Append(el);
                    }
                    else if (!instr && (el == ']' || el == '}'))
                    {
                        sbp.Append('\n');
                        tabs--;
                        DrawTabs(tabs, sbp);
                        sbp.Append(el);
                    }
                    else if (!instr)
                    {
                        if (prevchar == ',' || prevchar == '[' || prevchar == ']' || prevchar == '{' || prevchar == '}')
                        {
                            sbp.Append('\n');
                            DrawTabs(tabs, sbp);
                        }
                        sbp.Append(el);
                    }
                    else
                    {
                        sbp.Append(el);
                    }


                    if (!instr && el == '\"')
                        instr = true;
                    else if (instr && el == '\"' && prevchar != '\\')
                        instr = false;


                    prevchar = el;
                }
                prettified = sbp.ToString();
                return prettified;
            }

            void DrawTabs(int cnt, StringBuilder sbp)
            {
                if (cnt == 0)
                    return;

                for (int i = 0; i < cnt; i++)
                    sbp.Append('\t');
            }

            public JsonEncoder Add(DateTime val)
            {
                AppendDateTime(val);
                return this;

                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, DateTime val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == DateTime.MinValue)
                        return this;
                    AddProp(propertyName);
                }

                AppendDateTime(val);
                return this;
            }

            public JsonEncoder Add(DateTime? val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }

                AppendDateTime((DateTime)val);
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, DateTime? val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                    {
                        if (val == DateTime.MinValue)
                            return this;
                        AddProp(propertyName);
                    }
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }

                AppendDateTime((DateTime)val);
                return this;
            }





            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            void AppendDateTime(DateTime dt)
            {
                switch (this.jsonSettings.DateFormat)
                {
                    case JsonSettings.DateTimeStyle.Default:
                        ////sb.Append("\"");
                        sb.Append("\"\\/Date(");

                        // sb.Append($"\"\\/Date({((dt.Kind == DateTimeKind.Utc) ? ((ulong)(dt.Subtract(epoch).TotalMilliseconds) * 10000) : ((ulong)(dt.ToUniversalTime().Subtract(epoch).TotalMilliseconds) * 10000)) })\\/\"");

                        if (dt.Kind == DateTimeKind.Utc)
                            sb.Append(((ulong)(dt.Subtract(epoch).TotalMilliseconds) * 10000).ToString());
                        else
                            sb.Append(((ulong)(dt.ToUniversalTime().Subtract(epoch).TotalMilliseconds) * 10000).ToString());
                        sb.Append(")\\/\"");
                        ////sb.Append("\"");
                        break;
                    case JsonSettings.DateTimeStyle.EpochTime:
                        if (dt.Kind == DateTimeKind.Utc)
                            sb.Append(((ulong)(dt.Subtract(epoch).TotalMilliseconds) * 10000).ToString());
                        else
                            sb.Append(((ulong)(dt.ToUniversalTime().Subtract(epoch).TotalMilliseconds) * 10000).ToString());
                        break;
                    case JsonSettings.DateTimeStyle.ISO:
                        sb.Append($"\"{dt.ToString("o")}\"");
                        //sb.Append("\"");
                        //sb.Append(dt.ToString("o"));
                        //sb.Append("\"");
                        //sb.Append($"\"{dt.Year}-{String.Format("{0:00}", dt.Month)}-{String.Format("{0:00}", dt.Day)}T{String.Format("{0:00}", dt.Hour)}:{String.Format("{0:00}", dt.Minute)}:{String.Format("{0:00}", dt.Second)}.{dt.Millisecond}Z\"");
                        //sb.Append("\"2018-08-21T09:42:21.9770676Z\"");
                        //		dt.ToString("o")	"2018-08-21T09:42:21.9770676Z"	string

                        break;
                    case JsonSettings.DateTimeStyle.Javascript:
                        sb.Append("\"");
                        if (dt.Kind == DateTimeKind.Utc)
                            sb.Append(dt.ToString("o"));
                        else
                            sb.Append(dt.ToUniversalTime().ToString("o"));
                        sb.Append("\"");
                        break;

                }
            }

            public JsonEncoder Add(int val)
            {
                sb.Append(val);
                return this;
                // return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, int val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                    AddProp(propertyName);
                sb.Append(val);
                return this;
            }

            public JsonEncoder Add(int? val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(val);
                return this;
                //  return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, int? val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                        AddProp(propertyName);
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(val);
                return this;
            }





            public JsonEncoder Add(string val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }
                AddStr(val);
                return this;

                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, string val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                        AddProp(propertyName);
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }

                AddStr(val);
                return this;
            }



            public JsonEncoder Add(long val)
            {
                sb.Append(val);
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, long val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                    AddProp(propertyName);
                sb.Append(val);
                return this;
            }

            public JsonEncoder Add(long? val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }
                sb.Append(val);
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, long? val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                        AddProp(propertyName);
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(val);
                return this;
            }




            public JsonEncoder Add(ulong val)
            {
                //return Add(null, val);
                sb.Append(val);
                return this;
            }

            public JsonEncoder Add(string propertyName, ulong val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                    AddProp(propertyName);
                sb.Append(val);
                return this;
            }

            public JsonEncoder Add(ulong? val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(val);
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, ulong? val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                        AddProp(propertyName);
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(val);
                return this;
            }




            public JsonEncoder Add(uint val)
            {
                sb.Append(val);
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, uint val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                    AddProp(propertyName);
                sb.Append(val);
                return this;
            }

            public JsonEncoder Add(uint? val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(val);
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, uint? val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                        AddProp(propertyName);
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(val);
                return this;
            }




            public JsonEncoder Add(short val)
            {
                sb.Append(val);
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, short val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                    AddProp(propertyName);
                sb.Append(val);
                return this;
            }

            public JsonEncoder Add(short? val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(val);
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, short? val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                        AddProp(propertyName);
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(val);
                return this;
            }




            public JsonEncoder Add(ushort val)
            {
                sb.Append(val);
                return this;

                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, ushort val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                    AddProp(propertyName);
                sb.Append(val);
                return this;
            }

            public JsonEncoder Add(ushort? val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(val);
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, ushort? val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                        AddProp(propertyName);
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(val);
                return this;
            }




            public JsonEncoder Add(sbyte val)
            {
                sb.Append(val);
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, sbyte val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                    AddProp(propertyName);
                sb.Append(val);
                return this;
            }

            public JsonEncoder Add(sbyte? val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(val);
                return this;

                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, sbyte? val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                        AddProp(propertyName);
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(val);
                return this;
            }



            public JsonEncoder Add(byte val)
            {
                sb.Append(val);
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, byte val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                    AddProp(propertyName);
                sb.Append(val);
                return this;
            }

            public JsonEncoder Add(byte? val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(val);
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, byte? val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                        AddProp(propertyName);
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(val);
                return this;
            }




            public JsonEncoder Add(bool val)
            {
                sb.Append(val.ToString().ToLower());
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, bool val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                    AddProp(propertyName);
                sb.Append(val.ToString().ToLower());
                return this;
            }

            public JsonEncoder Add(bool? val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(val.ToString().ToLower());
                return this;
                //return Add(null, val);
            }


            public JsonEncoder Add(string propertyName, bool? val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                        AddProp(propertyName);
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(val.ToString().ToLower());
                return this;
            }



            public JsonEncoder Add(char val)
            {
                AddStr(val.ToString());
                return this;

                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, char val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                    AddProp(propertyName);
                AddStr(val.ToString());
                return this;
            }

            public JsonEncoder Add(char? val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }

                AddStr(val.ToString());
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, char? val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                        AddProp(propertyName);
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }

                AddStr(val.ToString());
                return this;
            }



            public JsonEncoder Add(float val)
            {
                sb.Append(val.ToString(CultureInfo.InvariantCulture));
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, float val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                    AddProp(propertyName);
                sb.Append(val.ToString(CultureInfo.InvariantCulture));
                return this;
            }

            public JsonEncoder Add(float? val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(((float)val).ToString(CultureInfo.InvariantCulture));
                return this;

                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, float? val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                        AddProp(propertyName);
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(((float)val).ToString(CultureInfo.InvariantCulture));
                return this;
            }




            public JsonEncoder Add(double val)
            {
                sb.Append(val.ToString("r", CultureInfo.InvariantCulture));
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, double val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                    AddProp(propertyName);
                sb.Append(val.ToString("r", CultureInfo.InvariantCulture));
                return this;
            }

            public JsonEncoder Add(double? val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(((double)val).ToString("r", CultureInfo.InvariantCulture));
                return this;

                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, double? val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                        AddProp(propertyName);
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(((double)val).ToString("r", CultureInfo.InvariantCulture));
                return this;
            }



            public JsonEncoder Add(decimal val)
            {
                sb.Append(val.ToString(CultureInfo.InvariantCulture));
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, decimal val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                    AddProp(propertyName);
                sb.Append(val.ToString(CultureInfo.InvariantCulture));
                return this;
            }

            public JsonEncoder Add(decimal? val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(((decimal)val).ToString(CultureInfo.InvariantCulture));
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, decimal? val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                        AddProp(propertyName);
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }

                sb.Append(((decimal)val).ToString(CultureInfo.InvariantCulture));
                return this;
            }




            public JsonEncoder Add(Guid val)
            {
                AddStr(val.ToString());
                return this;
                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, Guid val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                    AddProp(propertyName);
                AddStr(val.ToString());
                return this;
            }

            public JsonEncoder Add(Guid? val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }

                AddStr(((Guid)val).ToString());

                return this;

                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, Guid? val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                        AddProp(propertyName);
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }

                AddStr(((Guid)val).ToString());

                return this;
            }




            public JsonEncoder Add(byte[] val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }

                AddStr(Convert.ToBase64String(val));
                return this;

                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, byte[] val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                        AddProp(propertyName);
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }

                AddStr(Convert.ToBase64String(val));
                return this;
            }



            public JsonEncoder Add(TimeSpan val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }

                AddStr(val.ToString());
                return this;

                //return Add(null, val);
            }

            public JsonEncoder Add(string propertyName, TimeSpan val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                        AddProp(propertyName);
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }

                AddStr(val.ToString());
                return this;
            }






            /// <summary>
            /// To supply heterogen values inside of Dictionary
            /// </summary>
            /// <typeparam name="V"></typeparam>
            /// <param name="propertyName"></param>
            /// <param name="val"></param>
            /// <returns></returns>
            public JsonEncoder Add(string propertyName, Dictionary<string, Action> val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                    {
                        AddProp(propertyName);
                        if (val.Count == 0)
                        {
                            sb.Append("{}");
                            lastchar = '}';
                            return this;
                        }
                    }
                }
                else if (val == null)
                {
                    AddNull();
                    lastchar = '}';
                    return this;
                }


                sb.Append("{");
                lastchar = '{';

                foreach (var item in val)
                {
                    if (lastchar == '}' || lastchar == ']')
                    {
                        sb.Append(",");
                        lastchar = ',';
                    }
                    AddProp(item.Key);
                    item.Value();

                    lastchar = '}'; //to put commas after standard values
                }
                sb.Append("}");
                lastchar = '}';
                return this;

            }

            /// <summary>
            /// To supply heterogen values inside of Dictionary
            /// </summary>
            /// <param name="val"></param>
            /// <returns></returns>
            public JsonEncoder Add(Dictionary<string, Action> val)
            {
                if (val == null)
                {
                    AddNull();
                    lastchar = '}';
                    return this;
                }


                sb.Append("{");
                lastchar = '{';

                foreach (var item in val)
                {
                    if (lastchar == '}' || lastchar == ']')
                    {
                        sb.Append(",");
                        lastchar = ',';
                    }
                    AddProp(item.Key);
                    item.Value();

                    lastchar = '}'; //to put commas after standard values
                }
                sb.Append("}");
                lastchar = '}';
                return this;

                //return Add(null, val);
            }

            /// <summary>
            /// Supplies heterogonenous array elements
            /// </summary>
            /// <param name="propertyName"></param>
            /// <param name="val"></param>
            /// <returns></returns>
            public JsonEncoder Add(string propertyName, List<Action> val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                    {
                        AddProp(propertyName);
                        if (val.Count() == 0)
                        {
                            sb.Append("[]");
                            lastchar = ']';
                            return this;
                        }
                    }
                }
                else if (val == null)
                {
                    AddNull();
                    lastchar = ']';
                    return this;
                }

                sb.Append("[");
                lastchar = '[';

                foreach (var item in val)
                {
                    if (lastchar == '}' || lastchar == ']')
                    {
                        sb.Append(",");
                        lastchar = ',';
                    }

                    item();

                    lastchar = '}'; //to put commas after standard values
                }
                sb.Append("]");
                lastchar = ']';
                return this;

            }

            /// <summary>
            ///  Supplies heterogonenous array elements
            /// </summary>
            /// <param name="val"></param>
            /// <returns></returns>
            public JsonEncoder Add(List<Action> val)
            {
                if (val == null)
                {
                    AddNull();
                    lastchar = ']';
                    return this;
                }

                sb.Append("[");
                lastchar = '[';

                foreach (var item in val)
                {
                    if (lastchar == '}' || lastchar == ']')
                    {
                        sb.Append(",");
                        lastchar = ',';
                    }

                    item();

                    lastchar = '}'; //to put commas after standard values
                }
                sb.Append("]");
                lastchar = ']';
                return this;

                //return Add(null, val);
            }


            Type TypeString = typeof(string);

            /// <summary>
            /// Adds Dictionary each Key will be transformed into String
            /// </summary>
            /// <typeparam name="K"></typeparam>
            /// <typeparam name="V"></typeparam>
            /// <param name="propertyName"></param>
            /// <param name="val"></param>
            /// <param name="f"></param>
            /// <returns></returns>
            public JsonEncoder Add<K, V>(string propertyName, IDictionary<K, V> val, Action<V> f)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                    {
                        AddProp(propertyName);
                        if (val.Count == 0)
                        {
                            sb.Append("{}");
                            lastchar = '}';
                            return this;
                        }
                    }
                }
                else if (val == null)
                {
                    AddNull();
                    lastchar = '}';
                    return this;
                }


                sb.Append("{");
                lastchar = '{';

                foreach (var item in val)
                {
                    if (lastchar == '}' || lastchar == ']')
                    {
                        sb.Append(",");
                        lastchar = ',';
                    }
                    AddProp((string)Convert.ChangeType(item.Key, TypeString));
                    f(item.Value);

                    lastchar = '}'; //to put commas after standard values
                }
                sb.Append("}");
                lastchar = '}';
                return this;

            }

            /// <summary>
            ///  Adds Dictionary each Key will be transformed into String
            /// </summary>
            /// <typeparam name="K"></typeparam>
            /// <typeparam name="V"></typeparam>
            /// <param name="val"></param>
            /// <param name="f"></param>
            /// <returns></returns>
            public JsonEncoder Add<K, V>(IDictionary<K, V> val, Action<V> f)
            {
                if (val == null)
                {
                    AddNull();
                    lastchar = '}';
                    return this;
                }


                sb.Append("{");
                lastchar = '{';

                foreach (var item in val)
                {
                    if (lastchar == '}' || lastchar == ']')
                    {
                        sb.Append(",");
                        lastchar = ',';
                    }
                    AddProp((string)Convert.ChangeType(item.Key, TypeString));
                    f(item.Value);

                    lastchar = '}'; //to put commas after standard values
                }
                sb.Append("}");
                lastchar = '}';
                return this;

                // return Add(null, val, f);
            }

            /// <summary>
            /// Adds class implementing IJsonEncoder
            /// </summary>
            /// <param name="propertyName"></param>
            /// <param name="val"></param>
            /// <returns></returns>
            public JsonEncoder Add(string propertyName, IJsonEncoder val)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                        AddProp(propertyName);
                }
                else if (val == null)
                {
                    AddNull();
                    return this;
                }


                sb.Append("{");
                lastchar = '{';
                val.BiserJsonEncode(this);
                sb.Append("}");
                lastchar = '}';

                return this;
            }

            public JsonEncoder Add(IJsonEncoder val)
            {
                if (val == null)
                {
                    AddNull();
                    return this;
                }


                sb.Append("{");
                lastchar = '{';
                val.BiserJsonEncode(this);
                sb.Append("}");
                lastchar = '}';

                return this;

                //return Add(null,val);
            }

            /// <summary>
            /// Supply array and transformation function, one for each array element
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="propertyName"></param>
            /// <param name="val"></param>
            /// <param name="f"></param>
            /// <returns></returns>
            public JsonEncoder Add<T>(string propertyName, IEnumerable<T> val, Action<T> f)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (val == null)
                        return this;
                    else
                    {
                        AddProp(propertyName);
                        if (val.Count() == 0)
                        {
                            sb.Append("[]");
                            lastchar = ']';
                            return this;
                        }
                    }
                }
                else if (val == null)
                {
                    AddNull();
                    lastchar = ']';
                    return this;
                }

                sb.Append("[");
                lastchar = '[';


                foreach (var item in val)
                {
                    if (lastchar == '}' || lastchar == ']')
                    {
                        sb.Append(",");
                        lastchar = ',';
                    }

                    f(item);

                    lastchar = '}'; //to put commas after standard values
                }
                sb.Append("]");
                lastchar = ']';
                return this;
            }

            /// <summary>
            ///  Supply array and transformation function, one for each array element
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="val"></param>
            /// <param name="f"></param>
            /// <returns></returns>
            public JsonEncoder Add<T>(IEnumerable<T> val, Action<T> f)
            {
                if (val == null)
                {
                    AddNull();
                    lastchar = ']';
                    return this;
                }

                sb.Append("[");
                lastchar = '[';

                //#if NETSTANDARD
                //            bool ic = typeof(IJsonEncoder).GetTypeInfo().IsAssignableFrom(typeof(T).Ge‌​tTypeInfo());
                //            if(!ic)
                //                 ic = typeof(System.Collections.IDictionary).GetTypeInfo().IsAssignableFrom(typeof(T).Ge‌​tTypeInfo());
                //#else
                //            bool ic = typeof(IJsonEncoder).IsAssignableFrom(typeof(T));
                //            if(!ic)
                //                ic = typeof(System.Collections.IDictionary).IsAssignableFrom(typeof(T));
                //#endif          



                foreach (var item in val)
                {
                    if (lastchar == '}' || lastchar == ']')
                    {
                        sb.Append(",");
                        lastchar = ',';
                    }

                    f(item);

                    lastchar = '}'; //to put commas after standard values
                }
                sb.Append("]");
                lastchar = ']';
                return this;
                //return Add(null, items, f);
            }



        }
    }
}
