/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
*/

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
//using System.Threading.Tasks;

using DBreeze;
using DBreeze.Transactions;
using DBreeze.Utils;
using DBreeze.DataTypes;
using System.IO;


namespace DBreeze.TextSearch
{
    /// <summary>
    /// New instance per transaction. Is created by necessity, while inserting or selecting anything concerning TextSearch subsystem.
    /// </summary>
    internal class TextSearchHandler
    {
        public bool InsertWasPerformed = false;
        Transaction tran = null;
        Dictionary<string, HashSet<uint>> defferedDocIds = new Dictionary<string, HashSet<uint>>();

        /// <summary>
        /// TextSearch handler becomes universal for other entites
        /// </summary>
        public Dictionary<string, HashSet<uint>> DeferredVectors = new Dictionary<string, HashSet<uint>>();
       

        public TextSearchHandler(Transaction tran)
        {
            this.tran = tran;
        }


        /// <summary>
        /// Internal search-table structure.       
        /// </summary>
        internal class ITS
        {
            public HashSet<int> ChangedDocIds = new HashSet<int>();

            /// <summary>
            /// External document index to internal - 1. Key byte, Value NestedTable
            /// </summary>
            public NestedTable e2i = null;
            /// <summary>
            /// Internal document index to external - 2. Key byte, Value NestedTable
            /// </summary>
            public NestedTable i2e = null;
            /// <summary>
            /// Searchables to insert - 3 (byte). internal docId(int)+ new byte[]{0}/new byte[]{1} (0 for current searchables, 1 for new intended to be saved searchables), Value is searchables.
            /// Insert always compares newly intended with current and if no changes exits. 
            /// Indexer replaces new with current.
            /// itbls.Value.ChangedDocIds contains IDs of changed docs per search table
            /// </summary>
            public NestedTable srch = null;

            //Key 4: LastIndexedTime tran.Select<byte, byte[]>(tbl, 4); Under index 4 we hold LastIndexedTime for that table   

            /// <summary>
            /// Key 10: [uint,byte[]] where K is BlockID[uint] (1000 words per block), Value is GzippedAndProtobufed Dictionary of [uint, byte[]] where K is ID of the word in Key2 and value its WAH reserved (init reservation 100KB per block)            
            /// </summary>
            public NestedTable blocks = null;

            /// <summary>
            /// Key 11: [int]-[0,0,0,11], Value uint; - current blockNumber
            /// </summary>
            public uint currentBlock = 0;

            /// <summary>
            /// Key 12: [int][0,0,0,12], Value uint; used number in the block
            /// </summary>
            public uint numberInBlock = 0;
            /// <summary>
            /// Key 20 : [string,byte[]] NestedTable(Index to search by words)
            /// <para>Where Key: string - word</para>
            /// <para>Value: [byte[]] BlockId[uint] + NumberInBlock[uint] (reference to Key 10)</para>
            /// </summary>
            public NestedTable words = null;

            /// <summary>
            /// Key 14 (byte): Value: int, where 0 - legacy, non-encrypted; Otherwise encryption type. 1 - ITextStreamCrypto bound to TextConfiguration.
            /// </summary>
            public int Encryption = 0;

            // Used only by in-transaction migration: newly copied nested rows are not guaranteed
            // to be visible through another read scope until commit.
            public Dictionary<string, WordInDocs> MigratedWords = null;
            public Dictionary<uint, byte[]> MigratedBlocks = null;
        }

        /// <summary>
        /// Registering all search-tables mutated during transaction
        /// </summary>
        Dictionary<string, ITS> itbls = new Dictionary<string, ITS>();

        public enum eInsertMode
        {
            Insert,
            Append,
            Remove,
            //RemoveAll
        }

        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="tran"></param>
        /// <param name="tableName"></param>
        /// <param name="documentIDs"></param>
        /// <returns></returns>
        public Dictionary<byte[], HashSet<string>> GetDocumentsSearchables(Transaction tran, string tableName,  HashSet<byte[]> documentIDs)
        {
            ITS its = null;
            its = new ITS()
            {
                e2i = tran.SelectTable<byte>(tableName, 1, 0),
                srch = tran.SelectTable<byte>(tableName, 3, 0),
            };

            var encRow = tran.Select<byte, int>(tableName, 14);
            if (encRow.Exists)
                its.Encryption = encRow.Value;

            if (its.Encryption > 0 && tran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.TextEncryptor == null)
                throw new Exception($"Encryptor for the text search table {tableName} is null (Configuration.TextSearchConfig.TextEncryptor), set it up with your keys");

            its.e2i.ValuesLazyLoadingIsOn = false;
            its.srch.ValuesLazyLoadingIsOn = false;

            Dictionary<byte[], HashSet<string>> rdocuments = new Dictionary<byte[], HashSet<string>>();

            foreach (var documentID in documentIDs)
            {
                var r1 = its.e2i.Select<byte[], int>(documentID);

                if (r1.Exists)          //DOCUMENT EXISTS
                {                   
                    //Getting searchables for this document                
                    byte[] oldSrch = its.srch.Select<byte[], byte[]>(CreateSearchablesKey(r1.Value, 0), true).Value;
                    //rdocuments[documentID] = GetSearchablesFromByteArray_AsHashSet(oldSrch); //always instantiated hashset

                    var hs = GetSearchablesFromByteArray_AsHashSet(oldSrch,
                        its.Encryption > 0 ? tran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.TextEncryptor : null
                        ); //always instantiated hashset

                    rdocuments[documentID] = hs;
                    //if (Encryptor == null)
                    //    rdocuments[documentID] = hs;
                    //else
                    //{
                    //    var nhs = new HashSet<string>();                        
                    //    foreach(var el in hs)
                    //        nhs.Add(Encryptor.TextEncryptor(el,false));

                    //    rdocuments[documentID] = nhs;
                    //}
                }
            }

            return rdocuments;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="tran"></param>
        /// <param name="tableName"></param>
        /// <param name="documentId"></param>
        /// <param name="containsWords"></param>
        /// <param name="fullMatchWords"></param>
        /// <param name="deferredIndexing"></param>
        /// <param name="containsMinimalLength"></param>
        /// <param name="iMode"></param>        
        public void InsertDocumentText(Transaction tran, string tableName, byte[] documentId, string containsWords, string fullMatchWords, 
            bool deferredIndexing, int containsMinimalLength, eInsertMode iMode)
        {

            //tran._transactionUnit.TransactionsCoordinator._engine.Configuration.
            if (String.IsNullOrEmpty(tableName) || documentId == null)
                return;

            containsWords = containsWords ?? String.Empty;
            fullMatchWords = fullMatchWords ?? String.Empty;

            if ((iMode == eInsertMode.Append || iMode == eInsertMode.Remove) && (String.IsNullOrEmpty(containsWords) && String.IsNullOrEmpty(fullMatchWords)))
                return;           

            //tran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.QuantityOfWordsInBlock
            SortedSet<string> pST = this.GetWordsDefinitionFromText(containsWords, fullMatchWords, containsMinimalLength,
                tran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.MaximalWordSize); //flattend searchables

            StringBuilder sbPs = new StringBuilder();

            //Registering all tables for text-search in current transaction
            ITS its = null;

            if (!itbls.TryGetValue(tableName, out its))
            {
                its = new ITS()
                {
                    e2i = tran.InsertTable<byte>(tableName, 1, 0),
                    i2e = tran.InsertTable<byte>(tableName, 2, 0),
                    srch = tran.InsertTable<byte>(tableName, 3, 0),
                };


                its.e2i.ValuesLazyLoadingIsOn = false;
                its.i2e.ValuesLazyLoadingIsOn = false;
                its.srch.ValuesLazyLoadingIsOn = false;

                //Getting table encryption information
                var rowEncryption = tran.Select<byte, int>(tableName, 14);
                if (rowEncryption.Exists)
                {
                    its.Encryption = rowEncryption.Value;
                }

                if (its.Encryption == 0 && its.e2i.Count() == 0 && its.srch.Count() == 0)
                {
                    //only for empty tables
                    if (tran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.UseTextEncryptor)
                    {
                        //We want to use encryptor for this table
                        its.Encryption = 1; //Fixed mode 1 - ITextStreamCrypto
                    }

                    //Duplicating is ok. Early stage notification about absense of the encryptor
                    if (its.Encryption > 0 && tran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.TextEncryptor == null)
                        throw new Exception($"Encryptor for the text search table {tableName} is null (Configuration.TextSearchConfig.TextEncryptor), set it up with your keys");

                    //Inserting the fact, table is encrypted
                    if(its.Encryption > 0)
                        tran.Insert<byte, int>(tableName, 14, its.Encryption); //Fixed mode 1 - ITextStreamCrypto
                }

                itbls.Add(tableName, its);
            }

            //Duplicating is ok. Early stage notification about absense of the encryptor
            if (its.Encryption > 0 && tran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.TextEncryptor == null)
                throw new Exception($"Encryptor for the text search table {tableName} is null (Configuration.TextSearchConfig.TextEncryptor), set it up with your keys");

            //Internal document ID
            int iId = 0;

            //Searching document by externalID
            var r1 = its.e2i.Select<byte[], int>(documentId);

            if (r1.Exists)          //DOCUMENT EXISTS
            {
                iId = r1.Value;

                //Getting old searchables for this document                
                byte[] oldSrch = its.srch.Select<byte[], byte[]>(CreateSearchablesKey(iId, 0), true).Value;
                HashSet<string> oldSearchables = GetSearchablesFromByteArray_AsHashSet(oldSrch,
                    its.Encryption > 0 ? tran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.TextEncryptor : null
                    ); //always instantiated hashset

                switch (iMode)
                {
                    case eInsertMode.Insert:
                        if (oldSearchables.SetEquals(pST))
                            return; //Going out, nothing to insert

                        foreach (string word in pST)
                        {
                            sbPs.Append(word);
                            sbPs.Append(' ');
                        }
                        break;
                    case eInsertMode.Append:
                    case eInsertMode.Remove:

                        if (iMode == eInsertMode.Append && oldSearchables.IsSupersetOf(pST))
                            return;
                        if (iMode == eInsertMode.Remove && !oldSearchables.Overlaps(pST))
                            return;

                        foreach (var ew in pST)
                        {
                            if (iMode == eInsertMode.Append)
                                oldSearchables.Add(ew);
                            else
                                oldSearchables.Remove(ew);
                        }

                        foreach (var el in oldSearchables)
                        {
                            sbPs.Append(el);
                            sbPs.Append(' ');
                        }

                        break;
                }
            }
            else
            {
                //DOCUMENT NEW
                if (pST.Count < 1)
                    return; //Going out, nothing to insert

                //Document is new
                if (iMode == eInsertMode.Append)
                    iMode = eInsertMode.Insert;
                else if (iMode == eInsertMode.Remove)
                    return; //Going out

                iId = checked(its.i2e.Max<int, byte[]>().Key + 1);

                its.e2i.Insert<byte[], int>(documentId, iId);
                its.i2e.Insert<int, byte[]>(iId, documentId);

                foreach (string word in pST)
                {
                    sbPs.Append(word);
                    sbPs.Append(' ');
                }
            }

            this.InsertWasPerformed = true;

            //Inserting into affected table
            if (!deferredIndexing)
                its.ChangedDocIds.Add(iId);
            else
            {
                if (!defferedDocIds.ContainsKey(tableName))
                    defferedDocIds[tableName] = new HashSet<uint>();

                defferedDocIds[tableName].Add((uint)iId);
            }

            //Inserting searchables to be indexed            
            its.srch.Insert<byte[], byte[]>(CreateSearchablesKey(iId, 1),
                GetByteArrayFromSearchbles(sbPs.ToString(),
                its.Encryption > 0 ? tran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.TextEncryptor : null
                ));
        }



        internal class WordInDocs
        {
            /// <summary>
            /// Wah2 block id
            /// </summary>
            public uint BlockId { get; set; } = 0;
            /// <summary>
            /// Number in Wah2 block
            /// </summary>
            public uint NumberInBlock { get; set; } = 0;
            /// <summary>
            /// Processed
            /// </summary>
            public bool Processed { get; set; } = false;
            /// <summary>
            /// Unzipped WABI
            /// </summary>
            public byte[] wahArray { get; set; } = null;          
        }        

        /// <summary>
        /// Started only in case if InsertWasPerformed in deffered or not deffered way
        /// </summary>
        public void BeforeCommit()
        {
            this.DoIndexing(this.tran,this.itbls);  //Do start indexing inside of commit            
        }

        /// <summary>
        ///  Started only in case if InsertWasPerformed in deffered or not deffered way
        /// </summary>
        public void AfterCommit()
        {
            bool startIndexer = false;
            //Trying start deffered indexer in parallel thread for text search.
            if (defferedDocIds.Count > 0)
            {
                tran._transactionUnit.TransactionsCoordinator._engine.DeferredIndexer.Add(defferedDocIds);
                defferedDocIds.Clear();
                //tran._transactionUnit.TransactionsCoordinator._engine.DeferredIndexer.StartDefferedIndexing();
                startIndexer = true;
            }

            if(DeferredVectors.Count > 0)
            {
                tran._transactionUnit.TransactionsCoordinator._engine.DeferredIndexer.AddVectors(DeferredVectors);
                DeferredVectors.Clear();
                startIndexer = true;
            }

            if(startIndexer)
                tran._transactionUnit.TransactionsCoordinator._engine.DeferredIndexer.StartDefferedIndexing();
        }

        /// <summary>
        /// itbls and transaction must be supplied, to make it working from outside
        /// </summary>
        sealed class WordChange
        {
            public readonly HashSet<int> RemoveFrom = new HashSet<int>();
            public readonly HashSet<int> AddTo = new HashSet<int>();
            public WordInDocs Definition;
        }

        const int NewWordReferenceFlushThreshold = 100000;

        static void FlushNewWordReferences(NestedTable words, SortedDictionary<string, byte[]> pendingWords,
            ITextStreamCrypto encryptor)
        {
            // LTrie reuses the previous key path. Ordinal plaintext order keeps equal prefixes
            // adjacent; deterministic stream encryption preserves those prefixes in stored keys.
            foreach (var pendingWord in pendingWords)
            {
                if (encryptor != null)
                    words.Insert<byte[], byte[]>(encryptor.TextEncrypt(pendingWord.Key), pendingWord.Value);
                else
                    words.Insert<string, byte[]>(pendingWord.Key, pendingWord.Value);
            }

            pendingWords.Clear();
        }

        internal void DoIndexing(Transaction itran, Dictionary<string, ITS> xitbls)
        {
            byte[] btUdtStart = DateTime.UtcNow.Ticks.To_8_bytes_array_BigEndian();
            foreach (var tbl in xitbls)
            {
                ITS its = tbl.Value;
                if (its.srch == null)   //Can be instantiated in insert procedure, depending how we use indexer
                {
                    its.srch = itran.InsertTable<byte>(tbl.Key, 3, 0);
                    its.srch.ValuesLazyLoadingIsOn = false;
                }
                if (its.blocks == null)
                    its.blocks = itran.InsertTable<byte>(tbl.Key, 10, 0);
                if (its.words == null)
                    its.words = itran.InsertTable<byte>(tbl.Key, 20, 0);
                if (its.currentBlock == 0)
                {
                    its.currentBlock = itran.Select<int, uint>(tbl.Key, 11).Value;
                    its.numberInBlock = itran.Select<int, uint>(tbl.Key, 12).Value;
                }

                //wheather table is encrypted
                itran.ValuesLazyLoadingIsOn = false;
                var encRow = itran.Select<byte, int>(tbl.Key, 14);
                if(encRow.Exists)
                {
                    its.Encryption = encRow.Value;
                    if (its.Encryption > 0 && itran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.TextEncryptor == null)
                        throw new Exception($"Encryptor for the text search table {tbl.Key} is null (Configuration.TextSearchConfig.TextEncryptor), set it up with your keys");
                }

                its.blocks.ValuesLazyLoadingIsOn = false;
                its.words.ValuesLazyLoadingIsOn = false;

                if (its.currentBlock == 0)
                {
                    its.numberInBlock = 0;
                    its.currentBlock = 1;
                }

                // Capture encryptor once per table (null when table is not encrypted)
                ITextStreamCrypto encryptor = its.Encryption > 0
                    ? itran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.TextEncryptor
                    : null;

                int wordsPerBlock = itran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.QuantityOfWordsInBlock;
                if (wordsPerBlock < 1)
                    throw new InvalidOperationException("DBreeze.TextSearch: QuantityOfWordsInBlock must be positive");

                var changes = new Dictionary<string, WordChange>(StringComparer.Ordinal);
                var newWordReferences = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);

                Func<string, bool, WordChange> getChange = delegate(string word, bool createIfMissing)
                {
                    WordChange existingChange;
                    if (changes.TryGetValue(word, out existingChange))
                        return existingChange;

                    byte[] wordValue = null;
                    WordInDocs migratedDefinition = null;
                    bool wordExists = its.MigratedWords != null && its.MigratedWords.TryGetValue(word, out migratedDefinition);
                    if (!wordExists && encryptor != null)
                    {
                        Row<byte[], byte[]> row = its.words.Select<byte[], byte[]>(encryptor.TextEncrypt(word), true);
                        wordExists = row.Exists;
                        if (wordExists)
                            wordValue = row.Value;
                    }
                    else if (!wordExists)
                    {
                        Row<string, byte[]> row = its.words.Select<string, byte[]>(word, true);
                        wordExists = row.Exists;
                        if (wordExists)
                            wordValue = row.Value;
                    }

                    if (!wordExists && !createIfMissing)
                        return null;

                    var definition = migratedDefinition ?? new WordInDocs();
                    if (wordExists)
                    {
                        if (migratedDefinition == null)
                        {
                            if (wordValue == null || wordValue.Length < 8)
                                throw new InvalidDataException("DBreeze.TextSearch: invalid word-to-block reference");
                            definition.BlockId = ReadUInt32BigEndian(wordValue, 0);
                            definition.NumberInBlock = ReadUInt32BigEndian(wordValue, 4);
                        }
                    }
                    else
                    {
                        its.numberInBlock = checked(its.numberInBlock + 1);
                        if (its.numberInBlock > (uint)wordsPerBlock)
                        {
                            its.currentBlock = checked(its.currentBlock + 1);
                            its.numberInBlock = 1;
                        }

                        definition.BlockId = its.currentBlock;
                        definition.NumberInBlock = its.numberInBlock;
                        newWordReferences[word] = CreateWordReference(definition.BlockId, definition.NumberInBlock);
                        if (newWordReferences.Count > NewWordReferenceFlushThreshold)
                            FlushNewWordReferences(its.words, newWordReferences, encryptor);
                    }

                    var change = new WordChange { Definition = definition };
                    changes.Add(word, change);
                    return change;
                };

                int[] changedDocumentIds = new int[its.ChangedDocIds.Count];
                its.ChangedDocIds.CopyTo(changedDocumentIds);
                Array.Sort(changedDocumentIds);

                for (int documentIndex = 0; documentIndex < changedDocumentIds.Length; documentIndex++)
                {
                    int docId = changedDocumentIds[documentIndex];
                    byte[] currentKey = CreateSearchablesKey(docId, 0);
                    byte[] pendingKey = CreateSearchablesKey(docId, 1);
                    byte[] oldSrch = its.srch.Select<byte[], byte[]>(currentKey).Value;
                    byte[] newSrch = its.srch.Select<byte[], byte[]>(pendingKey).Value;
                    HashSet<string> oldWords = GetSearchablesFromByteArray_AsHashSet(oldSrch, encryptor);
                    HashSet<string> newWords = GetSearchablesFromByteArray_AsHashSet(newSrch, encryptor);

                    foreach (string word in oldWords)
                    {
                        if (newWords.Contains(word))
                            continue;
                        WordChange change = getChange(word, false);
                        if (change != null)
                            change.RemoveFrom.Add(docId);
                    }

                    foreach (string word in newWords)
                    {
                        if (!oldWords.Contains(word))
                            getChange(word, true).AddTo.Add(docId);
                    }

                    its.srch.RemoveKey<byte[]>(pendingKey);
                    its.srch.Insert<byte[], byte[]>(currentKey, newSrch);
                }

                FlushNewWordReferences(its.words, newWordReferences, encryptor);

                var orderedChanges = new List<KeyValuePair<string, WordChange>>(changes);
                orderedChanges.Sort(delegate(KeyValuePair<string, WordChange> left, KeyValuePair<string, WordChange> right)
                {
                    int comparison = left.Value.Definition.BlockId.CompareTo(right.Value.Definition.BlockId);
                    if (comparison == 0)
                        comparison = left.Value.Definition.NumberInBlock.CompareTo(right.Value.Definition.NumberInBlock);
                    if (comparison == 0)
                        comparison = StringComparer.Ordinal.Compare(left.Key, right.Key);
                    return comparison;
                });

                var block = new Dictionary<uint, byte[]>();
                uint loadedBlockId = 0;
                int loadedBlockCapacity = 0;
                int minimalBlockReserve = itran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.MinimalBlockReservInBytes;

                for (int changeIndex = 0; changeIndex < orderedChanges.Count; changeIndex++)
                {
                    KeyValuePair<string, WordChange> entry = orderedChanges[changeIndex];
                    WordInDocs definition = entry.Value.Definition;
                    if (definition.BlockId != loadedBlockId)
                    {
                        if (loadedBlockId != 0)
                            SaveBlock(its.blocks, loadedBlockId, block, loadedBlockCapacity, minimalBlockReserve);
                        loadedBlockId = definition.BlockId;
                        byte[] migratedBlock = null;
                        if (its.MigratedBlocks != null)
                            its.MigratedBlocks.TryGetValue(loadedBlockId, out migratedBlock);
                        LoadBlock(its.blocks, loadedBlockId, migratedBlock, block, out loadedBlockCapacity);
                    }

                    byte[] compressedBitmap;
                    WABI bitmap = block.TryGetValue(definition.NumberInBlock, out compressedBitmap)
                        ? new WABI(compressedBitmap)
                        : new WABI();
                    bitmap.Add(entry.Value.AddTo, true);
                    bitmap.Add(entry.Value.RemoveFrom, false);

                    if (bitmap.IsEmpty)
                    {
                        block.Remove(definition.NumberInBlock);
                        if (encryptor != null)
                            its.words.RemoveKey<byte[]>(encryptor.TextEncrypt(entry.Key));
                        else
                            its.words.RemoveKey<string>(entry.Key);
                    }
                    else
                        block[definition.NumberInBlock] = bitmap.GetCompressedByteArray();
                }

                if (loadedBlockId != 0)
                    SaveBlock(its.blocks, loadedBlockId, block, loadedBlockCapacity, minimalBlockReserve);

                itran.Insert<int, uint>(tbl.Key, 11, its.currentBlock);
                itran.Insert<int, uint>(tbl.Key, 12, its.numberInBlock);

                //Setting last indexing time
                itran.Insert<byte, byte[]>(tbl.Key, 4, btUdtStart);

            }//eo foreach tablesToIndex            
        }

        static byte[] CreateSearchablesKey(int documentId, byte marker)
        {
            byte[] result = new byte[5];
            // DBreeze's sortable Int32 encoding flips the sign bit before writing big-endian.
            WriteUInt32BigEndian(result, 0, unchecked((uint)(documentId ^ Int32.MinValue)));
            result[4] = marker;
            return result;
        }

        static byte[] CreateWordReference(uint blockId, uint numberInBlock)
        {
            byte[] result = new byte[8];
            WriteUInt32BigEndian(result, 0, blockId);
            WriteUInt32BigEndian(result, 4, numberInBlock);
            return result;
        }

        static void LoadBlock(NestedTable blocks, uint blockId, byte[] prefetched, Dictionary<uint, byte[]> block, out int existingCapacity)
        {
            block.Clear();
            byte[] stored = prefetched ?? blocks.Select<uint, byte[]>(blockId).Value;
            existingCapacity = stored == null ? 0 : stored.Length;
            if (stored == null)
                return;
            if (stored.Length < 4)
                throw new InvalidDataException("DBreeze.TextSearch: invalid bitmap block header");

            int payloadLength = ReadInt32BigEndian(stored, 0);
            if (payloadLength < 0 || payloadLength > stored.Length - 4)
                throw new InvalidDataException("DBreeze.TextSearch: invalid bitmap block length");
            if (payloadLength == 0)
                return;

            byte[] payload = new byte[payloadLength];
            Buffer.BlockCopy(stored, 4, payload, 0, payloadLength);
            payload.Decode_DICT_PROTO_UINT_BYTEARRAY(block, Compression.eCompressionMethod.Gzip);
        }

        static void SaveBlock(NestedTable blocks, uint blockId, Dictionary<uint, byte[]> block, int existingCapacity, int minimalReserve)
        {
            if (block.Count == 0)
            {
                blocks.RemoveKey<uint>(blockId);
                return;
            }

            byte[] payload = block.Encode_DICT_PROTO_UINT_BYTEARRAY(Compression.eCompressionMethod.Gzip);
            int required = checked(payload.Length + 4);
            int minimum = Math.Max(4, minimalReserve);
            int capacity;
            if (existingCapacity < required)
            {
                int doubled = existingCapacity <= 0 || existingCapacity > Int32.MaxValue / 2
                    ? required
                    : existingCapacity * 2;
                capacity = Math.Max(minimum, Math.Max(required, doubled));
            }
            else if (existingCapacity > minimum && required <= existingCapacity / 4)
            {
                int doubledRequired = required > Int32.MaxValue / 2 ? required : required * 2;
                capacity = Math.Max(minimum, doubledRequired);
            }
            else
                capacity = Math.Max(minimum, existingCapacity);

            if (capacity < required)
                capacity = required;

            byte[] stored = new byte[capacity];
            WriteInt32BigEndian(stored, 0, payload.Length);
            Buffer.BlockCopy(payload, 0, stored, 4, payload.Length);
            blocks.Insert<uint, byte[]>(blockId, stored);
        }

        static uint ReadUInt32BigEndian(byte[] value, int offset)
        {
            return BinaryPrimitives.ReadUInt32BigEndian(value.AsSpan(offset, sizeof(uint)));
        }

        static int ReadInt32BigEndian(byte[] value, int offset)
        {
            return unchecked((int)(ReadUInt32BigEndian(value, offset) ^ 0x80000000u));
        }

        static void WriteUInt32BigEndian(byte[] value, int offset, uint number)
        {
            BinaryPrimitives.WriteUInt32BigEndian(value.AsSpan(offset, sizeof(uint)), number);
        }

        static void WriteInt32BigEndian(byte[] value, int offset, int number)
        {
            WriteUInt32BigEndian(value, offset, unchecked((uint)(number ^ Int32.MinValue)));
        }

        #region "Converters"
        /// <summary>
        /// Converter from searchbales to byte[]
        /// </summary>
        /// <param name="searchables"></param>
        /// <returns></returns>
        byte[] GetByteArrayFromSearchbles(string searchables, ITextStreamCrypto encryptor)
        {
            if(encryptor != null)
                return encryptor.TextEncrypt(searchables).GZip_Compress();

            return searchables.To_UTF8Bytes().GZip_Compress();
        }

        /// <summary>
        /// Converter from byte[] searchables.
        /// </summary>
        /// <param name="searchables"></param>
        /// <returns></returns>
        string GetSearchablesFromByteArray(byte[] searchables, ITextStreamCrypto encryptor)
        {
            if (searchables == null)
                return String.Empty;

            if (encryptor != null)
                return encryptor.TextDecrypt(searchables.GZip_Decompress());

            return searchables.GZip_Decompress().ToUTF8String();
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="searchables"></param>
        /// <returns></returns>
        HashSet<string> GetSearchablesFromByteArray_AsHashSet(byte[] searchables, ITextStreamCrypto encryptor)
        {
            HashSet<string> res = new HashSet<string>(StringComparer.Ordinal);

            string r = GetSearchablesFromByteArray(searchables, encryptor);
            if (String.IsNullOrEmpty(r))
                return res;

            ReadOnlySpan<char> text = r.AsSpan();
            int start = 0;
            while (start < text.Length)
            {
                while (start < text.Length && text[start] == ' ')
                    start++;
                if (start == text.Length)
                    break;

                int end = text.Slice(start).IndexOf(' ');
                if (end < 0)
                    end = text.Length;
                else
                    end += start;
                res.Add(text.Slice(start, end - start).ToString());
                start = end + 1;
            }
            return res;
        }

        #endregion

        /// <summary>
        /// Returns null in case of notfound anything or what ever
        /// </summary>
        /// <param name="containsWords"></param>
        /// <param name="fullMatchWords"></param>
        /// <param name="containsMinimalLength"></param>
        /// <param name="maxWordSize">Taken from configuration. Default is 50. word separated by spaces</param>
        /// <returns></returns>
        SortedSet<string> GetWordsDefinitionFromText(string containsWords, string fullMatchWords, int containsMinimalLength, int maxWordSize)
        {
            var words = new SortedSet<string>(StringComparer.Ordinal);
            containsWords = containsWords ?? String.Empty;
            fullMatchWords = fullMatchWords ?? String.Empty;

            if (containsMinimalLength < 3)
                containsMinimalLength = 3;
            if (maxWordSize < 1)
                throw new ArgumentOutOfRangeException("maxWordSize", "MaximalWordSize must be positive");

            ReadOnlySpan<char> exactText = fullMatchWords.AsSpan();
            int exactStart = 0;
            while (exactStart < exactText.Length)
            {
                while (exactStart < exactText.Length && exactText[exactStart] == ' ')
                    exactStart++;
                if (exactStart == exactText.Length)
                    break;

                int exactEnd = exactText.Slice(exactStart).IndexOf(' ');
                if (exactEnd < 0)
                    exactEnd = exactText.Length;
                else
                    exactEnd += exactStart;
                int exactLength = exactEnd - exactStart;
                if (exactLength >= containsMinimalLength)
                    words.Add(exactText.Slice(exactStart, exactLength).ToString().ToLower());
                exactStart = exactEnd + 1;
            }

            if (containsWords.Length == 0)
                return words;

            ReadOnlySpan<char> containsText = containsWords.AsSpan();
            int tokenStart = 0;
            while (tokenStart < containsText.Length)
            {
                while (tokenStart < containsText.Length && containsText[tokenStart] == ' ')
                    tokenStart++;
                if (tokenStart == containsText.Length)
                    break;

                int tokenEnd = containsText.Slice(tokenStart).IndexOf(' ');
                if (tokenEnd < 0)
                    tokenEnd = containsText.Length;
                else
                    tokenEnd += tokenStart;

                while (tokenStart < tokenEnd)
                {
                    int chunkLength = Math.Min(maxWordSize, tokenEnd - tokenStart);
                    AddWordAndSuffixes(words, containsText.Slice(tokenStart, chunkLength), containsMinimalLength);
                    tokenStart += chunkLength;
                }

                tokenStart = tokenEnd + 1;
            }

            return words;
        }

        static void AddWordAndSuffixes(SortedSet<string> words, ReadOnlySpan<char> value, int minimalLength)
        {
            if (value.Length < minimalLength)
                return;

            string word = value.ToString().ToLower();
            int suffixCount = word.Length - minimalLength;
            for (int i = 0; i <= suffixCount; i++)
                words.Add(i == 0 ? word : word.Substring(i));
        }

             
        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="searchKeywords"></param>
        ///// <param name="useContainsLogic"></param>
        ///// <param name="wordsList"></param>
        //internal void WordsPrepare(string searchKeywords, bool useContainsLogic, ref HashSet<string> wordsList)
        //{
        //    try
        //    {
        //        if (wordsList == null)
        //            wordsList = new HashSet<string>();

        //        if (!useContainsLogic)
        //        {
        //            foreach (var wrd in searchKeywords.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(r=>r.Length >= 2))
        //                wordsList.Add(" " + wrd);

        //            return;
        //        }

        //        StringBuilder sb = new StringBuilder();               
        //        string word = String.Empty;
                
        //        //NO REVIEW
        //        foreach (var c in searchKeywords)
        //        {
        //            if (c == ' ')
        //            {
        //                if (sb.Length >= 2)
        //                {
        //                    word = sb.ToString().ToLower();
        //                    if (!wordsList.Contains(word))
        //                        wordsList.Add(word);
        //                }

        //                if (sb.Length > 0)
        //                    sb.Remove(0, sb.Length);
        //            }
        //            else
        //                sb.Append(c);
        //        }

        //        //Handling last word
        //        {
        //            if (sb.Length >= 2)
        //            {
        //                word = sb.ToString().ToLower();
        //                if (!wordsList.Contains(word))
        //                    wordsList.Add(word);
        //            }

        //            if (sb.Length > 0)
        //                sb.Remove(0, sb.Length);
        //        }

        //        return;
        //    }
        //    catch (Exception ex)
        //    {
        //        throw ex;
        //    }
        //}

        ///// <summary>
        ///// Contains logic
        ///// </summary>
        ///// <param name="searchKeywords"></param>
        ///// <returns></returns>
        //internal HashSet<string> PrepareSearchKeyWords(string searchKeywords)
        //{
        //    try
        //    {
        //        StringBuilder sb = new StringBuilder();
        //        HashSet<string> words = new HashSet<string>();
        //        string word = String.Empty;


        //        Action processWord = () =>
        //        {                    
        //            if (sb.Length >= 2)
        //            {
        //                word = sb.ToString().ToLower();
        //                if (!words.Contains(word))
        //                    words.Add(word);
        //            }

        //            if (sb.Length > 0)
        //                sb.Remove(0, sb.Length);
        //            //sb.Clear();
        //        };


        //        //NO REVIEW
        //        foreach (var c in searchKeywords)
        //        {
        //            if (c == ' ')
        //                processWord();
        //            else 
        //                sb.Append(c);
        //        }


        //        //WITH REVIEW START
        //        //foreach (var c in searchKeywords)
        //        //{
        //        //    if (c == '-' || c == '@')   //Complex names or email address inside
        //        //        continue;

        //        //    if (Char.IsLetterOrDigit(c) || Char.IsSymbol(c))
        //        //    {
        //        //        sb.Append(c);
        //        //    }
        //        //    else
        //        //    {
        //        //        processWord();
        //        //    }
        //        //}
        //        //WITH REVIEW STOP

        //        //Handling last word
        //        processWord();

        //        return words;
        //    }
        //    catch (Exception ex)
        //    {
        //        throw ex;
        //    }
        //}


        /// <summary>
        /// Migrates a non-encrypted TextSearch table to a new encrypted TextSearch table.
        /// <para>The encryptor must be configured in Configuration.TextSearchConfig.TextEncryptor before calling.</para>
        /// <para>After migration the new table is immediately usable in encrypted mode.</para>
        /// <para>The old table is left untouched — the caller may drop it if no longer needed.</para>
        /// </summary>
        /// <param name="oldTableName">Source non-encrypted table name.</param>
        /// <param name="newTableName">Destination encrypted table name (must differ from oldTableName).</param>
        public void MigrateTextSearchTableToEncrypted(string oldTableName, string newTableName)
        {
            if (string.IsNullOrEmpty(oldTableName) || string.IsNullOrEmpty(newTableName))
                throw new ArgumentException("oldTableName and newTableName must be specified");

            if (oldTableName.Equals(newTableName, StringComparison.Ordinal))
                throw new ArgumentException("oldTableName and newTableName must be different");

            ITextStreamCrypto encryptor = tran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.TextEncryptor;
            if (encryptor == null)
                throw new Exception("Encryptor is null (Configuration.TextSearchConfig.TextEncryptor). Set it up with your keys before migrating.");

            Row<byte, int> sourceEncryption = tran.Select<byte, int>(oldTableName, 14);
            if (sourceEncryption.Exists && sourceEncryption.Value != 0)
                throw new InvalidOperationException("DBreeze.TextSearch migration source must be a non-encrypted table.");

            if (tran.Count(newTableName) != 0)
                throw new InvalidOperationException("DBreeze.TextSearch migration destination must be empty.");

            // ── Read-only nested tables from the old (plain) table ──────────────
            NestedTable oldE2i    = tran.SelectTable<byte>(oldTableName, 1, 0);
            NestedTable oldI2e    = tran.SelectTable<byte>(oldTableName, 2, 0);
            NestedTable oldSrch   = tran.SelectTable<byte>(oldTableName, 3, 0);
            NestedTable oldBlocks = tran.SelectTable<byte>(oldTableName, 10, 0);
            NestedTable oldWords  = tran.SelectTable<byte>(oldTableName, 20, 0);

            oldE2i.ValuesLazyLoadingIsOn    = false;
            oldI2e.ValuesLazyLoadingIsOn    = false;
            oldSrch.ValuesLazyLoadingIsOn   = false;
            oldBlocks.ValuesLazyLoadingIsOn = false;
            oldWords.ValuesLazyLoadingIsOn  = false;

            // ── Read scalar config from old table ────────────────────────────────
            var oldLastIndexed  = tran.Select<byte, byte[]>(oldTableName, 4);
            var oldCurrentBlock = tran.Select<int, uint>(oldTableName, 11);
            var oldNumberInBlock= tran.Select<int, uint>(oldTableName, 12);

            // ── Write nested tables into the new (encrypted) table ───────────────
            NestedTable newE2i    = tran.InsertTable<byte>(newTableName, 1, 0);
            NestedTable newI2e    = tran.InsertTable<byte>(newTableName, 2, 0);
            NestedTable newSrch   = tran.InsertTable<byte>(newTableName, 3, 0);
            NestedTable newBlocks = tran.InsertTable<byte>(newTableName, 10, 0);
            NestedTable newWords  = tran.InsertTable<byte>(newTableName, 20, 0);

            newE2i.ValuesLazyLoadingIsOn    = false;
            newI2e.ValuesLazyLoadingIsOn    = false;
            newSrch.ValuesLazyLoadingIsOn   = false;
            newBlocks.ValuesLazyLoadingIsOn = false;
            newWords.ValuesLazyLoadingIsOn  = false;

            // 1. Copy document ID mappings as-is (no encryption needed for ID maps)
            foreach (var row in oldE2i.SelectForward<byte[], int>())
                newE2i.Insert<byte[], int>(row.Key, row.Value);

            foreach (var row in oldI2e.SelectForward<int, byte[]>())
                newI2e.Insert<int, byte[]>(row.Key, row.Value);

            // 2. Copy scalar config as-is
            tran.Insert<byte, byte[]>(newTableName, 4,
                oldLastIndexed.Exists ? oldLastIndexed.Value : DateTime.MinValue.Ticks.To_8_bytes_array_BigEndian());

            tran.Insert<int, uint>(newTableName, 11,
                oldCurrentBlock.Exists ? oldCurrentBlock.Value : 1u);

            tran.Insert<int, uint>(newTableName, 12,
                oldNumberInBlock.Exists ? oldNumberInBlock.Value : 0u);

            // 3. Mark new table as encrypted (mode 1 = ITextStreamCrypto)
            tran.Insert<byte, int>(newTableName, 14, 1);

            // 4. Copy bitmap blocks as-is
            //    Blocks contain WAH-compressed bitmaps keyed by internal integer positions — no word text inside.
            var migratedBlocks = new Dictionary<uint, byte[]>();
            foreach (var row in oldBlocks.SelectForward<uint, byte[]>())
            {
                newBlocks.Insert<uint, byte[]>(row.Key, row.Value);
                migratedBlocks[row.Key] = row.Value;
            }

            // 5. Migrate searchables (key 3): re-encrypt the plain-text word list per document.
            //    Storage format: key = internalDocId[4 bytes] + marker[1 byte] (0=current, 1=pending)
            //    Value (non-encrypted): GZip( UTF-8 bytes of "word1 word2 word3 ..." )
            //    Value (encrypted):     GZip( encryptor.TextEncrypt("word1 word2 word3 ...") )
            var pendingDocumentIds = new HashSet<int>();
            foreach (var row in oldSrch.SelectForward<byte[], byte[]>())
            {
                if (row.Key == null || row.Key.Length != 5 || (row.Key[4] != 0 && row.Key[4] != 1))
                    throw new InvalidDataException("DBreeze.TextSearch: invalid searchables key during migration");
                if (row.Key[4] == 1)
                    pendingDocumentIds.Add(row.Key.Substring(0, 4).To_Int32_BigEndian());

                if (row.Value == null || row.Value.Length == 0)
                {
                    newSrch.Insert<byte[], byte[]>(row.Key, row.Value);
                    continue;
                }
                 
                // Decompress and decrypt from plain UTF-8, then re-encrypt
                string plainText = row.Value.GZip_Decompress().ToUTF8String();
                byte[] reEncrypted = encryptor.TextEncrypt(plainText).GZip_Compress();
                newSrch.Insert<byte[], byte[]>(row.Key, reEncrypted);
            }

            // 6. Migrate word index (key 20): re-key each entry using encrypted byte[] key.
            //    Old table: Insert<string, byte[]>(word, blockRef)
            //    New table: Insert<byte[], byte[]>(encryptor.TextEncrypt(word), blockRef)
            var migratedWords = new Dictionary<string, WordInDocs>(StringComparer.Ordinal);
            foreach (var row in oldWords.SelectForward<string, byte[]>())
            {
                newWords.Insert<byte[], byte[]>(encryptor.TextEncrypt(row.Key), row.Value);
                if (row.Value == null || row.Value.Length < 8)
                    throw new InvalidDataException("DBreeze.TextSearch: invalid word-to-block reference during migration");
                migratedWords[row.Key] = new WordInDocs
                {
                    BlockId = ReadUInt32BigEndian(row.Value, 0),
                    NumberInBlock = ReadUInt32BigEndian(row.Value, 4),
                };
            }

            // A pending row is not part of the copied bitmap yet. Index it now so the migrated
            // table is immediately consistent and does not depend on the source's deferred queue.
            if (pendingDocumentIds.Count != 0)
            {
                var migratedTable = new ITS
                {
                    srch = newSrch,
                    blocks = newBlocks,
                    words = newWords,
                    currentBlock = oldCurrentBlock.Exists ? oldCurrentBlock.Value : 1u,
                    numberInBlock = oldNumberInBlock.Exists ? oldNumberInBlock.Value : 0u,
                    Encryption = 1,
                    ChangedDocIds = pendingDocumentIds,
                    MigratedWords = migratedWords,
                    MigratedBlocks = migratedBlocks,
                };
                var tablesToIndex = new Dictionary<string, ITS>(StringComparer.Ordinal);
                tablesToIndex.Add(newTableName, migratedTable);
                DoIndexing(tran, tablesToIndex);
            }
        }

    }//eoc
}
