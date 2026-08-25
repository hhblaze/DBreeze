/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Collections.Generic;

namespace DBreeze.Storage
{
    /// <summary>
    /// Memory storage
    /// </summary>
    public class MemoryStorage:IDisposable
    {
        byte[] _f = null;
        readonly object _lock = new object();

        int _ptrEnd = 0;
        int _capacity = 0;

        int _initialCapacity = 0;
        int _increaseOnInBytes = 1000000;

        eMemoryExpandStartegy _expandStrategy = eMemoryExpandStartegy.FIXED_LENGTH_INCREASE;

        /// <summary>
        /// 
        /// </summary>
        public enum eMemoryExpandStartegy
        {
            MULTIPLY_CAPACITY_BY_2,
            FIXED_LENGTH_INCREASE
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="initialCapacity">Initial Memory Capacity in bytes</param>
        /// <param name="increaseOnBytes">Rules is strategy is FIXED_LENGTH_INCREASE, quantity of bytes to increse memory</param>
        /// <param name="strategy">Memory expand strategy</param>
        public MemoryStorage(int initialCapacity,int increaseOnBytes, eMemoryExpandStartegy strategy)
        {
            if (initialCapacity < 5)
                initialCapacity = 5;

            _initialCapacity = initialCapacity;

            if(strategy == eMemoryExpandStartegy.FIXED_LENGTH_INCREASE)
            {
                if (increaseOnBytes < 5)
                    increaseOnBytes = 5;

                _increaseOnInBytes = increaseOnBytes;
            }

            _expandStrategy = strategy;
            _capacity = _initialCapacity;
            _f = new byte[_initialCapacity];
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _f = null;
                _capacity = 0;
                _ptrEnd = 0;
            }
        }

        private void CheckDisposed()
        {
            if (_f == null)
                throw new ObjectDisposedException("MemoryStorage");
        }

        /// <summary>
        /// Gives an ability to access field itself. Must use external logical lock.
        /// </summary>
        public byte[] RawBuffer
        {
            get
            {
                lock (_lock)
                {
                    CheckDisposed();
                    return _f;
                }
            }
        }

        /// <summary>
        /// Sets EOF pointer to 0.
        /// <para>USE withInternalArrayResize by necessity. If it's true then array will be re-initialized to initial capacity</para>
        /// <para>this also will call GC and the whole process will take some time.</para>
        /// <para>If false, only pointer EOF will be set to 0, capacity of the array will not be changed - very fast</para>
        /// </summary>
        /// <param name="withInternalArrayResize"></param>
        public void Clear(bool withInternalArrayResize)
        {
            lock (_lock)
            {
                CheckDisposed();
                _ptrEnd = 0;
                               
                if (withInternalArrayResize)
                {
                    _capacity = _initialCapacity;
                    _f = new byte[_initialCapacity];
                }
            }
        }

        /// <summary>
        /// End of file
        /// </summary>
        public int EOF
        {
            get
            {
                lock (_lock)
                {
                    CheckDisposed();
                    return _ptrEnd;
                }
            }
        }

        /// <summary>
        /// Can return null
        /// </summary>
        /// <returns></returns>
        public byte[] GetFullData()
        {
            lock (_lock)
            {
                CheckDisposed();
                byte[] ret = new byte[_ptrEnd];

                Buffer.BlockCopy(_f, 0, ret, 0, _ptrEnd);

                return ret;
            }
        }

        /// <summary>
        /// Total reserved field length. EOF shows the end of useful information.
        /// </summary>
        public int MemorySize
        {
            get
            {
                lock (_lock)
                {
                    CheckDisposed();
                    return _f.Length;
                }
            }
        }

        /// <summary>
        /// If length = 0 returns new byte[0]
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public byte[] Read(int offset, int length)
        {
            lock (_lock)
            {
                CheckDisposed();

                if (offset < 0 || length < 0 || offset >= _capacity)
                    return null;

                if (length == 0)
                    return new byte[0];

                int available = _capacity - offset;
                int q2r = length > available ? available : length;

                byte[] ret = new byte[q2r];

                Buffer.BlockCopy(_f, offset, ret, 0, q2r);

                return ret;
            }
        }
     

        private void Resize(int upTo)
        {
            if (upTo <= _capacity)
                return;

            long newCapacity;

            switch (_expandStrategy)
            {
                case eMemoryExpandStartegy.MULTIPLY_CAPACITY_BY_2:
                    long step = (long)_capacity * 2L;
                    newCapacity = ((long)upTo + step - 1L) / step * step;
                    break;
                case eMemoryExpandStartegy.FIXED_LENGTH_INCREASE:
                    long difference = (long)upTo - _capacity;
                    long increments = (difference + _increaseOnInBytes - 1L) / _increaseOnInBytes;
                    newCapacity = (long)_capacity + increments * _increaseOnInBytes;
                    break;
                default:
                    throw new InvalidOperationException("Unknown memory expansion strategy.");
            }

            if (newCapacity > Int32.MaxValue)
                throw new OutOfMemoryException("MemoryStorage cannot exceed Int32.MaxValue bytes.");

            _capacity = (int)newCapacity;
            byte[] _nf = new byte[_capacity];
            Buffer.BlockCopy(_f, 0, _nf, 0, _ptrEnd);
            _f = _nf;
        }

        private void Write(byte[] data, int offset)
        {
            Write(ref data, offset);
        }

        /// <summary>
        /// Must be called from lock
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        private void Write(ref byte[] data, int offset)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset");

            long end = (long)offset + data.Length;
            if (end > Int32.MaxValue)
                throw new ArgumentOutOfRangeException("data", "MemoryStorage cannot exceed Int32.MaxValue bytes.");

            int pe = (int)end;

            if (pe > _capacity)
                Resize(pe); 

            if (pe > _ptrEnd)
                _ptrEnd = pe;

            Buffer.BlockCopy(data, 0, _f, offset, data.Length);
        }

        private void Write(byte[] data, int dataOffset, int count, int offset)
        {
            if (data == null)
                throw new ArgumentNullException("data");
            if (dataOffset < 0 || count < 0 || dataOffset > data.Length - count)
                throw new ArgumentOutOfRangeException("dataOffset/count");
            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset");

            long end = (long)offset + count;
            if (end > Int32.MaxValue)
                throw new OutOfMemoryException("MemoryStorage cannot exceed Int32.MaxValue bytes.");

            int pe = (int)end;
            if (pe > _capacity)
                Resize(pe);
            if (pe > _ptrEnd)
                _ptrEnd = pe;
            if (count != 0)
                Buffer.BlockCopy(data, dataOffset, _f, offset, count);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public int Write_ToTheEnd(ref byte[] data)
        {
            lock (_lock)
            {
                CheckDisposed();
                int retPtr = _ptrEnd;

                if (data == null || data.Length < 1)
                    return retPtr;

                Write(ref data, _ptrEnd);

                return retPtr;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public int Write_ToTheEnd(byte[] data)
        {
            lock (_lock)
            {
                CheckDisposed();
                int retPtr = _ptrEnd;

                if (data == null || data.Length < 1)
                    return retPtr;

                Write(ref data, _ptrEnd);

                return retPtr;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="data"></param>
        public void Write_ByOffset(int offset, ref byte[] data)
        {
            lock (_lock)
            {
                CheckDisposed();
                if (data == null || data.Length < 1 || offset < 0)
                    return;

                Write(ref data, offset);
            }

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="data"></param>
        public void Write_ByOffset(int offset, byte[] data)
        {
            lock (_lock)
            {
                CheckDisposed();
                if (data == null || data.Length < 1 || offset < 0)
                    return;

                Write(ref data, offset);
            }
        }

        internal void Write_ByOffset(int offset, byte[] data, int dataOffset, int count)
        {
            lock (_lock)
            {
                CheckDisposed();
                Write(data, dataOffset, count, offset);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="datas"></param>
        public void Writes_ByOffsets(Dictionary<long, byte[]> datas)
        {
            lock (_lock)
            {
                CheckDisposed();
                if (datas == null || datas.Count < 1)
                    return;

                foreach (var data in datas)   //no need in datas.OrderBy(r=>r.Key)
                {
                    if (data.Key < 0 || data.Key > Int32.MaxValue)
                        throw new ArgumentOutOfRangeException("datas", "MemoryStorage offset must fit Int32.");

                    Write(data.Value, (int)data.Key);
                    
                }                
            }
        }
    }
}
