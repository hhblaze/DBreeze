
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Deployer
{
    public static class StringUtils
    {

        /// <summary>
        /// 
        /// </summary>
        /// <param name="input"></param>
        /// <param name="replaceWith"></param>
        /// <returns></returns>
        public static string ReplaceMultiple(this string input, Dictionary<string, string> replaceWith)
        {
            if (input == null || replaceWith == null || replaceWith.Count < 1)
                return input;

            replaceWith = replaceWith.OrderByDescending(r => r.Key.Length).ToDictionary(r => r.Key, r => r.Value);

            System.Text.RegularExpressions.Regex regex = null;
#if NET35
            //|| NETr40   //The same must be use for .NET 4.0
            regex = new System.Text.RegularExpressions.Regex(String.Join("|", replaceWith.Keys.Select(k => System.Text.RegularExpressions.Regex.Escape(k)).Cast<string>().ToArray() ));

#else
            regex = new System.Text.RegularExpressions.Regex(String.Join("|", replaceWith.Keys.Select(k => System.Text.RegularExpressions.Regex.Escape(k))));

#endif

            return regex.Replace(input, m => replaceWith[m.Value]);

        }
    }
}
