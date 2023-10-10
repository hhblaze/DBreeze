/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Oleksiy Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
*/

#if KNNSearch
using DBreeze.HNSW;
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


        /// <summary>
        /// <para>Inserts associated with the external documents vectors into VectorStorage created under tableName.</para>
        /// <para>It is possible to have many storages, just use different table names</para>
        /// <para>This operation uses inserts into tableName, so tran.SynchronizeTables must reflect that</para>
        /// </summary>
        /// <param name="tableName">Table where VectorEngine stores its data. Supplied table must be used only by the vector engine.</param>
        /// <param name="vectors">Key is an External Document ID; Value is a vector representing the document.</param>
        /// <param name="deferredIndexing"></param>
        /// <returns>list of internalIDs associated with the inserted documents externalIDs (not necessary to store, communication with Vectors via externalID is possible)</returns>
        public IReadOnlyList<int> VectorsInsert(string tableName, IReadOnlyDictionary<byte[], float[]> vectors, bool deferredIndexing = false)
        {
            //switch (typeof(TItem))
            //{
            //    case var type when type == typeof(float[]):
            //        VectorsInsertFloat(tableName, vectors);
            //        break;
            //    default:

            //        throw new Exception($"TItem type:  {typeof(TItem).ToString()} is not supported. Supported: float[].");
            //}


            var world = new SmallWorld<float[], float>(CosineDistance.SIMDForUnits, DefaultRandomGenerator.Instance,
               new SmallWorld<float[], float>.Parameters()
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

        }

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
            var fVectors = vectors.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Select(Convert.ToSingle).ToArray()
                );

            return VectorsInsert(tableName, fVectors, deferredIndexing);
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
            var world = new SmallWorld<float[], float>(CosineDistance.SIMDForUnits, DefaultRandomGenerator.Instance,
                      new SmallWorld<float[], float>.Parameters() //ParametersDouble()
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
        /// <para>Searches similar documents to vectorRequest in VectorStorage of tableName.</para>        
        /// <para>This operation only selects from tableName</para>
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="vectorRequest"></param>
        /// <param name="returnQuantity">Default is 3, how many similar documents must be returned</param>
        /// <param name="excludingDocuments">Default is null, External Documents IDs to be excluded from the search</param>
        /// <returns>ExternalId, Distance, InternalId. Less Distance - more similar document; result set is already sorted ascending by the Distance</returns>
        public IEnumerable<(byte[] ExternalId, float Distance, int InternalId)> VectorsSearchSimilar(string tableName, float[] vectorRequest, int returnQuantity=3, List<byte[]> excludingDocuments=null)
        {
            if (returnQuantity < 1)
                returnQuantity = 1;

            var world = new SmallWorld<float[], float>(CosineDistance.SIMDForUnits, DefaultRandomGenerator.Instance,                       
                       new SmallWorld<float[], float>.Parameters() //ParametersDouble()
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

            return new List<(byte[],float, int)>();

        }

        /// <summary>
        /// <para>Searches similar documents to vectorRequest in VectorStorage of tableName.</para>        
        /// <para>This operation only selects from tableName</para>
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="vectorRequest"></param>
        /// <param name="returnQuantity">Default is 3, how many similar documents must be returned</param>
        /// <param name="excludingDocuments">Default is null, External Documents IDs to be excluded from the search</param>
        /// <returns>ExternalId, Distance, InternalId. Less Distance - more similar document; result set is already sorted ascending by the Distance</returns>
        public IEnumerable<(byte[] ExternalId, float Distance, int InternalId)> VectorsSearchSimilar(string tableName, double[] vectorRequest, int returnQuantity = 3, List<byte[]> excludingDocuments = null)
        {
            return this.VectorsSearchSimilar(tableName, vectorRequest.Select(Convert.ToSingle).ToArray(), returnQuantity, excludingDocuments);
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="internalIDs">must be already sorted ascending</param>
        internal void VectorsDoIndexing(string tableName, List<int> internalIDs)
        {
            var world = new SmallWorld<float[], float>(CosineDistance.SIMDForUnits, DefaultRandomGenerator.Instance,
                       new SmallWorld<float[], float>.Parameters() //ParametersDouble()
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
           
            //A cap, this functionality is supported only for .NET STANDARD 2.1 and .NET Core>6

        }


    }
}
#endif