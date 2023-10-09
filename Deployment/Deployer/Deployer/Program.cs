using NuGet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Deployer
{
    class Program
    {
        public static string MyPath="";

        static void Main(string[] args)
        {
            MyPath = AssemblyDirectory + "\\";      //will be DBreeze\Deployment\bin\
            DirectoryInfo di = null;

#if DEBUG
            MyPath += @"..\..\..\..\bin\";
#endif

            di = new DirectoryInfo(MyPath);
            //string msbldpath = MyPath + "run_msbuild.bat";
            //string msbldpath = MyPath + "run_msbuild19.bat";
            string msbldpath = MyPath + "run_msbuild22.bat";

            string fileVersion = "";
            string productVersion = "";
            
            var tr = Deployer.Resource1.templ.Split(new char[] { '\r', '\n' },StringSplitOptions.RemoveEmptyEntries);
            List<string> rpl = new List<string>();          
            foreach (var t in tr)
                rpl.Add(t);

            bool noerror = false;
            string tpr = "";

            //IN DBreeze project one Framework is default (like .NET5 in NetCoreApp project ot .NET4.7.2 in DBreeze project)
            //When compiling subframeworks from that, we need to change base framework and its define-constants on the compiling framework
            string baseFramework="";
            string baseDefineConstants = "";
            string currentFramework = "";
            string currentDefineConstants = "";
            string prjDEBUG = "<Configuration Condition=\" '$(Configuration)' == '' \">Debug</Configuration>";
            string prjRELEASE = "<Configuration Condition=\" '$(Configuration)' == '' \">Release</Configuration>";

            string prj = File.ReadAllText(MyPath + @"..\..\DBreeze\DBreeze.csproj");

            bool skipRecompile = false;


            if (!skipRecompile)
            {




                //----------------------------------------- DBreeze (Framework) project --------------------------------------
                baseFramework = "<TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>";
                baseDefineConstants = "<DefineConstants>TRACE;NET40;NET472</DefineConstants>";


                /*

                                    Currently on .NET Frameworks, initial DBREEZE is .NET 4.7.2, so its constants are 
                                    <DefineConstants>TRACE;NET40;NET472</DefineConstants> [21]
                                    When constant is used it means that code-block can be used starting from this framework and higher
                                    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion> [19]
                 */




                //.NET Framework 3.5
                currentFramework = "<TargetFrameworkVersion>v3.5</TargetFrameworkVersion>";
                currentDefineConstants = "<DefineConstants>TRACE;NET35</DefineConstants>";

                if (Directory.Exists(MyPath + @"..\..\DBreeze\bin\Release"))
                    Directory.Delete(MyPath + @"..\..\DBreeze\bin\Release", true);
                tpr = prj;
                Console.WriteLine("Creating .NET35");
                //tpr = tpr.Replace(rpl[0], rpl[1]);    //Debug on Release
                ////tpr = tpr.Replace(rpl[2], rpl[3]); //4.5 on 3.5
                //tpr = tpr.Replace(rpl[19], rpl[3]); //4.7.2 on 3.5 //<TargetFrameworkVersion>v3.5</TargetFrameworkVersion> [3]
                ////tpr = tpr.Replace(rpl[5], rpl[6]); //NET40 on //NET35
                //tpr = tpr.Replace(rpl[21], rpl[6]); //NET472 on //NET35

                tpr = tpr.ReplaceMultiple(new Dictionary<string, string> { { prjDEBUG, prjRELEASE }, { baseFramework, currentFramework }, { baseDefineConstants, currentDefineConstants } });

                File.WriteAllText(MyPath + @"..\..\DBreeze\DBreezeTMP.csproj", tpr);
                Compile(msbldpath, "DBreeze");

                noerror = File.Exists(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.dll");
                Console.WriteLine("done " + noerror);
                if (!noerror)
                    return;
                File.Copy(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.dll", MyPath + @"NET35\DBreeze.dll", true);
                File.Copy(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.XML", MyPath + @"NET35\DBreeze.XML", true);


                //.NET Framework 4.0
                currentFramework = "<TargetFrameworkVersion>v4.0</TargetFrameworkVersion>";
                currentDefineConstants = "<DefineConstants>TRACE;NET40;NETr40</DefineConstants>";

                if (Directory.Exists(MyPath + @"..\..\DBreeze\bin\Release"))
                    Directory.Delete(MyPath + @"..\..\DBreeze\bin\Release", true);
                tpr = prj;
                Console.WriteLine("Creating .NET40");
                //tpr = tpr.Replace(rpl[0], rpl[1]);    //Debug on Release
                //tpr = tpr.Replace(rpl[19], rpl[4]); //4.7.2 on 4.0
                //tpr = tpr.Replace(rpl[21], rpl[7]); //NET472 on //NET40;NETr40
                tpr = tpr.ReplaceMultiple(new Dictionary<string, string> { { prjDEBUG, prjRELEASE }, { baseFramework, currentFramework }, { baseDefineConstants, currentDefineConstants } });

                File.WriteAllText(MyPath + @"..\..\DBreeze\DBreezeTMP.csproj", tpr);
                Compile(msbldpath, "DBreeze");

                noerror = File.Exists(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.dll");
                Console.WriteLine("done " + noerror);
                if (!noerror)
                    return;
                File.Copy(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.dll", MyPath + @"NET40\DBreeze.dll", true);
                File.Copy(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.XML", MyPath + @"NET40\DBreeze.XML", true);


                //.NET Framework 4.6.1
                currentFramework = "<TargetFrameworkVersion>v4.6.1</TargetFrameworkVersion>";
                currentDefineConstants = "<DefineConstants>TRACE;NET40</DefineConstants>";

                if (Directory.Exists(MyPath + @"..\..\DBreeze\bin\Release"))
                    Directory.Delete(MyPath + @"..\..\DBreeze\bin\Release", true);
                tpr = prj;
                Console.WriteLine("Creating .NET4.6.1");
                //tpr = tpr.Replace(rpl[0], rpl[1]);    //Debug on Release
                //tpr = tpr.Replace(rpl[19], rpl[11]); //4.7.2 on 4.6.1            
                //tpr = tpr.Replace(rpl[21], rpl[5]); //NET472 on //NET40
                tpr = tpr.ReplaceMultiple(new Dictionary<string, string> { { prjDEBUG, prjRELEASE }, { baseFramework, currentFramework }, { baseDefineConstants, currentDefineConstants } });

                File.WriteAllText(MyPath + @"..\..\DBreeze\DBreezeTMP.csproj", tpr);
                Compile(msbldpath, "DBreeze");

                noerror = File.Exists(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.dll");
                Console.WriteLine("done " + noerror);
                if (!noerror)
                    return;
                File.Copy(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.dll", MyPath + @"NET461\DBreeze.dll", true);
                File.Copy(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.XML", MyPath + @"NET461\DBreeze.XML", true);


                //.NET Framework 4.6.2
                currentFramework = "<TargetFrameworkVersion>v4.6.2</TargetFrameworkVersion>";
                currentDefineConstants = "<DefineConstants>TRACE;NET40</DefineConstants>";

                if (Directory.Exists(MyPath + @"..\..\DBreeze\bin\Release"))
                    Directory.Delete(MyPath + @"..\..\DBreeze\bin\Release", true);
                tpr = prj;
                Console.WriteLine("Creating .NET4.6.2");
                //tpr = tpr.Replace(rpl[0], rpl[1]);    //Debug on Release
                //tpr = tpr.Replace(rpl[19], rpl[12]); //4.7.2 on 4.6.2
                //tpr = tpr.Replace(rpl[21], rpl[5]); //NET472 on //NET40
                tpr = tpr.ReplaceMultiple(new Dictionary<string, string> { { prjDEBUG, prjRELEASE }, { baseFramework, currentFramework }, { baseDefineConstants, currentDefineConstants } });

                File.WriteAllText(MyPath + @"..\..\DBreeze\DBreezeTMP.csproj", tpr);
                Compile(msbldpath, "DBreeze");

                noerror = File.Exists(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.dll");
                Console.WriteLine("done " + noerror);
                if (!noerror)
                    return;
                File.Copy(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.dll", MyPath + @"NET462\DBreeze.dll", true);
                File.Copy(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.XML", MyPath + @"NET462\DBreeze.XML", true);


                //.NET Framework 4.7
                currentFramework = "<TargetFrameworkVersion>v4.7</TargetFrameworkVersion>";
                currentDefineConstants = "<DefineConstants>TRACE;NET40</DefineConstants>";

                if (Directory.Exists(MyPath + @"..\..\DBreeze\bin\Release"))
                    Directory.Delete(MyPath + @"..\..\DBreeze\bin\Release", true);
                tpr = prj;
                Console.WriteLine("Creating .NET4.7");
                //tpr = tpr.Replace(rpl[0], rpl[1]);    //Debug on Release
                //tpr = tpr.Replace(rpl[19], rpl[18]); //4.7.2 on 4.7
                //tpr = tpr.Replace(rpl[21], rpl[5]); //NET472 on //NET40
                tpr = tpr.ReplaceMultiple(new Dictionary<string, string> { { prjDEBUG, prjRELEASE }, { baseFramework, currentFramework }, { baseDefineConstants, currentDefineConstants } });

                File.WriteAllText(MyPath + @"..\..\DBreeze\DBreezeTMP.csproj", tpr);
                Compile(msbldpath, "DBreeze");

                noerror = File.Exists(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.dll");
                Console.WriteLine("done " + noerror);
                if (!noerror)
                    return;
                File.Copy(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.dll", MyPath + @"NET47\DBreeze.dll", true);
                File.Copy(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.XML", MyPath + @"NET47\DBreeze.XML", true);


                //.NET Framework 4.72
                currentFramework = "<TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>";
                currentDefineConstants = "<DefineConstants>TRACE;NET40;NET472</DefineConstants>";

                if (Directory.Exists(MyPath + @"..\..\DBreeze\bin\Release"))
                    Directory.Delete(MyPath + @"..\..\DBreeze\bin\Release", true);
                tpr = prj;
                Console.WriteLine("Creating .NET4.7.2");
                //tpr = tpr.Replace(rpl[0], rpl[1]);    //Debug on Release
                //tpr = tpr.Replace(rpl[2], rpl[19]); //4.5 on 4.7.2            
                //tpr = tpr.Replace(rpl[21], rpl[5]); //NET472 on //NET472
                tpr = tpr.ReplaceMultiple(new Dictionary<string, string> { { prjDEBUG, prjRELEASE }, { baseFramework, currentFramework }, { baseDefineConstants, currentDefineConstants } });

                File.WriteAllText(MyPath + @"..\..\DBreeze\DBreezeTMP.csproj", tpr);
                Compile(msbldpath, "DBreeze");

                noerror = File.Exists(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.dll");
                Console.WriteLine("done " + noerror);
                if (!noerror)
                    return;
                File.Copy(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.dll", MyPath + @"NET472\DBreeze.dll", true);
                File.Copy(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.XML", MyPath + @"NET472\DBreeze.XML", true);


                ////.NET Framework Xamarin
                ////                      (removing, it must support one of .NET Standard versions https://github.com/dotnet/standard/blob/master/docs/versions.md)
                //if (Directory.Exists(MyPath + @"..\..\DBreeze\bin\Release"))
                //    Directory.Delete(MyPath + @"..\..\DBreeze\bin\Release", true);
                //tpr = prj;
                //Console.WriteLine("Creating Xamarin");
                //tpr = tpr.Replace(rpl[0], rpl[1]);    //Debug on Release            
                //tpr = tpr.Replace(rpl[8], ""); //System.Web.Extensions
                //tpr = tpr.Replace(rpl[9], ""); //DbMJSON
                //tpr = tpr.Replace(rpl[10], ""); //MJsonSerializator

                //File.WriteAllText(MyPath + @"..\..\DBreeze\DBreezeTMP.csproj", tpr);
                //Compile(msbldpath, "DBreeze");

                //noerror = File.Exists(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.dll");
                //Console.WriteLine("done " + noerror);
                //if (!noerror)
                //    return;
                //File.Copy(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.dll", MyPath + @"XAMARIN\DBreeze.dll", true);
                //File.Copy(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.XML", MyPath + @"XAMARIN\DBreeze.XML", true);


                ////.NET 4.5
                //currentFramework = "<TargetFrameworkVersion>v4.5</TargetFrameworkVersion>";
                //currentDefineConstants = "<DefineConstants>TRACE;NET40</DefineConstants>";

                //if (Directory.Exists(MyPath + @"..\..\DBreeze\bin\Release"))
                //    Directory.Delete(MyPath + @"..\..\DBreeze\bin\Release", true);
                //tpr = prj;
                //Console.WriteLine("Creating .NET4.5");
                ////tpr = tpr.Replace(rpl[0], rpl[1]);    //Debug on Release
                ////tpr = tpr.Replace(rpl[19], rpl[2]); //4.7.2 on 4.5
                ////tpr = tpr.Replace(rpl[21], rpl[5]); //NET472 on //NET40
                //tpr = tpr.ReplaceMultiple(new Dictionary<string, string> { { prjDEBUG, prjRELEASE }, { baseFramework, currentFramework }, { baseDefineConstants, currentDefineConstants } });

                //File.WriteAllText(MyPath + @"..\..\DBreeze\DBreezeTMP.csproj", tpr);
                //Compile(msbldpath, "DBreeze");

                //noerror = File.Exists(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.dll");
                //Console.WriteLine("done " + noerror);
                //if (!noerror)
                //    return;
                //File.Copy(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.dll", MyPath + @"NET45\DBreeze.dll", true);
                //File.Copy(MyPath + @"..\..\DBreeze\bin\Release\DBreeze.XML", MyPath + @"NET45\DBreeze.XML", true);
                ////--------------------------------------------------------

                //Removing DBreeze TMP project
                File.Delete(MyPath + @"..\..\DBreeze\DBreezeTMP.csproj");

                //----------------------------------------- EOF DBreeze (Framework) project --------------------------------------








                //-----------------------------------------  DBreeze.Portable project --------------------------------------

                ////.NET Portable            
                //if (Directory.Exists(MyPath + @"..\..\NETPortable\bin\Release"))
                //    Directory.Delete(MyPath + @"..\..\NETPortable\bin\Release", true);
                ////msbldpath = MyPath + "run_msbuild_port.bat";
                //prj = File.ReadAllText(MyPath + @"..\..\NETPortable\DBreeze.Portable.csproj");
                //tpr = prj;
                //Console.WriteLine("Creating .NETPortable");
                //tpr = tpr.Replace(rpl[0], rpl[1]);    //Debug on Release           

                //File.WriteAllText(MyPath + @"..\..\NETPortable\DBreezeTMP.csproj", tpr);
                //Compile(msbldpath, "NETPortable");

                //noerror = File.Exists(MyPath + @"..\..\NETPortable\bin\Release\DBreeze.dll");
                //Console.WriteLine("done " + noerror);
                //if (!noerror)
                //    return;
                //File.Copy(MyPath + @"..\..\NETPortable\bin\Release\DBreeze.dll", MyPath + @"PORTABLE\DBreeze.dll", true);
                //File.Copy(MyPath + @"..\..\NETPortable\bin\Release\DBreeze.XML", MyPath + @"PORTABLE\DBreeze.XML", true);

                //File.Delete(MyPath + @"..\..\NETPortable\DBreezeTMP.csproj");

                ////----------------------------------------- EOF DBreeze.Portable project --------------------------------------




                //----------------------------------------- DBreeze.Net5 (Actually base is .Net6) project --------------------------------------
                //Change base when necessary
                baseFramework = "<TargetFramework>net6.0</TargetFramework>";
                baseDefineConstants = "<DefineConstants>TRACE;RELEASE;NETCOREAPP1_0;NET40;NETCOREAPP2_0;NET50;KNNSearch</DefineConstants>";


                //.NET6 (default)   (based on .NETC5 project)
                currentFramework = "<TargetFramework>net6.0</TargetFramework>";
                currentDefineConstants = "<DefineConstants>TRACE;RELEASE;NETCOREAPP1_0;NET40;NETCOREAPP2_0;NET50;KNNSearch</DefineConstants>";

                if (Directory.Exists(MyPath + @"..\..\DBreeze.Net5\bin\Release\net6.0"))
                    Directory.Delete(MyPath + @"..\..\DBreeze.Net5\bin\Release\net6.0", true);
                //msbldpath = MyPath + "run_msbuild_uwp.bat";
                prj = File.ReadAllText(MyPath + @"..\..\DBreeze.Net5\DBreeze.Net5.csproj");
                tpr = prj;
                Console.WriteLine("Creating Net6.0");
                //tpr = tpr.Replace(rpl[0], rpl[1]);    //Debug on Release
                tpr = tpr.ReplaceMultiple(new Dictionary<string, string> { { baseFramework, currentFramework }, { baseDefineConstants, currentDefineConstants } });
                //tpr = tpr.Replace("<TargetFramework>net5.0</TargetFramework>", "<TargetFramework>net5.0</TargetFramework>");
                //tpr = tpr.Replace("<DefineConstants>TRACE;RELEASE;NETCOREAPP1_0;NET40;NETPORTABLE;NETCOREAPP2_0;NET50;</DefineConstants>", "<DefineConstants>TRACE;RELEASE;NETCOREAPP1_0;NET40;NETPORTABLE;NETCOREAPP2_0;NET50;</DefineConstants>");
                // tpr = tpr.Replace(rpl[22], rpl[23]); //<DefineConstants>RELEASE;NETCOREAPP1_0;NET40;NETPORTABLE</DefineConstants>   ---->   <DefineConstants>RELEASE;NETCOREAPP1_0;NET40;NETPORTABLE;NETCOREAPP2_0</DefineConstants>

                File.WriteAllText(MyPath + @"..\..\DBreeze.Net5\DBreezeTMP.csproj", tpr);
                Compile(msbldpath, "DBreeze.Net5");

                noerror = File.Exists(MyPath + @"..\..\DBreeze.Net5\bin\Release\net6.0\DBreeze.dll");
                Console.WriteLine("done " + noerror);
                if (!noerror)
                    return;//bin\Release\net5.0\DBreeze.xml
                File.Copy(MyPath + @"..\..\DBreeze.Net5\bin\Release\net6.0\DBreeze.dll", MyPath + @"NET6_0\DBreeze.dll", true);
                File.Copy(MyPath + @"..\..\DBreeze.Net5\bin\Release\net6.0\DBreeze.xml", MyPath + @"NET6_0\DBreeze.XML", true);

                File.Delete(MyPath + @"..\..\DBreeze.Net5\DBreezeTMP.csproj");

                //----------------------------------------- EOF DBreeze.Net5 project --------------------------------------




                //----------------------------------------- DBreeze.NetCoreApp project --------------------------------------
                //Change base when necessary               
                baseFramework = "<TargetFramework>netcoreapp3.1</TargetFramework>";
                baseDefineConstants = "<DefineConstants>TRACE;NETCOREAPP1_0;NET40;NETPORTABLE;NETCOREAPP2_0;KNNSearch;RELEASE;NETCOREAPP;NETCOREAPP3_1;</DefineConstants>";

                //TRACE;NETCOREAPP1_0;NET40;NETPORTABLE;NETCOREAPP2_0;KNNSearch;RELEASE;NETCOREAPP;NETCOREAPP3_1;

                //.NET Core App 1.0 (default)    (based on .NETCoreApp project)
                currentFramework = "<TargetFramework>netcoreapp1.0</TargetFramework>";
                currentDefineConstants = "<DefineConstants>TRACE;RELEASE;NETCOREAPP1_0;NET40;NETPORTABLE;</DefineConstants>";

                if (Directory.Exists(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp1.0"))
                    Directory.Delete(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp1.0", true);
                //msbldpath = MyPath + "run_msbuild_uwp.bat";
                prj = File.ReadAllText(MyPath + @"..\..\DBreeze.NetCoreApp\DBreeze.NetCoreApp.csproj");
                tpr = prj;
                Console.WriteLine("Creating netcoreapp1.0");
                //tpr = tpr.Replace(rpl[14], rpl[13]);    //netcoreapp1.1 on netcoreapp1.0           
                tpr = tpr.ReplaceMultiple(new Dictionary<string, string> { { baseFramework, currentFramework }, { baseDefineConstants, currentDefineConstants } });

                File.WriteAllText(MyPath + @"..\..\DBreeze.NetCoreApp\DBreezeTMP.csproj", tpr);
                Compile(msbldpath, "DBreeze.NetCoreApp");

                noerror = File.Exists(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp1.1\DBreeze.dll");
                Console.WriteLine("done " + noerror);
                if (!noerror)
                    return;
                File.Copy(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp1.0\DBreeze.dll", MyPath + @"NETCOREAPP1_0\DBreeze.dll", true);
                File.Copy(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp1.0\DBreeze.XML", MyPath + @"NETCOREAPP1_0\DBreeze.XML", true);

                File.Delete(MyPath + @"..\..\DBreeze.NetCoreApp\DBreezeTMP.csproj");


                //.NET Core App 1.1   (based on .NETCoreApp project)
                currentFramework = "<TargetFramework>netcoreapp1.1</TargetFramework>";
                currentDefineConstants = "<DefineConstants>TRACE;RELEASE;NETCOREAPP1_0;NET40;NETPORTABLE;</DefineConstants>";

                if (Directory.Exists(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp1.1"))
                    Directory.Delete(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp1.1", true);
                //msbldpath = MyPath + "run_msbuild_uwp.bat";
                prj = File.ReadAllText(MyPath + @"..\..\DBreeze.NetCoreApp\DBreeze.NetCoreApp.csproj");
                tpr = prj;
                Console.WriteLine("Creating netcoreapp1.1");
                //tpr = tpr.Replace(rpl[0], rpl[1]);    //Debug on Release       
                //tpr = tpr.Replace(rpl[13], rpl[14]);
                tpr = tpr.ReplaceMultiple(new Dictionary<string, string> { { baseFramework, currentFramework }, { baseDefineConstants, currentDefineConstants } });

                File.WriteAllText(MyPath + @"..\..\DBreeze.NetCoreApp\DBreezeTMP.csproj", tpr);
                Compile(msbldpath, "DBreeze.NetCoreApp");

                noerror = File.Exists(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp1.1\DBreeze.dll");
                Console.WriteLine("done " + noerror);
                if (!noerror)
                    return;
                File.Copy(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp1.1\DBreeze.dll", MyPath + @"NETCOREAPP1_1\DBreeze.dll", true);
                File.Copy(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp1.1\DBreeze.XML", MyPath + @"NETCOREAPP1_1\DBreeze.XML", true);

                File.Delete(MyPath + @"..\..\DBreeze.NetCoreApp\DBreezeTMP.csproj");


                //.NET Core App 2.0   (based on .NETCoreApp project)
                currentFramework = "<TargetFramework>netcoreapp2.0</TargetFramework>";
                currentDefineConstants = "<DefineConstants>TRACE;RELEASE;NETCOREAPP1_0;NET40;NETPORTABLE;NETCOREAPP2_0;</DefineConstants>";

                if (Directory.Exists(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp2.0"))
                    Directory.Delete(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp2.0", true);
                //msbldpath = MyPath + "run_msbuild_uwp.bat";
                prj = File.ReadAllText(MyPath + @"..\..\DBreeze.NetCoreApp\DBreeze.NetCoreApp.csproj");
                tpr = prj;
                Console.WriteLine("Creating netcoreapp2.0");
                //tpr = tpr.Replace(rpl[0], rpl[1]);    //Debug on Release       
                //tpr = tpr.Replace(rpl[13], rpl[15]);
                //tpr = tpr.Replace("<DefineConstants>TRACE;RELEASE;NETCOREAPP1_0;NET40;NETPORTABLE</DefineConstants>", "<DefineConstants>TRACE;RELEASE;NETCOREAPP1_0;NET40;NETPORTABLE;NETCOREAPP2_0</DefineConstants>");
                // tpr = tpr.Replace(rpl[22], rpl[23]); //<DefineConstants>RELEASE;NETCOREAPP1_0;NET40;NETPORTABLE</DefineConstants>   ---->   <DefineConstants>RELEASE;NETCOREAPP1_0;NET40;NETPORTABLE;NETCOREAPP2_0</DefineConstants>
                tpr = tpr.ReplaceMultiple(new Dictionary<string, string> { { baseFramework, currentFramework }, { baseDefineConstants, currentDefineConstants } });

                File.WriteAllText(MyPath + @"..\..\DBreeze.NetCoreApp\DBreezeTMP.csproj", tpr);
                Compile(msbldpath, "DBreeze.NetCoreApp");

                noerror = File.Exists(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp2.0\DBreeze.dll");
                Console.WriteLine("done " + noerror);
                if (!noerror)
                    return;
                File.Copy(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp1.1\DBreeze.dll", MyPath + @"NETCOREAPP2_0\DBreeze.dll", true);
                File.Copy(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp1.1\DBreeze.XML", MyPath + @"NETCOREAPP2_0\DBreeze.XML", true);

                File.Delete(MyPath + @"..\..\DBreeze.NetCoreApp\DBreezeTMP.csproj");




                //.NET Core App 3.1   (based on .NETCoreApp project)
                currentFramework = "<TargetFramework>netcoreapp3.1</TargetFramework>";
                currentDefineConstants = "<DefineConstants>TRACE;NETCOREAPP1_0;NET40;NETPORTABLE;NETCOREAPP2_0;KNNSearch;RELEASE;NETCOREAPP;NETCOREAPP3_1;</DefineConstants>";

                if (Directory.Exists(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp3.1"))
                    Directory.Delete(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp3.1", true);
                //msbldpath = MyPath + "run_msbuild_uwp.bat";
                prj = File.ReadAllText(MyPath + @"..\..\DBreeze.NetCoreApp\DBreeze.NetCoreApp.csproj");
                tpr = prj;
                Console.WriteLine("Creating netcoreapp3.1");
                //tpr = tpr.Replace(rpl[0], rpl[1]);    //Debug on Release       
                //tpr = tpr.Replace(rpl[13], rpl[15]);
                //tpr = tpr.Replace("<DefineConstants>TRACE;RELEASE;NETCOREAPP1_0;NET40;NETPORTABLE</DefineConstants>", "<DefineConstants>TRACE;RELEASE;NETCOREAPP1_0;NET40;NETPORTABLE;NETCOREAPP2_0</DefineConstants>");
                // tpr = tpr.Replace(rpl[22], rpl[23]); //<DefineConstants>RELEASE;NETCOREAPP1_0;NET40;NETPORTABLE</DefineConstants>   ---->   <DefineConstants>RELEASE;NETCOREAPP1_0;NET40;NETPORTABLE;NETCOREAPP2_0</DefineConstants>
                tpr = tpr.ReplaceMultiple(new Dictionary<string, string> { { baseFramework, currentFramework }, { baseDefineConstants, currentDefineConstants } });

                File.WriteAllText(MyPath + @"..\..\DBreeze.NetCoreApp\DBreezeTMP.csproj", tpr);
                Compile(msbldpath, "DBreeze.NetCoreApp");

                noerror = File.Exists(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp3.1\DBreeze.dll");
                Console.WriteLine("done " + noerror);
                if (!noerror)
                    return;
                File.Copy(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp3.1\DBreeze.dll", MyPath + @"NETCOREAPP3_1\DBreeze.dll", true);
                File.Copy(MyPath + @"..\..\DBreeze.NetCoreApp\bin\Release\netcoreapp3.1\DBreeze.XML", MyPath + @"NETCOREAPP3_1\DBreeze.XML", true);

                File.Delete(MyPath + @"..\..\DBreeze.NetCoreApp\DBreezeTMP.csproj");


                //----------------------------------------- EOF DBreeze.NetCoreApp project --------------------------------------



                //----------------------------------------- DBreeze.NetStandard project --------------------------------------
                //https://devblogs.microsoft.com/dotnet/the-future-of-net-standard/
                baseFramework = "<TargetFramework>netstandard1.6</TargetFramework>";
                baseDefineConstants = "<DefineConstants>TRACE;RELEASE;NETSTANDARD1_6;NET40;NETPORTABLE</DefineConstants>";


                //.NET STANDARD 1.6  (default)
                currentFramework = "<TargetFramework>netstandard1.6</TargetFramework>";
                currentDefineConstants = "<DefineConstants>TRACE;RELEASE;NETSTANDARD1_6;NET40;NETPORTABLE</DefineConstants>";

                if (Directory.Exists(MyPath + @"..\..\DBreeze.NetStandard\bin\Release\netstandard1.6"))
                    Directory.Delete(MyPath + @"..\..\DBreeze.NetStandard\bin\Release\netstandard1.6", true);
                //msbldpath = MyPath + "run_msbuild_uwp.bat";
                prj = File.ReadAllText(MyPath + @"..\..\DBreeze.NetStandard\DBreeze.NetStandard.csproj");
                tpr = prj;
                Console.WriteLine("Creating .NET Standard 1.6");
                //tpr = tpr.Replace(rpl[0], rpl[1]);    //Debug on Release           
                tpr = tpr.ReplaceMultiple(new Dictionary<string, string> { { baseFramework, currentFramework }, { baseDefineConstants, currentDefineConstants } });

                File.WriteAllText(MyPath + @"..\..\DBreeze.NetStandard\DBreezeTMP.csproj", tpr);
                Compile(msbldpath, "DBreeze.NetStandard");

                noerror = File.Exists(MyPath + @"..\..\DBreeze.NetStandard\bin\Release\netstandard1.6\DBreeze.dll");
                Console.WriteLine("done " + noerror);
                if (!noerror)
                    return;
                File.Copy(MyPath + @"..\..\DBreeze.NetStandard\bin\Release\netstandard1.6\DBreeze.dll", MyPath + @"NETSTANDARD16\DBreeze.dll", true);
                File.Copy(MyPath + @"..\..\DBreeze.NetStandard\bin\Release\netstandard1.6\DBreeze.XML", MyPath + @"NETSTANDARD16\DBreeze.XML", true);

                File.Delete(MyPath + @"..\..\DBreeze.NetStandard\DBreezeTMP.csproj");



                //.NET STANDARD 2.0
                currentFramework = "<TargetFramework>netstandard2.0</TargetFramework>";
                currentDefineConstants = "<DefineConstants>TRACE;RELEASE;NETSTANDARD1_6;NET40;NETPORTABLE</DefineConstants>";

                if (Directory.Exists(MyPath + @"..\..\DBreeze.NetStandard\bin\Release\netstandard2.0"))
                    Directory.Delete(MyPath + @"..\..\DBreeze.NetStandard\bin\Release\netstandard2.0", true);
                //msbldpath = MyPath + "run_msbuild_uwp.bat";
                prj = File.ReadAllText(MyPath + @"..\..\DBreeze.NetStandard\DBreeze.NetStandard.csproj");
                tpr = prj;
                Console.WriteLine("Creating .NET Standard 2.0");
                //tpr = tpr.Replace(rpl[16], rpl[17]);    //Debug on Release        //<TargetFramework>netstandard1.6</TargetFramework> 16 , <TargetFramework>netstandard2.0</TargetFramework> 17    
                tpr = tpr.ReplaceMultiple(new Dictionary<string, string> { { baseFramework, currentFramework }, { baseDefineConstants, currentDefineConstants } });

                File.WriteAllText(MyPath + @"..\..\DBreeze.NetStandard\DBreezeTMP.csproj", tpr);
                Compile(msbldpath, "DBreeze.NetStandard");

                noerror = File.Exists(MyPath + @"..\..\DBreeze.NetStandard\bin\Release\netstandard2.0\DBreeze.dll");
                Console.WriteLine("done " + noerror);
                if (!noerror)
                    return;
                File.Copy(MyPath + @"..\..\DBreeze.NetStandard\bin\Release\netstandard2.0\DBreeze.dll", MyPath + @"NETSTANDARD2_0\DBreeze.dll", true);
                File.Copy(MyPath + @"..\..\DBreeze.NetStandard\bin\Release\netstandard2.0\DBreeze.XML", MyPath + @"NETSTANDARD2_0\DBreeze.XML", true);

                File.Delete(MyPath + @"..\..\DBreeze.NetStandard\DBreezeTMP.csproj");


                //.NET STANDARD 2.1
                currentFramework = "<TargetFramework>netstandard2.1</TargetFramework>";
                currentDefineConstants = "<DefineConstants>TRACE;RELEASE;NETSTANDARD1_6;NET40;NETPORTABLE;NETSTANDARD2_1;KNNSearch</DefineConstants>";

                if (Directory.Exists(MyPath + @"..\..\DBreeze.NetStandard\bin\Release\netstandard2.0"))
                    Directory.Delete(MyPath + @"..\..\DBreeze.NetStandard\bin\Release\netstandard2.0", true);
                //msbldpath = MyPath + "run_msbuild_uwp.bat";
                prj = File.ReadAllText(MyPath + @"..\..\DBreeze.NetStandard\DBreeze.NetStandard.csproj");
                tpr = prj;
                Console.WriteLine("Creating .NET Standard 2.1");
                //tpr = tpr.Replace(rpl[16], rpl[20]);    //<TargetFramework>netstandard1.6</TargetFramework> 16 , <TargetFramework>netstandard2.1</TargetFramework> 20 
                //tpr = tpr.Replace("<DefineConstants>TRACE;RELEASE;NETSTANDARD1_6;NET40;NETPORTABLE</DefineConstants>",
                //    "<DefineConstants>TRACE;RELEASE;NETSTANDARD1_6;NET40;NETPORTABLE;NETSTANDARD2_1;</DefineConstants>");

                tpr = tpr.ReplaceMultiple(new Dictionary<string, string> { { baseFramework, currentFramework }, { baseDefineConstants, currentDefineConstants } });

                File.WriteAllText(MyPath + @"..\..\DBreeze.NetStandard\DBreezeTMP.csproj", tpr);
                Compile(msbldpath, "DBreeze.NetStandard");

                noerror = File.Exists(MyPath + @"..\..\DBreeze.NetStandard\bin\Release\netstandard2.1\DBreeze.dll");
                Console.WriteLine("done " + noerror);
                if (!noerror)
                    return;
                File.Copy(MyPath + @"..\..\DBreeze.NetStandard\bin\Release\netstandard2.1\DBreeze.dll", MyPath + @"NETSTANDARD2_1\DBreeze.dll", true);
                File.Copy(MyPath + @"..\..\DBreeze.NetStandard\bin\Release\netstandard2.1\DBreeze.XML", MyPath + @"NETSTANDARD2_1\DBreeze.XML", true);

                File.Delete(MyPath + @"..\..\DBreeze.NetStandard\DBreezeTMP.csproj");

                //----------------------------------------- EOF DBreeze.NetStandard project --------------------------------------

            }//eof skipRecompile (for testing and debugging packaging system)


            Console.WriteLine("Packing DLLs and ULTIMATE ZIP");
            //Packing DLLs into zips
            var ultf = di.GetFiles("*ULTIMATE*").First();

            if (ultf == null)
            {
                Console.WriteLine("Not found ultimate archive like DBreeze_1_077_2016_0829_ULTIMATE.zip. ERROR. Going out");
                return;
            }

            ZipArchive ultimate_archive = ZipFile.Open(ultf.FullName, ZipArchiveMode.Update);
            //Clearing old entries
            foreach (var ultentrin in ultimate_archive.Entries.ToList())
            {
                ultentrin.Delete();
            }

            foreach (var d in di.GetDirectories())
            {
                Debug.WriteLine(d.FullName);


                foreach (var f in d.GetFiles("DBreeze.dll"))
                {
                    var vf = FileVersionInfo.GetVersionInfo(f.FullName);
                    fileVersion = FileVersionInfo.GetVersionInfo(f.FullName).FileVersion;
                    productVersion = FileVersionInfo.GetVersionInfo(f.FullName).ProductVersion;
                    break;
                }

                //Initial zip files must be copied manually, because we must give them name in the end, like _NET472
                foreach (var f in d.GetFiles("DBreeze_*.zip"))
                {
                    using (ZipArchive archive = ZipFile.Open(f.FullName, ZipArchiveMode.Update))
                    {
                        archive.GetEntry("DBreeze.dll").Delete();
                        archive.GetEntry("DBreeze.XML").Delete();
                        archive.CreateEntryFromFile(f.Directory + @"\DBreeze.dll", "DBreeze.dll");
                        archive.CreateEntryFromFile(f.Directory + @"\DBreeze.XML", "DBreeze.XML");
                    }
                    File.Move(f.FullName, f.Directory + @"\" + "DBreeze" + "_" + productVersion.Replace(".", "_") + "_" + f.Name.Substring(24));

                    //Adding to Ultimate archive new archives
                    ultimate_archive.CreateEntryFromFile(f.Directory + @"\" + "DBreeze" + "_" + productVersion.Replace(".", "_") + "_" + f.Name.Substring(24)
                        , "DBreeze" + "_" + productVersion.Replace(".", "_") + "_" + f.Name.Substring(24)
                        , CompressionLevel.Optimal);

                    break;
                }

            }

            //Copying documentation
            ultimate_archive.CreateEntryFromFile(di.FullName + @"..\..\Documentation\_DBreeze.Documentation.actual.pdf"
                        , "_DBreeze.Documentation.actual.pdf"
                        , CompressionLevel.Optimal);

            ultimate_archive.Dispose();

           


            //Renaming ultimate Archive
            File.Move(ultf.FullName, di.FullName + "DBreeze" + "_" + productVersion.Replace(".", "_") + "_ULTIMATE.zip");

            Console.WriteLine("Packing Nuget");

            var localRepo = PackageRepositoryFactory.Default.CreateRepository(MyPath + @"..\Nuget\Actual");
            //var pck = localRepo.FindPackage("DBreeze", new SemanticVersion("1.77.0.0"));
            var pck = localRepo.FindPackage("DBreeze");
            string[] fileVersion1 = null;

           

            using (ZipArchive archive = ZipFile.Open(MyPath + @"..\Nuget\Actual\DBreeze.actual.nupkg", ZipArchiveMode.Update))
            {

                var ent_nuspec = archive.GetEntry("DBreeze.nuspec");
                ent_nuspec.ExtractToFile(MyPath + @"..\Nuget\Actual\DBreeze.nuspec", true);
                ent_nuspec.Delete();
                var ent_txt = File.ReadAllText(MyPath + @"..\Nuget\Actual\DBreeze.nuspec");

                fileVersion = FileVersionInfo.GetVersionInfo(MyPath + @"NET45\DBreeze.dll").FileVersion;
                fileVersion1 = fileVersion.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries);

                string verAsString = fileVersion1[0] + "." + fileVersion1[1] + "." + fileVersion1[2] + fileVersion1[3];

                ent_txt = ent_txt.Replace(pck.Title, pck.Title.Substring(0, pck.Title.Length - verAsString.Length) + verAsString);

                if (fileVersion1[1][0] == '0')
                    fileVersion1[1] = fileVersion1[1].Substring(1);
                //productVersion = FileVersionInfo.GetVersionInfo(MyPath + @"NET45\DBreeze.dll").ProductVersion;
                ent_txt = ent_txt.Replace("<version>" + pck.Version + "</version>", "<version>" + fileVersion1[0] + "." + fileVersion1[1] + ".0" + "</version>");
                File.WriteAllText(MyPath + @"..\Nuget\Actual\DBreeze.nuspec", ent_txt);
                archive.CreateEntryFromFile(MyPath + @"..\Nuget\Actual\DBreeze.nuspec", "DBreeze.nuspec", CompressionLevel.Optimal);

                File.Delete(MyPath + @"..\Nuget\Actual\DBreeze.nuspec");

                ////REMARKED CHECKED, XAMARIN MOVING TO .NET STANDARD
                //archive.GetEntry("lib/MonoAndroid/DBreeze.dll").Delete();
                //archive.GetEntry("lib/MonoAndroid/DBreeze.XML").Delete();
                //archive.CreateEntryFromFile(MyPath + "XAMARIN" + @"\DBreeze.dll", "lib/MonoAndroid/DBreeze.dll", CompressionLevel.Optimal);
                //archive.CreateEntryFromFile(MyPath + "XAMARIN" + @"\DBreeze.xml", "lib/MonoAndroid/DBreeze.XML", CompressionLevel.Optimal);

                archive.GetEntry("lib/net35/DBreeze.dll").Delete();
                archive.GetEntry("lib/net35/DBreeze.XML").Delete();
                archive.CreateEntryFromFile(MyPath + "NET35" + @"\DBreeze.dll", "lib/net35/DBreeze.dll", CompressionLevel.Optimal);
                archive.CreateEntryFromFile(MyPath + "NET35" + @"\DBreeze.xml", "lib/net35/DBreeze.XML", CompressionLevel.Optimal);

                archive.GetEntry("lib/net40/DBreeze.dll").Delete();
                archive.GetEntry("lib/net40/DBreeze.XML").Delete();
                archive.CreateEntryFromFile(MyPath + "NET40" + @"\DBreeze.dll", "lib/net40/DBreeze.dll", CompressionLevel.Optimal);
                archive.CreateEntryFromFile(MyPath + "NET40" + @"\DBreeze.xml", "lib/net40/DBreeze.XML", CompressionLevel.Optimal);

                archive.GetEntry("lib/net461/DBreeze.dll").Delete();
                archive.GetEntry("lib/net461/DBreeze.XML").Delete();
                archive.CreateEntryFromFile(MyPath + "NET461" + @"\DBreeze.dll", "lib/net461/DBreeze.dll", CompressionLevel.Optimal);
                archive.CreateEntryFromFile(MyPath + "NET461" + @"\DBreeze.xml", "lib/net461/DBreeze.XML", CompressionLevel.Optimal);

                archive.GetEntry("lib/net462/DBreeze.dll").Delete();
                archive.GetEntry("lib/net462/DBreeze.XML").Delete();
                archive.CreateEntryFromFile(MyPath + "NET462" + @"\DBreeze.dll", "lib/net462/DBreeze.dll", CompressionLevel.Optimal);
                archive.CreateEntryFromFile(MyPath + "NET462" + @"\DBreeze.xml", "lib/net462/DBreeze.XML", CompressionLevel.Optimal);

                archive.GetEntry("lib/net47/DBreeze.dll").Delete();
                archive.GetEntry("lib/net47/DBreeze.XML").Delete();
                archive.CreateEntryFromFile(MyPath + "NET47" + @"\DBreeze.dll", "lib/net47/DBreeze.dll", CompressionLevel.Optimal);
                archive.CreateEntryFromFile(MyPath + "NET47" + @"\DBreeze.xml", "lib/net47/DBreeze.XML", CompressionLevel.Optimal);

                archive.GetEntry("lib/net472/DBreeze.dll").Delete();
                archive.GetEntry("lib/net472/DBreeze.XML").Delete();
                archive.CreateEntryFromFile(MyPath + "NET472" + @"\DBreeze.dll", "lib/net472/DBreeze.dll", CompressionLevel.Optimal);
                archive.CreateEntryFromFile(MyPath + "NET472" + @"\DBreeze.xml", "lib/net472/DBreeze.XML", CompressionLevel.Optimal);

                archive.GetEntry("lib/net45/DBreeze.dll").Delete();
                archive.GetEntry("lib/net45/DBreeze.XML").Delete();
                archive.CreateEntryFromFile(MyPath + "NET45" + @"\DBreeze.dll", "lib/net45/DBreeze.dll", CompressionLevel.Optimal);
                archive.CreateEntryFromFile(MyPath + "NET45" + @"\DBreeze.xml", "lib/net45/DBreeze.XML", CompressionLevel.Optimal);

                //archive.GetEntry("lib/netcore451/DBreeze.dll").Delete();
                //archive.GetEntry("lib/netcore451/DBreeze.XML").Delete();
                //archive.CreateEntryFromFile(MyPath + "UWP" + @"\DBreeze.dll", "lib/netcore451/DBreeze.dll", CompressionLevel.Optimal);
                //archive.CreateEntryFromFile(MyPath + "UWP" + @"\DBreeze.xml", "lib/netcore451/DBreeze.XML", CompressionLevel.Optimal);

                archive.GetEntry("lib/netcoreapp1.0/DBreeze.dll").Delete();
                archive.GetEntry("lib/netcoreapp1.0/DBreeze.XML").Delete();
                archive.CreateEntryFromFile(MyPath + "NETCOREAPP1_0" + @"\DBreeze.dll", "lib/netcoreapp1.0/DBreeze.dll", CompressionLevel.Optimal);
                archive.CreateEntryFromFile(MyPath + "NETCOREAPP1_0" + @"\DBreeze.xml", "lib/netcoreapp1.0/DBreeze.XML", CompressionLevel.Optimal);
                //archive.CreateEntryFromFile(MyPath + "UWP" + @"\DBreeze.dll", "lib/netcoreapp1.0/DBreeze.dll", CompressionLevel.Optimal);
                //archive.CreateEntryFromFile(MyPath + "UWP" + @"\DBreeze.xml", "lib/netcoreapp1.0/DBreeze.XML", CompressionLevel.Optimal);

                archive.GetEntry("lib/netcoreapp1.1/DBreeze.dll").Delete();
                archive.GetEntry("lib/netcoreapp1.1/DBreeze.XML").Delete();
                archive.CreateEntryFromFile(MyPath + "NETCOREAPP1_1" + @"\DBreeze.dll", "lib/netcoreapp1.1/DBreeze.dll", CompressionLevel.Optimal);
                archive.CreateEntryFromFile(MyPath + "NETCOREAPP1_1" + @"\DBreeze.xml", "lib/netcoreapp1.1/DBreeze.XML", CompressionLevel.Optimal);

                archive.GetEntry("lib/netcoreapp2.0/DBreeze.dll").Delete();
                archive.GetEntry("lib/netcoreapp2.0/DBreeze.XML").Delete();
                archive.CreateEntryFromFile(MyPath + "NETCOREAPP2_0" + @"\DBreeze.dll", "lib/netcoreapp2.0/DBreeze.dll", CompressionLevel.Optimal);
                archive.CreateEntryFromFile(MyPath + "NETCOREAPP2_0" + @"\DBreeze.xml", "lib/netcoreapp2.0/DBreeze.XML", CompressionLevel.Optimal);
                
                CreateLibEntry(archive, "lib/net6.0", MyPath + "NET6_0"); //<--------------------------------------------------------------------------------- USE THAT FOR NEW ENTRIES
                //archive.GetEntry("lib/net5.0/DBreeze.dll").Delete();
                //archive.GetEntry("lib/net5.0/DBreeze.XML").Delete();
                //archive.CreateEntryFromFile(MyPath + "NET5_0" + @"\DBreeze.dll", "lib/net5.0/DBreeze.dll", CompressionLevel.Optimal);
                //archive.CreateEntryFromFile(MyPath + "NET5_0" + @"\DBreeze.XML", "lib/net5.0/DBreeze.XML", CompressionLevel.Optimal);

                archive.GetEntry("lib/netstandard1.6/DBreeze.dll").Delete();
                archive.GetEntry("lib/netstandard1.6/DBreeze.XML").Delete();
                archive.CreateEntryFromFile(MyPath + "NETSTANDARD16" + @"\DBreeze.dll", "lib/netstandard1.6/DBreeze.dll", CompressionLevel.Optimal);
                archive.CreateEntryFromFile(MyPath + "NETSTANDARD16" + @"\DBreeze.xml", "lib/netstandard1.6/DBreeze.XML", CompressionLevel.Optimal);
                //archive.CreateEntryFromFile(MyPath + "UWP" + @"\DBreeze.dll", "lib/netstandard1.6/DBreeze.dll", CompressionLevel.Optimal);
                //archive.CreateEntryFromFile(MyPath + "UWP" + @"\DBreeze.dll", "lib/netstandard1.6/DBreeze.XML", CompressionLevel.Optimal);

               
                archive.GetEntry("lib/netstandard2.0/DBreeze.dll").Delete();
                archive.GetEntry("lib/netstandard2.0/DBreeze.XML").Delete();
                archive.CreateEntryFromFile(MyPath + "NETSTANDARD2_0" + @"\DBreeze.dll", "lib/netstandard2.0/DBreeze.dll", CompressionLevel.Optimal);
                archive.CreateEntryFromFile(MyPath + "NETSTANDARD2_0" + @"\DBreeze.xml", "lib/netstandard2.0/DBreeze.XML", CompressionLevel.Optimal);

                CreateLibEntry(archive, "lib/netstandard2.1", MyPath + "NETSTANDARD2_1"); //<------------------------------------------------------------------------------------ USE THAT FOR NEW ENTRIES
                //archive.GetEntry("lib/netstandard2.1/DBreeze.dll").Delete();
                //archive.GetEntry("lib/netstandard2.1/DBreeze.XML").Delete();
                //archive.CreateEntryFromFile(MyPath + "NETSTANDARD2_1" + @"\DBreeze.dll", "lib/netstandard2.1/DBreeze.dll", CompressionLevel.Optimal);
                //archive.CreateEntryFromFile(MyPath + "NETSTANDARD2_1" + @"\DBreeze.xml", "lib/netstandard2.1/DBreeze.XML", CompressionLevel.Optimal);

                archive.GetEntry("lib/portable-net45+win8+wp8+wpa81/DBreeze.dll").Delete();
                archive.GetEntry("lib/portable-net45+win8+wp8+wpa81/DBreeze.XML").Delete();
                archive.CreateEntryFromFile(MyPath + "PORTABLE" + @"\DBreeze.dll", "lib/portable-net45+win8+wp8+wpa81/DBreeze.dll", CompressionLevel.Optimal);
                archive.CreateEntryFromFile(MyPath + "PORTABLE" + @"\DBreeze.xml", "lib/portable-net45+win8+wp8+wpa81/DBreeze.XML", CompressionLevel.Optimal);


            }

            File.Copy(MyPath + @"..\Nuget\Actual\DBreeze.actual.nupkg", MyPath + $"..\\Nuget\\DBreeze.{fileVersion1[0] + "." + fileVersion1[1] + ".0"}.nupkg", true);


            Console.WriteLine("Done...");
            Console.ReadLine();
        }

        static void CreateLibEntry(ZipArchive archive, string folderInArchive, string pathOrigin)        
        {
            string wentry = String.Empty;// folderInArchive;
            ZipArchiveEntry entry = null;

            Action<string> a = (fileName) =>
            {
                wentry = folderInArchive + "/" + fileName;
                entry = archive.GetEntry(wentry);
                if (entry != null)
                    entry.Delete();
                archive.CreateEntryFromFile(pathOrigin + @"\" + fileName, wentry, CompressionLevel.Optimal);
            };

            //if(archive.Entries.Where(r=>r.FullName.StartsWith(folderInArchive)).FirstOrDefault() == null)
            //    archive.CreateEntry(wentry, CompressionLevel.Optimal);
         
            a("DBreeze.dll");
            a("DBreeze.XML");

        }

        static void Compile(string msbldpath, string folder)
        {
            ProcessStartInfo start = new ProcessStartInfo();
            // Enter in the command line arguments, everything you would enter after the executable name itself
            start.Arguments = folder;
            // Enter the executable to run, including the complete path
            start.FileName = msbldpath;
            // Do you want to show a console window?
            start.WindowStyle = ProcessWindowStyle.Normal;
            int exitCode = 0;
            

            using (Process proc = Process.Start(start))
            {                
                proc.WaitForExit();

                // Retrieve the app's exit code
                exitCode = proc.ExitCode;
                
            }

        }

        public static string AssemblyDirectory
        {
            get
            {
                string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                UriBuilder uri = new UriBuilder(codeBase);
                string path = Uri.UnescapeDataString(uri.Path);
                return Path.GetDirectoryName(path);
            }
        }


        public static object Resources1 { get; private set; }
    }
}
