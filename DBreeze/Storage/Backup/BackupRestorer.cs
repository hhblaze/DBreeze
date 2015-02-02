using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.IO;

using DBreeze.Exceptions;
using DBreeze.Utils;

namespace DBreeze.Storage
{
    /// <summary>
    /// Access to Database restoration from incremental backups.
    /// </summary>
    public class BackupRestorer
    {
        /// <summary>
        /// Object characterizes the backup restoration process
        /// </summary>
        public class BackupRestorationProcess
        {
            public BackupRestorationProcess()
            {
                Finished = false;
                ReadinessInProcent = 0;
            }

            /// <summary>
            /// How many procesnt of restoration is done
            /// </summary>
            public int ReadinessInProcent { get; set; }

            /// <summary>
            /// true when restore is completed
            /// </summary>
            public bool Finished { get; set; }
        }

        /// <summary>
        /// Subscribe on it to receive notification about restore process
        /// </summary>
        public event Action<BackupRestorationProcess> OnRestore;

        /// <summary>
        /// Place where resides or should reside database
        /// </summary>
        public string DataBaseFolder { get; set; }
        /// <summary>
        /// Place where reside incremnetal dbreeze backup files
        /// </summary>
        public string BackupFolder { get; set; }

        /// <summary>
        /// Holder of filenames and file handlers
        /// </summary>
        Dictionary<string, FileStream> ds = new Dictionary<string, FileStream>();

        Backup.BackupFileNamesParser BackupFNP = new Backup.BackupFileNamesParser();

        public BackupRestorer()
        {
        }

        /// <summary>
        /// Starts backup restore routine
        /// </summary>
        public void StartRestoration()
        {
            try
            {
                //holder of filenames and filestreams
                ds.Clear();

                DirectoryInfo diDB = new DirectoryInfo(DataBaseFolder);
                DirectoryInfo diBP = new DirectoryInfo(BackupFolder);

                if (!diDB.Exists)
                    diDB.Create();

                if (!diBP.Exists)
                    diBP.Create();

                long totalBackupFileLength = 0;
                long processed = 0;

                foreach (var file in diBP.GetFiles())
                {
                    totalBackupFileLength += file.Length;
                }

                if (totalBackupFileLength == processed)
                {
                    OnRestore(new BackupRestorationProcess()
                    {
                        ReadinessInProcent = 100,
                        Finished = true
                    });
                    return;
                }

                int readinessInProcent = Convert.ToInt32((processed * 100) / totalBackupFileLength);
                int prevReadinessInProcent = 0;

                OnRestore(new BackupRestorationProcess()
                {
                    ReadinessInProcent = readinessInProcent,
                    Finished = false
                });

                byte[] readOut = new byte[100000];
                int cnt = 0;
                byte[] pack = null;
                uint packSize = 0;

                foreach (var file in diBP.GetFiles().Where(r => r.Name.StartsWith("dbreeze_ibp_")).OrderBy(r => r.Name))
                {
                    using (var bfs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        while ((cnt = bfs.Read(readOut, 0, readOut.Length)) > 0)
                        {
                            processed += cnt;
                            readinessInProcent = Convert.ToInt32((processed * 100) / totalBackupFileLength);
                            pack = pack.Concat(readOut.Substring(0, cnt));

                            while (true)
                            {
                                if (pack == null || pack.Length < 4)
                                    break;

                                packSize = pack.Substring(0, 4).To_UInt32_BigEndian();

                                if (pack.Length >= 4 + packSize)
                                {
                                    this.DoPackage(pack.Substring(4, (int)packSize));
                                    pack = pack.Substring(4 + (int)packSize);
                                }
                                else
                                    break;
                            }

                            if (prevReadinessInProcent != readinessInProcent)
                            {
                                prevReadinessInProcent = readinessInProcent;
                                OnRestore(new BackupRestorationProcess()
                                {
                                    ReadinessInProcent = readinessInProcent,
                                    Finished = false
                                });
                            }
                        }

                        if (prevReadinessInProcent != readinessInProcent)
                        {
                            prevReadinessInProcent = readinessInProcent;
                            OnRestore(new BackupRestorationProcess()
                            {
                                ReadinessInProcent = readinessInProcent,
                                Finished = false
                            });
                        }

                        bfs.Close();
                    }
                }

                this.CloseHandels();

                OnRestore(new BackupRestorationProcess()
                {
                    ReadinessInProcent = 100,
                    Finished = true
                });

            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        private void CloseHandels()
        {
            foreach (var f in ds)
            {
                f.Value.Close();
                f.Value.Dispose();
            }

            ds.Clear();
        }



        private FileStream GetFileStream(string fileName)
        {
            FileStream fsret = null;

            if (!ds.TryGetValue(fileName, out fsret))
            {
                if (fileName.EndsWith(".rhp") || fileName.EndsWith(".rol"))
                    return null;

                //Creating 3 files
                string tfn = Path.Combine(this.DataBaseFolder, fileName);
                fsret = new FileStream(tfn + ".rhp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                ds.Add(fileName + ".rhp", fsret);

                fsret = new FileStream(tfn + ".rol", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                ds.Add(fileName + ".rol", fsret);

                fsret = new FileStream(tfn, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                ds.Add(fileName, fsret);

            }

            return fsret;
        }

        private void DoPackage(byte[] pack)
        {
            ulong fileNumber = pack.Substring(0, 8).To_UInt64_BigEndian();
            byte type = pack.Substring(8, 1)[0];

            string filename = BackupFNP.ParseFilenameBack(fileNumber);
            long offset = 0;
            byte[] data = null;
            //Console.WriteLine("t: {0}", type);

            //types description
            // 0 - table file, 1 - rollback file, 2 - rollbackhelper, 3 - recreate table file (only table file), 4 - recreate rollback file (only rollback file), 5 - removing complete table

            FileStream lfs = null;
            bool contains = false;
            string tfn = String.Empty;

            switch (type)
            {
                case 0:

                    //Write into table file
                    lfs = this.GetFileStream(filename);

                    //if (lfs == null)
                    //{
                    //    Console.WriteLine("Backup lfs = null");
                    //    return;
                    //}

                    offset = pack.Substring(9, 8).To_Int64_BigEndian();
                    data = pack.Substring(17);
                    lfs.Position = offset;
                    lfs.Write(data, 0, data.Length);
                    lfs.Flush();
                    break;
                case 1:
                    //write into rollback file
                    lfs = this.GetFileStream(filename + ".rol");

                    if (lfs == null)
                    {
                        Console.WriteLine("Backup lfs = null");
                        return;
                    }

                    offset = pack.Substring(9, 8).To_Int64_BigEndian();
                    data = pack.Substring(17);
                    lfs.Position = offset;
                    lfs.Write(data, 0, data.Length);
                    lfs.Flush();
                    break;
                case 2:
                    //write into rollbackhelper
                    lfs = this.GetFileStream(filename + ".rhp");

                    if (lfs == null)
                    {
                        Console.WriteLine("Backup lfs = null");
                        return;
                    }

                    offset = pack.Substring(9, 8).To_Int64_BigEndian();
                    data = pack.Substring(17);
                    lfs.Position = offset;
                    lfs.Write(data, 0, data.Length);
                    lfs.Flush();
                    break;
                case 3:

                    //3 - recreate table file (only table file)

                    contains = ds.ContainsKey(filename);
                    if (contains)
                    {
                        ds[filename].Close();
                        ds[filename].Dispose();

                    }

                    tfn = Path.Combine(this.DataBaseFolder, filename);
                    File.Delete(tfn);

                    if (!contains)
                        ds.Add(filename, null);

                    ds[filename] = new FileStream(tfn, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                    break;

                case 4:

                    //4 - recreate rollback file (only rollback file)

                    contains = ds.ContainsKey(filename + ".rol");
                    if (contains)
                    {
                        ds[filename + ".rol"].Close();
                        ds[filename + ".rol"].Dispose();

                    }

                    tfn = Path.Combine(this.DataBaseFolder, filename + ".rol");
                    File.Delete(tfn);

                    if (!contains)
                        ds.Add(filename + ".rol", null);

                    ds[filename + ".rol"] = new FileStream(tfn, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                    break;
                case 5:

                    //5 - removing complete table

                    if (ds.ContainsKey(filename))
                    {
                        ds[filename].Close();
                        ds[filename].Dispose();
                    }

                    ds.Remove(filename);

                    if (ds.ContainsKey(filename + ".rol"))
                    {
                        ds[filename + ".rol"].Close();
                        ds[filename + ".rol"].Dispose();
                    }

                    ds.Remove(filename + ".rol");

                    if (ds.ContainsKey(filename + ".rhp"))
                    {
                        ds[filename + ".rhp"].Close();
                        ds[filename + ".rhp"].Dispose();
                    }

                    ds.Remove(filename + ".rhp");

                    break;

            }
        }

    }//Restorer class end

}
