/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBreeze.Exceptions
{
    /// <summary>
    /// Unified class for Debreeze exceptions
    /// </summary>
    public class DBreezeException : Exception
    {

        public DBreezeException()
        {
        }

        public DBreezeException(string message)
            : base(message)
        {
            
        }

        public DBreezeException(string message,Exception innerException)
            : base(message,innerException)
        {

        }

        /*   USAGE  
         try
            {
                int i = 0;
                int b = 12;
                int c = b / i;               
            }
            catch(System.Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.GET_TABLE_WRITE_FAILED, "myTable", ex);  
         *      //or just 
         *      throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DB_IS_NOT_OPERATABLE,ex);  
         *      //or just     
         *      throw new DBreezeException("my extra info", ex);
            }
         
         * Result Exception will be
         * 
         * DBreeze.Exceptions.DBreezeException: Getting table "myTable" from the schema failed! --> DIVIDE BY ZERO....
               bei DBreeze.Transactions.Transaction.Insert(String tableName, Byte[] key, Byte[] value) in C:\Users\blaze\Documents\Visual Studio 2010\Projects\DBreeze\DBreeze\Transactions\Transaction.cs:Zeile 67.
               bei VisualTester.FastTests.TA1_Thread1() in C:\Users\blaze\Documents\Visual Studio 2010\Projects\DBreeze\VisualTester\FastTests.cs:Zeile 646.
         * 
         */


        public enum eDBreezeExceptions
        {
            UNKNOWN, //Fake one

            //General, internal exception
            GENERAL_EXCEPTION_DB_NOT_OPERABLE,
            GENERAL_EXCEPTION_DB_OPERABLE,

            //Enging
            DB_IS_NOT_OPERABLE,
            CREATE_DB_FOLDER_FAILED,

            //Schema
            SCHEME_GET_TABLE_WRITE_FAILED,
            SCHEME_FILE_PROTOCOL_IS_UNKNOWN,
            SCHEME_TABLE_DELETE_FAILED,
            SCHEME_TABLE_RENAME_FAILED,

            //SchemaInternal.UserTable name patterns
            TABLE_NAMES_TABLENAMECANTBEEMPTY,
            TABLE_NAMES_TABLENAMECANT_CONTAINRESERVEDSYMBOLS,
            TABLE_PATTERN_CANTBEEMPTY,
            TABLE_PATTERN_SYMBOLS_AFTER_SHARP,

            //LTrie
            TABLE_IS_NOT_OPEARABLE,
            COMMIT_FAILED,
            TRANSACTIONAL_COMMIT_FAILED,
            ROLLBACK_FAILED,
            TRANSACTIONAL_ROLLBACK_FAILED,
            ROLLBACK_NOT_OPERABLE,
            PREPARE_ROLLBACK_FILE_FAILED,
            KEY_IS_TOO_LONG,
            RECREATE_TABLE_FAILED,
            RESTORE_ROLLBACK_DATA_FAILED,

            //Transaction Journal
            CLEAN_ROLLBACK_FILES_FOR_FINISHED_TRANSACTIONS_FAILED,

            //Transactions Coordinator
            TRANSACTION_DOESNT_EXIST,
            TRANSACTION_CANBEUSED_FROM_ONE_THREAD,
            TRANSACTION_IN_DEADLOCK,
            TRANSACTION_TABLE_WRITE_REGISTRATION_FAILED,
            TRANSACTION_GETTING_TRANSACTION_FAILED,


            //Transaction
            TRANSACTION_TABLES_RESERVATION_FAILED,
            TRANSACTION_TABLES_RESERVATION_CANBEDONE_ONCE,
            TRANSACTION_TABLES_RESERVATION_LIST_MUSTBEFILLED,

            //DataTypes
            UNSUPPORTED_DATATYPE,
            UNSUPPORTED_DATATYPE_VALUE,
            KEY_CANT_BE_NULL,
            PARTIAL_VALUE_CANT_BE_NULL,

            //XML serializer
            XML_SERIALIZATION_ERROR,
            XML_DESERIALIZATION_ERROR,

            //MICROSOFT JSON serializer
            MJSON_SERIALIZATION_ERROR,
            MJSON_DESERIALIZATION_ERROR,

            //Custom serializer
            CUSTOM_SERIALIZATION_ERROR,
            CUSTOM_DESERIALIZATION_ERROR,

            //DBINTABLE
            DBINTABLE_CHANGEDATA_FROMSELECTVIEW,

            DYNAMIC_DATA_BLOCK_VALUE_IS_BIG,

            BACKUP_FOLDER_CREATE_FAILED,

            TABLE_WAS_CHANGED_LINKS_ARE_NOT_ACTUAL,
            /// <summary>
            /// The rest must be supplied via extra params
            /// </summary>
            DBREEZE_RESOURCES_CONCERNING
        }

        public static Exception Throw(Exception innerException)
        {
            return GenerateException(eDBreezeExceptions.GENERAL_EXCEPTION_DB_OPERABLE, String.Empty, innerException);
        }

        public static Exception Throw(eDBreezeExceptions exceptionType,Exception innerException)
        {
            return GenerateException(exceptionType, String.Empty, innerException);
        }

        public static Exception Throw(eDBreezeExceptions exceptionType)
        {
            return GenerateException(exceptionType, String.Empty, null);
        }

        public static Exception Throw(eDBreezeExceptions exceptionType, string message, Exception innerException)
        {
            return GenerateException(exceptionType, message, innerException);
        }

        /*  USAGE EXAMPLES
         
         throw new TableNotOperableException(this.TableName);
         throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DB_IS_NOT_OPERABLE);
         throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.ROLLBACK_FAILED, _rollbackFileName,ex);
         throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.KEY_IS_TOO_LONG);  
         */

        /// <summary>
        /// Internal
        /// </summary>
        /// <param name="exceptionType"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        private static Exception GenerateException(eDBreezeExceptions exceptionType, string message, Exception innerException)
        {
            switch (exceptionType)
            {
                //General
                case eDBreezeExceptions.GENERAL_EXCEPTION_DB_NOT_OPERABLE:
                    return new DBreezeException(String.Format("Database is not operable, please find out the problem and restart the engine! {0}",message), innerException);
                    
                //Enging
                case eDBreezeExceptions.DB_IS_NOT_OPERABLE:
                    return new DBreezeException(String.Format("Database is not operable, please find out the problem and restart the engine! {0}", message), innerException);
                case eDBreezeExceptions.CREATE_DB_FOLDER_FAILED:
                    return new DBreezeException("Creation of the database folder failed!", innerException);
                // return new DBreezeException(String.Format("{0}creation of the database folder failed: {1}", ExceptionHeader, originalException.ToString()));

                //Schema
                case eDBreezeExceptions.SCHEME_GET_TABLE_WRITE_FAILED:
                    return new DBreezeException(String.Format("Getting table \"{0}\" from the schema failed!", message), innerException);
                case eDBreezeExceptions.SCHEME_FILE_PROTOCOL_IS_UNKNOWN:
                    return new DBreezeException(String.Format("Scheme file protocol is unknown from the schema failed!"), innerException);
                case eDBreezeExceptions.SCHEME_TABLE_DELETE_FAILED:
                    return new DBreezeException(String.Format("User table \"{0}\" delete failed!",message), innerException);
                case eDBreezeExceptions.SCHEME_TABLE_RENAME_FAILED:
                    return new DBreezeException(String.Format("User table \"{0}\" rename failed!", message), innerException);


                //SchemaInternal.UserTable name patterns
                case eDBreezeExceptions.TABLE_NAMES_TABLENAMECANTBEEMPTY:
                    return new DBreezeException(String.Format("Table name can't be empty!"), innerException);
                case eDBreezeExceptions.TABLE_NAMES_TABLENAMECANT_CONTAINRESERVEDSYMBOLS:
                    return new DBreezeException(String.Format("Table name can not contain reserved symbols like * # @ \\ ^ $ ~ ´"), innerException);
                case eDBreezeExceptions.TABLE_PATTERN_CANTBEEMPTY:
                    return new DBreezeException(String.Format("Table pattern can't be empty!"), innerException);
                case eDBreezeExceptions.TABLE_PATTERN_SYMBOLS_AFTER_SHARP:
                    return new DBreezeException(String.Format("After # must follow / and any other symbol!"), innerException);
  
                    
                //LTrie
                //case eDBreezeExceptions.TABLE_IS_NOT_OPEARABLE:
                //    return new DBreezeException(String.Format("Table \"{0}\" is not operable!", message), innerException);
                case eDBreezeExceptions.COMMIT_FAILED:
                    return new DBreezeException(String.Format("Table \"{0}\" commit failed!", message), innerException);     //ADD TABLE NAME!!!
                case eDBreezeExceptions.TRANSACTIONAL_COMMIT_FAILED:
                    return new DBreezeException(String.Format("Transaction commit failed on table \"{0}\"!",message), innerException);
                case eDBreezeExceptions.RESTORE_ROLLBACK_DATA_FAILED:
                    return new DBreezeException(String.Format("Restore rollback file \"{0}\" failed!", message), innerException);
                case eDBreezeExceptions.ROLLBACK_NOT_OPERABLE:                                            //WTF ?????????????????
                    //return new DBreezeException(String.Format("{0}rollback of the file \"{1}\" is not operatable: {2}", ExceptionHeader, description, originalException.ToString()));
                    return new DBreezeException(String.Format("Rollback of the file \"{0}\" is not operable!", message), innerException);
                case eDBreezeExceptions.ROLLBACK_FAILED:                                                 
                    return new DBreezeException(String.Format("Rollback of the table \"{0}\" failed!", message), innerException);
                case eDBreezeExceptions.TRANSACTIONAL_ROLLBACK_FAILED:                                                 
                    return new DBreezeException(String.Format("Transaction rollback failed on the table \"{0}\"!", message), innerException);
                case eDBreezeExceptions.RECREATE_TABLE_FAILED:
                    return new DBreezeException(String.Format("Table \"{0}\" re-creation failed!", message), innerException);
                case eDBreezeExceptions.PREPARE_ROLLBACK_FILE_FAILED:
                    return new DBreezeException(String.Format("Rollback file \"{0}\" preparation failed!", message), innerException);
                case eDBreezeExceptions.KEY_IS_TOO_LONG:             
                    return new DBreezeException(String.Format("Key is too long, maximal key size is: {0}!", UInt16.MaxValue.ToString()), innerException);
                case eDBreezeExceptions.TABLE_WAS_CHANGED_LINKS_ARE_NOT_ACTUAL:
                    {
                        //It can happen when we have read LTrieRow with link to value, then table was re-created or restored from other table,
                        //and then we want to get value from an "old" link
                        return new DBreezeException(String.Format("Table was changed (Table Recrete, Table RestoreTableFromTheOtherTable), links are not actual, repeat reading operation!"), innerException);
                    }

                //Transaction Journal
                case eDBreezeExceptions.CLEAN_ROLLBACK_FILES_FOR_FINISHED_TRANSACTIONS_FAILED:
                    return new DBreezeException(String.Format("Transaction journal couldn't clean rollback files of the finished transactions!"), innerException);


                //Transactions Coordinator
                case eDBreezeExceptions.TRANSACTION_DOESNT_EXIST:
                    return new DBreezeException(String.Format("Transaction doesn't exist anymore!"), innerException);
                case eDBreezeExceptions.TRANSACTION_CANBEUSED_FROM_ONE_THREAD:
                    return new DBreezeException(String.Format("One transaction can be used from one thread only!"), innerException);
                case eDBreezeExceptions.TRANSACTION_IN_DEADLOCK:
                    return new DBreezeException(String.Format("Transaction is in a deadlock state and will be terminated. To avoid such case use Transaction.SynchronizeTables!"), innerException);
                case eDBreezeExceptions.TRANSACTION_TABLE_WRITE_REGISTRATION_FAILED:
                    return new DBreezeException(String.Format("Transaction registration table for Write failed!"), innerException);
                case eDBreezeExceptions.TRANSACTION_GETTING_TRANSACTION_FAILED:
                    return new DBreezeException(String.Format("getting transaction failed!"), innerException);


                //Transaction
                case eDBreezeExceptions.TRANSACTION_TABLES_RESERVATION_FAILED:
                    return new DBreezeException(String.Format("Reservation tables for modification or synchronized read failed! Use SynchronizeTables before any modification!"), innerException);
                case eDBreezeExceptions.TRANSACTION_TABLES_RESERVATION_CANBEDONE_ONCE:
                    return new DBreezeException(String.Format("Reservation tables for modification or synchronized read failed! Only one synchronization call permitted per transaction!"), innerException);
                case eDBreezeExceptions.TRANSACTION_TABLES_RESERVATION_LIST_MUSTBEFILLED:
                    return new DBreezeException(String.Format("Reservation tables for modification or synchronized read failed! Synchronization list must be filled!"), innerException);
                    

                //DataTypes
                case eDBreezeExceptions.UNSUPPORTED_DATATYPE:
                    return new DBreezeException(String.Format("Unsupported data type \"{0}\"!", message), innerException);
                case eDBreezeExceptions.UNSUPPORTED_DATATYPE_VALUE:
                    return new DBreezeException(String.Format("Unsupported data type value \"{0}\"!", message), innerException);                    
                case eDBreezeExceptions.KEY_CANT_BE_NULL:
                    return new DBreezeException(String.Format("Key can't be NULL!"), innerException);
                case eDBreezeExceptions.PARTIAL_VALUE_CANT_BE_NULL:
                    return new DBreezeException(String.Format("Partial value can't be NULL!"), innerException);


                //XML serializer
                case eDBreezeExceptions.XML_SERIALIZATION_ERROR:
                    return new DBreezeException(String.Format("XML serialization error!"), innerException);
                case eDBreezeExceptions.XML_DESERIALIZATION_ERROR:
                    return new DBreezeException(String.Format("XML deserialization error!"), innerException);


                //MICROSOFT JSON serializer
                case eDBreezeExceptions.MJSON_SERIALIZATION_ERROR:
                    return new DBreezeException(String.Format("Microsoft JSON serialization error!"), innerException);
                case eDBreezeExceptions.MJSON_DESERIALIZATION_ERROR:
                    return new DBreezeException(String.Format("Microsoft JSON deserialization error!"), innerException);

                //Custom serializer
                case eDBreezeExceptions.CUSTOM_SERIALIZATION_ERROR:
                    return new DBreezeException(String.Format("Custom serialization error!"), innerException);
                case eDBreezeExceptions.CUSTOM_DESERIALIZATION_ERROR:
                    return new DBreezeException(String.Format("Custom deserialization error!"), innerException);

                //DBINTABLE
                case eDBreezeExceptions.DBINTABLE_CHANGEDATA_FROMSELECTVIEW:
                    return new DBreezeException(String.Format("Changing data after SelectTable is not permitted, use InsertTable instead!"), innerException);

                //Dynamic data blocks
                case eDBreezeExceptions.DYNAMIC_DATA_BLOCK_VALUE_IS_BIG:
                    return new DBreezeException(String.Format("Value is too big, more then Int32.MaxValue!"), innerException);

                //Backup
                case eDBreezeExceptions.BACKUP_FOLDER_CREATE_FAILED:
                    return new DBreezeException(String.Format("Backup folder creation has failed"), innerException);

                case eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING:
                    return new DBreezeException(String.Format("DBreeze.DbreezeResources err: \"{0}\"!", message), innerException);
            }

            //Fake
            return new DBreezeException("Unknown mistake occured");
        }

    }
}
