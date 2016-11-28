/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze;
using DBreeze.Utils;
using DBreeze.DataTypes;

namespace DBreeze.TextSearch
{
    /// <summary>
    /// Text search block
    /// </summary>
    public class SBlock
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
        /// TextSearchManager
        /// </summary>
        internal TextSearchTable _tsm = null;
        /// <summary>
        /// Word, fullMatch  
        /// </summary>
        internal Dictionary<string,bool> ParsedWords = new Dictionary<string, bool>();

        /// <summary>
        /// 
        /// </summary>
        internal int BlockId = 0;
        internal int LeftBlockId = 0;
        internal int RightBlockId = 0;
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

        SBlock CreateBlock(SBlock block, eOperation operation)
        {
            if (_tsm == null)
                throw new Exception("DBreeze.Exception: search block must be added via TextSearchMangager");

            SBlock b = new SBlock()
            {
                _tsm = this._tsm,
                BlockId = this._tsm.cntBlockId++,
                LeftBlockId = this.BlockId,
                RightBlockId = block.BlockId,
                TransBlockOperation = operation
            };

            this._tsm.Blocks[b.BlockId] = b;

            return b;
        }

        /// <summary>
        /// Returns last added block
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        public SBlock AND(SBlock block)
        {
            return this.CreateBlock(block, eOperation.AND);           
        }

        /// <summary>
        /// Returns last added block
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        public SBlock OR(SBlock block)
        {
            return this.CreateBlock(block, eOperation.OR);
        }

        /// <summary>
        /// Returns last added block
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        public SBlock XOR(SBlock block)
        {
            return this.CreateBlock(block, eOperation.XOR);
        }

        /// <summary>
        /// Returns last added block
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        public SBlock EXCLUDE(SBlock block)
        {
            return this.CreateBlock(block, eOperation.EXCLUDE);
        }
        #endregion

        /// <summary>
        /// IEnumerable returning External GetDocumentIDs
        /// </summary>
        /// <returns></returns>
        public IEnumerable<byte[]> GetDocumentIDs()
        {

            this._tsm.ComputeWordsOrigin();

            //New Logical block is always a result of operation between 2 blocks
            //Usual block is added via TextSearchManager
            
            var myArray = this.GetArrays();
            if (myArray.Count != 0)
            {
                DBreeze.DataTypes.Row<int, byte[]> docRow = null;
                foreach (var el in WABI.TextSearch_AND_logic(myArray))
                {
                    //Getting document external ID
                    docRow = this._tsm.tbExternalIDs.Select<int, byte[]>((int)el);
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

            //Logical blocks            

            this.foundArraysAreComputed = true;

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
                    if (mrg == null)
                        la = new List<byte[]>();
                    else
                        la = new List<byte[]> { mrg };
                }

                if (ra.Count > 1)
                {
                    mrg = WABI.MergeByAndLogic(ra);
                    if (mrg == null)
                        ra = new List<byte[]>();
                    else
                        ra = new List<byte[]> { mrg };
                }
            }


            switch (this.TransBlockOperation)
            {
                case eOperation.AND:
                    if(la == null || ra == null || la.Count == 0 || ra.Count == 0)
                        return foundArrays;                  
                    la.AddRange(ra);
                    this.foundArrays = la;
                    return this.foundArrays;   
                                     
                case eOperation.OR:
                    la.AddRange(ra);
                    mrg = WABI.MergeByOrLogic(la);
                    if(mrg != null)
                        this.foundArrays.Add(mrg);
                    return this.foundArrays;

                case eOperation.XOR:
                    la.AddRange(ra);
                    mrg = WABI.MergeByXorLogic(la);
                    if (mrg != null)
                        this.foundArrays.Add(mrg);
                    return this.foundArrays;

                case eOperation.EXCLUDE:
                    mrg = WABI.MergeByExcludeLogic(la.FirstOrDefault(),ra.FirstOrDefault());
                    if (mrg != null)
                        this.foundArrays.Add(mrg);
                    return this.foundArrays;
            }

            return foundArrays;
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
                                this.foundArrays.Clear();
                                return; //Parsed Words
                            }
                            else//Adding word to block array
                                this.foundArrays.Add(this._tsm.RealWords[wrd.Key].wahArray);
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
                                this.foundArrays.Add(WABI.MergeByOrLogic(echoes));  //Echoes must be merged by OrLogic
                            else
                            {
                                //Found arrays must be cleared out
                                this.foundArrays.Clear();
                                return; //Parsed Words
                            }
                        }
                        break;
                    case eOperation.OR:
                        if (wrd.Value)
                        {
                            if (this._tsm.RealWords.ContainsKey(wrd.Key))  //And word itself
                                this.foundArrays.Add(this._tsm.RealWords[wrd.Key].wahArray);
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
                                this.foundArrays.Add(WABI.MergeByOrLogic(echoes));
                        }
                        break;
                }

                foundArraysAreComputed = true;
            }//eo of parsedWords

            if (this.InternalBlockOperation == eOperation.OR)
                this.foundArrays = new List<byte[]> { WABI.MergeByOrLogic(this.foundArrays) };

        }//eo GetArrays
        
    }
}
