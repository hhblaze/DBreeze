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
    /// Manager for word aligned bitmap indexes
    /// </summary>
    public class TextSearchTable
    {
        /// <summary>
        /// 
        /// </summary>
        internal Transactions.Transaction _tran = null;
        internal string _tableName = String.Empty;
         
        /// <summary>
        /// 
        /// </summary>
        public int NoisyQuantity = 1000;
        /// <summary>
        /// 
        /// </summary>
        public bool SearchCriteriaIsNoisy = false;

        internal NestedTable tbWords = null;
        internal NestedTable tbBlocks = null;
        internal NestedTable tbExternalIDs = null;

        internal Dictionary <string, TextSearchHandler.WordInDocs> RealWords = new Dictionary<string, TextSearchHandler.WordInDocs>();
        /// <summary>
        /// Second preparation layer after parsing. After this layer we grab words from db and put into real words
        /// </summary>
        internal Dictionary<string, PureWordDef> PureWords = new Dictionary<string, PureWordDef>();

        internal class PureWordDef
        {
            public bool FullMatch { get; set; } = false;
            public bool Processed { get; set; } = false;
            /// <summary>
            /// In case if word must be searched using contains logic in db can be StartsWith echoes
            /// </summary>
            public HashSet<string> StartsWith = new HashSet<string>();
        }
                
        internal Dictionary<int, SBlock> Blocks = new Dictionary<int, SBlock>();
        internal int cntBlockId = 1;

        bool toComputeWordsOrigin = true; 

        /// <summary>
        /// 
        /// </summary>
        internal void ComputeWordsOrigin()
        {
            if (!toComputeWordsOrigin)
                return;

            //this.SearchCriteriaIsNoisy = false;

            if (this.tbWords == null)
            {
                if (this._tran == null || String.IsNullOrEmpty(this._tableName))
                    throw new Exception("DBreeze.TextSearch.WABIs.AddWords: transaction is not initialzed");

                this.tbWords = this._tran.SelectTable<byte>(this._tableName, 20, 0);
                this.tbWords.ValuesLazyLoadingIsOn = false;
            }

            if (this.tbBlocks == null)
            {
                this.tbBlocks = this._tran.SelectTable<byte>(this._tableName, 10, 0);
                this.tbBlocks.ValuesLazyLoadingIsOn = false;
            }

            if (this.tbExternalIDs == null)
            {
                tbExternalIDs = this._tran.SelectTable<byte>(this._tableName, 2, 0);
                tbExternalIDs.ValuesLazyLoadingIsOn = false;
            }

            ////DEBUG
            //foreach (var dbgWrd in this.tbWords.SelectForward<string,byte[]>())
            //{
            //    Console.WriteLine(dbgWrd.Key);
            //}
          

            TextSearchHandler.WordInDocs wid = null;
            int containsFound = 0;
            HashSet<string> startsWithEchoes = null;

            //possibly to move all RealWords to Pure
            //Resolving pure words
            foreach (var wrd in this.PureWords.Where(r => !r.Value.Processed).OrderBy(r => r.Key))
            {

                if (wrd.Value.FullMatch)
                {
                    if (this.RealWords.ContainsKey(wrd.Key))
                        continue;
                    var row2 = this.tbWords.Select<string, byte[]>(wrd.Key);
                    if (row2.Exists)
                    {
                        wid = new TextSearchHandler.WordInDocs()
                        {
                            BlockId = row2.Value.Substring(0, 4).To_UInt32_BigEndian(),
                            NumberInBlock = row2.Value.Substring(4, 4).To_UInt32_BigEndian()
                        };

                        this.RealWords[wrd.Key] = wid;
                    }
                }
                else
                {
                    //Contains
                    containsFound = 0;
                    startsWithEchoes = new HashSet<string>();
                    foreach (var row1 in this.tbWords.SelectForwardStartsWith<string, byte[]>(wrd.Key).Take(this.NoisyQuantity))
                    {
                        containsFound++;

                        if (wrd.Key != row1.Key)
                            startsWithEchoes.Add(row1.Key);

                        if (this.RealWords.ContainsKey(row1.Key))
                            continue;

                        wid = new TextSearchHandler.WordInDocs()
                        {
                            BlockId = row1.Value.Substring(0, 4).To_UInt32_BigEndian(),
                            NumberInBlock = row1.Value.Substring(4, 4).To_UInt32_BigEndian()
                        };

                        this.RealWords.Add(row1.Key, wid);
                    }

                    if (startsWithEchoes.Count > 0)
                        wrd.Value.StartsWith = startsWithEchoes;

                    if (containsFound == this.NoisyQuantity)
                        this.SearchCriteriaIsNoisy = true;
                }

                wrd.Value.Processed = true;
            }

            //Getting bitmaps for the non-processed RealWords
            //Getting blocks for the returned words

            uint currentBlockId = 0;
            Dictionary<uint, byte[]> block = null;
            byte[] btBlock = null;

            foreach (var wrd in this.RealWords.Where(r => !r.Value.Processed).OrderBy(r => r.Value.BlockId))
            {
                if (currentBlockId != wrd.Value.BlockId)
                {
                    currentBlockId = wrd.Value.BlockId;
                    block = new Dictionary<uint, byte[]>();
                    btBlock = this.tbBlocks.Select<uint, byte[]>(wrd.Value.BlockId).Value;
                    btBlock = btBlock.Substring(4, btBlock.Substring(0, 4).To_Int32_BigEndian());
                    btBlock.Decode_DICT_PROTO_UINT_BYTEARRAY(block, Compression.eCompressionMethod.Gzip);
                }

                wrd.Value.wahArray = new WABI(block[wrd.Value.NumberInBlock]).GetUncompressedByteArray();                
                wrd.Value.Processed = true;
            }

            toComputeWordsOrigin = false;
        }
        


        /// <summary>
        /// 
        /// </summary>
        /// <param name="containsWords"></param>
        /// <param name="fullMatchWords"></param>
        /// <returns></returns>
        public SBlock BlockAND(string containsWords, string fullMatchWords="")
        {
            fullMatchWords = String.IsNullOrEmpty(fullMatchWords) ? "" : fullMatchWords;
            containsWords = String.IsNullOrEmpty(containsWords) ? "" : containsWords;

            //SBlock sb = new BlockAND()
            //{
            //    _tsm = this,
            //    InternalBlockOperation = SBlock.eOperation.AND,
            //    BlockId = this.cntBlockId++,
            //    IsLogicalBlock = false
            //};

            SBlock sb = new SBlock()
            {
                _tsm = this,
                InternalBlockOperation = SBlock.eOperation.AND,
                BlockId = this.cntBlockId++,
                IsLogicalBlock = false
            };

            Blocks.Add(sb.BlockId, sb);
            
            this.WordsPrepare(fullMatchWords.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(r => r.Length >= 2), true, ref sb.ParsedWords);
            this.WordsPrepare(containsWords.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(r => r.Length >= 2), false, ref sb.ParsedWords);

            toComputeWordsOrigin = true;
            return sb;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="containsWords"></param>
        /// <param name="fullMatchWords"></param>
        /// <returns></returns>
        public SBlock BlockAND(IEnumerable<string> containsWords, IEnumerable<string> fullMatchWords)
        {
            //SBlock sb = new BlockAND()
            //{
            //    _tsm = this,
            //    InternalBlockOperation = SBlock.eOperation.AND,
            //    BlockId = this.cntBlockId++,
            //    IsLogicalBlock = false
            //};

            SBlock sb = new SBlock()
            {
                _tsm = this,
                InternalBlockOperation = SBlock.eOperation.AND,
                BlockId = this.cntBlockId++,
                IsLogicalBlock = false
            };

            Blocks.Add(sb.BlockId, sb);

            this.WordsPrepare(fullMatchWords, true, ref sb.ParsedWords);
            this.WordsPrepare(containsWords, false, ref sb.ParsedWords);

            toComputeWordsOrigin = true;
            return sb;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="containsWords"></param>
        /// <param name="fullMatchWords"></param>
        /// <returns></returns>
        public SBlock BlockOR(string containsWords, string fullMatchWords="")
        {
            fullMatchWords = String.IsNullOrEmpty(fullMatchWords) ? "" : fullMatchWords;
            containsWords = String.IsNullOrEmpty(containsWords) ? "" : containsWords;

            //SBlock sb = new BlockOR()
            //{
            //    _tsm = this,
            //    InternalBlockOperation = SBlock.eOperation.OR,
            //    BlockId = this.cntBlockId++,
            //    IsLogicalBlock = false
            //};

            SBlock sb = new SBlock()
            {
                _tsm = this,
                InternalBlockOperation = SBlock.eOperation.OR,
                BlockId = this.cntBlockId++,
                IsLogicalBlock = false
            };

            Blocks.Add(sb.BlockId, sb);
            
            this.WordsPrepare(fullMatchWords.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(r => r.Length >= 2), true, ref sb.ParsedWords);
            this.WordsPrepare(containsWords.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(r => r.Length >= 2), false, ref sb.ParsedWords);

            toComputeWordsOrigin = true;
            return sb;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="containsWords"></param>
        /// <param name="fullMatchWords"></param>
        /// <returns></returns>
        public SBlock BlockOR(IEnumerable<string> containsWords, IEnumerable<string> fullMatchWords)
        {

            //SBlock sb = new BlockOR()
            //{
            //    _tsm = this,
            //    InternalBlockOperation = SBlock.eOperation.OR,
            //    BlockId = this.cntBlockId++,
            //    IsLogicalBlock = false
            //};

            SBlock sb = new SBlock()
            {
                _tsm = this,
                InternalBlockOperation = SBlock.eOperation.OR,
                BlockId = this.cntBlockId++,
                IsLogicalBlock = false
            };

            Blocks.Add(sb.BlockId, sb);

            this.WordsPrepare(fullMatchWords, true, ref sb.ParsedWords);
            this.WordsPrepare(containsWords, false, ref sb.ParsedWords);

            toComputeWordsOrigin = true;
            return sb;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="searchKeywords"></param>
        /// <param name="fullMatch"></param>
        /// <param name="wordsList"></param>
        void WordsPrepare(IEnumerable<string> searchKeywords, bool fullMatch, ref Dictionary<string,bool> wordsList)
        {
            string word = "";

            if (searchKeywords == null || searchKeywords.Count() == 0)
                return;

            foreach (var wrd in searchKeywords)
            {   
                word = wrd.ToLower();
                if (word.Trim().Length < 2 || word.Contains(" "))
                    continue;

                //this.PureWords.Add(word, new TextSearchTable.PureWordDef() { FullMatch = fullMatch, Processed = false });
                ////Adding also words to blocks
                //wordsList.Add(word, fullMatch);

                if (!this.PureWords.ContainsKey(word))
                {
                    this.PureWords.Add(word, new TextSearchTable.PureWordDef() { FullMatch = fullMatch, Processed = false });
                }
                else
                {
                    //In case if word already in the list and is fullMatch, but supplied in not fullmatch we change to contains
                    if (!fullMatch && this.PureWords[word].FullMatch)
                    {
                        this.PureWords[word].FullMatch = fullMatch;
                        this.PureWords[word].Processed = false;
                    }
                }

                //Adding also words to blocks
                if (!wordsList.ContainsKey(word))
                    wordsList.Add(word, fullMatch);
                else
                {
                    //In case if word already in the list and is fullMatch, but supplied in not fullmatch we change to contains
                    if (!fullMatch && wordsList[word])
                        wordsList[word] = fullMatch;
                }
            }
        }
        

    }
}
