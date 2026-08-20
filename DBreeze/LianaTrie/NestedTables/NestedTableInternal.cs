/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
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
        NestedTableInternal _parentNestedTable = null;
        byte[] _key = null;
        long _rootStart = 0;
        ulong _fullValueStart = 0;
        int _disposed = 0;
        internal bool ValuesLazyLoadingIsOn = true;
        internal bool ClosePending = false;
        internal bool CoordinatorOwned = false;

        public uint quantityOpenReads = 0;

        /// <summary>
        /// Identifies that table is fake, because we always want to return data even default (count - 0, select - row with .Exists= false etc...)
        /// </summary>
        public bool TableExists = false;

        public NestedTableInternal(bool tableExists, LTrie masterTrie, long rootStart, long shiftFromValueStart, bool useCache, NestedTableInternal parentNestedTable, ref byte[] key)
        {
            //DbInTableStorage - Dispose and Recreate (Stay Empty)

            /////////////////////////////////   !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!     GET RID OF , bool useCache
            /////////////////////////////////   !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!     GET RID OF MASTER TABLE INSERT (table can be created by read thread also)
            
            TableExists = tableExists;

            if (tableExists)
            {
                _shiftFromValueStart = shiftFromValueStart;
                //Flag distinguish between masterTrie.InsertTable or masterTrie.SelectTable (InsertTable, creates tables if they don't exist)
                //_masterTableInsert = masterTableInsert;
                _masterTrie = masterTrie;
                _parentNestedTable = parentNestedTable;
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
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            //Cascade trie disposing
            if (table != null)
            {
                table.Dispose();
            }
        }

        //internal void CloseTable(bool insertTablesAllowed)
        internal void CloseTable()
        {
            // The master trie can be auto-closed when its owning Transaction ends.
            // Its coordinator detaches every nested table before disposing the lock;
            // a handle disposed afterwards therefore has nothing left to release.
            if (!CoordinatorOwned)
                return;

            this.ParentTrie.NestedTablesCoordinator.CloseTable(this);
        }

        private LTrie ParentTrie
        {
            get { return _parentNestedTable == null ? _masterTrie : _parentNestedTable.table; }
        }

        private const int DirectHandleMask = 0x40000000;
        private const int HandlePayloadMask = 0x3FFFFFFF;

        internal int CaptureWriteHandleState()
        {
#if NET35 || NETr40
            int currentThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
#else
            int currentThreadId = Environment.CurrentManagedThreadId;
#endif
            if (_masterTrie._modificationThreadId == -1)
                return DirectHandleMask | (currentThreadId & HandlePayloadMask);

            return _masterTrie.ModificationSessionId;
        }

        internal int ValidateWriteHandleState(int handleState)
        {
#if NET35 || NETr40
            int currentThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
#else
            int currentThreadId = Environment.CurrentManagedThreadId;
#endif
            if ((handleState & DirectHandleMask) != 0)
            {
                if ((handleState & HandlePayloadMask) != (currentThreadId & HandlePayloadMask))
                    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_CANBEUSED_FROM_ONE_THREAD);

                if (_masterTrie._modificationThreadId != -1)
                    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_DOESNT_EXIST);

                return currentThreadId;
            }

            if (handleState == 0 ||
                _masterTrie.ModificationSessionId != handleState ||
                _masterTrie._modificationThreadId == -1)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_DOESNT_EXIST);
            }

            if (_masterTrie._modificationThreadId != currentThreadId)
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_CANBEUSED_FROM_ONE_THREAD);

            return currentThreadId;
        }

        internal void CompleteWriteEpoch(int modificationThreadId)
        {
            if (!IsModified)
                return;

            if (_masterTrie.NestedTablesCoordinator.ModificationThreadId != modificationThreadId)
                _masterTrie.NestedTablesCoordinator.ModificationThreadId = modificationThreadId;

            LTrie immediateParent = ParentTrie;
            if (immediateParent.TableIsModified && _masterTrie.TableIsModified)
                return;

            NestedTableInternal current = this;
            while (current != null)
            {
                current.ParentTrie.TableIsModified = true;
                current = current._parentNestedTable;
            }
        }

        internal ulong FullValueStart
        {
            get { return _fullValueStart; }
        }

        internal long RootStart
        {
            get { return _rootStart; }
        }

        internal byte[] StructuralKey
        {
            get { return _key; }
        }

        internal bool IsModified
        {
            get { return table != null && table.TableIsModified; }
        }

        internal void BindIdentity(ulong fullValueStart, byte[] structuralKey)
        {
            _fullValueStart = fullValueStart;
            _key = structuralKey;
            CoordinatorOwned = true;
        }

        internal void DetachFromCoordinator()
        {
            ClosePending = false;
            CoordinatorOwned = false;
        }

        internal long SetNewRootStart(ulong fullValueStart, long newValueStart, byte[] structuralKey)
        {
            _fullValueStart = fullValueStart;
            if (structuralKey != null)
                _key = structuralKey;

            table.Storage.TrieSettings.ROOT_START = newValueStart + this._shiftFromValueStart;
            _rootStart = table.Storage.TrieSettings.ROOT_START;

            return _rootStart;
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
                return table.GetTable(row, ref btKey, tableIndex, this._masterTrie, true, false, this);
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
                return table.GetTable(row, ref btKey, tableIndex, this._masterTrie, false, false, this);
            }
            else
            {
                LTrieRootNode readRootNode = new LTrieRootNode(table);
                row = table.GetKey(ref btKey, readRootNode, true);

                return table.GetTable(row, ref btKey, tableIndex, this._masterTrie, false, true, this);
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
