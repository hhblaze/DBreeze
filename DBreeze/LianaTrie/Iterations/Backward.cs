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
    public class Backward
    {
        LTrieRootNode _root = null;

        /// <summary>
        /// If we use load Key already With Value, or just key and link to the value
        /// </summary>
        bool ReturnKeyValuePair = false; 

        public Backward(LTrieRootNode root, bool ValuesLazyLoadingIsOn)
        {
            _root = root;

            //ReturnKeyValuePair = !_root.Tree.ValuesLazyLoadingIsOn;
            ReturnKeyValuePair = !ValuesLazyLoadingIsOn;
        }

        byte[] endKey = null;
        bool includeStartKey = false;
        bool includeStopKey = false;
        bool keyIsFound = false;
        byte[] initialKey = null;
        ulong skippedCnt = 0;
        ulong skippingTotal = 0;

        #region "Iterate Backward"

        private IEnumerable<LTrieRow> ItBwd(LTrieGenerationNode gn, byte[] generationMapLine, bool useCache)
        {
            byte[] key = null;
            LTrieRow row = null;
            byte[] gml = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;

            foreach (var kd in gn.KidsInNode.GetKidsBackward())
            {
                if (kd.ValueKid || !kd.LinkToNode)
                {
                    //Value Kid
                    //Raise Up Counter, iterate further if counter permits

                    row = new LTrieRow(this._root);

                    if (ReturnKeyValuePair)
                    {
                        this._root.Tree.Cache.ReadKeyValue(useCache, kd.Ptr,out valueStartPtr, out valueLength, out key, out xValue);

                        row.ValueStartPointer = valueStartPtr;
                        row.ValueFullLength = valueLength;
                        row.Value = xValue;
                        row.ValueIsReadOut = true;
                    }
                    else
                    {
                        key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                    }
                    //Console.WriteLine("KN: {0}", key.ToBytesString(""));


                    
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

                    //foreach (var xr in ItBwd(gn1, generationMapLine, useCache))
                    foreach (var xr in ItBwd(gn1, gml, useCache))
                    {
                        yield return xr;
                    }
                }
            }
        }



        public IEnumerable<LTrieRow> IterateBackward(bool useCache)
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
            LTrieRow row = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;

            foreach (var kd in gn.KidsInNode.GetKidsBackward())
            {
                //Kid can be value link or node link
                //if value link we can count 1 up
                if (kd.ValueKid || !kd.LinkToNode)
                {
                   
                    //Console.WriteLine("KN: {0}", key.ToBytesString(""));

                    //cnt++;
                    row = new LTrieRow(this._root);

                    if (ReturnKeyValuePair)
                    {
                        this._root.Tree.Cache.ReadKeyValue(useCache, kd.Ptr, out valueStartPtr, out valueLength, out key, out xValue);

                        row.ValueStartPointer = valueStartPtr;
                        row.ValueFullLength = valueLength;
                        row.Value = xValue;
                        row.ValueIsReadOut = true;
                    }
                    else
                    {
                        key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
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

                    //foreach (var xr in ItBwd(gn1, generationMapLine, useCache))
                    foreach (var xr in ItBwd(gn1, gml, useCache))
                    {
                        //cnt++;
                        yield return xr;
                    }
                }
            }

            //Console.WriteLine("CNT: {0}", cnt);
        }

        #endregion

        #region "Iterate Backward StartFrom key"


        private IEnumerable<LTrieRow> ItBwdStartFrom(LTrieGenerationNode gn, byte[] generationMapLine,bool useCache)
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
                startFrom = 255; //will check starting from KidsValue and then 0-255
            }
            else
            {
                //Kid is still not found
                if (generationMapLine.Length > initialKey.Length)
                {
                    startFrom = 256;            //must be 256        
                }
                else
                {
                    startFrom = initialKey[generationMapLine.Length - 1];
                }
            }

            foreach (var kd in gn.KidsInNode.GetKidsBackward(startFrom))
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

                        //Key is still not found 

                        if ((includeStartKey) ? key.IfStringArraySmallerOrEqualThen(initialKey) : key.IfStringArraySmallerThen(initialKey))
                        //if (key.IfStringArraySmallerThen(initialKey))
                        //if (IfFirstKeyIsSmallerThenCompareKey(key, initialKey))
                        {
                            keyIsFound = true;                            

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
                    }
                }
                else
                {
                    if (!keyIsFound && startFrom != 256 && startFrom > kd.Val)
                        keyIsFound = true;

                    //It's a Link To Node, gettign new generation Node
                    LTrieGenerationNode gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItBwdStartFrom(gn1, generationMapLine, useCache))
                    foreach (var xr in ItBwdStartFrom(gn1, gml, useCache))
                    {
                        yield return xr;
                    }
                }
            }
        }


        public IEnumerable<LTrieRow> IterateBackwardStartFrom(byte[] initKey, bool inclStartKey, bool useCache)
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
            foreach (var kd in gn.KidsInNode.GetKidsBackward(initialKey[0]))
            {
                //Console.WriteLine("KN: {0}", key.ToBytesString(""));

                //Kid can be value link or node link
                //if value link we can count 1 up
                if (kd.ValueKid || !kd.LinkToNode)
                {
                    

                    if (keyIsFound)
                    {
                        //We return this one key

                        //cnt++;
                        row = new LTrieRow(this._root);
                        if (ReturnKeyValuePair)
                        {
                            this._root.Tree.Cache.ReadKeyValue(useCache, kd.Ptr, out valueStartPtr, out valueLength, out key, out xValue);

                            row.ValueStartPointer = valueStartPtr;
                            row.ValueFullLength = valueLength;
                            row.Value = xValue;
                            row.ValueIsReadOut = true;
                        }
                        else
                        {
                            key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                        }
                       
                        row.Key = key;
                        row.LinkToValue = kd.Ptr;
                        yield return row;
                    }
                    else
                    {
                        if (ReturnKeyValuePair)
                        {
                            this._root.Tree.Cache.ReadKeyValue(useCache, kd.Ptr, out valueStartPtr, out valueLength, out key, out xValue);
                        }
                        else
                        {
                            key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                        }
                        //Checking if key equals to the found element, bigger or smaller

                        //Key is still not found 

                        if ((includeStartKey) ? key.IfStringArraySmallerOrEqualThen(initialKey) : key.IfStringArraySmallerThen(initialKey))
                        //if(key.IfStringArraySmallerThen(initialKey))
                        //if (IfFirstKeyIsSmallerThenCompareKey(key, initialKey))
                        {
                            keyIsFound = true;

                            //We return this one key
                            //We dont apply reading key with value here, using LazyLoading
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

                            //going on iteration
                        }
                    }
                }
                else
                {
                    if (!keyIsFound && initialKey[0] > kd.Val)
                        keyIsFound = true;
                    
                    gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    //increasing map line must hold already 2 elements
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItBwdStartFrom(gn1, generationMapLine, useCache))
                    foreach (var xr in ItBwdStartFrom(gn1, gml, useCache))
                    {
                        //cnt++;      //NEED ONLY FOR SKIP
                        yield return xr;
                    }
                }
            }

            //Console.WriteLine("CNT: {0}", cnt);
        }


        #endregion

        #region "Iterate Backward From-To Key"


        /// <summary>
        /// ItBwdFromTo
        /// </summary>
        /// <param name="gn"></param>
        /// <param name="generationMapLine"></param>
        /// <param name="useCache"></param>
        /// <returns></returns>
        private IEnumerable<LTrieRow> ItBwdFromTo(LTrieGenerationNode gn, byte[] generationMapLine, bool useCache)
        {
            byte[] key = null;
            LTrieRow row = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;

            byte[] gml = null;

            int startFrom = 0;
            if (keyIsFound)
            {
                startFrom = 255; //will check starting from KidsValue and then 0-255
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

            foreach (var kd in gn.KidsInNode.GetKidsBackward(startFrom))
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

                        if ((includeStopKey) ? key.IfStringArrayBiggerOrEqualThen(endKey) : key.IfStringArrayBiggerThen(endKey))
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

                        if ((includeStartKey) ? key.IfStringArraySmallerOrEqualThen(initialKey) : key.IfStringArraySmallerThen(initialKey))
                        {
                            keyIsFound = true;

                            if ((includeStopKey) ? key.IfStringArrayBiggerOrEqualThen(endKey) : key.IfStringArrayBiggerThen(endKey))
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
                    }
                }
                else
                {
                    if (!keyIsFound && startFrom != 256 && startFrom > kd.Val)
                        keyIsFound = true;

                    //It's a Link To Node, gettign new generation Node
                    LTrieGenerationNode gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItBwdFromTo(gn1, generationMapLine, useCache))
                    foreach (var xr in ItBwdFromTo(gn1, gml, useCache))
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
        /// IterateBackwardFromTo
        /// </summary>
        /// <param name="initKey"></param>
        /// <param name="stopKey"></param>
        /// <param name="inclStartKey"></param>
        /// <param name="inclStopKey"></param>
        /// <param name="useCache"></param>
        /// <returns></returns>
        public IEnumerable<LTrieRow> IterateBackwardFromTo(byte[] initKey, byte[] stopKey, bool inclStartKey, bool inclStopKey, bool useCache)
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
            foreach (var kd in gn.KidsInNode.GetKidsBackward(initialKey[0]))
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


                        if ((includeStopKey) ? key.IfStringArrayBiggerOrEqualThen(endKey) : key.IfStringArrayBiggerThen(endKey))
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

                        if ((includeStartKey) ? key.IfStringArraySmallerOrEqualThen(initialKey) : key.IfStringArraySmallerThen(initialKey))
                        {
                            keyIsFound = true;


                            if ((includeStopKey) ? key.IfStringArrayBiggerOrEqualThen(endKey) : key.IfStringArrayBiggerThen(endKey))
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
                                break;

                            //going on iteration
                        }
                    }
                }
                else
                {
                    if (!keyIsFound && initialKey[0] > kd.Val)
                        keyIsFound = true;

                    gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    //increasing map line must hold already 2 elements
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItBwdFromTo(gn1, generationMapLine, useCache))
                    foreach (var xr in ItBwdFromTo(gn1, gml, useCache))
                    {
                        if (xr == null)
                        {
                            break;
                        }
                        yield return xr;
                    }
                }
            }

        }


        #endregion

        #region "Get Maximal Key"

        private IEnumerable<LTrieRow> ItBwdForMaximal(LTrieGenerationNode gn, byte[] generationMapLine, bool useCache)
        {
            byte[] key = null;
            LTrieRow row = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;
            byte[] gml = null;

            foreach (var kd in gn.KidsInNode.GetKidsBackward())
            {
                if (kd.ValueKid || !kd.LinkToNode)
                {
                    //Value Kid
                    //Raise Up Counter, iterate further if counter permits                    
                    //Console.WriteLine("KN: {0}", key.ToBytesString(""));

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
                    //It's a Link To Node, gettign new generation Node
                    LTrieGenerationNode gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    foreach (var xr in ItBwdForMaximal(gn1, gml, useCache))
                    //foreach (var xr in ItBwdForMaximal(gn1, generationMapLine, useCache))
                    {
                        yield return xr;
                    }
                }
            }
        }



        public LTrieRow IterateBackwardForMaximal(bool useCache)
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

            foreach (var kd in gn.KidsInNode.GetKidsBackward())
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

                    //foreach (var xr in ItBwdForMaximal(gn1, generationMapLine, useCache))
                    foreach (var xr in ItBwdForMaximal(gn1, gml, useCache))
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

        #region "Skip N Backward StartFrom key then Backward"



        //bool keyIsFound = false;

        private IEnumerable<LTrieRow> ItBwdSkipFrom(LTrieGenerationNode gn, byte[] generationMapLine, bool useCache)
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
                startFrom = 255; //will check starting from KidsValue and then 0-255
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

            foreach (var kd in gn.KidsInNode.GetKidsBackward(startFrom))
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

                        if (key.IfStringArraySmallerOrEqualThen(initialKey))
                        //if (key.IfStringArraySmallerThen(initialKey))
                        //if (IfFirstKeyIsSmallerThenCompareKey(key, initialKey))
                        {
                            keyIsFound = true;

                            //case if Startkey doesn't exist, then first encountered value can be calculated as first for skipping
                            if (key.IfStringArraySmallerThen(initialKey))
                            {
                                skippedCnt++;

                                if (skippedCnt > skippingTotal)
                                {
                                   // key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);

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
                    //It's a Link To Node, gettign new generation Node
                    LTrieGenerationNode gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItBwdSkipFrom(gn1, generationMapLine, useCache))
                    foreach (var xr in ItBwdSkipFrom(gn1, gml, useCache))
                    {
                        yield return xr;
                    }
                }
            }
        }


        public IEnumerable<LTrieRow> IterateBackwardSkipFrom(byte[] initKey, ulong skippingQuantity, bool useCache)
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
            foreach (var kd in gn.KidsInNode.GetKidsBackward(initialKey[0]))
            {
                //Console.WriteLine("KN: {0}", key.ToBytesString(""));

                //Kid can be value link or node link
                //if value link we can count 1 up
                if (kd.ValueKid || !kd.LinkToNode)
                {
                    

                    if (keyIsFound)
                    {
                        //We return this one key

                        //cnt++;
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

                        if (key.IfStringArraySmallerOrEqualThen(initialKey))
                        //if (key.IfStringArraySmallerThen(initialKey))
                        //if (IfFirstKeyIsSmallerThenCompareKey(key, initialKey))
                        {
                            keyIsFound = true;

                            //case if Startkey doesn't exist, then first encountered value can be calculated as first for skipping
                            if (key.IfStringArraySmallerThen(initialKey))
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
                    gn1 = new LTrieGenerationNode(this._root);
                    gn1.Pointer = kd.Ptr;
                    gn1.Value = (byte)kd.Val;
                    //increasing map line must hold already 2 elements
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItBwdSkipFrom(gn1, generationMapLine, useCache))
                    foreach (var xr in ItBwdSkipFrom(gn1, gml, useCache))
                    {
                        //cnt++;      //NEED ONLY FOR SKIP
                        yield return xr;
                    }
                }
            }

            //Console.WriteLine("CNT: {0}", cnt);
        }


        #endregion

        #region "Skip Backward N then Iterate Backward"

        private IEnumerable<LTrieRow> ItBwdSkip(LTrieGenerationNode gn, byte[] generationMapLine, bool useCache)
        {
            byte[] key = null;
            LTrieRow row = null;
            byte[] gml = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;

            foreach (var kd in gn.KidsInNode.GetKidsBackward())
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

                    //foreach (var xr in ItBwdSkip(gn1, generationMapLine, useCache))
                    foreach (var xr in ItBwdSkip(gn1, gml, useCache))
                    {
                        yield return xr;
                    }
                }
            }
        }



        public IEnumerable<LTrieRow> IterateBackwardSkip(ulong skippingQuantity, bool useCache)
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

            //ulong cnt = 0;

            byte[] generationMapLine = new byte[1] { 0 };
            byte[] gml = null;
            LTrieGenerationNode gn1 = null;
            byte[] key = null;
            LTrieRow row = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;

            foreach (var kd in gn.KidsInNode.GetKidsBackward())
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

                    //foreach (var xr in ItBwdSkip(gn1, generationMapLine, useCache))
                    foreach (var xr in ItBwdSkip(gn1, gml, useCache))
                    {
                        //cnt++;
                        yield return xr;
                    }
                }
            }

            //Console.WriteLine("CNT: {0}", cnt);
        }

        #endregion




        #region "Iterate Backward StartWith"

        private IEnumerable<LTrieRow> ItBwdStartsWith(LTrieGenerationNode gn, byte[] generationMapLine, int deep, bool useCache)
        {
            byte[] key = null;
            LTrieRow row = null;

            byte[] gml = null;
            long valueStartPtr = 0;
            uint valueLength = 0;
            byte[] xValue = null;   


            foreach (var kd in gn.KidsInNode.GetKidsBackward())
            {

                //deep corresponds to search key(initialKey) index for compare value
                //deep can be bigger then initKey
                //in small deep kd.Value can represent link to node

                if (deep > (initialKey.Length-1))
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
                        row.Key = key;
                        if (ReturnKeyValuePair)
                        {
                            row.ValueStartPointer = valueStartPtr;
                            row.ValueFullLength = valueLength;
                            row.Value = xValue;
                            row.ValueIsReadOut = true;
                        }
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

                        foreach (var xr in ItBwdStartsWith(gn1, gml, deep + 1,  useCache))
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

                                foreach (var xr in ItBwdStartsWith(gn1, gml, deep + 1, useCache))
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

                                foreach (var xr in ItBwdStartsWith(gn1, gml, deep + 1, useCache))
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



        public IEnumerable<LTrieRow> IterateBackwardStartsWith(byte[] initKey, bool useCache)
        {
            if (initKey.Length < 1)
                yield break;

            LTrieGenerationNode gn = null;

            initialKey = initKey;

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
            LTrieRow row = null;

            //bool kFnd = false;

            foreach (var kd in gn.KidsInNode.GetKidsBackward(initialKey[0]))
            {
                //For first linke only
                if (kd.Val != initKey[0])
                    continue;

                //Kid can be value link or node link
                //if value link we can count 1 up
                if (kd.ValueKid || !kd.LinkToNode)
                {
                    key = this._root.Tree.Cache.ReadKey(useCache, kd.Ptr);
                    //Console.WriteLine("KN: {0}", key.ToBytesString(""));

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
                    gml = generationMapLine.Concat(gn1.Value);
                    gn1.ReadSelf(useCache, gml);
                    //generationMapLine = generationMapLine.Concat(gn1.Value);
                    //gn1.ReadSelf(useCache, generationMapLine);

                    //foreach (var xr in ItBwdStartsWith(gn1, generationMapLine, 1, true, useCache))
                    foreach (var xr in ItBwdStartsWith(gn1, gml, 1, useCache))
                    {
                        //cnt++;
                        yield return xr;
                    }
                }
            }

            //Console.WriteLine("CNT: {0}", cnt);
        }
        

        #endregion

        #region "Iterate Backward StartsWithClosestToPrefix"


        public IEnumerable<LTrieRow> IterateBackwardStartsWithClosestToPrefix(byte[] initKey, bool useCache)
        {
            if(initKey.Length<1)
                return new List<LTrieRow>();

            Forward fw = new Forward(this._root,!this.ReturnKeyValuePair);
            fw.IterateForwardStartsWith_Prefix_Helper(initKey, useCache);

            if (fw.PrefixDeep == -1)
                return new List<LTrieRow>();

            byte[] newKey = new byte[fw.PrefixDeep + 1];
            Buffer.BlockCopy(initKey, 0, newKey, 0, fw.PrefixDeep + 1);
            return this.IterateBackwardStartsWith(newKey, useCache);     
        }


        #endregion
    }

}
