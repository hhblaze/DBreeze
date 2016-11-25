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
    public class TextSearchManager
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
            public List<string> StartsWith = new List<string>();
        }
                
        internal Dictionary<int, SBlock> Blocks = new Dictionary<int, SBlock>();
        internal int cntBlockId = 1;

        bool toComputeWordsOrigin = true; 

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
          

            TextSearchHandler.WordInDocs wid = null;
            int containsFound = 0;
            List<string> startsWithEchoes = new List<string>();

            //!!!!!!!!!!!!!!!!!!!!!!!!!!! Later move all RealWords to Pure
            //Resolving pure words
            foreach (var wrd in this.PureWords.Where(r => !r.Value.Processed).OrderBy(r => r.Key))
            {

                if (wrd.Value.FullMatch)
                {
                    var row2 = this.tbWords.Select<string, byte[]>(wrd.Key);
                    if (row2.Exists)
                    {
                        wid = new TextSearchHandler.WordInDocs()
                        {
                            BlockId = row2.Value.Substring(0, 4).To_UInt32_BigEndian(),
                            NumberInBlock = row2.Value.Substring(4, 4).To_UInt32_BigEndian()
                        };

                        this.RealWords.Add(wrd.Key, wid);
                    }
                }
                else
                {
                    //Contains
                    containsFound = 0;
                    startsWithEchoes = new List<string>();
                    foreach (var row1 in this.tbWords.SelectForwardStartsWith<string, byte[]>(wrd.Key).Take(this.NoisyQuantity))
                    {
                        wid = new TextSearchHandler.WordInDocs()
                        {
                            BlockId = row1.Value.Substring(0, 4).To_UInt32_BigEndian(),
                            NumberInBlock = row1.Value.Substring(4, 4).To_UInt32_BigEndian()
                        };

                        if (wrd.Key != row1.Key)
                            startsWithEchoes.Add(row1.Key);

                        if (!this.RealWords.ContainsKey(row1.Key))  //upper we don't need such check
                            this.RealWords.Add(row1.Key, wid);

                        containsFound++;
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

                wrd.Value.wahArray = new WAH2(block[wrd.Value.NumberInBlock]).GetUncompressedByteArray();
                //wrd.Value.wah = new WAH2(block[wrd.Value.NumberInBlock]);
                wrd.Value.Processed = true;
            }

            toComputeWordsOrigin = false;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="fullMatch"></param>
        /// <param name="words"></param>
        /// <returns></returns>
        public SBlock SearchAndBlock(bool fullMatch, string words)
        {
            SBlock sb = new SBlock()
            {
                _tsm = this,
                InternalBlockOperation = SBlock.eOperation.AND,
                BlockId = this.cntBlockId++,
                IsLogicalBlock = false
            };

            Blocks.Add(sb.BlockId, sb);

            if (String.IsNullOrEmpty(words))
                return sb;

            this.WordsPrepare(words, fullMatch, ref sb.ParsedWords);
            toComputeWordsOrigin = true;
            return sb;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="fullMatch"></param>
        /// <param name="words"></param>
        /// <returns></returns>
        public SBlock SearchOrBlock(bool fullMatch, string words)
        {
            SBlock sb = new SBlock()
            {
                _tsm = this,
                InternalBlockOperation = SBlock.eOperation.OR,
                BlockId = this.cntBlockId++,
                IsLogicalBlock = false
            };
            Blocks.Add(sb.BlockId, sb);

            if (String.IsNullOrEmpty(words))
                return sb;

            this.WordsPrepare(words, fullMatch, ref sb.ParsedWords);
            toComputeWordsOrigin = true;
            return sb;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="searchKeywords"></param>
        /// <param name="fullMatch"></param>
        /// <param name="wordsList"></param>
        void WordsPrepare(string searchKeywords, bool fullMatch, ref Dictionary<string,bool> wordsList)
        {
            string word = "";
            foreach (var wrd in searchKeywords.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(r => r.Length >= 2))
            {                                
                //word = (fullMatch ? wrd.Substring(1) : wrd).ToLower();
                word = wrd.ToLower();
                if (word.Trim().Length < 2 || word.Contains(" ") || this.PureWords.ContainsKey(word))
                    continue;

                this.PureWords.Add(word, new TextSearchManager.PureWordDef() { FullMatch = fullMatch, Processed = false });
                //Adding also words to blocks
                wordsList.Add(word, fullMatch);
            }

        }
        

    }
}
