/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBreeze.LianaTrie
{  
    internal class LTrieGenerationMap
    {
        //Dictionary<int, LTrieGenerationNode> _d = new Dictionary<int, LTrieGenerationNode>();
         LTrieGenerationNode[] _d = new LTrieGenerationNode[8];
        int size = 0;

        private bool ReGenerateMapUpToIndex = true;
        byte[] MapValuesUpToIndex = null;

        public int Count()
        {
            return size;
        }

        public void Add(int key, LTrieGenerationNode value)
        {
            if (key != size)
                throw new Exception("LTrieAsciiGenerationMap Generation Map error Add Index");

            ReGenerateMapUpToIndex = true;

            if((_d.Length-1) < key)
            {
                Array.Resize(ref _d, key*2);
            }

            _d[key] = value;

            //LTrieGenerationNode node = null;
            //_d.TryGetValue(key, out node);
            //if (node == null)
            //    _d.Add(key, value);
            //else
            //    _d[key] = value;

          

            //Place is ok!
            size++;
        }

        public IEnumerable<KeyValuePair<int, LTrieGenerationNode>> Ascending
        {
            get
            {
                for (int i = 0; i < size; i++)
                {
                    yield return new KeyValuePair<int, LTrieGenerationNode>(i, _d[i]);
                }
            }
        }

        public IEnumerable<KeyValuePair<int, LTrieGenerationNode>> Descending
        {
            get
            {
                for (int i = size - 1; i >= 0; i--)
                {
                    yield return new KeyValuePair<int, LTrieGenerationNode>(i, _d[i]);
                }
            }
        }

        public LTrieGenerationNode this[int key]
        {
            get
            {
                return _d[key];
            }
            set
            {
                _d[key] = value;

                ReGenerateMapUpToIndex = true;
            }
        }

        public void Clear()
        {
            size = 0;

            ReGenerateMapUpToIndex = true;
        }

        public bool ContainsKey(int key)
        {
            return (key < size);
        }

        public void RemoveBiggerThenKey(int key)
        {
            if ((key + 1) < size)
                size = key + 1;

            ReGenerateMapUpToIndex = true;
        }

        public void RemoveBiggerOrEqualThenKey(int key)
        {
            if (key < size)
                size = key;

            ReGenerateMapUpToIndex = true;
        }

        /// <summary>
        /// Used for finding out hash from Generation Map
        /// </summary>
        /// <param name="index"></param>
        /// <param name="forceGenerateMapUpToIndex"></param>
        /// <returns></returns>
        public byte[] GenerateMapNodesValuesUpToIndex(int index, bool forceGenerateMapUpToIndex = false)
        {
            if (!forceGenerateMapUpToIndex && !ReGenerateMapUpToIndex)
                return MapValuesUpToIndex;

            MapValuesUpToIndex = new byte[index + 1];


            for (int i = 0; i <= index; i++)
            {

                MapValuesUpToIndex[i] = _d[i].Value;
            }

            ReGenerateMapUpToIndex = false;

            return MapValuesUpToIndex;
        }

    }



    //internal class LTrieGenerationMap
    //{
    //    Dictionary<int, LTrieGenerationNode> _d = new Dictionary<int, LTrieGenerationNode>();
    //    int size = 0;

    //    private bool ReGenerateMapUpToIndex = true;
    //    byte[] MapValuesUpToIndex = null;

    //    public int Count()
    //    {
    //        return size;
    //    }

    //    public void Add(int key, ref LTrieGenerationNode value)
    //    {
    //        if (key != size)
    //            throw new Exception("LTrieAsciiGenerationMap Generation Map error Add Index");

    //        ReGenerateMapUpToIndex = true;


    //        LTrieGenerationNode node = null;
    //        _d.TryGetValue(key, out node);
    //        if (node == null)
    //            _d.Add(key, value);
    //        else
    //            _d[key] = value;

    //        //if (_d.ContainsKey(key))
    //        //{
    //        //    _d[key] = value;
    //        //}
    //        //else
    //        //{
    //        //    _d.Add(key, value);
    //        //}

    //        //Place is ok!
    //        size++;
    //    }

    //    public IEnumerable<KeyValuePair<int, LTrieGenerationNode>> Ascending
    //    {
    //        get
    //        {
    //            for (int i = 0; i < size; i++)
    //            {
    //                yield return new KeyValuePair<int, LTrieGenerationNode>(i, _d[i]);
    //            }
    //        }
    //    }

    //    public IEnumerable<KeyValuePair<int, LTrieGenerationNode>> Descending
    //    {
    //        get
    //        {
    //            for (int i = size - 1; i >= 0; i--)
    //            {
    //                yield return new KeyValuePair<int, LTrieGenerationNode>(i, _d[i]);
    //            }
    //        }
    //    }

    //    public LTrieGenerationNode this[int key]
    //    {
    //        get
    //        {
    //            return _d[key];
    //        }
    //        set
    //        {
    //            _d[key] = value;

    //            ReGenerateMapUpToIndex = true;
    //        }
    //    }

    //    public void Clear()
    //    {
    //        size = 0;

    //        ReGenerateMapUpToIndex = true;
    //    }

    //    public bool ContainsKey(int key)
    //    {
    //        return (key < size);
    //    }

    //    public void RemoveBiggerThenKey(int key)
    //    {
    //        if ((key + 1) < size)
    //            size = key + 1;

    //        ReGenerateMapUpToIndex = true;
    //    }

    //    public void RemoveBiggerOrEqualThenKey(int key)
    //    {
    //        if (key < size)
    //            size = key;

    //        ReGenerateMapUpToIndex = true;
    //    }

    //    /// <summary>
    //    /// Used for finding out hash from Generation Map
    //    /// </summary>
    //    /// <param name="index"></param>
    //    /// <returns></returns>
    //    public byte[] GenerateMapNodesValuesUpToIndex(int index)
    //    {
    //        if (!ReGenerateMapUpToIndex)
    //            return MapValuesUpToIndex;

    //        MapValuesUpToIndex = new byte[index + 1];


    //        for (int i = 0; i <= index; i++)
    //        {

    //            MapValuesUpToIndex[i] = _d[i].Value;
    //        }

    //        ReGenerateMapUpToIndex = false;

    //        return MapValuesUpToIndex;
    //    }

    //}
}
