using DBreeze.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TesterNet6.ByteConversion
{
    internal static class TestMultiKeyDictionary
    {

        public static void RunTestMultiKeyConcurrent()
        {
            // 1. Instantiation (using a ValueTuple for the multi-key)
            // Ensure you wrap it in a 'using' statement because it implements IDisposable (uses ReaderWriterLockSlim)
            using var inventory = new MultiKeyConcurrentSortedDictionary<(string, int, int), string>();

            Debug.WriteLine("--- 1. ADDING ELEMENTS ---");
            // Using the .Add() method
            inventory.Add(("Electronics", 1, 1), "Smartphone");
            inventory.Add(("Electronics", 1, 2), "Laptop");
            inventory.Add(("Electronics", 2, 1), "Headphones");

            // Using the indexer []
            inventory[("Groceries", 5, 1)] = "Apples";
            inventory[("Groceries", 5, 2)] = "Bananas";
            inventory[("Groceries", 6, 1)] = "Milk";

            Debug.WriteLine($"Total items: {inventory.Count}"); // Expected: 6


            Debug.WriteLine("\n--- 2. GETTING ELEMENTS ---");
            // Exact match Get
            string? phone = inventory.Get(("Electronics", 1, 1));
            Debug.WriteLine($"Found at (Electronics, 1, 1): {phone}");

            // Using TryGetValue safely
            if (inventory.TryGetValue(("Groceries", 5, 2), out string? fruit))
            {
                Debug.WriteLine($"Successfully retrieved: {fruit}");
            }

            // Checking if a key exists
            bool hasMilk = inventory.Contains(("Groceries", 6, 1));
            Debug.WriteLine($"Has Milk? {hasMilk}");


            Debug.WriteLine("\n--- 3. PARTIAL KEY SEARCH (GetByKeyStart) ---");
            // Search by 1 key (All Electronics)
            Debug.WriteLine("All Electronics:");
            var allElectronics = inventory.GetByKeyStart("Electronics");
            foreach (var item in allElectronics)
            {
                Debug.WriteLine($"  - {item.Item1}: {item.Item2}");
            }

            // Search by 2 keys (Groceries in Aisle 5 only)
            Debug.WriteLine("\nGroceries in Aisle 5:");
            var groceriesAisle5 = inventory.GetByKeyStart("Groceries", 5);
            foreach (var item in groceriesAisle5)
            {
                Debug.WriteLine($"  - Shelf {item.Item1.Item3}: {item.Item2}");
            }


            Debug.WriteLine("\n--- 4. CONCURRENT OPERATIONS (Thread-Safety Demo) ---");
            // Let's add 1,000 items concurrently on background threads
            Debug.WriteLine("Adding 1000 items concurrently...");
            Parallel.For(0, 1000, i =>
            {
                inventory.Add(("Hardware", 10, i), $"Tool #{i}");

                // Simultaneously reading shouldn't crash!
                _ = inventory.Count;
            });
            Debug.WriteLine($"Total items after concurrent add: {inventory.Count}"); // Expected: 1006


            Debug.WriteLine("\n--- 5. REMOVING ELEMENTS ---");
            // Remove a specific exact item
            inventory.Remove(("Electronics", 1, 1));
            Debug.WriteLine($"Count after exact remove: {inventory.Count}"); // Expected: 1005

            // Remove by PARTIAL key (e.g., Remove ALL Groceries in Aisle 5)
            inventory.Remove("Groceries", 5);
            Debug.WriteLine($"Count after partial remove (removed 2 items): {inventory.Count}"); // Expected: 1003


            Debug.WriteLine("\n--- 6. ITERATING EVERYTHING (GetAll) ---");
            // Notice: This is safe to do while other threads might be writing, 
            // because it returns a materialized snapshot List.
            var snapshot = inventory.GetAll();
            Debug.WriteLine($"Snapshot captured {snapshot.Count} items safely.");


            Debug.WriteLine("\n--- 7. CLEARING ---");
            inventory.Clear();
            Debug.WriteLine($"Count after Clear: {inventory.Count}"); // Expected: 0
        }
    }
}
