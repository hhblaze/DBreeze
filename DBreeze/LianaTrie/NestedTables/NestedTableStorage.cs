/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.Storage;

namespace DBreeze.LianaTrie
{
    internal class NestedTableStorage :IStorage
    {
        IStorage _masterStorage = null;
        TrieSettings _trieSettings = null;

        public NestedTableStorage(IStorage masterStorage, TrieSettings trieSettings)
        {
            _masterStorage = masterStorage;
            _trieSettings = trieSettings;
        }

        public TrieSettings TrieSettings
        {
            get { 
                //Returning trie settigns of the nested table, together with the new root start
                return this._trieSettings; 
            }
        }

        public DBreezeConfiguration DbreezeConfiguration
        {
            get { return this._masterStorage.DbreezeConfiguration; }
        }

        public string Table_FileName
        {
            get { return _masterStorage.Table_FileName; }
        }

        public byte[] Table_WriteToTheEnd(byte[] data)
        {
            return this._masterStorage.Table_WriteToTheEnd(data);            
        }

        public void Table_WriteByOffset(long offset, byte[] data)
        {
            this._masterStorage.Table_WriteByOffset(offset, data);
            
        }

        public void Table_WriteByOffset(byte[] offset, byte[] data)
        {
            this._masterStorage.Table_WriteByOffset(offset, data);           
        }

        public byte[] Table_Read(bool useCache, long offset, int quantity)
        {
            return this._masterStorage.Table_Read(useCache, offset, quantity);
        }

        public byte[] Table_Read(bool useCache, byte[] offset, int quantity)
        {
            return this._masterStorage.Table_Read(useCache, offset, quantity);
        }

        public void Table_Dispose()
        {
           //DO NOTHING
        }


        public void Commit()
        {            
            this._masterStorage.Commit();
        }

        public void Rollback()
        {
            this._masterStorage.Rollback();
        }

        public void TransactionalCommit()
        {
            this._masterStorage.TransactionalCommit();
        }

        public void TransactionalCommitIsFinished()
        {
            this._masterStorage.TransactionalCommitIsFinished();
        }

        public void TransactionalRollback()
        {
            this._masterStorage.TransactionalRollback();
        }

        public void RecreateFiles()
        {
            //DO NOTHING
        }

        /// <summary>
        /// Works only for master tables
        /// </summary>
        /// <param name="newTableFullPath"></param>
        public void RestoreTableFromTheOtherTable(string newTableFullPath)
        {
            //DO NOTHING
        }


        public DateTime StorageFixTime
        {
            get { return this._masterStorage.StorageFixTime; }
        }


        public long Length
        {
            get { return this._masterStorage.Length; }
        }
    }
}
