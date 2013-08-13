/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who thinks that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DBreeze.Storage;

namespace DBreeze
{
    public class DBreezeConfiguration : IDisposable
    {
        Backup _backup = new Backup();

        public DBreezeConfiguration()
        {
            DBreezeDataFolderName = String.Empty;
            //DiskFlushBehaviour = eDiskFlush.INDUSTRIAL;
            Storage = eStorage.DISK;
        }

        public void Dispose()
        {
            if (Backup != null)
                Backup.Dispose();
        }

        /// <summary>
        /// Incremental backup plan. If is not instantiated, incremental backup will be switch off
        /// </summary>
        public Backup Backup
        {
            get
            {
                return this._backup;
            }
            set
            {
                if (value == null)
                    this._backup = new Backup();
                else
                    this._backup = value;
            }
        }

        /// <summary>
        /// Folder where will reside DBreeze database files
        /// </summary>
        public string DBreezeDataFolderName { get; set; }

        public enum eStorage
        {
            DISK,
            MEMORY
        }

        /// <summary>
        /// DISK OR MEMORY, DEFAULT IS DISK,
        /// DBreezeDataFolderName must be supplied.
        /// </summary>
        public eStorage Storage { get; set; }


        /// <summary>
        /// Pattern based way to specify storage and location for tables.
        /// <para>Key of this dictionary must contain table pattern e.g. Article$/Items# or Car456 or Items*</para>
        /// <para>Value, if is String.Empty, means that table will be located in memory.</para>
        /// <para>Value, if not empty, means physical storage folder path, where table should reside.</para>
        /// <para>If table doesn't intersect any pattern then default DB configuration will be overriden for the storage.</para>
        /// <para>If table intersects more the one pattern then first found will be applied.</para>
        /// <para>Help for patterns:</para>
        /// <para>$ * #</para>
        /// <para>"U" symbol in the following examples means intersection</para>
        /// <para>* - 1 or more of any symbol kind (every symbol after * will be cutted): Items* U Items123/Pictures</para>
        /// <para># - symbols (except slash) followed by slash and minimum another symbol: Items#/Picture U Items123/Picture</para>
        /// <para>$ - 1 or more symbols except slash (every symbol after $ will be cutted): Items$ U Items123;  Items$ !U Items123/Pictures </para>
        /// </summary>
        public Dictionary<string, string> AlternativeTablesLocations = new Dictionary<string, string>();


        //public enum eDiskFlush
        //{
        //    /// <summary>
        //    /// <para>Default setting for all tables.</para>
        //    /// <para>All writes are directed to the disk, without intermediate OS file cache.</para>
        //    /// <para>Used for productional servers, where data can't be lost in case of power outtage or OS crash.</para>
        //    /// </summary>
        //    INDUSTRIAL,
        //    /// <summary>
        //    /// <para>Speed grows up. OS file sysetm cache is used. </para>
        //    /// <para>Good for temporary tables. </para>
        //    /// <para>Data can be lost and tables can be corrupted in case of power outtage or OS crash.</para>
        //    /// </summary>
        //    LIGHT
        //}

        ///// <summary>
        ///// Depending upon purposes of DBreeze usage this property can vary.
        ///// <para>Default value is INDUSTRIAL usage (Fully ACID, safe from power loss or OS crash).</para>
        ///// <para>Each table, by pattern technique, can be separately setup via.....</para>
        ///// </summary>
        //public eDiskFlush DiskFlushBehaviour { get; set; }

        ///// <summary>
        ///// Pattern based way to specify eDiskFlush behaviour for tables.
        ///// <para>Key of this dictionary must contain table pattern e.g. Article$/Items# or Car456 or Items*</para>
        ///// <para>Value, if is String.Empty, means that table will be located in memory.</para>
        ///// <para>Value - eDiskFlush</para>
        ///// <para>If table doesn't intersect any pattern then default DB DiskFlushBehaviour configuration will be taken.</para>
        ///// <para>If table intersects more the one pattern then first found will be applied.</para>
        ///// <para>Help for patterns:</para>
        ///// <para>$ * #</para>
        ///// <para>"U" symbol in the following examples means intersection</para>
        ///// <para>* - 1 or more of any symbol kind (every symbol after * will be cutted): Items* U Items123/Pictures</para>
        ///// <para># - symbols (except slash) followed by slash and minimum another symbol: Items#/Picture U Items123/Picture</para>
        ///// <para>$ - 1 or more symbols except slash (every symbol after $ will be cutted): Items$ U Items123;  Items$ !U Items123/Pictures </para>
        ///// </summary>
        //public Dictionary<string, eDiskFlush> AlternativeDiskFlushBehaviour = new Dictionary<string, eDiskFlush>();

        ///// <summary>
        ///// Configuration synchronizer, for changing Dictionaries on the fly
        ///// </summary>
        //public object SyncRoot = new object();


    }
}
