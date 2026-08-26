using DBreeze;
using DBreeze.HNSW;
using DBreeze.Transactions;
using DBreeze.Utils;

internal static class VectorRegressionTests
{
    private static readonly string DatabaseTestRoot =
        Environment.GetEnvironmentVariable("DBREEZE_TEST_ROOT") ?? @"D:\Temp\DbreezeDbTest";

    public static void GetAllEnumeratesOnlyExternalIndex()
    {
        string root = CreateFolder(nameof(GetAllEnumeratesOnlyExternalIndex));
        try
        {
            var floatParameters = new Transaction.VectorTableParameters<float[]>
            {
                BucketSize = 2,
                QuantityOfLogicalProcessorToCompute = 1
            };
            var doubleParameters = new Transaction.VectorTableParameters<double[]>
            {
                BucketSize = 2,
                QuantityOfLogicalProcessorToCompute = 1
            };
            var floats = new List<(long, float[])>
            {
                (long.MinValue, new[] { 1f, 0f, 0f }),
                (-1L, new[] { 0f, 1f, 0f }),
                (0L, new[] { 0f, 0f, 1f }),
                (1L, new[] { 1f, 0f, 0f }),
                (long.MaxValue, new[] { 0f, 1f, 0f })
            };
            var doubles = new List<(long, double[])>
            {
                (long.MinValue, new[] { 1d, 0d, 0d }),
                (-1L, new[] { 0d, 1d, 0d }),
                (0L, new[] { 0d, 0d, 1d }),
                (1L, new[] { 1d, 0d, 0d }),
                (long.MaxValue, new[] { 0d, 1d, 0d })
            };

            using (var engine = new DBreezeEngine(root))
            {
                using (Transaction write = engine.GetTransaction())
                {
                    write.SynchronizeTables("vectors-f", "vectors-d");
                    write.VectorsInsert("vectors-f", floats, floatParameters);
                    write.VectorsInsert("vectors-d", doubles, doubleParameters);
                    write.Commit();
                }

                using (Transaction read = engine.GetTransaction())
                {
                    AssertVectorSet(floats, read.VectorsGetAll<float[]>("vectors-f", floatParameters).ToArray());
                    AssertVectorSet(doubles, read.VectorsGetAll<double[]>("vectors-d", doubleParameters).ToArray());
                }

                using (Transaction remove = engine.GetTransaction())
                {
                    remove.SynchronizeTables("vectors-f", "vectors-d");
                    remove.VectorsRemove<float[]>("vectors-f", new List<long> { long.MaxValue }, floatParameters);
                    remove.VectorsRemove<double[]>("vectors-d", new List<long> { long.MaxValue }, doubleParameters);
                    remove.Commit();
                }

                using (Transaction read = engine.GetTransaction())
                {
                    AssertIds(new[] { long.MinValue, -1L, 0L, 1L },
                        read.VectorsGetAll<float[]>("vectors-f", floatParameters, true).Select(static item => item.Item1));
                    AssertIds(floats.Select(static item => item.Item1),
                        read.VectorsGetAll<float[]>("vectors-f", floatParameters, false).Select(static item => item.Item1));
                    AssertIds(new[] { long.MinValue, -1L, 0L, 1L },
                        read.VectorsGetAll<double[]>("vectors-d", doubleParameters, true).Select(static item => item.Item1));
                    AssertIds(doubles.Select(static item => item.Item1),
                        read.VectorsGetAll<double[]>("vectors-d", doubleParameters, false).Select(static item => item.Item1));
                }
            }
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    public static void GetAllSupportsExternalAndQuantizedStorage()
    {
        string root = CreateFolder(nameof(GetAllSupportsExternalAndQuantizedStorage));
        try
        {
            var externalItems = new Dictionary<long, float[]>
            {
                [101] = new[] { 1f, 0f, 0f },
                [102] = new[] { 0f, 1f, 0f },
                [103] = new[] { 0f, 0f, 1f }
            };
            var externalParameters = new Transaction.VectorTableParameters<float[]>
            {
                QuantityOfLogicalProcessorToCompute = 1,
                GetItem = id => externalItems[id]
            };
            var mseParameters = new Transaction.VectorTableParameters<float[]>
            {
                QuantityOfLogicalProcessorToCompute = 1,
                TurboQuant = new TurboQuantParams { BitWidth = 4, Mode = eTurboQuantMode.MSE, RandomSeed = 413 }
            };
            var innerProductParameters = new Transaction.VectorTableParameters<double[]>
            {
                QuantityOfLogicalProcessorToCompute = 1,
                TurboQuant = new TurboQuantParams { BitWidth = 4, Mode = eTurboQuantMode.InnerProduct, RandomSeed = 719 }
            };
            var mse = CreateFloatVectors(201);
            var innerProduct = CreateDoubleVectors(301);

            using (var engine = new DBreezeEngine(root))
            {
                using (Transaction write = engine.GetTransaction())
                {
                    write.SynchronizeTables("vectors-external", "vectors-mse", "vectors-ip");
                    write.VectorsInsert("vectors-external",
                        externalItems.Select(static item => (item.Key, item.Value)).ToList(), externalParameters);
                    write.VectorsInsert("vectors-mse", mse, mseParameters);
                    write.VectorsInsert("vectors-ip", innerProduct, innerProductParameters);
                    write.Commit();
                }

                using (Transaction read = engine.GetTransaction())
                {
                    AssertVectorSet(externalItems.Select(static item => (item.Key, item.Value)),
                        read.VectorsGetAll<float[]>("vectors-external", externalParameters).ToArray());
                    AssertIds(mse.Select(static item => item.Item1),
                        read.VectorsGetAll<float[]>("vectors-mse", mseParameters).Select(static item => item.Item1));
                    AssertIds(innerProduct.Select(static item => item.Item1),
                        read.VectorsGetAll<double[]>("vectors-ip", innerProductParameters).Select(static item => item.Item1));
                }
            }
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    public static void GetAllFailsClosedAndReleasesEnumerationLock()
    {
        string root = CreateFolder(nameof(GetAllFailsClosedAndReleasesEnumerationLock));
        try
        {
            using (var engine = new DBreezeEngine(root))
            {
                using (Transaction empty = engine.GetTransaction())
                    Assert(!empty.VectorsGetAll<float[]>("vectors-empty").Any(), "An empty vector table returned items.");

                using (Transaction seed = engine.GetTransaction())
                {
                    seed.VectorsInsert("vectors-valid", CreateFloatVectors(401));
                    seed.Commit();
                }

                using (Transaction read = engine.GetTransaction())
                {
                    IEnumerator<(long, float[])> enumerator = read.VectorsGetAll<float[]>("vectors-valid").GetEnumerator();
                    Assert(enumerator.MoveNext(), "The valid vector enumeration was empty.");
                    enumerator.Dispose();
                    Assert(read.VectorsCount<float[]>("vectors-valid") == 4,
                        "Early enumerator disposal retained the graph read lock.");
                }

                using (Transaction corrupt = engine.GetTransaction())
                {
                    corrupt.Insert<byte[], byte[]>("vectors-malformed", 4.ToIndex(99L), new byte[7]);
                    corrupt.Commit();
                }

                AssertThrows<InvalidDataException>(() =>
                {
                    using Transaction read = engine.GetTransaction();
                    _ = read.VectorsGetAll<float[]>("vectors-malformed").ToArray();
                });
            }
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    private static List<(long, float[])> CreateFloatVectors(long firstId) => new()
    {
        (firstId, new[] { 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f }),
        (firstId + 1, new[] { 0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f }),
        (firstId + 2, new[] { 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f }),
        (firstId + 3, new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f })
    };

    private static List<(long, double[])> CreateDoubleVectors(long firstId) => new()
    {
        (firstId, new[] { 1d, 0d, 0d, 0d, 0d, 0d, 0d, 0d }),
        (firstId + 1, new[] { 0d, 1d, 0d, 0d, 0d, 0d, 0d, 0d }),
        (firstId + 2, new[] { 0d, 0d, 1d, 0d, 0d, 0d, 0d, 0d }),
        (firstId + 3, new[] { 0d, 0d, 0d, 1d, 0d, 0d, 0d, 0d })
    };

    private static void AssertVectorSet<T>(IEnumerable<(long, T[])> expected, IEnumerable<(long, T[])> actual)
        where T : IEquatable<T>
    {
        Dictionary<long, T[]> expectedById = expected.ToDictionary(static item => item.Item1, static item => item.Item2);
        Dictionary<long, T[]> actualById = actual.ToDictionary(static item => item.Item1, static item => item.Item2);
        AssertIds(expectedById.Keys, actualById.Keys);
        foreach (KeyValuePair<long, T[]> item in expectedById)
            Assert(item.Value.SequenceEqual(actualById[item.Key]), "Vector payload mismatch for external ID " + item.Key + ".");
    }

    private static void AssertIds(IEnumerable<long> expected, IEnumerable<long> actual)
    {
        long[] expectedIds = expected.OrderBy(static value => value).ToArray();
        long[] actualIds = actual.OrderBy(static value => value).ToArray();
        Assert(expectedIds.SequenceEqual(actualIds),
            "External ID mismatch. Expected [" + String.Join(",", expectedIds) + "], actual [" + String.Join(",", actualIds) + "].");
    }

    private static string CreateFolder(string scenario)
    {
        string root = Path.Combine(DatabaseTestRoot, "net8-regressions", scenario + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteFolder(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
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

        throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
