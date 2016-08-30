/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.Utils;

namespace DBreeze.Storage
{
    /// <summary>
    /// Storage layer
    /// </summary>
    public class StorageLayer : IStorage
    {
      
        IStorage _tableStorage = null;        
        
        public StorageLayer(string fileName, TrieSettings trieSettings, DBreezeConfiguration configuration)
        {          
            if (trieSettings.StorageWasOverriden)
            {
                switch (trieSettings.AlternativeTableStorageType)
                {
                    case DBreezeConfiguration.eStorage.DISK:

                        _tableStorage = (IStorage) new FSR(fileName, trieSettings, configuration);

                        break;
                    case DBreezeConfiguration.eStorage.MEMORY:

                        _tableStorage = (IStorage)new MSR(fileName, trieSettings, configuration);

                        break;
                    case DBreezeConfiguration.eStorage.RemoteInstance:

                        _tableStorage = (IStorage)new RISR(fileName, trieSettings, configuration);

                        break;
                }
            }
            else
            {
                switch (configuration.Storage)
                {
                    case DBreezeConfiguration.eStorage.DISK:

                        _tableStorage = (IStorage)new FSR(fileName, trieSettings, configuration);

                        break;
                    case DBreezeConfiguration.eStorage.MEMORY:

                        _tableStorage = (IStorage)new MSR(fileName, trieSettings, configuration);

                        break;
                    case DBreezeConfiguration.eStorage.RemoteInstance:

                        _tableStorage = (IStorage)new RISR(fileName, trieSettings, configuration);

                        break;
                }
            }

        }

        public TrieSettings TrieSettings
        {
            get { return _tableStorage.TrieSettings; }
        }

        public DBreezeConfiguration DbreezeConfiguration
        {
            get { return _tableStorage.DbreezeConfiguration; }
        }

        public string Table_FileName
        {
            get { return _tableStorage.Table_FileName; }
        }

        public void Table_Dispose()
        {
            _tableStorage.Table_Dispose();
        }

        public void Commit()
        {
            _tableStorage.Commit();
        }

        public void Rollback()
        {
            _tableStorage.Rollback();
        }

        public void TransactionalCommit()
        {
            _tableStorage.TransactionalCommit();
        }

        public void TransactionalCommitIsFinished()
        {
            _tableStorage.TransactionalCommitIsFinished();
        }

        public void TransactionalRollback()
        {
            _tableStorage.TransactionalRollback();
        }

        public void RecreateFiles()
        {
            _tableStorage.RecreateFiles();
        }

        public void Table_WriteByOffset(long offset, byte[] data)
        {
            _tableStorage.Table_WriteByOffset(offset, data);
        }

        public void Table_WriteByOffset(byte[] offset, byte[] data)
        {
            _tableStorage.Table_WriteByOffset(offset, data);
        }

        public byte[] Table_WriteToTheEnd(byte[] data)
        {
            return _tableStorage.Table_WriteToTheEnd(data);
        }

        public byte[] Table_Read(bool useCache, long offset, int quantity)
        {
            return _tableStorage.Table_Read(useCache, offset, quantity);
        }

        public byte[] Table_Read(bool useCache, byte[] offset, int quantity)
        {
            return _tableStorage.Table_Read(useCache, offset, quantity);
        }


        public void RestoreTableFromTheOtherTable(string newTableFullPath)
        {
            _tableStorage.RestoreTableFromTheOtherTable(newTableFullPath);
        }


        public DateTime StorageFixTime
        {
            get { return _tableStorage.StorageFixTime; }
        }


        public long Length
        {
            get { return _tableStorage.Length; }
        }
    }
}
