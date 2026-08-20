using DBreeze;
using DBreeze.TextSearch;
using DBreeze.Utils;
using System.Reflection;

internal static class TextSearchRegressionTests
{
    private static readonly string DatabaseTestRoot = @"D:\Temp\DbreezeDbTest";
    public static void WabiEnumerationAndMergesMatchReferenceModel()
    {
        Type type = typeof(DBreezeEngine).Assembly.GetType("DBreeze.TextSearch.WABI", throwOnError: true);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo enumerate = type.GetMethod(
            "TextSearch_AND_logic", flags, binder: null, new[] { typeof(List<byte[]>) }, modifiers: null);
        MethodInfo enumerateRange = type.GetMethod(
            "TextSearch_AND_logic", flags, binder: null,
            new[] { typeof(List<byte[]>), typeof(int), typeof(int), typeof(bool) }, modifiers: null);
        MethodInfo mergeAnd = type.GetMethod("MergeByAndLogic", flags);
        MethodInfo mergeOr = type.GetMethod("MergeByOrLogic", flags);
        MethodInfo mergeXor = type.GetMethod("MergeByXorLogic", flags);

        var random = new Random(90431);
        for (int iteration = 0; iteration < 160; iteration++)
        {
            int count = random.Next(1, 17);
            int maximumLength = random.Next(1, 257);
            var bitmaps = new List<byte[]>(count);
            for (int bitmapIndex = 0; bitmapIndex < count; bitmapIndex++)
            {
                byte[] bitmap = new byte[random.Next(1, maximumLength + 1)];
                random.NextBytes(bitmap);
                bitmaps.Add(bitmap);
            }

            int minimumLength = bitmaps.Min(static bitmap => bitmap.Length);
            var expectedDescending = new List<uint>();
            for (int documentId = checked(minimumLength * 8 - 1); documentId >= 0; documentId--)
            {
                bool present = true;
                for (int bitmapIndex = 0; bitmapIndex < count; bitmapIndex++)
                    present &= (bitmaps[bitmapIndex][documentId >> 3] & (1 << (documentId & 7))) != 0;
                if (present)
                    expectedDescending.Add((uint)documentId);
            }

            var actualDescending = ((IEnumerable<uint>)enumerate.Invoke(null, new object[] { bitmaps })).ToArray();
            AssertSequence(expectedDescending.Select(static id => checked((int)id)).ToArray(),
                actualDescending.Select(static id => checked((int)id)).ToArray(),
                "WABI no-range enumeration differs from the bitmap model.");

            int lower = random.Next(1, minimumLength * 8);
            int upper = random.Next(lower, minimumLength * 8);
            uint[] expectedAscending = expectedDescending
                .Where(id => id >= lower && id <= upper)
                .OrderBy(static id => id)
                .ToArray();
            uint[] actualAscending = ((IEnumerable<uint>)enumerateRange.Invoke(
                null, new object[] { bitmaps, lower, upper, false })).ToArray();
            AssertUIntSequence(expectedAscending, actualAscending, "WABI ascending range masks are incorrect.");

            uint[] expectedRangeDescending = expectedAscending.Reverse().ToArray();
            uint[] actualRangeDescending = ((IEnumerable<uint>)enumerateRange.Invoke(
                null, new object[] { bitmaps, upper, lower, true })).ToArray();
            AssertUIntSequence(expectedRangeDescending, actualRangeDescending,
                "WABI descending range masks are incorrect.");

            AssertBitmap(MergeReference(bitmaps, byte.MaxValue, static (left, right) => (byte)(left & right)),
                (byte[])mergeAnd.Invoke(null, new object[] { bitmaps }), "WABI AND merge differs from the model.");
            AssertBitmap(MergeReference(bitmaps, 0, static (left, right) => (byte)(left | right)),
                (byte[])mergeOr.Invoke(null, new object[] { bitmaps }), "WABI OR merge differs from the model.");
            AssertBitmap(MergeReference(bitmaps, 0, static (left, right) => (byte)(left ^ right)),
                (byte[])mergeXor.Invoke(null, new object[] { bitmaps }), "WABI XOR merge differs from the model.");
        }
    }

    public static void SynchronousIndexingRoundTrips()
    {
        using var engine = CreateMemoryEngine();
        Insert(engine, "text-sync", (1, "alpha premium", "tagone"));
        AssertSequence(new[] { 1 }, Query(engine, "text-sync", table => table.BlockAnd("premium")),
            "Synchronous TextSearch indexing failed.");
    }

    public static void InvalidParserConfigurationFailsEarly()
    {
        var configuration = new DBreezeConfiguration
        {
            Storage = DBreezeConfiguration.eStorage.MEMORY,
            NotifyAhead_WhenWriteTablePossibleDeadlock = false,
        };
        configuration.TextSearchConfig.MaximalWordSize = 0;
        using var engine = new DBreezeEngine(configuration);
        using var transaction = engine.GetTransaction();
        AssertThrows<ArgumentOutOfRangeException>(() =>
            transaction.TextInsert("text-invalid-parser", new byte[] { 1 }, "alpha", null));
    }

    public static void CompositionHandlesMissingTermsAndReusableBlocks()
    {
        using var engine = CreateMemoryEngine();
        Insert(engine, "text-compose", (1, "alpha beta", ""), (2, "alpha gamma", ""), (3, "gamma", ""));

        AssertSequence(new[] { 2, 1 }, Query(engine, "text-compose",
            table => table.BlockOr("missing").Or("alpha")), "missing OR existing failed.");

        using var transaction = engine.GetTransaction();
        TextSearchTable search = transaction.TextSearch("text-compose");
        SBlock reusable = search.BlockAnd("alpha");
        AssertSequence(new[] { 1 }, ToIds(reusable.And("beta").GetDocumentIDs()), "Reusable block AND failed.");
        AssertSequence(new[] { 3, 2, 1 }, ToIds(reusable.Or("gamma").GetDocumentIDs()),
            "A cached child block was mutated by an earlier composition.");
    }

    public static void QueryParametersAreSinglePassAndTableScoped()
    {
        using var engine = CreateMemoryEngine();
        Insert(engine, "text-parameters", (1, "alpha", ""));

        using var transaction = engine.GetTransaction();
        TextSearchTable search = transaction.TextSearch("text-parameters");
        SBlock block = search.BlockAnd(new OneShotEnumerable("alpha"), null, true);
        AssertSequence(new[] { 1 }, ToIds(block.GetDocumentIDs()), "One-shot query enumerable was consumed more than once.");

        SBlock alpha = search.BlockAnd("alpha");
        AssertSequence(new[] { 1 }, ToIds(alpha.GetDocumentIDs()), "A repeated pure block query failed.");
        SBlock ignored = alpha.And("   ", "", ignoreOnEmptyParameters: true);
        if (!ReferenceEquals(alpha, ignored))
            throw new InvalidOperationException("Whitespace-only block was not ignored.");
        AssertSequence(new[] { 1 }, ToIds(ignored.GetDocumentIDs()), "Whitespace-only ignored parameter changed the query.");

        TextSearchTable other = transaction.TextSearch("text-other");
        SBlock foreign = other.BlockAnd("alpha");
        AssertThrows<ArgumentException>(() => search.BlockAnd("alpha").Or(foreign));
    }

    public static void ExternalRangesAreBoundedAndCanBeOneSided()
    {
        using var engine = CreateMemoryEngine();
        Insert(engine, "text-ranges", (10, "alpha", ""), (20, "alpha", ""), (30, "alpha", ""));

        AssertSequence(new[] { 20 }, QueryRange(engine, true, 25, 15), "Descending two-sided range failed.");
        AssertSequence(new[] { 20, 10 }, QueryRange(engine, true, 25, null), "Descending start-only range failed.");
        AssertSequence(new[] { 30, 20 }, QueryRange(engine, true, null, 15), "Descending stop-only range failed.");
        AssertSequence(new[] { 30 }, QueryRange(engine, true, 30, 30), "Descending exact-bound range failed.");
        AssertSequence(Array.Empty<int>(), QueryRange(engine, true, 15, 15), "Empty descending range returned documents.");
        AssertSequence(Array.Empty<int>(), QueryRange(engine, true, 15, 25), "Inverted descending range returned documents.");
        AssertSequence(Array.Empty<int>(), QueryRange(engine, true, 5, null), "Descending non-overlapping range returned documents.");
        AssertSequence(Array.Empty<int>(), QueryRange(engine, true, null, 35), "Descending upper-gap range returned documents.");

        AssertSequence(new[] { 20 }, QueryRange(engine, false, 15, 25), "Ascending two-sided range failed.");
        AssertSequence(new[] { 20, 30 }, QueryRange(engine, false, 15, null), "Ascending start-only range failed.");
        AssertSequence(new[] { 10, 20 }, QueryRange(engine, false, null, 25), "Ascending stop-only range failed.");
        AssertSequence(new[] { 10 }, QueryRange(engine, false, 10, 10), "Ascending exact-bound range failed.");
        AssertSequence(Array.Empty<int>(), QueryRange(engine, false, 15, 15), "Empty ascending range returned documents.");
        AssertSequence(Array.Empty<int>(), QueryRange(engine, false, 25, 15), "Inverted ascending range returned documents.");
        AssertSequence(Array.Empty<int>(), QueryRange(engine, false, 35, null), "Ascending non-overlapping range returned documents.");
        AssertSequence(Array.Empty<int>(), QueryRange(engine, false, null, 5), "Ascending lower-gap range returned documents.");
    }

    public static void MutationsRemoveEmptyWordsAndBlocks()
    {
        using var engine = CreateMemoryEngine();
        Insert(engine, "text-mutations", (1, null, "tagone"));
        AssertSequence(new[] { 1 }, Query(engine, "text-mutations", table => table.BlockAnd("", "tagone")), "Exact insert failed.");

        using (var transaction = engine.GetTransaction())
        {
            transaction.TextAppend("text-mutations", new byte[] { 1 }, null, "tagtwo");
            transaction.Commit();
        }
        AssertSequence(new[] { 1 }, Query(engine, "text-mutations", table => table.BlockAnd("", "tagtwo")), "Append failed.");

        using (var transaction = engine.GetTransaction())
        {
            transaction.TextRemove("text-mutations", new byte[] { 1 }, "tagone");
            transaction.Commit();
        }
        AssertSequence(Array.Empty<int>(), Query(engine, "text-mutations", table => table.BlockAnd("", "tagone")), "Remove failed.");

        using (var transaction = engine.GetTransaction())
        {
            transaction.TextRemoveAll("text-mutations", new byte[] { 1 });
            transaction.Commit();
        }
        AssertSequence(Array.Empty<int>(), Query(engine, "text-mutations", table => table.BlockAnd("", "tagtwo")), "RemoveAll failed.");

        using (var transaction = engine.GetTransaction())
        {
            var words = transaction.SelectTable<byte>("text-mutations", 20, 0);
            var blocks = transaction.SelectTable<byte>("text-mutations", 10, 0);
            if (words.Count() != 0 || blocks.Count() != 0)
                throw new InvalidOperationException("Empty word/block entries remained after clearing the last bitmap bit.");
        }
    }

    public static void CryptoVectorsAndEncryptedSearchRemainCompatible()
    {
        byte[] key = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        byte[] iv = Enumerable.Range(0, 16).Select(value => (byte)(15 - value)).ToArray();
        var crypto = new WabiStreamCrypto(key, iv);

        AssertCiphertext(crypto, "", "");
        AssertCiphertext(crypto, "a", "13");
        AssertCiphertext(crypto, "123456789012345", "4383D00C794578134A9AF5F8B976B0");
        AssertCiphertext(crypto, "1234567890123456", "4383D00C794578134A9AF5F8B976B097");
        AssertCiphertext(crypto, "12345678901234567", "4383D00C794578134A9AF5F8B976B097D8");
        AssertCiphertext(crypto, "Привет 周杰伦", "A22E32B89CCB9F99A31F1548AAA7140909E0F1CE7D72");
        AssertEncryptedPrefix(crypto, "123456789012345", "12345678901234567-crosses-aes-block");
        AssertEncryptedPrefix(crypto, "Привет ", "Привет 周杰伦 и DBreeze");

        byte[] stable = crypto.TextEncrypt("stable");
        Array.Clear(key);
        Array.Clear(iv);
        if (!stable.SequenceEqual(crypto.TextEncrypt("stable")))
            throw new InvalidOperationException("WabiStreamCrypto retained mutable caller key/IV arrays.");

        AssertThrows<ArgumentException>(() => new WabiStreamCrypto(new byte[15], new byte[16]));
        AssertThrows<ArgumentException>(() => new WabiStreamCrypto(new byte[16], new byte[15]));

        using var engine = CreateMemoryEngine(crypto, true);
        Insert(engine, "text-encrypted", (1, "premium prefixable", "exacttag"));
        AssertSequence(new[] { 1 }, Query(engine, "text-encrypted", table => table.BlockAnd("prem")), "Encrypted prefix search failed.");
        AssertSequence(new[] { 1 }, Query(engine, "text-encrypted", table => table.BlockAnd("", "exacttag")), "Encrypted exact search failed.");
    }

    public static void LexicalWordBatchesPreserveTriePrefixLocality()
    {
        byte[] key = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        byte[] iv = Enumerable.Range(0, 16).Select(value => (byte)(15 - value)).ToArray();
        VerifyLexicalWordBatch(encryptor: null, useCrypto: false, "plain");
        VerifyLexicalWordBatch(new WabiStreamCrypto(key, iv), useCrypto: true, "encrypted");
    }

    public static void LargeLexicalBatchFlushesAndReopens()
    {
        const int intermediateBatchCount = 100001;
        const int uniqueWordCount = 100005;
        string folder = Path.Combine(DatabaseTestRoot, nameof(LargeLexicalBatchFlushesAndReopens), Guid.NewGuid().ToString("N"));
        try
        {
            var exactWords = new System.Text.StringBuilder(uniqueWordCount * 16);
            for (int i = uniqueWordCount - 1; i >= 0; i--)
            {
                if (i != uniqueWordCount - 1)
                    exactWords.Append(' ');
                exactWords.Append("batchword").Append(i.ToString("D6"));
            }

            using (var engine = CreateDiskEngine(folder))
            using (var transaction = engine.GetTransaction())
            {
                transaction.TextInsert("text-large-batch", new byte[] { 1 }, null, exactWords.ToString());
                transaction.Commit();
            }

            using (var engine = CreateDiskEngine(folder))
            {
                using (var transaction = engine.GetTransaction())
                {
                    var words = transaction.SelectTable<byte>("text-large-batch", 20, 0);
                    if (words.Count() != (ulong)uniqueWordCount)
                        throw new InvalidOperationException("The >100,000 word-reference batch was not persisted completely.");

                    var insertionOrder = words.SelectForward<string, byte[]>()
                        .Select(row => (Pointer: row.LinkToValue.To_UInt64_BigEndian(), Word: row.Key))
                        .OrderBy(row => row.Pointer)
                        .Select(row => row.Word)
                        .ToArray();
                    AssertOrdinalInsertionOrder(insertionOrder, 0, intermediateBatchCount);
                    AssertOrdinalInsertionOrder(insertionOrder, intermediateBatchCount,
                        uniqueWordCount - intermediateBatchCount);
                }

                AssertSequence(new[] { 1 }, Query(engine, "text-large-batch",
                    table => table.BlockAnd("", "batchword100004")), "Intermediate-batch word was not searchable after reopen.");
                AssertSequence(new[] { 1 }, Query(engine, "text-large-batch",
                    table => table.BlockAnd("", "batchword000000")), "Final-flush word was not searchable after reopen.");
            }
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    public static void MigrationValidatesAndIndexesPendingRows()
    {
        byte[] key = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        byte[] iv = Enumerable.Range(0, 16).Select(value => (byte)(15 - value)).ToArray();
        var crypto = new WabiStreamCrypto(key, iv);
        using var engine = CreateMemoryEngine(crypto, false);
        Insert(engine, "text-migration-source", (1, "alpha", ""), (2, "beta", ""));

        using (var transaction = engine.GetTransaction())
        {
            var searchables = transaction.InsertTable<byte>("text-migration-source", 3, 0);
            searchables.Insert<byte[], byte[]>(1.To_4_bytes_array_BigEndian().Concat(new byte[] { 1 }), "beta ".To_UTF8Bytes().GZip_Compress());
            transaction.Commit();
        }

        using (var transaction = engine.GetTransaction())
        {
            transaction.Support_Migration_EncryptTextSearchTable("text-migration-source", "text-migration-destination");
            transaction.Commit();
        }

        AssertSequence(new[] { 2, 1 }, Query(engine, "text-migration-destination", table => table.BlockAnd("beta")),
            "Migration did not merge a pending searchable row into an existing word bitmap.");
        AssertSequence(Array.Empty<int>(), Query(engine, "text-migration-destination", table => table.BlockAnd("alpha")),
            "Migration retained the stale bitmap for a pending row.");

        using (var transaction = engine.GetTransaction())
        {
            transaction.Insert("text-migration-nonempty", 1, 1);
            transaction.Commit();
        }
        using (var transaction = engine.GetTransaction())
            AssertThrows<InvalidOperationException>(() => transaction.Support_Migration_EncryptTextSearchTable("text-migration-source", "text-migration-nonempty"));

        using var encryptedEngine = CreateMemoryEngine(crypto, true);
        Insert(encryptedEngine, "text-encrypted-source", (1, "alpha", ""));
        using var encryptedTransaction = encryptedEngine.GetTransaction();
        AssertThrows<InvalidOperationException>(() => encryptedTransaction.Support_Migration_EncryptTextSearchTable("text-encrypted-source", "text-encrypted-target"));
    }

    public static void RandomizedCompositionMatchesSetModel()
    {
        const int documentCount = 64;
        string[] vocabulary = { "alpha", "beta", "gamma", "delta", "epsilon", "missing" };
        var model = vocabulary.ToDictionary(word => word, _ => new HashSet<int>(), StringComparer.Ordinal);
        var random = new Random(0x5EED);

        using var engine = CreateMemoryEngine();
        using (var transaction = engine.GetTransaction())
        {
            for (int id = 1; id <= documentCount; id++)
            {
                var documentWords = new List<string>();
                for (int wordIndex = 0; wordIndex < vocabulary.Length - 1; wordIndex++)
                {
                    if (random.Next(3) == 0)
                    {
                        string word = vocabulary[wordIndex];
                        documentWords.Add(word);
                        model[word].Add(id);
                    }
                }

                transaction.TextInsert("text-random", new[] { (byte)id }, null, String.Join(" ", documentWords));
            }
            transaction.Commit();
        }

        using var queryTransaction = engine.GetTransaction();
        TextSearchTable search = queryTransaction.TextSearch("text-random");
        for (int iteration = 0; iteration < 200; iteration++)
        {
            string leftWord = vocabulary[random.Next(vocabulary.Length)];
            string rightWord = vocabulary[random.Next(vocabulary.Length)];
            int operation = random.Next(4);
            SBlock left = search.BlockAnd("", leftWord);
            SBlock right = search.BlockAnd("", rightWord);
            SBlock expression;
            IEnumerable<int> expected;

            switch (operation)
            {
                case 0:
                    expression = left.And(right);
                    expected = model[leftWord].Intersect(model[rightWord]);
                    break;
                case 1:
                    expression = left.Or(right);
                    expected = model[leftWord].Union(model[rightWord]);
                    break;
                case 2:
                    expression = left.Xor(right);
                    expected = model[leftWord].SymmetricExcept(model[rightWord]);
                    break;
                default:
                    expression = left.Exclude(right);
                    expected = model[leftWord].Except(model[rightWord]);
                    break;
            }

            AssertSequence(expected.OrderByDescending(id => id).ToArray(), ToIds(expression.GetDocumentIDs()),
                $"Randomized composition mismatch at iteration {iteration}.");
        }
    }

    public static void DiskIndexReopensAndUpdates()
    {
        string folder = Path.Combine(DatabaseTestRoot, nameof(DiskIndexReopensAndUpdates), Guid.NewGuid().ToString("N"));
        try
        {
            using (var engine = new DBreezeEngine(folder))
                Insert(engine, "text-reopen", (1, "alpha prefixable", "exacttag"), (2, "alpha", ""));

            using (var engine = new DBreezeEngine(folder))
            {
                AssertSequence(new[] { 2, 1 }, Query(engine, "text-reopen", table => table.BlockAnd("alpha")),
                    "Reopened prefix index differs from the persisted index.");
                AssertSequence(new[] { 1 }, Query(engine, "text-reopen", table => table.BlockAnd("", "exacttag")),
                    "Reopened exact index differs from the persisted index.");
                Insert(engine, "text-reopen", (1, "beta", "replacement"));
            }

            using (var engine = new DBreezeEngine(folder))
            {
                AssertSequence(new[] { 2 }, Query(engine, "text-reopen", table => table.BlockAnd("alpha")),
                    "Reopened update retained stale words.");
                AssertSequence(new[] { 1 }, Query(engine, "text-reopen", table => table.BlockAnd("beta")),
                    "Reopened update did not persist new words.");
            }
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void VerifyLexicalWordBatch(WabiStreamCrypto encryptor, bool useCrypto, string suffix)
    {
        string folder = Path.Combine(DatabaseTestRoot, "textsearch-lexical-" + suffix, Guid.NewGuid().ToString("N"));
        const string tableName = "text-lexical";
        try
        {
            using (var engine = CreateDiskEngine(folder, encryptor, useCrypto))
            using (var transaction = engine.GetTransaction())
            {
                transaction.TextInsert(tableName, new byte[] { 1 }, "zulu", "omega alpha");
                transaction.TextInsert(tableName, new byte[] { 2 }, "prefixable", "beta alphabet");
                transaction.TextInsert(tableName, new byte[] { 3 }, "alpine", "gamma bravo");
                transaction.Commit();
            }

            using (var engine = CreateDiskEngine(folder, encryptor, useCrypto))
            {
                AssertSequence(new[] { 2 }, Query(engine, tableName, table => table.BlockAnd("pref")),
                    "Prefix search failed after lexical batch reopen.");
                AssertSequence(new[] { 1 }, Query(engine, tableName, table => table.BlockAnd("", "alpha")),
                    "Exact search failed after lexical batch reopen.");

                using var transaction = engine.GetTransaction();
                var words = transaction.SelectTable<byte>(tableName, 20, 0);
                var insertionOrder = new List<(ulong Pointer, string Word)>();
                if (encryptor == null)
                {
                    foreach (var row in words.SelectForward<string, byte[]>())
                        insertionOrder.Add((row.LinkToValue.To_UInt64_BigEndian(), row.Key));
                }
                else
                {
                    foreach (var row in words.SelectForward<byte[], byte[]>())
                        insertionOrder.Add((row.LinkToValue.To_UInt64_BigEndian(), encryptor.TextDecrypt(row.Key)));
                }

                insertionOrder.Sort(static (left, right) => left.Pointer.CompareTo(right.Pointer));
                AssertOrdinalInsertionOrder(insertionOrder.Select(static item => item.Word).ToArray(),
                    0, insertionOrder.Count);
            }
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void AssertOrdinalInsertionOrder(string[] words, int offset, int count)
    {
        int end = checked(offset + count);
        for (int i = offset + 1; i < end; i++)
        {
            if (StringComparer.Ordinal.Compare(words[i - 1], words[i]) > 0)
            {
                throw new InvalidOperationException(
                    $"Word-reference insertion lost ordinal prefix locality: '{words[i - 1]}' before '{words[i]}'.");
            }
        }
    }

    private static int[] QueryRange(DBreezeEngine engine, bool descending, int? start, int? stop)
    {
        return Query(engine, "text-ranges", table =>
        {
            table.Descending = descending;
            table.ExternalDocumentIdStart = start.HasValue ? new[] { (byte)start.Value } : null;
            table.ExternalDocumentIdStop = stop.HasValue ? new[] { (byte)stop.Value } : null;
            return table.BlockAnd("alpha");
        });
    }

    private static DBreezeEngine CreateMemoryEngine(ITextStreamCrypto crypto = null, bool useCrypto = false)
    {
        var configuration = new DBreezeConfiguration
        {
            Storage = DBreezeConfiguration.eStorage.MEMORY,
            NotifyAhead_WhenWriteTablePossibleDeadlock = false,
        };
        configuration.TextSearchConfig.TextEncryptor = crypto;
        configuration.TextSearchConfig.UseTextEncryptor = useCrypto;
        return new DBreezeEngine(configuration);
    }

    private static DBreezeEngine CreateDiskEngine(string folder, ITextStreamCrypto crypto = null, bool useCrypto = false)
    {
        var configuration = new DBreezeConfiguration
        {
            DBreezeDataFolderName = folder,
            NotifyAhead_WhenWriteTablePossibleDeadlock = false,
        };
        configuration.TextSearchConfig.TextEncryptor = crypto;
        configuration.TextSearchConfig.UseTextEncryptor = useCrypto;
        return new DBreezeEngine(configuration);
    }

    private static void Insert(DBreezeEngine engine, string table, params (int Id, string Contains, string Exact)[] documents)
    {
        using var transaction = engine.GetTransaction();
        for (int i = 0; i < documents.Length; i++)
            transaction.TextInsert(table, new[] { (byte)documents[i].Id }, documents[i].Contains, documents[i].Exact);
        transaction.Commit();
    }

    private static int[] Query(DBreezeEngine engine, string table, Func<TextSearchTable, SBlock> createBlock)
    {
        using var transaction = engine.GetTransaction();
        return ToIds(createBlock(transaction.TextSearch(table)).GetDocumentIDs());
    }

    private static int[] ToIds(IEnumerable<byte[]> ids) => ids.Select(id => (int)id[0]).ToArray();

    private static byte[] MergeReference(List<byte[]> bitmaps, byte seed, Func<byte, byte, byte> merge)
    {
        byte[] result = new byte[bitmaps.Max(static bitmap => bitmap.Length)];
        for (int byteIndex = 0; byteIndex < result.Length; byteIndex++)
        {
            byte value = seed;
            for (int bitmapIndex = 0; bitmapIndex < bitmaps.Count; bitmapIndex++)
            {
                byte operand = byteIndex < bitmaps[bitmapIndex].Length ? bitmaps[bitmapIndex][byteIndex] : (byte)0;
                value = merge(value, operand);
            }
            result[byteIndex] = value;
        }

        int length = result.Length;
        while (length > 0 && result[length - 1] == 0)
            length--;
        if (length == 0)
            return null;
        if (length != result.Length)
            Array.Resize(ref result, length);
        return result;
    }

    private static void AssertUIntSequence(uint[] expected, uint[] actual, string message)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"{message} Expected [{string.Join(",", expected)}], actual [{string.Join(",", actual)}].");
    }

    private static void AssertBitmap(byte[] expected, byte[] actual, string message)
    {
        if (expected == null || actual == null)
        {
            if (expected != actual)
                throw new InvalidOperationException(message);
            return;
        }

        if (!expected.AsSpan().SequenceEqual(actual))
            throw new InvalidOperationException(message);
    }

    private static void AssertSequence(int[] expected, int[] actual, string message)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"{message} Expected [{string.Join(",", expected)}], actual [{string.Join(",", actual)}].");
    }

    private static void AssertCiphertext(WabiStreamCrypto crypto, string plaintext, string expectedHex)
    {
        byte[] encrypted = crypto.TextEncrypt(plaintext);
        string actualHex = Convert.ToHexString(encrypted);
        if (!String.Equals(expectedHex, actualHex, StringComparison.Ordinal))
            throw new InvalidOperationException($"AES-CTR vector mismatch for length {encrypted.Length}: {actualHex}.");
        if (!String.Equals(plaintext, crypto.TextDecrypt(encrypted), StringComparison.Ordinal))
            throw new InvalidOperationException("AES-CTR roundtrip failed.");
    }

    private static void AssertEncryptedPrefix(WabiStreamCrypto crypto, string prefix, string text)
    {
        byte[] encryptedPrefix = crypto.TextEncrypt(prefix);
        byte[] encryptedText = crypto.TextEncrypt(text);
        if (encryptedText.Length < encryptedPrefix.Length
            || !encryptedText.AsSpan(0, encryptedPrefix.Length).SequenceEqual(encryptedPrefix))
        {
            throw new InvalidOperationException("AES-CTR no longer preserves TextSearch prefixes.");
        }
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed class OneShotEnumerable : IEnumerable<string>
    {
        private readonly string _value;
        private bool _enumerated;

        public OneShotEnumerable(string value) => _value = value;

        public IEnumerator<string> GetEnumerator()
        {
            if (_enumerated)
                throw new InvalidOperationException("The enumerable was requested twice.");
            _enumerated = true;
            yield return _value;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static IEnumerable<int> SymmetricExcept(this IEnumerable<int> left, IEnumerable<int> right)
    {
        var result = new HashSet<int>(left);
        result.SymmetricExceptWith(right);
        return result;
    }
}
