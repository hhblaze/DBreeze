/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.LianaTrie;
using DBreeze.DataTypes;
using DBreeze.Utils;

namespace DBreeze.DataTypes
{
    /// <summary>
    /// Row
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    public class Row<TKey,TValue>
    {
        //LTrieRootNode _root = null;
        //byte[] _ptrToValue = null;
        bool _exists = false;
        bool _useCache = false;

        internal NestedTable nestedTable = null;

        TKey _key;
        LTrie _masterTrie = null;
        LTrieRow _row = null;

        //byte[] btKey = null; 

        public Row(LTrieRow row, LTrie masterTrie, bool useCache)
        {
            if (row == null)
                _exists = false;
            else
            {
                _row = row;
                //_root = row._root;
                //_ptrToValue = row.LinkToValue;
                _exists = row.Exists;
            }
            _useCache = useCache;
            _masterTrie = masterTrie;

            if (_exists)
                _key = DataTypesConvertor.ConvertBack<TKey>(row.Key);
        }

        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="root"></param>
        ///// <param name="ptrToValue"></param>
        ///// <param name="exists"></param>
        ///// <param name="key"></param>
        ///// <param name="useCache">(useCache == true) ? READ : READ_SYNCHRO   models</param>
        //public Row(LTrieRootNode root, byte[] ptrToValue, bool exists,byte[] key,bool useCache)
        //{
        //    _root = root;
        //    _ptrToValue = ptrToValue;
        //    _exists = exists;
        //    _useCache = useCache;
        //    //btKey = key;

        //    if (_exists)
        //        _key = DataTypesConvertor.ConvertBack<TKey>(key);
        //}

        /// <summary>
        /// Exists
        /// </summary>
        public bool Exists
        {
            get { return _exists; }
        }

        /// <summary>
        /// Key
        /// </summary>
        public TKey Key
        {
            get
            {
                return _key;
            }
        }

        /// <summary>
        /// We are inside of the row.
        /// <para>This Method will give you ability to the nested tables which can be stored inside of table by tableIndex</para>
        /// </summary>
        /// <returns></returns>
        public NestedTable GetTable(uint tableIndex)
        {
            if (!_exists)
                return new NestedTable(null,false,false);

            ///////////  FOR NOW allow insert from master is always false, later we have to change Transaction.Insert, and insertPart to return also a row???

            //FOR NOW - NO DIFFERENCE
            if(nestedTable == null)
            {
                //Master row select
                var nt = _row.Root.Tree.GetTable(_row, ref _row.Key, tableIndex, _masterTrie, false, this._useCache);

                //if (_masterTrie != null)
                //    nt.ValuesLazyLoadingIsOn = _masterTrie.ValuesLazyLoadingIsOn;

                return nt;
            }
            else
            {
                
                //Nested table
                var nt = _row.Root.Tree.GetTable(_row, ref _row.Key, tableIndex, _masterTrie, nestedTable._insertAllowed, this._useCache);
                //nt.ValuesLazyLoadingIsOn = _masterTrie.ValuesLazyLoadingIsOn;
                return nt;
            }

            
            //Check if table exists
            //must be supplied horizontal index

            //2 cases one is master table, second is nested table

            //Nested Table we can get in master via SelectTable


            //if (nestedTable == null)
            //    return new NestedTable(null, false, false);

            //return this.nestedTable;//.GetTable<TNestedKey>(TNestedKey
        }
        

        /// <summary>
        /// Returns partial value representation starting from specif index and specified length.
        /// <para>To get full value as byte[] use GetValuePart(0)</para>
        /// </summary>
        /// <param name="startIndex"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public byte[] GetValuePart(uint startIndex, uint length)
        {

            if (_exists)
            {
                //Bringing arguments to int scope
                if ((startIndex + length) > Int32.MaxValue)
                {
                    length = Int32.MaxValue - startIndex;
                }

                if (_row.ValueIsReadOut)
                    return _row.GetPartialValue(startIndex, length, true);  //Cache plays no role here


                long valueStartPointer = 0;
                uint valueFullLength = 0;
                //return this._root.Tree.Cache.ReadValuePartially(this._ptrToValue, startIndex, length, this._useCache, out valueStartPointer, out valueFullLength);
                return this._row.Root.Tree.Cache.ReadValuePartially(this._row.LinkToValue, startIndex, length, this._useCache, out valueStartPointer, out valueFullLength);
            }

            return null;
        }

        /// <summary>
        /// Returns partial value representation starting from specif index till and till the end of value.
        /// </summary>
        /// <param name="startIndex"></param>
        /// <returns></returns>
        public byte[] GetValuePart(uint startIndex)
        {
            uint length = Int32.MaxValue;

            if (_exists)
            {
                //Bringing arguments to int scope
                if ((startIndex + length) > Int32.MaxValue)
                {
                    length = Int32.MaxValue - startIndex;
                }

                if (_row.ValueIsReadOut)
                    return _row.GetFullValue(true); //Cache plays no role here

                long valueStartPointer = 0;
                uint valueFullLength = 0;
                return this._row.Root.Tree.Cache.ReadValuePartially(this._row.LinkToValue, startIndex, length, this._useCache, out valueStartPointer, out valueFullLength);
            }

            return null;
        }

        /// <summary>
        /// Returns physical link to key/value if it exists, otherwise null,
        /// this link can be used by SelectDirect (always returns 8 bytes)
        /// </summary>
        public byte[] LinkToValue
        {
            get
            {
                if (_exists)
                {
                    return this._row.LinkToValue.EnlargeByteArray_BigEndian(8);
                }

                return null;
            }
        }


        /// <summary>
        /// Insert dynamic length datablock is possible via tran.InsertDataBlock or NestedTable.InsertDataBlock.
        /// <para></para>
        /// can return null.
        /// </summary>
        /// <param name="startIndex"></param>
        /// <returns>Returns datablock which identifier is stored in this row from specified index.</returns>
        public byte[] GetDataBlock(uint startIndex=0)
        {
            byte[] dataBlockId = null;

            if (_exists)
            {
                if (_row.ValueIsReadOut)
                {
                    if (_row.Value == null)
                        return null;

                    dataBlockId = _row.Value.Substring((int)startIndex, 16);

                    return this._row.Root.Tree.Cache.ReadDynamicDataBlock(ref dataBlockId, this._useCache);
                }

                long valueStartPointer = 0;
                uint valueFullLength = 0;
                dataBlockId = this._row.Root.Tree.Cache.ReadValuePartially(this._row.LinkToValue, startIndex, 16, this._useCache, out valueStartPointer, out valueFullLength);
                return this._row.Root.Tree.Cache.ReadDynamicDataBlock(ref dataBlockId, this._useCache);
            }

            return null;
        }


        /// <summary>
        /// Returns datablock which fixed address, which identifier is stored in this row from specified index.
        /// </summary>
        /// <typeparam name="TVal"></typeparam>
        /// <param name="startIndex"></param>
        /// <returns></returns>
        public TVal GetDataBlockWithFixedAddress<TVal>(uint startIndex = 0)
        {
            byte[] dataBlockId = null;

            if (_exists)
            {
                if (_row.ValueIsReadOut)
                {
                    if (_row.Value == null)
                        return default(TVal);

                    dataBlockId = _row.Value.Substring((int)startIndex, 16);

                    //dataBlockId=this._row.Root.Tree.Cache.ReadDynamicDataBlock(ref dataBlockId, this._useCache);
                    //return DataTypesConvertor.ConvertBack<TValue>(this._row.Root.Tree.Cache.ReadDynamicDataBlock(ref dataBlockId, this._useCache));

                    //return this._row.Root.Tree.Cache.ReadDynamicDataBlock(ref dataBlockId, this._useCache);

                }

                long valueStartPointer = 0;
                uint valueFullLength = 0;
                dataBlockId = this._row.Root.Tree.Cache.ReadValuePartially(this._row.LinkToValue, startIndex, 16, this._useCache, out valueStartPointer, out valueFullLength);
                //return this._row.Root.Tree.Cache.ReadDynamicDataBlock(ref dataBlockId, this._useCache);

                //dataBlockId = this._row.Root.Tree.Cache.ReadDynamicDataBlock(ref dataBlockId, this._useCache);
                //return DataTypesConvertor.ConvertBack<TValue>(this._row.Root.Tree.Cache.ReadDynamicDataBlock(ref dataBlockId, this._useCache));
            }

            if (dataBlockId == null)
                return default(TVal);

            dataBlockId = this._row.Root.Tree.Cache.ReadDynamicDataBlock(ref dataBlockId, this._useCache);
            return DataTypesConvertor.ConvertBack<TVal>(this._row.Root.Tree.Cache.ReadDynamicDataBlock(ref dataBlockId, this._useCache));
        }

        /// <summary>
        /// Concept of the objects storage (read docu from 20170321)
        /// Get object from a datablock with a fixed address, 
        /// having that the pointer to the object (16 byte) is saved from the startIndex inside of a row's value.  
        /// Returns null if object is not found.
        /// </summary>
        /// <typeparam name="TVal"></typeparam>
        /// <returns></returns>
        public DBreeze.Objects.DBreezeObject<TVal> ObjectGet<TVal>()
        {
            var ret = new Objects.DBreezeObject<TVal>();

            byte[] dataBlockId = null;
            int startIndex = 0;

            if (_exists)
            {
                if (_row.ValueIsReadOut)
                {
                    if (_row.Value == null)
                        return null;

                    dataBlockId = _row.Value.Substring(startIndex, 16);
                }
                else
                {
                    long valueStartPointer = 0;
                    uint valueFullLength = 0;
                    dataBlockId = this._row.Root.Tree.Cache.ReadValuePartially(this._row.LinkToValue, (uint)startIndex, 16, this._useCache, out valueStartPointer, out valueFullLength);
                }
            }
            else
                return null;

            if (dataBlockId == null)
                return null;

            ret.ptrToExisingEntity = dataBlockId;
            dataBlockId = this._row.Root.Tree.Cache.ReadDynamicDataBlock(ref dataBlockId, this._useCache);
            Dictionary<uint, byte[]> d = new Dictionary<uint, byte[]>();
            ret.ExisingEntity = this._row.Root.Tree.Cache.ReadDynamicDataBlock(ref dataBlockId, this._useCache);
            Biser.Decode_DICT_PROTO_UINT_BYTEARRAY(ret.ExisingEntity, d);
            if (d == null || d.Count < 1)
                return null;
            ret.Entity = DataTypesConvertor.ConvertBack<TVal>(d[0]);
            return ret;
        }

        ///// <summary>
        ///// Tries to update value stored by InsertDataBlockWithFixedAddress,
        ///// having that reference to it is a part of value
        ///// </summary>
        ///// <typeparam name="TVal"></typeparam>
        ///// <param name="newValue"></param>
        ///// <param name="startIndex">reference to InsertDataBlockWithFixedAddress</param>
        //public void UpdateDataBlockWithFixedAddress<TVal>(TVal newValue, uint startIndex = 0)
        //{          

        //    byte[] dataBlockId = null;

        //    if (_exists)
        //    {
        //        if (_row.ValueIsReadOut)
        //        {
        //            if (_row.Value == null)
        //                return;

        //            dataBlockId = _row.Value.Substring((int)startIndex, 16);
        //        }

        //        long valueStartPointer = 0;
        //        uint valueFullLength = 0;
        //        dataBlockId = this._row.Root.Tree.Cache.ReadValuePartially(this._row.LinkToValue, startIndex, 16, this._useCache, out valueStartPointer, out valueFullLength);
        //    }

        //    if (dataBlockId == null)
        //        return;

        //    byte[] data = DataTypesConvertor.ConvertValue(newValue);
        //    //dataBlockId = this._row.Root.Tree.Cache.ReadDynamicDataBlock(ref dataBlockId, this._useCache);
        //    dataBlockId = this._row.Root.Tree.Cache.ReadDynamicDataBlock(ref dataBlockId, false);
        //    this._row.Root.Tree.Cache.WriteDynamicDataBlock(ref dataBlockId, ref data);            
        //}


        /// <summary>
        /// Returns full value and converts it to the value data type.
        /// <para>To take full value or part of the value as byte[] use GetValuePart or GetBytes (for string types like DbAscii etc.)</para>
        /// <para>If your value contains serialized object inside or it's a string type (like DbAscii etc.), use Value.Get property.</para>
        /// </summary>
        /// <returns></returns>
        public TValue Value
        {
            get
            {
                if (_exists)
                {
                    if (_row.ValueIsReadOut)
                    {
                        return DataTypesConvertor.ConvertBack<TValue>(_row.Value); ;
                    }

                    long valueStartPointer = 0;
                    uint valueFullLength = 0;
                    //Console.WriteLine("UseCache " + this._useCache);
                    byte[] res = this._row.Root.Tree.Cache.ReadValue(this._row.LinkToValue, this._useCache, out valueStartPointer, out valueFullLength);
                    //Console.WriteLine("Res " + res.ToBytesString(""));

                    //Remembering once read out result
                    _row.ValueIsReadOut = true;
                    _row.Value = res;                   

                    return DataTypesConvertor.ConvertBack<TValue>(res);
                }

                return default(TValue);
            }
        }






        public void PrintOut()
        {
            PrintOut(String.Empty);
        }
        /// <summary>
        /// Experimantal Console PrintOut
        /// </summary>
        public void PrintOut(string leadingText)
        {
            if (!_exists)
            {
                System.Diagnostics.Debug.WriteLine("Key doesn't exist");
                //Console.WriteLine("Key doesn't exist");
            }
            else
            {


                if (typeof(TKey) == DBreeze.DataTypes.DataTypesConvertor.TYPE_BYTE_ARRAY)
                {
                    System.Diagnostics.Debug.WriteLine(String.Format("{2}; K: {0}; V: {1}", this._key, Value, leadingText));
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(String.Format("{2}; K: {0}; V: {1}", this._key, Value, leadingText));
                }
            }
        }

    }
}
