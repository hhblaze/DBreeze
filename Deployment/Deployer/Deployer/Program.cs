using Microsoft.Web.XmlTransform;
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
            string msbldpathV2 = MyPath + "runV2_msbuild22.bat";

            string fileVersion = "";
            string productVersion = "";
            
            //var tr = Deployer.Resource1.templ.Split(new char[] { '\r', '\n' },StringSplitOptions.RemoveEmptyEntries);
            //List<string> rpl = new List<string>();          
            //foreach (var t in tr)
            //    rpl.Add(t);

            //bool noerror = false;
            //string tpr = "";

            //IN DBreeze project one Framework is default (like .NET5 in NetCoreApp project ot .NET4.7.2 in DBreeze project)
            //When compiling subframeworks from that, we need to change base framework and its define-constants on the compiling framework
            //string baseFramework="";
            //string baseDefineConstants = "";
            //string currentFramework = "";
            //string currentDefineConstants = "";
            //string prjDEBUG = "<Configuration Condition=\" '$(Configuration)' == '' \">Debug</Configuration>";
            //string prjRELEASE = "<Configuration Condition=\" '$(Configuration)' == '' \">Release</Configuration>";

            string prj = File.ReadAllText(MyPath + @"..\..\DBreeze\DBreeze.csproj");

            bool skipRecompile = false;            


            if (!skipRecompile)
            {
                //if (Directory.Exists(MyPath + @"..\..\DBreeze\bin\Release"))
                //    Directory.Delete(MyPath + @"..\..\DBreeze\bin\Release", true);
                //msbuild MyProject.csproj /p:TargetFramework=netcoreapp3.1 /p:DefineConstants=NETCOREAPP3_1
                int exitCode = 0;

                string targetFramework = "TargetFramework";
                string targetFrameworkVersion = "TargetFrameworkVersion";

                //NET FRAMEWORK

                exitCode = Utils.Compile(msbldpathV2, targetFrameworkVersion, @"DBreeze\DBreeze.csproj", "v3.5", "TRACE;RELEASE;" + "NET35", @"DBreeze\bin\Release", "NET35");
                if (exitCode != 0) { Console.ReadKey(); return; }
                exitCode = Utils.Compile(msbldpathV2, targetFrameworkVersion, @"DBreeze\DBreeze.csproj", "v4.0", "TRACE;RELEASE;" + "NET40;NETr40", @"DBreeze\bin\Release", "NET40");
                if (exitCode != 0) { Console.ReadKey(); return; }
                exitCode = Utils.Compile(msbldpathV2, targetFrameworkVersion, @"DBreeze\DBreeze.csproj", "v4.5", "TRACE;RELEASE;" + "NET40", @"DBreeze\bin\Release", "NET45");
                if (exitCode != 0) { Console.ReadKey(); return; }
                exitCode = Utils.Compile(msbldpathV2, targetFrameworkVersion, @"DBreeze\DBreeze.csproj", "v4.6.1", "TRACE;RELEASE;" + "NET40", @"DBreeze\bin\Release", "NET461");
                if (exitCode != 0) { Console.ReadKey(); return; }
                exitCode = Utils.Compile(msbldpathV2, targetFrameworkVersion, @"DBreeze\DBreeze.csproj", "v4.6.2", "TRACE;RELEASE;" + "NET40", @"DBreeze\bin\Release", "NET462");
                if (exitCode != 0) { Console.ReadKey(); return; }
                exitCode = Utils.Compile(msbldpathV2, targetFrameworkVersion, @"DBreeze\DBreeze.csproj", "v4.7", "TRACE;RELEASE;" + "NET40", @"DBreeze\bin\Release", "NET47");
                if (exitCode != 0) { Console.ReadKey(); return; }
                exitCode = Utils.Compile(msbldpathV2, targetFrameworkVersion, @"DBreeze\DBreeze.csproj", "v4.7.2", "TRACE;RELEASE;" + "NET40;NET472", @"DBreeze\bin\Release", "NET472");
                if (exitCode != 0) { Console.ReadKey(); return; }

                ////NET CORE
                exitCode = Utils.Compile(msbldpathV2, targetFramework, @"DBreeze.NetCoreApp\DBreeze.NetCoreApp.csproj", "netcoreapp1.0", "TRACE;RELEASE;" + "NETCOREAPP1_0;NET40;NETPORTABLE;", @"DBreeze.NetCoreApp\bin\Release\netcoreapp1.0", "NETCOREAPP1_0");
                if (exitCode != 0) { Console.ReadKey(); return; }
                exitCode = Utils.Compile(msbldpathV2, targetFramework, @"DBreeze.NetCoreApp\DBreeze.NetCoreApp.csproj", "netcoreapp1.1", "TRACE;RELEASE;" + "NETCOREAPP1_0;NET40;NETPORTABLE;", @"DBreeze.NetCoreApp\bin\Release\netcoreapp1.1", "NETCOREAPP1_1");
                if (exitCode != 0) { Console.ReadKey(); return; }
                exitCode = Utils.Compile(msbldpathV2, targetFramework, @"DBreeze.NetCoreApp\DBreeze.NetCoreApp.csproj", "netcoreapp2.0", "TRACE;RELEASE;" + "NETCOREAPP1_0;NET40;NETPORTABLE;NETCOREAPP2_0;", @"DBreeze.NetCoreApp\bin\Release\netcoreapp2.0", "NETCOREAPP2_0");
                if (exitCode != 0) { Console.ReadKey(); return; }
                exitCode = Utils.Compile(msbldpathV2, targetFramework, @"DBreeze.NetCoreApp\DBreeze.NetCoreApp.csproj", "netcoreapp3.1", "TRACE;RELEASE;" + "NETCOREAPP1_0;NET40;NETPORTABLE;NETCOREAPP2_0;NETCOREAPP;NETCOREAPP3_1;NET6FUNC;", @"DBreeze.NetCoreApp\bin\Release\netcoreapp3.1", "NETCOREAPP3_1");
                if (exitCode != 0) { Console.ReadKey(); return; }

                ////NET PORTABLE
                exitCode = Utils.Compile(msbldpathV2, targetFrameworkVersion, @"NETPortable\DBreeze.Portable.csproj", "v4.5", "TRACE;RELEASE;" + "NETCOREAPP1_0;NET40;NETPORTABLE;", @"NETPortable\bin\Release", "PORTABLE");
                if (exitCode != 0) { Console.ReadKey(); return; }

                ////.NET
                exitCode = Utils.Compile(msbldpathV2, targetFramework, @"DBreeze.Net5\DBreeze.Net5.csproj", "net6.0", "TRACE;RELEASE;" + "NETCOREAPP1_0;NET40;NETCOREAPP2_0;NET50;NET6FUNC;", @"DBreeze.Net5\bin\Release\net6.0", "NET6_0");
                if (exitCode != 0) { Console.ReadKey(); return; }

                ////.NET STANDARD
                exitCode = Utils.Compile(msbldpathV2, targetFramework, @"DBreeze.NetStandard\DBreeze.NetStandard.csproj", "netstandard2.0", "TRACE;RELEASE;" + "NETSTANDARD;NETSTANDARD1_6;NET40;NETPORTABLE;NETSTANDARD2_0", @"DBreeze.NetStandard\bin\Release\netstandard2.0", "NETSTANDARD2_0");
                if (exitCode != 0) { Console.ReadKey(); return; }
                exitCode = Utils.Compile(msbldpathV2, targetFramework, @"DBreeze.NetStandard\DBreeze.NetStandard.csproj", "netstandard1.6", "TRACE;RELEASE;" + "NETSTANDARD1_6;NET40;NETPORTABLE", @"DBreeze.NetStandard\bin\Release\netstandard1.6", "NETSTANDARD16");
                if (exitCode != 0) { Console.ReadKey(); return; }
                exitCode = Utils.Compile(msbldpathV2, targetFramework, @"DBreeze.NetStandard\DBreeze.NetStandard.csproj", "netstandard2.1", "TRACE;RELEASE;" + "NETSTANDARD1_6;NET40;NETPORTABLE;NETSTANDARD2_1;NET6FUNC;", @"DBreeze.NetStandard\bin\Release\netstandard2.1", "NETSTANDARD2_1");
                if (exitCode != 0) { Console.ReadKey(); return; }


                Utils.ConsolePrint("ALL IS DONE", ConsoleColor.Green);

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

            string nuspecProtoPath = MyPath + @"..\Nuspec\DBreezePrototype.nuspec";
            string nuspecPath = MyPath + @"..\Nuspec\DBreeze.nuspec";

            var nuspec = File.ReadAllText(nuspecProtoPath);
            nuspec = nuspec.ReplaceMultiple(new Dictionary<string, string> {
                { "{@version}", productVersion }
            });

            File.WriteAllText(nuspecPath, nuspec);

            //var localRepo = PackageRepositoryFactory.Default.CreateRepository(MyPath + @"..\Nuget\Actual");
            ////var pck = localRepo.FindPackage("DBreeze", new SemanticVersion("1.77.0.0"));
            //var pck = localRepo.FindPackage("DBreeze");
            //string[] fileVersion1 = null;

           

            //using (ZipArchive archive = ZipFile.Open(MyPath + @"..\Nuget\Actual\DBreeze.actual.nupkg", ZipArchiveMode.Update))
            //{

            //    var ent_nuspec = archive.GetEntry("DBreeze.nuspec");
            //    ent_nuspec.ExtractToFile(MyPath + @"..\Nuget\Actual\DBreeze.nuspec", true);
            //    ent_nuspec.Delete();
            //    var ent_txt = File.ReadAllText(MyPath + @"..\Nuget\Actual\DBreeze.nuspec");

            //    fileVersion = FileVersionInfo.GetVersionInfo(MyPath + @"NET45\DBreeze.dll").FileVersion;
            //    fileVersion1 = fileVersion.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries);

            //    string verAsString = fileVersion1[0] + "." + fileVersion1[1] + "." + fileVersion1[2] + fileVersion1[3];

            //    ent_txt = ent_txt.Replace(pck.Title, pck.Title.Substring(0, pck.Title.Length - verAsString.Length) + verAsString);

            //    if (fileVersion1[1][0] == '0')
            //        fileVersion1[1] = fileVersion1[1].Substring(1);
            //    //productVersion = FileVersionInfo.GetVersionInfo(MyPath + @"NET45\DBreeze.dll").ProductVersion;
            //    ent_txt = ent_txt.Replace("<version>" + pck.Version + "</version>", "<version>" + fileVersion1[0] + "." + fileVersion1[1] + ".0" + "</version>");
            //    File.WriteAllText(MyPath + @"..\Nuget\Actual\DBreeze.nuspec", ent_txt);
            //    archive.CreateEntryFromFile(MyPath + @"..\Nuget\Actual\DBreeze.nuspec", "DBreeze.nuspec", CompressionLevel.Optimal);

            //    File.Delete(MyPath + @"..\Nuget\Actual\DBreeze.nuspec");

            //    ////REMARKED CHECKED, XAMARIN MOVING TO .NET STANDARD
            //    //archive.GetEntry("lib/MonoAndroid/DBreeze.dll").Delete();
            //    //archive.GetEntry("lib/MonoAndroid/DBreeze.XML").Delete();
            //    //archive.CreateEntryFromFile(MyPath + "XAMARIN" + @"\DBreeze.dll", "lib/MonoAndroid/DBreeze.dll", CompressionLevel.Optimal);
            //    //archive.CreateEntryFromFile(MyPath + "XAMARIN" + @"\DBreeze.xml", "lib/MonoAndroid/DBreeze.XML", CompressionLevel.Optimal);

            //    archive.GetEntry("lib/net35/DBreeze.dll").Delete();
            //    archive.GetEntry("lib/net35/DBreeze.XML").Delete();
            //    archive.CreateEntryFromFile(MyPath + "NET35" + @"\DBreeze.dll", "lib/net35/DBreeze.dll", CompressionLevel.Optimal);
            //    archive.CreateEntryFromFile(MyPath + "NET35" + @"\DBreeze.xml", "lib/net35/DBreeze.XML", CompressionLevel.Optimal);

            //    archive.GetEntry("lib/net40/DBreeze.dll").Delete();
            //    archive.GetEntry("lib/net40/DBreeze.XML").Delete();
            //    archive.CreateEntryFromFile(MyPath + "NET40" + @"\DBreeze.dll", "lib/net40/DBreeze.dll", CompressionLevel.Optimal);
            //    archive.CreateEntryFromFile(MyPath + "NET40" + @"\DBreeze.xml", "lib/net40/DBreeze.XML", CompressionLevel.Optimal);

            //    archive.GetEntry("lib/net461/DBreeze.dll").Delete();
            //    archive.GetEntry("lib/net461/DBreeze.XML").Delete();
            //    archive.CreateEntryFromFile(MyPath + "NET461" + @"\DBreeze.dll", "lib/net461/DBreeze.dll", CompressionLevel.Optimal);
            //    archive.CreateEntryFromFile(MyPath + "NET461" + @"\DBreeze.xml", "lib/net461/DBreeze.XML", CompressionLevel.Optimal);

            //    archive.GetEntry("lib/net462/DBreeze.dll").Delete();
            //    archive.GetEntry("lib/net462/DBreeze.XML").Delete();
            //    archive.CreateEntryFromFile(MyPath + "NET462" + @"\DBreeze.dll", "lib/net462/DBreeze.dll", CompressionLevel.Optimal);
            //    archive.CreateEntryFromFile(MyPath + "NET462" + @"\DBreeze.xml", "lib/net462/DBreeze.XML", CompressionLevel.Optimal);

            //    archive.GetEntry("lib/net47/DBreeze.dll").Delete();
            //    archive.GetEntry("lib/net47/DBreeze.XML").Delete();
            //    archive.CreateEntryFromFile(MyPath + "NET47" + @"\DBreeze.dll", "lib/net47/DBreeze.dll", CompressionLevel.Optimal);
            //    archive.CreateEntryFromFile(MyPath + "NET47" + @"\DBreeze.xml", "lib/net47/DBreeze.XML", CompressionLevel.Optimal);

            //    archive.GetEntry("lib/net472/DBreeze.dll").Delete();
            //    archive.GetEntry("lib/net472/DBreeze.XML").Delete();
            //    archive.CreateEntryFromFile(MyPath + "NET472" + @"\DBreeze.dll", "lib/net472/DBreeze.dll", CompressionLevel.Optimal);
            //    archive.CreateEntryFromFile(MyPath + "NET472" + @"\DBreeze.xml", "lib/net472/DBreeze.XML", CompressionLevel.Optimal);

            //    archive.GetEntry("lib/net45/DBreeze.dll").Delete();
            //    archive.GetEntry("lib/net45/DBreeze.XML").Delete();
            //    archive.CreateEntryFromFile(MyPath + "NET45" + @"\DBreeze.dll", "lib/net45/DBreeze.dll", CompressionLevel.Optimal);
            //    archive.CreateEntryFromFile(MyPath + "NET45" + @"\DBreeze.xml", "lib/net45/DBreeze.XML", CompressionLevel.Optimal);

            //    //archive.GetEntry("lib/netcore451/DBreeze.dll").Delete();
            //    //archive.GetEntry("lib/netcore451/DBreeze.XML").Delete();
            //    //archive.CreateEntryFromFile(MyPath + "UWP" + @"\DBreeze.dll", "lib/netcore451/DBreeze.dll", CompressionLevel.Optimal);
            //    //archive.CreateEntryFromFile(MyPath + "UWP" + @"\DBreeze.xml", "lib/netcore451/DBreeze.XML", CompressionLevel.Optimal);

            //    archive.GetEntry("lib/netcoreapp1.0/DBreeze.dll").Delete();
            //    archive.GetEntry("lib/netcoreapp1.0/DBreeze.XML").Delete();
            //    archive.CreateEntryFromFile(MyPath + "NETCOREAPP1_0" + @"\DBreeze.dll", "lib/netcoreapp1.0/DBreeze.dll", CompressionLevel.Optimal);
            //    archive.CreateEntryFromFile(MyPath + "NETCOREAPP1_0" + @"\DBreeze.xml", "lib/netcoreapp1.0/DBreeze.XML", CompressionLevel.Optimal);
            //    //archive.CreateEntryFromFile(MyPath + "UWP" + @"\DBreeze.dll", "lib/netcoreapp1.0/DBreeze.dll", CompressionLevel.Optimal);
            //    //archive.CreateEntryFromFile(MyPath + "UWP" + @"\DBreeze.xml", "lib/netcoreapp1.0/DBreeze.XML", CompressionLevel.Optimal);

            //    archive.GetEntry("lib/netcoreapp1.1/DBreeze.dll").Delete();
            //    archive.GetEntry("lib/netcoreapp1.1/DBreeze.XML").Delete();
            //    archive.CreateEntryFromFile(MyPath + "NETCOREAPP1_1" + @"\DBreeze.dll", "lib/netcoreapp1.1/DBreeze.dll", CompressionLevel.Optimal);
            //    archive.CreateEntryFromFile(MyPath + "NETCOREAPP1_1" + @"\DBreeze.xml", "lib/netcoreapp1.1/DBreeze.XML", CompressionLevel.Optimal);

            //    archive.GetEntry("lib/netcoreapp2.0/DBreeze.dll").Delete();
            //    archive.GetEntry("lib/netcoreapp2.0/DBreeze.XML").Delete();
            //    archive.CreateEntryFromFile(MyPath + "NETCOREAPP2_0" + @"\DBreeze.dll", "lib/netcoreapp2.0/DBreeze.dll", CompressionLevel.Optimal);
            //    archive.CreateEntryFromFile(MyPath + "NETCOREAPP2_0" + @"\DBreeze.xml", "lib/netcoreapp2.0/DBreeze.XML", CompressionLevel.Optimal);

            //    archive.GetEntry("lib/netcoreapp3.1/DBreeze.dll")?.Delete();
            //    archive.GetEntry("lib/netcoreapp3.1/DBreeze.XML")?.Delete();
            //    archive.CreateEntryFromFile(MyPath + "NETCOREAPP3_1" + @"\DBreeze.dll", "lib/netcoreapp3.1/DBreeze.dll", CompressionLevel.Optimal);
            //    archive.CreateEntryFromFile(MyPath + "NETCOREAPP3_1" + @"\DBreeze.xml", "lib/netcoreapp3.1/DBreeze.XML", CompressionLevel.Optimal);

            //    CreateLibEntry(archive, "lib/net6.0", MyPath + "NET6_0"); //<--------------------------------------------------------------------------------- USE THAT FOR NEW ENTRIES
            //    archive.GetEntry("lib/net6.0/DBreeze.dll")?.Delete();
            //    archive.GetEntry("lib/net6.0/DBreeze.XML")?.Delete();
            //    archive.CreateEntryFromFile(MyPath + "NET6_0" + @"\DBreeze.dll", "lib/net6.0/DBreeze.dll", CompressionLevel.Optimal);
            //    archive.CreateEntryFromFile(MyPath + "NET6_0" + @"\DBreeze.XML", "lib/net6.0/DBreeze.XML", CompressionLevel.Optimal);

            //    archive.GetEntry("lib/netstandard1.6/DBreeze.dll").Delete();
            //    archive.GetEntry("lib/netstandard1.6/DBreeze.XML").Delete();
            //    archive.CreateEntryFromFile(MyPath + "NETSTANDARD16" + @"\DBreeze.dll", "lib/netstandard1.6/DBreeze.dll", CompressionLevel.Optimal);
            //    archive.CreateEntryFromFile(MyPath + "NETSTANDARD16" + @"\DBreeze.xml", "lib/netstandard1.6/DBreeze.XML", CompressionLevel.Optimal);
            //    //archive.CreateEntryFromFile(MyPath + "UWP" + @"\DBreeze.dll", "lib/netstandard1.6/DBreeze.dll", CompressionLevel.Optimal);
            //    //archive.CreateEntryFromFile(MyPath + "UWP" + @"\DBreeze.dll", "lib/netstandard1.6/DBreeze.XML", CompressionLevel.Optimal);

               
            //    archive.GetEntry("lib/netstandard2.0/DBreeze.dll").Delete();
            //    archive.GetEntry("lib/netstandard2.0/DBreeze.XML").Delete();
            //    archive.CreateEntryFromFile(MyPath + "NETSTANDARD2_0" + @"\DBreeze.dll", "lib/netstandard2.0/DBreeze.dll", CompressionLevel.Optimal);
            //    archive.CreateEntryFromFile(MyPath + "NETSTANDARD2_0" + @"\DBreeze.xml", "lib/netstandard2.0/DBreeze.XML", CompressionLevel.Optimal);

            //    CreateLibEntry(archive, "lib/netstandard2.1", MyPath + "NETSTANDARD2_1"); //<------------------------------------------------------------------------------------ USE THAT FOR NEW ENTRIES
            //    archive.GetEntry("lib/netstandard2.1/DBreeze.dll")?.Delete();
            //    archive.GetEntry("lib/netstandard2.1/DBreeze.XML")?.Delete();
            //    archive.CreateEntryFromFile(MyPath + "NETSTANDARD2_1" + @"\DBreeze.dll", "lib/netstandard2.1/DBreeze.dll", CompressionLevel.Optimal);
            //    archive.CreateEntryFromFile(MyPath + "NETSTANDARD2_1" + @"\DBreeze.xml", "lib/netstandard2.1/DBreeze.XML", CompressionLevel.Optimal);

            //    archive.GetEntry("lib/portable-net45+win8+wp8+wpa81/DBreeze.dll").Delete();
            //    archive.GetEntry("lib/portable-net45+win8+wp8+wpa81/DBreeze.XML").Delete();
            //    archive.CreateEntryFromFile(MyPath + "PORTABLE" + @"\DBreeze.dll", "lib/portable-net45+win8+wp8+wpa81/DBreeze.dll", CompressionLevel.Optimal);
            //    archive.CreateEntryFromFile(MyPath + "PORTABLE" + @"\DBreeze.xml", "lib/portable-net45+win8+wp8+wpa81/DBreeze.XML", CompressionLevel.Optimal);


            //}

            //File.Copy(MyPath + @"..\Nuget\Actual\DBreeze.actual.nupkg", MyPath + $"..\\Nuget\\DBreeze.{fileVersion1[0] + "." + fileVersion1[1] + ".0"}.nupkg", true);


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

        /// <summary>
        /// Returns non 0 if error
        /// </summary>
        /// <param name="msbldpath"></param>
        /// <param name="folder"></param>
        /// <param name="targetFramework"></param>
        /// <param name="compilationSymbols"></param>
        /// <returns></returns>
        static int Compile(string msbldpath, string folder, string targetFramework, string compilationSymbols)
        {
            ProcessStartInfo start = new ProcessStartInfo()
            {
                Arguments = $@"{folder} {targetFramework} ""{compilationSymbols}""",
                FileName = msbldpath,
                WindowStyle = ProcessWindowStyle.Hidden,

                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            //// Enter in the command line arguments, everything you would enter after the executable name itself
            //start.Arguments = folder;
            //// Enter the executable to run, including the complete path
            //start.FileName = msbldpath;
            //// Do you want to show a console window?
            //start.WindowStyle = ProcessWindowStyle.Normal;

            int exitCode = 0;




            using (Process proc = Process.Start(start))
            {
                if (proc != null)
                {
                    proc.OutputDataReceived += (sender, e) => Console.WriteLine(e.Data);
                    proc.ErrorDataReceived += (sender, e) => Console.WriteLine(e.Data);

                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    proc.WaitForExit();

                    // Retrieve the app's exit code
                    exitCode = proc.ExitCode;
                }


            }

            return exitCode;

        }


        static void Compile(string msbldpath, string folder)
        {
            ProcessStartInfo start = new ProcessStartInfo()
            {
                 Arguments = folder,
                 FileName = msbldpath,
                 WindowStyle = ProcessWindowStyle.Hidden,

                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            //// Enter in the command line arguments, everything you would enter after the executable name itself
            //start.Arguments = folder;
            //// Enter the executable to run, including the complete path
            //start.FileName = msbldpath;
            //// Do you want to show a console window?
            //start.WindowStyle = ProcessWindowStyle.Normal;

            int exitCode = 0;


            

            using (Process proc = Process.Start(start))
            {
                if(proc != null)
                {
                    proc.OutputDataReceived += (sender, e) => Console.WriteLine(e.Data);
                    proc.ErrorDataReceived += (sender, e) => Console.WriteLine(e.Data);

                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    proc.WaitForExit();

                    // Retrieve the app's exit code
                    exitCode = proc.ExitCode;
                }
              
                
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
