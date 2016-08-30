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
    /// <summary>
    /// Storage interface
    /// </summary>
    public interface IStorage
    {
        //!!!!!  Dispose storage self by Table_Dispose?
        //!!!!!  WriteToTheEnd(params byte[][] data) in TrieDiskStorage probably remove

        //Common
        TrieSettings TrieSettings { get; }
        DBreezeConfiguration DbreezeConfiguration { get; }

        //Table
        string Table_FileName { get; }
        //byte[] Table_WriteToTheEnd(ref byte[] data);
        //void Table_WriteByOffset(long offset, ref byte[] data);
        //void Table_WriteByOffset(byte[] offset, ref byte[] data);
        //void Table_WritesByOffset(Dictionary<long, byte[]> datas);
        //byte[] Table_Read(long offset, int quantity);
        //byte[] Table_Read(byte[] offset, int quantity);
        //void Table_RecreateFile();
        
        //void Commit_Signal();
        //void Rollback_Signal();

        void Table_Dispose();

        //Rollback
        //string Rollback_FileName { get; }
        //void Rollback_Write_Helper(long lastRollBackLength, byte[] data);
        //byte[] Rollback_Read_Helper();
        //void Rollback_CreateFiles();
        //void Rollback_RecreateFiles();
        //void Rollback_Read(long position, ref byte[] data, out int readOut);
        //void Rollback_Write(long offset, byte[] data, byte[] LastRollBackLength);

        //void Rollback_Dispose();

        //------------------------- new generation DBreeze 150

        ////////////// transaction parts

        void Commit();
        void Rollback();
        void TransactionalCommit();
        void TransactionalCommitIsFinished();
        void TransactionalRollback();

        //
        void RecreateFiles();


        ////////////// writes and reads
        void Table_WriteByOffset(long offset, byte[] data);
        void Table_WriteByOffset(byte[] offset, byte[] data);        
        byte[] Table_WriteToTheEnd(byte[] data);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="useCache">true=reading threads, false = writing threads</param>
        /// <param name="offset"></param>
        /// <param name="quantity"></param>
        /// <returns></returns>
        byte[] Table_Read(bool useCache, long offset, int quantity);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="useCache">true=reading threads, false = writing threads</param>
        /// <param name="offset"></param>
        /// <param name="quantity"></param>
        /// <returns></returns>
        byte[] Table_Read(bool useCache, byte[] offset, int quantity);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="newTableFullPath"></param>
        void RestoreTableFromTheOtherTable(string newTableFullPath);

        /// <summary>
        /// UTC DateTime when table was initialized
        /// </summary>
        DateTime StorageFixTime { get; }

        /// <summary>
        /// Length of the Storage
        /// </summary>
        long Length { get; }
    }
}
