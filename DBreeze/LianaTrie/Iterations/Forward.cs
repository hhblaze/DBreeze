/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.Utils;

namespace DBreeze.LianaTrie.Iterations
{
    public class Forward
    {
        LTrieRootNode _root = null;

        /// <summary>
        /// If we use load Key already With Value, or just key and link to the value
        /// </summary>
        public bool ReturnKeyValuePair = false; 

        public Forward(LTrieRootNode root, bool ValuesLazyLoadingIsOn)
        {
            _root = root;

            //ReturnKeyValuePair = !_root.Tree.ValuesLazyLoadingIsOn;
            ReturnKeyValuePair = !ValuesLazyLoadingIsOn;
        }

        byte[] endKey = null;
        bool includeStartKey = false;
        bool includeStopKey = false;
        ulong skippedCnt = 0;
        ulong skippingTotal = 0;
        bool keyIsFound = false;
        byte[] initialKey = null;


        #region "Iterate Forward"

        private IEnumerable<LTrieRow> ItFrw(LTrieGenerationNode gn, byte[] generationMapLine,bool useCache)
        {
            byte[] key = null;
            LTrieRow row = null;
            byte[] gml = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;

            foreach (var kd in gn.KidsInNode.GetKidsForward())
            {
                if (kd.ValueKid || !kd.LinkToNode)
                {
                    //Value Kid
                    //Raise Up Counter, iterate further if counter permits
                    if (ReturnKeyValuePair)
                    {
                        this._root.Tree.Cache.ReadKeyValue(useCache, kd.Ptr, out valueStartPtr, out valueLength, out key, out xValue);
                    }
                    else
                    {
                        key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                    }
                    //Console.WriteLine("KN: {0}", key.ToBytesString(""));


                    row = new LTrieRow(this._root);
                    if (ReturnKeyValuePair)
                    {
                        row.ValueStartPointer = valueStartPtr;
                        row.ValueFullLength = valueLength;
                        row.Value = xValue;
                        row.ValueIsReadOut = true;
                    }
                    row.Key = key;
                    row.LinkToValue = kd.Ptr;
                    yield return row;
                }
                else
                {
                    //It's a Link To Node, gettign new generation Node
                    LTrieGenerationNode gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItFrw(gn1, generationMapLine,useCache))
                    foreach (var xr in ItFrw(gn1, gml, useCache))
                    {
                        yield return xr;
                    }
                }
            }
        }


        public IEnumerable<LTrieRow> IterateForward(bool useCache)
        {
            LTrieGenerationNode gn = null;

           

            LTrieGenerationMap _generationMap = new LTrieGenerationMap();
            //_generationMap.Clear();

            //if (_generationMap.Count() == 0)
            //{
                //Loading it from Link TO ZERO Pointer
                gn = new LTrieGenerationNode(this._root);
                gn.Pointer = this._root.LinkToZeroNode;
                //gn.Value=0; - default
                _generationMap.Add(0, gn);

                gn.ReadSelf(useCache, _generationMap.GenerateMapNodesValuesUpToIndex(0));
            //}
            //else
            //{
            //    //new
            //    gn = _generationMap[0];
            //}

            //ulong cnt = 0;

            byte[] generationMapLine = new byte[1] { 0 };
            byte[] gml = null;
            LTrieGenerationNode gn1 = null;
            byte[] key = null;
            LTrieRow row = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;

            foreach (var kd in gn.KidsInNode.GetKidsForward())
            {
                //Kid can be value link or node link
                //if value link we can count 1 up
                if (kd.ValueKid || !kd.LinkToNode)
                {

                    if (ReturnKeyValuePair)
                    {
                        this._root.Tree.Cache.ReadKeyValue(useCache, kd.Ptr, out valueStartPtr, out valueLength, out key, out xValue);
                    }
                    else
                    {
                        key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                    }
                    //Console.WriteLine("KN: {0}", key.ToBytesString(""));

                    //cnt++;
                    row = new LTrieRow(this._root);
                    if (ReturnKeyValuePair)
                    {
                        row.ValueStartPointer = valueStartPtr;
                        row.ValueFullLength = valueLength;
                        row.Value = xValue;
                        row.ValueIsReadOut = true;
                    }
                    row.Key = key;
                    row.LinkToValue = kd.Ptr;
                    yield return row;
                }
                else
                {
                    gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItFrw(gn1, generationMapLine, useCache))
                    foreach (var xr in ItFrw(gn1, gml, useCache))
                    {
                        //cnt++;
                        yield return xr;
                    }
                }
            }

            //Console.WriteLine("CNT: {0}", cnt);
        }

        #endregion

        #region "Iterate Forward StartFrom key"



        private IEnumerable<LTrieRow> ItFrwStartFrom(LTrieGenerationNode gn, byte[] generationMapLine, bool useCache)
        {
            byte[] key = null;
            LTrieRow row = null;
            byte[] gml = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;

            int startFrom = 0;
            if (keyIsFound)
            {
                startFrom = 256; //will check starting from KidsValue and then 0-255
            }
            else
            {
                //Kid is still not found
                if (generationMapLine.Length > initialKey.Length)
                {
                    startFrom = 256;
                }
                else
                {
                    startFrom = initialKey[generationMapLine.Length - 1];
                }
            }

            foreach (var kd in gn.KidsInNode.GetKidsForward(startFrom))
            {
                if (kd.ValueKid || !kd.LinkToNode)
                {
                    //Value Kid    

                    

                    if (keyIsFound)
                    {
                        //We return this one key
                        if (ReturnKeyValuePair)
                        {
                            this._root.Tree.Cache.ReadKeyValue(useCache, kd.Ptr, out valueStartPtr, out valueLength, out key, out xValue);
                        }
                        else
                        {
                            key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                        }

                        row = new LTrieRow(this._root);
                        if (ReturnKeyValuePair)
                        {
                            row.ValueStartPointer = valueStartPtr;
                            row.ValueFullLength = valueLength;
                            row.Value = xValue;
                            row.ValueIsReadOut = true;
                        }
                        row.Key = key;
                        row.LinkToValue = kd.Ptr;
                        yield return row;
                    }
                    else
                    {
                        key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                        //Checking if key equals to the found element, bigger or smaller

                        //Key is still not found 

                        if ((includeStartKey) ? key.IfStringArrayBiggerOrEqualThen(initialKey) : key.IfStringArrayBiggerThen(initialKey))
                        //if (key.IfStringArrayBiggerThen(initialKey))
                        //if (IfFirstKeyIsBiggerThenCompareKey(key, initialKey))
                        {
                            keyIsFound = true;

                            //We return this one key                        
                            row = new LTrieRow(this._root);
                            row.Key = key;
                            row.LinkToValue = kd.Ptr;
                            yield return row;
                        }
                        
                    }
                }
                else
                {
                    if (!keyIsFound && startFrom != 256 && startFrom < kd.Val)
                        keyIsFound = true;

                    //It's a Link To Node, gettign new generation Node
                    LTrieGenerationNode gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItFrwStartFrom(gn1, generationMapLine, useCache))
                    foreach (var xr in ItFrwStartFrom(gn1, gml, useCache))
                    {
                        yield return xr;
                    }
                }
            }
        }


        public IEnumerable<LTrieRow> IterateForwardStartFrom(byte[] initKey, bool inclStartKey, bool useCache)
        {
            LTrieGenerationNode gn = null;

            initialKey = initKey;
            includeStartKey = inclStartKey;

         

            LTrieGenerationMap _generationMap = new LTrieGenerationMap();            

            //if (_generationMap.Count() == 0)
            //{
                //Loading it from Link TO ZERO Pointer
                gn = new LTrieGenerationNode(this._root);
                gn.Pointer = this._root.LinkToZeroNode;
                //gn.Value=0; - default
                _generationMap.Add(0, gn);

                gn.ReadSelf(useCache, _generationMap.GenerateMapNodesValuesUpToIndex(0));
            //}

            //ulong cnt = 0; //NEED ONLY FOR SKIP

            byte[] generationMapLine = new byte[1] { 0 };
            byte[] gml = null;
            LTrieGenerationNode gn1 = null;
            byte[] key = null;
            LTrieRow row = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;

            //Starting from first key. It's interesting inside of RecursiveYieldReturn to look Starting from value
            //If intialKey index already bigger then its own length
            //But for the first must be enough
            foreach (var kd in gn.KidsInNode.GetKidsForward(initialKey[0]))
            {
                //Console.WriteLine("KN: {0}", key.ToBytesString(""));

                //Kid can be value link or node link
                //if value link we can count 1 up
                if (kd.ValueKid || !kd.LinkToNode)
                {
                   

                    if (keyIsFound)
                    {
                        //We return this one key
                        if (ReturnKeyValuePair)
                        {
                            this._root.Tree.Cache.ReadKeyValue(useCache, kd.Ptr, out valueStartPtr, out valueLength, out key, out xValue);
                        }
                        else
                        {
                            key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                        }

                        //cnt++;
                        row = new LTrieRow(this._root);
                        if (ReturnKeyValuePair)
                        {
                            row.ValueStartPointer = valueStartPtr;
                            row.ValueFullLength = valueLength;
                            row.Value = xValue;
                            row.ValueIsReadOut = true;
                        }
                        row.Key = key;
                        row.LinkToValue = kd.Ptr;
                        yield return row;
                    }
                    else
                    {
                        //Checking if key equals to the found element, bigger or smaller
                        key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                        //Key is still not found 

                        if ((includeStartKey) ? key.IfStringArrayBiggerOrEqualThen(initialKey) : key.IfStringArrayBiggerThen(initialKey))
                        //if(key.IfStringArrayBiggerThen(initialKey))
                        //if (IfFirstKeyIsBiggerThenCompareKey(key, initialKey))
                        {
                            keyIsFound = true;

                            //We return this one key
                            //cnt++;
                            row = new LTrieRow(this._root);
                            row.Key = key;
                            row.LinkToValue = kd.Ptr;
                            yield return row;

                            //going on iteration
                        }
                        
                    }
                }
                else
                {
                    if (!keyIsFound && initialKey[0] < kd.Val)
                        keyIsFound = true;

                    gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    //increasing map line must hold already 2 elements
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItFrwStartFrom(gn1, generationMapLine, useCache))
                    foreach (var xr in ItFrwStartFrom(gn1, gml, useCache))
                    {
                        //cnt++;      //NEED ONLY FOR SKIP
                        yield return xr;
                    }
                }
            }

            //Console.WriteLine("CNT: {0}", cnt);
        }

        #endregion

        #region "Iterate Forward From-To Key"


        /// <summary>
        /// ItFrwFromTo
        /// </summary>
        /// <param name="gn"></param>
        /// <param name="generationMapLine"></param>
        /// <param name="useCache"></param>
        /// <returns></returns>
        private IEnumerable<LTrieRow> ItFrwFromTo(LTrieGenerationNode gn, byte[] generationMapLine, bool useCache)
        {
            byte[] key = null;
            LTrieRow row = null;
            byte[] gml = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;

            int startFrom = 0;
            if (keyIsFound)
            {
                startFrom = 256; //will check starting from KidsValue and then 0-255
            }
            else
            {
                //Kid is still not found
                if (generationMapLine.Length > initialKey.Length)
                {
                    startFrom = 256;
                }
                else
                {
                    startFrom = initialKey[generationMapLine.Length - 1];
                }
            }

            foreach (var kd in gn.KidsInNode.GetKidsForward(startFrom))
            {
                if (kd.ValueKid || !kd.LinkToNode)
                {
                    //Value Kid    

                    if (ReturnKeyValuePair)
                    {
                        this._root.Tree.Cache.ReadKeyValue(useCache, kd.Ptr, out valueStartPtr, out valueLength, out key, out xValue);
                    }
                    else
                    {
                        key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                    }

                    if (keyIsFound)
                    {
                        //We return this one key
                        if ((includeStopKey) ? key.IfStringArraySmallerOrEqualThen(endKey) : key.IfStringArraySmallerThen(endKey))
                        {
                            row = new LTrieRow(this._root);
                            if (ReturnKeyValuePair)
                            {
                                row.ValueStartPointer = valueStartPtr;
                                row.ValueFullLength = valueLength;
                                row.Value = xValue;
                                row.ValueIsReadOut = true;
                            }
                            row.Key = key;
                            row.LinkToValue = kd.Ptr;
                            yield return row;
                        }
                        else
                        {
                            yield return null;
                            break;
                        }
                    }
                    else
                    {
                        //Checking if key equals to the found element, bigger or smaller

                        //Key is still not found 

                        if ((includeStartKey) ? key.IfStringArrayBiggerOrEqualThen(initialKey) : key.IfStringArrayBiggerThen(initialKey))
                        //if (key.IfStringArrayBiggerThen(initialKey))
                        //if (IfFirstKeyIsBiggerThenCompareKey(key, initialKey))
                        {
                            keyIsFound = true;

                            if ((includeStopKey) ? key.IfStringArraySmallerOrEqualThen(endKey) : key.IfStringArraySmallerThen(endKey))
                            {
                                //We return this one key                        
                                row = new LTrieRow(this._root);
                                if (ReturnKeyValuePair)
                                {
                                    row.ValueStartPointer = valueStartPtr;
                                    row.ValueFullLength = valueLength;
                                    row.Value = xValue;
                                    row.ValueIsReadOut = true;
                                }
                                row.Key = key;
                                row.LinkToValue = kd.Ptr;
                                yield return row;
                            }
                            else
                            {
                                yield return null;
                                break;
                            }
                        }
                        //else
                        //    break;
                    }
                }
                else
                {
                    if (!keyIsFound && startFrom != 256 && startFrom < kd.Val)
                        keyIsFound = true;


                    //It's a Link To Node, gettign new generation Node
                    LTrieGenerationNode gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItFrwFromTo(gn1, generationMapLine, useCache))
                    foreach (var xr in ItFrwFromTo(gn1, gml, useCache))
                    {
                        if (xr == null)
                        {
                            yield return null;
                            break;
                        }
                        yield return xr;
                    }
                }
            }
        }

        /// <summary>
        /// IterateForwardFromTo
        /// </summary>
        /// <param name="initKey"></param>
        /// <param name="stopKey"></param>
        /// <param name="inclStartKey"></param>
        /// <param name="inclStopKey"></param>
        /// <param name="useCache"></param>
        /// <returns></returns>
        public IEnumerable<LTrieRow> IterateForwardFromTo(byte[] initKey, byte[] stopKey, bool inclStartKey, bool inclStopKey, bool useCache)
        {
            LTrieGenerationNode gn = null;

            initialKey = initKey;
            endKey = stopKey;
            includeStartKey = inclStartKey;
            includeStopKey = inclStopKey;

            LTrieGenerationMap _generationMap = new LTrieGenerationMap();

            //if (_generationMap.Count() == 0)
            //{
            //Loading it from Link TO ZERO Pointer
            gn = new LTrieGenerationNode(this._root);
            gn.Pointer = this._root.LinkToZeroNode;
            //gn.Value=0; - default
            _generationMap.Add(0, gn);

            gn.ReadSelf(useCache, _generationMap.GenerateMapNodesValuesUpToIndex(0));
            //}

            //ulong cnt = 0; //NEED ONLY FOR SKIP

            byte[] generationMapLine = new byte[1] { 0 };
            byte[] gml = null;
            LTrieGenerationNode gn1 = null;
            byte[] key = null;
            LTrieRow row = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;

            //Starting from first key. It's interesting inside of RecursiveYieldReturn to look Starting from value
            //If intialKey index already bigger then its own length
            //But for the first must be enough
            foreach (var kd in gn.KidsInNode.GetKidsForward(initialKey[0]))
            {
                //Console.WriteLine("KN: {0}", key.ToBytesString(""));

                //Kid can be value link or node link
                //if value link we can count 1 up
                if (kd.ValueKid || !kd.LinkToNode)
                {
                    if (ReturnKeyValuePair)
                    {
                        this._root.Tree.Cache.ReadKeyValue(useCache, kd.Ptr, out valueStartPtr, out valueLength, out key, out xValue);
                    }
                    else
                    {
                        key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                    }

                    if (keyIsFound)
                    {
                        //We return this one key

                        if ((includeStopKey) ? key.IfStringArraySmallerOrEqualThen(endKey) : key.IfStringArraySmallerThen(endKey))
                        {
                            row = new LTrieRow(this._root);
                            if (ReturnKeyValuePair)
                            {
                                row.ValueStartPointer = valueStartPtr;
                                row.ValueFullLength = valueLength;
                                row.Value = xValue;
                                row.ValueIsReadOut = true;
                            }
                            row.Key = key;
                            row.LinkToValue = kd.Ptr;
                            yield return row;
                        }
                        else
                            break;
                    }
                    else
                    {
                        //Checking if key equals to the found element, bigger or smaller

                        //Key is still not found 

                        if ((includeStartKey) ? key.IfStringArrayBiggerOrEqualThen(initialKey) : key.IfStringArrayBiggerThen(initialKey))
                        //if (IfFirstKeyIsBiggerThenCompareKey(key, initialKey))
                        {
                            keyIsFound = true;

                            //We return this one key in case if it's smaller or smaller/equal to stop key

                            if ((includeStopKey) ? key.IfStringArraySmallerOrEqualThen(endKey) : key.IfStringArraySmallerThen(endKey))
                            {
                                row = new LTrieRow(this._root);
                                if (ReturnKeyValuePair)
                                {
                                    row.ValueStartPointer = valueStartPtr;
                                    row.ValueFullLength = valueLength;
                                    row.Value = xValue;
                                    row.ValueIsReadOut = true;
                                }
                                row.Key = key;
                                row.LinkToValue = kd.Ptr;
                                yield return row;
                            }
                            else
                                break;

                            //going on iteration
                        }
                        //else
                        //    break;
                    }
                }
                else
                {
                    if (!keyIsFound && initialKey[0] < kd.Val)
                        keyIsFound = true;

                    gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    //increasing map line must hold already 2 elements
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItFrwFromTo(gn1, generationMapLine, useCache))
                    foreach (var xr in ItFrwFromTo(gn1, gml, useCache))
                    {
                        //cnt++;      //NEED ONLY FOR SKIP
                        if (xr == null)
                        {
                            break;
                        }
                        yield return xr;
                    }
                }
            }

            //Console.WriteLine("CNT: {0}", cnt);
        }

        #endregion

        #region "Get Minimal Key"

        private IEnumerable<LTrieRow> ItFrwForMin(LTrieGenerationNode gn, byte[] generationMapLine, bool useCache)
        {
            byte[] key = null;
            LTrieRow row = null;
            byte[] gml = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;

            foreach (var kd in gn.KidsInNode.GetKidsForward())
            {
                if (kd.ValueKid || !kd.LinkToNode)
                {
                    //Value Kid
                    //Raise Up Counter, iterate further if counter permits
                    if (ReturnKeyValuePair)
                    {
                        this._root.Tree.Cache.ReadKeyValue(useCache, kd.Ptr, out valueStartPtr, out valueLength, out key, out xValue);
                    }
                    else
                    {
                        key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                    }
                    //Console.WriteLine("KN: {0}", key.ToBytesString(""));


                    row = new LTrieRow(this._root);
                    if (ReturnKeyValuePair)
                    {
                        row.ValueStartPointer = valueStartPtr;
                        row.ValueFullLength = valueLength;
                        row.Value = xValue;
                        row.ValueIsReadOut = true;
                    }
                    row.Key = key;
                    row.LinkToValue = kd.Ptr;
                    yield return row;
                }
                else
                {
                    //It's a Link To Node, gettign new generation Node
                    LTrieGenerationNode gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItFrwForMin(gn1, generationMapLine, useCache))
                    foreach (var xr in ItFrwForMin(gn1, gml, useCache))
                    {
                        yield return xr;
                    }
                }
            }
        }


        public LTrieRow IterateForwardForMinimal(bool useCache)
        {
            LTrieGenerationNode gn = null;

           

            LTrieGenerationMap _generationMap = new LTrieGenerationMap();

            //_generationMap.Clear();

            //if (_generationMap.Count() == 0)
            //{
                //Loading it from Link TO ZERO Pointer
                gn = new LTrieGenerationNode(this._root);
                gn.Pointer = this._root.LinkToZeroNode;
                //gn.Value=0; - default
                _generationMap.Add(0, gn);

                gn.ReadSelf(useCache, _generationMap.GenerateMapNodesValuesUpToIndex(0));
            //}

            //ulong cnt = 0;

            byte[] generationMapLine = new byte[1] { 0 };
            byte[] gml = null;
            LTrieGenerationNode gn1 = null;
            byte[] key = null;
            LTrieRow row = new LTrieRow(this._root);
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;

            foreach (var kd in gn.KidsInNode.GetKidsForward())
            {
                //Kid can be value link or node link
                //if value link we can count 1 up
                if (kd.ValueKid || !kd.LinkToNode)
                {
                    if (ReturnKeyValuePair)
                    {
                        this._root.Tree.Cache.ReadKeyValue(useCache, kd.Ptr, out valueStartPtr, out valueLength, out key, out xValue);
                    }
                    else
                    {
                        key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                    }
                    //Console.WriteLine("KN: {0}", key.ToBytesString(""));

                    //cnt++;
                    row = new LTrieRow(this._root);
                    if (ReturnKeyValuePair)
                    {
                        row.ValueStartPointer = valueStartPtr;
                        row.ValueFullLength = valueLength;
                        row.Value = xValue;
                        row.ValueIsReadOut = true;
                    }
                    row.Key = key;
                    row.LinkToValue = kd.Ptr;
                    return row;
                }
                else
                {
                    gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItFrwForMin(gn1, generationMapLine, useCache))
                    foreach (var xr in ItFrwForMin(gn1, gml, useCache))
                    {
                        //cnt++;
                        return xr;
                    }
                }
            }

            //Console.WriteLine("CNT: {0}", cnt);

            return row;
        }

        #endregion

        #region "Skip StartFrom key N then Forward"

        //bool keyIsFound = false;


        private IEnumerable<LTrieRow> ItFrwSkipFrom(LTrieGenerationNode gn, byte[] generationMapLine, bool useCache)
        {
            byte[] key = null;
            LTrieRow row = null;
            byte[] gml = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;

            int startFrom = 0;
            if (keyIsFound)
            {
                startFrom = 256; //will check starting from KidsValue and then 0-255
            }
            else
            {
                //Kid is still not found
                if (generationMapLine.Length > initialKey.Length)
                {
                    startFrom = 256;
                }
                else
                {
                    startFrom = initialKey[generationMapLine.Length - 1];
                }
            }

            foreach (var kd in gn.KidsInNode.GetKidsForward(startFrom))
            {
                if (kd.ValueKid || !kd.LinkToNode)
                {
                    //Value Kid    
                                       

                    if (keyIsFound)
                    {
                        //We return this one key
                        skippedCnt++;

                        if (skippedCnt > skippingTotal)
                        {
                            if (ReturnKeyValuePair)
                            {
                                this._root.Tree.Cache.ReadKeyValue(useCache, kd.Ptr, out valueStartPtr, out valueLength, out key, out xValue);
                            }
                            else
                            {
                                key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                            }

                            row = new LTrieRow(this._root);
                            if (ReturnKeyValuePair)
                            {
                                row.ValueStartPointer = valueStartPtr;
                                row.ValueFullLength = valueLength;
                                row.Value = xValue;
                                row.ValueIsReadOut = true;
                            }
                            row.Key = key;
                            row.LinkToValue = kd.Ptr;
                            yield return row;
                        }
                    }
                    else
                    {
                        key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);

                        //Checking if key equals to the found element, bigger or smaller

                        //Key is still not found 

                        if (key.IfStringArrayBiggerOrEqualThen(initialKey))
                        {
                            keyIsFound = true;

                            //case if Startkey doesn't exist, then first encountered value can be calculated as first for skipping
                            if (key.IfStringArrayBiggerThen(initialKey))
                            {
                                skippedCnt++;

                                if (skippedCnt > skippingTotal)
                                {
                                    //key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);

                                    row = new LTrieRow(this._root);
                                    row.Key = key;
                                    row.LinkToValue = kd.Ptr;
                                    yield return row;
                                }
                            }
                           
                        }
                    }
                }
                else
                {
                    //special case when from key doesn't exist
                    if (startFrom< kd.Val)
                        keyIsFound = true;

                    //It's a Link To Node, gettign new generation Node
                    LTrieGenerationNode gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItFrwSkipFrom(gn1, generationMapLine, useCache))
                    foreach (var xr in ItFrwSkipFrom(gn1, gml, useCache))
                    {
                        yield return xr;
                    }
                }
            }
        }


        public IEnumerable<LTrieRow> IterateForwardSkipFrom(byte[] initKey, ulong skippingQuantity, bool useCache)
        {
         

            LTrieGenerationNode gn = null;

            initialKey = initKey;

            skippingTotal = skippingQuantity;

            LTrieGenerationMap _generationMap = new LTrieGenerationMap();

            //if (_generationMap.Count() == 0)
            //{
                //Loading it from Link TO ZERO Pointer
                gn = new LTrieGenerationNode(this._root);
                gn.Pointer = this._root.LinkToZeroNode;
                //gn.Value=0; - default
                _generationMap.Add(0, gn);

                gn.ReadSelf(useCache, _generationMap.GenerateMapNodesValuesUpToIndex(0));
            //}

            //ulong cnt = 0; //NEED ONLY FOR SKIP

            byte[] generationMapLine = new byte[1] { 0 };
            byte[] gml = null;
            LTrieGenerationNode gn1 = null;
            byte[] key = null;
            LTrieRow row = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;

            //Starting from first key. It's interesting inside of RecursiveYieldReturn to look Starting from value
            //If intialKey index already bigger then its own length
            //But for the first must be enough
            foreach (var kd in gn.KidsInNode.GetKidsForward(initialKey[0]))
            {
                //Console.WriteLine("KN: {0}", key.ToBytesString(""));

                //Kid can be value link or node link
                //if value link we can count 1 up
                if (kd.ValueKid || !kd.LinkToNode)
                {
                   

                    if (keyIsFound)
                    {
                        //We return this one key, if quantity of skips is enough
                        skippedCnt++;

                        if (skippedCnt > skippingTotal)
                        {
                            if (ReturnKeyValuePair)
                            {
                                this._root.Tree.Cache.ReadKeyValue(useCache, kd.Ptr, out valueStartPtr, out valueLength, out key, out xValue);
                            }
                            else
                            {
                                key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                            }

                            //cnt++;
                            row = new LTrieRow(this._root);
                            if (ReturnKeyValuePair)
                            {
                                row.ValueStartPointer = valueStartPtr;
                                row.ValueFullLength = valueLength;
                                row.Value = xValue;
                                row.ValueIsReadOut = true;
                            }
                            row.Key = key;
                            row.LinkToValue = kd.Ptr;
                            yield return row;
                        }
                    }
                    else
                    {
                        key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);

                        //Checking if key equals to the found element, bigger or smaller

                        //Key is still not found 

                        if (key.IfStringArrayBiggerOrEqualThen(initialKey))
                        {
                            keyIsFound = true;

                            //case if Startkey doesn't exist, then first encountered value can be calculated as first for skipping
                            if (key.IfStringArrayBiggerThen(initialKey))
                            {
                                skippedCnt++;

                                if (skippedCnt > skippingTotal)
                                {
                                    //key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);

                                    row = new LTrieRow(this._root);
                                    row.Key = key;
                                    row.LinkToValue = kd.Ptr;
                                    yield return row;
                                }
                            }                        
                        }
                    }
                }
                else
                {
                    //special case when from key doesn't exist
                    if (initialKey[0] < kd.Val)
                        keyIsFound = true;

                    gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    //increasing map line must hold already 2 elements
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItFrwSkipFrom(gn1, generationMapLine, useCache))
                    foreach (var xr in ItFrwSkipFrom(gn1, gml, useCache))
                    {
                        //cnt++;      //NEED ONLY FOR SKIP
                        yield return xr;
                    }
                }
            }

            //Console.WriteLine("CNT: {0}", cnt);
        }

        #endregion        

        #region "Skip N then Forward"

        private IEnumerable<LTrieRow> ItFrwSkip(LTrieGenerationNode gn, byte[] generationMapLine, bool useCache)
        {
            byte[] key = null;
            LTrieRow row = null;
            byte[] gml = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;

            foreach (var kd in gn.KidsInNode.GetKidsForward())
            {
                if (kd.ValueKid || !kd.LinkToNode)
                {
                    skippedCnt++;

                    if (skippedCnt > skippingTotal)
                    {
                        //Value Kid                        
                        if (ReturnKeyValuePair)
                        {
                            this._root.Tree.Cache.ReadKeyValue(useCache, kd.Ptr, out valueStartPtr, out valueLength, out key, out xValue);
                        }
                        else
                        {
                            key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                        }

                        //Console.WriteLine("KN: {0}", key.ToBytesString(""));

                        row = new LTrieRow(this._root);
                        if (ReturnKeyValuePair)
                        {
                            row.ValueStartPointer = valueStartPtr;
                            row.ValueFullLength = valueLength;
                            row.Value = xValue;
                            row.ValueIsReadOut = true;
                        }
                        row.Key = key;
                        row.LinkToValue = kd.Ptr;
                        yield return row;
                    }
                }
                else
                {
                    //It's a Link To Node, gettign new generation Node
                    LTrieGenerationNode gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);


                    //foreach (var xr in ItFrwSkip(gn1, generationMapLine, useCache))
                    foreach (var xr in ItFrwSkip(gn1, gml, useCache))
                    {
                        yield return xr;
                    }
                }
            }
        }


        public IEnumerable<LTrieRow> IterateForwardSkip(ulong skippingQuantity, bool useCache)
        {
            LTrieGenerationNode gn = null;

            skippingTotal = skippingQuantity;

            LTrieGenerationMap _generationMap = new LTrieGenerationMap();

            //_generationMap.Clear();

            //if (_generationMap.Count() == 0)
            //{
                //Loading it from Link TO ZERO Pointer
                gn = new LTrieGenerationNode(this._root);
                gn.Pointer = this._root.LinkToZeroNode;
                //gn.Value=0; - default
                _generationMap.Add(0, gn);

                gn.ReadSelf(useCache, _generationMap.GenerateMapNodesValuesUpToIndex(0));
            //}
                 

            byte[] generationMapLine = new byte[1] { 0 };
            byte[] gml = null;
            LTrieGenerationNode gn1 = null;
            byte[] key = null;
            LTrieRow row = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;

            foreach (var kd in gn.KidsInNode.GetKidsForward())
            {
                //Kid can be value link or node link
                //if value link we can count 1 up
                if (kd.ValueKid || !kd.LinkToNode)
                {
                    skippedCnt++;

                    if (skippedCnt > skippingTotal)
                    {
                        if (ReturnKeyValuePair)
                        {
                            this._root.Tree.Cache.ReadKeyValue(useCache, kd.Ptr, out valueStartPtr, out valueLength, out key, out xValue);
                        }
                        else
                        {
                            key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                        }
                        //Console.WriteLine("KN: {0}", key.ToBytesString(""));

                        //cnt++;
                        row = new LTrieRow(this._root);
                        if (ReturnKeyValuePair)
                        {
                            row.ValueStartPointer = valueStartPtr;
                            row.ValueFullLength = valueLength;
                            row.Value = xValue;
                            row.ValueIsReadOut = true;
                        }
                        row.Key = key;
                        row.LinkToValue = kd.Ptr;
                        yield return row;
                    }
                }
                else
                {
                    gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItFrwSkip(gn1, generationMapLine, useCache))
                    foreach (var xr in ItFrwSkip(gn1, gml, useCache))
                    {
                        //cnt++;
                        yield return xr;
                    }
                }
            }

            //Console.WriteLine("CNT: {0}", cnt);
        }


        #endregion



        #region "Iterate Forward StartsWith"


        private IEnumerable<LTrieRow> ItFrwStartsWith(LTrieGenerationNode gn, byte[] generationMapLine, int deep,  bool useCache)
        {
            byte[] key = null;
            LTrieRow row = null;

            byte[] gml = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;


            foreach (var kd in gn.KidsInNode.GetKidsForward())
            {

                //deep corresponds to search key(initialKey) index for compare value
                //deep can be bigger then initKey
                //in small deep kd.Value can represent link to node

                if (deep > (initialKey.Length - 1))
                {
                    //we are bigger then supplied key for search (initialKey)
                    if (kd.ValueKid || !kd.LinkToNode)
                    {
                        //visualize all possible

                        if (ReturnKeyValuePair)
                        {
                            this._root.Tree.Cache.ReadKeyValue(useCache, kd.Ptr, out valueStartPtr, out valueLength, out key, out xValue);
                        }
                        else
                        {
                            key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                        }

                        row = new LTrieRow(this._root);
                        if (ReturnKeyValuePair)
                        {
                            row.ValueStartPointer = valueStartPtr;
                            row.ValueFullLength = valueLength;
                            row.Value = xValue;
                            row.ValueIsReadOut = true;
                        }
                        row.Key = key;
                        row.LinkToValue = kd.Ptr;
                        yield return row;
                    }
                    else
                    {
                        //grow with ability to show all
                        LTrieGenerationNode gn1 = new LTrieGenerationNode(this._root);
                        gn1.Pointer = kd.Ptr;
                        gn1.Value = (byte)kd.Val;
                        gml = generationMapLine.Concat(gn1.Value);
                        gn1.ReadSelf(useCache, gml);

                        foreach (var xr in ItFrwStartsWith(gn1, gml, deep + 1, useCache))
                        {
                            yield return xr;
                        }
                    }
                }
                else
                {
                    //search indexes still cooresponds to data

                    if (kd.Val == initialKey[deep]) //and we can compare every supplied byte with index
                    {
                        if (deep == (initialKey.Length - 1))
                        {
                            //final index length value 
                            if (kd.ValueKid || !kd.LinkToNode)
                            {
                                //visualize

                                if (ReturnKeyValuePair)
                                {
                                    this._root.Tree.Cache.ReadKeyValue(useCache, kd.Ptr, out valueStartPtr, out valueLength, out key, out xValue);
                                }
                                else
                                {
                                    key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                                }

                                row = new LTrieRow(this._root);
                                if (ReturnKeyValuePair)
                                {
                                    row.ValueStartPointer = valueStartPtr;
                                    row.ValueFullLength = valueLength;
                                    row.Value = xValue;
                                    row.ValueIsReadOut = true;
                                }
                                row.Key = key;
                                row.LinkToValue = kd.Ptr;
                                yield return row;
                            }
                            else
                            {
                                //grow with ability to show all
                                LTrieGenerationNode gn1 = new LTrieGenerationNode(this._root);
                                gn1.Pointer = kd.Ptr;
                                gn1.Value = (byte)kd.Val;
                                gml = generationMapLine.Concat(gn1.Value);
                                gn1.ReadSelf(useCache, gml);

                                foreach (var xr in ItFrwStartsWith(gn1, gml, deep + 1, useCache))
                                {
                                    yield return xr;
                                }
                            }
                        }
                        else
                        {
                            //smaller then final index length

                            if (kd.ValueKid || !kd.LinkToNode)
                            {
                                //do nothing in case of ValueKid
                                if (!kd.ValueKid)
                                {
                                    //Link to Value, probably this value suits to us
                                    key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);

                                    if (key.IfStringArrayStartsWith(initialKey))
                                    {
                                        //visualize

                                        row = new LTrieRow(this._root);
                                        row.Key = key;
                                        row.LinkToValue = kd.Ptr;
                                        yield return row;
                                    }
                                }
                            }
                            else
                            {
                                //grow up
                                LTrieGenerationNode gn1 = new LTrieGenerationNode(this._root);
                                gn1.Pointer = kd.Ptr;
                                gn1.Value = (byte)kd.Val;
                                gml = generationMapLine.Concat(gn1.Value);
                                gn1.ReadSelf(useCache, gml);

                                foreach (var xr in ItFrwStartsWith(gn1, gml, deep + 1, useCache))
                                {
                                    yield return xr;
                                }
                            }
                        }


                    }
                    else
                    {
                        //do nothing
                    }
                }

            }
        }

        
        public IEnumerable<LTrieRow> IterateForwardStartsWith(byte[] initKey, bool useCache)
        {
            if (initKey.Length < 1)
                yield break;

            LTrieGenerationNode gn = null;

            initialKey = initKey;


            LTrieGenerationMap _generationMap = new LTrieGenerationMap();

            //if (_generationMap.Count() == 0)
            //{
            //Loading it from Link TO ZERO Pointer
            gn = new LTrieGenerationNode(this._root);
            gn.Pointer = this._root.LinkToZeroNode;
            //gn.Value=0; - default
            _generationMap.Add(0, gn);

            gn.ReadSelf(useCache, _generationMap.GenerateMapNodesValuesUpToIndex(0));
            //}

            //ulong cnt = 0; //NEED ONLY FOR SKIP

            byte[] generationMapLine = new byte[1] { 0 };
            byte[] gml = null;
            LTrieGenerationNode gn1 = null;
            byte[] key = null;
            LTrieRow row = null;


            //Starting from first key. It's interesting inside of RecursiveYieldReturn to look Starting from value
            //If intialKey index already bigger then its own length
            //But for the first must be enough
            foreach (var kd in gn.KidsInNode.GetKidsForward(initialKey[0]))
            {
                if (kd.Val != initKey[0])
                    continue;

                //Console.WriteLine("KN: {0}", key.ToBytesString(""));

                //Kid can be value link or node link
                //if value link we can count 1 up
                if (kd.ValueKid || !kd.LinkToNode)
                {
                    key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);

                    if (key.IfStringArrayStartsWith(initialKey))
                    {
                        //cnt++;
                        row = new LTrieRow(this._root);
                        row.Key = key;
                        row.LinkToValue = kd.Ptr;
                        yield return row;
                    }
                }
                else
                {
                    gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    //increasing map line must hold already 2 elements
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItFrwStartsWith(gn1, generationMapLine, 1, true, useCache))
                    foreach (var xr in ItFrwStartsWith(gn1, gml, 1, useCache))
                    {
                        yield return xr;
                    }
                }
            }

            //Console.WriteLine("CNT: {0}", cnt);
        }


        #endregion


        #region "Iterate Forward StartsWithClosestToPrefix"

        public int PrefixDeep = -1;

        public IEnumerable<LTrieRow> IterateForwardStartsWithClosestToPrefix(byte[] initKey, bool useCache)
        {
            if (initKey.Length < 1)
                return new List<LTrieRow>();

            PrefixDeep = -1;

            this.IterateForwardStartsWith_Prefix_Helper(initKey, useCache);

            if (PrefixDeep == -1)
                return new List<LTrieRow>();

            byte[] newKey = new byte[PrefixDeep + 1];
            Buffer.BlockCopy(initKey, 0, newKey, 0, PrefixDeep + 1);
            return this.IterateForwardStartsWith(newKey, useCache);     

        }


        

        public void IterateForwardStartsWith_Prefix_Helper(byte[] initKey, bool useCache)
        {

            LTrieGenerationNode gn = null;

            initialKey = initKey;


            LTrieGenerationMap _generationMap = new LTrieGenerationMap();

            //Loading it from Link TO ZERO Pointer
            gn = new LTrieGenerationNode(this._root);
            gn.Pointer = this._root.LinkToZeroNode;            
            _generationMap.Add(0, gn);
            gn.ReadSelf(useCache, _generationMap.GenerateMapNodesValuesUpToIndex(0));
            
            byte[] generationMapLine = new byte[1] { 0 };
            byte[] gml = null;
            LTrieGenerationNode gn1 = null;
            byte[] key = null;
            LTrieRow row = null;



            //Starting from first key. It's interesting inside of RecursiveYieldReturn to look Starting from value
            //If intialKey index already bigger then its own length
            //But for the first must be enough
            foreach (var kd in gn.KidsInNode.GetKidsForward(initialKey[0]))
            {
                if (kd.Val != initKey[0])
                    continue;

                //Console.WriteLine(System.Text.Encoding.ASCII.GetString(new byte[] {initKey[0]}));

                //Kid can be value link or node link
                //if value link we can count 1 up
                if (kd.ValueKid || !kd.LinkToNode)
                {
                    PrefixDeep++;
                    //key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);

                    //if (key[0] == initialKey[0])
                    //    lstClosestPrefix.Add(initialKey[0]);
                }
                else
                {
                    gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    //increasing map line must hold already 2 elements
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);

                    PrefixDeep++;

                    foreach (var xr in ItFrwStartsWith_Prefix_Helper(gn1, gml, 1, useCache))
                    {
                        //we must iterate to fill lstClosestPrefix
                    }
                }
            }
           
        }



        private IEnumerable<LTrieRow> ItFrwStartsWith_Prefix_Helper(LTrieGenerationNode gn, byte[] generationMapLine, int deep, bool useCache)
        {
            byte[] key = null;
            LTrieRow row = null;

            byte[] gml = null;



            foreach (var kd in gn.KidsInNode.GetKidsForward())
            {

                //deep corresponds to search key(initialKey) index for compare value
                //deep can be bigger then initKey
                //in small deep kd.Value can represent link to node

                if (deep > (initialKey.Length - 1))
                {
                    //DOING NOTHING

                    //////we are bigger then supplied key for search (initialKey)
                    ////if (kd.ValueKid || !kd.LinkToNode)
                    ////{
                    ////    //visualize all possible

                    ////    key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                    ////    row = new LTrieRow(this._root);
                    ////    row.Key = key;
                    ////    row.LinkToValue = kd.Ptr;
                    ////    yield return row;
                    ////}
                    ////else
                    ////{
                    ////    //grow with ability to show all
                    ////    LTrieGenerationNode gn1 = new LTrieGenerationNode(this._root);
                    ////    gn1.Pointer = kd.Ptr;
                    ////    gn1.Value = (byte)kd.Val;
                    ////    gml = generationMapLine.Concat(gn1.Value);
                    ////    gn1.ReadSelf(useCache, gml);

                    ////    foreach (var xr in ItFrwStartsWith_Prefix_Helper(gn1, gml, deep + 1, useCache))
                    ////    {
                    ////        yield return xr;
                    ////    }
                    ////}
                }
                else
                {
                    //search indexes still cooresponds to data

                    if (kd.Val == initialKey[deep]) //and we can compare every supplied byte with index
                    {
                        if (deep == (initialKey.Length - 1))
                        {
                            //final index length value 
                            if (kd.ValueKid || !kd.LinkToNode)
                            {
                                PrefixDeep++;

                                ////visualize

                                //key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);

                                //row = new LTrieRow(this._root);
                                //row.Key = key;
                                //row.LinkToValue = kd.Ptr;
                                //yield return row;

                               
                            }
                            else
                            {
                                PrefixDeep++;

                                ////grow with ability to show all
                                //LTrieGenerationNode gn1 = new LTrieGenerationNode(this._root);
                                //gn1.Pointer = kd.Ptr;
                                //gn1.Value = (byte)kd.Val;
                                //gml = generationMapLine.Concat(gn1.Value);
                                //gn1.ReadSelf(useCache, gml);

                                //foreach (var xr in ItFrwStartsWith_Prefix_Helper(gn1, gml, deep + 1, useCache))
                                //{
                                //    yield return xr;
                                //}
                            }
                        }
                        else
                        {
                            //smaller then final index length

                            if (kd.ValueKid || !kd.LinkToNode)
                            {
                                //do nothing in case of ValueKid
                                if (!kd.ValueKid)
                                {
                                    PrefixDeep++;
                                    ////Link to Value, probably this value suits to us
                                    //key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);

                                    //if (key.IfStringArrayStartsWith(initialKey))
                                    //{
                                    //    //visualize

                                    //    row = new LTrieRow(this._root);
                                    //    row.Key = key;
                                    //    row.LinkToValue = kd.Ptr;
                                    //    yield return row;
                                    //}
                                    
                                }
                            }
                            else
                            {
                                PrefixDeep++;

                                //grow up
                                LTrieGenerationNode gn1 = new LTrieGenerationNode(this._root);
                                gn1.Pointer = kd.Ptr;
                                gn1.Value = (byte)kd.Val;
                                gml = generationMapLine.Concat(gn1.Value);
                                gn1.ReadSelf(useCache, gml);

                                foreach (var xr in ItFrwStartsWith_Prefix_Helper(gn1, gml, deep + 1, useCache))
                                {
                                   yield return xr;
                                }
                            }
                        }


                    }
                    else
                    {
                        //do nothing
                    }
                }

            }
        }

    
        #endregion
    }
}
