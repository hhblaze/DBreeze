/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.Utils;

namespace DBreeze.LianaTrie
{    
    internal class LTrieGenerationNode
    {
        LTrieRootNode _root = null;
        public byte[] Pointer = null;
        ushort DefaultPointerLen = 0;
        byte[] DefaultEmptyPointer = null;

        public LTrieKidsInNode KidsInNode = null;

        public byte Value = 0;

        public bool ToWrite = false;    //Modified by SetupKidPointer,RemoveKidPointer(!!!! absent for now),SetupKidWithValue to true, used by WriteSelf

        /// <summary>
        /// Reservations are calculated by Schema. They Depend upon quantity of Existing Kids.
        /// QuantityAvailableReservations is calculated in ReadSelf if Kid.Pointer equals to ZeroPointer - it means reservation.
        /// Default is 0
        /// </summary>
        private int QuantityReservationSlots = 0;

        /// <summary>
        /// When New Node is created it's true
        /// After Reading Node it must become false,
        /// After WriteSelf it will be or true or false.
        /// </summary>
        public bool ToChangeParentNode = true;

        public bool ToRemoveFromParentNode = false;

        /// <summary>
        /// Field which contains either null (if Generation node is empty and had no kids) or value in format of prepared for Save Kids,
        /// in case if ReadOutExistingNode.
        /// </summary>
        public byte[] KidsBeforeModification = null;

        /// <summary>
        /// Is calculated as LengthOfTheKidsBlockInFile(2 bytes) + (256 * (DefaultPointerLen + 1[byte definition]))
        /// </summary>
        int MaximumKidLineLength = 0;



        //Format
        //TotalSize,LinkToValue,0-255 Links to Kids

        public LTrieGenerationNode(LTrieRootNode rootNode)
        {
            _root = rootNode;

            DefaultEmptyPointer = this._root.EmptyPointer;
            DefaultPointerLen = this._root.Tree.Storage.TrieSettings.POINTER_LENGTH;
            Pointer = new byte[DefaultPointerLen];

            KidsInNode = new LTrieKidsInNode(DefaultPointerLen);

            //Total length,link to value, 256* (Ptr + 1[name of kid[0-255]] + 1 (ptr to next gen or to value)
            MaximumKidLineLength = 2 + DefaultPointerLen + (256 * (DefaultPointerLen + 2));
              
          
            
        }


        public void RemoveKidPointer(byte kid)
        {
            //Used by Save_GM_nodes_Starting_From

            ToWrite = true;

            KidsInNode.RemoveKid(kid);
        }

        public void SetupKidPointer(byte kid, byte[] ptr)
        {
            //Used by Save_GM_nodes_Starting_From

            ToWrite = true;

            if (ptr == null)
                ptr = new byte[DefaultPointerLen];

            KidsInNode.AddKidPointer(kid, ptr);
        }

        //Trying to setup kid
        /// <summary>
        /// 
        /// </summary>
        /// <param name="kid"></param>
        /// <param name="lastElementOfTheKey"></param>
        /// <param name="fullKey"></param>
        /// <param name="value"></param>
        /// <param name="useExistingPointerToValue"></param>
        /// <param name="WasUpdated">true means that value existed and was updated</param>
        /// <param name="dontUpdateIfExists">When true - if value exists, we dont update it. If WasUpdated = true then we value exists, if false - we have inserted new one</param>
        /// <returns></returns>
        public LTrieSetupKidResult SetupKidWithValue(byte kid, bool lastElementOfTheKey, ref byte[] fullKey, ref byte[] value, bool useExistingPointerToValue,out bool WasUpdated,bool dontUpdateIfExists)
        {
            //useExistingPointerToValue is used to move previously saved kid to the new place
            ToWrite = true;
            WasUpdated = false;

            LTrieSetupKidResult res = new LTrieSetupKidResult();
           

            byte[] ptr = null;
            LTrieKid lKid = null;

            bool tryToOverWrite = true;

            if (lastElementOfTheKey)
            {
                //We must setup Kid value
                if (!useExistingPointerToValue)     //Hack for oldKid link replacement
                {
                    //Here we also can decide if kid existed, probably we could overwrite it (in total 3 places also in the bottom)                  
                    lKid = KidsInNode.GetKidValue();
                    
                    if (!lKid.Exists)
                    {
                        tryToOverWrite = false;
                        this._root.RecordsCount++;
                    }

                    WasUpdated = tryToOverWrite;

                    if (WasUpdated && dontUpdateIfExists)
                    {
                        //Value exists, but we don't want to update it
                        ptr = lKid.Ptr;
                    }
                    else
                    {
                        ptr = this.WriteKidValue(ref fullKey, ref value, tryToOverWrite, lKid.Ptr);
                    }
                }
                else
                    ptr = value;

                if (WasUpdated && dontUpdateIfExists)
                {
                    //Value exists, but we don't want to update it
                }
                else
                {
                    KidsInNode.AddKid(256, ptr);
                }

                res.ValueLink = ptr;
                res.IterateFurther = false;
                return res;
            }
            else
            {
                //If element is not the last one
                //we have to check if its kid place already resides with some other value, if yes then we have to move further.
                //And also probably move further the element who resides its place.
                //finding empty slot for our value(s). If place is empty then we save our element.

                if (KidsInNode.ContainsKid(kid))
                {
                    //When we Setup Old Kid we should never come here

                    lKid = KidsInNode.GetKid(kid);

                    if (!lKid.LinkToNode)
                    {
                        //Here we have link to the value
                                               
                       
                        ptr = this._root.Tree.Cache.ReadKey(false, lKid.Ptr);
                        

                        if (fullKey._ByteArrayEquals(ptr))
                        {
                            //Insert key equals to storedKey, it means - UPDATE

                            //We write for now always on the new place value and update reference
                            WasUpdated = true;

                            if (dontUpdateIfExists)
                            {
                                ptr = lKid.Ptr;
                            }
                            else
                            {
                                ptr = this.WriteKidValue(ref fullKey, ref value, true, lKid.Ptr);

                                KidsInNode.AddKid(kid, ptr);
                            }

                            res.ValueLink = ptr;
                            res.IterateFurther = false;
                            return res;
                        }
                        else
                        {
                            //Remembering old Key
                            res.KeyOldKid = ptr;
                            //Remembering old link
                            res.ValPtrOldKid = KidsInNode.ReplaceValueLinkOnKidLink(kid);
                        }

                    }

                    
                    res.IterateFurther = true;
                    return res;
                }
                else
                {
                    //Just save this one
                    if (!useExistingPointerToValue)
                    {
                        //Here we also can decide if kid existed, probably we could overwrite it (2 places also check up there)
                        lKid = KidsInNode.GetKid(kid);

                        if (!lKid.Exists)
                        {
                            tryToOverWrite = false;
                            this._root.RecordsCount++;
                        }

                        WasUpdated = tryToOverWrite;

                        if (WasUpdated && dontUpdateIfExists)
                        {
                            //Value exists, but we don't want to update it
                            ptr = lKid.Ptr;
                        }
                        else
                        {
                            ptr = this.WriteKidValue(ref fullKey, ref value, tryToOverWrite, lKid.Ptr);
                        }
                    }
                    else
                        ptr = value;       //Hack for oldKid link replacement

                    if (WasUpdated && dontUpdateIfExists)
                    {
                        //Value exists, but we don't want to update it
                    }
                    else
                    {
                        KidsInNode.AddKid(kid, ptr);
                    }
                    res.ValueLink = ptr;
                    res.IterateFurther = false;
                    return res;
                }
            }
        }


        public LTrieSetupKidResult SetupKidWithValuePartially(byte kid, bool lastElementOfTheKey, ref byte[] fullKey, ref byte[] value, bool useExistingPointerToValue, uint startIndex, out long valueStartPtr, out bool WasUpdated)
        {
            //useExistingPointerToValue is used to move previously saved kid to the new place
            ToWrite = true;
            WasUpdated = false;

            LTrieSetupKidResult res = new LTrieSetupKidResult();
          

            byte[] ptr = null;
            LTrieKid lKid = null;

            bool tryToOverWrite = true;

            if (lastElementOfTheKey)
            {
                //We must setup Kid value
                if (!useExistingPointerToValue)     //Hack for oldKid link replacement
                {
                    //Here we also can decide if kid existed, probably we could overwrite it (in total 3 places also in the bottom)                  
                    lKid = KidsInNode.GetKidValue();

                    if (!lKid.Exists)
                    {
                        tryToOverWrite = false;
                        this._root.RecordsCount++;
                    }

                    WasUpdated = tryToOverWrite;
                    ptr = this.WriteKidValuePartially(ref fullKey, ref value, tryToOverWrite, lKid.Ptr, startIndex, out valueStartPtr);
                }
                else
                {
                    valueStartPtr = -1;
                    ptr = value;
                }

                KidsInNode.AddKid(256, ptr);
                res.ValueLink = ptr;
                res.IterateFurther = false;
                return res;
            }
            else
            {
               

                if (KidsInNode.ContainsKid(kid))
                {
                    //When we Setup Old Kid we should never come here

                    lKid = KidsInNode.GetKid(kid);

                    if (!lKid.LinkToNode)
                    {
                        //Here we have link to the value

                        //We will check here if it's the same key
               
                        ptr = this._root.Tree.Cache.ReadKey(false, lKid.Ptr);
               

                        if (fullKey._ByteArrayEquals(ptr))
                        {
                            //Insert key equals to storedKey, it means - UPDATE

                            //We write for now always on the new place value and update reference
                            WasUpdated = true;
                            ptr = this.WriteKidValuePartially(ref fullKey, ref value, true, lKid.Ptr, startIndex, out valueStartPtr);

                            KidsInNode.AddKid(kid, ptr);
                            res.ValueLink = ptr;
                            res.IterateFurther = false;
                            return res;
                        }
                        else
                        {
                            //Remembering old Key
                            res.KeyOldKid = ptr;
                            //Remembering old link
                            res.ValPtrOldKid = KidsInNode.ReplaceValueLinkOnKidLink(kid);
                        }

                    }

                    valueStartPtr = -1;
                    res.IterateFurther = true;
                    return res;
                }
                else
                {
                    //Just save this one
                    if (!useExistingPointerToValue)
                    {
                        //Here we also can decide if kid existed, probably we could overwrite it (2 places also check up there)
                        lKid = KidsInNode.GetKid(kid);

                        if (!lKid.Exists)
                        {
                            tryToOverWrite = false;
                            this._root.RecordsCount++;
                        }

                        WasUpdated = tryToOverWrite;
                        ptr = this.WriteKidValuePartially(ref fullKey, ref value, tryToOverWrite, lKid.Ptr, startIndex, out valueStartPtr);
                    }
                    else
                    {
                        valueStartPtr = -1;
                        ptr = value;       //Hack for oldKid link replacement
                    }

                    KidsInNode.AddKid(kid, ptr);
                    res.ValueLink = ptr;
                    res.IterateFurther = false;                    
                    return res;
                }
            }
        }

        public void RemoveAllKids()
        {
            //Is interesting only for GenerationNode = 0
            ToWrite = true;
            this._root.RecordsCount = 0;

            KidsInNode.RemoveAllKids();

            //KidsInNode.RemoveValueKid();

            //for (int i = 0; i < 256; i++)
            //    KidsInNode.RemoveKid(i);

            
        }

        /// <summary>
        /// Pointer to the removing key value can be null, if such key never existed
        /// </summary>
        /// <param name="kid"></param>
        /// <param name="lastElementOfTheKey"></param>
        /// <param name="key"></param>
        /// <param name="ptrToValue"></param>
        /// <param name="WasRemoved">indicates that value existed if true</param>
        /// <param name="retrieveDeletedValue">indicates if we must also return deleted value</param>
        /// <param name="WasRemoved">deleted value as byte[] will be here</param>
        /// <returns></returns>
        public bool RemoveKid(byte kid, bool lastElementOfTheKey, ref byte[] key, out bool WasRemoved, bool retrieveDeletedValue, out byte[] deletedValue)
        {
            WasRemoved = false;
            deletedValue = null;
            long valueStartPtr=0;
            uint valueLength=0;

            //Result true means that we must iterate further
            
            LTrieKid kd = null;
            byte[] storedKey = null;

            if (lastElementOfTheKey)
            {
                //Trying to delete value, as result we must receive bool true, if value existed (and we can change here also count and setup ToWrite = true)

                kd = KidsInNode.GetKidValue();
                if (kd.Exists)
                {
                    //If we want to get value of the deleted key
                    if (retrieveDeletedValue)
                    {
                        deletedValue = this._root.Tree.Cache.ReadValue(KidsInNode.GetKidValue().Ptr, false, out valueStartPtr, out valueLength);                        
                    }
                    
                    KidsInNode.RemoveValueKid();
                    WasRemoved = true;
                    this._root.RecordsCount--;
                    ToWrite = true;
                }

                //In any case going out
                return false;

            }
            else
            {
                if (KidsInNode.ContainsKid(kid))
                {
                    kd = KidsInNode.GetKid(kid);
                    
                    if (kd.LinkToNode)
                        return true;    //Iterating further

                    //Here is link to value, retrieve value
                    //storedKey = ReadKidKeyFromValPtr(kd.Ptr);
                    storedKey = this._root.Tree.Cache.ReadKey(false, kd.Ptr);

                    if (storedKey._ByteArrayEquals(key))
                    {
                        //If we want to get value of the deleted key
                        if (retrieveDeletedValue)
                        {
                            deletedValue = this._root.Tree.Cache.ReadValue(KidsInNode.GetKid(kid).Ptr, false, out valueStartPtr, out valueLength);
                        }


                        KidsInNode.RemoveKid(kid);
                        WasRemoved = true;
                        this._root.RecordsCount--;
                        ToWrite = true;

                        return false;
                    }

                    //Stored key is not the same, going out
                    return false;
                }

                //Such kid was not found we do nothing
                return false;
            }

        }

      
        

        public void ReadSelf(bool useCache,byte[] generationMapLine)
        {
            //Cache is used only in case of Reading Functions
            ToChangeParentNode = false;

            byte[] bKids = this._root.Tree.Cache.GenerationNodeRead(useCache, Pointer, generationMapLine, MaximumKidLineLength);

            if (bKids != null && bKids.Length != 0)
            {
                if (!useCache)
                    KidsBeforeModification = bKids;

                //Console.WriteLine("L: {0}; B: {1}",bKids.Length,bKids.ToBytesString(""));
                //was
                //ParseKids(bKids);                
                //after opt
                this.QuantityReservationSlots = KidsInNode.ParseKids(ref bKids);
                //Console.WriteLine("QRS: {0}", this.QuantityReservationSlots);
            }
        }



        //private void ParseKids(byte[] bKids)
        //{
        //    int i = 0;
        //    //byte[] aKid = null;
        //    int kid = 0;
        //    int ValuePtrOrNodePointer = 0;  //0 - Node, 1 - Value
        //    byte[] ptr = new byte[DefaultPointerLen];

        //    int len = bKids.Length;

        //    this.QuantityReservationSlots = 0;

        //    i = DefaultPointerLen;

        //    byte[] valKid = new byte[DefaultPointerLen];
        //    for (int j = 0; j < DefaultPointerLen; j++)
        //    {
        //        valKid[j] = bKids[j];
        //    }

        //    //KidsInNode.AddKid(256, bKids.Substring(0, DefaultPointerLen));
        //    KidsInNode.AddKid(256, valKid);

        //    //We will accumulate real kid size to form KidsBeforeModification (which is used in rollback and Read threads) 
        //    //In the beginning we read from disk MaximumKidLineLength, so we need to get real KidsBeforeModification.

        //    bool ptrIsEmpty = true;

        //    //For optimization, give the whole func to KidsInNode for one loop, must return Quantity reservation slots
        //    for (; ; )
        //    {
        //        if (i >= len)
        //            return;  //To Form KidsBeforeModification.   or do..While(i<len)

        //        //Try to lower down quantity of substrings
        //        kid = bKids[i];
        //        ValuePtrOrNodePointer = bKids[i + 1];

        //        //faster for instead
        //        //ptr = bKids.Substring(i + 2, DefaultPointerLen);

        //        for (int j = 0; j < DefaultPointerLen; j++)
        //        {
        //            ptr[j] = bKids[i + 2 + j];
        //        }

        //        i += 2 + DefaultPointerLen;

        //        /*optimization for pointer is empty*/
        //        switch (DefaultPointerLen)
        //        {                   
        //            case 5:     
        //                ptrIsEmpty = (
        //                       ptr[0] == 0
        //                       &&
        //                       ptr[1] == 0
        //                       &&
        //                       ptr[2] == 0
        //                       &&
        //                       ptr[3] == 0
        //                       &&
        //                       ptr[4] == 0
        //                       );
        //                break;
        //            case 6:
        //                ptrIsEmpty = (
        //                  ptr[0] == 0
        //                  &&
        //                  ptr[1] == 0
        //                  &&
        //                  ptr[2] == 0
        //                  &&
        //                  ptr[3] == 0
        //                  &&
        //                  ptr[4] == 0
        //                  &&
        //                  ptr[5] == 0
        //                  );
        //                break;
        //            default:
        //                ptrIsEmpty = this._root._IfPointerIsEmpty(ptr);
        //                break;

        //        }
        //        /****************************************/

        //        //Adding Kid only in case if it's active not reserved space

        //        //if (!this._root._IfPointerIsEmpty(ptr))
        //        if (!ptrIsEmpty)
        //        {
                    
        //            if (ValuePtrOrNodePointer == 1)
        //            {
        //                KidsInNode.AddKid(kid, ptr);
        //            }
        //            else
        //            {
        //                KidsInNode.AddKidPointer(kid, ptr);
        //            }
                    

        //        }

        //        //Place is correct, we must get into consideration reserved before slots also
        //        this.QuantityReservationSlots += 1;
        //    }


        //}



        public LTrieKid GetKidAsValue(bool searchForAValue, int kid)
        {
            LTrieKid kidDef = new LTrieKid();

            //Returns null
            if (searchForAValue)
            {
                kidDef = KidsInNode.GetKidValue();
            }
            else
            {
                //Trying to retrieve
                kidDef = KidsInNode.GetKid(kid);


            }

            return kidDef;
        }


        /// <summary>
        /// Returns pointer to the newly stored value
        /// </summary>
        /// <param name="ptr"></param>
        /// <param name="value"></param>
        /// <param name="fullKey"></param>
        /// <param name="startIndex">from which point we should overwrite value</param>
        /// <returns></returns>
        private byte[] TryOverWriteValuePartially(byte[] ptr, ref byte[] value, ref byte[] fullKey, uint startIndex, out long valueStartPtr)
        {
            //valueStartPtr = -1;
            byte[] btFullValueStart = null;

            //Where ptr is a pointer to the stored value

            //Changing format:
            //1 byte - ValueWriteProtocol identification (0 - without FULL reserved size[used after first insert], 1 - with extra reservation space), 
            //FullKeyLen (2 bytes), FullValueLen (4 bytes),[Reservation Length] ,FullKey,FullValue            
            //1 + 2 + 4 + 4 + 100 = 111
            int initRead = 111; //Where 100 is standard max size of the key in case of words (even 50 must be enough), in case of sentences can be much longer, probably we can setup it later

            long lPtr = (long)ptr.DynamicLength_To_UInt64_BigEndian();
            //long lPtr = (long)ptr.EnlargeByteArray_BigEndian(8).To_UInt64_BigEndian();

            byte[] data = this._root.Tree.Storage.Table_Read(false,lPtr, initRead);
            byte protocol = data[0];
            ushort keySize = 0;
            int valueSize = 0;
            int totalReservedSize = 0;

            byte[] btKeySize = new byte[] { data[1], data[2] };
            byte[] btValueSize = new byte[] { data[3], data[4], data[5], data[6] };
            byte[] btTotalReservedSize = null;

            keySize = btKeySize.To_UInt16_BigEndian();

            /*NULL SUPPORT*/
            //Getting old written value info
            //valueSize = (int)btValueSize.To_UInt32_BigEndian();
            if ((data[3] & 0x80) > 0)
            {
                //NULL
                valueSize = 0;
            }
            else
            {
                valueSize = (int)btValueSize.To_UInt32_BigEndian();
            }
            //Getting new value info
            //uint newValueLength = 2147483648; //NULL first bit is 1 from 4 bytes
            uint newValueRealLength = 0;

            if (value != null)
            {
                //then 
                //newValueLength = (uint)value.Length;
                //newValueRealLength = (uint)value.Length;

                newValueRealLength = (uint)valueSize;

                if ((startIndex + value.Length) > valueSize)
                {
                    newValueRealLength = (uint)(startIndex + value.Length);
                }
            }


            /**************/

            switch (protocol)
            {
                case 0:
                    //We don't have reservation identifiers, it happens after first insert into the new place                   
                    break;
                case 1:
                    btTotalReservedSize = new byte[] { data[7], data[8], data[9], data[10] };
                    totalReservedSize = (int)btTotalReservedSize.To_UInt32_BigEndian();
                    break;
            }

            byte[] newData = null;

            //long rollBackFileDataPointer = 0;
            byte[] oldData = null;

            byte[] oldValue = null;
            byte[] newValue = null;

            if (totalReservedSize > 0)
            {
                //Protocol 1

                //We are checking if current value can placed over existing value

                /*NULL SUPPORT*/
                //if ((newValueRealLength + (int)startIndex) <= totalReservedSize)                              
                if (newValueRealLength <= totalReservedSize)                              
                {
                    //OVERWRITE, LEAVING PROTOCOL 1
                    //btValueSize = value.Length.To_4_bytes_array_BigEndian();

                    /*FORMING NEW DATA*/
                    //For now we have to read the full value and copy into it our value
                    //Reading full 
                    /******************/

                    //newData =
                    //    new byte[] { 1 }.ConcatMany(
                    //        btKeySize,
                    //        btValueSize,
                    //        btTotalReservedSize,
                    //        fullKey,
                    //        value
                    //    );

                    //!!!!!!!!!!!!!!!!!   ROLLBACK and READ THREADS
                    //Checking if old data already in memory, if not re-read PROTOCOL 1 here
                    if (data.Length >= (11 + totalReservedSize + keySize))
                    {
                        oldData = data.Substring(0, 11 + totalReservedSize + keySize);

                        oldValue = data.Substring(11 + keySize, valueSize); 
                    }
                    else
                    {
                        oldData = this._root.Tree.Storage.Table_Read(false, lPtr, 11 + totalReservedSize + keySize);

                        oldValue = oldData.Substring(11 + keySize, valueSize); 
                    }


                    /*FORMING NEW DATA*/
                    //For now we have to read the full value and copy into it our value
                    /*NULL SUPPORT*/
                    //newValue = oldValue.CopyInsideArrayCanGrow((int)startIndex, value);
                    newValue = oldValue.CopyInsideArrayCanGrow((int)startIndex, ((value == null) ? new byte[0] : value));
                    /**************/

                    btValueSize = ((uint)newValue.Length).To_4_bytes_array_BigEndian();
                    
                    newData =
                       new byte[] { 1 }.ConcatMany(
                           btKeySize,
                           btValueSize,
                           btTotalReservedSize,
                           fullKey,
                           newValue
                   );

                    //rollBackFileDataPointer = this._root.Tree.RollerBack.WriteRollBackData(1, ptr, ref oldData);
                    //this._root.Tree.Cache.AddPointerToTheValueInRollBackFile(ptr, rollBackFileDataPointer);
                    /////////////////////////////////////////////////

                    //this._root.Tree.Storage.WriteByOffset(ptr,newData);

                    this._root.Tree.Cache.ValueWritingOver(ptr, ref oldData, ref newData, ref fullKey);

                    valueStartPtr = (long)ptr.DynamicLength_To_UInt64_BigEndian() + 11 + fullKey.Length;

                    return ptr;
                }
                else
                {
                    //NO OVERWRITE, CREATE NEW PROTOCOL 1
                    if (data.Length >= (11 + totalReservedSize + keySize))
                    {
                        //no need to make this operation here
                        //oldData = data.Substring(0, 11 + totalReservedSize + keySize);

                        oldValue = data.Substring(11 + keySize, valueSize); 
                    }
                    else
                    {
                        oldData = this._root.Tree.Storage.Table_Read(false, lPtr, 11 + totalReservedSize + keySize);

                        oldValue = oldData.Substring(11 + keySize, valueSize);
                    }


                    /*FORMING NEW DATA*/
                    //For now we have to read the full value and copy into it our value
                    /*NULL SUPPORT*/
                    //newValue = oldValue.CopyInsideArrayCanGrow((int)startIndex, value);
                    newValue = oldValue.CopyInsideArrayCanGrow((int)startIndex, ((value == null) ? new byte[0] : value));
                    /**************/

                    btValueSize = ((uint)newValue.Length).To_4_bytes_array_BigEndian();

                    btTotalReservedSize = btValueSize;

                    newData =
                        new byte[] { 1 }.ConcatMany(
                            btKeySize,
                            btValueSize,
                            btTotalReservedSize,
                            fullKey,
                            newValue
                        );

                    //return this._root.Tree.Storage.WriteToTheEnd(newData);
                    btFullValueStart = this._root.Tree.Cache.ValueWritingEnd(ref newData, ptr);

                    valueStartPtr = (long)btFullValueStart.DynamicLength_To_UInt64_BigEndian() + 11 + fullKey.Length;

                    return btFullValueStart;

                }
            }
            else
            {
                //Protocol 0 (after first insert)
                /*NULL SUPPORT*/
                //if ((newValueRealLength + (int)startIndex) == valueSize)
                if (newValueRealLength == valueSize)
                {
                    //new value resides the same space as previous we save it on top leaving protocol 0

                    //OVERWRITE, LEAVING PROTOCOL 0

                    //newData =
                    //    new byte[] { 0 }.ConcatMany(
                    //        btKeySize,
                    //        btValueSize,
                    //        fullKey,
                    //        value
                    //    );

                    //!!!!!!!!!!!!!!!!!   ROLLBACK and READ THREADS
                    //Checking if old data already in memory, if not re-read PROTOCOL 1 here
                    if (data.Length >= (7 + keySize + valueSize))
                    {
                        oldData = data.Substring(0, 7 + keySize + valueSize);

                        oldValue = data.Substring(7 + keySize, valueSize); 
                    }
                    else
                    {
                        oldData = this._root.Tree.Storage.Table_Read(false, lPtr, 7 + keySize + valueSize);

                        oldValue = oldData.Substring(7 + keySize, valueSize); 
                    }


                    /*FORMING NEW DATA*/
                    //For now we have to read the full value and copy into it our value
                    /*NULL SUPPORT*/
                    //newValue = oldValue.CopyInsideArrayCanGrow((int)startIndex, value);
                    newValue = oldValue.CopyInsideArrayCanGrow((int)startIndex, ((value == null) ? new byte[0] : value));                    
                    /**************/

                    btValueSize = ((uint)newValue.Length).To_4_bytes_array_BigEndian();

                    newData =
                       new byte[] { 0 }.ConcatMany(
                           btKeySize,
                           btValueSize,                      
                           fullKey,
                           newValue
                   );

                    //rollBackFileDataPointer = this._root.Tree.RollerBack.WriteRollBackData(1, ptr, ref oldData);
                    //this._root.Tree.Cache.AddPointerToTheValueInRollBackFile(ptr, rollBackFileDataPointer);
                    /////////////////////////////////////////////////

                    //this._root.Tree.Storage.WriteByOffset(ptr, newData);

                    this._root.Tree.Cache.ValueWritingOver(ptr, ref oldData, ref newData, ref fullKey);

                    valueStartPtr = (long)ptr.DynamicLength_To_UInt64_BigEndian() + 7 + fullKey.Length;

                    return ptr;
                }
                ///*NULL SUPPORT*/
                //else if ((newValueRealLength + 4 + (int)startIndex) <= valueSize)
                //{
                //    //we can change protocol to 1, staying with this update on the same place

                //    //OVERWRITE CHANGING PROTOCOL TO 1
                //    btTotalReservedSize = ((uint)(valueSize - 4)).To_4_bytes_array_BigEndian();



                //    //btValueSize = value.Length.To_4_bytes_array_BigEndian();

                //    //newData =
                //    //    new byte[] { 1 }.ConcatMany(
                //    //        btKeySize,
                //    //        btValueSize,
                //    //        btTotalReservedSize,
                //    //        fullKey,
                //    //        value
                //    //    );

                //    //!!!!!!!!!!!!!!!!!   ROLLBACK and READ THREADS
                //    //Checking if old data already in memory, if not re-read PROTOCOL 1 here
                //    if (data.Length >= (7 + keySize + valueSize))
                //    {
                //        oldData = data.Substring(0, 7 + keySize + valueSize);

                //        oldValue = data.Substring(7 + keySize, valueSize); 
                //    }
                //    else
                //    {
                //        oldData = this._root.Tree.Storage.Read(lPtr, 7 + keySize + valueSize);

                //        oldValue = oldData.Substring(7 + keySize, valueSize);
                //    }


                //    /*FORMING NEW DATA*/
                //    //For now we have to read the full value and copy into it our value
                //    /*NULL SUPPORT*/
                //    //newValue = oldValue.CopyInsideArrayCanGrow((int)startIndex, value);
                //    newValue = oldValue.CopyInsideArrayCanGrow((int)startIndex, ((value == null) ? new byte[0] : value));
                //    /**************/
                     

                //    btValueSize = ((uint)newValue.Length).To_4_bytes_array_BigEndian();

                //    newData =
                //       new byte[] { 1 }.ConcatMany(
                //           btKeySize,
                //           btValueSize,
                //           btTotalReservedSize,
                //           fullKey,
                //           newValue
                //   );


                //    //rollBackFileDataPointer = this._root.Tree.RollerBack.WriteRollBackData(1, ptr, ref oldData);
                //    //this._root.Tree.Cache.AddPointerToTheValueInRollBackFile(ptr, rollBackFileDataPointer);
                //    /////////////////////////////////////////////////

                //    //this._root.Tree.Storage.WriteByOffset(ptr, newData);

                //    this._root.Tree.Cache.ValueWritingOver(ptr, ref oldData, ref newData, ref fullKey);

                //    valueStartPtr = (long)ptr.DynamicLength_To_UInt64_BigEndian() + 11 + fullKey.Length;

                //    return ptr;
                //}
                else
                {
                    //NO OVERWRITE, CREATE NEW PROTOCOL 1

                    if (data.Length >= (7 + keySize + valueSize))
                    {
                        //no need to do this operation here, we don't store oldData
                        //oldData = data.Substring(0, 7 + keySize + valueSize);

                        oldValue = data.Substring(7 + keySize, valueSize); 
                    }
                    else
                    {
                        oldData = this._root.Tree.Storage.Table_Read(false, lPtr, 7 + keySize + valueSize);

                        oldValue = oldData.Substring(7 + keySize, valueSize);
                    }


                    /*FORMING NEW DATA*/
                    //For now we have to read the full value and copy into it our value
                    /*NULL SUPPORT*/
                    //newValue = oldValue.CopyInsideArrayCanGrow((int)startIndex, value);
                    newValue = oldValue.CopyInsideArrayCanGrow((int)startIndex, ((value == null) ? new byte[0] : value));
                    /**************/

                    btValueSize = ((uint)newValue.Length).To_4_bytes_array_BigEndian();
                    btTotalReservedSize = btValueSize;

                    newData =
                       new byte[] { 1 }.ConcatMany(
                           btKeySize,
                           btValueSize,
                           btTotalReservedSize,
                           fullKey,                       
                           newValue
                   );

                    //btValueSize = value.Length.To_4_bytes_array_BigEndian();
                    //btTotalReservedSize = btValueSize;

                    //newData =
                    //    new byte[] { 1 }.ConcatMany(
                    //        btKeySize,
                    //        btValueSize,
                    //        btTotalReservedSize,
                    //        fullKey,
                    //        value
                    //    );

                    //return this._root.Tree.Storage.WriteToTheEnd(newData);

                    btFullValueStart = this._root.Tree.Cache.ValueWritingEnd(ref newData, ptr);

                    valueStartPtr = (long)btFullValueStart.DynamicLength_To_UInt64_BigEndian() + 11 + fullKey.Length;

                    return btFullValueStart;
                }

            }
        }


        private byte[] WriteKidValuePartially(ref byte[] fullKey, ref byte[] value, bool tryToOverwrite, byte[] overWritePointer, uint startIndex, out long valueStartPtr)
        {
            //valueStartPtr is a ptr to the start of the value (after protocol, keyLenValLen and FullKey)
            

            /*NULL SUPPORT*/
            //if (value == null)
            //    value = new byte[0];
            /***************/

            //!!!!!!!!!!!!!!!!!  OverWriteIsAllowed will not work good for InsertPart, so best way act here in common way. 
            //!!!!!!!!!!!!!!!!!  Who needs speed must use SelectFullValue->CopyInside->InsertFullValue 
            //if (!_root.Tree.OverWriteIsAllowed)
            //    tryToOverwrite = false;


            //Console.WriteLine("Writing Key {0} value", fullKey.ToBytesString(""));
            if (tryToOverwrite)
            {
                //Console.WriteLine("OVERWRITING");

                //trying to get value size
                return TryOverWriteValuePartially(overWritePointer, ref value, ref fullKey, startIndex, out valueStartPtr);
            }

            //Changing format:
            //1 byte protocol,FullKeyLen (2 bytes), FullValueLen (4 bytes),[4 bytes reeserved space if protocol 1, if 0 nothing],FullKey,FullValue

            //reserving empty space
            /*NULL SUPPORT*/
            //byte[] oldData = new byte[value.Length + (int)startIndex];
            //byte[] newValue = oldData.CopyInsideArrayCanGrow((int)startIndex, value);

            byte[] oldData = new byte[((value == null) ? 0 : value.Length) + (int)startIndex];
            byte[] newValue = oldData.CopyInsideArrayCanGrow((int)startIndex, value);


            /***************/

            //writing as protocol 0
            byte[] data =
                new byte[] { 0 }
                .ConcatMany(
                    ((ushort)fullKey.Length).To_2_bytes_array_BigEndian(),
                    /*NULL SUPPORT*/
                    ((uint)(((value == null) ? 0 : value.Length) + (int)startIndex)).To_4_bytes_array_BigEndian(),
                    /***************/
                    fullKey,
                    newValue
                );

            byte[] btFullValueStart = this._root.Tree.Cache.ValueWritingEnd(ref data,null);


            valueStartPtr = (long)btFullValueStart.DynamicLength_To_UInt64_BigEndian() + 7 + fullKey.Length;

            return btFullValueStart;

            //return this._root.Tree.Storage.WriteToTheEnd(data);


        }


        /// <summary>
        /// Returns pointer to the newly stored value
        /// </summary>
        /// <param name="ptr"></param>
        /// <param name="value"></param>
        /// <param name="fullKey"></param>
        /// <returns></returns>
        private byte[] TryOverWriteValue(byte[] ptr, ref byte[] value, ref byte[] fullKey)
        {
            //Where ptr is a pointer to the stored value

            //Changing format:
            //1 byte - ValueWriteProtocol identification (0 - without FULL reserved size[used after first insert], 1 - with extra reservation space), 
            //FullKeyLen (2 bytes), FullValueLen (4 bytes),[Reservation Length] ,FullKey,FullValue            
            //1 + 2 + 4 + 4 + 100 = 111
            int initRead = 111; //Where 100 is standard max size of the key in case of words (even 50 must be enough), in case of sentences can be much longer, probably we can setup it later

            long lPtr = (long)ptr.DynamicLength_To_UInt64_BigEndian();
            //long lPtr = (long)ptr.EnlargeByteArray_BigEndian(8).To_UInt64_BigEndian();

            byte[] data = this._root.Tree.Storage.Table_Read(false, lPtr, initRead);
            byte protocol = data[0];
            ushort keySize = 0;
            int valueSize = 0;
            int totalReservedSize = 0;

            byte[] btKeySize = new byte[] { data[1], data[2] };
            byte[] btValueSize = new byte[] { data[3], data[4], data[5], data[6] };
            byte[] btTotalReservedSize = null;

            keySize = btKeySize.To_UInt16_BigEndian();

            /*NULL SUPPORT*/
            //Getting old written value info
            if ((data[3] & 0x80) > 0)
            {
                //NULL
                valueSize = 0;
            }
            else
            {
                valueSize = (int)btValueSize.To_UInt32_BigEndian();
            }
            //Getting new value info
            uint newValueLength = 2147483648; //NULL first bit is 1 from 4 bytes
            uint newValueRealLength = 0;

            if (value != null)
            {
                //then 
                newValueLength = (uint)value.Length;
                newValueRealLength = (uint)value.Length;
            }
            /**************/

            switch (protocol)
            {
                case 0:
                    //We don't have reservation identifiers, it happens after first insert into the new place                   
                    break;
                case 1:
                    btTotalReservedSize = new byte[] { data[7], data[8], data[9], data[10] };
                    totalReservedSize = (int)btTotalReservedSize.To_UInt32_BigEndian();                    
                    break;
            }

            byte[] newData = null;

            //long rollBackFileDataPointer = 0;
            byte[] oldData = null;

            if (totalReservedSize > 0)
            {
                //Protocol 1

                //We are checking if current value can placed over existing value

                /*NULL SUPPORT*/
                if (newValueRealLength <= totalReservedSize)
                {
                    //OVERWRITE, LEAVING PROTOCOL 1

                    /*NULL SUPPORT*/
                    //btValueSize = ((uint)value.Length).To_4_bytes_array_BigEndian();
                    btValueSize = newValueLength.To_4_bytes_array_BigEndian();
                    /**************/
                    
                    newData =
                        new byte[] {1}.ConcatMany(
                            btKeySize,
                            btValueSize,
                            btTotalReservedSize,
                            fullKey,
                            value
                        );

                    //!!!!!!!!!!!!!!!!!   ROLLBACK and READ THREADS
                    //Checking if old data already in memory, if not re-read PROTOCOL 1 here
                    if (data.Length >= (11 + totalReservedSize + keySize))
                        oldData = data.Substring(0, 11 + totalReservedSize + keySize);
                    else
                        oldData = this._root.Tree.Storage.Table_Read(false, lPtr, 11 + totalReservedSize + keySize);

                    //rollBackFileDataPointer = this._root.Tree.RollerBack.WriteRollBackData(1, ptr, ref oldData);
                    //this._root.Tree.Cache.AddPointerToTheValueInRollBackFile(ptr, rollBackFileDataPointer);
                    /////////////////////////////////////////////////

                    //this._root.Tree.Storage.WriteByOffset(ptr,newData);

                    this._root.Tree.Cache.ValueWritingOver(ptr, ref oldData, ref newData, ref fullKey);

                    return ptr;
                }
                else
                {                   
                    //NO OVERWRITE, CREATE NEW PROTOCOL 1

                    /*NULL SUPPORT*/
                    //btValueSize = ((uint)value.Length).To_4_bytes_array_BigEndian();
                    //btTotalReservedSize = btValueSize;
                    btValueSize = newValueLength.To_4_bytes_array_BigEndian();
                    btTotalReservedSize = newValueRealLength.To_4_bytes_array_BigEndian();
                    /**************/

                    newData =
                        new byte[] { 1 }.ConcatMany(
                            btKeySize,
                            btValueSize,
                            btTotalReservedSize,
                            fullKey,
                            value
                        );

                    //return this._root.Tree.Storage.WriteToTheEnd(newData);
                    return this._root.Tree.Cache.ValueWritingEnd(ref newData, ptr);
                    
                }
            }
            else
            {
                //Protocol 0 (after first insert)

                /*NULL SUPPORT*/
                if (newValueRealLength == valueSize)
                {
                    //new value resides the same space as previous we save it on top leaving protocol 0

                    //OVERWRITE, LEAVING PROTOCOL 0

                    /*NULL SUPPORT*/
                    btValueSize = newValueLength.To_4_bytes_array_BigEndian();
                    /**************/

                    newData =
                        new byte[] { 0 }.ConcatMany(
                            btKeySize,
                            btValueSize,
                            fullKey,
                            value
                        );

                    //!!!!!!!!!!!!!!!!!   ROLLBACK and READ THREADS
                    //Checking if old data already in memory, if not re-read PROTOCOL 1 here
                    if (data.Length >= (7 + keySize + valueSize))
                        oldData = data.Substring(0, 7 + keySize + valueSize);
                    else
                        oldData = this._root.Tree.Storage.Table_Read(false, lPtr, 7 + keySize + valueSize);

                    //rollBackFileDataPointer = this._root.Tree.RollerBack.WriteRollBackData(1, ptr, ref oldData);
                    //this._root.Tree.Cache.AddPointerToTheValueInRollBackFile(ptr, rollBackFileDataPointer);
                    /////////////////////////////////////////////////

                    //this._root.Tree.Storage.WriteByOffset(ptr, newData);

                    this._root.Tree.Cache.ValueWritingOver(ptr, ref oldData, ref newData, ref fullKey);

                    return ptr;
                }
                /*NULL SUPPORT*/
                else if ((newValueRealLength + 4) <= valueSize)
                {
                    //we can change protocol to 1, staying with this update on the same place

                    //OVERWRITE CHANGING PROTOCOL TO 1
                    /*NULL SUPPORT*/
                    btTotalReservedSize = ((uint)(valueSize - 4)).To_4_bytes_array_BigEndian();
                    //btValueSize = ((uint)value.Length).To_4_bytes_array_BigEndian();
                    btValueSize = newValueLength.To_4_bytes_array_BigEndian();
                    /***************/

                    newData =
                        new byte[] { 1 }.ConcatMany(
                            btKeySize,
                            btValueSize,
                            btTotalReservedSize,
                            fullKey,
                            value
                        );

                    //!!!!!!!!!!!!!!!!!   ROLLBACK and READ THREADS
                    //Checking if old data already in memory, if not re-read PROTOCOL 1 here
                    if (data.Length >= (7 + keySize + valueSize))
                        oldData = data.Substring(0, 7 + keySize + valueSize);
                    else
                        oldData = this._root.Tree.Storage.Table_Read(false, lPtr, 7 + keySize + valueSize);

                    //rollBackFileDataPointer = this._root.Tree.RollerBack.WriteRollBackData(1, ptr, ref oldData);
                    //this._root.Tree.Cache.AddPointerToTheValueInRollBackFile(ptr, rollBackFileDataPointer);
                    /////////////////////////////////////////////////

                    //this._root.Tree.Storage.WriteByOffset(ptr, newData);

                    this._root.Tree.Cache.ValueWritingOver(ptr, ref oldData, ref newData, ref fullKey);


                    return ptr;
                }
                else
                {
                    //NO OVERWRITE, CREATE NEW PROTOCOL 1

                    /*NULL SUPPORT*/
                    //btValueSize = ((uint)value.Length).To_4_bytes_array_BigEndian();
                    //btTotalReservedSize = btValueSize;

                    btValueSize = newValueLength.To_4_bytes_array_BigEndian();
                    btTotalReservedSize = newValueRealLength.To_4_bytes_array_BigEndian();
                    /***************/

                    newData =
                        new byte[] { 1 }.ConcatMany(
                            btKeySize,
                            btValueSize,
                            btTotalReservedSize,
                            fullKey,
                            value
                        );
                    
                    //return this._root.Tree.Storage.WriteToTheEnd(newData);
                    return this._root.Tree.Cache.ValueWritingEnd(ref newData, ptr);
                }
               
            }
        }


        private byte[] WriteKidValue(ref byte[] fullKey, ref byte[] value, bool tryToOverwrite, byte[] overWritePointer)
        {
            //Here value alread of normalized size Int32.MaxValue

            /*NULL SUPPORT*/
            //if (value == null)
            //    value = new byte[0];
            /***************/

            if (!_root.Tree.OverWriteIsAllowed)
                tryToOverwrite = false;

            //Console.WriteLine("Writing Key {0} value", fullKey.ToBytesString(""));
            if (tryToOverwrite)
            {
                //Console.WriteLine("OVERWRITING");

                //trying to get value size
                return TryOverWriteValue(overWritePointer, ref value, ref fullKey);
            }            

            //Changing format:
            //1 byte protocol,FullKeyLen (2 bytes), FullValueLen (4 bytes),[4 bytes reeserved space if protocol 1, if 0 nothing],FullKey,FullValue

            /*Adding NULL support*/
            uint valueLength = 2147483648; //NULL first bit is 1 from 4 bytes

            if (value != null)
            {
                //then 
                valueLength = (uint)value.Length;
            }
            /*********************/

            byte[] data =
                new byte[] {0}
                .ConcatMany(
                    ((ushort)fullKey.Length).To_2_bytes_array_BigEndian(),
                     /*NULL SUPPORT*/
                    //((uint)value.Length).To_4_bytes_array_BigEndian(),
                    valueLength.To_4_bytes_array_BigEndian(),
                    /***************/
                    fullKey,
                    value
                );

            return this._root.Tree.Cache.ValueWritingEnd(ref data, null);

            //return this._root.Tree.Storage.WriteToTheEnd(data);


        }



        public void WriteSelf(byte[] generationMapLine)
        {
            if (!ToWrite)
            {
                return;
            }

            //Returns NULL if no Kids
            //byte[] bKids = PrepareKidsForSave();

            int reservation = this.GetQuantityOfReservationSlots();
            bool OverWrite = true;

            //Standard behaviour overwriting of root nodes is permitted

            //WHEN OverWriteIsAllowed SHOULD NOT INFLUENT on nodes (KVPs and dynamic blocks will be overwritten)          
            //if (reservation > this.QuantityReservationSlots)
            //{
            //    OverWrite = false;
            //    this.QuantityReservationSlots = reservation;
            //}
            //*************************

            //WHEN OverWriteIsAllowed SHOULD INFLUENT on nodes (KVPs and dynamic blocks will be overwritten)          
            if (_root.Tree.OverWriteIsAllowed)
            {
                //Standard behaviour overwriting of root nodes is permitted
                if (reservation > this.QuantityReservationSlots)
                {
                    OverWrite = false;
                    this.QuantityReservationSlots = reservation;
                }
            }
            else
            {
                //We don't want to overwrite Trie-Nodes, but always write them to the end
                OverWrite = false;
            }
            //*************************

            // /*If kids are fully empty (no Kids and Value, then we setup pointer to Empty)
            // * and parent will have to renew its link on empty link. Then we don't have problems
            // * iterations forawrd backward. We don't save last change saved for this NodeLine. 
            // * Problem can appear after remove, previous node still has link to the empty line and it influences backward and forward work, inside of for(;;) going only down           
            // */
            //if (KidsInNode.ValueIsEmpty && KidsInNode.Count() == 0)
            //{
            //    this.Pointer = this._root.EmptyPointer;
            //    //Setting up flags regulating map writing
            //    ToChangeParentNode = true;
            //    ToRemoveFromParentNode = true;
            //    ToWrite = false;
            //    return;
            //}
            ///************************************************************************************/

            byte[] bKids = KidsInNode.GetKidsForSave(this.QuantityReservationSlots);


            ushort sLen = 0;
            if (bKids != null)
            {
                sLen = (ushort)bKids.Length;
            }
            else
            {                
                //NOTING TO SAVE. GENERATION NODE DIDN'T CHANGE
                //KidsBeforeModification were not changed 
                ToChangeParentNode = false;
                ToWrite = false;                
                return;
            }


            //We need correction for the case when we use one cursor for Select (with standard "write" visibility scope) and Insert statement in one transaction            
            if (KidsBeforeModification != null && bKids.Length > KidsBeforeModification.Length)
                OverWrite = false;

            //Checking if we can write on the same place or overWrite  
            if (!OverWrite)
            {
                //We write on new place
                ToChangeParentNode = true;


                //////////////   ROLLBACK SUPPORT
                //this._root.Tree.RollerBack.BlackListPointer(Pointer);
                ///////////////////////////////////////////////////////////////////////////////////

                ////Writing to the end
                //Pointer = this._root.Tree.Storage.WriteToTheEnd(
                //   sLen.To_2_bytes_array_BigEndian()
                //   .Concat(bKids)
                //   );
                byte[] xData = 
                                sLen.To_2_bytes_array_BigEndian()
                               .Concat(bKids);

                Pointer = this._root.Tree.Cache.GenerationNodeWritingEnd(Pointer, xData);
            }
            else
            {
                //Writing on the same place
                ToChangeParentNode = false;

                //////////////   ROLLBACK SUPPORT
                //byte[] rollBackData = ((short)KidsBeforeModification.Length).To_2_bytes_array_BigEndian().Concat(KidsBeforeModification);
                //this._root.Tree.RollerBack.WriteRollBackData(1, Pointer, ref rollBackData);
                ///////////////////////////////////////////////////////////////////////////////////

                ////Writing by pointer
                //this._root.Tree.Storage.WriteByOffset(Pointer,
                //   sLen.To_2_bytes_array_BigEndian()
                //   .Concat(bKids)
                //   );

                /*DEBUG*/
                //if (KidsBeforeModification == null)
                //{
                //    throw new Exception(String.Format("!!!DBreeze debug exception, LTrieGenNode KBM null, Reservation: {0}; QRS: {1};",reservation,this.QuantityReservationSlots));
                //}
                /*DEBUG*/


                //byte[] oldData = ((ushort)KidsBeforeModification.Length).To_2_bytes_array_BigEndian().Concat(KidsBeforeModification);
                byte[] newData = sLen.To_2_bytes_array_BigEndian().Concat(bKids);
                //this._root.Tree.Cache.GenerationNodeWritingOver(Pointer, newData);
                this._root.Tree.Cache.GenerationNodeWritingOver(Pointer, newData, generationMapLine, this.KidsBeforeModification);
            }

            ToWrite = false;

            //CHANGING KidsBeforeModification right here
            //Why here: 
            //1. KidsBeforeModification we use for Reading Threads to Read them and we make its copy in special Dictionary
            //2. Generation.WriteSelf works in 2 cases, when we need to load other generation node (then it's safe) and after Commit
            //   in this case generation node stays, but we change KidsBeforeModification in commit in case of mistake we clean generation map
            KidsBeforeModification = bKids;
        }
             


        /// <summary>
        /// Calculating Quantity of Reservations slots for 0 and 1 Evolutions.
        /// </summary>
        /// <param name="current"></param>
        /// <returns></returns>
        private int GetQuantityOfReservationSlots()
        {
            
            //We check quantity of Kids and Quantity of available reservations
            //and depending upon the correspondence quantity we decide how much to add to 0 and 1 Evolution tail
            //QuantityAvailableReservations
            
            int kc = KidsInNode.Count();

            if (kc < 2) return 1;

            if (kc == 2) return 2;
            if (kc < 5) return 4;
            if (kc < 9) return 8;
            if (kc < 17) return 16;
            if (kc < 33) return 32;
            if (kc < 65) return 64;
            if (kc < 129) return 128;

            return 256;


            //if (kc < 2) return 1;
            //if (kc > 128) return 256;
            //kc--;

            //if ((kc & 64) == 64) return 128;
            //if ((kc & 32) == 32) return 64;
            //if ((kc & 16) == 16) return 32;
            //if ((kc & 8) == 8) return 16;
            //if ((kc & 4) == 4) return 8;
            //if ((kc & 2) == 2) return 4;
            //if ((kc & 1) == 1) return 2;

            //return 256;
            
        }

    }
}
