using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <param name="distanceFunc"></param>
        /// <param name="initialCentroids"></param>
        /// <returns></returns>
        public static Dictionary<int, (double[], HashSet<int>)> KMeansCluster(List<double[]> data, int k, List<int> initialCentroids = null)
        {
            //double[][] data, 
            int maxIterations = 100;

            int dataLength = data.Count;
            if (dataLength == 0)
                return new Dictionary<int, (double[], HashSet<int>)>();

            if ((initialCentroids?.Count ?? 0) == 0 && k < 1)
                return new Dictionary<int, (double[], HashSet<int>)>();

            if ((initialCentroids?.Count ?? 0) > 0)
                k = initialCentroids.Count;

            if (dataLength < k)
                k = dataLength;

            int[] clusterAssignments = new int[dataLength];
            double[][] centroids = new double[k][];

            if ((initialCentroids?.Count ?? 0) > 0)
            {
                for (int i = 0; i < initialCentroids.Count; i++)
                    centroids[i] = data[initialCentroids[i]];
            }
            else
            {
                FastRandom rnd=new FastRandom();
                //Random rnd=new Random();
                //ThreadSafeFastRandom.Next(dataLength);

                for (int i = 0; i < k; i++)
                {
                    //centroids[i] = data[ThreadSafeFastRandom.Next(dataLength)]; // Initialize centroids randomly
                    centroids[i] = data[rnd.Next(dataLength)]; // Initialize centroids randomly
                }
            }


            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                // Assign each data point to the nearest centroid
                for (int i = 0; i < dataLength; i++)
                {
                    double minDistance = double.MaxValue;

                    int cluster = 0;
                    for (int j = 0; j < k; j++)
                    {                      
                        double distance = Math.Abs(VectorMath.Distance_SIMDForUnits(data[i], centroids[j]));
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            cluster = j;
                        }
                    }
                    clusterAssignments[i] = cluster;
                }

                // Update centroids based on the mean of the assigned data points
                double[][] newCentroids = new double[k][];
                int[] clusterCounts = new int[k];
                for (int i = 0; i < k; i++)
                    newCentroids[i] = new double[data[0].Length];

                for (int i = 0; i < dataLength; i++)
                {
                    int cluster = clusterAssignments[i];
                    clusterCounts[cluster]++;
                    for (int j = 0; j < data[i].Length; j++)
                        newCentroids[cluster][j] += data[i][j];
                }

                for (int i = 0; i < k; i++)
                {
                    if (clusterCounts[i] > 0)
                    {
                        for (int j = 0; j < newCentroids[i].Length; j++)
                            newCentroids[i][j] /= clusterCounts[i];
                    }
                }

                // Check if centroids have converged
                bool centroidsChanged = false;
                for (int i = 0; i < k; i++)
                {
                    if (!centroids[i].SequenceEqual(newCentroids[i]))
                    {
                        centroidsChanged = true;
                        break;
                    }
                }

                if (!centroidsChanged)
                    break;

                centroids = newCentroids;
            }


            //Key Cluster (equal to K, value items internal IDs)
            Dictionary<int, (double[], HashSet<int>)> d = new Dictionary<int, (double[], HashSet<int>)>(k);
            for (int j = 0; j < k; j++)
                d[j] = (centroids[j], new HashSet<int>());

            int v = 0;
            foreach (var el in clusterAssignments)
            {
                d[el].Item2.Add(v);
                v++;
            }

            return d;
        }//eof-----


        /// <summary>
        /// 
        /// </summary>
        /// <param name="initialCentroids"></param>
        /// <param name="dataToCheck"></param>
        /// <param name="distanceFunc"></param>
        /// <returns></returns>
        public static Dictionary<int, List<int>> KMeansCluster(List<double[]> initialCentroids, List<double[]> dataToCheck)
        {
            //double[][] data, 
            int maxIterations = 100;

            int k = 0;

            if ((initialCentroids?.Count ?? 0) == 0 || (dataToCheck?.Count ?? 0) == 0)
                return new Dictionary<int, List<int>>();

            int dataLength = dataToCheck.Count;
            k = initialCentroids.Count;


            int[] clusterAssignments = new int[dataLength];
            double[][] centroids = new double[k][];

            if ((initialCentroids?.Count ?? 0) > 0)
            {
                for (int i = 0; i < initialCentroids.Count; i++)
                    centroids[i] = initialCentroids[i];
            }
            //else
            //{
            //    //Random rnd=new Random();
            //    ThreadSafeFastRandom.Next(dataLength);

            //    for (int i = 0; i < k; i++)
            //    {
            //        centroids[i] = data[ThreadSafeFastRandom.Next(dataLength)]; // Initialize centroids randomly
            //        //centroids[i] = data[rnd.Next(dataLength)]; // Initialize centroids randomly
            //    }
            //}


            //for (int iteration = 0; iteration < maxIterations; iteration++)
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                // Assign each data point to the nearest centroid
                for (int i = 0; i < dataLength; i++)
                {
                    double minDistance = double.MaxValue;

                    int cluster = 0;
                    for (int j = 0; j < k; j++)
                    {                       
                        double distance = Math.Abs(VectorMath.Distance_SIMDForUnits(dataToCheck[i], centroids[j]));
                        //double distance = Math.Abs(distanceFunc(dataToCheck[i], centroids[j]));
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            cluster = j;
                        }
                    }
                    clusterAssignments[i] = cluster;
                }

                // Update centroids based on the mean of the assigned data points
                double[][] newCentroids = new double[k][];
                int[] clusterCounts = new int[k];
                for (int i = 0; i < k; i++)
                    newCentroids[i] = new double[dataToCheck[0].Length];

                for (int i = 0; i < dataLength; i++)
                {
                    int cluster = clusterAssignments[i];
                    clusterCounts[cluster]++;
                    for (int j = 0; j < dataToCheck[i].Length; j++)
                        newCentroids[cluster][j] += dataToCheck[i][j];
                }

                for (int i = 0; i < k; i++)
                {
                    if (clusterCounts[i] > 0)
                    {
                        for (int j = 0; j < newCentroids[i].Length; j++)
                            newCentroids[i][j] /= clusterCounts[i];
                    }
                }

                // Check if centroids have converged
                bool centroidsChanged = false;
                for (int i = 0; i < k; i++)
                {
                    if (!centroids[i].SequenceEqual(newCentroids[i]))
                    {
                        centroidsChanged = true;
                        break;
                    }
                }

                if (!centroidsChanged)
                    break;

                centroids = newCentroids;
            }


            //Key Cluster (equal to K, value items internal IDs)
            Dictionary<int, List<int>> d = new Dictionary<int, List<int>>(k);
            for (int j = 0; j < k; j++)
                d[j] = new List<int>();

            int v = 0;
            foreach (var el in clusterAssignments)
            {
                d[el].Add(v);
                v++;
            }

            return d;

        }//eof-----


    }//eoc
}//eon
