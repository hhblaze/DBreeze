/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBreeze
{
    public interface IFileSystemFactory
    {
        /// <summary>
        ///this._fsData = new FileStream(this._fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);
        ///this._fsRollback = new FileStream(this._fileName + ".rol", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);
        ///this._fsRollbackHelper = new FileStream(this._fileName + ".rhp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);
        /// </summary>
        /// <param name="name"></param>
        /// <param name="bufferSize"></param>
        /// <returns></returns>
        IFileStream CreateType1(string name, int bufferSize);

        /// <summary>
        ///new FileStream(tfn, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        /// </summary>
        /// <param name="name"></param>
        /// <param name="bufferSize"></param>
        /// <returns></returns>
        IFileStream CreateType2(string name);

        /// <summary>
        ///new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.None);
        /// </summary>
        /// <param name="name"></param>
        /// <param name="bufferSize"></param>
        /// <returns></returns>
        IFileStream CreateType3(string name);


        /// <summary>
        /// File
        /// </summary>
        /// <param name="path"></param>
        void Delete(string path);
        /// <summary>
        /// File
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        bool Exists(string path);
        /// <summary>
        /// File
        /// </summary>
        /// <param name="sourceFileName"></param>
        /// <param name="destFileName"></param>
        void Move(string sourceFileName, string destFileName);



        IDirectoryInfo CreateDirectoryInfo(string name);
    }

    public interface IDirectoryInfo
    {
        void Create();
        bool Exists { get; }
        IFileInfo[] GetFiles();
    }

    public interface IFileInfo
    {
        string FullName { get; }
        string Name { get; }
        long Length { get; }
    }

    public interface IFileStream:IDisposable
    {
        //void Dispose();
        //void Dispose(bool disposing);
        long Length { get; }
        string Name { get; }
        long Position { get; set; }        
        void Write(byte[] array, int offset, int count);
        int Read(byte[] array, int offset, int count);
        void Flush(bool flushToDisk);
        void Flush();

    }
}
