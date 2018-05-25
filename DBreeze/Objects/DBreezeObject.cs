/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBreeze.Objects
{
    /// <summary>
    /// Concept of the objects storage (read docu from 20170321)
    /// </summary>
    /// <typeparam name="TEntityType"></typeparam>
    public class DBreezeObject<TEntityType>
    {
        /// <summary>
        /// 
        /// </summary>
        public DBreezeObject()
        {
            //Indexes = new List<DBreezeIndex>();
            NewEntity = false;
            IncludeOldEntityIntoResult = false;
        }

        /// <summary>
        /// List of indexes
        /// </summary>
        public IList<DBreezeIndex> Indexes { get; set; }

        /// <summary>
        /// Deafult false. Skips reading existing value on the disk (insert time economy)
        /// </summary>
        public bool NewEntity { get; set; }

        /// <summary>
        /// Entity itself
        /// </summary>
        public TEntityType Entity { get; set; }

        /// <summary>
        /// If existing entity was taken before it can be supplied to speed up insert process
        /// by skipping reading value from new.
        /// Existing object must be taken from row
        /// </summary>
        internal byte[] ExisingEntity { get; set; }

        internal byte[] ptrToExisingEntity { get; set; }

        /// <summary>
        /// Default false. If true updated value will be included into DBreezeObjectInsertResult.OldEntity
        /// </summary>
        public bool IncludeOldEntityIntoResult { get; set; }

    }

    /// <summary>
    /// Concept of the objects storage (read docu from 20170321)
    /// Answer after transaction.ObjectInsert
    /// </summary>
    /// <typeparam name="TEntityType"></typeparam>
    public class DBreezeObjectInsertResult<TEntityType>
    {
        /// <summary>
        /// 
        /// </summary>
        public DBreezeObjectInsertResult()
        {          
           // EntityWasInserted = false;
            OldEntityWasFound = false;
            EntityWasInserted = true;
        }

        /// <summary>
        /// Indicates that old entity exists
        /// </summary>
        public bool OldEntityWasFound { get; set; }

        /// <summary>
        /// Entity which was found in database before update.
        /// Will be included if DBreezeObject.IncludeOldEntityIntoResult = true;
        /// </summary>
        public TEntityType OldEntity { get; set; }

        /// <summary>
        /// In case if entity was inserted is set to true
        /// </summary>
        public bool EntityWasInserted { get; set; }

        /// <summary>
        /// Pointer to DBreezeObject, ObjectGetByFixedAddress should help to retrieve it.
        /// </summary>
        public byte[] PtrToObject { get; set; }
    }
}
