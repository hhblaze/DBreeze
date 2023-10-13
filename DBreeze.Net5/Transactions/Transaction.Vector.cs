/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Oleksiy Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
*/

#if NET6FUNC
using DBreeze.HNSW;
using DBreeze.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBreeze.Transactions
{
    public partial class Transaction : IDisposable
    {


        ///// <summary>
        ///// <para>Inserts associated with the external documents vectors into VectorStorage created under tableName.</para>
        ///// <para>It is possible to have many storages, just use different table names</para>
        ///// <para>This operation uses inserts into tableName, so tran.SynchronizeTables must reflect that</para>
        ///// </summary>
        ///// <param name="tableName">Table where VectorEngine stores its data. Supplied table must be used only by the vector engine.</param>
        ///// <param name="vectors">Key is an External Document ID; Value is a vector representing the document.</param>
        ///// <param name="deferredIndexing"></param>
        ///// <returns>list of internalIDs associated with the inserted documents externalIDs (not necessary to store, communication with Vectors via externalID is possible)</returns>
        //public IReadOnlyList<int> VectorsInsert(string tableName, IReadOnlyDictionary<byte[], float[]> vectors, bool deferredIndexing = false)
        //{
        //    //switch (typeof(TItem))
        //    //{
        //    //    case var type when type == typeof(float[]):
        //    //        VectorsInsertFloat(tableName, vectors);
        //    //        break;
        //    //    default:

        //    //        throw new Exception($"TItem type:  {typeof(TItem).ToString()} is not supported. Supported: float[].");
        //    //}


        //    var world = new SmallWorld<float[], float>(CosineDistance.SIMDForUnits, DefaultRandomGenerator.Instance,
        //       new SmallWorld<float[], float>.Parameters()
        //       {
        //           EnableDistanceCacheForConstruction = true,//true 
        //                                                     // InitialDistanceCacheSize = SampleSize, 
        //           InitialDistanceCacheSize = 0, 
        //           NeighbourHeuristic = NeighbourSelectionHeuristic.SelectHeuristic,
        //           KeepPrunedConnections = true,
        //           ExpandBestSelection = true
        //       },
        //       this, tableName,
        //       threadSafe: false);

        //     return world.AddItems(vectors, deferredIndexing: deferredIndexing);

        //}

        /// <summary>
        /// <para>Inserts associated with the external documents vectors into VectorStorage created under tableName.</para>
        /// <para>It is possible to have many storages, just use different table names</para>
        /// <para>This operation uses inserts into tableName, so tran.SynchronizeTables must reflect that</para>
        /// </summary>
        /// <param name="tableName">Table where VectorEngine stores its data. Supplied table must be used only by the vector engine.</param>
        /// <param name="vectors">Key is an External Document ID; Value is a vector representing the document.</param>
        /// <param name="deferredIndexing"></param>
        /// <returns>list of internalIDs associated with the inserted documents externalIDs (not necessary to store, communication with Vectors via externalID is possible)</returns>
        public IReadOnlyList<int> VectorsInsert(string tableName, IReadOnlyDictionary<byte[], double[]> vectors, bool deferredIndexing = false)
        {
            var world = new SmallWorld<double[], double>(CosineDistanceDouble.SIMDForUnits, DefaultRandomGenerator.Instance,
             new SmallWorld<double[], double>.Parameters()
             {
                 EnableDistanceCacheForConstruction = true,//true 
                                                           // InitialDistanceCacheSize = SampleSize, 
                 InitialDistanceCacheSize = 0,
                 NeighbourHeuristic = NeighbourSelectionHeuristic.SelectHeuristic,
                 KeepPrunedConnections = true,
                 ExpandBestSelection = true
             },
             this, tableName,
             threadSafe: false);

            return world.AddItems(vectors, deferredIndexing: deferredIndexing);


            //var fVectors = vectors.ToDictionary(
            //        kvp => kvp.Key,
            //        kvp => kvp.Value.Select(Convert.ToSingle).ToArray()
            //    );

            //return VectorsInsert(tableName, fVectors, deferredIndexing);
        }


        /// <summary>
        /// Gets vector by externalDocumentIDs. Supported types are types of the insert in the table (float[], double[]...)
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="externalDocumentIDs"></param>
        /// <returns></returns>
        public List<double[]> GetVectorsByExternalDocumentIDs(string tableName, List<byte[]> externalDocumentIDs)
        {
            var world = new SmallWorld<double[], double>(CosineDistanceDouble.SIMDForUnits, DefaultRandomGenerator.Instance,
                            new SmallWorld<double[], double>.Parameters()
                            {
                                EnableDistanceCacheForConstruction = true,//true 
                                                                          // InitialDistanceCacheSize = SampleSize, 
                                InitialDistanceCacheSize = 0,
                                NeighbourHeuristic = NeighbourSelectionHeuristic.SelectHeuristic,
                                KeepPrunedConnections = true,
                                ExpandBestSelection = true
                            },
                            this, tableName,
                            threadSafe: false);

            return (List<double[]>)(object)world.GetVectorsByExternalDocumentIDs(externalDocumentIDs);

            //switch (typeof(TItem))
            //{
            //    case var type when type == typeof(float[]):
            //        {
            //            var world = new SmallWorld<float[], float>(CosineDistance.SIMDForUnits, DefaultRandomGenerator.Instance,
            //                new SmallWorld<float[], float>.Parameters()
            //                {
            //                    EnableDistanceCacheForConstruction = true,//true 
            //                                                              // InitialDistanceCacheSize = SampleSize, 
            //                    InitialDistanceCacheSize = 0,
            //                    NeighbourHeuristic = NeighbourSelectionHeuristic.SelectHeuristic,
            //                    KeepPrunedConnections = true,
            //                    ExpandBestSelection = true
            //                },
            //                this, tableName,
            //                threadSafe: false);

            //            return (List<TItem>)(object)world.GetVectorsByExternalDocumentIDs(externalDocumentIDs);

            //        }
                  
            //    case var type when type == typeof(double[]):
            //        {
            //            var world = new SmallWorld<double[], double>(CosineDistanceDouble.SIMDForUnits, DefaultRandomGenerator.Instance,
            //                new SmallWorld<double[], double>.Parameters()
            //                {
            //                    EnableDistanceCacheForConstruction = true,//true 
            //                                                              // InitialDistanceCacheSize = SampleSize, 
            //                    InitialDistanceCacheSize = 0,
            //                    NeighbourHeuristic = NeighbourSelectionHeuristic.SelectHeuristic,
            //                    KeepPrunedConnections = true,
            //                    ExpandBestSelection = true
            //                },
            //                this, tableName,
            //                threadSafe: false);

            //            return (List<TItem>)(object)world.GetVectorsByExternalDocumentIDs(externalDocumentIDs);

            //        }

            //    default:

            //        throw new Exception($"TItem type:  {typeof(TItem).ToString()} is not supported. Supported: float[], double[].");
            //}

        }


        /// <summary>
        /// <para>Removes External documents from the VectorStorage of tableName.</para>
        /// <para>Internally Uses tableName.Insert operations, so tran.SynchronizeTables must reflect that.</para>
        /// <para>Actually VectorStorage will go on to hold those associated vectors, but they will not appear in tran.VectorsSearchSimilar.</para>
        /// <para>It is possible to restore association with the activate = true parameter</para>
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="externalDocumentIDs"></param>
        /// <param name="activate">default is false (to remove/deactivate operation), it is possible again to activate removed association</param>
        public void VectorsRemove(string tableName, List<byte[]> externalDocumentIDs, bool activate = false)
        {
            //Here is no matter either float[] ot double[]
            var world = new SmallWorld<double[], double>(CosineDistanceDouble.SIMDForUnits, DefaultRandomGenerator.Instance,
                      new SmallWorld<double[], double>.Parameters() //ParametersDouble()
                      {
                          EnableDistanceCacheForConstruction = true,//true 
                          //InitialDistanceCacheSize = SampleSize, 
                          InitialDistanceCacheSize = 0,
                          NeighbourHeuristic = NeighbourSelectionHeuristic.SelectHeuristic,
                          KeepPrunedConnections = true,
                          ExpandBestSelection = true
                      },
                      this, tableName,
                      threadSafe: false);

            world.ActivateItems(externalDocumentIDs, activate);
        }


        /// <summary>
        /// <para>Clustering. Tries to assign all existing vectors in the table (representing external documents) to specified quantity of clusters using KMeans algorithm.</para>
        /// <para>It's possible to try to  create quantityOfClusters, starting from random cluster centers (just supply quantityOfClusters and leave externalDocumentIDsAsCentroids = null) or</para>
        /// <para>to try to create clusters around existing documents (supply externalDocumentIDsAsCentroids; quantityOfClusters will not play), taken those documents initially as centers of their clusters</para>
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="quantityOfClusters"></param>
        /// <param name="externalDocumentIDsAsCentroids">Can be NULL (then quantityOfClusters is taken), tends to create initial centroids for KMean from existing vectors, overrides quantityOfClusters, will make quantityOfClusters equal to externalIDsAsCentroids.Count.</param>
        /// <returns>Key is a number of cluster (not more than externalDocumentIDsAsCentroids.Count -1 or quantityOfClusters-1, when externalDocumentIDsAsCentroids = null); Value: List of externalDocumentIDs in this cluster</returns>
        public Dictionary<int, List<byte[]>> VectorsClusteringKMeans(string tableName, int quantityOfClusters, List<byte[]> externalDocumentIDsAsCentroids = null)
        {
            var world = new SmallWorld<double[], double>(CosineDistanceDouble.SIMDForUnits, DefaultRandomGenerator.Instance,
                          new SmallWorld<double[], double>.Parameters() //ParametersDouble()
                          {
                              EnableDistanceCacheForConstruction = true,//true 
                                                                        // InitialDistanceCacheSize = SampleSize, 
                              InitialDistanceCacheSize = 0,
                              NeighbourHeuristic = NeighbourSelectionHeuristic.SelectHeuristic,
                              KeepPrunedConnections = true,
                              ExpandBestSelection = true
                          },
                          this, tableName,
                          threadSafe: false);

            return world.KMeans(quantityOfClusters, externalDocumentIDsAsCentroids);

            //VectorStat vstat = null;
            //var rowVectorStat = this.Select<byte[], byte[]>(tableName, 1.ToIndex());
            //if (rowVectorStat.Exists)
            //    vstat = VectorStat.BiserDecode(rowVectorStat.Value);

            //if (vstat == null)
            //    return new Dictionary<int, List<byte[]>>();

            //switch (vstat.VectorType)
            //{
            //    case "float[]":
            //        {
            //            var world = new SmallWorld<float[], float>(CosineDistance.SIMDForUnits, DefaultRandomGenerator.Instance,
            //              new SmallWorld<float[], float>.Parameters() //ParametersDouble()
            //              {
            //                  EnableDistanceCacheForConstruction = true,//true 
            //                                                            // InitialDistanceCacheSize = SampleSize, 
            //                  InitialDistanceCacheSize = 0,
            //                  NeighbourHeuristic = NeighbourSelectionHeuristic.SelectHeuristic,
            //                  KeepPrunedConnections = true,
            //                  ExpandBestSelection = true
            //              },
            //              this, tableName,
            //              threadSafe: false);

            //            return world.KMeans(quantityOfClusters, externalDocumentIDsAsCentroids);
            //        }                    
            //    case "double[]":
            //        {
            //            var world = new SmallWorld<double[], double>(CosineDistanceDouble.SIMDForUnits, DefaultRandomGenerator.Instance,
            //              new SmallWorld<double[], double>.Parameters() //ParametersDouble()
            //              {
            //                  EnableDistanceCacheForConstruction = true,//true 
            //                                                            // InitialDistanceCacheSize = SampleSize, 
            //                  InitialDistanceCacheSize = 0,
            //                  NeighbourHeuristic = NeighbourSelectionHeuristic.SelectHeuristic,
            //                  KeepPrunedConnections = true,
            //                  ExpandBestSelection = true
            //              },
            //              this, tableName,
            //              threadSafe: false);

            //            return world.KMeans(quantityOfClusters, externalDocumentIDsAsCentroids);
            //        }

            //    default:
            //        return new Dictionary<int, List<byte[]>>();
            //}

        }

        /// <summary>
        /// having that each clusterPrototypes vector represent a cluster and itemsToBeClustered must be sparsed between clusterPrototypes
        /// </summary>
        /// <param name="clusterPrototypes">Each element is a vector representing a cluster (around it must concentrate itemsToBeClustered)</param>
        /// <param name="itemsToBeClustered">Vectors of all items that we want to assign to clusters</param>
        /// <returns>Key is a index in clusterPrototypes 0..clusterPrototypes-1; Value: List of indexes in itemsToBeClustered</returns>
        public Dictionary<int, List<int>> VectorsClusteringKMeans(List<double[]> clusterPrototypes, List<double[]> itemsToBeClustered)
        {
            var world = new SmallWorld<double[], double>(CosineDistanceDouble.SIMDForUnits, DefaultRandomGenerator.Instance,
                           new SmallWorld<double[], double>.Parameters() //ParametersDouble()
                           {
                               EnableDistanceCacheForConstruction = true,//true 
                                                                         // InitialDistanceCacheSize = SampleSize, 
                               InitialDistanceCacheSize = 0,
                               NeighbourHeuristic = NeighbourSelectionHeuristic.SelectHeuristic,
                               KeepPrunedConnections = true,
                               ExpandBestSelection = true
                           },
                           this, String.Empty,
                           threadSafe: false);

            return world.KMeans(clusterPrototypes, itemsToBeClustered);
        }

        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="clusterPrototypes"></param>
        ///// <param name="itemsToBeClustered"></param>
        ///// <returns></returns>
        //public Dictionary<int, List<int>> VectorsClusteringKMeans(List<float[]> clusterPrototypes, List<float[]> itemsToBeClustered)
        //{
        //    var world = new SmallWorld<float[], float>(CosineDistance.SIMDForUnits, DefaultRandomGenerator.Instance,
        //            new SmallWorld<float[], float>.Parameters() //ParametersDouble()
        //            {
        //                EnableDistanceCacheForConstruction = true,//true 
        //                                                          //InitialDistanceCacheSize = SampleSize, 
        //                InitialDistanceCacheSize = 0,
        //                NeighbourHeuristic = NeighbourSelectionHeuristic.SelectHeuristic,
        //                KeepPrunedConnections = true,
        //                ExpandBestSelection = true
        //            },
        //            this, String.Empty,
        //            threadSafe: false);

        //    return world.KMeans(clusterPrototypes, itemsToBeClustered);
        //}



        ///// <summary>
        ///// <para>Searches similar documents to vectorRequest in VectorStorage of tableName.</para>        
        ///// <para>This operation only selects from tableName</para>
        ///// </summary>
        ///// <param name="tableName"></param>
        ///// <param name="vectorRequest"></param>
        ///// <param name="returnQuantity">Default is 3, how many similar documents must be returned</param>
        ///// <param name="excludingDocuments">Default is null, External Documents IDs to be excluded from the search</param>
        ///// <returns>ExternalId, Distance, InternalId. Less Distance - more similar document; result set is already sorted ascending by the Distance</returns>
        //public IEnumerable<(byte[] ExternalId, float Distance, int InternalId)> VectorsSearchSimilar(string tableName, float[] vectorRequest, int returnQuantity=3, List<byte[]> excludingDocuments=null)
        //{
        //    if (returnQuantity < 1)
        //        returnQuantity = 1;

        //    var world = new SmallWorld<float[], float>(CosineDistance.SIMDForUnits, DefaultRandomGenerator.Instance,                       
        //               new SmallWorld<float[], float>.Parameters() //ParametersDouble()
        //               {
        //                   EnableDistanceCacheForConstruction = true,//true 
        //                   // InitialDistanceCacheSize = SampleSize, 
        //                   InitialDistanceCacheSize = 0, 
        //                   NeighbourHeuristic = NeighbourSelectionHeuristic.SelectHeuristic,
        //                   KeepPrunedConnections = true,
        //                   ExpandBestSelection = true
        //               },
        //               this, tableName,
        //               threadSafe: false);


        //    var knnResult = world.KNNSearch(vectorRequest, returnQuantity, excludingDocuments);
        //    if (knnResult != null)
        //    {
        //        var searchResult = knnResult.OrderBy(r => Math.Abs(r.Distance));
        //        if (searchResult != null)
        //        {
        //            return searchResult.Select(r => (r.ExternalId, r.Distance, r.Id));
        //        }
        //    }

        //    return new List<(byte[],float, int)>();

        //}

        /// <summary>
        /// <para>Searches similar documents to vectorRequest in VectorStorage of tableName.</para>        
        /// <para>This operation only selects from tableName</para>
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="vectorRequest"></param>
        /// <param name="returnQuantity">Default is 3, how many similar documents must be returned</param>
        /// <param name="excludingDocuments">Default is null, External Documents IDs to be excluded from the search</param>
        /// <returns>ExternalId, Distance, InternalId. Less Distance - more similar document; result set is already sorted ascending by the Distance</returns>
        public IEnumerable<(byte[] ExternalId, double Distance, int InternalId)> VectorsSearchSimilar(string tableName, double[] vectorRequest, int returnQuantity = 3, List<byte[]> excludingDocuments = null)
        {
            if (returnQuantity < 1)
                returnQuantity = 1;

            var world = new SmallWorld<double[], double>(CosineDistanceDouble.SIMDForUnits, DefaultRandomGenerator.Instance,
                       new SmallWorld<double[], double>.Parameters() //ParametersDouble()
                       {
                           EnableDistanceCacheForConstruction = true,//true 
                           // InitialDistanceCacheSize = SampleSize, 
                           InitialDistanceCacheSize = 0,
                           NeighbourHeuristic = NeighbourSelectionHeuristic.SelectHeuristic,
                           KeepPrunedConnections = true,
                           ExpandBestSelection = true
                       },
                       this, tableName,
                       threadSafe: false);


            var knnResult = world.KNNSearch(vectorRequest, returnQuantity, excludingDocuments);
            if (knnResult != null)
            {
                var searchResult = knnResult.OrderBy(r => Math.Abs(r.Distance));
                if (searchResult != null)
                {
                    return searchResult.Select(r => (r.ExternalId, r.Distance, r.Id));
                }
            }

            return new List<(byte[], double, int)>();
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="internalIDs">must be already sorted ascending</param>
        internal void VectorsDoIndexing(string tableName, List<int> internalIDs)
        {
            var world = new SmallWorld<double[], double>(CosineDistanceDouble.SIMDForUnits, DefaultRandomGenerator.Instance,
                         new SmallWorld<double[], double>.Parameters() //ParametersDouble()
                         {
                             EnableDistanceCacheForConstruction = true,//true 
                                                                       // InitialDistanceCacheSize = SampleSize, 
                             InitialDistanceCacheSize = 0,
                             NeighbourHeuristic = NeighbourSelectionHeuristic.SelectHeuristic,
                             KeepPrunedConnections = true,
                             ExpandBestSelection = true
                         },
                         this, tableName,
                         threadSafe: false);

            world.IndexIDs(internalIDs);

            //VectorStat vstat = null;
            //var rowVectorStat = this.Select<byte[], byte[]>(tableName, 1.ToIndex());
            //if (rowVectorStat.Exists)
            //    vstat = VectorStat.BiserDecode(rowVectorStat.Value);

            //if (vstat == null)
            //    return;

            //switch(vstat.VectorType)
            //{
            //    case "float[]":
            //        {
            //            var world = new SmallWorld<float[], float>(CosineDistance.SIMDForUnits, DefaultRandomGenerator.Instance,
            //              new SmallWorld<float[], float>.Parameters() //ParametersDouble()
            //              {
            //                  EnableDistanceCacheForConstruction = true,//true 
            //                                                            // InitialDistanceCacheSize = SampleSize, 
            //                  InitialDistanceCacheSize = 0,
            //                  NeighbourHeuristic = NeighbourSelectionHeuristic.SelectHeuristic,
            //                  KeepPrunedConnections = true,
            //                  ExpandBestSelection = true
            //              },
            //              this, tableName,
            //              threadSafe: false);

            //            world.IndexIDs(internalIDs);
            //        }
            //        break;
            //    case "double[]":
            //        {
            //            var world = new SmallWorld<double[], double>(CosineDistanceDouble.SIMDForUnits, DefaultRandomGenerator.Instance,
            //              new SmallWorld<double[], double>.Parameters() //ParametersDouble()
            //              {
            //                  EnableDistanceCacheForConstruction = true,//true 
            //                                                            // InitialDistanceCacheSize = SampleSize, 
            //                  InitialDistanceCacheSize = 0,
            //                  NeighbourHeuristic = NeighbourSelectionHeuristic.SelectHeuristic,
            //                  KeepPrunedConnections = true,
            //                  ExpandBestSelection = true
            //              },
            //              this, tableName,
            //              threadSafe: false);

            //            world.IndexIDs(internalIDs);
            //        }
            //        break;
            //    default: 
            //        return;
            //}



        }


    }
}
#else
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;


namespace DBreeze.Transactions
{
    public partial class Transaction : IDisposable
    {


        /// <summary>
        /// 
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="internalIDs">must be already sorted ascending</param>
        internal void VectorsDoIndexing(string tableName, List<int> internalIDs)
        {
           
            //A cap, this functionality is supported only for .NET STANDARD2.1> and .NET6> .NetCore3.1>

        }


    }
}
#endif