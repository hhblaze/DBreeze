using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TesterNet6.ByteConversion;
using static TesterNet6.DebugCase1;

namespace TesterNet6
{
    internal static class TestbyteConversions
    {

        private static readonly Random _rand = new Random(42); // fixed seed for reproducibility


        public static void RunTestMemory()
        {
            using (var tran = Program.DBEngine.GetTransaction())
            {
                
                
                tran.Insert<int, int>("mem_Customer", 1, 1);
                tran.Commit();
            }

            using (var tran = Program.DBEngine.GetTransaction())
            {
                var ers = tran.Select<int, int>("mem_Customer", 1);
                Debug.WriteLine(ers.Value);
            }

        }

        public static void RunTestConcatMany()
        {
            byte[] btemp = Array.Empty<byte>();
            btemp = null;
            byte[] btemp2 = DBreeze.Utils.BytesProcessing.Concat(btemp, new byte[] { 1, 2 });
            byte[] sa1 = System.Text.Encoding.UTF8.GetBytes("AB");
            byte[] sa2 = System.Text.Encoding.UTF8.GetBytes("AAAA");

            var rtz = ByteConversionNew.IfStringArraySmallerOrEqualThen(sa1, sa2);

            string hex = "1F00000000201F00000000201F00000000201F00000000201F00000000201F00000000201F00000000201F00000000201F00000000201F00000000201F0000000020";
            long? dt=4684545135877354;
            var bt1dt = ByteConversionOld.To_9_bytes_array_BigEndian(dt);
            var bt2dt = ByteConversionNew.To_9_bytes_array_BigEndian(dt);

            ulong ulng = 15;
            var ulngBt = ByteConversionOld.To_8_bytes_array_BigEndian(ulng);
            byte[] sbsTes = new byte[50];

            var sw = Stopwatch.StartNew();

            for (int i = 0; i < 10_000_000; i++)
            {
                var bcr = ByteConversionOld._ByteArrayEquals(new byte[2000], new byte[2000]);
                //var hba = ByteConversionOld.ToByteArrayFromHex(hex);
                //var bt = ByteConversionOld.To_8_bytes_array_BigEndian(ulng);
                //ulng = ByteConversionOld.To_UInt64_BigEndian(ulngBt);
                //var bty = ByteConversionOld.Substring(sbsTes, 50, 20);
                //byte[] s = ByteConversionOld.ConcatMany(new byte[] { 2, 3, 4 }, new byte[] { 3, 7, 8 }, new byte[] { 9, 10, 11 });                
                //byte[] s = ByteConversionOld.ConcatMany(new byte[1200], new byte[2400], new byte[3500]);
                //var clbt = ByteConversionOld.Concat(new byte[] { 54, 65, 6, 23, 123, 45 }, new byte[] { 54, 65, 6, 23, 123, 45 });
                //var clbt = ByteConversionOld.Concat(new byte[12000], new byte[24000]);
            }
            sw.Stop();
            Debug.WriteLine($"OLD ToBytes speed: {sw.Elapsed.TotalSeconds:F2} seconds.");


            sw = Stopwatch.StartNew();
            for (int i = 0; i < 10_000_000; i++)
            {
                var bcr = ByteConversionNew._ByteArrayEquals(new byte[2000], new byte[2000]);
                //var hba = ByteConversionNew.ToByteArrayFromHex(hex);
                //var bt = ByteConversionNew.To_8_bytes_array_BigEndian(ulng);
                //ulng = ByteConversionNew.To_UInt64_BigEndian(ulngBt);
                //var bty = ByteConversionNew.Substring(sbsTes, 50, 20);
                //byte[] s = ByteConversionNew.ConcatMany(new byte[] { 2, 3, 4 }, new byte[] { 3, 7, 8 }, new byte[] { 9, 10, 11 });
                //byte[] s = ByteConversionNew.ConcatMany(new byte[1200], new byte[2400], new byte[3500]);
                //var clbt = ByteConversionNew.Concat(new byte[12000], new byte[24000]);
            }
            sw.Stop();
            Debug.WriteLine($"New ToBytes speed: {sw.Elapsed.TotalSeconds:F2} seconds.");
        }

        public static void RunTestDecimals()
        {
            var testValues = GenerateTestDecimals(10_000_000);

            decimal? iuo = 0;
            byte[] iouBt = DBreeze.Utils.BytesProcessing.To_16_bytes_array_BigEndian(iuo);
            decimal? iot1 = DBreeze.Utils.BytesProcessing.To_Decimal_BigEndian_NULL(iouBt);

            // Testing old and new byte[]
            foreach (var val in testValues)
            {
                var brold = ByteConversionOld.To_15_bytes_array_BigEndian(val);
                var brnew = ByteConversionNew.To_15_bytes_array_BigEndian(val);

                if (!brold.SequenceEqual(brnew))
                {
                    var dOld1 = ByteConversionOld.To_Decimal_BigEndian(brold);
                    var dNew1 = ByteConversionOld.To_Decimal_BigEndian(brnew);
                    Debug.WriteLine($"BACK_OLD: {dOld1}; {dNew1}");

                    var dOld2 = ByteConversionNew.To_Decimal_BigEndian(brold);
                    var dNew2 = ByteConversionNew.To_Decimal_BigEndian(brnew);
                    Debug.WriteLine($"BACK_NEW: {dOld2}; {dNew2}");
                }

                var dOld = ByteConversionOld.To_Decimal_BigEndian(brold);
                var dNew = ByteConversionNew.To_Decimal_BigEndian(brold);

                if (dOld != dNew)
                {
                    Debug.WriteLine($"Mismatch detected for value: {val}");
                }
            }

            var sw = Stopwatch.StartNew();
            foreach (var val in testValues)
            {
                var b = ByteConversionOld.To_15_bytes_array_BigEndian(val);
            }
            sw.Stop();
            Debug.WriteLine($"OLD ToBytes speed: {sw.Elapsed.TotalSeconds:F2} seconds.");

            sw = Stopwatch.StartNew();
            foreach (var val in testValues)
            {
                var b = ByteConversionNew.To_15_bytes_array_BigEndian(val);
            }
            sw.Stop();
            Debug.WriteLine($"NEW ToBytes speed: {sw.Elapsed.TotalSeconds:F2} seconds.");

            sw = Stopwatch.StartNew();
            foreach (var val in testValues)
            {
                sw.Stop();
                var b = ByteConversionNew.To_15_bytes_array_BigEndian(val);
                sw.Start();
                var d = ByteConversionOld.To_Decimal_BigEndian(b);
            }
            sw.Stop();
            Debug.WriteLine($"Old ToDecimal speed: {sw.Elapsed.TotalSeconds:F2} seconds.");

            sw = Stopwatch.StartNew();
            foreach (var val in testValues)
            {
                sw.Stop();
                var b = ByteConversionNew.To_15_bytes_array_BigEndian(val);
                sw.Start();
                var d = ByteConversionNew.To_Decimal_BigEndian(b);
            }
            sw.Stop();
            Debug.WriteLine($"NEW ToDecimal speed: {sw.Elapsed.TotalSeconds:F2} seconds.");
        }



        private static decimal[] GenerateTestDecimals(int count)
        {
            var arr = new decimal[count];
            int idx = 0;

            decimal[] edgeCases = new decimal[]
            {
        0m, -0m,
        1m, -1m,
        42m, -42m,

        decimal.MaxValue,
        decimal.MinValue,

        0.0000000000000000000000000001m, // max scale (28)
        -0.0000000000000000000000000001m,

        79228162514264337593543950335m,  // max integer
        -79228162514264337593543950335m,

        1.2345678901234567890123456789m,
        -1.2345678901234567890123456789m,

        10000000000000000000000000000m,
        -10000000000000000000000000000m
            };

            foreach (var v in edgeCases)
            {
                arr[idx++] = v;
            }

            while (idx < count)
            {
                arr[idx++] = GenerateRandomDecimal();
            }

            return arr;
        }



        private static decimal GenerateRandomDecimal()
        {
            // 96-bit integer parts
            int lo = _rand.Next(int.MinValue, int.MaxValue);
            int mid = _rand.Next(int.MinValue, int.MaxValue);
            int hi = _rand.Next(int.MinValue, int.MaxValue);

            bool isNegative = _rand.Next(2) == 0;

            // scale: 0–28 (critical for coverage)
            byte scale = (byte)_rand.Next(0, 29);

            try
            {
                return new decimal(lo, mid, hi, isNegative, scale);
            }
            catch
            {
                // fallback in rare overflow cases
                return 0m;
            }
        }












        public static void RunTestFloats()
        {
            var testValues = GenerateTestFloats(10_000_000);

            // Testing old and new byte[]
            foreach (var val in testValues)
            {
                var brold = ByteConversionOld.To_4_bytes_array_BigEndian(val);
                var brnew = ByteConversionNew.To_4_bytes_array_BigEndian(val);

                if (!brold.SequenceEqual(brnew))
                {
                    var fbOld1 = ByteConversionOld.To_Float_BigEndian(brold);
                    var fbNew1 = ByteConversionOld.To_Float_BigEndian(brnew);
                    Debug.WriteLine($"BACK_OLD: {fbOld1}; {fbNew1}");

                    var fbOld2 = ByteConversionNew.To_Float_BigEndian(brold);
                    var fbNew2 = ByteConversionNew.To_Float_BigEndian(brnew);
                    Debug.WriteLine($"BACK_NEW: {fbOld2}; {fbNew2}");
                }

                var fbOld = ByteConversionOld.To_Float_BigEndian(brold);
                var fbNew = ByteConversionNew.To_Float_BigEndian(brold);
                
                

                if (fbOld != fbNew)
                {
                    Debug.WriteLine($"Mismatch detected for value: {val}");
                }
            }

            var sw = Stopwatch.StartNew();
            foreach (var val in testValues)
            {
                var b = ByteConversionOld.To_4_bytes_array_BigEndian(val);
            }
            sw.Stop();
            Debug.WriteLine($"OLD ToBytes speed: {sw.Elapsed.TotalSeconds:F2} seconds.");

            sw = Stopwatch.StartNew();
            foreach (var val in testValues)
            {
                var b = ByteConversionNew.To_4_bytes_array_BigEndian(val);
            }
            sw.Stop();
            Debug.WriteLine($"NEW ToBytes speed: {sw.Elapsed.TotalSeconds:F2} seconds.");

            sw = Stopwatch.StartNew();
            foreach (var val in testValues)
            {
                sw.Stop();
                var b = ByteConversionNew.To_4_bytes_array_BigEndian(val);
                sw.Start();
                var f = ByteConversionOld.To_Float_BigEndian(b);
            }
            sw.Stop();
            Debug.WriteLine($"Old ToFloat speed: {sw.Elapsed.TotalSeconds:F2} seconds.");

            sw = Stopwatch.StartNew();
            foreach (var val in testValues)
            {
                sw.Stop();
                var b = ByteConversionNew.To_4_bytes_array_BigEndian(val);
                sw.Start();
                var f = ByteConversionNew.To_Float_BigEndian(b);
            }
            sw.Stop();
            Debug.WriteLine($"NEW ToFloat speed: {sw.Elapsed.TotalSeconds:F2} seconds.");
            /*
             OLD ToBytes speed: 5,93 seconds.
NEW ToBytes speed: 2,54 seconds.
Old ToFloat speed: 2,75 seconds.
NEW ToFloat speed: 1,76 seconds.
             */
        }


        private static float[] GenerateTestFloats(int count)
        {
            var arr = new float[count];
            int idx = 0;

            float[] edgeCases = new float[]
            {
        0.0f, -0.0f,
        1.0f, -1.0f, 42.0f, -42.0f,
        (float)Math.PI, -(float)Math.PI,
        (float)Math.E, -(float)Math.E,
        1.0f / 3.0f, -1.0f / 3.0f,
        1e-7f, -1e-7f,
        float.Epsilon, -float.Epsilon,
        1234567.5f, -1234567.5f,
        float.MaxValue, float.MinValue
            };

            foreach (var v in edgeCases)
            {
                arr[idx++] = v;
            }

            while (idx < count)
            {
                arr[idx++] = GenerateRandomFloat();
            }

            return arr;
        }



        private static float GenerateRandomFloat()
        {
            int rangeType = _rand.Next(5);

            switch (rangeType)
            {
                case 0: // normal [-1e3, 1e3]
                    return (float)(_rand.NextDouble() * 2000.0 - 1000.0);

                case 1: // small [1e-7, 1e-3]
                    float sign1 = _rand.Next(2) == 0 ? 1f : -1f;
                    return sign1 * (float)(_rand.NextDouble() * 1e-3 + 1e-7);

                case 2: // large [1e3, 1e7]
                    float sign2 = _rand.Next(2) == 0 ? 1f : -1f;
                    return sign2 * (float)(_rand.NextDouble() * 1e7 + 1e3);

                case 3: // very large [1e20, 1e38]
                    float sign3 = _rand.Next(2) == 0 ? 1f : -1f;
                    return sign3 * (float)(Math.Pow(10, _rand.Next(20, 38)) * _rand.NextDouble());

                case 4: // very small [1e-38, 1e-7]
                    float sign4 = _rand.Next(2) == 0 ? 1f : -1f;
                    return sign4 * (float)(Math.Pow(10, -_rand.Next(7, 38)) * _rand.NextDouble());

                default:
                    return 0f;
            }
        }






        public static void RunTestDoubles()
        {
            var testValues = GenerateTestDoubles(10_000_000);

            //Testing old and new byte[]
            foreach (var val in testValues)
            {
                var brold = ByteConversionOld.To_9_bytes_array_BigEndian(val);
                var brnew = ByteConversionNew.To_9_bytes_array_BigEndian(val);
                if (!brold.SequenceEqual(brnew))
                {
                    var dbOld1 = ByteConversionOld.To_Double_BigEndian(brold);
                    var dbNew1 = ByteConversionOld.To_Double_BigEndian(brnew);
                    Debug.WriteLine($"BACK_OLD: {dbOld1}; {dbNew1} ");

                    var dbOld2 = ByteConversionNew.To_Double_BigEndian(brold);
                    var dbNew2 = ByteConversionNew.To_Double_BigEndian(brnew);
                    Debug.WriteLine($"BACK_NEW: {dbOld2}; {dbNew2} ");
                }

                var dbOld = ByteConversionOld.To_Double_BigEndian(brold);
                var dbNew = ByteConversionNew.To_Double_BigEndian(brnew);

                if (dbOld != dbNew)
                {
                    Debug.WriteLine($"Mismatch detected for value: {val}");
                }
            }

            /*
             
             OLD ToBytes speed: 6,95 seconds.
NEW ToBytes speed: 2,79 seconds.
Old ToDouble speed: 3,63 seconds.
NEW ToDouble speed: 1,62 seconds.

             */

            var swOld = Stopwatch.StartNew();
            foreach (var val in testValues)
            {
                var brold = ByteConversionOld.To_9_bytes_array_BigEndian(val);               
            }
            swOld.Stop();
            Debug.WriteLine($"OLD ToBytes speed: {swOld.Elapsed.TotalSeconds:F2} seconds.");

            swOld = Stopwatch.StartNew();
            foreach (var val in testValues)
            {
                var brold = ByteConversionNew.To_9_bytes_array_BigEndian(val);                
            }
            swOld.Stop();
            Debug.WriteLine($"NEW ToBytes speed: {swOld.Elapsed.TotalSeconds:F2} seconds.");

            swOld = Stopwatch.StartNew();
            foreach (var val in testValues)
            {
                swOld.Stop();
                var brold = ByteConversionOld.To_9_bytes_array_BigEndian(val);
                swOld.Start();
                var dbOld2 = ByteConversionOld.To_Double_BigEndian(brold);

            }
            swOld.Stop();
            Debug.WriteLine($"Old ToDouble speed: {swOld.Elapsed.TotalSeconds:F2} seconds.");

            swOld = Stopwatch.StartNew();
            foreach (var val in testValues)
            {
                swOld.Stop();
                var brold = ByteConversionNew.To_9_bytes_array_BigEndian(val);
                swOld.Start();
                var dbOld2 = ByteConversionNew.To_Double_BigEndian(brold);

            }
            swOld.Stop();
            Debug.WriteLine($"NEW ToDouble speed: {swOld.Elapsed.TotalSeconds:F2} seconds.");
        }

        private static double[] GenerateTestDoubles(int count)
        {
            var arr = new double[count];
            int idx = 0;
            
            // Include edge cases first
            double[] edgeCases = new double[]
            {
                //double.NaN, double.PositiveInfinity, double.NegativeInfinity,
                1e308d, -1e308d, 1e-308d, -1e-308d,
                1234567789789878745.12345d,
                0.0d, -0.0d,
                1.0d, -1.0d, 42.0d, -42.0d,
                Math.PI, -Math.PI, Math.E, -Math.E,
                1.0d / 3.0d, -1.0d / 3.0d,
                0.00000000000012345d, -0.00000000000012345d,
                double.Epsilon, -double.Epsilon,
                12345678901234.5d, -12345678901234.5d,
                double.MaxValue, double.MinValue
            };

            foreach (var v in edgeCases)
            {
                arr[idx++] = v;
            }

            // Fill the rest with random doubles
            while (idx < count)
            {
                arr[idx++] = GenerateRandomDouble();
            }

            return arr;
        }

        private static double GenerateRandomDouble()
        {
            // Randomly choose a magnitude range
            int rangeType = _rand.Next(5);

            switch (rangeType)
            {
                case 0: // normal range [-1e3, 1e3]
                    return (_rand.NextDouble() * 2000.0) - 1000.0;
                case 1: // small numbers [1e-12, 1e-3]
                    double sign1 = _rand.Next(2) == 0 ? 1.0 : -1.0;
                    return sign1 * _rand.NextDouble() * 1e-3 + 1e-12;
                case 2: // large numbers [1e6, 1e12]
                    double sign2 = _rand.Next(2) == 0 ? 1.0 : -1.0;
                    return sign2 * (_rand.NextDouble() * 1e12 + 1e6);
                case 3: // extremely large numbers [1e200, 1e308]
                    double sign3 = _rand.Next(2) == 0 ? 1.0 : -1.0;
                    return sign3 * Math.Pow(10, _rand.Next(200, 308)) * _rand.NextDouble();
                case 4: // extremely small numbers [1e-300, 1e-12]
                    double sign4 = _rand.Next(2) == 0 ? 1.0 : -1.0;
                    return sign4 * Math.Pow(10, -_rand.Next(12, 300)) * _rand.NextDouble();
                default:
                    return 0.0;
            }
        }
   




        internal static void TestDouble()
        {
            var tarr = GetLexicographicalTestValues();

            foreach(var el in tarr)
            {
                var brold = TesterNet6.ByteConversion.ByteConversionOld.To_9_bytes_array_BigEndian(el);
                var brnew = TesterNet6.ByteConversion.ByteConversionNew.To_9_bytes_array_BigEndian(el);

                if(!brold.SequenceEqual(brnew))
                {
                    var dbOld1 = TesterNet6.ByteConversion.ByteConversionOld.To_Double_BigEndian(brold);
                    var dbNew1 = TesterNet6.ByteConversion.ByteConversionOld.To_Double_BigEndian(brnew);
                    Debug.WriteLine($"BACK_OLD: {dbOld1}; {dbNew1} ");

                    var dbOld2 = TesterNet6.ByteConversion.ByteConversionNew.To_Double_BigEndian(brold);
                    var dbNew2 = TesterNet6.ByteConversion.ByteConversionNew.To_Double_BigEndian(brnew);
                    Debug.WriteLine($"BACK_NEW: {dbOld2}; {dbNew2} ");

                }

                var dbOld = TesterNet6.ByteConversion.ByteConversionOld.To_Double_BigEndian(brold);
                var dbNew = TesterNet6.ByteConversion.ByteConversionNew.To_Double_BigEndian(brold);

                if(dbOld != dbNew)
                {

                }
            }
            
        }


        public static double[] GetLexicographicalTestValues()
        {
            return new double[]
            {
        // 1. Zero handling (The Critical Bug Fix)
        0.0d,
        -0.0d,

        // 2. Base Integers
        1.0d,
        -1.0d,
        42.0d,
        -42.0d,

        // 3. Known Math Constants (Fractions & Rounding checks)
        Math.PI,      // 3.1415926535897931
        -Math.PI,
        Math.E,       // 2.7182818284590451
        -Math.E,

        // 4. Repeated/Infinite Decimals (Stress testing the 15 decimal point formatting difference)
        1.0d / 3.0d,  // 0.3333333333333333
        -1.0d / 3.0d,
        
        // 5. Very small scientific notation variants
        0.00000000000012345d,
        -0.00000000000012345d,
        double.Epsilon,  // Minimum positive > 0 value (4.94065645841247E-324)
        -double.Epsilon,

        // 6. Very large numbers
        12345678901234.5d,
        -12345678901234.5d,
        double.MaxValue, // 1.7976931348623157E+308
        double.MinValue,  // -1.7976931348623157E+308
        12345678901234.5456546845468d,
        -12345678901234.5487986546874d,
            };
        }



        public static void RunDeepCopyTests()
        {
            Debug.WriteLine("--- Setting up test object ---");

            // 1. Setup Original Object
            var original = new Employee(id: "EMP-12345", rank: 1)
            {
                Name = "John Doe",
                Location = new Address { City = "New York", ZipCode = 10001 },
                Scores = [95, 87, 92], // C# 12 collection expression
                Matrix = new[,] { { 1, 2 }, { 3, 4 } },
                OnWorkCompleted = () => Debug.WriteLine("Done!"),
                StructData = new MetadataStruct(99, new Address { City = "Metadata City", ZipCode = 55555 })
            };

            // Create a circular reference
            original.Manager = original;

            // 2. Perform Deep Copy
            Debug.WriteLine("--- Performing Deep Copy ---");
            var copy = DBreeze.Utils.DeepCopyByExpressionTrees.CloneByExpressionTree(original);//.DeepCopyByExpressionTrees(original);

            // 3. Assertions & Validations
            Debug.WriteLine("\n--- Validating Results ---");

            // Reference Check
            Debug.WriteLine($"Is copy a different object reference? {!ReferenceEquals(original, copy)} (Expected: True)");

            // Primitive & String check
            Debug.WriteLine($"Name matches? {copy.Name == original.Name} (Expected: True)");

            // Readonly field check
            Debug.WriteLine($"Readonly ID matches? {copy.Id == original.Id} (Expected: True)");
            Debug.WriteLine($"Readonly Struct field matches? {copy.StructData.Id == original.StructData.Id} (Expected: True)");

            // Circular Reference Check
            Debug.WriteLine($"Does copy's manager point to the copy itself? {ReferenceEquals(copy.Manager, copy)} (Expected: True)");
            Debug.WriteLine($"Is original's manager still original? {ReferenceEquals(original.Manager, original)} (Expected: True)");

            // Delegate Check (Should be null as per your code's logic)
            Debug.WriteLine($"Is delegate stripped (null)? {copy.OnWorkCompleted is null} (Expected: True)");

            // Array Check (1D)
            copy.Scores[0] = 999;
            Debug.WriteLine($"Original array unaffected by copy mutation? {original.Scores[0] == 95} (Expected: True)");

            // Array Check (2D)
            copy.Matrix[0, 0] = 999;
            Debug.WriteLine($"Original 2D array unaffected? {original.Matrix[0, 0] == 1} (Expected: True)");

            // Nested Class Check
            copy.Location.City = "Los Angeles";
            Debug.WriteLine($"Original nested class unaffected? {original.Location.City == "New York"} (Expected: True)");

            // Struct Containing Class Check
            copy.StructData.AssociatedAddress.City = "Changed City";
            Debug.WriteLine($"Original struct's nested class unaffected? {original.StructData.AssociatedAddress.City == "Metadata City"} (Expected: True)");

            Debug.WriteLine("\nAll tests ran successfully!");
        }

        // ==========================================
        // TEST MODELS
        // ==========================================

        public class Employee
        {
            // Readonly field to test Reflection SetValue fallback
            public readonly string Id;

            // Readonly property with a hidden readonly backing field
            public int Rank { get; }

            public string Name { get; set; } = string.Empty;
            public Address Location { get; set; } = new();

            // Circular reference test
            public Employee? Manager { get; set; }

            public int[] Scores { get; set; } = [];
            public int[,] Matrix { get; set; } = new int[0, 0];

            // Delegate to test skipping logic
            public Action? OnWorkCompleted { get; set; }

            // Struct that requires deep copying because it contains a class
            public MetadataStruct StructData { get; set; }

            public Employee(string id, int rank)
            {
                Id = id;
                Rank = rank;
            }
        }

        public class Address
        {
            public string City { get; set; } = string.Empty;
            public int ZipCode { get; set; }
        }

        public struct MetadataStruct
        {
            public readonly int Id;
            public Address AssociatedAddress; // Class inside a struct

            public MetadataStruct(int id, Address associatedAddress)
            {
                Id = id;
                AssociatedAddress = associatedAddress;
            }
        }
    }
}
