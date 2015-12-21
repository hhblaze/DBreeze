using System;
using System.Collections.Generic;
using System.Linq;

using Foundation;
using UIKit;

using DBreeze;

namespace TestIOS
{
    public class Application
    {
        static DBreezeEngine engine = null;
        // This is the main entry point of the application.
        static void Main(string[] args)
        {
            // if you want to use a different Application Delegate class from "AppDelegate"
            // you can specify it here.
            UIApplication.Main(args, null, "AppDelegate");

            if (engine == null)
            {
                engine = new DBreezeEngine(@"ADD YOUR FILESYSTEM PATH TO DBREEZE FOLDER");
            }

            using (var tran = engine.GetTransaction())
            {
                tran.Insert<int, string>("t1", 1, "val1");
                tran.Insert<int, string>("t1", 2, "val1");
                tran.Commit();
            }

            using (var tran = engine.GetTransaction())
            {
                Console.WriteLine("Inserted val" + tran.Select<int, string>("t1", 1).Value);

            }
        }
    }
}