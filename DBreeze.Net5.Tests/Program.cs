internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Any(arg => String.Equals(arg, "--textsearch-large-batch", StringComparison.OrdinalIgnoreCase)))
        {
            TextSearchRegressionTests.LargeLexicalBatchFlushesAndReopens();
            Console.WriteLine($"PASS {nameof(TextSearchRegressionTests.LargeLexicalBatchFlushesAndReopens)}");
            return 0;
        }

        (string Name, Action Test)[] tests =
        {
            (nameof(TextSearchRegressionTests.SynchronousIndexingRoundTrips), TextSearchRegressionTests.SynchronousIndexingRoundTrips),
            (nameof(TextSearchRegressionTests.WabiEnumerationAndMergesMatchReferenceModel), TextSearchRegressionTests.WabiEnumerationAndMergesMatchReferenceModel),
            (nameof(TextSearchRegressionTests.InvalidParserConfigurationFailsEarly), TextSearchRegressionTests.InvalidParserConfigurationFailsEarly),
            (nameof(TextSearchRegressionTests.CompositionHandlesMissingTermsAndReusableBlocks), TextSearchRegressionTests.CompositionHandlesMissingTermsAndReusableBlocks),
            (nameof(TextSearchRegressionTests.QueryParametersAreSinglePassAndTableScoped), TextSearchRegressionTests.QueryParametersAreSinglePassAndTableScoped),
            (nameof(TextSearchRegressionTests.ExternalRangesAreBoundedAndCanBeOneSided), TextSearchRegressionTests.ExternalRangesAreBoundedAndCanBeOneSided),
            (nameof(TextSearchRegressionTests.MutationsRemoveEmptyWordsAndBlocks), TextSearchRegressionTests.MutationsRemoveEmptyWordsAndBlocks),
            (nameof(TextSearchRegressionTests.CryptoVectorsAndEncryptedSearchRemainCompatible), TextSearchRegressionTests.CryptoVectorsAndEncryptedSearchRemainCompatible),
            (nameof(TextSearchRegressionTests.LexicalWordBatchesPreserveTriePrefixLocality), TextSearchRegressionTests.LexicalWordBatchesPreserveTriePrefixLocality),
            (nameof(TextSearchRegressionTests.MigrationValidatesAndIndexesPendingRows), TextSearchRegressionTests.MigrationValidatesAndIndexesPendingRows),
            (nameof(TextSearchRegressionTests.RandomizedCompositionMatchesSetModel), TextSearchRegressionTests.RandomizedCompositionMatchesSetModel),
            (nameof(TextSearchRegressionTests.DiskIndexReopensAndUpdates), TextSearchRegressionTests.DiskIndexReopensAndUpdates),
        };

        try
        {
            foreach (var test in tests)
            {
                test.Test();
                Console.WriteLine($"PASS {test.Name}");
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
