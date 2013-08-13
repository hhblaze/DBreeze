/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who thinks that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.IO;

using DBreeze.Exceptions;
using DBreeze.Utils;

namespace DBreeze.Storage
{
    //For those who doesn't like system snapshot approach.    

    public class Backup:IDisposable
    {
        DateTime udtInit = DateTime.UtcNow;
        internal object lock_ibp_fs = new object();
        FileStream fs = null;
        //copy from configuration
        internal string DBreezeFolderName = String.Empty;       
       
        int _bufferSize = 8192;

        public Backup()
        {        
            
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="backupFolderName">Folder where will be restored incremental backup</param>
        public Backup(string backupFolderName)
        {
            this._backupFolderName = backupFolderName;
            this.InitBackupFolder();
        }

        public void Dispose()
        {
            lock (lock_ibp_fs)
            {
                if (fs != null)
                {
                    
                    FSR.NET_Flush(fs);

                    fs.Close();
                    fs.Dispose();
                    fs = null;
                }
            }
        }

        //public enum eBackupType
        //{
        //    IncrementalBackup
        //}

        private uint _IncrementalBackupFileIntervalMin = 24 * 60;

        /// <summary>
        /// Identifies how often will be created new file for backup (Minimum 5 minutes).
        /// Default value is one day
        /// </summary>
        public uint IncrementalBackupFileIntervalMin 
        {
            get { return _IncrementalBackupFileIntervalMin; }
            set {
                if (value < 5)
                    _IncrementalBackupFileIntervalMin = 5;
                else
                    _IncrementalBackupFileIntervalMin = value;
            }
        }

        internal bool IsActive = false;
        private string _backupFolderName = String.Empty;

        /// <summary>
        /// Folder where backup files will be created
        /// </summary>
        public string BackupFolderName
        {   get { 
                    return this._backupFolderName; 
                }
            set {
                this._backupFolderName = value;
                InitBackupFolder();
            } 
        }

        private void InitBackupFolder()
        {
            try
            {
                DirectoryInfo di = new DirectoryInfo(this._backupFolderName);

                if (!di.Exists)
                    di.Create();
                                                
                
                //bmFs = new FileStream(Path.Combine(this._backupFolderName, "DBreezeBM.mg1"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _bufferSize);

                IsActive = true;
            }
            catch (Exception ex)
            {
                IsActive = false;                        
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.BACKUP_FOLDER_CREATE_FAILED, ex);
            }
        }

        internal BackupFileNamesParser BackupFNP = new BackupFileNamesParser();

        internal class BackupFileNamesParser
        {
            /// <summary>
            /// Helper for table file names transitions for backup
            /// </summary>
            /// <param name="fileName"></param>
            /// <returns></returns>
            internal ulong ParseFilename(string fileName)
            {
                switch (fileName)
                {
                    case "_DBreezeSchema":
                        return UInt64.MaxValue - 1;
                    case "_DBreezeTranJrnl":
                        return UInt64.MaxValue - 2;
                    default:
                        return Convert.ToUInt64(fileName);
                }

            }

            internal string ParseFilenameBack(ulong filenumber)
            {
                switch (filenumber)
                {
                    case (UInt64.MaxValue - 1):
                        return "_DBreezeSchema";
                    case (UInt64.MaxValue - 2):
                        return "_DBreezeTranJrnl";
                    default:
                        return filenumber.ToString();
                }

            }
        }


        bool wasWritten = false;

        /// <summary>
        /// Represents incremental backup of protocol 1
        /// </summary>
        /// <param name="fileNumber"></param>
        /// <param name="type">0 - table file, 1 - rollback file, 2 - rollbackhelper, 3 - recreate table file, 5 - removing complete table </param>
        /// <param name="pos"></param>
        /// <param name="data"></param>
        internal void WriteBackupElement(ulong fileNumber, byte type, long pos, byte[] data)
        {            
            lock (lock_ibp_fs)
            {
                this.GetFileStream();

                uint size = 0;
                byte[] toSave = null;

                switch (type)
                {
                    case 0:
                    case 1:

                        //8(fileNumber)+1(type)+8(position)+data.Length
                        size = Convert.ToUInt32(8 + 1 + 8 + data.Length);
                        toSave = size.To_4_bytes_array_BigEndian().ConcatMany(fileNumber.To_8_bytes_array_BigEndian(),
                            new byte[] { type },
                            pos.To_8_bytes_array_BigEndian(),
                            data);                       
                        break;
                    case 2:

                        //Now we save new information into rollback 
                        size = Convert.ToUInt32(8 + 1 + 8 + data.Length);
                        toSave = size.To_4_bytes_array_BigEndian().ConcatMany(fileNumber.To_8_bytes_array_BigEndian(),
                            new byte[] { 2 },
                            ((long)0).To_8_bytes_array_BigEndian(),
                            data);
                        break;
                    case 3: //recreate table file                 
                    case 5: //removing complete table

                        //8(fileNumber)+1(type)
                        size = 9;
                        toSave = size.To_4_bytes_array_BigEndian()
                            .ConcatMany(
                            fileNumber.To_8_bytes_array_BigEndian(),
                            new byte[] { type }
                            );
                        break;
                   

                    
                }

                wasWritten = true;
                fs.Write(toSave, 0, toSave.Length);
            }
           
            //Console.WriteLine(String.Format("{0}> FN: {1} - {2}; at {3} q {4}", writeTime.ToString("dd.MM.yyyy HH:mm:ss"), fileNumber, type.ToString(), pos.ToString(), 
            //    data.Length.ToString()));
        }

        public void Flush()
        {
            if (!wasWritten)
                return;

            lock (lock_ibp_fs)
            {                
                FSR.NET_Flush(fs);
                wasWritten = false;
            }
        }
        

        ulong currentBackup = 0;

        private void GetFileStream()
        {
            DateTime dtBase = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan ts = DateTime.UtcNow.Subtract(dtBase);
            uint jj = (uint)(ts.TotalMinutes / (double)_IncrementalBackupFileIntervalMin);
            DateTime backupTime = dtBase.AddMinutes(jj * _IncrementalBackupFileIntervalMin);
            ulong bupTime = Convert.ToUInt64(backupTime.ToString("yyyyMMddHHmmss"));

            if (fs == null)
            {

                currentBackup = bupTime;
                string bupFileName = String.Format("dbreeze_ibp_{0}.ibp", bupTime);
                string fullBackupFileName = Path.Combine(this._backupFolderName, bupFileName);

                //fs = new FileStream(fullBackupFileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,_bufferSize,FileOptions.WriteThrough);
                fs = new FileStream(fullBackupFileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _bufferSize,FileOptions.WriteThrough);
                fs.Position = fs.Length;
            }
            else
            {
                //we got open file stream, checking if it is of correct time, otherwise creating new file

                if (currentBackup != bupTime)
                {
                    
                    FSR.NET_Flush(fs);

                    fs.Close();
                    fs.Dispose();
                    currentBackup = bupTime;
                    string bupFileName = String.Format("dbreeze_ibp_{0}.ibp", bupTime);
                    string fullBackupFileName = Path.Combine(this._backupFolderName, bupFileName);
                    //fs = new FileStream(fullBackupFileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _bufferSize, FileOptions.WriteThrough);
                    fs = new FileStream(fullBackupFileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _bufferSize, FileOptions.WriteThrough);
                    fs.Position = fs.Length;
                }
            }

            //return fs;
        }

        //private void DateTimeFileNameTests()
        //{
        //    int _IncrementalBackupFileIntervalMin = 30;

        //    DateTime dtBase = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);
           
        //    DateTime now = DateTime.UtcNow;

        //    //WinterTime
        //    // now = new DateTime(2012, 10, 27, 22, 0, 0, DateTimeKind.Utc);
        //    //SummerTime
        //    now = new DateTime(2012, 3, 24, 22, 0, 0, DateTimeKind.Utc);

        //    DateTime xnow = new DateTime();
        //    //test
        //    for (int i = 0; i < 20; i++)
        //    {
        //        xnow = now.AddHours(i).AddMinutes(15);
                
        //        TimeSpan ts = xnow.Subtract(dtBase);

        //        uint jj = (uint)(ts.TotalMinutes / (double)_IncrementalBackupFileIntervalMin);

        //        DateTime ndt = dtBase.AddMinutes(jj * _IncrementalBackupFileIntervalMin);

        //        Console.WriteLine("h: {0}; xnow: {1}; xnowloc: {2}; ndt: {3}", i, xnow.ToString("dd.MM.yyyy HH:mm:ss"),
        //            xnow.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
        //            ndt.ToString("dd.MM.yyyy HH:mm:ss")
        //            );
        //    }


        //    return;
        //}


       

    }
}
