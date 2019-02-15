/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using DBreeze.Utils;
using DBreeze.Exceptions;

namespace DBreeze.Storage
{
    /// <summary>
    /// DBreeze random and sequential disk IO buffers implementation.
    /// Specially designed for DBreeze specific storage format.
    /// Not for common usage.
    /// </summary>
    internal class FSR : IStorage
    {        
        //!!try catches can be taken away from reads and writes, when procs are fully balanced
        
        #region "Variables"

        /// <summary>
        /// Indicates subsystem vitality
        /// </summary>
        public bool IsOperable = true;
        /// <summary>
        /// Random buffer
        /// </summary>
        Dictionary<long, byte[]> _randBuf = new Dictionary<long, byte[]>();

        /// <summary>
        /// Record in rollback is characterized with 
        /// </summary>
        class r
        {
            /// <summary>
            /// offset in rollback file
            /// </summary>
            public long o { get; set; }
            /// <summary>
            /// Length in rollback file
            /// </summary>
            public int l { get; set; }
        }

        /// <summary>
        /// Rollback cache
        /// Key is offset in data file, value is corresponding offset and length in rollback file
        /// </summary>
        Dictionary<long, r> _rollbackCache = new Dictionary<long, r>();

        /// <summary>
        /// Random buffer maximal size before flush
        /// </summary>
       int maxRandomBufferSize = 3000000; //Random buffer size before flush
       int maxRandomElementsCount = 500; //Random buffer maximal quantity of elements in buffer before flush

       int _seqBufCapacity = 1048576;
        MemoryStorage _seqBuf = new MemoryStorage(1024, 1024 * 100, MemoryStorage.eMemoryExpandStartegy.FIXED_LENGTH_INCREASE);     
       
        int usedBufferSize = 0; //Used buffer size before flush
        /// <summary>
        /// Rollback file re-creation after initialization
        /// </summary>
        public int MaxRollbackFileSize = 131072;

        string _fileName = String.Empty;
        ulong ulFileName = 0;   //ulong file name, for backup purposes
        object lock_fs = new object();
        int _fileStreamBufferSize = 8192;

        FileStream _fsData = null;
        FileStream _fsRollback = null;
        FileStream _fsRollbackHelper = null;
        /// <summary>
        /// Pointer to the end of file, before current commit
        /// </summary>
        long eofData = 0;
        long eofRollback = 0;
        long fsLength = 0;

        TrieSettings _trieSettings = null;
        ushort DefaultPointerLen = 0;
        DBreezeConfiguration _configuration = null;

        bool _backupIsActive = false;

        /// <summary>
        /// DateTime when file was initialized. Is remembered by LTrieRow, based on this file.
        /// If file is change after RestoreTableFromTheOtherTable or RecreateFiles,
        /// LTrieRow will have different version and will return exception.
        /// </summary>
        DateTime _storageFixTime = DateTime.UtcNow;

        #endregion

        public FSR(string fileName, TrieSettings trieSettings, DBreezeConfiguration configuration)
        {
            this._fileName = fileName;
            this._configuration = configuration;
            this._trieSettings = trieSettings;
            DefaultPointerLen = this._trieSettings.POINTER_LENGTH;

            _backupIsActive = this._configuration.Backup.IsActive;

            //Transforms fileName into ulong digit
            ulFileName = this._configuration.Backup.BackupFNP.ParseFilename(Path.GetFileNameWithoutExtension(this._fileName));

            InitFiles();
        }

        /// <summary>
        /// Physical length of the storage file
        /// </summary>
        public long Length
        {
            get { return this.eofData; }
        }

        /// <summary>
        /// Returns time of file initiation, ead remarks on 
        /// </summary>
        public DateTime StorageFixTime
        {
            get { return _storageFixTime; }
        }

        /// <summary>
        /// 
        /// </summary>
        public TrieSettings TrieSettings
        {
            get { return _trieSettings; }
        }

        /// <summary>
        /// 
        /// </summary>
        public DBreezeConfiguration DbreezeConfiguration
        {
            get { return this._configuration; }
        }

        /// <summary>
        /// 
        /// </summary>
        public string Table_FileName
        {
            get { return this._fileName; }
        }

        /// <summary>
        /// 
        /// </summary>
        public void Table_Dispose()
        {
            lock (lock_fs)
            {
                if (_fsData != null)
                {                    
                    _fsData.Dispose();
                    _fsData = null;
                }

                if (_fsRollback != null)
                {                    
                    _fsRollback.Dispose();
                    _fsRollback = null;
                }

                if (_fsRollbackHelper != null)
                {                    
                    _fsRollbackHelper.Dispose();
                    _fsRollbackHelper = null;
                }

                _seqBuf.Dispose();
                _rollbackCache.Clear();
            }
                       
        }

        #region Initialization

        private void InitFiles()
        {
            //Creates filestreams and rollbacks, restores rollback to the initial file, if necessary
          
            try
            {
                this._fsData = new FileStream(this._fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);
                this._fsRollback = new FileStream(this._fileName + ".rol", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);
                this._fsRollbackHelper = new FileStream(this._fileName + ".rhp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);

                //!!!!We dont have this value in root yet, could have and economize tail of the file in case if rollback occured

                if (this._fsData.Length == 0)
                {
                    //Writing initial root data

                    _fsData.Position = 0;
                    _fsData.Write(new byte[this._trieSettings.ROOT_SIZE], 0, this._trieSettings.ROOT_SIZE);


                    if (_backupIsActive)
                    {
                        this._configuration.Backup.WriteBackupElement(ulFileName, 0, 0, new byte[this._trieSettings.ROOT_SIZE]);
                    }

                    //no flush here
                }

                eofData = this._fsData.Length;
                fsLength = this._fsData.Length;

                //Check is .rhp is empty add 0 pointer
                if (this._fsRollbackHelper.Length == 0)
                {
                    //no sense to write here

                    //_fsRollbackHelper.Position = 0;
                    //_fsRollbackHelper.Write(eofRollback.To_8_bytes_array_BigEndian(), 0, 8);

                    //NET_Flush(_fsRollbackHelper);
                }
                else
                {
                    InitRollback();
                }


                _storageFixTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                IsOperable = false;
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DB_IS_NOT_OPERABLE, "FSR INIT FAILED: " + this._fileName, ex);
            }

        }




        #endregion

        #region InitRollback

        private void InitRollback()
        {
            byte[] btWork = new byte[8];
            _fsRollbackHelper.Position = 0;
            _fsRollbackHelper.Read(btWork, 0, 8);
            eofRollback = btWork.To_Int64_BigEndian();

            if (eofRollback == 0)
            {
                if (this._fsRollback.Length >= MaxRollbackFileSize)
                {
                    this._fsRollback.Dispose();
                    File.Delete(this._fileName + ".rol");
                    this._fsRollback = new FileStream(this._fileName + ".rol", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);
                    
                    //no sense to do anything with backup
                }

                return;
            }

            //!!!Check if data file is empty write first root 64 bytes, ??? Where it must stay after rollback restoration???
             

            //Restoring rollback
            RestoreInitRollback();

            //Checking if we can recreate rollback file
            if (this._fsRollback.Length >= MaxRollbackFileSize)
            {                
                this._fsRollback.Dispose();

                File.Delete(this._fileName + ".rol");
                this._fsRollback = new FileStream(this._fileName + ".rol", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);

                //no sense to do anything with backup
            }

            eofRollback = 0;
            _fsRollbackHelper.Position = 0;
            _fsRollbackHelper.Write(eofRollback.To_8_bytes_array_BigEndian(), 0, 8);

            NET_Flush(_fsRollbackHelper);

            //try
            //{

            //}
            //catch (Exception ex)
            //{
            //    IsOperable = false;
            //    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.RESTORE_ROLLBACK_DATA_FAILED, this._fileName, ex);
            //}

        }


        /// <summary>
        /// 
        /// </summary>
        void RestoreInitRollback()
        {
            //100KB chunk
            byte[] rba = new byte[100 * 1024];
            int readOut = 0;
            long fsPosition = 0;

            do
            {
                this._fsRollback.Position = fsPosition;
                readOut = this._fsRollback.Read(rba, 0, rba.Length);


                /*************************************************************  Support dynamic size ************************/
                if ((fsPosition + readOut) > eofRollback)
                {
                    readOut = (int)(eofRollback - fsPosition);
                }
                /************************************************/

                fsPosition += readOut;

                if (readOut == 0)
                    break;
                if (readOut < rba.Length)
                    ParseRollBackFile(rba.Substring(0, readOut));
                else
                    ParseRollBackFile(rba);

            } while (true);

            NET_Flush(this._fsData);

        }

        byte[] protocolData = null;
        private bool ProtocolStarted = false;
        private byte ProtocolNumber = 0;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rbd"></param>
        private void ParseRollBackFile(byte[] rbd)
        {
            byte[] left = null;

            if (!ProtocolStarted)
            {
                ProtocolNumber = rbd[0];

                switch (ProtocolNumber)
                {
                    case 1:
                        ProtocolNumber = 1;
                        ProtocolStarted = true;

                        left = DoProtocol1(rbd);
                        if (left != null)
                        {
                            ParseRollBackFile(left);
                        }
                        break;
                    default:
                        throw new Exception("ROLLBACK.ParseRollBackFile: unknown protocol, p1");
                }
            }
            else
            {
                switch (ProtocolNumber)
                {
                    case 1:
                        left = DoProtocol1(rbd);
                        if (left != null)
                        {
                            ParseRollBackFile(left);
                        }
                        break;
                    default:
                        throw new Exception("ROLLBACK.ParseRollBackFile: unknown protocol, p2");
                }
            }
        }



        private byte[] DoProtocol1(byte[] rbd)
        {//Returns left bytes
            byte[] left = null;

            //Protocol: type of Rollback record - 1 byte; Offset - size is trie.Storage.TreeSettings.POINTER_LENGTH (Default pointer length); data length - 4 bytes; data

            //We must read out full protocol value into memory and then start to overwrite original file.
            //If full value cant be read we can think that this part of protocol is corrupted and definitely not written to the original file,
            //we can stop rollback process.

            //Reading 
            protocolData = protocolData.Concat(rbd);

            if (protocolData.Length < (1 + DefaultPointerLen + 4))
                return null;

            //Getting data length
            int len = (int)(protocolData.Substring(1 + DefaultPointerLen, 4).To_UInt32_BigEndian());
            byte[] data = null;


            if (protocolData.Length >= len + 1 + DefaultPointerLen + 4)
            {
                data = protocolData.Substring(1 + DefaultPointerLen + 4, len);
                left = protocolData.Substring(len + 1 + DefaultPointerLen + 4, protocolData.Length);


                byte[] offset = protocolData.Substring(1, DefaultPointerLen);

                //Writing data back
                //Console.WriteLine("Of: {0}; DL: {1}", offset.ToBytesString(""), data.Length.To_4_bytes_array_BigEndian().ToBytesString(""));

                this._fsData.Position = (long)offset.DynamicLength_To_UInt64_BigEndian();
                this._fsData.Write(data, 0, data.Length);
            }
            else
                return null;

            //if finished
            protocolData = null;
            ProtocolStarted = false;
            return left;
        }
        #endregion

        #region "NET FLUSH"
#if NET40
        public static void NET_Flush(FileStream mfs)
        {
            mfs.Flush(true);
        }
#else

        [System.Runtime.InteropServices.DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool FlushFileBuffers(IntPtr hFile);

        public static void NET_Flush(FileStream mfs)
        {
            mfs.Flush();
            IntPtr handle = mfs.SafeFileHandle.DangerousGetHandle();

            if (!FlushFileBuffers(handle))
                throw new System.ComponentModel.Win32Exception();
        }
#endif
        #endregion

        #region "RestoreTableFromTheOtherTable"

        /// <summary>
        /// 
        /// </summary>
        /// <param name="newTableFullPath"></param>
        public void RestoreTableFromTheOtherTable(string newTableFullPath)
        {
            lock (lock_fs)
            {
                if (_fsData != null)
                {                    
                    _fsData.Dispose();
                    _fsData = null;
                }

                if (_fsRollback != null)
                {                    
                    _fsRollback.Dispose();
                    _fsRollback = null;
                }

                if (_fsRollbackHelper != null)
                {                    
                    _fsRollbackHelper.Dispose();
                    _fsRollbackHelper = null;
                }

                _randBuf.Clear();
                _rollbackCache.Clear();
                usedBufferSize = 0;
                eofRollback = 0;
                eofData = 0;
                fsLength = 0;
                _seqBuf.Clear(true);
                

                File.Delete(this._fileName);
                File.Delete(this._fileName + ".rol");
                File.Delete(this._fileName + ".rhp");

                if (File.Exists(newTableFullPath))
                    File.Move(newTableFullPath, this._fileName);

                if(File.Exists(newTableFullPath + ".rol"))
                    File.Move(newTableFullPath + ".rol", this._fileName + ".rol");

                if (File.Exists(newTableFullPath + ".rhp"))
                    File.Move(newTableFullPath + ".rhp", this._fileName + ".rhp");

                InitFiles();

            }
        }
        #endregion

        #region "Recreate Files"

        /// <summary>
        /// 
        /// </summary>
        public void RecreateFiles()
        {
            lock (lock_fs)
            {
                if (_fsData != null)
                {                    
                    _fsData.Dispose();
                    _fsData = null;
                }

                if (_fsRollback != null)
                {                    
                    _fsRollback.Dispose();
                    _fsRollback = null;
                }

                if (_fsRollbackHelper != null)
                {                    
                    _fsRollbackHelper.Dispose();
                    _fsRollbackHelper = null;
                }

                _randBuf.Clear();
                _rollbackCache.Clear();
                usedBufferSize = 0;
                eofRollback = 0;
                eofData = 0;
                fsLength = 0;
                _seqBuf.Clear(true);

                File.Delete(this._fileName);
                File.Delete(this._fileName + ".rol");
                File.Delete(this._fileName + ".rhp");
                
                InitFiles();

            }
        }
        #endregion



        /// <summary>
        /// Must be called from lock_fs
        /// </summary>
        void FlushSequentialBuffer()
        {

            if (_seqBuf.EOF == 0)
                return;

            //long pos = _fsData.Length;
            long pos = fsLength;
            _fsData.Position = pos;
            _fsData.Write(_seqBuf.RawBuffer, 0, _seqBuf.EOF);

            fsLength += _seqBuf.EOF;

            if (_backupIsActive)
            {               
                byte[] btWork = new byte[_seqBuf.EOF];
                Buffer.BlockCopy(_seqBuf.RawBuffer, 0, btWork, 0, btWork.Length);
                this._configuration.Backup.WriteBackupElement(ulFileName, 0, pos, btWork);
            }

            _seqBuf.Clear(true);
        }

        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public byte[] Table_WriteToTheEnd(byte[] data)
        {
            
            long position = 0;

            /* Emulation of the direct write to the disk without sequential cache */

            //_fsData.Position = position = _fsData.Length;
            //_fsData.Write(data, 0, data.Length);

            //return ((ulong)position).To_8_bytes_array_BigEndian().Substring(8 - DefaultPointerLen, DefaultPointerLen);

            /**************************************************************/

           

            lock (lock_fs)
            {
                //case when incoming data bigger then buffer, we clean buffer and write data directly to the disk

                if (data.Length > _seqBufCapacity)
                {
                    FlushSequentialBuffer();
                    //_fsData.Position = position = _fsData.Length;
                    _fsData.Position = position = fsLength;
                    _fsData.Write(data, 0, data.Length);

                    fsLength += data.Length;

                    return ((ulong)position).To_8_bytes_array_BigEndian().Substring(8 - DefaultPointerLen, DefaultPointerLen);
                }
                
                //Time to clean buffer
                if (_seqBuf.EOF + data.Length > _seqBufCapacity)
                {
                    FlushSequentialBuffer();
                }

                //Writing into buffer

                //position = _fsData.Length + _seqBuf.EOF;
                position = fsLength + _seqBuf.EOF;

                _seqBuf.Write_ToTheEnd(data);
                
                //eofData (ptr to the end of file before current commit) will be increased only after flush

            }
                        
            return ((ulong)position).To_8_bytes_array_BigEndian().Substring(8 - DefaultPointerLen, DefaultPointerLen);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="data"></param>
        public void Table_WriteByOffset(byte[] offset, byte[] data)
        {
            Table_WriteByOffset((long)offset.DynamicLength_To_UInt64_BigEndian(), data);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="data"></param>
        public void Table_WriteByOffset(long offset, byte[] data)
        {

            /* Emulation of the direct save without random cache */
            //lock (lock_fs)
            //{
            //    FlushSequentialBuffer();
            //    _fsData.Position = offset;
            //    _fsData.Write(data, 0, data.Length);
            //}

            //Console.WriteLine("yeah");
            //return;
            /******************************************************/


            //DB RULE1. We cant update and go out of the end of file
            //!! ALL throw new Exception must be taken away after testS
            //!! This is a cutted implementation for DBreeze we dont take care buffer elements overlapping (start+len U some elements -> should be not possible)
            //overwriting partly file and partly sequential buffer is not allowed

            if (data == null || data.Length == 0)
            {
                throw new Exception("FSR.WriteByOffset: data == null || data.Length == 0");
            }

            lock (lock_fs)
            {

                //if (offset >= _fsData.Length)
                if (offset >= fsLength)
                {
                    //Overwriting sequential buffer
                    //_seqBuf.Write_ByOffset(Convert.ToInt32(offset - _fsData.Length), data);                    
                    _seqBuf.Write_ByOffset(Convert.ToInt32(offset - fsLength), data);
                    return;
                }

                //if (offset < _fsData.Length && offset + data.Length > _fsData.Length)
                if (offset < fsLength && offset + data.Length > fsLength)
                {
                    throw new Exception("FSR.WriteByOffset: offset < _fsData.Length && offset + data.Length > _fsData.Length");
                }

                //if (offset + data.Length > (_fsData.Length + _seqBuf.EOF))
                if(offset + data.Length > (fsLength + _seqBuf.EOF))
                {
                    //DB RULE1. We cant update and go out of the end of file. Only if we write into empty file root in the beginning
                    throw new Exception("FSR.WriteByOffset: offset + data.Length > (_fsData.Length + seqEOF)");
                }

                byte[] inBuf = null;
                if (_randBuf.TryGetValue(offset, out inBuf))
                {
                    if (inBuf.Length != data.Length)
                    {
                        //OLD solution
                        //it means we overwrite second time the same position with different length of data - what is not allowed
                        //throw new Exception("FSR.WriteByOffset: inBuf.Length != data.Length");

                        //Solution from 20140425
                        //we just overwrite offset value with the new data
                    }

                    //setting new value for such offset
                    _randBuf[offset] = data;
                }
                else
                {
                    //We put data to the buffer first and flush it if buffer > allowed space. We dont take care if data is bigger then buffer.
                    //In any case first we put it to the buffer 
                    _randBuf.Add(offset, data);
                    usedBufferSize += data.Length;
                }

                //if we are able to store data into buffer lets do it                
                if (usedBufferSize >= maxRandomBufferSize || _randBuf.Count() > maxRandomElementsCount)
                    FlushRandomBuffer();
            }
        }
        
        /// <summary>
        /// Is called only from lock_fs and must be finished by calling NET_Flush
        /// </summary>     
        void FlushRandomBuffer()
        {
            if (_randBuf.Count() == 0)
            {
                return;
            }

            //First we write all data into rollback file and helper, calling flush on rollback
            //then updating data of data file but dont call update
            //clearing random buffer

            //Creating rollback header           
            byte[] offset = null;
            byte[] btRoll = null;

            bool flushRollback = false;
           
            //first loop for saving rollback data
            foreach (var de in _randBuf.OrderBy(r => r.Key))               
            {
                offset = ((ulong)de.Key).To_8_bytes_array_BigEndian().Substring(8 - DefaultPointerLen, DefaultPointerLen);

                if (_rollbackCache.ContainsKey(de.Key))
                    continue;

                //Reading from dataFile values which must be rolled back
                btRoll = new byte[de.Value.Length];

                _fsData.Position = de.Key;
                _fsData.Read(btRoll, 0, btRoll.Length);
                //Console.WriteLine("2;{0};{1}", de.Key, ((btRoll == null) ? -1 : btRoll.Length));

                //Forming protocol for rollback
                btRoll = new byte[] { 1 }
                           .ConcatMany(
                           offset,
                           ((uint)btRoll.Length).To_4_bytes_array_BigEndian(),
                           btRoll
                           );

                //Writing rollback
                _fsRollback.Position = eofRollback;
                _fsRollback.Write(btRoll, 0, btRoll.Length);

                if (_backupIsActive)
                {
                    this._configuration.Backup.WriteBackupElement(ulFileName, 1, eofRollback, btRoll);
                }

                _rollbackCache.Add(de.Key, new r { o = eofRollback + 1 + offset.Length + 4, l = de.Value.Length });         

                //increasing eof rollback file
                eofRollback += btRoll.Length;

                flushRollback = true;
            }

            if (flushRollback)
            {

                //Flushing rollback
                NET_Flush(_fsRollback);

                //Writing into helper
                _fsRollbackHelper.Position = 0;
                _fsRollbackHelper.Write(eofRollback.To_8_bytes_array_BigEndian(), 0, 8);

                //Flushing rollback helper
                NET_Flush(_fsRollbackHelper);


                if (_backupIsActive)
                {
                    this._configuration.Backup.WriteBackupElement(ulFileName, 2, 0, eofRollback.To_8_bytes_array_BigEndian());
                    this._configuration.Backup.Flush();
                }
            }

            //second loop for saving data
            foreach (var de in _randBuf.OrderBy(r => r.Key))      //sorting can mean nothing here, only takes extra time
            {
                _fsData.Position = de.Key;
                _fsData.Write(de.Value, 0, de.Value.Length);

                if (de.Key + de.Value.Length > fsLength)
                    fsLength = de.Key + de.Value.Length;

                if (_backupIsActive)
                {
                    this._configuration.Backup.WriteBackupElement(ulFileName, 0, de.Key, de.Value);
                }
            }

            //No flush of data file, it will be done on Flush()                        

            _randBuf.Clear();
            usedBufferSize = 0;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="useCache"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public byte[] Table_Read(bool useCache, byte[] offset, int count)
        {
            return Table_Read(useCache, (long)offset.DynamicLength_To_UInt64_BigEndian(), count);
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="useCache"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public byte[] Table_Read(bool useCache, long offset, int count)
        {
            byte[] res = null;

            lock (lock_fs)
            {
                if (!useCache)
                {
                    //WRITER

                    //We read exactly what is already saved, without rollback.
                    //all data must be either in file or in buffer
                    //we must form resulting byte[] 

                    //Buffer
                    //Finding first element which is smaller or equal then offset
                    List<long> bufKeys = new List<long>();

                    if (_randBuf.Count() > 0)
                    {
                        var qkvp = _randBuf.OrderByDescending(r => r.Key).Where(r => r.Key < offset).Take(1).Where(r => (r.Key + r.Value.Length - 1) >= offset);


                        if (qkvp.Count() > 0)
                        {
                            bufKeys.Add(qkvp.FirstOrDefault().Key);
                        }

                        foreach (var kvp in _randBuf.OrderBy(r => r.Key).Where(r => r.Key >= offset && r.Key < (offset + count)))
                        {
                            bufKeys.Add(kvp.Key);
                        }
                    }

                    //reading full byte[] from original file and putting on top keys
                    //We use full length of the file
                    //if (offset + count > _fsData.Length + _seqBuf.EOF)
                    //    res = new byte[_fsData.Length + _seqBuf.EOF - offset];
                    if (offset + count > fsLength + _seqBuf.EOF)
                        res = new byte[fsLength + _seqBuf.EOF - offset];
                    else
                        res = new byte[count];

                    byte[] btWork = null;

                    //if (offset < _fsData.Length)
                    if (offset < fsLength)
                    {
                        //Starting reading from file
                        _fsData.Position = offset;

                        if (offset + res.Length <= _fsData.Length)
                        {
                            //must be taken completely from file
                            _fsData.Read(res, 0, res.Length);
                            //Console.WriteLine("3;{0};{1}", offset, ((res == null) ? -1 : res.Length));
                        }
                        else
                        {
                            //partly from file, partly from sequential cache
                            //int v1 = Convert.ToInt32(_fsData.Length - offset);
                            int v1 = Convert.ToInt32(fsLength - offset);
                            _fsData.Read(res, 0, v1);
                            //Console.WriteLine("4;{0};{1}", offset, ((res == null) ? -1 : res.Length));
                            Buffer.BlockCopy(_seqBuf.RawBuffer, 0, res, v1, res.Length - v1);
                        }
                    }
                    else
                    {
                        //!!! threat if seqBuf is empty, should not happen thou

                        //completely taken from seqbuf
                        //Buffer.BlockCopy(_seqBuf.RawBuffer, Convert.ToInt32(offset - _fsData.Length), res, 0, res.Length);
                        Buffer.BlockCopy(_seqBuf.RawBuffer, Convert.ToInt32(offset - fsLength), res, 0, res.Length);
                    }


                    //putting on top
                    foreach (var bk in bufKeys)
                    {
                        if (offset + res.Length <= bk)
                            continue;

                        btWork = _randBuf[bk];

                        bool cut = false;
                        int start = 0;
                        int stop = btWork.Length;

                        if (bk < offset)
                        {
                            cut = true;
                            start = Convert.ToInt32(offset - bk);
                        }

                        if ((offset + count) < (bk + btWork.Length))
                        {
                            cut = true;
                            stop = Convert.ToInt32(offset + count - bk);
                        }

                        if (cut)
                        {
                            byte[] tmp = new byte[stop - start];
                            Buffer.BlockCopy(btWork, start, tmp, 0, tmp.Length);
                            btWork = tmp;
                        }

                        Buffer.BlockCopy(btWork, 0, res, (start > 0) ? 0 : Convert.ToInt32(bk - offset), btWork.Length);
                    }


                }
                else
                {
                    //READER

                    //we must use rollback file.
                    //We can read only up to commited file lengh eofData

                    List<long> bufKeys = new List<long>();
                                        
                    if (_rollbackCache.Count() > 0)
                    {
                        var qkvp = _rollbackCache.OrderByDescending(r => r.Key).Where(r => r.Key < offset).Take(1).Where(r => (r.Key + r.Value.l - 1) >= offset);


                        if (qkvp.Count() > 0)
                        {
                            bufKeys.Add(qkvp.FirstOrDefault().Key);
                        }

                        foreach (var kvp in _rollbackCache.OrderBy(r => r.Key).Where(r => r.Key >= offset && r.Key < (offset + count)))
                        {
                            bufKeys.Add(kvp.Key);
                        }
                    }

                    //reading full byte[] from original file and putting on top keys

                    /*
                         * Transaction with minimum 2 tables. T2 is inserted, reference to T2 KVP is taken, then this reference is saved into T1.
                         * Commit().
                         * Commit calls TransactionalCommit for every table sequentially. First it meets table T1, then T2.
                         * In both tables TransactionalCommit procedures are successfull.
                         * then Commit procedure for each table calls TransactionalCommitIsFinished (this proc will clear rollback refs and moves eofData for every table).
                         * First encounters T1 and only then T2. 
                         * ....Somewhere here (between calling T1 and T2 TransactionalCommitIsFinished) starts a parallel thread. 
                         * After T1 TransactionalCommitIsFinished our parallel thread P1 reads data from T1, 
                         * and gets SelectDirect reference to T2 KVP. Then tries to read from not yet TransactionalCommitIsFinished T2.
                         * and for T2 happens: eofData < offset
                         * 
                         * To avoid such specific case we use for calculation this._fsData.Length instead of eofData in case if (eofData < offset && TransactionalCommitIsStarted)            
                         * 19.07.2013 10:25
                        */

                    //WAS
                    //if (offset + count > eofData)
                    //    res = new byte[eofData - offset];
                    //else
                    //    res = new byte[count];
                    ///////

                    //NOW
                    if (offset + count > eofData)
                    {
                        if (eofData < offset && TransactionalCommitIsStarted)   //NOT FINISHED multi-table COMMIT. SelectDirect case
                        {
                            //Probably not finished transaction and SelectDirect case. We return value,
                            //because at this momont all transaction table have successfully gone through TransactionalCommit() procedure.

                            //if (offset + count > this._fsData.Length)
                            //{
                            //    res = new byte[this._fsData.Length - offset];
                            //}
                            if (offset + count > fsLength)
                            {
                                //res = new byte[this._fsData.Length - offset];
                                res = new byte[fsLength - offset];
                            }
                            else
                            {
                                res = new byte[count];
                            }
                        }
                        else
                        {
                            res = new byte[eofData - offset];
                        }
                    }
                    else
                        res = new byte[count];
                    ///////

                    _fsData.Position = offset;
                    _fsData.Read(res, 0, res.Length);
                    //Console.WriteLine("1;{0};{1}", offset, ((res == null) ? -1 : res.Length));

                    byte[] btWork = null;
                    r rb = null;
                    //putting on top
                    foreach (var bk in bufKeys)
                    {
                        if (offset + res.Length <= bk)
                            continue;

                        rb = _rollbackCache[bk];
                        //reading from rollback
                        btWork = new byte[rb.l];

                        _fsRollback.Position = rb.o;
                        _fsRollback.Read(btWork, 0, btWork.Length);

                        bool cut = false;
                        int start = 0;
                        int stop = btWork.Length;

                        if (bk < offset)
                        {
                            cut = true;
                            start = Convert.ToInt32(offset - bk);
                        }

                        if ((offset + count) < (bk + btWork.Length))
                        {
                            cut = true;
                            stop = Convert.ToInt32(offset + count - bk);
                        }

                        if (cut)
                        {
                            byte[] tmp = new byte[stop - start];
                            Buffer.BlockCopy(btWork, start, tmp, 0, tmp.Length);
                            btWork = tmp;
                        }

                        Buffer.BlockCopy(btWork, 0, res, (start > 0) ? 0 : Convert.ToInt32(bk - offset), btWork.Length);
                    }

                }
            }

            return res;
        }

       

        /// <summary>
        /// Cleans all buffers and flushes data to the disk
        /// </summary>
        public void Commit()
        {
            lock (lock_fs)
            {
                FlushSequentialBuffer();
                FlushRandomBuffer();

                NET_Flush(_fsData);

                if (_backupIsActive)
                {
                    this._configuration.Backup.Flush();
                }

                if (eofRollback != 0)
                {
                    //Finalizing rollback helper

                    eofRollback = 0;
                    _fsRollbackHelper.Position = 0;
                    _fsRollbackHelper.Write(eofRollback.To_8_bytes_array_BigEndian(), 0, 8);

                    NET_Flush(_fsRollbackHelper);

                    if (_backupIsActive)
                    {
                        this._configuration.Backup.WriteBackupElement(ulFileName, 2, 0, eofRollback.To_8_bytes_array_BigEndian());
                        this._configuration.Backup.Flush();
                    }
                }

                _rollbackCache.Clear();

                //eofData = this._fsData.Length;
                eofData = fsLength;

            }
        }

        /// <summary>
        /// Transactional Commit is started
        /// </summary>
        bool TransactionalCommitIsStarted = false;

        /// <summary>
        /// 
        /// </summary>
        public void TransactionalCommit()
        {
            lock (lock_fs)
            {
                FlushSequentialBuffer();
                FlushRandomBuffer();

                NET_Flush(_fsData);

                TransactionalCommitIsStarted = true;
            }

            if (_backupIsActive)
            {
                this._configuration.Backup.Flush();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void TransactionalCommitIsFinished()
        {
            lock (lock_fs)
            {
                if (eofRollback != 0)
                {
                    //Finalizing rollback helper

                    eofRollback = 0;
                    _fsRollbackHelper.Position = 0;
                    _fsRollbackHelper.Write(eofRollback.To_8_bytes_array_BigEndian(), 0, 8);

                    NET_Flush(_fsRollbackHelper);

                    if (_backupIsActive)
                    {
                        this._configuration.Backup.WriteBackupElement(ulFileName, 2, 0, eofRollback.To_8_bytes_array_BigEndian());
                        this._configuration.Backup.Flush();
                    }
                }

                _rollbackCache.Clear();

                //eofData = this._fsData.Length;
                eofData = fsLength;

                TransactionalCommitIsStarted = false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void TransactionalRollback()
        {
            Rollback();
            TransactionalCommitIsStarted = false;
        }

        /// <summary>
        /// Standard and transactional rollback
        /// </summary>
        public void Rollback()
        {
            try
            {
                lock (lock_fs)
                {
                    //Clearing random buffer
                    if (_randBuf.Count() != 0)
                    {
                        usedBufferSize = 0;
                        _randBuf.Clear();
                    }

                    //Restoring Rollback records
                    byte[] btWork = null;

                    if (_rollbackCache.Count() > 0)
                    {

                        foreach (var rb in _rollbackCache)
                        {
                            btWork = new byte[rb.Value.l];
                            _fsRollback.Position = rb.Value.o;
                            _fsRollback.Read(btWork, 0, btWork.Length);

                            _fsData.Position = rb.Key;
                            _fsData.Write(btWork, 0, btWork.Length);

                            if (rb.Key + btWork.Length > fsLength)
                                fsLength = rb.Key + btWork.Length;

                            if (_backupIsActive)
                            {
                                this._configuration.Backup.WriteBackupElement(ulFileName, 0, rb.Key, btWork);
                            }
                        }

                        NET_Flush(_fsData);

                         if (_backupIsActive)
                        {
                            this._configuration.Backup.Flush();
                        }

                        //Restoring rhp
                        eofRollback = 0;
                        _fsRollbackHelper.Position = 0;
                        _fsRollbackHelper.Write(eofRollback.To_8_bytes_array_BigEndian(), 0, 8);

                        NET_Flush(_fsRollbackHelper);

                        if (_backupIsActive)
                        {
                            this._configuration.Backup.WriteBackupElement(ulFileName, 2, 0, eofRollback.To_8_bytes_array_BigEndian());
                            this._configuration.Backup.Flush();
                        }

                        //Clearing rollbackCache
                        _rollbackCache.Clear();

                    }

                    //we dont move eofData, space can be re-used up to next restart (may be root can have this info in next protocols)
                    //eofData = this._fsData.Length;
                }
            }
            catch (Exception ex)
            {
                IsOperable = false;
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.RESTORE_ROLLBACK_DATA_FAILED, this._fileName, ex);
            }


        }











        
    }
}
