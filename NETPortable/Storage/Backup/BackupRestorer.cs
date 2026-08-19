using System;
using System.Collections.Generic;
using System.IO;

namespace DBreeze.Storage
{
    public class BackupRestorer
    {
        public class BackupRestorationProcess
        {
            public BackupRestorationProcess()
            {
                Finished = false;
                ReadinessInProcent = 0;
            }

            public int ReadinessInProcent { get; set; }
            public bool Finished { get; set; }
        }

        public event Action<BackupRestorationProcess> OnRestore;
        public string DataBaseFolder { get; set; }
        public string BackupFolder { get; set; }

        readonly Dictionary<string, IFileStream> _handles = new Dictionary<string, IFileStream>();
        readonly Backup.BackupFileNamesParser _fileNames = new Backup.BackupFileNamesParser();
        readonly DBreezeConfiguration _configuration;
        readonly byte[] _sizeBuffer = new byte[4];
        readonly byte[] _recordHeader = new byte[17];
        readonly byte[] _copyBuffer = new byte[64 * 1024];

        public BackupRestorer(DBreezeConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException("configuration");
            if (configuration.FSFactory == null)
                throw new ArgumentException("configuration.FSFactory must be initialized.", "configuration");
            _configuration = configuration;
        }

        public void StartRestoration()
        {
            CloseHandles();
            IDirectoryInfo databaseDirectory = _configuration.FSFactory.CreateDirectoryInfo(DataBaseFolder);
            IDirectoryInfo backupDirectory = _configuration.FSFactory.CreateDirectoryInfo(BackupFolder);
            if (!databaseDirectory.Exists)
                databaseDirectory.Create();
            if (!backupDirectory.Exists)
                backupDirectory.Create();

            IFileInfo[] allFiles = backupDirectory.GetFiles();
            var backupFiles = new List<IFileInfo>();
            long totalLength = 0;
            foreach (IFileInfo file in allFiles)
            {
                if (!file.Name.StartsWith("dbreeze_ibp_", StringComparison.Ordinal))
                    continue;
                backupFiles.Add(file);
                totalLength = checked(totalLength + file.Length);
            }
            backupFiles.Sort(delegate(IFileInfo x, IFileInfo y)
            {
                return StringComparer.Ordinal.Compare(x.Name, y.Name);
            });

            long processed = 0;
            int lastProgress = -1;
            ReportProgress(0, false, ref lastProgress);
            try
            {
                foreach (IFileInfo file in backupFiles)
                {
                    using (IFileStream source = _configuration.FSFactory.CreateType3(file.FullName))
                    {
                        while (source.Position < source.Length)
                        {
                            if (source.Length - source.Position < 4)
                                throw new InvalidDataException("Incomplete incremental backup record size.");
                            ReadExactly(source, _sizeBuffer, 0, 4);
                            uint recordSize = ReadUInt32BigEndian(_sizeBuffer, 0);
                            if (recordSize < 9 || recordSize > Int32.MaxValue)
                                throw new InvalidDataException("Invalid incremental backup record size.");
                            if (source.Length - source.Position < recordSize)
                                throw new InvalidDataException("Incomplete incremental backup record.");

                            ReadExactly(source, _recordHeader, 0, 9);
                            ulong fileNumber = ReadUInt64BigEndian(_recordHeader, 0);
                            byte type = _recordHeader[8];
                            string fileName = _fileNames.ParseFilenameBack(fileNumber);

                            if (type <= 2)
                            {
                                if (recordSize < 17)
                                    throw new InvalidDataException("Backup write record is too short.");
                                ReadExactly(source, _recordHeader, 9, 8);
                                long position = DecodeInt64BigEndian(_recordHeader, 9);
                                int dataLength = checked((int)recordSize - 17);
                                if (position < 0 || position > Int64.MaxValue - dataLength)
                                    throw new InvalidDataException("Invalid backup write range.");
                                WritePayload(source, GetFileStream(fileName, type), position, dataLength);
                            }
                            else if (type == 3 || type == 4 || type == 5)
                            {
                                if (recordSize != 9)
                                    throw new InvalidDataException("Backup command record has an invalid size.");
                                if (type == 5)
                                    DeleteTable(fileName);
                                else
                                    RecreateFile(type == 3 ? fileName : fileName + ".rol");
                            }
                            else
                            {
                                throw new InvalidDataException("Unknown incremental backup record type.");
                            }

                            processed = checked(processed + 4 + recordSize);
                            int progress = totalLength == 0 ? 100 : (int)(processed * 100 / totalLength);
                            ReportProgress(progress, false, ref lastProgress);
                        }
                    }
                }
            }
            finally
            {
                CloseHandles();
            }

            ReportProgress(100, true, ref lastProgress);
        }

        void WritePayload(IFileStream source, IFileStream destination, long position, int count)
        {
            destination.Position = position;
            while (count > 0)
            {
                int chunk = count > _copyBuffer.Length ? _copyBuffer.Length : count;
                ReadExactly(source, _copyBuffer, 0, chunk);
                destination.Write(_copyBuffer, 0, chunk);
                count -= chunk;
            }
        }

        IFileStream GetFileStream(string tableFileName, byte type)
        {
            string suffix = type == 1 ? ".rol" : type == 2 ? ".rhp" : String.Empty;
            string key = tableFileName + suffix;
            IFileStream stream;
            if (_handles.TryGetValue(key, out stream))
                return stream;

            EnsureTableFiles(tableFileName);
            return _handles[key];
        }

        void EnsureTableFiles(string tableFileName)
        {
            if (_handles.ContainsKey(tableFileName))
                return;

            string tablePath = Path.Combine(DataBaseFolder, tableFileName);
            string directory = Path.GetDirectoryName(tablePath);
            if (!String.IsNullOrEmpty(directory))
            {
                IDirectoryInfo directoryInfo = _configuration.FSFactory.CreateDirectoryInfo(directory);
                if (!directoryInfo.Exists)
                    directoryInfo.Create();
            }
            _handles.Add(tableFileName + ".rhp", _configuration.FSFactory.CreateType2(tablePath + ".rhp"));
            _handles.Add(tableFileName + ".rol", _configuration.FSFactory.CreateType2(tablePath + ".rol"));
            _handles.Add(tableFileName, _configuration.FSFactory.CreateType2(tablePath));
        }

        void RecreateFile(string fileName)
        {
            IFileStream stream;
            if (_handles.TryGetValue(fileName, out stream))
            {
                stream.Dispose();
                _handles.Remove(fileName);
            }
            string path = Path.Combine(DataBaseFolder, fileName);
            _configuration.FSFactory.Delete(path);
            _handles.Add(fileName, _configuration.FSFactory.CreateType2(path));
        }

        void DeleteTable(string tableFileName)
        {
            string[] names = { tableFileName, tableFileName + ".rol", tableFileName + ".rhp" };
            foreach (string name in names)
            {
                IFileStream stream;
                if (_handles.TryGetValue(name, out stream))
                {
                    stream.Dispose();
                    _handles.Remove(name);
                }
                _configuration.FSFactory.Delete(Path.Combine(DataBaseFolder, name));
            }
        }

        void CloseHandles()
        {
            try
            {
                foreach (KeyValuePair<string, IFileStream> item in _handles)
                    item.Value.Flush(true);
            }
            finally
            {
                foreach (KeyValuePair<string, IFileStream> item in _handles)
                {
                    try { item.Value.Dispose(); }
                    catch { }
                }
                _handles.Clear();
            }
        }

        void ReportProgress(int progress, bool finished, ref int previous)
        {
            if (!finished && progress == previous)
                return;
            previous = progress;
            Action<BackupRestorationProcess> handler = OnRestore;
            if (handler != null)
                handler(new BackupRestorationProcess { ReadinessInProcent = progress, Finished = finished });
        }

        static void ReadExactly(IFileStream stream, byte[] buffer, int offset, int count)
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

        static uint ReadUInt32BigEndian(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
                ((uint)data[offset + 2] << 8) | data[offset + 3];
        }

        static ulong ReadUInt64BigEndian(byte[] data, int offset)
        {
            ulong value = 0;
            for (int i = 0; i < 8; i++)
                value = (value << 8) | data[offset + i];
            return value;
        }

        static long DecodeInt64BigEndian(byte[] data, int offset)
        {
            // DBreeze's legacy signed-integer format offsets Int64 by Int64.MinValue.
            return unchecked((long)(ReadUInt64BigEndian(data, offset) ^ 0x8000000000000000UL));
        }
    }
}
