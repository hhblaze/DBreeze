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

namespace DBreeze.LianaTrie
{   
    public class LTrieRow
    {
         LTrieRootNode _root = null;

        public LTrieRootNode Root
        {
            get
            {
                //Checking if current instance of an object still represents the same table, cause table could be changed after table.RestoreFile or Table.Recreation    
                //Master, Nested tables are covered, DataBlocks are not covered. To get DataBlock is enough only pointer. But may be there is not so much sense
                //to operate with pure pointers, if table can be compacted or recreated, at least at some places.
                if (TableFixTime != _root.Tree.Storage.StorageFixTime)
                    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TABLE_WAS_CHANGED_LINKS_ARE_NOT_ACTUAL, new Exception());

                return _root;
            }
        }

        /// <summary>
        /// Represents pointer to the value in physical file, remember for READ must come via sync-cache
        /// -1 if not defined
        /// </summary>
        public long ValueStartPointer = -1;
        /// <summary>
        /// If ValueStartPointer == -1, has no meaning
        /// </summary>
        public uint ValueFullLength = 0;

        DateTime TableFixTime = DateTime.UtcNow;

        public LTrieRow(LTrieRootNode root)
        {
            _root = root;

            this._linkToValue = root.EmptyPointer;

            TableFixTime = root.Tree.Storage.StorageFixTime;
        }
        /// <summary>
        /// Default is null
        /// </summary>
        //public byte[] Key { get; set; }
        public byte[] Key = null;

        public bool ValueIsReadOut = false;
        public byte[] Value = null;

        ///// <summary>
        ///// Default is null
        ///// </summary>
        //public byte[] Value { get; set; }

        private byte[] _linkToValue = null;
        /// <summary>
        /// Experimental, instead of Value, we supply link to the value
        /// </summary>
        public byte[] LinkToValue
        {
            get
            {
                if (TableFixTime != _root.Tree.Storage.StorageFixTime)
                    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TABLE_WAS_CHANGED_LINKS_ARE_NOT_ACTUAL, new Exception());

                return _linkToValue;
            }
            set
            {
                this._linkToValue = value;

                this._exists = !value._IfPointerIsEmpty((ushort)this._linkToValue.Length);

                //if (this._root._IfPointerIsEmpty(value))
                //    this._exists = false;
                //else
                //    this._exists = true;
            }
        }

        private bool _exists = false;
        /// <summary>
        /// Default is false
        /// </summary>
        public bool Exists
        {
            get
            {
                //Checking if table which we read now is the same which has generated this LTrieRow.
                //If not then in-between table recreate was executed or restore from other tables
                if (TableFixTime != _root.Tree.Storage.StorageFixTime)
                    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TABLE_WAS_CHANGED_LINKS_ARE_NOT_ACTUAL, new Exception());

                return this._exists;
            }

        }

        /// <summary>
        /// Returns either value partially or null
        /// </summary>
        /// <param name="startIndex"></param>
        /// <param name="length"></param>
        /// <param name="useCache">if true, then only committed data will be shown</param>
        /// <returns></returns>
        public byte[] GetPartialValue(uint startIndex, uint length, bool useCache)
        {
            if (ValueIsReadOut)
            {
                if (this.Value == null)
                    return null;
                return this.Value.Substring((int)startIndex, (int)length);
            }

            if (Exists)
                return this._root.Tree.Cache.ReadValuePartially(this.LinkToValue, startIndex, length, useCache, out ValueStartPointer, out ValueFullLength);

            return null;
        }


        /// <summary>
        /// Returns either value as byte array or null if value doesn't exist
        /// </summary>
        /// <param name="useCache">if true, then only committed data will be shown</param>
        /// <returns></returns>
        public byte[] GetFullValue(bool useCache)
        {
            if (ValueIsReadOut)
            {
                return this.Value;
            }

            if (Exists)
                return this._root.Tree.Cache.ReadValue(this.LinkToValue, useCache, out ValueStartPointer, out ValueFullLength);
            
            return null;
        }

        ///// <summary>
        ///// For internal usage. insert using the same root.
        ///// Returns link to the full-value (incl key+prot)
        ///// </summary>
        ///// <param name="key"></param>
        ///// <param name="value"></param>
        ///// <param name="startIndex"></param>
        ///// <returns></returns>
        //internal byte[] InsertPart(byte[] key, byte[] value, uint startIndex)
        //{
        //    return this._root.AddKeyPartially(ref key, ref value, startIndex);
        //}
    }
}
