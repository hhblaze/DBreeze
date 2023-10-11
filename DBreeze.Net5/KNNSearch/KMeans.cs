using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#if KNNSearch
namespace DBreeze.HNSW
{
    internal static class Clusterization
    {


        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="k"></param>
        /// <param name="distanceFunc"></param>
        /// <param name="initialCentroids">Internal Vectors ID that create initial centroids or null - then will be taken k for the random centroids</param>  
        /// <returns></returns>
        public static Dictionary<int, List<int>> KMeansCluster(ItemList<float[]> data, int k, Func<float[], float[], float> distanceFunc, List<int> initialCentroids = null)
        {
            //float[][] data, 
            int maxIterations = 100;

            int dataLength = data.Count;
            if(dataLength == 0)
                return new Dictionary<int, List<int>>();

            if ((initialCentroids?.Count ?? 0) == 0 && k<1)
                return new Dictionary<int, List<int>>();

            if ((initialCentroids?.Count ?? 0) > 0)
                k= initialCentroids.Count;

            if (dataLength < k)
                k = dataLength;

            int[] clusterAssignments = new int[dataLength];
            float[][] centroids = new float[k][];

            if ((initialCentroids?.Count ?? 0) > 0)
            {
                for (int i = 0; i < initialCentroids.Count; i++)
                    centroids[i] = data[initialCentroids[i]];
            }
            else
            {
                //Random rnd=new Random();
                ThreadSafeFastRandom.Next(dataLength);

                for (int i = 0; i < k; i++)
                {
                    centroids[i] = data[ThreadSafeFastRandom.Next(dataLength)]; // Initialize centroids randomly
                    //centroids[i] = data[rnd.Next(dataLength)]; // Initialize centroids randomly
                }
            }

            //Key Cluster (equal to K, value items internal IDs)
            Dictionary<int, List<int>> d = new Dictionary<int, List<int>>(k);
            for (int j = 0; j < k; j++)
            {
                d[j] = new List<int>();
            }

            //for (int iteration = 0; iteration < maxIterations; iteration++)
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                // Assign each data point to the nearest centroid
                for (int i = 0; i < dataLength; i++)
                {
                    float minDistance = float.MaxValue;

                    int cluster = 0;
                    for (int j = 0; j < k; j++)
                    {
                        float distance = distanceFunc(data[i], centroids[j]);
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            cluster = j;
                        }
                    }
                    clusterAssignments[i] = cluster;                    
                }

                // Update centroids based on the mean of the assigned data points
                float[][] newCentroids = new float[k][];
                int[] clusterCounts = new int[k];
                for (int i = 0; i < k; i++)
                {
                    newCentroids[i] = new float[data[0].Length];
                }

                for (int i = 0; i < dataLength; i++)
                {
                    int cluster = clusterAssignments[i];
                    clusterCounts[cluster]++;
                    for (int j = 0; j < data[i].Length; j++)
                    {
                        newCentroids[cluster][j] += data[i][j];
                    }
                }

                for (int i = 0; i < k; i++)
                {
                    if (clusterCounts[i] > 0)
                    {
                        for (int j = 0; j < newCentroids[i].Length; j++)
                        {
                            newCentroids[i][j] /= clusterCounts[i];
                        }
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
                {
                    break;
                }

                centroids = newCentroids;
            }

            int v = 0;
            foreach (var el in clusterAssignments)
            {
                d[el].Add(v);
                v++;
            }

            return d;
            //return clusterAssignments;
        }//eoc-----










        //public class KMeans
        //{
        //    public int K { get; private set; }
        //    public List<Cluster> Clusters { get; private set; }

        //    public KMeans(int k)
        //    {
        //        K = k;
        //        Clusters = new List<Cluster>();
        //    }

        //    public void Fit(List<DataPoint> dataPoints)
        //    {
        //        // Initialize clusters.
        //        for (int i = 0; i < K; i++)
        //        {
        //            Clusters.Add(new Cluster());
        //        }

        //        // Assign data points to clusters.
        //        foreach (DataPoint dataPoint in dataPoints)
        //        {
        //            Cluster closestCluster = Clusters.MinBy(cluster => cluster.Centroid.DistanceTo(dataPoint));
        //            closestCluster.AddDataPoint(dataPoint);
        //        }

        //        // Calculate new cluster centroids.
        //        foreach (Cluster cluster in Clusters)
        //        {
        //            cluster.CalculateCentroid();
        //        }

        //        // Repeat until convergence.
        //        bool converged = false;
        //        while (!converged)
        //        {
        //            converged = true;

        //            // Assign data points to clusters.
        //            foreach (DataPoint dataPoint in dataPoints)
        //            {
        //                Cluster closestCluster = Clusters.MinBy(cluster => cluster.Centroid.DistanceTo(dataPoint));

        //                if (closestCluster != dataPoint.Cluster)
        //                {
        //                    converged = false;
        //                    dataPoint.Cluster.RemoveDataPoint(dataPoint);
        //                    closestCluster.AddDataPoint(dataPoint);
        //                }
        //            }

        //            // Calculate new cluster centroids.
        //            foreach (Cluster cluster in Clusters)
        //            {
        //                cluster.CalculateCentroid();
        //            }
        //        }
        //    }

        //    public List<int> Predict(List<DataPoint> dataPoints)
        //    {
        //        List<int> predictions = new List<int>();

        //        foreach (DataPoint dataPoint in dataPoints)
        //        {
        //            Cluster closestCluster = Clusters.MinBy(cluster => cluster.Centroid.DistanceTo(dataPoint));
        //            predictions.Add(closestCluster.Label);
        //        }

        //        return predictions;
        //    }
        //}

        //public class Cluster
        //{
        //    public int Label { get; private set; }
        //    public DataPoint Centroid { get; private set; }
        //    public List<DataPoint> DataPoints { get; private set; }

        //    public Cluster()
        //    {
        //        Label = 0;
        //        Centroid = new DataPoint();
        //        DataPoints = new List<DataPoint>();
        //    }

        //    public void AddDataPoint(DataPoint dataPoint)
        //    {
        //        DataPoints.Add(dataPoint);
        //    }

        //    public void RemoveDataPoint(DataPoint dataPoint)
        //    {
        //        DataPoints.Remove(dataPoint);
        //    }

        //    public void CalculateCentroid()
        //    {
        //        Centroid = new DataPoint();

        //        foreach (DataPoint dataPoint in DataPoints)
        //        {
        //            Centroid.Sum(dataPoint);
        //        }

        //        Centroid.Divide(DataPoints.Count);
        //    }
        //}

        //public class DataPoint
        //{
        //    public double[] Values { get; private set; }

        //    public DataPoint()
        //    {
        //        Values = new double[0];
        //    }

        //    public DataPoint(double[] values)
        //    {
        //        Values = values;
        //    }

        //    public void Sum(DataPoint dataPoint)
        //    {
        //        for (int i = 0; i < Values.Length; i++)
        //        {
        //            Values[i] += dataPoint.Values[i];
        //        }
        //    }

        //    public void Divide(int divisor)
        //    {
        //        for (int i = 0; i < Values.Length; i++)
        //        {
        //            Values[i] /= divisor;
        //        }
        //    }

        //    public double DistanceTo(DataPoint dataPoint)
        //    {
        //        double distance = 0;

        //        for (int i = 0; i < Values.Length; i++)
        //        {
        //            distance += (Values[i] - dataPoint.Values[i]) * (Values[i] - dataPoint.Values[i]);
        //        }

        //        return Math.Sqrt(distance);
        //    }
        //}//eoc






    }//eoc
}//eon
#endif