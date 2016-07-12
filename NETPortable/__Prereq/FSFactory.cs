using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DBreeze;

using System.IO;

namespace DBreeze.Programmers
{
    public class FSFactory : IFileSystemFactory
    {
        public IDirectoryInfo CreateDirectoryInfo(string name)
        {
            MDirectoryInfo di = new MDirectoryInfo(name);
            return di;
        }

        public IFileStream CreateType1(string name, int bufferSize)
        {
            MFileStream fs = new MFileStream();
            fs.fs = new FileStream(name, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize, FileOptions.WriteThrough);
            return fs;
        }

        public IFileStream CreateType2(string name)
        {
            MFileStream fs = new MFileStream();
            fs.fs = new FileStream(name, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return fs;
        }

        public IFileStream CreateType3(string name)
        {
            MFileStream fs = new MFileStream();
            fs.fs = new FileStream(name, FileMode.Open, FileAccess.Read, FileShare.None);
            return fs;
        }

        public void Delete(string path)
        {
            File.Delete(path);
        }

        public bool Exists(string path)
        {
            return File.Exists(path);
        }

        public void Move(string sourceFileName, string destFileName)
        {
            File.Move(sourceFileName, destFileName);
        }
    }//eoc

    /// <summary>
    /// 
    /// </summary>
    public class MFileStream : IFileStream
    {
        internal FileStream fs = null;        

        public long Length
        {
            get
            {
                return fs.Length;
            }
        }

        public string Name
        {
            get
            {
                return fs.Name;
            }
        }

        public long Position
        {
            get
            {
                return fs.Position;
            }

            set
            {
                fs.Position = value;
            }
        }

        public void Dispose()
        {
            fs.Dispose();
        }

        public void Flush()
        {
            fs.Flush();
        }

        public void Flush(bool flushToDisk)
        {
            fs.Flush(flushToDisk);
        }

        public int Read(byte[] array, int offset, int count)
        {
            return fs.Read(array, offset, count);
        }

        public void Write(byte[] array, int offset, int count)
        {
            fs.Write(array, offset, count);
        }
    }//eoc

    /// <summary>
    /// 
    /// </summary>
    public class MDirectoryInfo : IDirectoryInfo
    {
        string name = "";
        DirectoryInfo di = null;

        public MDirectoryInfo(string name)
        {
            this.name = name;
            di = new DirectoryInfo(name);
        }

        public bool Exists
        {
            get
            {
                return di.Exists;
            }
        }

        public void Create()
        {
            if (!di.Exists)
                di.Create();            
        }

        public IFileInfo[] GetFiles()
        {
            FileInfo[] fis = di.GetFiles();
            IFileInfo[] mfi = new MFileInfo[fis.Length];
            FileInfo fi = null;

            for (int i = 0; i < mfi.Length; i++)
            {
                fi = fis[i];
                mfi[i] = new MFileInfo(fi.FullName, fi.Name, fi.Length);
            }


            return mfi;
        }
    }//eoc

    public class MFileInfo : IFileInfo
    {
        string fullName = "";
        string name = "";
        long length = 0;

        public MFileInfo(string fullName, string name, long length)
        {
            this.fullName = fullName;
            this.name = name;
            this.length = length;
        }

        public string FullName
        {
            get
            {
                return this.fullName;
            }
        }

        public long Length
        {
            get
            {
                return this.length;
            }
        }

        public string Name
        {
            get
            {
                return this.name;
            }
        }
    }//eoc


}//eoNs
