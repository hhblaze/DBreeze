/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.Utils;
using DBreeze.Exceptions;
using DBreeze.Tries;

namespace DBreeze.LianaTrie
{
    public class LTrieRootNode:ITrieRootNode
    {
        public LTrie Tree = null;
        byte[] me = null;
        public byte[] LinkToZeroNode = null;
        ushort DefaultPointerLen = 0;
        ushort DefaultRootSize = 0;
        public byte[] EmptyPointer = null;        
      

        /// <summary>
        /// Indicates quantity of Records in the table
        /// </summary>
        public ulong RecordsCount = 0;


        public LTrieRootNode(LTrie tree)
        {
            Tree = tree;
            DefaultPointerLen = Tree.Storage.TrieSettings.POINTER_LENGTH;
            DefaultRootSize = Tree.Storage.TrieSettings.ROOT_SIZE;

            //me = new byte[Tree.Storage.TreeSettings.ROOT_SIZE];
            LinkToZeroNode = new byte[DefaultPointerLen];

            this.EmptyPointer = new byte[DefaultPointerLen];

            //Reading Root Node
            this.ReadRootNode();


        }

        private void ReadRootNode()
        {            
            me = this.Tree.Cache.RootNodeRead();

            //Initial file already contains filled with 0 ROOT_SIZE Len will not be 0
            //Memory and DbInTable must already have ROOT_SIZE filled with 0 and Len will not be 0

            //if (me.Length == 0)
            //{
            //    //newly created file based root, RootNodeRead, in case of empty read, will return byte[0] 
            //    //here we must create new root

            //    me = new byte[DefaultRootSize];
            //    byte[] oldRootData = null; //must stay null

            //    this.Tree.Cache.RootNodeWrite(ref me, true, ref oldRootData);
            //    //this.Tree.Cache.RootNodeWrite(this.EmptyPointer, ref me, true, ref oldRootData);

            //}           
            

            //OLD
            //if (me.Length == 0 || me[0] == 0)   //Support of the memory where field is reserved or DbInTable where initial bytes are filled with 0
            //{
            //    me = new byte[DefaultRootSize];
            //    byte[] oldRootData = null; //must stay null

             
            //    this.Tree.Cache.RootNodeWrite(ref me, true, ref oldRootData);
            //    //this.Tree.Cache.RootNodeWrite(this.EmptyPointer, ref me, true, ref oldRootData);
                
            //}
            ///////////////////

            DeserializeRootNode();
        }

     

        /// <summary>
        /// bytes[] to objects
        /// </summary>
        private void DeserializeRootNode()
        {
            //1+1+5+8+18 = 33 (if DefaultPointerLen = 6 then 34 )

            //First byte is identifier: 1 if from memory, if 0 from File - under discussion, probably we don't need this logical element.
            //Second byte is file protocol identifier starting from 1 up to 255    
            this.LinkToZeroNode = me.Substring(2, DefaultPointerLen);
            this.RecordsCount = me.Substring(2 + DefaultPointerLen, 8).To_UInt64_BigEndian();
            //dbreeze identifier 18 symbols

        }

        /// <summary>
        /// Root to byte[]
        /// </summary>
        private byte[] SerializeRootNode()
        {
            byte[] newRoot = null;

            //dbreeze.tiesky.com (18 bytes)
            byte[] dbreeze = new byte[] { 0x64, 0x62, 0x72, 0x65, 0x65, 0x7A, 0x65, 0x2E, 0x74, 0x69, 0x65, 0x73, 0x6B, 0x79, 0x2E, 0x63, 0x6F, 0x6D };

            newRoot = newRoot.ConcatMany(
                new byte[] { 1 },  //For supporting Memory Element. It shows that Root Is created                
                new byte[] { 1 }, //-File protocol identifier
                this.LinkToZeroNode,                         //Link to zero node
                this.RecordsCount.To_8_bytes_array_BigEndian(),
                dbreeze
                );

            newRoot = newRoot.EnlargeByteArray_LittleEndian(DefaultRootSize);

            return newRoot;
        }

        public void TransactionalRollBack()
        {
            try
            {
                this.Tree.Cache.TransactionalRollBack();

                //Important, Better to re-read all generation nodes for safety reasons, de bene esse
                this._generationMap.Clear();

                //And re-Read RootNode
                ReadRootNode();
            }
            catch (Exception ex)
            {
                //HERE DB MUST BECOMES NOT-OPERABLE !!!!!!!!!!!!!!!!!!!!!!!!!

                //PARTIALLY CASCADE  this.Tree.Cache.RollBack(); has wrap, others not                
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTIONAL_ROLLBACK_FAILED, this.Tree.TableName, ex);
            }
        }


        public void RollBack()
        {
           
            try
            {                
                this.Tree.Cache.RollBack();

                //Important, Better to re-read all generation nodes for safety reasons, de bene esse
                this._generationMap.Clear();

                //And re-Read RootNode
                ReadRootNode();
            }
            catch (Exception ex)
            {
                //HERE DB MUST BECOMES NOT-OPERABLE !!!!!!!!!!!!!!!!!!!!!!!!!

                //PARTIALLY CASCADE  this.Tree.Cache.RollBack(); has wrap, others not
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.ROLLBACK_FAILED, this.Tree.TableName, ex);                             
            }
            
        }

        public void TransactionalCommit()
        {
            try
            {
                ////rollbak will be done on the level of the tree
                this.Save_GM_nodes_Starting_From(0);

                byte[] oldRoot = me;

                //Gettign new root for save
                me = this.SerializeRootNode();

                //Synchronized inside
                //this.Tree.Cache.TransactionalCommit(this.EmptyPointer, ref me, ref oldRoot);
                                
                this.Tree.Cache.TransactionalCommit(ref me, ref oldRoot);
                
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTIONAL_COMMIT_FAILED, this.Tree.TableName, ex);               
            }
        }

        public void Commit()
        {
            try
            {
                
                this.Save_GM_nodes_Starting_From(0);             

                byte[] oldRoot = me;

                me = this.SerializeRootNode();

                //Synchronized inside         
                //DBreeze.Diagnostic.SpeedStatistic.StartCounter("Commit");
                //this.Tree.Cache.Commit(this.EmptyPointer, ref me, ref oldRoot);
                this.Tree.Cache.Commit(ref me, ref oldRoot);
                //DBreeze.Diagnostic.SpeedStatistic.StopCounter("Commit");

            }
            catch (Exception ex)
            {
                ////rollbak will be done on the level of the tree
          
                ////////////////////////////////////////   
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.COMMIT_FAILED, this.Tree.TableName, ex);                          
            }

        }

        internal LTrieGenerationMap _generationMap = new LTrieGenerationMap();

        

        public void Save_GM_nodes_Starting_From(int index)
        {
            //if (_generationMap.Count() == 0)
            //    return;

            //Going from last record to the first (last always has in kids references to values)
            //Saving one node after another, if node has grown up and will recide totally new place
            //in the file, then parent also has to be updated then gn.ParentChange flag will be true.

            bool ChangeParent = false;
            LTrieGenerationNode prevNode = null;


            int gmMaxIndex = _generationMap.Count() - 1;

            foreach (var gn in _generationMap.Descending)
            {                

                if (ChangeParent)
                {
                    //prev Node can't be null here

                    if (prevNode.ToRemoveFromParentNode)
                    {
                        gn.Value.RemoveKidPointer(prevNode.Value);
                    }
                    else
                    {
                        gn.Value.SetupKidPointer(prevNode.Value, prevNode.Pointer);
                    }

                    //Important switches
                    ChangeParent = false;
                    prevNode.ToChangeParentNode = false;
                    prevNode.ToRemoveFromParentNode = false;
                }

                if (gn.Key < index)
                    break;


                //TESTS
                //Every element which must be save we try to represent as set of GN bytes starting from 0 up to this generation node
                //and record them as Key into memory dictionary with value byte[] oldKids, later reader will be able on every iteration point to request these kids from memory
                //Probably except 0 node, because for reading it will be not interesting, there LinkZeroNode is always synchronized


                //Adding MapKids and WritinSelf synchro is checked 
                byte[] generationMapLine = null;
                if (gmMaxIndex > 0)
                {
                    generationMapLine = _generationMap.GenerateMapNodesValuesUpToIndex(gmMaxIndex, true);                    
                    gmMaxIndex--;
                    //this.Tree.Cache.AddMapKids(_generationMap.GenerateMapNodesValuesUpToIndex(gmMaxIndex--), gn.Value.KidsBeforeModification);                 
                }

                //Writing on disk                
                gn.Value.WriteSelf(generationMapLine);
                
                
                if (gn.Key == 0)
                {
                    //Setting new LinkToZeroNode for Writing in into the ROOT
                   
                    this.LinkToZeroNode = gn.Value.Pointer;

                    //Console.WriteLine("SAVING KEY: {0}", visualizeGenerationMap());
                }

                //Setting up vital variables
                prevNode = gn.Value;
                ChangeParent = gn.Value.ToChangeParentNode;


            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="WasUpdated">true means that value existed and was updated</param>
        /// <param name="dontUpdateIfExists">When true - if value exists, we dont update it. If WasUpdated = true then we value exists, if false - we have inserted new one</param>
        /// <returns></returns>
        public byte[] AddKey(ref byte[] key, ref byte[] value, out bool WasUpdated, bool dontUpdateIfExists)
        {
            //indicates that key we insert, already existed in the system and was updated
            WasUpdated = false;

            //if (key == null || key.Length == 0)
            //    return;
            if (key == null)
                return null;

            if (key.Length > UInt16.MaxValue)
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.KEY_IS_TOO_LONG);



            LTrieGenerationNode gn = null;

            if (_generationMap.Count() == 0)
            {
                //Loading it from Link TO ZERO Pointer
                gn = new LTrieGenerationNode(this);
                gn.Pointer = this.LinkToZeroNode;
                //gn.Value=0; - default
                _generationMap.Add(0, gn);

                gn.ReadSelf(false, null);
            }


            LTrieSetupKidResult res = new LTrieSetupKidResult();

            byte[] key1 = null; //we need it as null
            byte[] val1 = null;

            //len can be expanded inside of the algorithm maximum by one
            int len = key.Length;

            bool cleanCheck = true;



            /*Special case key is empty byte[0] */
            if (key.Length == 0)
            {
                //Saving byte[0] key
                res = _generationMap[0].SetupKidWithValue((byte)0, true, ref key, ref value, false, out WasUpdated, dontUpdateIfExists);

                return res.ValueLink;
            }
            /**/


            for (int i = 0; i < len; i++)
            {
                //Getting kid from actual generation map

                if (cleanCheck && i != 0 && _generationMap.ContainsKey(i) && _generationMap[i].Value != key[i - 1])
                {
                    cleanCheck = false;

                    //In case if i>0, it's not the first element and we have to compare if there are generation mapsstarting from this point.
                    //If generationMap[i] exists and it's value not that what we want, we have to clean full in memory generation map as 

                    //Save_node... up to i
                    Save_GM_nodes_Starting_From(i);

                    //Remove Gen Map starting from... i
                    _generationMap.RemoveBiggerOrEqualThenKey(i);
                }

                if (!_generationMap.ContainsKey(i))
                {
                    //All ok, for the first generation node        

                    //We read or create generation map
                    //And add it to the _generationMap

                    gn = new LTrieGenerationNode(this);
                    gn.Value = key[i - 1];
                    gn.Pointer = _generationMap[i - 1].KidsInNode.GetPointerToTheKid(key[i - 1]);
                    //gn.Pointer = _generationMap[i - 1].GetKidPointer(key[i - 1]);

                    _generationMap.Add(i, gn);

                    if (gn.Pointer != null)
                        gn.ReadSelf(false, null);
                    else
                        gn.Pointer = new byte[DefaultPointerLen];       //!!!!!!!!!!!!! Check if it'S really necessary or we can leave it as null
                }


                //Generation Node in this trie can have link to kids [0-255] and link to the value.
                //If Kids>0 && Value Link is not Default Empty Pointer, then this value-link refers to the end of the sentence.
                //If Kids==0 && Value Link is not Default Empty Pointer, then this value-link refers to the sentence which can go after this last character, so also to the value.
                //Dual behaviour.


                if (res.KeyOldKid != null)
                {
                    //It means that on this stage probably we have to setup both kids
                    //We can check Length of both keys and their current condition

                    //key1 = res.KeyOldKid; //we need it as null
                    val1 = res.ValPtrOldKid;


                    if ((res.KeyOldKid.Length - 1) < i)
                    {
                        _generationMap[i].SetupKidWithValue((byte)0, true, ref key1, ref val1, true, out WasUpdated, dontUpdateIfExists);
                    }
                    else
                    {
                        _generationMap[i].SetupKidWithValue(res.KeyOldKid[i], false, ref key1, ref val1, true, out WasUpdated, dontUpdateIfExists);
                    }

                    //Cleaning up KeyOldKid - probably not necessary, de bene esse (just in case)
                    res.KeyOldKid = null;
                }


                //One more only then we setup value, otherwise we bind to the kid, Check Docs in fileDb LtrieSpreadExample1.jpg
                res = _generationMap[i].SetupKidWithValue(((i == key.Length) ? (byte)0 : key[i]), (i == key.Length), ref key, ref value, false, out WasUpdated, dontUpdateIfExists);

                if (!res.IterateFurther)
                {
                    //After setting up value, we can just exit
                    return res.ValueLink;
                }
                else
                {
                    //we don't need value on this phase, we can go on further

                    //Expanding iteration cycle by one and, on the next iteration cycle we should go out from the loop.
                    if (i == (key.Length - 1))
                        len++;
                }



            }

            //Should not happen as null, we have to return link to the full value
            return null;
        }


        /// <summary>
        /// Returns link to the full value together with the key
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="startIndex"></param>
        /// <param name="WasUpdated">indicates that key we insert, already existed in the system and was updated</param>
        /// <returns></returns>
        public byte[] AddKeyPartially(ref byte[] key, ref byte[] value, uint startIndex, out long valueStartPtr,out bool WasUpdated)
        {
            WasUpdated = false;

            if (key == null)
            {
                valueStartPtr = -1;
                return null;
            }

            if (key.Length > UInt16.MaxValue)
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.KEY_IS_TOO_LONG);

            if (value == null)
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.PARTIAL_VALUE_CANT_BE_NULL);


            LTrieGenerationNode gn = null;

            if (_generationMap.Count() == 0)
            {
                //Loading it from Link TO ZERO Pointer
                gn = new LTrieGenerationNode(this);
                gn.Pointer = this.LinkToZeroNode;
                //gn.Value=0; - default
                _generationMap.Add(0, gn);

                gn.ReadSelf(false, null);
            }


            LTrieSetupKidResult res = new LTrieSetupKidResult();

            byte[] key1 = null; //we need it as null
            byte[] val1 = null;

            //len can be expanded inside of the algorithm maximum by one
            int len = key.Length;

            bool cleanCheck = true;

            /*Special case key is empty byte[0] */
            if (key.Length == 0)
            {                
                //Saving byte[0] key
                res = _generationMap[0].SetupKidWithValuePartially((byte)0, true, ref key, ref value, false, startIndex, out valueStartPtr, out WasUpdated);

                return res.ValueLink;
            }
            /**/

            for (int i = 0; i < len; i++)
            {
                //Getting kid from actual generation map

                if (cleanCheck && i != 0 && _generationMap.ContainsKey(i) && _generationMap[i].Value != key[i - 1])
                {
                    cleanCheck = false;

                    //In case if i>0, it's not the first element and we have to compare if there are generation mapsstarting from this point.
                    //If generationMap[i] exists and it's value not that what we want, we have to clean full in memory generation map as 

                    //Save_node... up to i
                    Save_GM_nodes_Starting_From(i);

                    //Remove Gen Map starting from... i
                    _generationMap.RemoveBiggerOrEqualThenKey(i);
                }

                if (!_generationMap.ContainsKey(i))
                {
                    //All ok, for the first generation node        

                    //We read or create generation map
                    //And add it to the _generationMap

                    gn = new LTrieGenerationNode(this);
                    gn.Value = key[i - 1];
                    gn.Pointer = _generationMap[i - 1].KidsInNode.GetPointerToTheKid(key[i - 1]);
                    //gn.Pointer = _generationMap[i - 1].GetKidPointer(key[i - 1]);

                    _generationMap.Add(i, gn);

                    if (gn.Pointer != null)
                        gn.ReadSelf(false, null);
                    else
                        gn.Pointer = new byte[DefaultPointerLen];       //!!!!!!!!!!!!! Check if it'S really necessary or we can leave it as null
                }


                //Generation Node in this trie can have link to kids [0-255] and link to the value.
                //If Kids>0 && Value Link is not Default Empty Pointer, then this value-link refers to the end of the sentence.
                //If Kids==0 && Value Link is not Default Empty Pointer, then this value-link refers to the sentence which can go after this last character, so also to the value.
                //Dual behaviour.


                if (res.KeyOldKid != null)
                {
                    //It means that on this stage probably we have to setup both kids
                    //We can check Length of both keys and their current condition

                    //key1 = res.KeyOldKid; //we need it as null
                    val1 = res.ValPtrOldKid;


                    if ((res.KeyOldKid.Length - 1) < i)
                    {
                        _generationMap[i].SetupKidWithValuePartially((byte)0, true, ref key1, ref val1, true, startIndex, out valueStartPtr, out WasUpdated);
                    }
                    else
                    {
                        _generationMap[i].SetupKidWithValuePartially(res.KeyOldKid[i], false, ref key1, ref val1, true, startIndex, out valueStartPtr, out WasUpdated);
                    }

                    //Cleaning up KeyOldKid - probably not necessary, de bene esse (just in case)
                    res.KeyOldKid = null;
                }


                //One more only then we setup value, otherwise we bind to the kid, Check Docs in fileDb LtrieSpreadExample1.jpg
                res = _generationMap[i].SetupKidWithValuePartially(((i == key.Length) ? (byte)0 : key[i]), (i == key.Length), ref key, ref value, false, startIndex, out valueStartPtr, out WasUpdated);

                if (!res.IterateFurther)
                {
                    //After setting up value, we can just exit
                    return res.ValueLink;
                }
                else
                {
                    //we don't need value on this phase, we can go on further

                    //Expanding iteration cycle by one and, on the next iteration cycle we should go out from the loop.
                    if (i == (key.Length - 1))
                        len++;
                }



            }

            //Should not happen as null, we have to return link to the full value
            valueStartPtr = -1;
            return null;

        }

        /// <summary>
        /// Check TransactionCommit in case of RemoveAll with file Recreation.
        /// Note if some other threads are reading parallel data, exception will be thrown in their transaction.
        /// It's correct.
        /// </summary>
        /// <param name="withFileRecreation"></param>
        public void RemoveAll(bool withFileRecreation)
        {
         

            if (!withFileRecreation)
            {
                LTrieGenerationNode gn = null;

                _generationMap.Clear();

                if (_generationMap.Count() == 0)
                {
                    //Loading it from Link TO ZERO Pointer
                    gn = new LTrieGenerationNode(this);
                    gn.Pointer = this.LinkToZeroNode;
                    //gn.Value=0; - default
                    _generationMap.Add(0, gn);

                    gn.ReadSelf(false, null);
                }

                
                _generationMap[0].RemoveAllKids();
            }
            else
            {               
                try
                {
                    this.Tree.Cache.RecreateDB();

                    //Important, Better to re-read all generation nodes for safety reasons, de bene esse
                    this._generationMap.Clear();

                    //And re-Read RootNode
                    ReadRootNode();
                }
                catch (Exception ex)
                {
                    ////////////////////  MADE THAT Table is not Opearable on the upper level, 
                    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.RECREATE_TABLE_FAILED, this.Tree.TableName, ex);                    
                }
                
            }
        }

        /// <summary>
        /// Takes value fresh no committed value row.GetFullValue(false);
        /// </summary>
        /// <param name="oldKey"></param>
        /// <param name="newKey"></param>
        /// <returns></returns>
        public bool ChangeKey(ref byte[] oldKey, ref byte[] newKey)
        {
            byte[] refToInsertedValue = null;
            return this.ChangeKey(ref oldKey, ref newKey, out refToInsertedValue);
        }

        /// <summary>
        /// Takes value fresh no committed value row.GetFullValue(false);
        /// </summary>
        /// <param name="oldKey"></param>
        /// <param name="newKey"></param>
        /// <param name="refToInsertedValue">returns ptr in the file to the new key</param>
        /// <returns></returns>
        public bool ChangeKey(ref byte[] oldKey, ref byte[] newKey, out byte[] refToInsertedValue)
        {
            //The best way read old, remove, and create new, with holding of transactions,
            //just changing pointers to the value will give nothing, because in the value also the full key is written, so we will need 
            //to make new value
            refToInsertedValue = null;

            var row = this.GetKey(oldKey, false, false);

            if (row.Exists)
            {
                byte[] oldKeyValue = row.GetFullValue(false);
                bool WasRemoved = false;
                byte[] deletedValue = null;
                this.RemoveKey(ref oldKey, out WasRemoved, false, out deletedValue);
                bool WasUpdated = false;
                refToInsertedValue = this.AddKey(ref newKey, ref oldKeyValue,out WasUpdated,false);

                refToInsertedValue = refToInsertedValue.EnlargeByteArray_BigEndian(8);

                return true;
            }
            return false;
        }
       

        /// <summary>
        /// Will return pointer to the value of the removing kid (if it existed). Otherwise NULL.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="WasRemoved">indicates that value existed if true</param>
        /// <param name="retrieveDeletedValue">indicates if we should bind deleted value to the result</param>
        /// <param name="deletedValue">interesting only if WasRemoved = true and retrieveDeletedValue is true</param>
        /// <returns></returns>
        public void RemoveKey(ref byte[] key,out bool WasRemoved, bool retrieveDeletedValue, out byte[] deletedValue)
        {
            WasRemoved = false;
            deletedValue = null;
            //if (key == null || key.Length == 0)
            //    return;

            if (key == null)
                return;

            if (key.Length > UInt16.MaxValue)
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.KEY_IS_TOO_LONG);


            LTrieGenerationNode gn = null;

            if (_generationMap.Count() == 0)
            {
                //Loading it from Link TO ZERO Pointer
                gn = new LTrieGenerationNode(this);
                gn.Pointer = this.LinkToZeroNode;
                //gn.Value=0; - default
                _generationMap.Add(0, gn);

                gn.ReadSelf(false, null);
            }

         
            LTrieSetupKidResult res = new LTrieSetupKidResult();

            //byte[] key1 = null; //we need it as null
            //byte[] val1 = null;

            //len can be expanded inside of the algorithm maximum by one
            int len = key.Length;

            bool cleanCheck = true;

            bool iterateFurther = false;
                     
            /*SPECIAL CASE key=byte[0]*/
            if (key.Length == 0)
            {
                _generationMap[0].RemoveKid((byte)0, true, ref key, out WasRemoved, retrieveDeletedValue, out deletedValue);

                return;
            }
            /***************************/

            for (int i = 0; i < len; i++)
            {
                //Getting kid from actual generation map

                if (cleanCheck && i != 0 && _generationMap.ContainsKey(i) && _generationMap[i].Value != key[i - 1])
                {
                    cleanCheck = false;

                    //In case if i>0, it's not the first element and we have to compare if there are generation mapsstarting from this point.
                    //If generationMap[i] exists and it's value not that what we want, we have to clean full in memory generation map as 

                    //Save_node... up to i
                    Save_GM_nodes_Starting_From(i);

                    //Remove Gen Map starting from... i
                    _generationMap.RemoveBiggerOrEqualThenKey(i);
                }

                if (!_generationMap.ContainsKey(i))
                {
                    //All ok, for the first generation node

                    //We read or create generation map
                    //And add it to the _generationMap

                    gn = new LTrieGenerationNode(this);
                    gn.Value = key[i - 1];
                    gn.Pointer = _generationMap[i - 1].KidsInNode.GetPointerToTheKid(key[i - 1]);
                    //gn.Pointer = _generationMap[i - 1].GetKidPointer(key[i - 1]);

                    _generationMap.Add(i, gn);

                    if (gn.Pointer != null)
                        gn.ReadSelf(false, null);
                    else
                        gn.Pointer = new byte[DefaultPointerLen];       //!!!!!!!!!!!!! Check if it'S really necessary or we can leave it as null
                }

                //Trying to remove as a result we receive information if we should iterate further
                iterateFurther = _generationMap[i].RemoveKid((i == key.Length) ? (byte)0 : key[i], (i == key.Length), ref key, out WasRemoved, retrieveDeletedValue, out deletedValue);

                if (!iterateFurther)
                {
                    break;
                }
                else
                {
                    if (i == (key.Length - 1))
                        len++;
                }
            }

        }





        //#region "Checking Of Empty Pointer"

        ///// <summary>
        ///// Checks if pointer is empty
        ///// </summary>
        ///// <param name="ptr"></param>
        ///// <returns></returns>
        //public bool _IfPointerIsEmpty(byte[] ptr)
        //{
        //    //Executes 52 ms
        //    #region "Settign up delegate"
        //    switch (this.DefaultPointerLen)
        //    {
        //        case 5:     //Gives ability to allocate file up to 1 terrabyte (1.099.511.627.775)
        //            return !(
        //                   ptr[4] != 0
        //                   ||
        //                   ptr[3] != 0
        //                   ||
        //                   ptr[2] != 0
        //                   ||
        //                   ptr[1] != 0
        //                   ||
        //                   ptr[0] != 0
        //                   );                   
        //        case 4:     //4GB
        //             return !(
        //                   ptr[3] != 0
        //                   ||
        //                   ptr[2] != 0
        //                   ||
        //                   ptr[1] != 0
        //                   ||
        //                   ptr[0] != 0
        //                   );

        //        case 3:     //17MB
        //             return !(
        //                   ptr[2] != 0
        //                   ||
        //                   ptr[1] != 0
        //                   ||
        //                   ptr[0] != 0
        //                   );

        //        case 6:     //281 Terrabytes (281.474.976.710.655)
        //             return !(
        //                    ptr[5] != 0
        //                    ||
        //                    ptr[4] != 0
        //                    ||
        //                    ptr[3] != 0
        //                    ||
        //                    ptr[2] != 0
        //                    ||
        //                    ptr[1] != 0
        //                    ||
        //                    ptr[0] != 0
        //                    );

        //        case 7:     //72 Petabytes (72.057.594.037.927.935)
        //             return !(
        //                    ptr[6] != 0
        //                    ||
        //                    ptr[5] != 0
        //                    ||
        //                    ptr[4] != 0
        //                    ||
        //                    ptr[3] != 0
        //                    ||
        //                    ptr[2] != 0
        //                    ||
        //                    ptr[1] != 0
        //                    ||
        //                    ptr[0] != 0
        //                    );                   
        //        case 2:   //65 KB
        //             return !(
        //                   ptr[1] != 0
        //                   ||
        //                   ptr[0] != 0
        //                   );                   
        //        default:
        //            return ptr._ByteArrayEquals(this.EmptyPointer);

        //    }

        //    #endregion


        //}
        //#endregion





        #region "DATA FETCHING"






        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="useCache"></param>
        /// <param name="ValuesLazyLoadingIsOn">if true reads key only</param>
        /// <returns></returns>
        public LTrieRow GetKey(byte[] key, bool useCache, bool ValuesLazyLoadingIsOn)
        {            
            LTrieRow kv = new LTrieRow(this);

            kv.Key = key;

            //if (key == null || key.Length == 0)
            //    return kv;

            if (key == null)
                return kv;


            LTrieGenerationNode gn = null;

            if (_generationMap.Count() == 0)
            {
                //Loading it from Link TO ZERO Pointer
                gn = new LTrieGenerationNode(this);
                gn.Pointer = this.LinkToZeroNode;
                //gn.Value=0; - default
                _generationMap.Add(0, gn);

                gn.ReadSelf(useCache, _generationMap.GenerateMapNodesValuesUpToIndex(0));
                //gn.ReadSelf();
            }

            bool cleanCheck = true;

            LTrieKid kidDef = null;
            int p = 0;

            int len = key.Length;


            /*SPECIAL CASE key = byte[0]*/
            if (key.Length == 0)
            {
                kidDef = _generationMap[0].GetKidAsValue(true, 1);
                if (kidDef.Exists)
                {
                    kv.LinkToValue = kidDef.Ptr;
                }
                return kv;
            }
            /****************************/

            for (int i = 0; i < len; i++)
            {
                //Getting kid from actual generation map

                if (cleanCheck && i != 0 && _generationMap.ContainsKey(i) && _generationMap[i].Value != key[i - 1])
                {
                    cleanCheck = false;
                    _generationMap.RemoveBiggerOrEqualThenKey(i);
                }

                if (!_generationMap.ContainsKey(i))
                {
                    gn = new LTrieGenerationNode(this);
                    gn.Value = key[i - 1];
                    gn.Pointer = _generationMap[i - 1].KidsInNode.GetPointerToTheKid(key[i - 1]);

                    //FIND A SOLUTION FOR THIS NULL or EMPTY POINTER
                    //if (gn.Pointer == null || this._IfPointerIsEmpty(gn.Pointer))
                    //    return kv;
                    if (gn.Pointer == null || gn.Pointer._IfPointerIsEmpty(this.DefaultPointerLen))
                        return kv;
                    
                    _generationMap.Add(i, gn);

                    gn.ReadSelf(useCache, _generationMap.GenerateMapNodesValuesUpToIndex(i));
                    //gn.ReadSelf();
                }

                //Also if last element then supply 256 to get value not the link to value (if no value exit)
                //If kid is a link to next node we iterate further, if link on the value, we retrieve full key and value as link for TreeKVP stoping iteration
                //If link is empty (no kid) we return empty

                if (i >= key.Length)
                    p = i - 1;
                else
                    p = i;

                kidDef = _generationMap[i].GetKidAsValue((i >= (key.Length)), key[p]);

                if (kidDef.Exists)
                {
                    if (kidDef.ValueKid)
                    {
                        kv.LinkToValue = kidDef.Ptr;
                        return kv;
                    }

                    if (!kidDef.LinkToNode)
                    {
                        //byte[] storedKey = _generationMap[i].ReadKidKeyFromValPtr(kidDef.Ptr);                                               
                        long valueStartPtr = 0;
                        uint valueLength = 0;
                        byte[] xValue = null;
                        byte[] storedKey = null;

                        if (!ValuesLazyLoadingIsOn)
                        {
                            this.Tree.Cache.ReadKeyValue(useCache, kidDef.Ptr, out valueStartPtr, out valueLength, out storedKey, out xValue);                           
                        }
                        else
                        {
                            storedKey = this.Tree.Cache.ReadKey(useCache, kidDef.Ptr);
                        }

                       // byte[] storedKey = this.Tree.Cache.ReadKey(useCache, kidDef.Ptr);

                        if (key.Length != storedKey.Length || !key._ByteArrayEquals(storedKey))
                            return kv;

                        if (!ValuesLazyLoadingIsOn)
                        {
                            kv.ValueStartPointer = valueStartPtr;
                            kv.ValueFullLength = valueLength;
                            kv.Value = xValue;                           
                            kv.ValueIsReadOut = true;
                        }
                        kv.LinkToValue = kidDef.Ptr;
                        return kv;
                    }

                    if (i == key.Length - 1)
                        len++;

                    //iterating further
                }
                else
                    return kv;

            }



            return kv;
        }

        #endregion
    }
}
