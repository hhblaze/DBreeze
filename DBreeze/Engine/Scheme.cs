/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.IO;

using DBreeze.Storage;
using DBreeze.LianaTrie;
using DBreeze.Utils;

using DBreeze.Exceptions;

using DBreeze.SchemeInternal;

namespace DBreeze
{
    public class Scheme : IDisposable
    {
        internal DBreezeEngine Engine = null;

        CachedTableNames cachedTableNames = new CachedTableNames();
        
        /// <summary>
        /// Flag that closes file of the table if threads don't use it for reading or writing.
        /// </summary>
        internal bool AutoCloseOpenTables = true;

        static string Copyright = "DBreeze.tiesky.com";

        static string SchemaFileName = "_DBreezeSchema";

        //For System Tables or Records we reserve "@@@@" sequence
        static string LastFileNumberKeyName = "@@@@LastFileNumber";

        TrieSettings LTrieSettings = null;
        IStorage Storage = null;
        LTrie LTrie = null;

        //User files counter
        ulong LastFileNumber = 10000000;

        DbReaderWriterLock _sync_openTablesHolder = new DbReaderWriterLock();
        Dictionary<string, OpenTable> _openTablesHolder = new Dictionary<string, OpenTable>();

        bool _disposed = false;

        public Scheme(DBreezeEngine DBreezeEngine)
        {
            Engine = DBreezeEngine;

            this.OpenSchema();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _sync_openTablesHolder.EnterWriteLock();
            try
            {
                foreach (var row in _openTablesHolder)
                {
                    //Disposes all Ltrie, with storages and rollbacks
                    row.Value.Dispose();
                }

                //Clear self
                _openTablesHolder.Clear();

                //Disposing Schema trie
                if (LTrie != null)
                {
                    LTrie.Dispose();
                }
                //LTrieStorage.Dispose();
            }
            finally
            {
                _sync_openTablesHolder.ExitWriteLock();
            }

        }

        /*          TODO
         
         *  1. HERE we will add TableNames as RegEx with settings
         *  2. Checking Reserverd TableNames prefixes
         *  3. User TableName must start from @ut 
         *  4. GetPhysicalPathToTheUserTable - File with DIrectory Settings for different tables parser (to make reside different tables in different HDDs or even network drives)
         */

        private void OpenSchema()
        {
            LTrieSettings = new TrieSettings()
            {
                InternalTable = true,
                //SkipStorageBuffer = true
            };

            Storage = new StorageLayer(Path.Combine(Engine.MainFolder, SchemaFileName), LTrieSettings, Engine.Configuration);

            LTrie = new LTrie(Storage);

            LTrie.TableName = "DBreeze.Scheme";

            //Reading lastFileNumber
            ReadUserLastFileNumber();
        }


        private void ReadUserLastFileNumber()
        {
            byte[] btKeyName = Encoding.UTF8.GetBytes(LastFileNumberKeyName);
            LTrieRow row = LTrie.GetKey(btKeyName, false, false);

            if (row.Exists)
            {
                byte[] fullValue = row.GetFullValue(true);
                LastFileNumber = fullValue.To_UInt64_BigEndian();
            }
        }

        /// <summary>
        /// ONLY FOR INTERNAL NEEDS, lock must be handeled by outer procedure.
        /// Users must use GetTablePathFromTableName.
        /// Transactions Journal after start will try to delete RollbackFiles of the finished transactions.
        /// For this it needs to know exact pathes.
        /// For now all tables stored in one folder. Later we will have extra config file which lets to reside 
        /// some of tables in the other folders. 
        /// This function is an access globalizer to physical file locations by userTableName.
        /// !!!!TRAnJRNL, WHEN RESTORES ROLLBACK, MUST REFER TO Scheme trie settings in the future, FOR NOW DEFAULT
        /// </summary>
        /// <param name="userTableName"></param>
        /// <returns></returns>
        internal string GetPhysicalPathToTheUserTable(string userTableName)
        {
            try
            {
                byte[] btTableName = GetUserTableNameAsByte(userTableName);
                ulong fileName = 0;


                //Getting file name
                LTrieRow row = LTrie.GetKey(btTableName, false, false);

                if (row.Exists)
                {
                    byte[] fullValue = row.GetFullValue(true);
                    //Can be parsed different. First protocol version is 1
                    ushort schemeProtocol = fullValue.Substring(0, 2).To_UInt16_BigEndian();

                    switch (schemeProtocol)
                    {
                        case 1:
                            fileName = fullValue.Substring(2, 8).To_UInt64_BigEndian();
                            break;
                        default:
                            throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.SCHEME_FILE_PROTOCOL_IS_UNKNOWN);
                    }
                }
                else
                    return String.Empty;


                //Getting folder

                //For now returns path inside working folder, later re-make, take into consideration mapping of DB to tother folders.

                string alternativeTableLocation = String.Empty;

                if (CheckAlternativeTableLocationsIntersections(userTableName, out alternativeTableLocation))
                {
                    if (alternativeTableLocation == String.Empty)
                    {
                        //In memory table
                        //return Path.Combine(Engine.MainFolder, fileName.ToString());
                        return "MEMORY";
                    }
                    else
                    {
                        //returning alternative folder + fileName
                        return Path.Combine(alternativeTableLocation, fileName.ToString());
                    }
                }
                else
                {
                    //Standard path (Dbreeze mainFolder + fileName)
                    return Path.Combine(Engine.MainFolder, fileName.ToString());
                }
            }
            //catch (System.Threading.ThreadAbortException ex)
            //{
            //    //We don'T make DBisOperable = false;                
            //    throw ex;
            //}
            catch (Exception ex)
            {
                this.Engine.DBisOperable = false;
                this.Engine.DBisOperableReason = "GetPhysicalPathToTheUserTable";
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.GENERAL_EXCEPTION_DB_NOT_OPERABLE, this.Engine.DBisOperableReason, ex);

            }
        }


        /// <summary>
        /// Returns physical path to the table file, if table doesn't exists in the Scheme returns String.Empty
        /// </summary>
        /// <param name="userTableName"></param>
        /// <returns></returns>
        public string GetTablePathFromTableName(string userTableName)
        {
            //For user usage

            _sync_openTablesHolder.EnterReadLock();
            try
            {
                byte[] btTableName = GetUserTableNameAsByte(userTableName);

                LTrieRow row = LTrie.GetKey(btTableName, true, false);

                if (!row.Exists)
                {
                    return String.Empty;
                }

                byte[] fullValue = row.GetFullValue(true);
                //Can be parsed different. First protocol version is 1
                ushort schemeProtocol = fullValue.Substring(0, 2).To_UInt16_BigEndian();
                ulong fileName = 0;
                switch (schemeProtocol)
                {
                    case 1:
                        fileName = fullValue.Substring(2, 8).To_UInt64_BigEndian();
                        break;
                    default:
                        throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.SCHEME_FILE_PROTOCOL_IS_UNKNOWN);
                }

                string alternativeTableLocation = String.Empty;

                if (CheckAlternativeTableLocationsIntersections(userTableName, out alternativeTableLocation))
                {
                    if (alternativeTableLocation == String.Empty)
                        return "MEMORY";
                    else
                        return Path.Combine(alternativeTableLocation, fileName.ToString());
                }
                else
                {
                    return Path.Combine(Engine.MainFolder, fileName.ToString());
                }
            }
            finally
            {
                _sync_openTablesHolder.ExitReadLock();
            }

        }



        /// <summary>
        /// Adds static prefix to all user table names, to
        /// make selection of tables for different purposes easier with StartsWith function
        /// </summary>
        /// <param name="tableName"></param>
        /// <returns></returns>
        private byte[] GetUserTableNameAsByte(string tableName)
        {
            return Encoding.UTF8.GetBytes("@ut" + tableName);
        }

        private string GetUserTableNameAsString(string tableName)
        {
            return "@ut" + tableName;
        }

        /// <summary>
        /// Returns table for READ, WRITE FUNC
        /// </summary>
        /// <param name="userTableName"></param>
        /// <returns></returns>
        internal LTrie GetTable(string userTableName)
        {
            string tableName = GetUserTableNameAsString(userTableName);

            //TODO pattern based mapping If table doesn't exist we create it with properties which could be supplied after db init as regex theme.



            //Schema protocol: 2 bytes - protocol version, other data
            //For protocol 1: first 8 bytes will be TheFileName, starting from db10000-dbN (0-N ulong). up to 10000 are reserved for dbreeze.

            //Table names are UTF-8 based, no limits

            ulong fileName = 0;
            OpenTable otl = null;

            _sync_openTablesHolder.EnterUpgradeableReadLock();
            try
            {

                _openTablesHolder.TryGetValue(tableName, out otl);

                if (otl != null)
                {
                    //Try to increase usage and return LTrie
                    otl.Add();
                    return otl.Trie;
                }


                //Probably table Exists in db but not in openTablesHolder

                _sync_openTablesHolder.EnterWriteLock();
                try
                {
                    //UpgradeableRead recheck
                    _openTablesHolder.TryGetValue(tableName, out otl);

                    if (otl != null)
                    {
                        //Try to increase usage and return LTrie
                        otl.Add();
                        return otl.Trie;
                    }



                    byte[] btTableName = GetUserTableNameAsByte(userTableName);

                    //Trying to get fileName from cache
                    fileName = this.cachedTableNames.GetFileName(tableName);
                    // LTrieRow row = null;
                    bool tableExists = false;

                    if (fileName == 0)
                    {
                        LTrieRow row = LTrie.GetKey(btTableName, false, false);


                        if (row.Exists)
                        {
                            tableExists = true;

                            byte[] fullValue = row.GetFullValue(false);
                            //Can be parsed different. First protocol version is 1
                            ushort schemeProtocol = fullValue.Substring(0, 2).To_UInt16_BigEndian();

                            switch (schemeProtocol)
                            {
                                case 1:
                                    fileName = fullValue.Substring(2, 8).To_UInt64_BigEndian();
                                    break;
                                default:
                                    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.SCHEME_FILE_PROTOCOL_IS_UNKNOWN);
                            }
                        }
                        else
                        {
                            tableExists = false;
                            //Creating new table.

                            //Checking table name validity

                            //this will throw exception, if not valid
                            DbUserTables.UserTableNameIsOk(userTableName);


                            //Creating such table and renewing LastFileNumber counter

                            //Adding to LastFileNumber
                            LastFileNumber++;


                            ////Deleting physical files related to the table, if they existed - normally they should not
                            //DeleteAllReleatedTableFiles(Path.Combine(Engine.MainFolder, LastFileNumber.ToString()));

                            byte[] lft = LastFileNumber.To_8_bytes_array_BigEndian();

                            //Writing this number to Schema file
                            LTrie.Add(Encoding.UTF8.GetBytes(LastFileNumberKeyName), lft);

                            //Creating table self and writing to Schema file

                            LTrie.Add(btTableName,
                                new byte[] { 0, 1 }     //Protocol version 1
                                .Concat(lft));          //Number of the file

                            //Committing both records
                            LTrie.Commit();

                            fileName = LastFileNumber;

                            this.cachedTableNames.Add(tableName, fileName);
                        }
                    }
                    else
                        tableExists = true;

                    //Creating LTrie, adding it to _openTablesHolder

                    //Seeting up Trie TableName, OTHER SETTINGS

                    TrieSettings ts = new TrieSettings();
                    IStorage storage = null;


                    ////Checking if default Flusg Disk behaviour was overriden
                    //ts.DiskFlushBehaviour = Engine.Configuration.DiskFlushBehaviour;
                    ////Checking if we have alternative DiskFlush behaviour
                    //foreach (var pattern in Engine.Configuration.AlternativeDiskFlushBehaviour)
                    //{
                    //    //pattern.Key
                    //    if (DbUserTables.PatternsIntersect(pattern.Key, userTableName))
                    //    {

                    //        ts.DiskFlushBehaviour = pattern.Value;
                    //        break;
                    //    }
                    //}

                    string alternativeTableLocation = String.Empty;

                    if (CheckAlternativeTableLocationsIntersections(userTableName, out alternativeTableLocation))
                    {
                        ts.StorageWasOverriden = true;

                        if (alternativeTableLocation == String.Empty)
                        {
                            ts.AlternativeTableStorageType = DBreezeConfiguration.eStorage.MEMORY;

                            storage = new StorageLayer(Path.Combine(Engine.MainFolder, fileName.ToString()), ts, Engine.Configuration);
                        }
                        else
                        {
                            ts.AlternativeTableStorageType = DBreezeConfiguration.eStorage.DISK;
                            ts.AlternativeTableStorageFolder = alternativeTableLocation;

                            DirectoryInfo diAlt = new DirectoryInfo(alternativeTableLocation);
                            if (!diAlt.Exists)
                                diAlt.Create();

                            if (!tableExists)
                            {
                                //Deleting physical files related to the table, if they existed - normally they should not
                                DeleteAllReleatedTableFiles(Path.Combine(ts.AlternativeTableStorageFolder, LastFileNumber.ToString()));
                            }

                            storage = new StorageLayer(Path.Combine(ts.AlternativeTableStorageFolder, fileName.ToString()), ts, Engine.Configuration);
                        }
                    }
                    else
                    {
                        if (!tableExists)
                        {
                            //Deleting physical files related to the table, if they existed - normally they should not
                            DeleteAllReleatedTableFiles(Path.Combine(Engine.MainFolder, LastFileNumber.ToString()));
                        }

                        storage = new StorageLayer(Path.Combine(Engine.MainFolder, fileName.ToString()), ts, Engine.Configuration);
                    }

                    //storage = new StorageLayer(Path.Combine(Engine.MainFolder, fileName.ToString()), ts, Engine.Configuration);

                    LTrie trie = new LTrie(storage);

                    //Setting LTrie user table name
                    trie.TableName = userTableName;

                    //_openTablesHolder.Add(tableName, trie);

                    //Automatically increased usage in OpenTable constructor
                    _openTablesHolder.Add(tableName, new OpenTable(trie));

                    return trie;
                }
                catch (System.Exception ex)
                {
                    //CASCADE
                    throw ex;
                }
                finally
                {
                    _sync_openTablesHolder.ExitWriteLock();
                }
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.SCHEME_GET_TABLE_WRITE_FAILED, tableName, ex);
            }
            finally
            {
                _sync_openTablesHolder.ExitUpgradeableReadLock();
            }
        }

        /// <summary>
        /// Checks if in configuration was supplied alternative path for table location.
        /// Returns true if intersection was found.
        /// Alternative Path equals to String.Empty - locate in Memory
        /// </summary>
        /// <param name="userTableName"></param>
        /// <param name="alternativePath"></param>
        /// <returns></returns>
        internal bool CheckAlternativeTableLocationsIntersections(string userTableName, out string alternativePath)
        {
            alternativePath = String.Empty;

            foreach (var pattern in Engine.Configuration.AlternativeTablesLocations)
            {
                //pattern.Key
                if (DbUserTables.PatternsIntersect(pattern.Key, userTableName))
                {
                    alternativePath = pattern.Value;
                    return true;
                }
            }

            return false;
        }


        /// <summary>
        /// Called by Transaction, when it's time to be Disposed and close tables.
        /// Tables will be closed only in case of other threads don't use it.
        /// </summary>
        /// <param name="closeOpenTables"></param>
        internal void CloseTables(Dictionary<string, ulong?> closeOpenTables)
        {
            //if (Engine.Configuration.Storage == DBreezeConfiguration.eStorage.MEMORY)
            //    return;

            string tableName = String.Empty;
            OpenTable ot = null;
            bool toClose = false;

            string alternativeTableLocation = String.Empty;

            _sync_openTablesHolder.EnterWriteLock();
            try
            {
                //utn - user table name
                foreach (var utn in closeOpenTables)
                {

                    if (CheckAlternativeTableLocationsIntersections(utn.Key, out alternativeTableLocation))
                    {
                        if (alternativeTableLocation == String.Empty)
                        {
                            //Memory table, we don't close
                            continue;
                        }
                        else
                        {
                            //Physical table...going on
                        }
                    }
                    else
                    {
                        //Table location is not overridden, working further based on main DBreeze configuration
                        if (Engine.Configuration.Storage == DBreezeConfiguration.eStorage.MEMORY)
                            continue;   //we don't close memory tables
                    }

                    tableName = GetUserTableNameAsString(utn.Key);

                    _openTablesHolder.TryGetValue(tableName, out ot);

                    if (ot != null)
                    {
                        toClose = ot.Remove((ulong)utn.Value);

                        if (AutoCloseOpenTables)    //If AutoCloseIsEnabled, we dispose LTrie and closing physical file.
                        {
                            if (toClose)
                            {
                                //Closing table

                                //Console.WriteLine("Closing: " + utn.Key);

                                ot.Dispose();

                                _openTablesHolder.Remove(tableName);
                            }
                        }
                    }
                    //else
                    //{
                    //}
                }


            }
            finally
            {
                _sync_openTablesHolder.ExitWriteLock();
            }
        }


        /// <summary>
        /// Used by GetTableFor Write, if table is newly created and we have such file name
        /// </summary>
        /// <param name="fullTableFilePath"></param>
        private void DeleteAllReleatedTableFiles(string fullTableFilePath)
        {
            //This call can be done only for physical files, it's controlled on the upper level

            //if (this.Engine.Configuration.Storage == DBreezeConfiguration.eStorage.MEMORY)
            //    return;

            try
            {
                //Deleting DB File
                if (File.Exists(fullTableFilePath))
                    File.Delete(fullTableFilePath);

                //Deleting Rollback File
                if (File.Exists(fullTableFilePath + ".rol"))
                    File.Delete(fullTableFilePath + ".rol");

                //Deleting Rollback Help File
                if (File.Exists(fullTableFilePath + ".rhp"))
                    File.Delete(fullTableFilePath + ".rhp");

                /* Handling backup*/
                if (this.Engine.Configuration.Backup.IsActive)
                {
                    string exactFileName = Path.GetFileName(fullTableFilePath);
                    ulong ulFileName = this.Engine.Configuration.Backup.BackupFNP.ParseFilename(exactFileName);
                    long backup_filePosition = 0;
                    byte[] data = null;
                    this.Engine.Configuration.Backup.WriteBackupElement(ulFileName, 5, backup_filePosition, data);
                }
                /*****************/

            }
            catch (Exception ex)
            {
                //CASCADE
                throw ex;
            }
        }

        /// <summary>
        /// Returns if user table exists
        /// </summary>
        /// <param name="userTableName"></param>
        /// <returns></returns>
        public bool IfUserTableExists(string userTableName)
        {
            string tableName = GetUserTableNameAsString(userTableName);

            _sync_openTablesHolder.EnterWriteLock();
            try
            {
                if (_openTablesHolder.ContainsKey(tableName))
                    return true;

                ////Searching on the disk
                byte[] btTableName = this.GetUserTableNameAsByte(userTableName);
                var row = LTrie.GetKey(btTableName, false, true);
                return row.Exists;
            }
            catch (System.Exception ex)
            {
                throw ex;
            }
            finally
            {
                _sync_openTablesHolder.ExitWriteLock();
            }

            ////First trying to acquire memory storage, without lock but inside of "ignoring" try-catch, if answer is failed refer to the disk

            //_sync_openTablesHolder.EnterReadLock();
            //try
            //{
            //    if (_openTablesHolder.ContainsKey(tableName))
            //        return true;

            //}
            //finally
            //{
            //    _sync_openTablesHolder.ExitReadLock();
            //}



            //////Searching on the disk
            //byte[] btTableName = this.GetUserTableNameAsByte(userTableName);
            //var row = LTrie.GetKey(btTableName, true);
            //return row.Exists;
        }

        /// <summary>
        /// Returns List of user tables starting from specified mask.
        /// If mask is String.Empty returns all user tables
        /// </summary>
        /// <param name="mask"></param>
        /// <returns></returns>
        public List<string> GetUserTableNamesStartingWith(string mask)
        {
            List<string> ret = new List<string>();

            //No lock here, while IterateForwardStartsWith of the LTrie is safe (new root is created), and we don't acquire value from the key (which could be delete).
            //_sync_openTablesHolder.EnterReadLock();
            //try
            //{
            byte[] btKeyName = Encoding.UTF8.GetBytes("@ut" + mask);

            foreach (var row in LTrie.IterateForwardStartsWith(btKeyName, true, false))
            {
                //try       //try-catch could be necessary in case if we acquire value, which was deleted by other thread. Here we don't acquire value.
                //{
                ret.Add(System.Text.Encoding.UTF8.GetString(row.Key).Substring(3));
                //}
                //catch
                //{}                  

            }
            //}
            //finally
            //{
            //    _sync_openTablesHolder.ExitReadLock();
            //}

            return ret;
        }


        /// <summary>
        /// Deletes user table
        /// </summary>
        /// <param name="userTableName"></param>
        public void DeleteTable(string userTableName)
        {
            string tableName = GetUserTableNameAsString(userTableName);
            this.cachedTableNames.Remove(tableName);

            //Blocking Schema
            _sync_openTablesHolder.EnterWriteLock();
            try
            {
                if (_openTablesHolder.ContainsKey(tableName))
                {
                    //Someone can use this table
                    //We dispose table, what will cause disposing DBstorage and RollbackStorage
                    //In this moment parallel reading table threads inside of Iterations, can get Exceptions - What is acceptable for now.
                    _openTablesHolder[tableName].Dispose();


                    _openTablesHolder[tableName] = null;

                    //Deleting table from the holder
                    _openTablesHolder.Remove(tableName);
                }

                //Trying to get full file name, via globilzed function which will also support mapping outside the DB main directory
                string physicalDbFileName = GetPhysicalPathToTheUserTable(userTableName);

                if (physicalDbFileName == String.Empty)
                    return; //fake

                //Removing record from the schema

                byte[] btTableName = GetUserTableNameAsByte(userTableName);

                //ulong cc = LTrie.Count();
                LTrie.Remove(ref btTableName);
                LTrie.Commit();
                //cc = LTrie.Count();

                //Deleting file physically
                if (physicalDbFileName != "MEMORY")
                    DeleteAllReleatedTableFiles(physicalDbFileName);
            }
            catch (System.Exception ex)
            {
                DBreezeException.Throw(DBreezeException.eDBreezeExceptions.SCHEME_TABLE_DELETE_FAILED, userTableName, ex);
            }
            finally
            {
                _sync_openTablesHolder.ExitWriteLock();
            }


        }


        /// <summary>
        /// Renames user table, if it existed.
        /// <para>Safe, will make operation only when other threads stop to work with the oldTable</para>
        /// </summary>
        /// <param name="oldUserTableName"></param>
        /// <param name="newUserTableName"></param>
        public void RenameTable(string oldUserTableName, string newUserTableName)
        {
            for (; ; )
            {
                if (_disposed)
                    return;

                if (RenameTableInternal(oldUserTableName, newUserTableName))
                    return;

#if NET35 || NETr40
                System.Threading.Thread.Sleep(200);
#else
                System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(200));
#endif 
            }

        }

        /// <summary>
        /// Renames user table, if it existed.
        /// <para>If there are threads which are working with this table, rename will not be finished and will return false</para>
        /// </summary>
        /// <param name="oldUserTableName"></param>
        /// <param name="newUserTableName"></param>
        /// <returns>true if successfully renamed, otherwise false</returns>
        private bool RenameTableInternal(string oldUserTableName, string newUserTableName)
        {
            this.DeleteTable(newUserTableName);

            _sync_openTablesHolder.EnterWriteLock();
            try
            {
                string oldTableName = GetUserTableNameAsString(oldUserTableName);
                string newTableName = GetUserTableNameAsString(newUserTableName);

                OpenTable ot = null;

                string alternativeTableLocation = String.Empty;
                bool inMemory = false;

                _openTablesHolder.TryGetValue(oldTableName, out ot);

                if (CheckAlternativeTableLocationsIntersections(oldUserTableName, out alternativeTableLocation))
                {
                    if (alternativeTableLocation == String.Empty)
                    {
                        //In-Memory Table
                        inMemory = true;
                    }
                    else
                    {
                        if (ot != null)
                        {
                            return false;       //Other threads are working with this table

                        }
                    }
                }
                else
                {
                    if (Engine.Configuration.Storage == DBreezeConfiguration.eStorage.MEMORY)
                    {
                        //In-Memory Table
                        inMemory = true;
                    }
                    else
                    {
                        if (ot != null)
                        {
                            return false;       //Other threads are working with this table

                        }
                    }
                }

                //Changing key in Schema db

                byte[] btOldTableName = GetUserTableNameAsByte(oldUserTableName);
                byte[] btNewTableName = GetUserTableNameAsByte(newUserTableName);

                LTrie.ChangeKey(ref btOldTableName, ref btNewTableName);
                LTrie.Commit();

                this.cachedTableNames.Remove(oldTableName);

                if (inMemory && ot != null)
                {
                    //Changing reference for in-memory table,
                    _openTablesHolder.Add(newTableName, ot);
                    _openTablesHolder.Remove(oldTableName);
                }

            }
            catch (System.Exception ex)
            {
                DBreezeException.Throw(DBreezeException.eDBreezeExceptions.SCHEME_TABLE_RENAME_FAILED, oldUserTableName, ex);
            }
            finally
            {
                _sync_openTablesHolder.ExitWriteLock();
            }


            return true;
        }


    }
}
