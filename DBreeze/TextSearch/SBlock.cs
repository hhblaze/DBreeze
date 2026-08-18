/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;

namespace DBreeze.TextSearch
{
    /// <summary>
    /// 
    /// </summary>
    public class BlockAnd : SBlock
    {
        internal BlockAnd()
        {
            
        }

        /// <summary>
        /// Generates a logical block: 
        /// var tsm = tran.TextSearch("MyTextSearchTable");
        /// tsm.BlockAnd("pill").OR(new DBreeze.TextSearch.BlockAnd("blue", "#LNDDE"))
        /// .GetDocumentIDs
        /// </summary>
        /// <param name="containsWords">space separated words to be used by "contains" logic</param>
        /// <param name="fullMatchWords">space separated words to be used by "full-match" logic</param>
        public BlockAnd(string containsWords, string fullMatchWords = "")
            :this()
        {
            this._containsWords = containsWords;
            this._fullMatchWords = fullMatchWords;
            this.InternalBlockOperation = SBlock.eOperation.AND;
            this.IsLogicalBlock = false;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public class BlockOr : SBlock
    {
        internal BlockOr()
        {
           
        }

        /// <summary>
        /// Generates a logical block: 
        /// var tsm = tran.TextSearch("MyTextSearchTable");
        /// tsm.BlockAnd("pill").OR(new DBreeze.TextSearch.BlockOr("blue red", "#LNDDE"))
        /// .GetDocumentIDs
        /// </summary>
        /// <param name="containsWords">space separated words to be used by "contains" logic</param>
        /// <param name="fullMatchWords">space separated words to be used by "full-match" logic</param>
        public BlockOr(string containsWords, string fullMatchWords = "")
            : this()
        {
            this._containsWords = containsWords;
            this._fullMatchWords = fullMatchWords;
            this.InternalBlockOperation = SBlock.eOperation.OR;
            this.IsLogicalBlock = false;
        }
    }

    internal class BlockXOR : SBlock
    {
        public BlockXOR()
        {
          
        }
    }

    internal class BlockEXCLUDE : SBlock
    {
        public BlockEXCLUDE()
        {
           
        }
    }


    /// <summary>
    /// Text search block
    /// </summary>
    public abstract class SBlock
    {
        internal enum eOperation
        {
            /// <summary>
            /// Possible in and between blocks
            /// </summary>
            AND,
            /// <summary>
            /// Possible in and between blocks
            /// </summary>
            OR,
            /// <summary>
            /// Possible only between blocks
            /// </summary>
            XOR,
            /// <summary>
            /// Possible only between blocks
            /// </summary>
            EXCLUDE,
            NONE
        }

        /// <summary>
        /// Block will not be counted in intersection calculations if has empty FullMatch and Contains words
        /// </summary>
        internal bool Ignored = false;
        internal string _containsWords = "";
        internal string _fullMatchWords = "";

        /// <summary>
        /// TextSearchManager
        /// </summary>
        internal TextSearchTable _tsm = null;
        /// <summary>
        /// Word, fullMatch  
        /// </summary>
        internal Dictionary<string, bool> ParsedWords = new Dictionary<string, bool>();

        /// <summary>
        /// 
        /// </summary>
        internal int BlockId = 0;
        internal int LeftBlockId = -1;
        internal int RightBlockId = -1;
        internal eOperation TransBlockOperation = eOperation.OR;
        /// <summary>
        /// This block is a result of operation between 2 other blocks.
        /// False is a pure block added via TextSearchManager
        /// </summary>
        internal bool IsLogicalBlock = true;

        /// <summary>
        /// Internal in-block operation between words.
        /// Generation-term
        /// </summary>
        internal eOperation InternalBlockOperation = eOperation.AND;

        /// <summary>
        /// Generation-term
        /// </summary>
        List<byte[]> foundArrays = new List<byte[]>();
        bool foundArraysAreComputed = false;

        #region "Between block operations"

        SBlock CreateBlock(SBlock block, eOperation operation, bool ignoreOnEmptyParameters = false)
        {
            if (_tsm == null)
                throw new Exception("DBreeze.Exception: first search block must be added via TextSearchTable");

            if (block == null)
                throw new ArgumentNullException("block");

            if (block._tsm != null && !Object.ReferenceEquals(block._tsm, this._tsm))
                throw new ArgumentException("DBreeze.TextSearch: blocks from different TextSearchTable instances cannot be combined", "block");

            if (block._tsm == null)
            {
                //Creating real block
                block._tsm = this._tsm;
                block.BlockId = this._tsm.cntBlockId++;
                this._tsm.WordsPrepare(block._fullMatchWords.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(r => r.Length > 2), true, ref block.ParsedWords);
                this._tsm.WordsPrepare(block._containsWords.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(r => r.Length > 2), false, ref block.ParsedWords);
                this._tsm.toComputeWordsOrigin = true;
                this._tsm.Blocks[block.BlockId] = block;
            }

            // ParsedWords is authoritative here: it also works for enumerable-created blocks,
            // whitespace-only input and reusable blocks already attached to the table.
            if (ignoreOnEmptyParameters && (block.Ignored || block.ParsedWords.Count == 0))
                return this;

            //Creating logical block
            SBlock b = null;
            switch (operation)
            {
                case eOperation.AND:
                    b = new BlockAnd();
                    break;
                case eOperation.OR:
                    b = new BlockOr();
                    break;
                case eOperation.XOR:
                    b = new BlockXOR();
                    break;
                case eOperation.EXCLUDE:
                    b = new BlockEXCLUDE();
                    break;             
            }

            b._tsm = this._tsm;
            b.BlockId = this._tsm.cntBlockId++;
            b.LeftBlockId = BlockId;
            b.RightBlockId = block.BlockId;
            b.TransBlockOperation = operation;

            //SBlock b = new SBlock()
            //{
            //    _tsm = this._tsm,
            //    BlockId = this._tsm.cntBlockId++,
            //    LeftBlockId = this.BlockId,
            //    RightBlockId = block.BlockId,
            //    TransBlockOperation = operation
            //};

            this._tsm.Blocks[b.BlockId] = b;

            return b;
        }


        /// <summary>
        /// Adding new logical block (And or Or, depending upon parameter blockAnd)
        /// </summary>
        /// <param name="containsWords">space separated words to be used by "contains" logic</param>
        /// <param name="fullMatchWords">space separated words to be used by "full-match" logic</param>
        /// <param name="blockAnd">default value is true, indicating and block</param>
        /// <param name="ignoreOnEmptyParameters">Block will not be counted in intersection calculations if has empty FullMatch and Contains words</param>
        /// <returns></returns>
        public SBlock And(string containsWords, string fullMatchWords = "", bool blockAnd=true, bool ignoreOnEmptyParameters = false)
        {
            if(this.Ignored)
                return this.CreateBlock(blockAnd ? (SBlock)(new BlockAnd(containsWords, fullMatchWords)) : (new BlockOr(containsWords, fullMatchWords)), 
                    eOperation.OR, ignoreOnEmptyParameters);

            return this.CreateBlock(blockAnd ? (SBlock)(new BlockAnd(containsWords, fullMatchWords)) : (new BlockOr(containsWords, fullMatchWords)), 
                eOperation.AND, ignoreOnEmptyParameters);
        }
        /// <summary>
        /// Adding new logical block (And or Or, depending upon parameter blockAnd)
        /// </summary>
        /// <param name="containsWords">space separated words to be used by "contains" logic</param>
        /// <param name="fullMatchWords">space separated words to be used by "full-match" logic</param>
        /// <param name="blockAnd">default value is true, indicating and block</param>
        /// <param name="ignoreOnEmptyParameters">Block will not be counted in intersection calculations if has empty FullMatch and Contains words</param>
        /// <returns></returns>
        public SBlock Or(string containsWords, string fullMatchWords = "", bool blockAnd = true, bool ignoreOnEmptyParameters = false)
        {
            return this.CreateBlock(blockAnd ? (SBlock)(new BlockAnd(containsWords, fullMatchWords)) : (new BlockOr(containsWords, fullMatchWords)), 
                eOperation.OR, ignoreOnEmptyParameters);
        }
        /// <summary>
        /// Adding new logical block (And or Or, depending upon parameter blockAnd)
        /// </summary>
        /// <param name="containsWords">space separated words to be used by "contains" logic</param>
        /// <param name="fullMatchWords">space separated words to be used by "full-match" logic</param>
        /// <param name="blockAnd">default value is true, indicating and block</param>
        /// <param name="ignoreOnEmptyParameters">Block will not be counted in intersection calculations if has empty FullMatch and Contains words</param>
        /// <returns></returns>
        public SBlock Xor(string containsWords, string fullMatchWords = "", bool blockAnd = true, bool ignoreOnEmptyParameters = false)
        {
            if (this.Ignored)
                return this.CreateBlock(blockAnd ? (SBlock)(new BlockAnd(containsWords, fullMatchWords)) : (new BlockOr(containsWords, fullMatchWords)),
                    eOperation.OR, ignoreOnEmptyParameters);

            return this.CreateBlock(blockAnd ? (SBlock)(new BlockAnd(containsWords, fullMatchWords)) : (new BlockOr(containsWords, fullMatchWords)), 
                eOperation.XOR, ignoreOnEmptyParameters);
        }
        /// <summary>
        /// Adding new logical block (And or Or, depending upon parameter blockAnd)
        /// </summary>
        /// <param name="containsWords">space separated words to be used by "contains" logic</param>
        /// <param name="fullMatchWords">space separated words to be used by "full-match" logic</param>
        /// <param name="blockAnd">default value is true, indicating and block</param>
        /// <param name="ignoreOnEmptyParameters">Block will not be counted in intersection calculations if has empty FullMatch and Contains words</param>
        /// <returns></returns>
        public SBlock Exclude(string containsWords, string fullMatchWords = "", bool blockAnd = true, bool ignoreOnEmptyParameters = false)
        {
            if (this.Ignored)
                return this.CreateBlock(blockAnd ? (SBlock)(new BlockAnd(containsWords, fullMatchWords)) : (new BlockOr(containsWords, fullMatchWords)),
                    eOperation.OR, ignoreOnEmptyParameters);

            return this.CreateBlock(blockAnd ? (SBlock)(new BlockAnd(containsWords, fullMatchWords)) : (new BlockOr(containsWords, fullMatchWords)), 
                eOperation.EXCLUDE, ignoreOnEmptyParameters);
        }



        /// <summary>
        /// Returns last added block. Can be added existing block or new block in format
        /// new DBreeze.TextSearch.BlockAnd(... or new DBreeze.TextSearch.BlockOr(
        /// </summary>
        /// <param name="block"></param>
        /// <param name="ignoreOnEmptyParameters">Block will not be counted in intersection calculations if has empty FullMatch and Contains words</param>
        /// <returns></returns>
        public SBlock And(SBlock block, bool ignoreOnEmptyParameters = false)
        {
            if (this.Ignored)
                return this.CreateBlock(block, eOperation.OR, ignoreOnEmptyParameters);

            return this.CreateBlock(block, eOperation.AND, ignoreOnEmptyParameters);
        }


        /// <summary>
        /// Returns last added block. Can be added existing block or new block in format
        /// new DBreeze.TextSearch.BlockAnd(... or new DBreeze.TextSearch.BlockOr(
        /// </summary>
        /// <param name="block"></param>
        /// <param name="ignoreOnEmptyParameters">Block will not be counted in intersection calculations if has empty FullMatch and Contains words</param>
        /// <returns></returns>
        public SBlock Or(SBlock block, bool ignoreOnEmptyParameters = false)
        {
            return this.CreateBlock(block, eOperation.OR, ignoreOnEmptyParameters);
        }

        /// <summary>
        /// Returns last added block. Can be added existing block or new block in format
        /// new DBreeze.TextSearch.BlockAnd(... or new DBreeze.TextSearch.BlockOr(
        /// </summary>
        /// <param name="block"></param>
        /// <param name="ignoreOnEmptyParameters">Block will not be counted in intersection calculations if has empty FullMatch and Contains words</param>
        /// <returns></returns>
        public SBlock Xor(SBlock block, bool ignoreOnEmptyParameters = false)
        {
            if (this.Ignored)
                return this.CreateBlock(block, eOperation.OR, ignoreOnEmptyParameters);

            return this.CreateBlock(block, eOperation.XOR, ignoreOnEmptyParameters);
        }

        /// <summary>
        /// Returns last added block. Can be added existing block or new block in format
        /// new DBreeze.TextSearch.BlockAnd(... or new DBreeze.TextSearch.BlockOr(
        /// </summary>
        /// <param name="block"></param>
        /// <param name="ignoreOnEmptyParameters">Block will not be counted in intersection calculations if has empty FullMatch and Contains words</param>
        /// <returns></returns>
        public SBlock Exclude(SBlock block, bool ignoreOnEmptyParameters = false)
        {
            if (this.Ignored)
                return this.CreateBlock(block, eOperation.OR, ignoreOnEmptyParameters);

            return this.CreateBlock(block, eOperation.EXCLUDE, ignoreOnEmptyParameters);
        }
        #endregion

        /// <summary>
        /// IEnumerable returning External GetDocumentIDs
        /// </summary>
        /// <returns></returns>
        public IEnumerable<byte[]> GetDocumentIDs()
        {
            if (this._tsm == null)
                throw new InvalidOperationException("DBreeze.TextSearch: the first search block must be created via TextSearchTable");

            this._tsm.ComputeWordsOrigin();

            //New Logical block is always a result of operation between 2 blocks
            //Usual block is added via TextSearchManager

            if (!this._tsm.ResolveDocumentRange())
                yield break;

            var myArray = this.GetArrays();
            if (myArray.Count != 0)
            {
                DBreeze.DataTypes.Row<int, byte[]> docRow = null;
                //foreach (var el in WABI.TextSearch_AND_logic(myArray))

                var q = WABI.TextSearch_AND_logic(myArray);

                if (this._tsm.DocIdA > 0 || this._tsm.DocIdZ > 0 || !this._tsm.Descending)
                    q = WABI.TextSearch_AND_logic(myArray, this._tsm.DocIdA, this._tsm.DocIdZ, this._tsm.Descending);

                foreach (var el in q)
                {
                    //Getting document external ID
                    docRow = this._tsm.i2e.Select<int, byte[]>((int)el);
                    if (docRow.Exists)
                        yield return docRow.Value;
                }
            }
        }

        /// <summary>
        /// Universal function resulting final one/many arrays (in AND) and one array in (OR, XOR, EXCLUDE)
        /// </summary>
        internal List<byte[]> GetArrays()
        {
            if (this.foundArraysAreComputed)
                return this.foundArrays;

            if (!this.IsLogicalBlock)
            {
                //Real blocks
                this.GetPureBlockArrays();
                return this.foundArrays;
            }

            var left = this._tsm.Blocks[this.LeftBlockId];
            var right = this._tsm.Blocks[this.RightBlockId];

            var la = left.GetArrays();
            var ra = right.GetArrays();

            byte[] mrg = null;

            if (this.TransBlockOperation != eOperation.AND)
            {
                //If we change operation "sign" from AND to smth.else, we need to bring our left-right 
                //arrays to 1 element, to perform operations between them

                if (la.Count > 1)
                {
                    mrg = WABI.MergeByAndLogic(la);
                    la = mrg == null ? new List<byte[]>() : new List<byte[]> { mrg };
                }

                if (ra.Count > 1)
                {
                    mrg = WABI.MergeByAndLogic(ra);
                    ra = mrg == null ? new List<byte[]>() : new List<byte[]> { mrg };
                }
            }

            var result = new List<byte[]>();
            switch (this.TransBlockOperation)
            {
                case eOperation.AND:
                    if (la != null && ra != null && la.Count != 0 && ra.Count != 0)
                    {
                        result.Capacity = la.Count + ra.Count;
                        result.AddRange(la);
                        result.AddRange(ra);
                    }
                    break;

                case eOperation.OR:
                    mrg = WABI.MergeByOrLogic(CombineArrays(la, ra));
                    if (mrg != null)
                        result.Add(mrg);
                    break;

                case eOperation.XOR:
                    mrg = WABI.MergeByXorLogic(CombineArrays(la, ra));
                    if (mrg != null)
                        result.Add(mrg);
                    break;

                case eOperation.EXCLUDE:
                    if (la != null && la.Count != 0)
                    {
                        if (ra == null || ra.Count == 0)
                            mrg = la[0];
                        else
                            mrg = WABI.MergeByExcludeLogic(la[0], ra[0]);

                        if (mrg != null)
                            result.Add(mrg);
                    }
                    break;
            }

            // Publish the cache only after the whole computation succeeded. A parser/database
            // exception must not leave a permanently poisoned logical block.
            this.foundArrays = result;
            this.foundArraysAreComputed = true;
            return this.foundArrays;
        }

        static List<byte[]> CombineArrays(List<byte[]> left, List<byte[]> right)
        {
            int leftCount = left == null ? 0 : left.Count;
            int rightCount = right == null ? 0 : right.Count;
            var result = new List<byte[]>(leftCount + rightCount);
            if (leftCount != 0)
                result.AddRange(left);
            if (rightCount != 0)
                result.AddRange(right);
            return result;
        }

        /// <summary>
        /// Fills up foundArrays for the current block. If logic is And and word is not found can clear already array on that level.
        /// Concenrs only pure (not logical) blocks
        /// </summary>
        /// <returns></returns>
        void GetPureBlockArrays()
        {
            if (foundArraysAreComputed)
                return;

            var result = new List<byte[]>();
            List<byte[]> echoes = null;

            foreach (var wrd in this.ParsedWords)
            {
                if (!this._tsm.PureWords.ContainsKey(wrd.Key))  //Wrong input
                    continue;

                //if(wrd.Value) FullMatch
                switch (this.InternalBlockOperation)
                {
                    case eOperation.AND:
                        if (wrd.Value)
                        { //Word must be FullMatched and doesn't 
                            if (!this._tsm.RealWords.ContainsKey(wrd.Key))      //!!No match
                            {
                                //Found arrays must be cleared out
                                result.Clear();
                                this.foundArrays = result;
                                this.foundArraysAreComputed = true;
                                return; //Parsed Words
                            }
                            else//Adding word to block array
                                result.Add(this._tsm.RealWords[wrd.Key].wahArray);
                        }
                        else
                        { //Value must have contains
                            echoes = new List<byte[]>();
                            foreach (var conw in this._tsm.PureWords[wrd.Key].StartsWith)   //Adding all pure word StartsWith echoes
                                if (this._tsm.RealWords.ContainsKey(conw))
                                    echoes.Add(this._tsm.RealWords[conw].wahArray);

                            if (this._tsm.RealWords.ContainsKey(wrd.Key))  //And word itself
                                echoes.Add(this._tsm.RealWords[wrd.Key].wahArray);

                            if (echoes.Count > 0)
                                result.Add(WABI.MergeByOrLogic(echoes));  //Echoes must be merged by OrLogic
                            else
                            {
                                //Found arrays must be cleared out
                                result.Clear();
                                this.foundArrays = result;
                                this.foundArraysAreComputed = true;
                                return; //Parsed Words
                            }
                        }
                        break;
                    case eOperation.OR:
                        if (wrd.Value)
                        {
                            if (this._tsm.RealWords.ContainsKey(wrd.Key))  //And word itself
                                result.Add(this._tsm.RealWords[wrd.Key].wahArray);
                        }
                        else
                        {
                            echoes = new List<byte[]>();
                            foreach (var conw in this._tsm.PureWords[wrd.Key].StartsWith)   //Adding all pure word StartsWith echoes
                                if (this._tsm.RealWords.ContainsKey(conw))
                                    echoes.Add(this._tsm.RealWords[conw].wahArray);

                            if (this._tsm.RealWords.ContainsKey(wrd.Key))  //And word itself
                                echoes.Add(this._tsm.RealWords[wrd.Key].wahArray);

                            if (echoes.Count > 0)
                                result.Add(WABI.MergeByOrLogic(echoes));
                        }
                        break;
                }

            }//eo of parsedWords

            if (this.InternalBlockOperation == eOperation.OR)
            {
                byte[] merged = WABI.MergeByOrLogic(result);
                result.Clear();
                if (merged != null)
                    result.Add(merged);
            }

            this.foundArrays = result;
            this.foundArraysAreComputed = true;

        }//eo GetArrays

    }
}
