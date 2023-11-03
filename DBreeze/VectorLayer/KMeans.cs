/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Oleksiy Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
*/
#if NET6FUNC || NET472
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using DBreeze.Utils;

namespace DBreeze.VectorLayer
{
    internal class Clustering
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="k"></param>
        /// <returns></returns>
        public static Dictionary<int, (double[], HashSet<int>)> KMeansCluster(List<double[]> data, int k)
        {
            int dataLength = data.Count;
            //var distanceFunc = VectorMath.Distance_SIMDForUnits;

            if (dataLength == 0 || k <= 0)
                return new Dictionary<int, (double[], HashSet<int>)>();

            List<double[]> centroids = InitializeCentroidsKMeansPlusPlus(data, k, VectorMath.Distance_SIMDForUnits);
            int maxIterations = 100;

            Dictionary<int, (double[], HashSet<int>)> clusters = new Dictionary<int, (double[], HashSet<int>)>(k);
            for (int i = 0; i < k; i++)
                clusters[i] = (centroids[i], new HashSet<int>());

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                // Assign each data point to the nearest centroid
                foreach (var cluster in clusters)
                    cluster.Value.Item2.Clear();

                for (int i = 0; i < dataLength; i++)
                {
                    double minDistance = double.MaxValue;
                    int clusterIndex = 0;

                    for (int j = 0; j < k; j++)
                    {
                        double distance = VectorMath.Distance_SIMDForUnits(data[i], clusters[j].Item1);
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            clusterIndex = j;
                        }
                    }

                    clusters[clusterIndex].Item2.Add(i);
                }

                // Update centroids based on the mean of the assigned data points
                bool centroidsChanged = false;

                foreach (var cluster in clusters)
                {
                    double[] newCentroid = new double[data[0].Length];

                    foreach (var dataIndex in cluster.Value.Item2)
                    {
                        for (int j = 0; j < data[dataIndex].Length; j++)
                            newCentroid[j] += data[dataIndex][j];
                    }

                    for (int j = 0; j < newCentroid.Length; j++)
                    {
                        if (cluster.Value.Item2.Count > 0)
                            newCentroid[j] /= cluster.Value.Item2.Count;
                    }

                    if (!ArraysEqual(cluster.Value.Item1, newCentroid))
                    {
                        centroidsChanged = true;
                        clusters[cluster.Key] = (newCentroid, cluster.Value.Item2);
                    }
                }

                if (!centroidsChanged)
                    break;
            }

            return clusters;
        }

        private static List<double[]> InitializeCentroidsKMeansPlusPlus(List<double[]> data, int k, Func<double[], double[], double> distanceFunc)
        {
            List<double[]> centroids = new List<double[]>();
            Random random = new Random();

            // Choose the first centroid randomly
            centroids.Add(data[random.Next(data.Count)]);

            while (centroids.Count < k)
            {
                double[] distances = new double[data.Count];
                double totalDistance = 0.0;

                for (int i = 0; i < data.Count; i++)
                {
                    double minDistance = double.MaxValue;
                    for (int j = 0; j < centroids.Count; j++)
                    {
                        double distance = distanceFunc(data[i], centroids[j]);
                        minDistance = Math.Min(minDistance, distance);
                    }
                    distances[i] = minDistance * minDistance; // Squaring for probabilities
                    totalDistance += distances[i];
                }

                double randValue = random.NextDouble() * totalDistance;
                double cumulativeProbability = 0.0;
                for (int i = 0; i < data.Count; i++)
                {
                    cumulativeProbability += distances[i];
                    if (cumulativeProbability >= randValue)
                    {
                        centroids.Add(data[i]);
                        break;
                    }
                }
            }

            return centroids;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ArraysEqual(double[] arr1, double[] arr2)
        {
            if (arr1.Length != arr2.Length)
                return false;

            for (int i = 0; i < arr1.Length; i++)
            {
                if (arr1[i] != arr2[i])
                    return false;
            }

            return true;
        }



        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="data"></param>
        ///// <param name="k"></param>
        ///// <param name="distanceFunc"></param>
        ///// <param name="initialCentroids"></param>
        ///// <returns></returns>
        //public static Dictionary<int, (double[], HashSet<int>)> KMeansClusterOld(List<double[]> data, int k, List<int> initialCentroids = null)
        //{
        //    //double[][] data, 
        //    int maxIterations = 100;

        //    int dataLength = data.Count;
        //    if (dataLength == 0)
        //        return new Dictionary<int, (double[], HashSet<int>)>();

        //    if ((initialCentroids?.Count ?? 0) == 0 && k < 1)
        //        return new Dictionary<int, (double[], HashSet<int>)>();

        //    if ((initialCentroids?.Count ?? 0) > 0)
        //        k = initialCentroids.Count;

        //    if (dataLength < k)
        //        k = dataLength;

        //    int[] clusterAssignments = new int[dataLength];
        //    double[][] centroids = new double[k][];

        //    if ((initialCentroids?.Count ?? 0) > 0)
        //    {
        //        for (int i = 0; i < initialCentroids.Count; i++)
        //            centroids[i] = data[initialCentroids[i]];
        //    }
        //    else
        //    {
        //        FastRandom rnd=new FastRandom();
        //        //Random rnd=new Random();
        //        //ThreadSafeFastRandom.Next(dataLength);

        //        for (int i = 0; i < k; i++)
        //        {
        //            //centroids[i] = data[ThreadSafeFastRandom.Next(dataLength)]; // Initialize centroids randomly
        //            centroids[i] = data[rnd.Next(dataLength)]; // Initialize centroids randomly
        //        }
        //    }


        //    for (int iteration = 0; iteration < maxIterations; iteration++)
        //    {
        //        // Assign each data point to the nearest centroid
        //        for (int i = 0; i < dataLength; i++)
        //        {
        //            double minDistance = double.MaxValue;

        //            int cluster = 0;
        //            for (int j = 0; j < k; j++)
        //            {                      
        //                double distance = Math.Abs(VectorMath.Distance_SIMDForUnits(data[i], centroids[j]));
        //                if (distance < minDistance)
        //                {
        //                    minDistance = distance;
        //                    cluster = j;
        //                }
        //            }
        //            clusterAssignments[i] = cluster;
        //        }

        //        // Update centroids based on the mean of the assigned data points
        //        double[][] newCentroids = new double[k][];
        //        int[] clusterCounts = new int[k];
        //        for (int i = 0; i < k; i++)
        //            newCentroids[i] = new double[data[0].Length];

        //        for (int i = 0; i < dataLength; i++)
        //        {
        //            int cluster = clusterAssignments[i];
        //            clusterCounts[cluster]++;
        //            for (int j = 0; j < data[i].Length; j++)
        //                newCentroids[cluster][j] += data[i][j];
        //        }

        //        for (int i = 0; i < k; i++)
        //        {
        //            if (clusterCounts[i] > 0)
        //            {
        //                for (int j = 0; j < newCentroids[i].Length; j++)
        //                    newCentroids[i][j] /= clusterCounts[i];
        //            }
        //        }

        //        // Check if centroids have converged
        //        bool centroidsChanged = false;
        //        for (int i = 0; i < k; i++)
        //        {
        //            if (!centroids[i].SequenceEqual(newCentroids[i]))
        //            {
        //                centroidsChanged = true;
        //                break;
        //            }
        //        }

        //        if (!centroidsChanged)
        //            break;

        //        centroids = newCentroids;
        //    }


        //    //Key Cluster (equal to K, value items internal IDs)
        //    Dictionary<int, (double[], HashSet<int>)> d = new Dictionary<int, (double[], HashSet<int>)>(k);
        //    for (int j = 0; j < k; j++)
        //        d[j] = (centroids[j], new HashSet<int>());

        //    int v = 0;
        //    foreach (var el in clusterAssignments)
        //    {
        //        d[el].Item2.Add(v);
        //        v++;
        //    }

        //    return d;
        //}//eof-----


        /// <summary>
        /// 
        /// </summary>
        /// <param name="initialCentroids"></param>
        /// <param name="dataToCheck"></param>
        /// <returns></returns>
        public static Dictionary<int, List<int>> KMeansCluster(List<double[]> centroids, List<double[]> data)
        {
            if ((centroids?.Count ?? 0) == 0 || (data?.Count ?? 0) == 0)
                return new Dictionary<int, List<int>>();

            int dataLength = data.Count;            
            int k = centroids.Count;

            //List<double[]> centroids = InitializeCentroidsKMeansPlusPlus(data, k, VectorMath.Distance_SIMDForUnits);            
            int maxIterations = 100;

            Dictionary<int, (double[], List<int>)> clusters = new Dictionary<int, (double[], List<int>)>(k);
            for (int i = 0; i < k; i++)
                clusters[i] = (centroids[i], new List<int>());

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                // Assign each data point to the nearest centroid
                foreach (var cluster in clusters)
                    cluster.Value.Item2.Clear();

                for (int i = 0; i < dataLength; i++)
                {
                    double minDistance = double.MaxValue;
                    int clusterIndex = 0;

                    for (int j = 0; j < k; j++)
                    {
                        double distance = VectorMath.Distance_SIMDForUnits(data[i], clusters[j].Item1);
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            clusterIndex = j;
                        }
                    }

                    clusters[clusterIndex].Item2.Add(i);
                }

                // Update centroids based on the mean of the assigned data points
                bool centroidsChanged = false;

                foreach (var cluster in clusters)
                {
                    double[] newCentroid = new double[data[0].Length];

                    foreach (var dataIndex in cluster.Value.Item2)
                    {
                        for (int j = 0; j < data[dataIndex].Length; j++)
                            newCentroid[j] += data[dataIndex][j];
                    }

                    for (int j = 0; j < newCentroid.Length; j++)
                    {
                        if (cluster.Value.Item2.Count > 0)
                            newCentroid[j] /= cluster.Value.Item2.Count;
                    }

                    if (!ArraysEqual(cluster.Value.Item1, newCentroid))
                    {
                        centroidsChanged = true;
                        clusters[cluster.Key] = (newCentroid, cluster.Value.Item2);
                    }
                }

                if (!centroidsChanged)
                    break;
            }

            return clusters.ToDictionary(a=>a.Key,v=>v.Value.Item2);
        }

        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="initialCentroids"></param>
        ///// <param name="dataToCheck"></param>
        ///// <param name="distanceFunc"></param>
        ///// <returns></returns>
        //public static Dictionary<int, List<int>> KMeansClusterOld(List<double[]> initialCentroids, List<double[]> dataToCheck)
        //{
        //    //double[][] data, 
        //    int maxIterations = 100;

        //    int k = 0;

        //    if ((initialCentroids?.Count ?? 0) == 0 || (dataToCheck?.Count ?? 0) == 0)
        //        return new Dictionary<int, List<int>>();

        //    int dataLength = dataToCheck.Count;
        //    k = initialCentroids.Count;


        //    int[] clusterAssignments = new int[dataLength];
        //    double[][] centroids = new double[k][];

        //    if ((initialCentroids?.Count ?? 0) > 0)
        //    {
        //        for (int i = 0; i < initialCentroids.Count; i++)
        //            centroids[i] = initialCentroids[i];
        //    }
        //    //else
        //    //{
        //    //    //Random rnd=new Random();
        //    //    ThreadSafeFastRandom.Next(dataLength);

        //    //    for (int i = 0; i < k; i++)
        //    //    {
        //    //        centroids[i] = data[ThreadSafeFastRandom.Next(dataLength)]; // Initialize centroids randomly
        //    //        //centroids[i] = data[rnd.Next(dataLength)]; // Initialize centroids randomly
        //    //    }
        //    //}


        //    //for (int iteration = 0; iteration < maxIterations; iteration++)
        //    for (int iteration = 0; iteration < maxIterations; iteration++)
        //    {
        //        // Assign each data point to the nearest centroid
        //        for (int i = 0; i < dataLength; i++)
        //        {
        //            double minDistance = double.MaxValue;

        //            int cluster = 0;
        //            for (int j = 0; j < k; j++)
        //            {                       
        //                //double distance = Math.Abs(VectorMath.Distance_SIMDForUnits(dataToCheck[i], centroids[j]));
        //                double distance = VectorMath.Distance_SIMDForUnits(dataToCheck[i], centroids[j]);
        //                if (distance < minDistance)
        //                {
        //                    minDistance = distance;
        //                    cluster = j;
        //                }
        //            }
        //            clusterAssignments[i] = cluster;
        //        }

        //        // Update centroids based on the mean of the assigned data points
        //        double[][] newCentroids = new double[k][];
        //        int[] clusterCounts = new int[k];
        //        for (int i = 0; i < k; i++)
        //            newCentroids[i] = new double[dataToCheck[0].Length];

        //        for (int i = 0; i < dataLength; i++)
        //        {
        //            int cluster = clusterAssignments[i];
        //            clusterCounts[cluster]++;
        //            for (int j = 0; j < dataToCheck[i].Length; j++)
        //                newCentroids[cluster][j] += dataToCheck[i][j];
        //        }

        //        for (int i = 0; i < k; i++)
        //        {
        //            if (clusterCounts[i] > 0)
        //            {
        //                for (int j = 0; j < newCentroids[i].Length; j++)
        //                    newCentroids[i][j] /= clusterCounts[i];
        //            }
        //        }

        //        // Check if centroids have converged
        //        bool centroidsChanged = false;
        //        for (int i = 0; i < k; i++)
        //        {
        //            if (!centroids[i].SequenceEqual(newCentroids[i]))
        //            {
        //                centroidsChanged = true;
        //                break;
        //            }
        //        }

        //        if (!centroidsChanged)
        //            break;

        //        centroids = newCentroids;
        //    }


        //    //Key Cluster (equal to K, value items internal IDs)
        //    Dictionary<int, List<int>> d = new Dictionary<int, List<int>>(k);
        //    for (int j = 0; j < k; j++)
        //        d[j] = new List<int>();

        //    int v = 0;
        //    foreach (var el in clusterAssignments)
        //    {
        //        d[el].Add(v);
        //        v++;
        //    }

        //    return d;

        //}//eof-----


    }//eoc
}//eon
#endif