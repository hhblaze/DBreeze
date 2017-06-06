/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.Storage;
using DBreeze.Exceptions;
using DBreeze.LianaTrie.Iterations;

using DBreeze.Transactions;
using DBreeze.Tries;
using DBreeze.DataTypes;
using DBreeze.Utils;

namespace DBreeze.LianaTrie
{
    /// <summary>
    /// Liana Trie
    /// </summary>
    public class LTrie : ITrie, ITransactable, IDisposable
    {
        internal IStorage Storage;

        //Write Root Node, THE ONLY FOR WRITING FUNCTIONS.
        //Reading Functions create every time its own root node
        internal LTrieRootNode rn = null;
        //internal object LockRootNode = new object();

        /// <summary>
        /// Cache for overwriting nodes and values
        /// </summary>
        internal LTrieWriteCache Cache = null;

        //Indicates that table is operatable
        private bool TableIsOperable = true;
        //Locker
        //private object lock_TableIsOperatable = new object();

        //string rollBackFileName = String.Empty;

        /// <summary>
        /// Identifies that after Saving0generationNode was made no changes (Add,Remove etc)
        /// Used via TableIsModified
        /// </summary>
        internal bool TableIsModified = false;

        bool GenerationMapSaved = true;

        /// <summary>
        /// Is by Commit and Rollback only, we will use it to return correct ReadRootNodes out to the system
        /// Access via DtTableFixed interface ITrie
        /// </summary>
        long _DtTableFixed = (new DateTime(1970, 1, 1)).Ticks;

        /// <summary>
        /// Coordinator of nested tables
        /// </summary>
        internal NestedTablesCoordinator NestedTablesCoordinator = new NestedTablesCoordinator();
        internal bool NestedTable = false;

        /// <summary>
        /// Concerns Nodes, Values, DataBlocks.
        /// Flag can be setup only via nested table or transaction
        /// </summary>
        internal bool OverWriteIsAllowed = true;

        ///// <summary>
        ///// When it's on iterators, Select and SelectDirect return Row with the key and a pointer to the value.
        ///// <par>Value will be read out when we call it Row.Value.</par>
        ///// <pa>When it's off we read value together with the key in one round</pa>
        ///// </summary>
        //public bool ValuesLazyLoadingIsOn = true;


        /// <summary>
        /// Liana Trie
        /// </summary>
        /// <param name="storage"></param>
        public LTrie(IStorage storage)
        {
            Storage = storage;

            try
            {
                //Will instantiate also RollerBack
                Cache = new LTrieWriteCache(this);

                //If first reading or writing of root node fails also bring to exception
                rn = new LTrieRootNode(this);

                //rollBackFileName = Cache.RollBackFileName;
            }
            catch (Exception ex)
            {
                //lock (lock_TableIsOperatable)
                TableIsOperable = false;

                 throw new Exception(String.Format("LTrie init failed: {0}; Exception: {1}", this.TableName, ex.ToString()));
            }
           
        }

        public void Dispose()
        {            
            TableIsOperable = false;
                       
            this.NestedTablesCoordinator.Dispose();

            if (!NestedTable)
            {
                try
                {
                    Storage.Table_Dispose();
                }
                catch (Exception ex)
                {
                    //throw ex;
                }

                try
                {
                    Cache.Dispose();
                }
                catch (Exception ex)
                {

                }
            }
        }
              

        /// <summary>
        /// Will return exception, if not.
        /// Must be called by all functions
        /// </summary>
        private void CheckTableIsOperable()
        {


            if (!TableIsOperable)
                throw new TableNotOperableException(this.TableName);                
            
        }

        public void Commit()
        {
            //Only available for Writing root
            this.CheckTableIsOperable();

            this.NestedTablesCoordinator.ModificationThreadId = -1;

            if (!TableIsModified)
                return;

            try
            {

                /*Support nested tables*/
                this.NestedTablesCoordinator.Commit();
                /***********************/

                rn.Commit();

                //this.NestedTablesCoordinator.CloseAll();
             
                TableIsModified = false;
                GenerationMapSaved = true;
                DtTableFixed = DateTime.Now.Ticks;
            }
            catch (Exception ex)
            {
                //Trying Rollback
                this.RollBack();
                
                throw ex;
            }
            
            
        }

        object lock_rollback = new object();

        /// <summary>
        /// RollBack
        /// </summary>
        public void RollBack()
        {
            this.CheckTableIsOperable();

            this.NestedTablesCoordinator.ModificationThreadId = -1;

            if (!TableIsModified)
                return;

            try
            {
                lock (lock_rollback)
                {
                    if (!TableIsModified)
                        return;

                    /*Support nested tables*/
                    this.NestedTablesCoordinator.Rollback();
                    /***********************/

                    //Only available for Writing root
                    rn.RollBack();

                    TableIsModified = false;
                    GenerationMapSaved = true;
                    DtTableFixed = DateTime.Now.Ticks;
                }
            }
            catch (Exception ex)
            {
                TableIsOperable = false;
                throw new TableNotOperableException(this.TableName,ex);                                
            }
            
        }

        //object lock_nestedTblAccess = new object();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="row"></param>
        /// <param name="btKey"></param>
        /// <param name="tableIndex"></param>
        /// <param name="masterTrie"></param>
        /// <param name="insertTable">Regulates if InsertTable or SelectTable was called (ability to create table if it doesn't exist)</param>
        /// <param name="useCache">Regulates READ table thread or WRITE table thread - visibilityscope</param>
        /// <returns></returns>
        public NestedTable GetTable(LTrieRow row, ref byte[] btKey, uint tableIndex, LTrie masterTrie, bool insertTable, bool useCache)
        {
            //Console.WriteLine(useCache.ToString() + " " +  System.Threading.Thread.CurrentThread.ManagedThreadId.ToString());

            try
            {
                
                if (!insertTable && !row.Exists)
                {
                    //For Readers, which couldn't allocate table, we return empty DbInTable
                    return new NestedTable(null, false, false);
                }

                if (insertTable)
                {
                    //settign up modification thread
#if NET35 || NETr40
                    this.NestedTablesCoordinator.ModificationThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

#else
                    this.NestedTablesCoordinator.ModificationThreadId = Environment.CurrentManagedThreadId;
                   
#endif


                }


                if (masterTrie == null)
                    masterTrie = this;

                NestedTableInternal dit = null; //this one we will return

                byte[] fullValuePointer = null;

                if (insertTable)
                    TableIsModified = true; //flag will permit commit and rollback



                byte[] val = null;

                long Id_RootStart = 0;

                if (row.Exists)
                {

                    val = row.GetPartialValue(tableIndex * this.Storage.TrieSettings.ROOT_SIZE, this.Storage.TrieSettings.ROOT_SIZE, useCache);

                    if (val == null || val.Length < this.Storage.TrieSettings.ROOT_SIZE)
                    {
                        //For Readers, which couldn't allocate table, we return empty DbInTable
                        if (!insertTable)
                        {
                            return new NestedTable(null, false, false);
                        }

                        //Here can appear only WRITERS, which couldn't allocate table

                        byte[] btValue = new byte[this.Storage.TrieSettings.ROOT_SIZE];

                        long valueStartPointer = 0;

                        fullValuePointer = this.AddPartially(ref btKey, ref btValue, tableIndex * this.Storage.TrieSettings.ROOT_SIZE, out valueStartPointer);

                        Id_RootStart = valueStartPointer + (tableIndex * this.Storage.TrieSettings.ROOT_SIZE);

                        lock (this.NestedTablesCoordinator.lock_nestedTblAccess)
                        {
                            dit = new DataTypes.NestedTableInternal(true, masterTrie, Id_RootStart, (tableIndex * this.Storage.TrieSettings.ROOT_SIZE), false,this,ref btKey);
                            this.NestedTablesCoordinator.AddNestedTable(ref btKey, fullValuePointer.DynamicLength_To_UInt64_BigEndian(), Id_RootStart, dit);

                            //if (!insertTable)
                            //{
                                //If we open the table for read, we increase read value
                                dit.quantityOpenReads++;
                            //}
                        }

                    }
                    else
                    {
                        //then we must open/create DbInTable

                        Id_RootStart = row.ValueStartPointer + (tableIndex * this.Storage.TrieSettings.ROOT_SIZE);
                        fullValuePointer = row.LinkToValue;


                        lock (this.NestedTablesCoordinator.lock_nestedTblAccess)
                        {
                            dit = this.NestedTablesCoordinator.GetTable(ref btKey, Id_RootStart);

                            if (dit == null)
                            {
                                dit = new DataTypes.NestedTableInternal(true, masterTrie, Id_RootStart, (tableIndex * this.Storage.TrieSettings.ROOT_SIZE), useCache, this, ref btKey);
                                this.NestedTablesCoordinator.AddNestedTable(ref btKey, fullValuePointer.DynamicLength_To_UInt64_BigEndian(), Id_RootStart, dit);
                            }                           

                            //if (!insertTable)
                            //{
                                //If we open the table for read, we increase read value
                                dit.quantityOpenReads++;
                            //}
                        }

                    }
                }
                else
                {
                    //Every new row with DbInTable starts from here

                    //Here can appear only WRITERS, which couldn't allocate table

                    //creating space for the root
                    byte[] btValue = new byte[this.Storage.TrieSettings.ROOT_SIZE];
                    //adding empty root to the place
                    long valueStartPointer = 0;

                    fullValuePointer = this.AddPartially(ref btKey, ref btValue, tableIndex * this.Storage.TrieSettings.ROOT_SIZE, out valueStartPointer);

                    Id_RootStart = valueStartPointer + (tableIndex * this.Storage.TrieSettings.ROOT_SIZE);

                    lock (this.NestedTablesCoordinator.lock_nestedTblAccess)
                    {
                        dit = new DataTypes.NestedTableInternal(true, masterTrie, Id_RootStart, (tableIndex * this.Storage.TrieSettings.ROOT_SIZE), false, this, ref btKey);
                        this.NestedTablesCoordinator.AddNestedTable(ref btKey, fullValuePointer.DynamicLength_To_UInt64_BigEndian(), Id_RootStart, dit);

                        //if (!insertTable)
                        //{
                            //If we open the table for read, we increase read value
                            dit.quantityOpenReads++;
                        //}
                    }
                }

                //Console.WriteLine("Creating NestedTable " + btKey.ToBytesString(""));

                //this.NestedTablesCoordinator.AddNestedTable(ref btKey, fullValuePointer.DynamicLength_To_UInt64_BigEndian(), Id_RootStart, dit);


                return new NestedTable(dit, insertTable, true);
                
            }
            catch (Exception ex)
            {
                this.RollBack();
                //Cascade
                throw ex;
            }
        }


        public byte[] InsertDataBlock(ref byte[] initialPtr, ref byte[] data)
        {
            TableIsModified = true;
            return this.Cache.WriteDynamicDataBlock(ref initialPtr, ref data);            
        }

        public byte[] SelectDataBlock(ref byte[] initialPtr, bool useCache)
        {
            this.CheckTableIsOperable();

            return this.Cache.ReadDynamicDataBlock(ref initialPtr, useCache);            
        }


        /// <summary>
        /// Adds key. Overload without refs
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public byte[] Add(byte[] key, byte[] value)
        {
            bool WasUpdated = false;
            return this.Add(ref key, ref value, out WasUpdated,false);            
        }

        public byte[] Add(ref byte[] key, ref byte[] value)
        {
            bool WasUpdated = false;
            return this.Add(ref key, ref value, out WasUpdated, false);   
        }

        public byte[] Add(ref byte[] key, ref byte[] value, out bool WasUpdated)
        {
            return this.Add(ref key, ref value, out WasUpdated, false);   
        }


        /// <summary>
        /// Adds key
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="WasUpdated">indicates that key we insert, already existed in the system and was updated</param>
        /// <param name="dontUpdateIfExists">When true - if value exists, we dont update it. If WasUpdated = true then we value exists, if false - we have inserted new one</param>
        /// <returns>returns physical link to value</returns>
        public byte[] Add(ref byte[] key, ref byte[] value, out bool WasUpdated, bool dontUpdateIfExists)
        {
            //indicates that key we insert, already existed in the system and was updated
            WasUpdated = false;

            this.CheckTableIsOperable();
            byte[] linkToVal = null;
            try
            {

                //Only available for Writing root
                try
                {
                    linkToVal = rn.AddKey(ref key, ref value, out WasUpdated, dontUpdateIfExists);
                }
                catch (Exception ex1) 
                {
                    throw ex1;
                }
                

                /*********** Support of the nested tables  *******/
                this.NestedTablesCoordinator.Remove(ref key);
                /*************************************************/

                TableIsModified = true;
                GenerationMapSaved = false;

                return linkToVal;
            }
            catch (Exception ex)
            {
                this.RollBack();
                //Cascade
                throw ex;
            }            
        }


        /// <summary>
        /// Overload without refs
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="startIndex"></param>
        /// <returns></returns>
        public byte[] AddPartially(byte[] key, byte[] value, uint startIndex, out long valueStartPtr)
        {
            bool WasUpdated = false;
            return this.AddPartially(ref key, ref value, startIndex,out valueStartPtr,out WasUpdated);
        }

        /// <summary>
        /// REMEMBER THAT 
        /// all keys are first formed in memory and then copied to the disk, so it's not for storing movies inside of the value.
        /// For storing movies (BLOBs) will be used other approach, see docu.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="startIndex"></param>
        /// <returns></returns>
        public byte[] AddPartially(ref byte[] key, ref byte[] value,uint startIndex,out long valueStartPtr)
        {
            bool WasUpdated = false;
            return this.AddPartially(ref key, ref value, startIndex, out valueStartPtr,out WasUpdated);

            //this.CheckTableIsOperable();
            //byte[] linkToVal = null;
            //try
            //{
            //    //Only available for Writing root
            //    linkToVal = rn.AddKeyPartially(ref key, ref value, startIndex, out valueStartPtr);

            //    /*********** Support of the nested tables  *******/
            //    this.NestedTablesCoordinator.MoveNestedTablesRootStart(ref key, linkToVal.DynamicLength_To_UInt64_BigEndian(), valueStartPtr);
            //    /*************************************************/

            //    TableIsModified = true;
            //    GenerationMapSaved = false;

            //    return linkToVal;
            //}
            //catch (Exception ex)
            //{
            //    this.RollBack();
            //    //Cascade
            //    throw ex;
            //}
        }

        /// <summary>
        /// REMEMBER THAT 
        /// all keys are first formed in memory and then copied to the disk, so it's not for storing movies inside of the value.
        /// For storing movies (BLOBs) will be used other approach, see docu.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="startIndex"></param>
        /// <param name="WasUpdated"></param>
        /// <returns></returns>
        public byte[] AddPartially(ref byte[] key, ref byte[] value, uint startIndex, out long valueStartPtr, out bool WasUpdated)
        {
            this.CheckTableIsOperable();
            byte[] linkToVal = null;
            try
            {
                //Only available for Writing root
                linkToVal = rn.AddKeyPartially(ref key, ref value, startIndex, out valueStartPtr, out WasUpdated);

                /*********** Support of the nested tables  *******/
                this.NestedTablesCoordinator.MoveNestedTablesRootStart(ref key, linkToVal.DynamicLength_To_UInt64_BigEndian(), valueStartPtr);
                /*************************************************/

                TableIsModified = true;
                GenerationMapSaved = false;

                return linkToVal;
            }
            catch (Exception ex)
            {
                this.RollBack();
                //Cascade
                throw ex;
            }
        }



        public void Remove(ref byte[] key)
        {
            bool WasRemoved = false;
            byte[] deletedValue = null;
            Remove(ref key, out WasRemoved, false, out deletedValue);

            ////Only available for Writing root
            //this.CheckTableIsOperable();

            //try
            //{                
            //    rn.RemoveKey(ref key);

            //    /*********** Support of the nested tables  *******/
            //    this.NestedTablesCoordinator.Remove(ref key);
            //    /*************************************************/

            //    TableIsModified = true;
            //    GenerationMapSaved = false;
            //}
            //catch (Exception ex)
            //{
            //    this.RollBack();

            //    throw ex;
            //}
            
        }

        /// <summary>
        /// Removes the key
        /// </summary>
        /// <param name="key"></param>
        /// <param name="WasRemoved">indicates that value existed if true</param>
        /// <param name="retrieveDeletedValue">indicates if we should bind deleted value to the result</param>
        /// <param name="deletedValue">interesting only if WasRemoved = true and retrieveDeletedValue is true</param>
        public void Remove(ref byte[] key, out bool WasRemoved, bool retrieveDeletedValue, out byte[] deletedValue)
        {
            WasRemoved = false;
            deletedValue = null;

            //Only available for Writing root
            this.CheckTableIsOperable();

            try
            {
                rn.RemoveKey(ref key, out WasRemoved, retrieveDeletedValue, out deletedValue);

                /*********** Support of the nested tables  *******/
                this.NestedTablesCoordinator.Remove(ref key);
                /*************************************************/

                TableIsModified = true;
                GenerationMapSaved = false;
            }
            catch (Exception ex)
            {
                this.RollBack();

                throw ex;
            }

        }

        public void RemoveAll(bool withFileRecreation)
        {
            //Only available for Writing root
            this.CheckTableIsOperable();

            try
            {
                /*********** Support of the nested tables  *******/
                if (withFileRecreation)
                {

                    this.NestedTablesCoordinator.Dispose();

                }
                else
                {
                    this.NestedTablesCoordinator.RemoveAll();
                }
                /*************************************************/


                rn.RemoveAll(withFileRecreation);
                


                TableIsModified = true;
                GenerationMapSaved = false;
            }
            catch (Exception ex)
            {
                if (!withFileRecreation)
                {
                    this.RollBack();
                }
                else
                {
                    TableIsOperable = false;
                }

                throw ex;
            }
            
        }

        public void ChangeKey(ref byte[] oldKey, ref byte[] newKey,out byte[] ptrToNewKey,out bool WasChanged)
        {
            ptrToNewKey = null;
            WasChanged = false;

            //Only available for Writing root
            this.CheckTableIsOperable();

            try
            {
                bool changeResult = rn.ChangeKey(ref oldKey, ref newKey,out ptrToNewKey);
                WasChanged = changeResult;

                if (changeResult)
                {

                    /*********** Support of the nested tables  *******/
                    if (this.NestedTablesCoordinator.IfKeyIsInNestedList(ref oldKey))
                    {
                        var row = rn.GetKey(newKey, false, true);
                        this.NestedTablesCoordinator.ChangeKeyAndMoveNestedTablesRootStart(ref oldKey, ref newKey, row.LinkToValue.DynamicLength_To_UInt64_BigEndian(), row.ValueStartPointer);
                    }
                    /*************************************************/

                    TableIsModified = true;
                    GenerationMapSaved = false;
                }
            }
            catch (Exception ex)
            {
                this.RollBack();

                throw ex;
            }
        }

        public void ChangeKey(ref byte[] oldKey, ref byte[] newKey)
        {
            //Only available for Writing root
            this.CheckTableIsOperable();

            try
            {
                bool changeResult = rn.ChangeKey(ref oldKey, ref newKey);

                if (changeResult)
                {

                    /*********** Support of the nested tables  *******/
                    if(this.NestedTablesCoordinator.IfKeyIsInNestedList(ref oldKey))
                    {
                        var row = rn.GetKey(newKey, false, true);
                        this.NestedTablesCoordinator.ChangeKeyAndMoveNestedTablesRootStart(ref oldKey, ref newKey, row.LinkToValue.DynamicLength_To_UInt64_BigEndian(), row.ValueStartPointer);
                    }
                    /*************************************************/

                    TableIsModified = true;
                    GenerationMapSaved = false;
                }
            }
            catch (Exception ex)
            {
                this.RollBack();

                throw ex;
            }
        }
        
        
        /// <summary>
        /// Technical function.
        /// Used by Fetch SYNCHRO_READ FUNCs, which use write root node, to make last in-memory changes to flash on the disk, before commit.
        /// </summary>
        private void SaveGenerationMap()
        {
            if (!TableIsModified)
                return;

            try
            {
                if (!GenerationMapSaved)
                {
                    rn.Save_GM_nodes_Starting_From(0);
                    //To the same state brings Commit and Rollback
                    GenerationMapSaved = true;
                }


            }
            catch (Exception ex)
            {
                this.RollBack();

                throw ex;
            }
        }

        
        /// <summary>
        /// Interface function which recreates every time new rootNode from itself by every new function call.
        /// and also packs root node last fixation dateTime (ROLL or COMMIT).
        /// It will be used for READ FUNC's via Transaction, they can decide if to create new instance of read root or use existing.
        /// Returns NULL is !TableIsOperable.
        /// </summary>
        /// <param name="modifiedDt"></param>
        /// <returns></returns>
        public ITrieRootNode GetTrieReadNode(out long dtTableFixed)
        {
            dtTableFixed = DtTableFixed;

            if (!TableIsOperable)
                return null;

            LTrieRootNode readRootNode = new LTrieRootNode(this);
            return readRootNode;
        }



        /// <summary>
        /// if useCache = true; uses newly created root node, else uses writing root node
        /// </summary>
        /// <returns></returns>
        public ulong Count(bool useCache)
        {
            this.CheckTableIsOperable();

            if (useCache)
            {
                LTrieRootNode readRootNode = new LTrieRootNode(this);
                return readRootNode.RecordsCount;
            }
            else
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();
                return rn.RecordsCount;
            }
        }

        /// <summary>
        /// Can be used inside of DBreeze - concerns all read functions
        /// </summary>
        /// <param name="SYNCHRO_READ"></param>
        /// <returns></returns>
        public ulong Count(ITrieRootNode readRootNode)
        {
            this.CheckTableIsOperable();

            if (readRootNode==null)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                return rn.RecordsCount;

            }
            else
            {
                //LTrieRootNode readRootNode = new LTrieRootNode(this);
                return ((LTrieRootNode)readRootNode).RecordsCount;
            }
        }


        /// <summary>
        ///  if useCache = true; uses newly created root node, else uses writing root node
        /// </summary>
        /// <param name="key"></param>
        /// <param name="useCache"></param>
        /// <param name="ValuesLazyLoadingIsOn"></param>
        /// <returns></returns>
        public LTrieRow GetKey(byte[] key, bool useCache, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();


            if (useCache)
            {
                //Creating new Root -
                LTrieRootNode readRootNode = new LTrieRootNode(this);

                return readRootNode.GetKey(key, true, ValuesLazyLoadingIsOn);
            }
            else
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                return rn.GetKey(key, false, ValuesLazyLoadingIsOn);
            }

        }


        /// <summary>
        /// DBreeze compatible.
        /// Extension, which helps to READ-THREADS smartly utilize created before read-roots
        /// </summary>
        /// <param name="key"></param>
        /// <param name="readRootNode">if null then WRITE-ROOT NODE</param>
        /// <param name="ValuesLazyLoadingIsOn"></param>
        /// <returns></returns>
        public LTrieRow GetKey(ref byte[] key, ITrieRootNode readRootNode, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();


            if (readRootNode==null)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                return rn.GetKey(key, false, ValuesLazyLoadingIsOn);

            }
            else
            {
                return ((LTrieRootNode)readRootNode).GetKey(key, true, ValuesLazyLoadingIsOn);
            }
        }
 

        ////Iterate
        //public IEnumerable<LTrieRow> IterateForward(bool ValuesLazyLoadingIsOn)
        //{
        //    this.CheckTableIsOperable();

        //    LTrieRootNode readRootNode = new LTrieRootNode(this);

        //    Forward fw = new Forward(readRootNode, ValuesLazyLoadingIsOn);
        //    return fw.IterateForward(true);            
        //}

        public IEnumerable<LTrieRow> IterateForward(bool useCache, bool ValuesLazyLoadingIsOn) //bool SYNCHRO_READ
        {
            this.CheckTableIsOperable();

            if (!useCache)   //SYNCHRO_READ
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Forward fw = new Forward(rn, ValuesLazyLoadingIsOn);
                return fw.IterateForward(false);

            }
            else
            {
                LTrieRootNode readRootNode = new LTrieRootNode(this);
                Forward fw = new Forward(readRootNode, ValuesLazyLoadingIsOn);
                return fw.IterateForward(true);
            }
        }

        public IEnumerable<LTrieRow> IterateForward(ITrieRootNode readRootNode, bool ValuesLazyLoadingIsOn) //bool SYNCHRO_READ
        {
            this.CheckTableIsOperable();

            if (readRootNode == null)   //SYNCHRO_READ
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Forward fw = new Forward(rn, ValuesLazyLoadingIsOn);
                return fw.IterateForward(false);
               
            }
            else
            {
                Forward fw = new Forward((LTrieRootNode)readRootNode, ValuesLazyLoadingIsOn);
                return fw.IterateForward(true);
            }
        }

        //public IEnumerable<LTrieRow> IterateBackward(bool ValuesLazyLoadingIsOn)
        //{
        //    this.CheckTableIsOperable();

        //    LTrieRootNode readRootNode = new LTrieRootNode(this);

        //    Backward bw = new Backward(readRootNode, ValuesLazyLoadingIsOn);
        //    return bw.IterateBackward(true);
        //}

        public IEnumerable<LTrieRow> IterateBackward(bool useCache, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (!useCache)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Backward bw = new Backward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateBackward(false);

            }
            else
            {
                LTrieRootNode readRootNode = new LTrieRootNode(this);

                Backward bw = new Backward(readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateBackward(true);
            }
        }

        public IEnumerable<LTrieRow> IterateBackward(ITrieRootNode readRootNode, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (readRootNode==null)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Backward bw = new Backward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateBackward(false);

            }
            else
            {
                Backward bw = new Backward((LTrieRootNode)readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateBackward(true);
            }
        }
        



        ////Iterate  StartFrom
        //public IEnumerable<LTrieRow> IterateForwardStartFrom(byte[] key, bool includeStartKey, bool ValuesLazyLoadingIsOn)
        //{
        //    this.CheckTableIsOperable();

        //    LTrieRootNode readRootNode = new LTrieRootNode(this);
        //    Forward fw = new Forward(readRootNode, ValuesLazyLoadingIsOn);
        //    return fw.IterateForwardStartFrom(key, includeStartKey,true);
            
        //}

        public IEnumerable<LTrieRow> IterateForwardStartFrom(byte[] key, bool includeStartKey, bool useCache, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (!useCache)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Forward fw = new Forward(rn, ValuesLazyLoadingIsOn);
                return fw.IterateForwardStartFrom(key, includeStartKey, false);

            }
            else
            {
                LTrieRootNode readRootNode = new LTrieRootNode(this);
                Forward fw = new Forward(readRootNode, ValuesLazyLoadingIsOn);
                return fw.IterateForwardStartFrom(key, includeStartKey, true);
            }

        }

        public IEnumerable<LTrieRow> IterateForwardStartFrom(byte[] key, bool includeStartKey,ITrieRootNode readRootNode, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (readRootNode==null)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Forward fw = new Forward(rn, ValuesLazyLoadingIsOn);
                return fw.IterateForwardStartFrom(key, includeStartKey,false);

            }
            else
            {                
                Forward fw = new Forward((LTrieRootNode)readRootNode, ValuesLazyLoadingIsOn);
                return fw.IterateForwardStartFrom(key, includeStartKey,true);
            }

        }


        //public IEnumerable<LTrieRow> IterateBackwardStartFrom(byte[] key, bool includeStartKey, bool ValuesLazyLoadingIsOn)
        //{
        //    this.CheckTableIsOperable();

        //    LTrieRootNode readRootNode = new LTrieRootNode(this);
        //    Backward bw = new Backward(readRootNode, ValuesLazyLoadingIsOn);
        //    return bw.IterateBackwardStartFrom(key, includeStartKey,true);
        //}

        public IEnumerable<LTrieRow> IterateBackwardStartFrom(byte[] key, bool includeStartKey, bool useCache, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (!useCache)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Backward bw = new Backward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardStartFrom(key, includeStartKey, false);

            }
            else
            {
                LTrieRootNode readRootNode = new LTrieRootNode(this);
                Backward bw = new Backward(readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardStartFrom(key, includeStartKey, true);
            }
        }

        public IEnumerable<LTrieRow> IterateBackwardStartFrom(byte[] key, bool includeStartKey, ITrieRootNode readRootNode, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (readRootNode==null)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Backward bw = new Backward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardStartFrom(key, includeStartKey,false);

            }
            else
            {
                Backward bw = new Backward((LTrieRootNode)readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardStartFrom(key, includeStartKey,true);
            }
        }



        ////MIN-MAX
        //public LTrieRow IterateForwardForMinimal(bool ValuesLazyLoadingIsOn)
        //{
        //    this.CheckTableIsOperable();

        //    LTrieRootNode readRootNode = new LTrieRootNode(this);

        //    Forward bw = new Forward(readRootNode, ValuesLazyLoadingIsOn);
        //    return bw.IterateForwardForMinimal(true);
        //}

        public LTrieRow IterateForwardForMinimal(bool useCache, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (!useCache)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Forward bw = new Forward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateForwardForMinimal(false);

            }
            else
            {
                LTrieRootNode readRootNode = new LTrieRootNode(this);

                Forward bw = new Forward(readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateForwardForMinimal(true);
            }
        }

        public LTrieRow IterateForwardForMinimal(ITrieRootNode readRootNode, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (readRootNode==null)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Forward bw = new Forward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateForwardForMinimal(false);

            }
            else
            {
                Forward bw = new Forward((LTrieRootNode)readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateForwardForMinimal(true);
            }
        }





        public LTrieRow IterateBackwardForMaximal(bool useCache, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (!useCache)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Backward bw = new Backward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardForMaximal(false);

            }
            else
            {
                LTrieRootNode readRootNode = new LTrieRootNode(this);
                Backward bw = new Backward(readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardForMaximal(true);
            }
        }

        public LTrieRow IterateBackwardForMaximal(ITrieRootNode readRootNode, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (readRootNode==null)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Backward bw = new Backward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardForMaximal(false);

            }
            else
            {              
                Backward bw = new Backward((LTrieRootNode)readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardForMaximal(true);
            }
        }



        ////SKIP FROM
        //public IEnumerable<LTrieRow> IterateForwardSkipFrom(byte[] key, ulong skippingQuantity, bool ValuesLazyLoadingIsOn)
        //{
        //    this.CheckTableIsOperable();

        //    LTrieRootNode readRootNode = new LTrieRootNode(this);

        //    Forward bw = new Forward(readRootNode, ValuesLazyLoadingIsOn);
        //    return bw.IterateForwardSkipFrom(key,skippingQuantity,true);
        //}

        public IEnumerable<LTrieRow> IterateForwardSkipFrom(byte[] key, ulong skippingQuantity, bool useCache, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (!useCache)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Forward bw = new Forward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateForwardSkipFrom(key, skippingQuantity, false);

            }
            else
            {
                LTrieRootNode readRootNode = new LTrieRootNode(this);
                Forward bw = new Forward(readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateForwardSkipFrom(key, skippingQuantity, true);
            }
        }

        public IEnumerable<LTrieRow> IterateForwardSkipFrom(byte[] key, ulong skippingQuantity,ITrieRootNode readRootNode, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (readRootNode==null)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Forward bw = new Forward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateForwardSkipFrom(key, skippingQuantity,false);

            }
            else
            {

                Forward bw = new Forward((LTrieRootNode)readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateForwardSkipFrom(key, skippingQuantity,true);
            }
        }



        //public IEnumerable<LTrieRow> IterateBackwardSkipFrom(byte[] key, ulong skippingQuantity, bool ValuesLazyLoadingIsOn)
        //{
        //    this.CheckTableIsOperable();

        //    LTrieRootNode readRootNode = new LTrieRootNode(this);

        //    Backward bw = new Backward(readRootNode, ValuesLazyLoadingIsOn);
        //    return bw.IterateBackwardSkipFrom(key, skippingQuantity,true);
        //}

        public IEnumerable<LTrieRow> IterateBackwardSkipFrom(byte[] key, ulong skippingQuantity, bool useCache, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (!useCache)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Backward bw = new Backward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardSkipFrom(key, skippingQuantity, false);

            }
            else
            {
                LTrieRootNode readRootNode = new LTrieRootNode(this);
                Backward bw = new Backward(readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardSkipFrom(key, skippingQuantity, true);
            }
        }

        public IEnumerable<LTrieRow> IterateBackwardSkipFrom(byte[] key, ulong skippingQuantity, ITrieRootNode readRootNode, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (readRootNode==null)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Backward bw = new Backward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardSkipFrom(key, skippingQuantity,false);

            }
            else
            {
                Backward bw = new Backward((LTrieRootNode)readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardSkipFrom(key, skippingQuantity,true);
            }
        }



        //SKIP

        public IEnumerable<LTrieRow> IterateForwardSkip(ulong skippingQuantity, bool useCache, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (!useCache)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Forward bw = new Forward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateForwardSkip(skippingQuantity, false);

            }
            else
            {
                LTrieRootNode readRootNode = new LTrieRootNode(this);
                Forward bw = new Forward(readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateForwardSkip(skippingQuantity, true);
            }
        }

        public IEnumerable<LTrieRow> IterateForwardSkip(ulong skippingQuantity, ITrieRootNode readRootNode, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (readRootNode==null)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Forward bw = new Forward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateForwardSkip(skippingQuantity,false);

            }
            else
            {
                Forward bw = new Forward((LTrieRootNode)readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateForwardSkip(skippingQuantity,true);
            }
        }


        public IEnumerable<LTrieRow> IterateBackwardSkip(ulong skippingQuantity, bool useCache, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (!useCache)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Backward bw = new Backward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardSkip(skippingQuantity, false);

            }
            else
            {
                LTrieRootNode readRootNode = new LTrieRootNode(this);
                Backward bw = new Backward(readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardSkip(skippingQuantity, true);
            }
        }

        public IEnumerable<LTrieRow> IterateBackwardSkip(ulong skippingQuantity, ITrieRootNode readRootNode, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (readRootNode==null)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Backward bw = new Backward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardSkip(skippingQuantity,false);

            }
            else
            {
                Backward bw = new Backward((LTrieRootNode)readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardSkip(skippingQuantity,true);
            }
        }


        //Iterate From - To
        

        public IEnumerable<LTrieRow> IterateForwardFromTo(byte[] startKey, byte[] stopKey, bool includeStartKey, bool includeStopKey, bool useCache, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (!useCache)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Forward bw = new Forward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateForwardFromTo(startKey, stopKey, includeStartKey, includeStopKey, false);

            }
            else
            {
                LTrieRootNode readRootNode = new LTrieRootNode(this);
                Forward bw = new Forward(readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateForwardFromTo(startKey, stopKey, includeStartKey, includeStopKey, true);
            }
        }

        public IEnumerable<LTrieRow> IterateForwardFromTo(byte[] startKey, byte[] stopKey, bool includeStartKey, bool includeStopKey, ITrieRootNode readRootNode, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (readRootNode==null)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Forward bw = new Forward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateForwardFromTo(startKey, stopKey, includeStartKey, includeStopKey,false);

            }
            else
            {              
                Forward bw = new Forward((LTrieRootNode)readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateForwardFromTo(startKey, stopKey, includeStartKey, includeStopKey,true);
            }
        }

        


        public IEnumerable<LTrieRow> IterateBackwardFromTo(byte[] startKey, byte[] stopKey, bool includeStartKey, bool includeStopKey, bool useCache, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (!useCache)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Backward bw = new Backward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardFromTo(startKey, stopKey, includeStartKey, includeStopKey, false);

            }
            else
            {
                LTrieRootNode readRootNode = new LTrieRootNode(this);
                Backward bw = new Backward(readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardFromTo(startKey, stopKey, includeStartKey, includeStopKey, true);
            }
        }

        public IEnumerable<LTrieRow> IterateBackwardFromTo(byte[] startKey, byte[] stopKey, bool includeStartKey, bool includeStopKey, ITrieRootNode readRootNode, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (readRootNode==null)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Backward bw = new Backward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardFromTo(startKey, stopKey, includeStartKey, includeStopKey,false);

            }
            else
            {
                Backward bw = new Backward((LTrieRootNode)readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardFromTo(startKey, stopKey, includeStartKey, includeStopKey,true);
            }
        }

 
        public IEnumerable<LTrieRow> IterateForwardStartsWith(byte[] startKey, bool useCache, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (!useCache)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Forward bw = new Forward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateForwardStartsWith(startKey, false);

            }
            else
            {
                LTrieRootNode readRootNode = new LTrieRootNode(this);
                Forward bw = new Forward(readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateForwardStartsWith(startKey, true);
            }
        }

        public IEnumerable<LTrieRow> IterateForwardStartsWith(byte[] startKey, ITrieRootNode readRootNode, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (readRootNode==null)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Forward bw = new Forward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateForwardStartsWith(startKey,false);

            }
            else
            {
                Forward bw = new Forward((LTrieRootNode)readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateForwardStartsWith(startKey,true);
            }
        }




        //!!!!!!!!!!!!!!   add two more overloads like with starts with
#region "Iterate Forward StartsWith ClosestToPrefix"



        public IEnumerable<LTrieRow> IterateForwardStartsWithClosestToPrefix(byte[] startKey, bool useCache, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (!useCache)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Forward bw = new Forward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateForwardStartsWithClosestToPrefix(startKey, false);

            }
            else
            {
                LTrieRootNode readRootNode = new LTrieRootNode(this);
                Forward bw = new Forward(readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateForwardStartsWithClosestToPrefix(startKey, true);
            }
        }


        public IEnumerable<LTrieRow> IterateForwardStartsWithClosestToPrefix(byte[] startKey, ITrieRootNode readRootNode, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (readRootNode == null)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Forward bw = new Forward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateForwardStartsWithClosestToPrefix(startKey, false);

            }
            else
            {
                Forward bw = new Forward((LTrieRootNode)readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateForwardStartsWithClosestToPrefix(startKey, true);
            }
        }

#endregion



#region "Iterate Backward StartsWith ClosestToPrefix"


        public IEnumerable<LTrieRow> IterateBackwardStartsWithClosestToPrefix(byte[] startKey, bool useCache, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (!useCache)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Backward bw = new Backward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardStartsWithClosestToPrefix(startKey, false);

            }
            else
            {
                LTrieRootNode readRootNode = new LTrieRootNode(this);
                Backward bw = new Backward(readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardStartsWithClosestToPrefix(startKey, true);
            }
        }

        public IEnumerable<LTrieRow> IterateBackwardStartsWithClosestToPrefix(byte[] startKey, ITrieRootNode readRootNode, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (readRootNode == null)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Backward bw = new Backward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardStartsWithClosestToPrefix(startKey, false);

            }
            else
            {
                Backward bw = new Backward((LTrieRootNode)readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardStartsWithClosestToPrefix(startKey, true);

            }
        }

#endregion



        public IEnumerable<LTrieRow> IterateBackwardStartsWith(byte[] startKey, bool useCache, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (!useCache)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Backward bw = new Backward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardStartsWith(startKey, false);

            }
            else
            {
                LTrieRootNode readRootNode = new LTrieRootNode(this);

                Backward bw = new Backward(readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardStartsWith(startKey, true);
            }
        }

        public IEnumerable<LTrieRow> IterateBackwardStartsWith(byte[] startKey, ITrieRootNode readRootNode, bool ValuesLazyLoadingIsOn)
        {
            this.CheckTableIsOperable();

            if (readRootNode==null)
            {
                //Flashing changes on the disk before commit. In case if the same thread uses the same root node
                this.SaveGenerationMap();

                Backward bw = new Backward(rn, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardStartsWith(startKey,false);

            }
            else
            {
                Backward bw = new Backward((LTrieRootNode)readRootNode, ValuesLazyLoadingIsOn);
                return bw.IterateBackwardStartsWith(startKey,true);
            }
        }



        /// <summary>
        /// Wrapper for ITransactable
        /// </summary>
        public void SingleCommit()
        {
            this.Commit();
        }
        
        /// <summary>
        /// Wrapper for ITransactable
        /// </summary>
        public void SingleRollback()
        {
            this.RollBack();
        }
        
        public void ITRCommit()
        {

            this.CheckTableIsOperable();
            
            this.NestedTablesCoordinator.ModificationThreadId = -1;

            if (!TableIsModified)
                return;

            /*Support nested tables*/
            this.NestedTablesCoordinator.TransactionalCommit();
            /***********************/

            //No need of try catch here
            rn.TransactionalCommit();
                      
        }

        public void ITRCommitFinished()
        {
            this.CheckTableIsOperable();

            this.NestedTablesCoordinator.ModificationThreadId = -1;

            if (!TableIsModified)
                return;

            try
            {                

                /*Support nested tables*/
                this.NestedTablesCoordinator.TransactionalCommitFinished();
                /***********************/

                this.Cache.TransactionalCommitFinished();

                TableIsModified = false;
                GenerationMapSaved = true;
                DtTableFixed = DateTime.Now.Ticks;
            }
            //catch (System.Threading.ThreadAbortException ex)
            //{                                   
            //    throw ex;
            //}
            catch (Exception ex)
            {
                //MUST BRING TO NON-OPERATABLE
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTIONAL_COMMIT_FAILED, this.TableName,ex);               
            }
        }

        public void ITRRollBack()
        {
            //This rollback differs from the SingleRollBack, by cleaning in memorey _rootOldCopy, in case if table was already Committed inside of cascade(Transactional) commit

            this.CheckTableIsOperable();

            this.NestedTablesCoordinator.ModificationThreadId = -1;

            if (!TableIsModified)
                return;
                      

            /*Support nested tables*/
            this.NestedTablesCoordinator.TransactionalRollback();
            /***********************/

            //No need of try catch here
            rn.TransactionalRollBack();

            TableIsModified = false;
            GenerationMapSaved = true;
            DtTableFixed = DateTime.Now.Ticks;
        }


        public void TransactionIsFinished(int transactionThreadId)
        {
            //Console.WriteLine("TransactionIsFinished ThreadId: {0}", transactionThreadId);

            _modificationThreadId = -1;
            //////this.NestedTablesCoordinator.ModificationThreadId = -1;

            ///////*Support nested tables*/
            //////this.NestedTablesCoordinator.Rollback();
            ///////***********************/

            ////////Trying to Rollback not Commited. elements
            //////this.RollBack();

            //Trying to Rollback not Commited. elements
            this.RollBack();
        
        }

        public string TableName { get; set; }


        //public string RollBackFileName
        //{
        //    get
        //    {
        //        return this.rollBackFileName;
        //    }
        //}

        /// <summary>
        /// This variable becomes value more then -1 via TrasactionCoordinator, when it returns TableForWrite
        /// It becomes -1 after Transaction End call.
        /// No need of lock.
        /// </summary>
        internal int _modificationThreadId = -1;

        public void ModificationThreadId(int transactionThreadId)
        {
            _modificationThreadId = transactionThreadId;
        }


        public long DtTableFixed
        {
            get
            {
                return _DtTableFixed;
            }
            set
            {
                _DtTableFixed = value;
            }
        }



        
    }
}
