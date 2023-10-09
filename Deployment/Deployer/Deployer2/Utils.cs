using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Deployer
{
    internal static class Utils
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        /// <param name="color"></param>
        public static void ConsolePrint(string text, System.ConsoleColor color = ConsoleColor.White)
        {
            ConsoleColor originalColor = Console.ForegroundColor;

            // Set the console text color to green
            Console.ForegroundColor = color;

            // Print text in green color
            Console.WriteLine(text);

            // Reset console text color back to the original color
            Console.ForegroundColor = originalColor;
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="msbldpath"></param>
        /// <param name="folder"></param>
        /// <param name="targetFramework"></param>
        /// <param name="compilationSymbols"></param>
        /// <returns></returns>

        public static int Compile(string msbldpath, string targetFrameworkName, string folder, string targetFramework, string compilationSymbols, string releaseFolder, string destinationFolder)
        {
            ProcessStartInfo start = new ProcessStartInfo()
            {
                Arguments = $@"{folder} {targetFramework} ""{compilationSymbols}"" {targetFrameworkName}",
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

            if (exitCode != 0) Utils.ConsolePrint($"Error: {targetFramework} in {folder}", ConsoleColor.Red);
            else
            {
                try
                {
                    File.Copy(Program.MyPath + @"..\..\" + releaseFolder + @"\DBreeze.dll", Program.MyPath + destinationFolder + @"\DBreeze.dll", true);
                    File.Copy(Program.MyPath + @"..\..\" + releaseFolder + @"\DBreeze.XML", Program.MyPath + destinationFolder + @"\DBreeze.XML", true);
                }
                catch (Exception ex)
                {
                    Utils.ConsolePrint($"Error: {targetFramework} in {folder}: {ex.ToString()}", ConsoleColor.Red);
                    return 1;
                }
                

                Utils.ConsolePrint($"Done: {folder}", ConsoleColor.Green);
            }

            return exitCode;

        }



    }
}
