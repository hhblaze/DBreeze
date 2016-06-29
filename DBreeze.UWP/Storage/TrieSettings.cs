/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBreeze.Storage
{
    public class TrieSettings
    {
        //Combines user settings for the Trie and internal transport data.


        /// <summary>
        /// User parameter for the table.
        /// <para>Quantity of bytes which represent pointer inside of raw file (5 bytes = 1 Terrabyte, so the trie with 5 bytes pointer len can't be longer then 1 terrabyte).</para>
        /// <para>If you plan to have your table more then 1TB, set this value to bigger value, before table creation</para>
        /// </summary>
        public ushort POINTER_LENGTH = 5;

        /// <summary>
        /// For internal needs
        /// Quantity of bytes which reside ROOT_NODE
        /// </summary>
        internal ushort ROOT_SIZE = 64;

        /// <summary>
        /// For internal needs.
        /// Offset where root should reside in the file
        /// </summary>
        internal long ROOT_START = 0;

        ///// <summary>
        ///// For internal needs.
        ///// Default is null, then LTrie, creates its own RollerBack.
        ///// If not null, then DbInTable supplied it and Ltrie.Cache will skip creating the new one but will use for save and restore data supplied one.
        ///// </summary>
        //internal IRollBackFile RollerBack = null;
        internal bool IsNestedTable = false;

        /// <summary>
        /// If table is for internal purposes (like Scheme or Transaction Journal)
        /// </summary>
        internal bool InternalTable = false;

        ///// <summary>
        ///// For now internal property, but later can become public.
        ///// Skips usage of StorageBuffer inside of StorageLayer
        ///// </summary>
        //internal bool SkipStorageBuffer = false;

        /// <summary>
        /// Next 3 concern alternative table storage pathes.
        /// </summary>
        internal string AlternativeTableStorageFolder = String.Empty;
        internal DBreeze.DBreezeConfiguration.eStorage AlternativeTableStorageType = DBreezeConfiguration.eStorage.DISK;
        internal bool StorageWasOverriden = false;

        ///// <summary>
        ///// Concerning disk flush behaviour
        ///// </summary>
        //internal DBreeze.DBreezeConfiguration.eDiskFlush DiskFlushBehaviour = DBreezeConfiguration.eDiskFlush.INDUSTRIAL;
    }
    
}
