/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DBreeze.Storage
{
    /*
     .NET 8 optimized, some refs where removed from MSR
     */

    /// <summary>
    /// Memory storage
    /// </summary>
    internal class MemoryStorage : IDisposable
    {
        private byte[] _f;
        private readonly object _lock = new object();

        private int _ptrEnd = 0;
        private int _capacity = 0;

        private readonly int _initialCapacity = 0;
        private readonly int _increaseOnInBytes = 1000000;

        private readonly eMemoryExpandStartegy _expandStrategy = eMemoryExpandStartegy.FIXED_LENGTH_INCREASE;

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
        public MemoryStorage(int initialCapacity, int increaseOnBytes, eMemoryExpandStartegy strategy)
        {
            if (initialCapacity < 5)
                initialCapacity = 5;

            _initialCapacity = initialCapacity;

            if (strategy == eMemoryExpandStartegy.FIXED_LENGTH_INCREASE)
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
                _f = null!;
            }
        }

        /// <summary>
        /// Gives an ability to access field itself. Must use external logical lock.
        /// </summary>
        public byte[] RawBuffer
        {
            get
            {
                return _f;
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
                if (_ptrEnd == 0) return Array.Empty<byte>();

                // .NET 8 Optimization: Skips zeroing out the array before copying
                byte[] ret = GC.AllocateUninitializedArray<byte>(_ptrEnd);
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
                return _f.Length;
            }
        }

        /// <summary>
        /// If length = 0 returns new byte[0]
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public byte[]? Read(int offset, int length)
        {
            if (offset >= _capacity || offset < 0 || length < 0)
                return null;

            if (length == 0)
                return Array.Empty<byte>();

            lock (_lock)
            {
                int q2r = length;

                if (offset + length > _capacity)
                    q2r = _capacity - offset;

                // .NET 8 Optimization: Skips zeroing out the array
                byte[] ret = GC.AllocateUninitializedArray<byte>(q2r);
                Buffer.BlockCopy(_f, offset, ret, 0, q2r);

                return ret;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Resize(int upTo)
        {
            if (upTo <= _capacity)
                return;
                        
            switch (_expandStrategy)
            {
                case eMemoryExpandStartegy.MULTIPLY_CAPACITY_BY_2:
                    int step = _capacity * 2;
                    _capacity = ((upTo + step - 1) / step) * step;
                    break;

                case eMemoryExpandStartegy.FIXED_LENGTH_INCREASE:
                    int diff = upTo - _capacity;
                    int multiples = (diff + _increaseOnInBytes - 1) / _increaseOnInBytes;
                    _capacity += multiples * _increaseOnInBytes;
                    break;
            }

            byte[] nf = new byte[_capacity];
            if (_ptrEnd > 0)
            {
                Buffer.BlockCopy(_f, 0, nf, 0, _ptrEnd);
            }
            _f = nf;
        }

        /// <summary>
        /// Must be called from lock
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Write(byte[] data, int offset)
        {
            int pe = offset + data.Length;

            if (pe > _capacity)
                Resize(pe);

            if (pe > _ptrEnd)
                _ptrEnd = pe;

            Buffer.BlockCopy(data, 0, _f, offset, data.Length);
        }

        //}

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public int Write_ToTheEnd(byte[] data)
        {
            if (data == null || data.Length < 1)
                return _ptrEnd;

            lock (_lock)
            {
                int retPtr = _ptrEnd;
                Write(data, _ptrEnd);
                return retPtr;
            }
        }
               

        /// <summary>
        /// 
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="data"></param>
        public void Write_ByOffset(int offset, byte[] data)
        {
            if (data == null || data.Length < 1 || offset < 0)
                return;

            lock (_lock)
            {
                Write(data, offset);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="datas"></param>
        public void Writes_ByOffsets(Dictionary<long, byte[]> datas)
        {           
            if (datas == null || datas.Count == 0)
                return;

            lock (_lock)
            {
                foreach (KeyValuePair<long, byte[]> data in datas) //no need in datas.OrderBy(r=>r.Key)
                {
                    Write(data.Value, (int)data.Key);
                }
            }
        }
    }
}