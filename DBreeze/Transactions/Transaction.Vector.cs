/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Oleksiy Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
*/
#if NET6FUNC || NET472

using DBreeze.VectorLayer;
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

        /// <summary>
        /// <para>Inserts associated with the external documents vectors into VectorStorage created under tableName.</para>
        /// <para>It is possible to have many storages, just use different table names</para>
        /// <para>This operation uses inserts into tableName, so tran.SynchronizeTables must reflect that</para>
        /// </summary>
        /// <param name="tableName">Table where VectorEngine stores its data. Supplied table must be used only by the vector engine.</param>
        /// <param name="vectors">Key is an External Document ID; Value is a vector representing the document.</param>
        /// <param name="deferredIndexing">not used for now, due to the high speed of inserts</param>
        /// <returns>list of internalIDs associated with the inserted documents externalIDs (not necessary to store, communication with Vectors via externalID is possible)</returns>
        public void VectorsInsert(string tableName, IReadOnlyDictionary<byte[], double[]> vectors,bool deferredIndexing = false)
        {
            Vectors world = new Vectors(this, tableName);
            world.AddVectors(vectors);
        }

        /// <summary>
        /// Gets vectors by externalDocumentIDs.
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="externalDocumentIDs"></param>
        /// <returns></returns>
        public IEnumerable<(long, double[])> VectorsGetByExternalDocumentIDs(string tableName, List<byte[]> externalDocumentIDs)
        {
            Vectors world = new Vectors(this, tableName);
            return world.GetVectorsByExternalId(externalDocumentIDs);


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
            Vectors world = new Vectors(this, tableName);
            world.RemoveByExternalId(externalDocumentIDs);
        }


        /// <summary>
        /// Having that each clusterPrototypes vector represent a cluster and itemsToBeClustered must be sparsed between clusterPrototypes
        /// </summary>
        /// <param name="clusterPrototypes">Each element is a vector representing a cluster (around it must concentrate itemsToBeClustered)</param>
        /// <param name="itemsToBeClustered">Vectors of all items that we want to assign to clusters</param>
        /// <returns>Key is a index in clusterPrototypes 0..clusterPrototypes-1; Value: List of indexes in itemsToBeClustered</returns>
        public Dictionary<int, List<int>> VectorsClusteringKMeans(List<double[]> clusterPrototypes, List<double[]> itemsToBeClustered)
        {
            return DBreeze.VectorLayer.Clustering.KMeansCluster(clusterPrototypes, itemsToBeClustered);        
        }


        /// <summary>
        /// <para>Searches similar documents to vectorRequest in VectorStorage of tableName.</para>        
        /// <para>This operation only selects from tableName</para>
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="vectorQuery">query vector, will be searched the most relevant to it in the table</param>
        /// <param name="maxReturnQuantity">Default is 100, maximal return quantity (if very big - complete table will be returned), also can work VectorsSearchSimilar(.,maxReturnQuantity = 10000,.).Take(5000) or VectorsSearchSimilar(...).Take(10)  </param>
        /// <param name="excludingDocuments">Default is null, External Documents IDs to be excluded from the search</param>
        /// <returns>ExternalIds sorted by relevance</returns>
        public IEnumerable<byte[]> VectorsSearchSimilar(string tableName, double[] vectorQuery, int maxReturnQuantity = 100, HashSet<byte[]> excludingDocuments = null)
        {
            if (maxReturnQuantity < 1)
                maxReturnQuantity = 100;

            Vectors world = new Vectors(this, tableName);
            foreach(var el in world.GetSimilar(vectorQuery, maxReturn: maxReturnQuantity, excludingDocuments))
                yield return el.ExternalId;
        }



        /// <summary>
        /// CAP Possible future implementation, can be called from TextDeferredIndexer
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="internalIDs">must be already sorted ascending</param>
        internal void VectorsDoIndexing(string tableName, List<int> internalIDs)
        {
            

        }


    }
}
#else
using System;
using System.Collections.Generic;

namespace DBreeze.Transactions
{
    public partial class Transaction : IDisposable
    {
        /// <summary>
        /// CAP .NET 3.5
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="internalIDs">must be already sorted ascending</param>
        internal void VectorsDoIndexing(string tableName, List<int> internalIDs)
        {


        }
    }
}
#endif