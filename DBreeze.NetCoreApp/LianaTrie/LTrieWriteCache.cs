/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.Utils;
using DBreeze.Storage;
using DBreeze.Exceptions;

namespace DBreeze.LianaTrie
{
    internal class LTrieWriteCache:IDisposable
    {
        //!!!!!!!!!!!!!!  Problematic of virtualized Rollback
        //!!! DataIdentifier and all these dictionaries (except nodes)

        internal LTrie Trie = null;

        DbReaderWriterLock _sync_nodes = new DbReaderWriterLock();        
        //Key is GenerationMap line as string, Value is Kids in generation node of the last element of the generation map line
        Dictionary<string, byte[]> _nodes = new Dictionary<string, byte[]>();

        //DbReaderWriterLock _sync_values = new DbReaderWriterLock();
        //Dictionary<string, DataIdentifier> _values = new Dictionary<string, DataIdentifier>();
        
        //Holder of Dynamic data blocks cache: Key is pointer in the file to data block
        //Dictionary<ulong, DataIdentifier> dDynamicDataBlocks = new Dictionary<ulong, DataIdentifier>();
        //DbReaderWriterLock _sync_dDynamicDataBlocks = new DbReaderWriterLock();

        //private class DataIdentifier
        //{
        //    public long Pointer = 0;
        //    //public int Length { get; set; }
        //    /// <summary>
        //    /// Identifies that piece of data is in Rollback file. Otherwise in real file
        //    /// </summary>
        //    public bool ResidesInRollbackFile = true;
        //}

        ushort DefaultPointerLen = 0;

        //Takes always 8 bytes
        //byte[] rootPointerAsByte = null;
        bool IsNestedTable = false;

        //public LTrieWriteCache(IStorage storage, bool overWriteIsAllowed)
        public LTrieWriteCache(LTrie trie)
        {
            Trie = trie;           

            DefaultPointerLen = Trie.Storage.TrieSettings.POINTER_LENGTH;

            ////Represents pointer to the root of DefaultPointerLen size 
            //rootPointerAsByte = ((ulong)Trie.Storage.TrieSettings.ROOT_START).To_8_bytes_array_BigEndian().Substring(8 - DefaultPointerLen, DefaultPointerLen);

            IsNestedTable = Trie.Storage.TrieSettings.IsNestedTable;
                
        }

        #region RootNode v2

        object lock_root = new object();

        /// <summary>
        /// Represents old Root as byte[] array for the moment while we make Transactional Commit. Otherwise remains null
        /// </summary>
        byte[] rootTransaction = null;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rootData"></param>
        /// <param name="oldRootData"></param>
        private void RootNodeWrite(byte[] rootData)
        {
            //if (!IsNestedTable)
            //{
            //   // Console.WriteLine(Trie.Storage.Table_FileName + "___" + rootData.ToBytesString());
            //    Trie.Storage.Table_WriteByOffset(Trie.Storage.TrieSettings.ROOT_START, rootData);
            //}
            //else
            //{
            //    //Nested Table Roots we put into memory
            //    NestedTablesRoots.AddNestedtableRootForSave(Trie.Storage, Trie.Storage.TrieSettings.ROOT_START, rootData);
            //}


            lock (lock_root)
            {
                Trie.Storage.Table_WriteByOffset(Trie.Storage.TrieSettings.ROOT_START, rootData);
            }

        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] RootNodeRead()
        {
            byte[] root = null;

            lock (lock_root)
            {
                //For reading threads we return copy of rootTransaction, while Transactional Commit is still in progress

                if (rootTransaction == null)
                {                    
                    root = Trie.Storage.Table_Read(false, Trie.Storage.TrieSettings.ROOT_START, Trie.Storage.TrieSettings.ROOT_SIZE);

                }
                else
                {                    
                    root = rootTransaction;                    
                }
            }

            return root;
        }

        #endregion
        
        #region Transactional Commits and Rollbacks

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rootData"></param>
        /// <param name="oldRootData"></param>
        public void Commit(ref byte[] rootData, ref byte[] oldRootData)
        {
            //_sync_dDynamicDataBlocks.EnterWriteLock();
            //try
            //{
                _sync_nodes.EnterWriteLock();
                try
                {
                    //_sync_values.EnterWriteLock();
                    //try
                    //{
                        //Clearing values
                        //_values.Clear();

                        //_values = null;
                        //_values = new Dictionary<string, DataIdentifier>();

                        _nodes.Clear();

                        _nodes = null;
                        _nodes = new Dictionary<string, byte[]>();

                        //dDynamicDataBlocks.Clear();

                        //dDynamicDataBlocks = null;
                        //dDynamicDataBlocks = new Dictionary<ulong, DataIdentifier>();


                        //RootNodeWrite(rootPointer, ref rootData, false, ref oldRootData);
                        RootNodeWrite(rootData);

                        if (!IsNestedTable)
                            Trie.Storage.Commit();

                    //}
                    //catch (System.Exception ex)
                    //{
                    //    throw ex;
                    //}
                    //finally
                    //{
                    //    _sync_values.ExitWriteLock();
                    //}
                }
                catch (System.Exception ex)
                {
                    throw ex;
                }
                finally
                {
                    _sync_nodes.ExitWriteLock();
                }

            //}
            //catch (System.Exception ex)
            //{
            //    //CASCADE
            //    throw ex;
            //}
            //finally
            //{
            //    _sync_dDynamicDataBlocks.ExitWriteLock();
            //}

        }

        /// <summary>
        /// 
        /// </summary>
        public void RollBack()
        {
            //Fail of this procedure will cause DbNotOperatable state on the level of Trie
          
            //Before Clearing cache we need to acqire both locks.
            //_sync_dDynamicDataBlocks.EnterWriteLock();
            //try
            //{

                _sync_nodes.EnterWriteLock();
                try
                {
                    //_sync_values.EnterWriteLock();
                    //try
                    //{
                        //Deleting RollBack File

                        if (!IsNestedTable)
                            Trie.Storage.Rollback();

                        //Clearing cache _values and _nodes

                        //_values.Clear();

                        //_values = null;
                        //_values = new Dictionary<string, DataIdentifier>();

                        _nodes.Clear();

                        _nodes = null;
                        _nodes = new Dictionary<string, byte[]>();

                        //dDynamicDataBlocks.Clear();

                        //dDynamicDataBlocks = null;
                        //dDynamicDataBlocks = new Dictionary<ulong, DataIdentifier>();

                    //}
                    //catch (System.Exception ex)
                    //{
                    //    throw ex;
                    //}
                    //finally
                    //{
                    //    _sync_values.ExitWriteLock();
                    //}
                }
                catch (System.Exception ex)
                {
                    throw ex;
                }
                finally
                {
                    _sync_nodes.ExitWriteLock();
                }

            //}
            //catch (System.Exception ex)
            //{
            //    //CASCADE
            //    throw ex;
            //}
            //finally
            //{
            //    _sync_dDynamicDataBlocks.ExitWriteLock();
            //}
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="rootData"></param>
        /// <param name="oldRootData"></param>
        public void TransactionalCommit(ref byte[] rootData, ref byte[] oldRootData)
        {
            //We store old root data for RootNodeRead
            lock (lock_root)
            {
                rootTransaction = oldRootData;
            }

            try
            {
                RootNodeWrite(rootData);

                if (!IsNestedTable)
                    Trie.Storage.TransactionalCommit();
            }
            catch (Exception ex)
            {
                //CASCADE
                throw ex;
            }            
        }

        /// <summary>
        /// We need here to clear old root and call Storage Commit is finished
        /// </summary>
        public void TransactionalCommitFinished()
        {
            //_sync_dDynamicDataBlocks.EnterWriteLock();
            //try
            //{
                _sync_nodes.EnterWriteLock();
                try
                {
                    //_sync_values.EnterWriteLock();
                    //try
                    //{
                        if(!IsNestedTable)
                            Trie.Storage.TransactionalCommitIsFinished();

                        //Clearing values
                        //_values.Clear();

                        //_values = null;
                        //_values = new Dictionary<string, DataIdentifier>();

                        _nodes.Clear();

                        _nodes = null;
                        _nodes = new Dictionary<string, byte[]>();

                        //dDynamicDataBlocks.Clear();

                        //dDynamicDataBlocks = null;
                        //dDynamicDataBlocks = new Dictionary<ulong, DataIdentifier>();


                        lock (lock_root)
                        {
                            rootTransaction = null;
                        }                        
                    //}
                    //catch (System.Exception ex)
                    //{
                    //    //CASCADE
                    //    throw ex;
                    //}
                    //finally
                    //{
                    //    _sync_values.ExitWriteLock();
                    //}
                }
                catch (System.Exception ex)
                {
                    //CASCADE
                    throw ex;
                }
                finally
                {
                    _sync_nodes.ExitWriteLock();
                }

            //}
            //catch (System.Exception ex)
            //{
            //    //CASCADE
            //    throw ex;
            //}
            //finally
            //{
            //    _sync_dDynamicDataBlocks.ExitWriteLock();
            //}
        }

        /// <summary>
        /// 
        /// </summary>
        public void TransactionalRollBack()
        {
            //Fail of this procedure will cause DbNotOperatable state on the level of Trie

            //_sync_dDynamicDataBlocks.EnterWriteLock();
            //try
            //{
                _sync_nodes.EnterWriteLock();
                try
                {
                    //_sync_values.EnterWriteLock();
                    //try
                    //{

                        if (!IsNestedTable)
                            Trie.Storage.TransactionalRollback();
                   
                        //Clearing cache _values and _nodes
                        //_values.Clear();

                        //_values = null;
                        //_values = new Dictionary<string, DataIdentifier>();

                        _nodes.Clear();

                        _nodes = null;
                        _nodes = new Dictionary<string, byte[]>();

                        //dDynamicDataBlocks.Clear();

                        //dDynamicDataBlocks = null;
                        //dDynamicDataBlocks = new Dictionary<ulong, DataIdentifier>();

                        lock (lock_root)
                        {
                            rootTransaction = null;
                        }  

                    //}
                    //catch (System.Exception ex)
                    //{
                    //    throw ex;
                    //}
                    //finally
                    //{
                    //    _sync_values.ExitWriteLock();
                    //}
                }
                catch (System.Exception ex)
                {
                    throw ex;
                }
                finally
                {
                    _sync_nodes.ExitWriteLock();
                }

            //}
            //catch (System.Exception ex)
            //{
            //    //CASCADE
            //    throw ex;
            //}
            //finally
            //{
            //    _sync_dDynamicDataBlocks.ExitWriteLock();
            //}
        }

        #endregion
        
        #region Recreate table files

        /// <summary>
        /// Used by Root Node RemoveAll with key re-creation
        /// </summary>
        public void RecreateDB()
        {
            //_sync_dDynamicDataBlocks.EnterWriteLock();
            //try
            //{
                _sync_nodes.EnterWriteLock();
                try
                {
                    //_sync_values.EnterWriteLock();
                    //try
                    //{
                        //Clearing values
                        //_values.Clear();

                        //_values = null;
                        //_values = new Dictionary<string, DataIdentifier>();

                        _nodes.Clear();

                        _nodes = null;
                        _nodes = new Dictionary<string, byte[]>();


                        //dDynamicDataBlocks.Clear();

                        //dDynamicDataBlocks = null;
                        //dDynamicDataBlocks = new Dictionary<ulong, DataIdentifier>();

                        if (!IsNestedTable)
                        {
                            Trie.Storage.RecreateFiles();
                          
                        }
                    //}
                    //catch (System.Exception ex)
                    //{
                    //    throw ex;
                    //}
                    //finally
                    //{
                    //    _sync_values.ExitWriteLock();
                    //}
                }
                catch (System.Exception ex)
                {
                    throw ex;
                }
                finally
                {
                    _sync_nodes.ExitWriteLock();
                }

            //}
            //catch (System.Exception ex)
            //{
            //    throw ex;
            //}
            //finally
            //{
            //    _sync_dDynamicDataBlocks.ExitWriteLock();
            //}
        }

        #endregion
        
        #region Generation Nodes

        /// <summary>
        /// Returns NULL if not found
        /// </summary>
        /// <param name="generationMapLine"></param>
        /// <returns></returns>
        public byte[] GetNodeKids(byte[] generationMapLine)
        {
            string hash = generationMapLine.ToBase64String();


            _sync_nodes.EnterReadLock();
            try
            {
                byte[] ret = null;
                _nodes.TryGetValue(hash, out ret);

                return ret;
            }
            catch (System.Exception ex)
            {
                throw ex;
            }
            finally
            {
                _sync_nodes.ExitReadLock();
            }

        }

        ///// <summary>
        ///// If line exists - makes nothing, otherwise writes in
        ///// </summary>
        ///// <param name="generationMapLine"></param>
        ///// <param name="kidsBeforeModification"></param>
        //public void AddMapKids(byte[] generationMapLine, byte[] kidsBeforeModification)
        //{
        //    string hash = generationMapLine.ToBase64String();

        //    //Console.WriteLine("Adding Kids in hash: {0}", hash);

        //    _sync_nodes.EnterWriteLock();
        //    try
        //    {
        //        byte[] ret = null;
        //        _nodes.TryGetValue(hash, out ret);


        //        if (ret == null)
        //        {
        //            if (kidsBeforeModification == null)
        //                kidsBeforeModification = new byte[0];

        //            _nodes.Add(hash, kidsBeforeModification);
        //        }
        //    }
        //    catch (System.Exception ex)
        //    {
        //        throw ex;
        //    }
        //    finally
        //    {
        //        _sync_nodes.ExitWriteLock();
        //    }

        //}

        /// <summary>
        /// Writing Generation Node to the end of File.
        /// We use current generation node pointer to black list it.
        /// if we reuse this new pointer inside of one transaction for overwriting, we don't need to back it up for rollback any more.
        /// </summary>
        /// <param name="pointer"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public byte[] GenerationNodeWritingEnd(byte[] pointer,byte[] data)
        {
            return Trie.Storage.Table_WriteToTheEnd(data);            
        }

        ///// <summary>
        ///// OverWriting Generation Node, we supply params where, oldData and newData.
        ///// Old Data - not used thou
        ///// </summary>
        ///// <param name="pointer"></param>
        ///// <param name="oldData"></param>
        ///// <param name="newData"></param>
        //public void GenerationNodeWritingOver(byte[] pointer, byte[] newData)
        //{            
        //    _sync_nodes.EnterWriteLock();
        //    try
        //    {   
        //        Trie.Storage.Table_WriteByOffset(pointer, newData);                
        //    }
        //    catch (System.Exception ex)
        //    {
        //        throw ex;
        //    }
        //    finally
        //    {
        //        _sync_nodes.ExitWriteLock();
        //    }

           
        //}

        /// <summary>
        /// OverWriting Generation Node, we supply params where, oldData and newData.
        /// </summary>
        /// <param name="pointer"></param>
        /// <param name="newData"></param>
        /// <param name="generationMapLine"></param>
        /// <param name="kidsBeforeModification"></param>
        public void GenerationNodeWritingOver(byte[] pointer, byte[] newData, byte[] generationMapLine, byte[] kidsBeforeModification)
        {
            _sync_nodes.EnterWriteLock();
            try
            {
                long ptrU = (long)pointer.DynamicLength_To_UInt64_BigEndian();


                if (Trie.Storage.Length > ptrU && generationMapLine != null)
                {
                    //Update only. Filling parallel read nodes. 
                    string hash = generationMapLine.ToBase64String();

                    byte[] ret = null;
                    _nodes.TryGetValue(hash, out ret);                    

                    if (ret == null)
                    {
                        if (kidsBeforeModification == null)
                            kidsBeforeModification = new byte[0];

                        _nodes[hash] = kidsBeforeModification;
                    }
                }
                
                Trie.Storage.Table_WriteByOffset(pointer, newData);
            }
            catch (System.Exception ex)
            {
                throw ex;
            }
            finally
            {
                _sync_nodes.ExitWriteLock();
            }


        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="useCache"></param>
        /// <param name="pointer"></param>
        /// <param name="cachedGenerationMapLine"></param>
        /// <param name="MaximumNodeLineLength"></param>
        /// <returns></returns>
        public byte[] GenerationNodeRead(bool useCache, byte[] pointer, byte[] cachedGenerationMapLine, int MaximumNodeLineLength)
        {
            byte[] node=null;

            if (useCache)
            {
                node = this.GetNodeKids(cachedGenerationMapLine);

                if (node != null)
                {
                    //Here node can be also of empty length [0] - it means that cachedGenerationMapLine exists but old kids were empty, it will be checked on upper levels
                    return node;
                }
            }

            if (pointer._IfPointerIsEmpty(DefaultPointerLen))
                return null;

            //Reading it from disk

            byte[] line = null;

            //Locking Node on Read
            _sync_nodes.EnterReadLock();
            try
            {
                line = Trie.Storage.Table_Read(useCache, pointer, MaximumNodeLineLength);               
            }
            catch (System.Exception ex)
            {
                throw ex;
            }
            finally
            {
                _sync_nodes.ExitReadLock();
            }
                                   

            if (line == null || line.Length < 2)
                return null;

            ushort sLen = (new byte[] { line[0], line[1] }).To_UInt16_BigEndian();

            if (sLen == 0)
                return null;

            return line.Substring(2, sLen);
        }

        #endregion

        #region "Dynamic Data Blocks"

        /// <summary>
        /// 
        /// </summary>
        /// <param name="initPtr"></param>
        /// <param name="useCache"></param>
        /// <returns>return NULL if not found or stored value is NULL, otherwise returns byte[]</returns>
        public byte[] ReadDynamicDataBlock(ref byte[] initPtr,bool useCache)
        {
            //Link to the block (initPtr) is represented by 8+4+4=16 bytes: 8 pointer (ulong), 4 bytes Block Length(ulong), 4 bytes data length (uint)
            //Block is represented only with data + reserved block space 
            //if initPtr = null, we create new DataBlock
            try
            {
                if (initPtr._IfDynamicDataPointerIsEmpty())
                    return null;

                byte[] ptr = initPtr.Substring(8 - DefaultPointerLen, DefaultPointerLen);
                byte[] btDataLen = initPtr.Substring(12, 4);

                if ((btDataLen[0] & 0x80) > 0)
                {
                    return null;    //no data
                }

                //value must be taken from DBstorage
                int dl = (int)btDataLen.To_UInt32_BigEndian();
                //returning value
                return Trie.Storage.Table_Read(useCache, ptr, dl);

                //ulong ptrHash = ptr.DynamicLength_To_UInt64_BigEndian();

                //if (useCache)
                //{
                //    //READER
                //    DataIdentifier di=null;

                //    _sync_dDynamicDataBlocks.EnterReadLock();
                //    try
                //    {
                //        if (dDynamicDataBlocks.TryGetValue(ptrHash, out di))
                //        {
                //            //value must be taken from Rollback
                //            if (di.Pointer == -1)
                //                return null;

                //            int dl = (int)btDataLen.To_UInt32_BigEndian();

                //            //returning value
                //            return this.RollerBack.ReadRollBackdata(di.Pointer, dl);
                //        }
                //        else
                //        {
                //            //value must be taken from DBstorage
                //            int dl = (int)btDataLen.To_UInt32_BigEndian();
                //            //returning value
                //            return Trie.Storage.Table_Read(ptr, dl);
                //        }
                //    }
                //    finally
                //    {
                //        _sync_dDynamicDataBlocks.ExitReadLock();
                //    }

                //}
                //else
                //{
                //    //WRITER

                //    //value must be taken from DBstorage
                //    int dl = (int)btDataLen.To_UInt32_BigEndian();
                //    //returning value
                //    return Trie.Storage.Table_Read(ptr, dl);
                //}
            }
            catch (Exception ex)
            {                
                throw ex;
            }
            
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="initPtr"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public byte[] WriteDynamicDataBlock(ref byte[] initPtr, ref byte[] data)
        {           
                     
            //Link to the block (initPtr) is represented by 8+4+4=16 bytes: 8 pointer (ulong), 4 bytes Block Length(ulong), 4 bytes data length (uint)
            //Block is represented only with data + reserved block space 
            //if initPtr = null, we create new DataBlock
            
            try
            {
               
                if (data != null && data.LongCount() > Int32.MaxValue)
                    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DYNAMIC_DATA_BLOCK_VALUE_IS_BIG);


                if (initPtr._IfDynamicDataPointerIsEmpty() || !Trie.OverWriteIsAllowed)
                {
                    //Init pointer  = null, so we write first time and to the end
                    //Writing to the End ()

                    //Special case value is nullable
                    if (data == null)
                        return new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x80, 0, 0, 0 };    //indicating null value

                    //First value write Returning 16 bytes.
                    byte[] ret = Trie.Storage.Table_WriteToTheEnd(data);    //getting pointer
                    ret = ret.EnlargeByteArray_BigEndian(8);
                    ret = ret.ConcatMany(
                        ((uint)data.Length).To_4_bytes_array_BigEndian(), //block size equals data length
                        ((uint)data.Length).To_4_bytes_array_BigEndian()  //data length
                        );

                    return ret;
                }
                else
                {
                  
                    if (data == null)
                    {
                        return initPtr.Substring(0, 12).Concat(new byte[] { 0x80, 0, 0, 0 });                        
                    }

                    byte[] ret = null;
                    byte[] btBlockLen = initPtr.Substring(8, 4);
                    int blockLen = (int)btBlockLen.To_UInt32_BigEndian();

                    if (blockLen == 0)
                    {
                        //Block was not reserved before we can write to the end
                        //Data is not null, we create new block

                        ret = Trie.Storage.Table_WriteToTheEnd(data);    //getting pointer
                        ret = ret.EnlargeByteArray_BigEndian(8);
                        ret = ret.ConcatMany(
                            ((uint)data.Length).To_4_bytes_array_BigEndian(), //block size equals data length
                            ((uint)data.Length).To_4_bytes_array_BigEndian()  //data length
                            );

                        return ret;
                    }
                    

                    if (blockLen < data.Length)
                    {
                        //situation when we need to write in any case to the new block, because previous was too small
                        ret = Trie.Storage.Table_WriteToTheEnd(data);    //getting pointer
                        ret = ret.EnlargeByteArray_BigEndian(8);
                        ret = ret.ConcatMany(
                            ((uint)data.Length).To_4_bytes_array_BigEndian(), //block size equals data length
                            ((uint)data.Length).To_4_bytes_array_BigEndian()  //data length
                            );

                        return ret;
                    }
                    else
                    {
                        //Overwrite
                        //original Data which we are going to overwrite, must be stored in RollBack   
                        byte[] ptr = initPtr.Substring(8 - DefaultPointerLen, DefaultPointerLen);
                        byte[] btDataLen = initPtr.Substring(12, 4);

                        //...Save new data
                        Trie.Storage.Table_WriteByOffset(ptr, data);

                        ptr = ptr.EnlargeByteArray_BigEndian(8);
                        ret = ptr.ConcatMany(
                        btBlockLen, //taking old block length
                        ((uint)data.Length).To_4_bytes_array_BigEndian()  //data length
                        );

                        return ret;

                        //ulong ptrHash = ptr.DynamicLength_To_UInt64_BigEndian();

                        //if ((btDataLen[0] & 0x80) > 0)
                        //{
                        //    //We have to store in RollbackCache info that previous block was of null size
                    
                        //    //put to cache fake
                        //    _sync_dDynamicDataBlocks.EnterWriteLock();
                        //    try
                        //    {
                        //        if (!dDynamicDataBlocks.ContainsKey(ptrHash))
                        //        {
                        //            dDynamicDataBlocks.Add(ptrHash, new DataIdentifier()
                        //            {
                        //                Pointer = -1         //Means that we have null value, so reader can recognize it
                        //            });
                        //        }
                        //    }
                        //    finally
                        //    {
                        //        _sync_dDynamicDataBlocks.ExitWriteLock();
                        //    }

                        //    //...Save new data
                        //    Trie.Storage.Table_WriteByOffset(ptr, data);

                        //   ptr = ptr.EnlargeByteArray_BigEndian(8);
                        //   ret = ptr.ConcatMany(
                        //   btBlockLen, //taking old block length
                        //   ((uint)data.Length).To_4_bytes_array_BigEndian()  //data length
                        //   );

                        //   return ret;
                        //}
                        //else
                        //{
                        //    //int dl = (int)btDataLen.To_UInt32_BigEndian();
                        //    //byte[] oldData = Trie.Storage.Table_Read(ptr, dl);

                        //    ////put to Rollback
                        //    //long rlbPtr = this.RollerBack.WriteRollBackData(1, ptr, ref oldData);


                        //    //...Save new data
                        //    Trie.Storage.Table_WriteByOffset(ptr, data);

                        //    //put to cache
                        //    _sync_dDynamicDataBlocks.EnterWriteLock();
                        //    try
                        //    {
                        //        if (!dDynamicDataBlocks.ContainsKey(ptrHash))
                        //        {
                        //            dDynamicDataBlocks.Add(ptrHash, new DataIdentifier()
                        //            {
                        //                Pointer = (long)ptr.DynamicLength_To_UInt64_BigEndian()                                           
                        //            });
                        //        }
                        //    }
                        //    finally
                        //    {
                        //        _sync_dDynamicDataBlocks.ExitWriteLock();
                        //    }

                        //    ////...Save new data
                        //    //Trie.Storage.Table_WriteByOffset(ptr, data);

                        //    ptr = ptr.EnlargeByteArray_BigEndian(8);

                        //    ret = ptr.ConcatMany(
                        //       btBlockLen, //taking old block length
                        //       ((uint)data.Length).To_4_bytes_array_BigEndian()  //data length
                        //       );

                        //    return ret;
                        //}

                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
           
        }



       

        #endregion


        #region Values

        /// <summary>
        ///  
        /// </summary>
        /// <param name="data"></param>
        /// <param name="oldPtr"></param>
        /// <returns></returns>
        public byte[] ValueWritingEnd(ref byte[] data, byte[] oldPtr)
        {
            return Trie.Storage.Table_WriteToTheEnd(data);

            //byte[] newPtr = null;

            //if (oldPtr != null)
            //{
            //    //if oldPtr != null, it means that we overwrite value completely on the new place.
            //    //READ threads will try to get this value by newly received pointer. we have to bind it to the old value which resides in the StorageFile (not in Rollback)
                               

            //    _sync_values.EnterWriteLock();
            //    try
            //    {
            //        //getting new pointer to the fullValue
            //        newPtr = Trie.Storage.Table_WriteToTheEnd(data);
            //        //creating hash of it
            //        string hashNew = newPtr.ToBase64String();

            //        //If cache contains oldPointer to data, our new hash must be bound to it also
                    
            //        DataIdentifier diNew = null;

            //        _values.TryGetValue(hashNew, out diNew);

            //        if (diNew == null)
            //        {
            //            DataIdentifier diOld = null;
            //            string hash = oldPtr.ToBase64String();

            //            _values.TryGetValue(hash, out diOld);

            //            if (diOld == null)
            //            {                           
            //                //creating new hash from the newly written pointer and explaining that it must be taken from real DbStorage, so it was completely moved to the new place
            //                _values.Add(hashNew, new DataIdentifier
            //                {
            //                    Pointer = (long)oldPtr.DynamicLength_To_UInt64_BigEndian(),
            //                    ResidesInRollbackFile = false       //Pointer to the old value resides in the storage file
            //                });
            //            }
            //            else
            //            {
            //                //Creating new hash and explain that it must the same hash setting as an old one
            //                //Settign up new has to refer to old hash
            //                _values.Add(hashNew, new DataIdentifier
            //                {
            //                    Pointer = diOld.Pointer,
            //                    ResidesInRollbackFile = diOld.ResidesInRollbackFile       //Pointer to the old value resides in the storage file
            //                });
            //            }
            //        }

            //        //if diNew exists we ignore it - actually it doesn't happen
            //    }
            //    catch (System.Exception ex)
            //    {
            //        throw ex;
            //    }
            //    finally
            //    {
            //        _sync_values.ExitWriteLock();
            //    }

            //    return newPtr;
            //}
            //else
            //{
            //    return Trie.Storage.Table_WriteToTheEnd(ref data);
            //}

            
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pointer"></param>
        /// <param name="oldData"></param>
        /// <param name="newData"></param>
        /// <param name="key"></param>
        public void ValueWritingOver(byte[] pointer, ref byte[] oldData, ref byte[] newData, ref byte[] key)
        {
            Trie.Storage.Table_WriteByOffset(pointer, newData);

            ////OverWriting of a value [used by WRITE FUNC] and Reading Key or Value [used by READ FUNC] must come via one Synchro lock _sync_data

            //long ptrToDataInRollBackFile = this.RollerBack.WriteRollBackData(1, pointer, ref oldData);

            //string hash = pointer.ToBase64String();

            //_sync_values.EnterWriteLock();
            //try
            //{
            //    if (!_values.ContainsKey(hash))
            //    {
            //        if (ptrToDataInRollBackFile != -1)
            //        {
            //            _values.Add(hash, new DataIdentifier
            //                {
            //                    Pointer = ptrToDataInRollBackFile//,
            //                    //ResidesInRollbackFile = true  - default
            //                });
            //        }                    
            //    }

            //    Trie.Storage.Table_WriteByOffset(pointer, ref newData);
            //}
            //catch (System.Exception ex)
            //{
            //    throw ex;
            //}
            //finally
            //{
            //    _sync_values.ExitWriteLock();
            //}
        }


        /// <summary>
        /// Interanl Function for reading Key and Value in one set. Initial block read is setUp to 4096 bytes
        /// </summary>
        /// <param name="useCache"></param>
        /// <param name="pointer">ptr to KVP</param>
        /// <param name="valueStartPtr">will be more then 0 only in case if valueLength more then 0. It makes no diff for the null and byte[0] values in this context. Avoid byte[0]</param>
        /// <param name="valueLength">will be 0 if val is null and if val is byte[0]</param>
        /// <param name="key"></param>
        /// <param name="val"></param>
        public void ReadKeyValue(bool useCache, byte[] pointer, out long valueStartPtr, out uint valueLength, out byte[] key, out byte[] val)
        {
            key = null;
            val = null;
            valueStartPtr = 0;  //If valueLength=0 then valueStartPtr = 0
            valueLength = 0;

            //Changing format:
            //1byte - protocol, FullKeyLen (2 bytes), FullValueLen (4 bytes),[Reserved Space For Update- 4 bytes],FullKey,FullValue
            //1 + 2 + 4 + 4 + 100 = 111
            //int initRead = 111; //Where 100 is standard max size of the key in case of words (even 50 must be enough), in case of sentences can be much longer, probably we can setup it later
            int initRead = 4096; //amandment, for the middle size key+value

            //DONT NEED TO EnlargeByteArray_BigEndian(8) ptr, because it's automatically done in TrieDisk Storage etc..

            //byte[] data = this._root.Tree.Storage.Read(ptr.EnlargeByteArray_BigEndian(8), initRead);
            byte[] data = Trie.Storage.Table_Read(useCache, pointer, initRead);
            byte protocol = data[0];
            ushort keySize = (new byte[] { data[1], data[2] }).To_UInt16_BigEndian();
            byte[] btValueSize = new byte[] { data[3], data[4], data[5], data[6] };
            int valueSize = 0;
            long lPtr = (long)pointer.DynamicLength_To_UInt64_BigEndian();
            
            /*VALUE SIZE COMPUTATION and NULL SUPPORT*/
            if ((data[3] & 0x80) > 0)
            {
                //VALUE is NULL
                valueLength = 0;
            }
            else
            {
                valueLength = btValueSize.To_UInt32_BigEndian();
                valueSize = (int)valueLength;

                if(valueSize == 0)
                    val = new byte[0];
            }
            /**************/
                    

            switch (protocol)
            {
                case 0:
                    //We don't have reservation identifiers, it happens after first insert into the new place 

                    if ((keySize + valueSize + 7) > initRead)
                    {
                        initRead = keySize + valueSize + 7;
                        data = Trie.Storage.Table_Read(useCache, pointer, initRead);
                    }

                    key = data.Substring(7, keySize);

                    if (valueSize > 0)
                    {
                        valueStartPtr = lPtr + 7 + keySize;                        
                        val = data.Substring(7 + keySize, valueSize);
                    }

                    break;
                case 1:
                    //With Reserved space

                    if ((keySize + valueSize + 11) > initRead)
                    {
                        initRead = keySize + valueSize + 11;
                        data = Trie.Storage.Table_Read(useCache, pointer, initRead);
                    }  

                    key = data.Substring(11, keySize);

                    if (valueSize > 0)
                    {
                        valueStartPtr = lPtr + 11 + keySize;                                              
                        val = data.Substring(11 + keySize, valueSize);
                    }
                    break;
            }

            
        }

        /// <summary>
        /// Internal function for reading key only from DB storage
        /// Is called from ReadKey
        /// </summary>
        /// <param name="useCache"></param>
        /// <param name="pointer">ptr to KVP</param>
        /// <returns></returns>
        public byte[] ReadKey(bool useCache, byte[] pointer)
        {
            //Changing format:
            //1byte - protocol, FullKeyLen (2 bytes), FullValueLen (4 bytes),[Reserved Space For Update- 4 bytes],FullKey,FullValue
            //1 + 2 + 4 + 4 + 100 = 111
            int initRead = 111; //Where 100 is standard max size of the key in case of words (even 50 must be enough), in case of sentences can be much longer, probably we can setup it later

            //DONT NEED TO EnlargeByteArray_BigEndian(8) ptr, because it's automatically done in TrieDisk Storage etc..

            //byte[] data = this._root.Tree.Storage.Read(ptr.EnlargeByteArray_BigEndian(8), initRead);
            byte[] data = Trie.Storage.Table_Read(useCache, pointer, initRead);
            byte protocol = data[0];
            ushort keySize = (new byte[] { data[1], data[2] }).To_UInt16_BigEndian();


            byte[] key = null;



            switch (protocol)
            {
                case 0:
                    //First insert - no reservation identifiers for the space

                    //Expanding read if necessary
                    if (keySize > (initRead - 7))   //>= ?
                    {
                        initRead = keySize + 7;
                        data = Trie.Storage.Table_Read(useCache, pointer, initRead);                        
                    }

                    key = data.Substring(7, keySize);

                    break;
                case 1:
                    //Expanding read if necessary
                    if (keySize > (initRead - 11))  //>= ?
                    {
                        initRead = keySize + 11;
                        data = Trie.Storage.Table_Read(useCache, pointer, initRead);
                    }

                    key = data.Substring(11, keySize);

                    break;
            }



            return key;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="pointer">ptr to KVP</param>
        /// <param name="useCache"></param>
        /// <param name="valueStartPtr"></param>
        /// <param name="valueLength"></param>
        /// <returns></returns>
        public byte[] ReadValue(byte[] pointer, bool useCache, out long valueStartPtr, out uint valueLength)
        {
            valueLength = 0;
            valueStartPtr = -1;

            if (pointer._IfPointerIsEmpty(DefaultPointerLen))
                return null;


            //Changing format:
            //1 byte protocol type, FullKeyLen (2 bytes), FullValueLen (4 bytes), [ReservedSpace], FullKey,FullValue        
            //1 + 2 + 4 + 4 + 100  = 111
            //int initRead = 111; //Where 100 is standard max size of the key in case of words (even 50 must be enough), in case of sentences can be much longer, probably we can setup it later
            int initRead = 4096;

            byte[] data = null;
            byte protocol = 0;
            ushort keySize = 0;
            int valueSize = 0;
            byte[] btValueSize = null;
            long lPtr = 0;
            byte[] val = null;
                        
            lPtr = (long)pointer.DynamicLength_To_UInt64_BigEndian();

            data = Trie.Storage.Table_Read(useCache, lPtr, initRead);

            btValueSize = new byte[] { data[3], data[4], data[5], data[6] };

            /*NULL SUPPORT*/
            if ((data[3] & 0x80) > 0)
            {
                //NULL
                return null;
            }
            /**************/

            valueLength = btValueSize.To_UInt32_BigEndian();
            valueSize = (int)valueLength;

            /*NULL SUPPORT*/
            //if (valueSize == 0)
            //    return null;
            if (valueSize == 0)
                return new byte[0];
            /**************/

            protocol = data[0];
            keySize = (new byte[] { data[1], data[2] }).To_UInt16_BigEndian();


            switch (protocol)
            {
                case 0:
                    //We don't have reservation identifiers, it happens after first insert into the new place 

                    if ((keySize + valueSize + 7) > initRead)
                    {
                        lPtr += 7 + keySize;
                        valueStartPtr = lPtr;
                        val = Trie.Storage.Table_Read(useCache, lPtr, valueSize);
                    }
                    else
                    {
                        valueStartPtr = lPtr + 7 + keySize;
                        val = data.Substring(7 + keySize, valueSize);
                    }

                    break;
                case 1:
                    //btTotalReservedSize = new byte[] { data[7], data[8], data[9], data[10] };
                    //totalReservedSize = btTotalReservedSize.To_Int32_BigEndian();
                    if ((keySize + valueSize + 11) > initRead)
                    {
                        lPtr += 11 + keySize;
                        valueStartPtr = lPtr;
                        val = Trie.Storage.Table_Read(useCache, lPtr, valueSize);
                    }
                    else
                    {
                        valueStartPtr = lPtr + 11 + keySize;
                        val = data.Substring(11 + keySize, valueSize);
                    }
                    break;
            }

            return val;

        }




        /// <summary>
        /// 
        /// </summary>
        /// <param name="pointer"></param>
        /// <param name="startIndex"></param>
        /// <param name="length"></param>
        /// <param name="useCache"></param>
        /// <param name="valueStartPtr">returns pointer where value starts from. -1 if can't be determined</param>
        /// <param name="valueLength">returns full value length; 0 - default</param>
        /// <returns></returns>
        public byte[] ReadValuePartially(byte[] pointer, uint startIndex, uint length, bool useCache, out long valueStartPtr,out uint valueLength)
        {
            valueStartPtr = -1;
            valueLength = 0;

            if (pointer._IfPointerIsEmpty(DefaultPointerLen))
                return null;

            

            //Changing format:
            //1 byte protocol type, FullKeyLen (2 bytes), FullValueLen (4 bytes), [ReservedSpace], FullKey,FullValue        
            //1 + 2 + 4 + 4 + 100  = 111
            int initRead = 111; //Where 100 is standard max size of the key in case of words (even 50 must be enough), in case of sentences can be much longer, probably we can setup it later

            byte[] data = null;
            byte protocol = 0;
            ushort keySize = 0;
            int valueSize = 0;
            byte[] btValueSize = null;
            long lPtr = 0;
            byte[] val = null;

            //Reading from disk inside of lock
            //lPtr = (long)pointer.EnlargeByteArray_BigEndian(8).To_UInt64_BigEndian();
            lPtr = (long)pointer.DynamicLength_To_UInt64_BigEndian();

            data = Trie.Storage.Table_Read(useCache, lPtr, initRead);

            btValueSize = new byte[] { data[3], data[4], data[5], data[6] };

            /*NULL SUPPORT*/
            if ((data[3] & 0x80) > 0)
            {
                //NULL
                return null;
            }
            /**************/

            valueLength = btValueSize.To_UInt32_BigEndian();
            valueSize = (int)valueLength;

            /*NULL SUPPORT*/
            //if (valueSize == 0)
            //    return null;
            if (valueSize == 0)
                return new byte[0];
            /**************/

            //Checking startIndex and Length compliance with valueSize
            if (length == 0)
                return new byte[0];

            if (valueSize < startIndex)
                return null;

            if (valueSize < (startIndex + length))
                length = (uint)valueSize - startIndex;

            //--------------------------------------------------------

            protocol = data[0];
            keySize = (new byte[] { data[1], data[2] }).To_UInt16_BigEndian();


            switch (protocol)
            {
                case 0:
                    //We don't have reservation identifiers, it happens after first insert into the new place 

                    if ((keySize + valueSize + 7) > initRead)
                    {
                        //HERE READ ONLY PART OF THE VALUE

                        //original in read
                        //lPtr += 7 + keySize;
                        //val = this.DBStorage.Read(lPtr, valueSize);
                        valueStartPtr = lPtr + 7 + keySize;
                        lPtr += 7 + keySize + (int)startIndex;
                        val = Trie.Storage.Table_Read(useCache, lPtr, (int)length);
                    }
                    else
                    {
                        //HERE READ ONLY PART OF THE VALUE

                        //original in read
                        //val = data.Substring(7 + keySize, valueSize);

                        //in partial read
                        valueStartPtr = lPtr + 7 + keySize;
                        val = data.Substring(7 + keySize + (int)startIndex, (int)length);
                    }

                    break;
                case 1:
                    //btTotalReservedSize = new byte[] { data[7], data[8], data[9], data[10] };
                    //totalReservedSize = btTotalReservedSize.To_Int32_BigEndian();
                    if ((keySize + valueSize + 11) > initRead)
                    {
                        //HERE READ ONLY PART OF THE VALUE

                        //original in read
                        //lPtr += 11 + keySize;
                        //val = this.DBStorage.Read(lPtr, valueSize);

                        //in partial read
                        valueStartPtr = lPtr + 11 + keySize;
                        lPtr += 11 + keySize + (int)startIndex;
                        val = Trie.Storage.Table_Read(useCache, lPtr, (int)length);
                    }
                    else
                    {
                        //HERE READ ONLY PART OF THE VALUE

                        //original in read
                        //val = data.Substring(11 + keySize, valueSize);

                        //in partial read
                        valueStartPtr = lPtr + 11 + keySize;
                        val = data.Substring(11 + keySize + (int)startIndex, (int)length);
                    }
                    break;
            }

            return val;
        }

        #endregion
                

        
    
        public void Dispose()
        {           
            //_sync_dDynamicDataBlocks.EnterWriteLock();
            //try
            //{
                _sync_nodes.EnterWriteLock();
                try
                {
                    //_sync_values.EnterWriteLock();
                    //try
                    //{
                        //Clearing values
                        //_values.Clear();

                        //_values = null;

                        _nodes.Clear();

                        _nodes = null;

                        //dDynamicDataBlocks.Clear();
                        //dDynamicDataBlocks = null;
                    //}
                    //finally
                    //{
                    //    _sync_values.ExitWriteLock();
                    //}
                }
                finally
                {
                    _sync_nodes.ExitWriteLock();
                }
            //}
            //finally
            //{
            //    _sync_dDynamicDataBlocks.ExitWriteLock();
            //}
        }


    }
}
