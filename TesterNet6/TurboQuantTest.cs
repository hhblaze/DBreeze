using DBreeze;
using DBreeze.HNSW;
using DBreeze.Transactions;
using System.Diagnostics;
using static DBreeze.Transactions.Transaction;

namespace TesterNet6
{
    /// <summary>
    /// Tests TurboQuant integration with DBreeze VectorLayer.
    /// Tests three modes: No quantization (default), MSE, and InnerProduct.
    /// </summary>
    internal class TurboQuantTest
    {
        static string DBPath = @"D:\Temp\DBVector";

        /// <summary>
        /// Creates random normalized synthetic vectors for testing.
        /// </summary>
        static float[] CreateRandomVector(int dim, Random rng)
        {
            float[] v = new float[dim];
            float sumSq = 0;
            for (int i = 0; i < dim; i++)
            {
                v[i] = (float)(rng.NextDouble() * 2 - 1);
                sumSq += v[i] * v[i];
            }
            float norm = (float)Math.Sqrt(sumSq);
            if (norm > 1e-10f)
                for (int i = 0; i < dim; i++)
                    v[i] /= norm;
            return v;
        }

        /// <summary>
        /// Computes MSE between two vectors.
        /// </summary>
        static double ComputeMse(float[] a, float[] b)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++)
            {
                double diff = a[i] - b[i];
                sum += diff * diff;
            }
            return sum / a.Length;
        }

        /// <summary>
        /// Computes cosine similarity between two vectors.
        /// </summary>
        static double CosineSimilarity(float[] a, float[] b)
        {
            double dot = 0, nA = 0, nB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                nA += a[i] * a[i];
                nB += b[i] * b[i];
            }
            return dot / (Math.Sqrt(nA) * Math.Sqrt(nB));
        }

        public static void Run()
        {
            Console.WriteLine("=== TurboQuant Integration Test ===");
            Console.WriteLine();

            // Use a fresh DB folder for testing
            string testDbPath = Path.Combine(DBPath, "TurboQuantTest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(testDbPath);

            var conf = new DBreezeConfiguration
            {
                DBreezeDataFolderName = testDbPath,
                Storage = DBreezeConfiguration.eStorage.MEMORY
            };

            using var engine = new DBreezeEngine(conf);
            const string tableName = "tq_test";

            const int dim = 1536; // 128;       // test dimension
            const int nVectors = 50;   // insert 50 vectors
            const int queryK = 5;      // top-5 search

            // Generate synthetic vectors
            var rng = new Random(42);
            var vectors = new List<(long, float[])>();
            for (int i = 0; i < nVectors; i++)
                vectors.Add((i + 1, CreateRandomVector(dim, rng)));

            // A known query vector (not in the DB)
            float[] queryVec = CreateRandomVector(dim, new Random(99));

            // ============================================================
            // TEST A: Full Precision (default, no quantization)
            // ============================================================
            Console.WriteLine("--- Test A: Full Precision (STANDARD, backward compatible) ---");
            RunTest(engine, tableName + "_fp", vectors, queryVec, queryK, null, "Full Precision");

            // ============================================================
            // TEST B: MSE Quantization (4-bit)
            // ============================================================
            Console.WriteLine("--- Test B: MSE 4-bit ---");
            var mseParams = new VectorTableParameters<float[]>
            {
                TurboQuant = new TurboQuantParams
                {
                    BitWidth = 4,
                    Mode = eTurboQuantMode.MSE,
                    RandomSeed = 42
                }
            };
            RunTest(engine, tableName + "_mse", vectors, queryVec, queryK, mseParams, "MSE 4-bit");

            // ============================================================
            // TEST C: InnerProduct Quantization (3-bit)
            // ============================================================
            Console.WriteLine("--- Test C: InnerProduct 3-bit ---");
            var prodParams = new VectorTableParameters<float[]>
            {
                TurboQuant = new TurboQuantParams
                {
                    BitWidth = 3,
                    Mode = eTurboQuantMode.InnerProduct,
                    RandomSeed = 42
                }
            };
            RunTest(engine, tableName + "_prod", vectors, queryVec, queryK, prodParams, "InnerProduct 3-bit");

            //// ============================================================
            //// TEST D: MSE Quantization (2-bit) - high compression
            //// ============================================================
            //Console.WriteLine("--- Test D: MSE 2-bit (high compression) ---");
            //var mse2Params = new VectorTableParameters<float[]>
            //{
            //    TurboQuant = new TurboQuantParams
            //    {
            //        BitWidth = 2,
            //        Mode = eTurboQuantMode.MSE,
            //        RandomSeed = 42
            //    }
            //};
            //RunTest(engine, tableName + "_mse2", vectors, queryVec, queryK, mse2Params, "MSE 2-bit");

            // ============================================================
            // Print space comparison
            // ============================================================
            Console.WriteLine();
            Console.WriteLine("=== Storage Size Comparison ===");
            Console.WriteLine($"Full precision: {dim * sizeof(float)} bytes per vector");
            Console.WriteLine($"MSE 4-bit: {2 + 1 + 4 + dim} bytes per vector ({(dim * sizeof(float))} -> {2 + 1 + 4 + dim})");
            Console.WriteLine($"MSE 2-bit: {2 + 1 + 4 + dim} bytes per vector");
            Console.WriteLine($"InnerProduct 3-bit: {2 + 1 + 4 + 4 + dim + dim} bytes per vector");

            Console.WriteLine();
            Console.WriteLine("=== Test Complete ===");

            // Cleanup
            try { Directory.Delete(testDbPath, true); } catch { }
        }

        static void RunTest(DBreezeEngine engine, string tableName,
            List<(long, float[])> vectors, float[] queryVec, int k,
            VectorTableParameters<float[]>? tqp, string label)
        {
            var sw = Stopwatch.StartNew();

            // Insert
            using (var tran = engine.GetTransaction())
            {
                tran.VectorsInsert(tableName, vectors, tqp);
                tran.Commit();
            }
            long insertMs = sw.ElapsedMilliseconds;

            // Search
            sw.Restart();
            List<(long externalId, float distance)> results;
            using (var tran = engine.GetTransaction())
            {
                results = tran.VectorsSearchSimilar(tableName, queryVec, k, tqp).ToList();
                //tran.Commit();
            }
            long searchMs = sw.ElapsedMilliseconds;

            // Get a specific vector to verify storage/retrieval
            sw.Restart();
            List<(long, float[])> retrieved;
            using (var tran = engine.GetTransaction())
            {
                retrieved = tran.VectorsGetByExternalId<float[]>(tableName,
                    vectors.Select(v => v.Item1).Take(3).ToList(), tqp).ToList();
                tran.Commit();
            }
            long getMs = sw.ElapsedMilliseconds;

            // Verify retrieval quality for MSE mode
            double avgMse = 0;
            if (tqp?.TurboQuant.IsEnabled == true && tqp.TurboQuant.Mode == eTurboQuantMode.MSE)
            {
                int count = 0;
                for (int i = 0; i < Math.Min(retrieved.Count, 3); i++)
                {
                    var original = vectors.First(v => v.Item1 == retrieved[i].Item1).Item2;
                    var reconstructed = retrieved[i].Item2;
                    double mse = ComputeMse(original, reconstructed);
                    avgMse += mse;
                    count++;
                }
                if (count > 0) avgMse /= count;
            }

            // Print results
            Console.WriteLine($"  Insert {vectors.Count} vectors: {insertMs} ms");
            Console.WriteLine($"  Search (k={k}):        {searchMs} ms");
            Console.WriteLine($"  Get 3 vectors:         {getMs} ms");
            Console.WriteLine($"  Results: {string.Join(", ", results.Select(r => $"#{r.externalId}(d={r.distance:F4})"))}");
            if (avgMse > 0)
                Console.WriteLine($"  Avg MSE (reconstructed vs original): {avgMse:F6}");
            else
                Console.WriteLine($"  (no quality metrics for this mode)");
            Console.WriteLine();

            // Quality checks
            if (results.Count != k)
                Console.WriteLine($"  ⚠ Expected {k} results, got {results.Count}");
            else
                Console.WriteLine($"  ✓ Found {k} results");

            if (avgMse > 0 && tqp?.TurboQuant.BitWidth == 4 && avgMse > 0.05)
                Console.WriteLine($"  ⚠ MSE {avgMse:F6} higher than expected (~0.009 for 4-bit)");
            else if (avgMse > 0)
                Console.WriteLine($"  ✓ MSE acceptable");
        }
    }
}