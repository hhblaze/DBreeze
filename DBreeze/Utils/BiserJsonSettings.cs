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
        /// JsonSettings
        /// </summary>
        public class JsonSettings
        {
            public enum DateTimeStyle
            {

                /// <summary>
                /// Default /Date(...)/
                /// </summary>
                Default,
                /// <summary>
                /// ISO Format: "2018-06-05T17:44:15.4430000Z" or "2018-06-05T17:44:15.4430000+02:00"
                /// </summary>
                ISO,
                /// <summary>
                /// Unix Epoch Milliseconds. Fastest for both operations
                /// </summary>
                EpochTime,
                /// <summary>
                /// Each local time must be converted into UTC and then represented as ISO
                /// </summary>
                Javascript
            }

            public enum JsonStringStyle
            {
                Default,
                Prettify
            }

            public JsonSettings()
            {

            }

            public DateTimeStyle DateFormat { get; set; } = DateTimeStyle.Default;
            public JsonStringStyle JsonStringFormat { get; set; } = JsonStringStyle.Default;

        }
    }
}
