/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Buffers.Binary;
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
        //A physical node pointer is unique within a table/cache epoch. Using it avoids
        //allocating and Base64-encoding the complete logical path on every lookup.
        Dictionary<ulong, byte[]> _nodes = new Dictionary<ulong, byte[]>();
        private int _disposed = 0;

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
                        _nodes = new Dictionary<ulong, byte[]>();

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
                catch
                {
                    throw;
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
                        _nodes = new Dictionary<ulong, byte[]>();

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
                catch
                {
                    throw;
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
                catch
            {
                //CASCADE
                throw;
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
                        _nodes = new Dictionary<ulong, byte[]>();

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
                catch
                {
                    //CASCADE
                    throw;
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
                        _nodes = new Dictionary<ulong, byte[]>();

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
                catch
                {
                    throw;
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
                        _nodes = new Dictionary<ulong, byte[]>();


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
                catch
                {
                    throw;
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
        /// <param name="pointer"></param>
        /// <returns></returns>
        public byte[] GetNodeKids(byte[] pointer)
        {
            ulong nodePointer = pointer.DynamicLength_To_UInt64_BigEndian();

            _sync_nodes.EnterReadLock();
            try
            {
                byte[] ret = null;
                _nodes.TryGetValue(nodePointer, out ret);

                return ret;
            }
                catch
            {
                throw;
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
        /// <param name="kidsBeforeModification"></param>
        public void GenerationNodeWritingOver(byte[] pointer, byte[] newData, byte[] kidsBeforeModification)
        {
            _sync_nodes.EnterWriteLock();
            try
            {
                ulong ptrU = pointer.DynamicLength_To_UInt64_BigEndian();


                if ((ulong)Trie.Storage.Length > ptrU)
                {
                    //Update only. Filling parallel read nodes.
                    byte[] ret = null;
                    _nodes.TryGetValue(ptrU, out ret);

                    if (ret == null)
                    {
                        if (kidsBeforeModification == null)
                            kidsBeforeModification = new byte[0];

                        _nodes[ptrU] = kidsBeforeModification;
                    }
                }
                
                Trie.Storage.Table_WriteByOffset(pointer, newData);
            }
                catch
            {
                throw;
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
        /// <param name="maximumNodeLineLength"></param>
        /// <returns></returns>
        public byte[] GenerationNodeRead(bool useCache, byte[] pointer, int maximumNodeLineLength)
        {
            byte[] node=null;

            if (useCache)
            {
                node = this.GetNodeKids(pointer);

                if (node != null)
                {
                    //An empty cached image means that the committed node had no kids; upper levels handle it.
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
                line = Trie.Storage.Table_Read(useCache, pointer, maximumNodeLineLength);
            }
                catch
            {
                throw;
            }
            finally
            {
                _sync_nodes.ExitReadLock();
            }
                                   

            if (line == null || line.Length < 2)
                return null;

            ushort sLen = BinaryPrimitives.ReadUInt16BigEndian(line);

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
                catch
            {                
                throw;
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
                catch
            {
                throw;
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
            ReadKeyValueCore(useCache, pointer, preferCommittedPointRead: false,
                out valueStartPtr, out valueLength, out key, out val);
        }

        /// <summary>
        /// Point-lookup variant.  A direct committed lookup has no sequential successor, so the
        /// disk implementation may use its memory-mapped view without disrupting traversal
        /// read-ahead.  Iterators deliberately continue to use <see cref="ReadKeyValue"/>.
        /// </summary>
        internal void ReadKeyValueCommittedPoint(byte[] pointer, out long valueStartPtr,
            out uint valueLength, out byte[] key, out byte[] val)
        {
            ReadKeyValueCore(useCache: true, pointer, preferCommittedPointRead: true,
                out valueStartPtr, out valueLength, out key, out val);
        }

        private void ReadKeyValueCore(bool useCache, byte[] pointer, bool preferCommittedPointRead,
            out long valueStartPtr, out uint valueLength, out byte[] key, out byte[] val)
        {
            if (!useCache)
            {
                ReadUncommittedKeyValue(pointer, out valueStartPtr, out valueLength, out key, out val);
                return;
            }
            const int initialRead = 111;
            long record = checked((long)pointer.DynamicLength_To_UInt64_BigEndian());
            long probeEpoch = 0;
            Span<byte> probe = stackalloc byte[initialRead];
            ReadOnlySpan<byte> data = probe;
            byte[] allocatedData = null;
            StorageLayer storage = Trie.Storage as StorageLayer;
            if (!(storage != null && storage.TryTableReadCommittedInto(record, probe, out probeEpoch, preferCommittedPointRead)))
            {
                allocatedData = preferCommittedPointRead && storage != null
                    ? storage.ReadCommittedPointProbe(record, initialRead, out probeEpoch)
                    : Trie.Storage.ReadRecordProbe(true, record, initialRead, out probeEpoch);
                data = allocatedData;
            }
            var header = new ValueRecordHeader(data);
            DurabilityTestHooks.Hit("read.eager-probe");

            // A complete single probe is already atomic. Keep allocation and key/
            // value conversion outside storage admission, as in the original path.
            if (header.RecordSize > data.Length)
            {
                // Header and payload may use separate admissions only if the epoch
                // still matches INSIDE the second admission. A racing publication
                // retries the header while retaining that admission.
                if (header.Size + header.KeySize <= data.Length)
                {
                    // The probe already owns the key. Read directly into the
                    // final value array instead of copying the entire record
                    // and then allocating/copying its value a second time.
                    val = Trie.Storage.ReadCommittedPayload(record, probeEpoch, preferCommittedPointRead, ref header, out byte[] refreshedKey);
                    if (refreshedKey != null)
                        data = refreshedKey;
                    key = data.Slice(header.Size, header.KeySize).ToArray();
                    valueLength = (uint)header.ValueSize;
                    valueStartPtr = header.ValueSize == 0 ? 0 : checked(record + header.Size + header.KeySize);
                    return;
                }
                data = Trie.Storage.ExpandCommittedRecord(record, allocatedData, probeEpoch, preferCommittedPointRead, ref header);
            }

            CopyRecordValue(record, data, header, out valueStartPtr, out valueLength, out key, out val);
        }

        private void ReadUncommittedKeyValue(byte[] pointer, out long valueStartPtr,
            out uint valueLength, out byte[] key, out byte[] val)
        {
            long record = checked((long)pointer.DynamicLength_To_UInt64_BigEndian());
            byte[] data = Trie.Storage.Table_Read(false, record, 4096);
            var header = new ValueRecordHeader(data);
            if (header.RecordSize > data.Length)
                data = Trie.Storage.Table_ReadRecordContinuation(false, record, record, header.RecordSize);
            CopyRecordValue(record, data, header, out valueStartPtr, out valueLength, out key, out val);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void CopyRecordValue(long record, ReadOnlySpan<byte> data, ValueRecordHeader header,
            out long valueStartPtr, out uint valueLength, out byte[] key, out byte[] val)
        {
            key = data.Slice(header.Size, header.KeySize).ToArray();
            valueLength = (uint)header.ValueSize;
            valueStartPtr = header.ValueSize == 0 ? 0 : checked(record + header.Size + header.KeySize);
            val = header.IsNull ? null : header.ValueSize == 0 ? Array.Empty<byte>() : data.Slice(header.Size + header.KeySize, header.ValueSize).ToArray();
        }

        public byte[] ReadKey(bool useCache, byte[] pointer)
        {
            if (!useCache)
                return ReadUncommittedKey(pointer);
            return ReadKey(useCache, pointer, out _, out _, out _);
        }

        // Insert/update only needs the existing key. Do not make the writer
        // pay for value metadata and a committed-view epoch it cannot use.
        private byte[] ReadUncommittedKey(byte[] pointer)
        {
            long record = checked((long)pointer.DynamicLength_To_UInt64_BigEndian());
            byte[] data = Trie.Storage.Table_Read(false, record, 111);
            int headerSize = data[0] == 0 ? 7 : data[0] == 1 ? 11 : 0;
            if (headerSize == 0)
                return null;
            ushort keySize = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(1, 2));
            if (headerSize + keySize > data.Length)
                data = Trie.Storage.Table_Read(false, record, headerSize + keySize);
            return data.Substring(headerSize, keySize);
        }

        internal byte[] ReadKey(bool useCache, byte[] pointer, out long valueStartPtr,
            out uint valueLength, out bool valueIsNull)
        {
            return ReadKey(useCache, pointer.DynamicLength_To_UInt64_BigEndian(),
                out valueStartPtr, out valueLength, out valueIsNull);
        }

        internal byte[] ReadKey(bool useCache, byte[] pointer, out long valueStartPtr,
            out uint valueLength, out bool valueIsNull, out long epoch)
        {
            return ReadKey(useCache, pointer.DynamicLength_To_UInt64_BigEndian(), out valueStartPtr,
                out valueLength, out valueIsNull, out epoch);
        }

        internal byte[] ReadKey(bool useCache, ulong pointer, out long valueStartPtr,
            out uint valueLength, out bool valueIsNull)
        {
            return ReadKey(useCache, pointer, out valueStartPtr, out valueLength, out valueIsNull, out _);
        }

        internal byte[] ReadKey(bool useCache, ulong pointer, out long valueStartPtr,
            out uint valueLength, out bool valueIsNull, out long epoch)
        {
            long record = checked((long)pointer);
            byte[] data = Trie.Storage.ReadRecordProbe(useCache, record, 111, out epoch);
            int headerSize = data[0] == 0 ? 7 : 11;
            ushort keySize = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(1, 2));
            if (headerSize + keySize > data.Length)
                return ReadLongKey(useCache, pointer, data, epoch, out valueStartPtr, out valueLength, out valueIsNull, out epoch);
            valueIsNull = (data[3] & 0x80) != 0;
            valueLength = valueIsNull ? 0 : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(3, 4));
            valueStartPtr = record + headerSize + keySize;
            return data.Substring(headerSize, keySize);
        }

        private byte[] ReadLongKey(bool useCache, ulong pointer, byte[] data, long probeEpoch, out long valueStartPtr,
            out uint valueLength, out bool valueIsNull, out long epoch)
        {
            using var reader = Trie.Storage.BeginRecordRead(useCache);
            epoch = reader.Epoch;
            valueStartPtr = -1;
            valueLength = 0;
            valueIsNull = false;

            //Changing format:
            //1byte - protocol, FullKeyLen (2 bytes), FullValueLen (4 bytes),[Reserved Space For Update- 4 bytes],FullKey,FullValue
            //1 + 2 + 4 + 4 + 100 = 111
            int initRead = 111; //Where 100 is standard max size of the key in case of words (even 50 must be enough), in case of sentences can be much longer, probably we can setup it later

            //DONT NEED TO EnlargeByteArray_BigEndian(8) ptr, because it's automatically done in TrieDisk Storage etc..

            //byte[] data = this._root.Tree.Storage.Read(ptr.EnlargeByteArray_BigEndian(8), initRead);
            long recordPointer = checked((long)pointer);
            // Reuse the first atomic probe when no publication intervened. This
            // avoids adding a third read/array to normal long-key lookups.
            if (useCache && (probeEpoch == 0 || reader.Epoch != probeEpoch))
                data = reader.Read(recordPointer, initRead);
            byte protocol = data[0];
            ushort keySize = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(1, 2));
            valueIsNull = (data[3] & 0x80) != 0;
            if (!valueIsNull)
                valueLength = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(3, 4));


            byte[] key = null;



            switch (protocol)
            {
                case 0:
                    //First insert - no reservation identifiers for the space

                    //Expanding read if necessary
                    if (keySize > (initRead - 7))   //>= ?
                    {
                        initRead = keySize + 7;
                        data = reader.Read(recordPointer, initRead);
                    }

                    key = data.Substring(7, keySize);
                    valueStartPtr = recordPointer + 7 + keySize;

                    break;
                case 1:
                    //Expanding read if necessary
                    if (keySize > (initRead - 11))  //>= ?
                    {
                        initRead = keySize + 11;
                        data = reader.Read(recordPointer, initRead);
                    }

                    key = data.Substring(11, keySize);
                    valueStartPtr = recordPointer + 11 + keySize;

                    break;
            }



            return key;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal bool ReadKeyMatches(bool useCache, ulong pointer, byte[] expectedKey,
            out long valueStartPtr, out uint valueLength, out bool valueIsNull, out long epoch)
        {
            // Short-key bytes and their epoch are captured by one backend probe.
            // Only a multi-read key needs to retain admission across probes.
            return expectedKey.Length <= 100
                ? ReadShortKeyMatches(useCache, pointer, expectedKey, out valueStartPtr, out valueLength, out valueIsNull, out epoch)
                : ReadLongKeyMatches(useCache, pointer, expectedKey, out valueStartPtr, out valueLength, out valueIsNull, out epoch);
        }

        private bool ReadShortKeyMatches(bool useCache, ulong pointer, byte[] expectedKey,
            out long valueStartPtr, out uint valueLength, out bool valueIsNull, out long epoch)
        {
            epoch = 0;
            valueStartPtr = -1;
            valueLength = 0;
            valueIsNull = false;
            const int initialRead = 111;
            long recordPointer = checked((long)pointer);
            Span<byte> probe = stackalloc byte[initialRead];
            byte[] allocatedData = null;
            ReadOnlySpan<byte> data = probe;
            if (!(useCache && Trie.Storage is StorageLayer storage &&
                storage.TryTableReadCommittedInto(recordPointer, probe, out epoch)))
            {
                allocatedData = Trie.Storage.ReadRecordProbe(useCache, recordPointer, initialRead, out epoch);
                data = allocatedData;
            }
            if (data.Length < 7)
                return false;

            byte protocol = data[0];
            ushort keySize = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2));
            valueIsNull = (data[3] & 0x80) != 0;
            if (!valueIsNull)
                valueLength = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(3, 4));

            int headerSize;
            switch (protocol)
            {
                case 0: headerSize = 7; break;
                case 1: headerSize = 11; break;
                default: return false;
            }

            if (keySize != expectedKey.Length)
                return false;
            if (headerSize + keySize > data.Length)
                return ReadLongKeyMatches(useCache, pointer, expectedKey, out valueStartPtr, out valueLength, out valueIsNull, out epoch);
            if (!data.Slice(headerSize, keySize).SequenceEqual(expectedKey))
                return false;

            valueStartPtr = recordPointer + headerSize + keySize;
            return true;
        }


        private bool ReadLongKeyMatches(bool useCache, ulong pointer, byte[] expectedKey,
            out long valueStartPtr, out uint valueLength, out bool valueIsNull, out long epoch)
        {
            using var reader = Trie.Storage.BeginRecordRead(useCache, point: true);
            epoch = reader.Epoch;
            valueStartPtr = -1;
            valueLength = 0;
            valueIsNull = false;
            const int initialRead = 111;
            long recordPointer = checked((long)pointer);
            Span<byte> probe = stackalloc byte[initialRead];
            byte[] allocatedData = null;
            ReadOnlySpan<byte> data = probe;
            if (!(reader.TryReadInto(recordPointer, probe)))
            {
                allocatedData = reader.Read(recordPointer, initialRead);
                data = allocatedData;
            }
            if (data.Length < 7)
                return false;

            byte protocol = data[0];
            ushort keySize = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2));
            valueIsNull = (data[3] & 0x80) != 0;
            if (!valueIsNull)
                valueLength = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(3, 4));

            int headerSize;
            switch (protocol)
            {
                case 0: headerSize = 7; break;
                case 1: headerSize = 11; break;
                default: return false;
            }

            if (keySize != expectedKey.Length)
                return false;
            if (headerSize + keySize > data.Length)
            {
                allocatedData = reader.Read(recordPointer, headerSize + keySize);
                data = allocatedData;
            }
            if (!data.Slice(headerSize, keySize).SequenceEqual(expectedKey))
                return false;

            valueStartPtr = recordPointer + headerSize + keySize;
            return true;
        }


        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal byte[] ReadCommittedFullValue(LTrieRow row) =>
            Trie.Storage.ReadCommittedValue(row, row.PreferCommittedPointRead, full: true, 0, 0);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal byte[] ReadCommittedValue(LTrieRow row, uint startIndex, uint length) =>
            Trie.Storage.ReadCommittedValue(row, row.PreferCommittedPointRead, full: false, startIndex, length);

        public byte[] ReadValue(byte[] pointer, bool useCache, out long valueStartPtr, out uint valueLength)
        {
            using var reader = Trie.Storage.BeginRecordRead(useCache);
            valueLength = 0;
            valueStartPtr = -1;

            if (pointer._IfPointerIsEmpty(DefaultPointerLen))
                return null;


            //Changing format:
            //1 byte protocol type, FullKeyLen (2 bytes), FullValueLen (4 bytes), [ReservedSpace], FullKey,FullValue        
            // Positioned reader I/O has no FileStream read-ahead to amortize the historical
            // 4 KiB probe. Writer-side delete/update paths, however, use the complete record
            // and benefit from the original single read for common values.
            int initRead = useCache ? 111 : 4096;

            byte[] data = null;
            byte protocol = 0;
            ushort keySize = 0;
            int valueSize = 0;
            long lPtr = 0;
            byte[] val = null;
                        
            lPtr = (long)pointer.DynamicLength_To_UInt64_BigEndian();

            data = reader.Read(lPtr, initRead);

            /*NULL SUPPORT*/
            if ((data[3] & 0x80) > 0)
            {
                //NULL
                return null;
            }
            /**************/

            valueLength = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(3, 4));
            if (valueLength > Int32.MaxValue)
                throw new InvalidOperationException("Stored value length exceeds the maximum supported array size.");
            valueSize = (int)valueLength;

            /*NULL SUPPORT*/
            //if (valueSize == 0)
            //    return null;
            if (valueSize == 0)
                return Array.Empty<byte>();
            /**************/

            protocol = data[0];
            keySize = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(1, 2));


            DurabilityTestHooks.Hit("read.record-header");
            switch (protocol)
            {
                case 0:
                    //We don't have reservation identifiers, it happens after first insert into the new place 

                    if ((keySize + valueSize + 7) > initRead)
                    {
                        lPtr += 7 + keySize;
                        valueStartPtr = lPtr;
                        val = reader.Read(lPtr, valueSize, (long)pointer.DynamicLength_To_UInt64_BigEndian());
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
                        val = reader.Read(lPtr, valueSize, (long)pointer.DynamicLength_To_UInt64_BigEndian());
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
            using var reader = Trie.Storage.BeginRecordRead(useCache);
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
            long lPtr = 0;
            byte[] val = null;

            //Reading from disk inside of lock
            //lPtr = (long)pointer.EnlargeByteArray_BigEndian(8).To_UInt64_BigEndian();
            lPtr = (long)pointer.DynamicLength_To_UInt64_BigEndian();

            data = reader.Read(lPtr, initRead);

            /*NULL SUPPORT*/
            if ((data[3] & 0x80) > 0)
            {
                //NULL
                return null;
            }
            /**************/

            valueLength = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(3, 4));
            if (valueLength > Int32.MaxValue)
                throw new InvalidOperationException("Stored value length exceeds the maximum supported array size.");
            valueSize = (int)valueLength;

            /*NULL SUPPORT*/
            //if (valueSize == 0)
            //    return null;
            if (valueSize == 0)
                return Array.Empty<byte>();
            /**************/

            //Checking startIndex and Length compliance with valueSize
            if (length == 0)
                return Array.Empty<byte>();

            if (startIndex > (uint)valueSize)
                return null;

            uint availableLength = (uint)valueSize - startIndex;
            if (length > availableLength)
                length = availableLength;

            if (length == 0)
                return Array.Empty<byte>();

            //--------------------------------------------------------

            protocol = data[0];
            keySize = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(1, 2));


            DurabilityTestHooks.Hit("read.record-header");
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
                        val = reader.Read(lPtr, (int)length, (long)pointer.DynamicLength_To_UInt64_BigEndian());
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
                        val = reader.Read(lPtr, (int)length, (long)pointer.DynamicLength_To_UInt64_BigEndian());
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
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

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
                    _sync_nodes.Dispose();
                }
            //}
            //finally
            //{
            //    _sync_dDynamicDataBlocks.ExitWriteLock();
            //}
        }


    }
}
