#if NET472 || NETSTANDARD2_1 || NETCOREAPP2_0
/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DBreeze.Utils
{
    /// <summary>
    /// Thread-Safe Concurrent MultiKeySortedDictionary where key is a Tuple with more than one key.
    /// Optimized for .NET 8. Uses ReaderWriterLockSlim for concurrent reads and exclusive writes.
    /// </summary>
    /// <typeparam name="TKey">Must be an ITuple (ValueTuple)</typeparam>
    /// <typeparam name="TValue">The value type</typeparam>
    public class MultiKeyConcurrentSortedDictionary<TKey, TValue> : IDisposable where TKey : ITuple
    {
        private readonly SortedDictionary<object, object> _dict = new();
        private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

        private int _dimension = -1;
        private long _count = 0;

        private readonly TKey _defaultKey = default!;
        private Func<List<object>, TKey> _impl = null!;

        public MultiKeyConcurrentSortedDictionary()
        {            
            MultiKeyDictionary.CreateDeconstructDelegate(_defaultKey.Length, _defaultKey.GetType(), ref _impl);
        }

        /// <summary>
        /// Total count of elements in the dictionary.
        /// </summary>
        public long Count
        {
            get
            {
                _lock.EnterReadLock();
                try { return _count; }
                finally { _lock.ExitReadLock(); }
            }
        }

        public byte[]? Serialize()
        {
            if (MultiKeyDictionary.ByteArraySerializator == null) return null;

            _lock.EnterReadLock();
            try
            {
                var dataToSerialize = GetAllObjInternal().Select(r => ((TKey)_impl(r), (TValue)r[^1]));
                return MultiKeyDictionary.ByteArraySerializator(dataToSerialize);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Deserialize(byte[] data)
        {
            if (MultiKeyDictionary.ByteArrayDeSerializator == null) return;

            var items = (IEnumerable<(TKey, TValue)>)MultiKeyDictionary.ByteArrayDeSerializator(data, typeof(IEnumerable<(TKey, TValue)>));

            _lock.EnterWriteLock();
            try
            {
                ClearInternal();
                foreach (var (key, value) in items)
                {
                    AddInternal(key, value);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Add(TKey keys, TValue value)
        {
            _lock.EnterWriteLock();
            try { AddInternal(keys, value); }
            finally { _lock.ExitWriteLock(); }
        }

        private void AddInternal(TKey keys, TValue value)
        {
            int tp = keys.Length;

            if (_dimension > -1 && tp != _dimension)
                throw new ArgumentException($"Key dimension is {_dimension}");

            if (_dimension == -1 && tp == 0)
                throw new ArgumentException("Keys are not supplied");

            SortedDictionary<object, object> currentDict = _dict;

            for (int i = 0; i < tp; i++)
            {
                var skt = keys[i] ?? throw new ArgumentNullException(nameof(keys), "Unsupported key type NULL");

                if (!currentDict.TryGetValue(skt, out var obj))
                {
                    if (i == tp - 1)
                    {
                        currentDict[skt] = value!;
                        _count++;
                    }
                    else
                    {
                        var newDict = new SortedDictionary<object, object>();
                        currentDict[skt] = newDict;
                        currentDict = newDict;
                    }
                }
                else
                {
                    if (i == tp - 1)
                        currentDict[skt] = value!; // Update existing
                    else
                        currentDict = (SortedDictionary<object, object>)obj;
                }
            }

            if (_dimension == -1)
                _dimension = tp;
        }

        /// <summary>
        /// Returns a snapshot of all elements. 
        /// Materialized to a List to prevent locking the dictionary during external iteration.
        /// </summary>
        public IReadOnlyList<(TKey, TValue)> GetAll()
        {
            _lock.EnterReadLock();
            try
            {
                return GetAllObjInternal()
                    .Select(el => ((TKey)_impl(el), (TValue)el[^1]))
                    .ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        private IEnumerable<List<object>> GetAllObjInternal()
        {
            if (_dimension > -1)
            {
                List<object> l = new();
                foreach (var el in GetRecursInternal(_dict, 1, l))
                {
                    l.Add(el);
                    yield return l;
                    l.RemoveAt(l.Count - 1);
                }
            }
        }

        /// <summary>
        /// Returns a snapshot of elements matching the start keys.
        /// </summary>
        public IReadOnlyList<(TKey, TValue)> GetByKeyStart(params object[] keys)
        {
            _lock.EnterReadLock();
            try
            {
                return GetByKeyStartInternal(keys).ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        private IEnumerable<(TKey, TValue)> GetByKeyStartInternal(object[] keys)
        {
            if (keys == null || keys.Length == 0)
            {
                foreach (var el in GetAllObjInternal())
                {
                    yield return ((TKey)_impl(el), (TValue)el[^1]);
                }
            }
            else if (_dimension == keys.Length)
            {
                if (TryGetValueInternal(out var getRes, keys))
                {
                    List<object> l = new(keys);
                    l.Add(getRes!);
                    yield return ((TKey)_impl(l), (TValue)getRes!);
                }
            }
            else if (_dimension > -1)
            {
                List<object> l = new();
                foreach (var el in GetRecursByKeyStartInternal(_dict, 1, l, keys))
                {
                    l.Add(el);
                    yield return ((TKey)_impl(l), (TValue)el);
                    l.RemoveAt(l.Count - 1);
                }
            }
        }

        public void Clear()
        {
            _lock.EnterWriteLock();
            try { ClearInternal(); }
            finally { _lock.ExitWriteLock(); }
        }

        private void ClearInternal()
        {
            _dict.Clear();
            _dimension = -1;
            _count = 0;
        }

        public bool Contains(TKey keys)
        {
            _lock.EnterReadLock();
            try
            {
                if (_dimension == -1) return false;
                if (keys.Length != _dimension) throw new ArgumentException($"Key dimension is {_dimension}");

                var objArray = new object[keys.Length];
                for (int i = 0; i < keys.Length; i++) objArray[i] = keys[i]!;

                return TryGetValueInternal(out object? _, objArray);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Remove(TKey keys)
        {
            var obj = new object[keys.Length];
            for (int i = 0; i < keys.Length; i++) obj[i] = keys[i]!;
            Remove(obj);
        }

        public void Remove(params object[] keys)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_dimension == -1) return;
                if (keys.Length > _dimension) throw new ArgumentException($"Key dimension is {_dimension}");

                SortedDictionary<object, object> currentDict = _dict;
                int tp = keys.Length;

                for (int i = 0; i < tp; i++)
                {
                    var skt = keys[i] ?? throw new ArgumentNullException(nameof(keys), "Unsupported key type NULL");

                    if (i == tp - 1)
                    {
                        int removedQuantity = 1;

                        if (_defaultKey.Length != keys.Length)
                        {
                            removedQuantity = GetByKeyStartInternal(keys).Count();
                        }

                        int cdLen = currentDict.Count;
                        currentDict.Remove(skt);
                        if (cdLen != currentDict.Count)
                            _count -= removedQuantity;

                        break;
                    }

                    if (!currentDict.TryGetValue(skt, out var obj)) return;
                    currentDict = (SortedDictionary<object, object>)obj;
                }

                if (_dict.Count == 0) _dimension = -1;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public TValue this[TKey keys]
        {
            get => Get(keys)!;
            set => Add(keys, value);
        }

        public TValue? Get(TKey keys)
        {
            TryGetValue(keys, out var result);
            return result;
        }

        public bool TryGetValue(TKey keys, out TValue? result)
        {
            _lock.EnterReadLock();
            try
            {
                if (_dimension == -1 || keys.Length != _dimension)
                {
                    result = default;
                    return false;
                }

                var obj = new object[keys.Length];
                for (int i = 0; i < keys.Length; i++) obj[i] = keys[i]!;

                bool success = TryGetValueInternal(out object? internalResult, obj);
                result = success ? (TValue)internalResult! : default;
                return success;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        private bool TryGetValueInternal(out object? result, params object[] keys)
        {
            SortedDictionary<object, object> currentDict = _dict;
            int tp = keys.Length;

            for (int i = 0; i < tp; i++)
            {
                var skt = keys[i];

                if (!currentDict.TryGetValue(skt, out var obj))
                {
                    result = default;
                    return false;
                }

                if (i == tp - 1)
                {
                    result = obj;
                    return true;
                }

                currentDict = (SortedDictionary<object, object>)obj;
            }

            result = default;
            return false;
        }

        private IEnumerable<object> GetRecursByKeyStartInternal(SortedDictionary<object, object> di, int dim, List<object> l, object[] keys)
        {
            SortedDictionary<object, object> subDi = di;

            while (dim <= keys.Length)
            {
                if (_dimension != dim)
                {
                    var skt = keys[dim - 1];
                    if (!subDi.TryGetValue(skt, out var objSubDi))
                        yield break;

                    l.Add(keys[dim - 1]);
                    dim++;
                    subDi = (SortedDictionary<object, object>)objSubDi;
                }
                else break;
            }

            foreach (var el in subDi)
            {
                l.Add(el.Key);

                if (_dimension == dim)
                {
                    yield return el.Value;
                    l.RemoveAt(l.Count - 1);
                }
                else
                {
                    foreach (var el1 in GetRecursByKeyStartInternal((SortedDictionary<object, object>)el.Value, dim + 1, l, keys))
                        yield return el1;

                    l.RemoveAt(l.Count - 1);
                }
            }
        }

        private IEnumerable<object> GetRecursInternal(SortedDictionary<object, object> di, int dim, List<object> l)
        {
            foreach (var el in di)
            {
                l.Add(el.Key);

                if (_dimension == dim)
                {
                    yield return el.Value;
                    l.RemoveAt(l.Count - 1);
                }
                else
                {
                    foreach (var el1 in GetRecursInternal((SortedDictionary<object, object>)el.Value, dim + 1, l))
                        yield return el1;

                    l.RemoveAt(l.Count - 1);
                }
            }
        }

        public void Dispose()
        {
            _lock?.Dispose();
        }
    }
}
#endif