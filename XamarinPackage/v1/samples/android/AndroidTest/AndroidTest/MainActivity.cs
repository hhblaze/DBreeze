using System;
using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;

using DBreeze;

namespace AndroidTest
{
    [Activity(Label = "AndroidTest", MainLauncher = true, Icon = "@drawable/icon")]
    public class MainActivity : Activity
    {
        int count = 1;
        DBreezeEngine engine = null;

        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Main);

            // Get our button from the layout resource,
            // and attach an event to it
            Button button = FindViewById<Button>(Resource.Id.MyButton);

            if (engine == null)
            {
                engine = new DBreezeEngine(@"./sdcard/tiesky.com/DBreezeTest");
            }

            button.Click += delegate {

                using (var tran = engine.GetTransaction())
                {
                    tran.Insert<int, string>("t1", count, "val" + count);
                    tran.Commit();
                }
                
                using (var tran = engine.GetTransaction())
                {                
                    button.Text = "Please, read detailed documentation on http://dbreeze.codeplex.com/ or http://dbreeze.tiesky.com/" + 
                        "... Inserted value is " + tran.Select<int, string>("t1", count).Value;
                    
                }

                count++;

            };
        }
    }
}

