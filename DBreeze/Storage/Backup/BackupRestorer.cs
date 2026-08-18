using System;
using System.Collections.Generic;
using System.IO;

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
        readonly byte[] _recordSizeBuffer = new byte[4];
        readonly byte[] _recordHeaderBuffer = new byte[9];
        readonly byte[] _offsetBuffer = new byte[8];
        readonly byte[] _copyBuffer = new byte[64 * 1024];

        Backup.BackupFileNamesParser BackupFNP = new Backup.BackupFileNamesParser();

        public BackupRestorer()
        {
        }

        /// <summary>
        /// Starts backup restore routine
        /// </summary>
        public void StartRestoration()
        {
            if (String.IsNullOrEmpty(DataBaseFolder))
                throw new ArgumentNullException("DataBaseFolder");
            if (String.IsNullOrEmpty(BackupFolder))
                throw new ArgumentNullException("BackupFolder");

            try
            {
                CloseHandles();
                ds.Clear();

                DirectoryInfo diDB = new DirectoryInfo(DataBaseFolder);
                DirectoryInfo diBP = new DirectoryInfo(BackupFolder);

                if (!diDB.Exists)
                    diDB.Create();

                if (!diBP.Exists)
                    diBP.Create();

                FileInfo[] backupFiles = diBP.GetFiles("dbreeze_ibp_*.ibp");
                Array.Sort(backupFiles, delegate(FileInfo x, FileInfo y)
                {
                    return String.CompareOrdinal(x.Name, y.Name);
                });

                long totalBackupFileLength = 0;
                long processed = 0;

                foreach (FileInfo file in backupFiles)
                    totalBackupFileLength += file.Length;

                if (totalBackupFileLength == 0)
                {
                    NotifyProgress(100, true);
                    return;
                }

                int readinessInProcent = 0;
                int prevReadinessInProcent = 0;

                NotifyProgress(0, false);

                foreach (FileInfo file in backupFiles)
                {
                    using (FileStream bfs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        while (bfs.Position < bfs.Length)
                        {
                            ReadExactly(bfs, _recordSizeBuffer, 0, _recordSizeBuffer.Length);
                            uint packageSize = ReadUInt32BigEndian(_recordSizeBuffer, 0);
                            if (packageSize < 9 || packageSize > Int32.MaxValue)
                                throw new InvalidDataException("Invalid incremental backup record size.");
                            if ((long)packageSize > bfs.Length - bfs.Position)
                                throw new InvalidDataException("Incomplete incremental backup record.");

                            ApplyPackage(bfs, (int)packageSize);
                            processed += 4L + packageSize;
                            readinessInProcent = (int)((double)processed * 100.0 / totalBackupFileLength);

                            if (prevReadinessInProcent != readinessInProcent)
                            {
                                prevReadinessInProcent = readinessInProcent;
                                NotifyProgress(readinessInProcent, false);
                            }
                        }
                    }
                }

                CloseHandles();
                NotifyProgress(100, true);
            }
            finally
            {
                CloseHandles();
            }
        }

        private void NotifyProgress(int readiness, bool finished)
        {
            Action<BackupRestorationProcess> handler = OnRestore;
            if (handler != null)
                handler(new BackupRestorationProcess { ReadinessInProcent = readiness, Finished = finished });
        }

        private void CloseHandles()
        {
            try
            {
                foreach (KeyValuePair<string, FileStream> file in ds)
                {
                    if (file.Value != null)
                        FSR.NET_Flush(file.Value);
                }
            }
            finally
            {
                foreach (KeyValuePair<string, FileStream> file in ds)
                {
                    if (file.Value == null)
                        continue;
                    try { file.Value.Dispose(); }
                    catch { }
                }
                ds.Clear();
            }
        }

        private FileStream GetFileStream(string fileName)
        {
            FileStream fsret = null;

            if (!ds.TryGetValue(fileName, out fsret))
            {
                string tableName = fileName;
                if (tableName.EndsWith(".rhp"))
                    tableName = tableName.Substring(0, tableName.Length - 4);
                else if (tableName.EndsWith(".rol"))
                    tableName = tableName.Substring(0, tableName.Length - 4);

                string tablePath = Path.Combine(DataBaseFolder, tableName);
                AddStreamIfMissing(tableName, tablePath);
                AddStreamIfMissing(tableName + ".rol", tablePath + ".rol");
                AddStreamIfMissing(tableName + ".rhp", tablePath + ".rhp");
                fsret = ds[fileName];
            }

            return fsret;
        }

        private void AddStreamIfMissing(string key, string path)
        {
            if (!ds.ContainsKey(key))
            {
                string directory = Path.GetDirectoryName(path);
                if (!String.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                ds.Add(key, new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
        }

        private void ApplyPackage(Stream backupStream, int packageSize)
        {
            ReadExactly(backupStream, _recordHeaderBuffer, 0, _recordHeaderBuffer.Length);
            ulong fileNumber = ReadUInt64BigEndian(_recordHeaderBuffer, 0);
            byte type = _recordHeaderBuffer[8];
            string filename = BackupFNP.ParseFilenameBack(fileNumber);

            switch (type)
            {
                case 0:
                case 1:
                case 2:
                    if (packageSize < 17)
                        throw new InvalidDataException("Backup write record is too short.");

                    ReadExactly(backupStream, _offsetBuffer, 0, _offsetBuffer.Length);
                    long targetOffset = ReadInt64BigEndian(_offsetBuffer, 0);
                    if (targetOffset < 0)
                        throw new InvalidDataException("Backup write offset is negative.");

                    int payloadLength = packageSize - 17;
                    if (targetOffset > Int64.MaxValue - payloadLength)
                        throw new InvalidDataException("Backup write range overflows the target file.");

                    string streamName = type == 0 ? filename : type == 1 ? filename + ".rol" : filename + ".rhp";
                    FileStream target = GetFileStream(streamName);
                    target.Position = targetOffset;
                    CopyExactly(backupStream, target, payloadLength);
                    break;
                case 3:
                case 4:
                    if (packageSize != 9)
                        throw new InvalidDataException("Backup recreate record has an invalid size.");

                    string recreatedName = type == 3 ? filename : filename + ".rol";
                    CloseHandle(recreatedName);
                    string recreatedPath = Path.Combine(DataBaseFolder, recreatedName);
                    File.Delete(recreatedPath);
                    ds[recreatedName] = new FileStream(recreatedPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    break;
                case 5:
                    if (packageSize != 9)
                        throw new InvalidDataException("Backup delete record has an invalid size.");

                    string tablePath = Path.Combine(DataBaseFolder, filename);
                    CloseHandle(filename);
                    CloseHandle(filename + ".rol");
                    CloseHandle(filename + ".rhp");
                    File.Delete(tablePath);
                    File.Delete(tablePath + ".rol");
                    File.Delete(tablePath + ".rhp");
                    break;
                default:
                    throw new InvalidDataException("Unknown incremental backup record type.");
            }
        }

        private void CloseHandle(string key)
        {
            FileStream stream;
            if (!ds.TryGetValue(key, out stream))
                return;

            if (stream != null)
            {
                try
                {
                    FSR.NET_Flush(stream);
                }
                finally
                {
                    try { stream.Dispose(); }
                    finally { ds.Remove(key); }
                }
            }
            else
                ds.Remove(key);
        }

        private void CopyExactly(Stream source, Stream destination, int count)
        {
            while (count > 0)
            {
                int chunk = Math.Min(_copyBuffer.Length, count);
                ReadExactly(source, _copyBuffer, 0, chunk);
                destination.Write(_copyBuffer, 0, chunk);
                count -= chunk;
            }
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read == 0)
                    throw new EndOfStreamException("Unexpected end of incremental backup stream.");
                offset += read;
                count -= read;
            }
        }

        private static uint ReadUInt32BigEndian(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
                | ((uint)data[offset + 2] << 8) | data[offset + 3];
        }

        private static ulong ReadUInt64BigEndian(byte[] data, int offset)
        {
            ulong value = 0;
            for (int i = 0; i < 8; i++)
                value = (value << 8) | data[offset + i];
            return value;
        }

        private static long ReadInt64BigEndian(byte[] data, int offset)
        {
            // DBreeze's legacy signed-integer format offsets Int64 by Int64.MinValue.
            return unchecked((long)(ReadUInt64BigEndian(data, offset) ^ 0x8000000000000000UL));
        }

    }//Restorer class end

}
