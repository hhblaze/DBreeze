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
            MyPath = AssemblyDirectory + "\\";
            DirectoryInfo di = null;
#if DEBUG
            MyPath += @"..\..\..\..\bin\";
#endif
            di = new DirectoryInfo(MyPath);

            //Copying

            foreach (var d in di.GetDirectories())
            {
                string fileVersion = "";
                string productVersion = "";


                foreach (var f in d.GetFiles("DBreeze.dll"))
                {
                    fileVersion = FileVersionInfo.GetVersionInfo(f.FullName).FileVersion;
                    productVersion = FileVersionInfo.GetVersionInfo(f.FullName).ProductVersion;
                }

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
                    
                    break;
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
    }
}
