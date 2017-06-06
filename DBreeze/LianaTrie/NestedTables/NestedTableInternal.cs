/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.LianaTrie;
using DBreeze.Storage;
using DBreeze.Exceptions;

namespace DBreeze.DataTypes
{
    
    /// <summary>
    /// In developing, will represent a table inside of the other table
    /// </summary>
    internal class NestedTableInternal:IDisposable
    {
        internal LTrie table = null;
        NestedTableStorage _storage = null;
        internal LTrie _masterTrie =null;
        //bool _masterTableInsert = false;
        long _shiftFromValueStart = 0;
        bool _useCache = false;
        LTrie _parentTable = null;
        byte[] _key = null;
        long _rootStart = 0;
        internal bool ValuesLazyLoadingIsOn = true;

        public uint quantityOpenReads = 0;

        /// <summary>
        /// Identifies that table is fake, because we always want to return data even default (count - 0, select - row with .Exists= false etc...)
        /// </summary>
        public bool TableExists = false;

        public NestedTableInternal(bool tableExists, LTrie masterTrie, long rootStart, long shiftFromValueStart, bool useCache, LTrie parentTrie,ref byte[] key)
        {
            //DbInTableStorage - Dispose and Recreate (Stay Empty)

            /////////////////////////////////   !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!     GET RID OF , bool useCache
            /////////////////////////////////   !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!     GET RID OF MASTER TABLE INSERT (table can be created by read thread also)
            
            TableExists = tableExists;

            if (tableExists)
            {
                _useCache = useCache;
                _shiftFromValueStart = shiftFromValueStart;
                //Flag distinguish between masterTrie.InsertTable or masterTrie.SelectTable (InsertTable, creates tables if they don't exist)
                //_masterTableInsert = masterTableInsert;
                _masterTrie = masterTrie;
                _parentTable = parentTrie;
                _key = key;
                _rootStart = rootStart;
                

                TrieSettings trieSettings = new TrieSettings()
                {                    
                    ROOT_START = rootStart,
                    IsNestedTable = true
                };

                _storage = new NestedTableStorage(masterTrie.Cache.Trie.Storage, trieSettings);

                //Then trie receives ITableFile wrapper with new settings

                table = new LTrie(_storage);
            }
        }

        public void Dispose()
        {
         
            //Cascade trie disposing
            if (table != null)
            {
                table.Dispose();
            }
        }

        //internal void CloseTable(bool insertTablesAllowed)
        internal void CloseTable()
        {
            this._parentTable.NestedTablesCoordinator.CloseTable(ref this._key, ref this._rootStart);
        }

        internal long SetNewRootStart(long newValueStart)
        {
            table.Storage.TrieSettings.ROOT_START = newValueStart + this._shiftFromValueStart;

            return table.Storage.TrieSettings.ROOT_START;
        }

        
        internal void Commit()
        {
            table.Commit();
        }

        internal void TransactionalCommit()
        {
            table.ITRCommit();
        }

        internal void TransactionalCommitFinished()
        {
            table.ITRCommitFinished();
        }
               
        internal void Rollback()
        {
            table.RollBack();
        }

        internal void TransactionalRollback()
        {
            table.ITRRollBack();
        }

        //internal bool IfWriteThread()
        //{
        //    return (_masterTrie.NestedTablesCoordinator.ModificationThreadId == System.Threading.Thread.CurrentThread.ManagedThreadId);                
        //}


        internal NestedTable GetTable<TKey>(TKey key, uint tableIndex,bool insertIsAllowed)
        {
            byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);
            LTrieRow row = null;

            if (insertIsAllowed)        //Insert of table is allowed by calls generation
            {
                row = table.GetKey(ref btKey, null, true);
                return table.GetTable(row, ref btKey, tableIndex, this._masterTrie, true, false);
            }

            //Only selects are allowed
#if NET35 || NETr40
            if (_masterTrie.NestedTablesCoordinator.ModificationThreadId == System.Threading.Thread.CurrentThread.ManagedThreadId)
#else
            if (_masterTrie.NestedTablesCoordinator.ModificationThreadId == Environment.CurrentManagedThreadId)                
#endif            
            {
                //This thread must NOT use cache
                row = table.GetKey(ref btKey, null, true);
                return table.GetTable(row, ref btKey, tableIndex, this._masterTrie, false, false);
            }
            else
            {
                LTrieRootNode readRootNode = new LTrieRootNode(table);
                row = table.GetKey(ref btKey, readRootNode, true);

                return table.GetTable(row, ref btKey, tableIndex, this._masterTrie, false, true);
            }
        }

        



        internal void RemoveAll()
        {
            //Must stay here
            //Will call cascade of Removing items
            if (table != null)
            {
                table.RemoveAll(false);
            }
        }

      


        
    }//eoc
}
