/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who thinks that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DBreeze.Storage;
using DBreeze.Storage.RemoteInstance;

namespace DBreeze
{
    public class DBreezeConfiguration : IDisposable
    {
        Backup _backup = new Backup();

        public DBreezeConfiguration()
        {
            DBreezeDataFolderName = String.Empty;            
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
            MEMORY,
            /// <summary>
            /// In case if database files are located on a remote host
            /// </summary>
            RemoteInstance
        }

        /// <summary>
        /// DISK, MEMORY or Remote Instance. DEFAULT IS DISK,
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

        /// <summary>
        /// In case if we want to use storage layer RemoteInstance (RISR), this must be supplied.
        /// Answers for sending data to Remote Acceptor and returning answer back
        /// </summary>
        public IRemoteInstanceCommunicator RICommunicator = null;
      
    }
}
