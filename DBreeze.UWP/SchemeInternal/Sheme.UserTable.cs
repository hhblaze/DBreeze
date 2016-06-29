/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.Exceptions;

namespace DBreeze.SchemeInternal
{
    public static class DbUserTables
    {
        /// <summary>
        /// Checks validity of the user table name
        /// </summary>
        /// <param name="tableName"></param>
        public static void UserTableNameIsOk(string tableName)
        {
            if (tableName == String.Empty)
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TABLE_NAMES_TABLENAMECANTBEEMPTY);         

            for (int i = 0; i < tableName.Length; i++)
            {
                switch(tableName[i])
                {
                    case '*':   //used as pattern mask                    
                    case '#':   //used as pattern mask
                    case '$':   //used as pattern mask
                    case '@':   //used for system tables
                    case '\\':  //reserved by dbreeze
                    case '^':   //reserved by dbreeze                    
                    case '~':   //reserved by dbreeze
                    case '´':   //reserved by dbreeze                    
                        throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TABLE_NAMES_TABLENAMECANT_CONTAINRESERVEDSYMBOLS); 
                }
            }
            
            return;

        }

        /// <summary>
        /// Throws exception if smth. happened.
        /// Returns either original table name or cutted if * is found
        /// </summary>
        /// <param name="tableName"></param>
        /// <returns></returns>
        public static string UserTablePatternIsOk(string tableName)
        {
           
            //RULES: 
            // tableName should not be empty
            // * means all following characters -> .+   <- what means must 1 or more any characters
            // $ means all following characters without slash, after $ should be no more characters
            // # means must be 1 or more characters, except slash, and followed by slash and another symbol
            // after slash must come something else
            // all what is after * will be cutted (for patterns storage)

            //Cars# - NOT acceptable (no trailing slash)  - may be add slash automatic?
            //Cars#/ - NOT acceptable (no symbol after slash)
            //Cars#/Items# - NOT acceptable (no trailing slash)
            //Cars#/I - acceptable
            //Cars#/* - acceptable
            //Cars*/Items -> converts into Cars* - and acceptable
            //Cars* - acceptable
            //Cars#/Items123 - acceptable            
            //Cars#/Items#/ - acceptable
            //Cars#/Items* - acceptable


            if (tableName == String.Empty)
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TABLE_PATTERN_CANTBEEMPTY);  

            for (int i = 0; i < tableName.Length; i++)
            {
                switch (tableName[i])
                {
                    case '*':
                        //Substring till * and return
                        return tableName.Substring(0, i+1);
                    case '$':
                        //Substring till $ and return
                        return tableName.Substring(0, i + 1);
                    case '#':

                        if ((i + 2) > (tableName.Length - 1))
                            throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TABLE_PATTERN_SYMBOLS_AFTER_SHARP); 

                        if (tableName[i + 1] != '/')
                            throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TABLE_PATTERN_SYMBOLS_AFTER_SHARP); 
                      
                        break;
                }
            }

            return tableName;
        }

        #region "Test patterns"

        //private static void TestUserTablesPatterns(bool assumption, string tableName)
        //{
        //    string res = UserTablePatternIsOk(tableName);

        //    string conres = "OK";

        //    if ((res != String.Empty) != assumption)
        //        conres = "ERROR";

        //    Console.WriteLine("{0}; {3}; {1}; {2}", conres, tableName, res, (res == String.Empty) ? "False" : "True");
        //}

        //public static void UserTablesPatternsCheck()
        //{
        //    //Already succesfully tested
        //    TestUserTablesPatterns(true, "a123");
        //    TestUserTablesPatterns(true, "a#/cars");

        //    TestUserTablesPatterns(false, "a123/Items#");
        //    TestUserTablesPatterns(false, "a123/Items#x");
        //    TestUserTablesPatterns(false, "a123/Items#xpo");

        //    TestUserTablesPatterns(true, "a123/Items/");
        //    TestUserTablesPatterns(false, "a123/Items#/");
        //    TestUserTablesPatterns(true, "a123/Items#/1");

        //    TestUserTablesPatterns(true, "abr*dfdsfs");
        //}
        #endregion

        /// <summary>
        /// 
        /// </summary>
        /// <param name="p1"></param>
        /// <param name="p2"></param>
        /// <returns></returns>
        public static bool PatternsIntersect(string p1, string p2)
        {
            int p1i = 0;
            int p2i = 0;

            int p1L = p1.Length;
            int p2L = p2.Length;

            
            for (; ; )
            {

                if (p1i > (p1.Length - 1) || p2i > (p2.Length - 1))             
                    break;              

                switch (p1[p1i])
                {
                    case '#':

                        switch (p2[p2i])
                        {
                            case '#':

                                //we must move p1i to the slash and p2i to the slash and go on check
                                p1i = p1.IndexOf('/', p1i); //Definitely must be more then -1, by table name rules
                                p2i = p2.IndexOf('/', p2i); //Definitely must be more then -1, by table name rules

                                break;
                            case '*':
                                return true;        //MATCH
                            case '/':
                                //p1i += 2;
                                //p2i += 1;
                                return false;       //NOT MATCH   
                            case '$':
                                //$ is a mask without slash
                                return false;       //NOT MATCH
                            default:     
                           
                                //seraching index of next slash
                                p2i = p2.IndexOf('/', p2i);
                                if (p2i == -1)
                                    return false;                   //NOT MATCH
                                                               

                                p1i += 1;
                                break;
                            
                        }


                        break;
                    case '*':
                        return true;                                //MATCH
                    case '$':

                        if (p2[p2i] == '*')
                            return true;  

                        p2i = p2.IndexOf('/', p2i);
                        if (p2i != -1)
                            return false;                           //NOT MATCH

                        return true;                                //MATCH
                    case '/':
                        if (p2[p2i] == '*')
                            return true;                            //Match

                        if (p2[p2i] != '/')
                            return false;                           //NOT MATCH
                        
                        p2i += 1;
                        p1i += 1;
                        break;
                    default:    //any other symbol

                        switch (p2[p2i])
                        {
                            case '#':

                                //searching next slash in p1

                                p1i = p1.IndexOf('/', p1i);
                                if (p1i == -1)
                                    return false;                   //NOT MATCH

                                p2i += 1;
                                break;
                            case '*':
                                return true;        //MATCH
                            case '$':
                                p1i = p1.IndexOf('/', p1i);
                                if (p1i != -1)
                                    return false;                   //NOT MATCH

                                return true;                        //MATCH
                            default:
                                if (p1[p1i] != p2[p2i])
                                    return false;                   //NOT MATCH

                                p2i += 1;
                                p1i += 1;
                                break;

                        }

                        break;
                }
            }

            if (p1i <= (p1.Length - 1) || p2i <= (p2.Length - 1))
                return false;                                      //NOT MATCH  


            return true;                                        //MATCH

        }


        /// <summary>
        /// Checks intersection between two lists of patterns
        /// </summary>
        /// <param name="list1"></param>
        /// <param name="list2"></param>
        /// <returns></returns>
        public static bool TableNamesIntersect(List<string> list1,List<string> list2)
        {
            var q = from a in list1
                    select a;

            var q1 = from a in list2
                    select a;

            if (list1.Count() > list2.Count())
            {
                q = from a in list2
                    select a;

                q1 = from a in list1
                    select a;
            }

            foreach (var p1 in q)
            {
                foreach(var p2 in q1)
                {
                    if (PatternsIntersect(p1, p2))
                        return true;
                }
            }


            return false;
        }

        /// <summary>
        /// Checks intersection between List of patterns and one pattern
        /// </summary>
        /// <param name="list1"></param>
        /// <param name="tableName"></param>
        /// <returns></returns>
        public static bool TableNamesContains(List<string> list1, string tableName)
        {
            var q = from a in list1
                    select a;          

            foreach (var p1 in q)
            {
                if (PatternsIntersect(p1, tableName))
                    return true;
            }


            return false;
        }

        #region "test Intersection"
        /// <summary>
        /// TEST Intersections pattern
        /// </summary>
        /// <param name="assumption">your assumption, if intersects or not</param>
        /// <param name="p1">pattern 1</param>
        /// <param name="p2">pattern 2</param>
        private static void TestIntersectionPatterns(bool assumption, string p1, string p2)
        {
            bool res = PatternsIntersect(p1, p2);

            string conres = "OK";

            if (res != assumption)
                conres = "ERROR";

            System.Diagnostics.Debug.WriteLine("{3}; {0}; {1} x {2}", PatternsIntersect(p1, p2), p1, p2, conres);
        }

        //public static void PatternsIntersectionsCheck()
        //{
        //    //Already succesfully tested

        //    TestIntersectionPatterns(true, "Cars$", "Cars123");
        //    TestIntersectionPatterns(false, "Cars$", "Cars");
        //    TestIntersectionPatterns(true, "Cars$", "Cars*");
        //    TestIntersectionPatterns(false, "Cars$", "Cars123/Items");

        //    //TestIntersectionPatterns(false, "Cars#/Items#/Pictures/OneHoh#/*", "Cars123/Items458FG/Pictures/OneHoh12/");
        //    //TestIntersectionPatterns(true, "Cars#/Items#/Pictures/OneHoh#/*", "Cars123/Items458FG/Pictures/OneHoh12/test");
        //    //TestIntersectionPatterns(true, "Cars#/Items#/Pictures/OneHoh*", "Cars123/Items458FG/Pictures/OneHoh127");

        //    //TestIntersectionPatterns(true, "#/#/*", "A/*");
        //    //TestIntersectionPatterns(false, "#/#/*", "/*");

        //    //TestIntersectionPatterns(false, "abc234", "Abc234");
        //    //TestIntersectionPatterns(true, "Abc234/itEms12", "Abc234/itEms12");
        //    //TestIntersectionPatterns(false, "Abc234/itEms12", "Abc234/itEms13");
        //    //TestIntersectionPatterns(false, "Abc#/itEms12", "Abc234/itEms13");
        //    //TestIntersectionPatterns(false, "Abc234/itEms13", "Abc#/itEms12");
        //    //TestIntersectionPatterns(true, "Abc#/itEms12", "Abc234/itEms12");
        //    //TestIntersectionPatterns(true, "Abc234/itEms12", "Abc#/itEms12");
        //    //TestIntersectionPatterns(true, "abc234", "abc234");
        //    //TestIntersectionPatterns(false, "abc234", "abc235");

        //    //TestIntersectionPatterns(false, "a/cars", "a#/cars");
        //    //TestIntersectionPatterns(false, "a#/cars", "a/cars");
        //    //TestIntersectionPatterns(true, "a#/cars", "a123/cars");
        //    //TestIntersectionPatterns(true, "a123/cars", "a#/cars");

        //    //TestIntersectionPatterns(true, "a*", "a#/cars");
        //    //TestIntersectionPatterns(true, "a#/cars", "a*");
        //    //TestIntersectionPatterns(true, "a*", "a123/cars");
        //    //TestIntersectionPatterns(true, "a123/cars", "a*");

        //    //TestIntersectionPatterns(false, "Cars#/Items#/Pictures", "Cars#/Items#/Invoices");
        //    //TestIntersectionPatterns(true, "Cars#/Items#/Pictures", "Cars#/Items#/Pictures");
        //    //TestIntersectionPatterns(true, "Cars#/Items#/Pictures", "Cars123/Items458FG/Pictures");
        //    //TestIntersectionPatterns(true, "Cars123/Items458FG/Pictures", "Cars#/Items#/Pictures");

        //    //TestIntersectionPatterns(false, "Cars123/Items458FG/Pictures", "Cars#/Items#/Invoices");
        //    //TestIntersectionPatterns(false, "Cars#/Items#/Invoices", "Cars123/Items458FG/Pictures");
        //    /////////////////////////////////////////
        //}

        //public static void PatternsIntersectionsCheckSpeed()
        //{
        //    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        //    sw.Start();

        //    for (int i = 0; i < 1000000; i++)
        //    {
        //        PatternsIntersect("Cars#/Items#/Pictures/OneHoh#/*", "Cars123/Items458FG/Pictures/OneHoh12/test");
        //    }

        //    sw.Stop();
        //    Console.WriteLine("Elapsed time: {0} ms", sw.ElapsedMilliseconds);
        //}

        #endregion
    }
}
