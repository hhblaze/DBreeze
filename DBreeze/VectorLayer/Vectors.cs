/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Oleksiy Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
*/
#if NET6FUNC || NET472
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBreeze.VectorLayer
{
    internal class Vectors
    {
        /// <summary>
        /// Quantity of vectors/centroids per Graph Edge, when reached splits Edge on EdgeVectorsQuantity/Dense bringing their new centroids on the upper Edge within the Graph
        /// </summary>
        public int Dense = 1000;

        Storage storage = null;

        public Vectors(DBreeze.Transactions.Transaction tran, string tableName) 
        {      
            storage=new Storage(tran, tableName);
            //Taking from Config
            this.Dense = tran._transactionUnit.TransactionsCoordinator._engine.Configuration.VectorLayerConfig.Dense;

            if(this.Dense < 50)
                this.Dense=50;
            else if(this.Dense>5000)
                this.Dense = 5000;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="queryVector"></param>
        /// <param name="maxReturn">Default is 100. Defence mechanism to avoid returning the complete graph. But can be set to whatever. Also can work GetSimlar(query, maxReturn: 10000).Take(5000) </param>
        /// <param name="excludingDocuments"></param>
        /// <returns></returns>
        public IEnumerable<Node> GetSimilar(double[] queryVector, int maxReturn = 100, HashSet<byte[]> excludingDocuments=null)
        {
            //-starting iteration from the entry point
            var ep = storage.GetEntryNode();

            RecSimilarOption option = new RecSimilarOption() { MaxReturn = maxReturn, Returned = 0, ExcludingDocuments = excludingDocuments };

            foreach (var nod in GetSimilarInternal(queryVector, ep, option))
            {
                if (option.Returned > option.MaxReturn)
                    break;

                yield return nod;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        class RecSimilarOption
        {
            public int MaxReturn;
            public int Returned;
            public HashSet<byte[]> ExcludingDocuments = null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="queryVector"></param>
        /// <param name="nd"></param>
        /// <returns></returns>
        private IEnumerable<Node> GetSimilarInternal(double[] queryVector, Node nd, RecSimilarOption option)
        {
            var closestNode = nd.GetClosestNode(queryVector, storage);
            if (closestNode.Item1 != null)
            {
                if (nd.HoldsVectors)
                {
                    foreach (var node in closestNode.Item2) //.OrderBy(r => r.Key)
                    {
                        if (option.ExcludingDocuments != null && option.ExcludingDocuments.Contains(node.Value.ExternalId))
                            continue;

                        if (option.Returned > option.MaxReturn)
                            break;
                        option.Returned++;
                        yield return node.Value;
                    }
                }
                else
                {
                    foreach (var centroidNode in closestNode.Item2)
                    {
                        if (option.Returned > option.MaxReturn)
                            break;

                        foreach (var node in GetSimilarInternal(queryVector, centroidNode.Value, option))
                        {
                            if (option.Returned > option.MaxReturn)
                                break;
                            yield return node;
                        }
                    }

                }
            }
            else
            {
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="externalIDs"></param>
        /// <returns></returns>
        public IEnumerable<(long, double[])> GetVectorsByExternalId(List<byte[]> externalIDs)
        {
            Dictionary<long, double[]> d = new Dictionary<long, double[]>();

            foreach (var el in externalIDs)
            {
                var node = storage.GetNodeByExternalId(el);
               yield return (node.Id, node.Vector);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="externalIDs"></param>
        public void RemoveByExternalId(List<byte[]> externalIDs)
        {
            foreach (var el in externalIDs)
                storage.RemoveNode(el);
        }


        /// <summary>
        /// Key ExternalId, Value Embedding Vector
        /// </summary>
        /// <param name="vectors"></param>
        public void AddVectors(IReadOnlyDictionary<byte[], double[]> vectors)
        {
            if ((vectors?.Count ?? 0) == 0)
                return;

            //!!!!!!! Add FlowControl, of vectors dimensionality

            foreach(var vector in vectors)
            {
                //-starting from the EntryNode, that must contain link to the first level of centroids
                AddVectorInternal(storage.GetEntryNode(forInsert: true), vector.Key, vector.Value);
            }

            RestructGraph();

            //-Saving changed Nodes
            storage.SaveNodes();

            //testInsert();
        }

        ///// <summary>
        ///// 
        ///// </summary>
        //private void testInsert()
        //{
        //    int totalChilds = 0;
        //    foreach(var el in storage.nodesStorage.Where(r=>r.Value.NodeType != Node.eType.Vector)
        //        .OrderBy(r=>r.Value.HoldsVectors)
        //        )
        //    {
        //        var parentNode = el.Value.GetParentNode(this.storage);
        //        if (el.Value.HoldsVectors)
        //            totalChilds += el.Value.ChildNodes.Count;
        //        Debug.WriteLine($"Centroid. ID {el.Key}; ParentId: { (parentNode == null ? -1 : parentNode.Id)}; VHold: {el.Value.HoldsVectors}; Childs: {el.Value.ChildNodes.Count} ");
        //    }

        //    Debug.WriteLine($"Total Childs: {totalChilds}");
        //    Debug.WriteLine($"-------------------------------");
        //}

        /// <summary>
        /// 
        /// </summary>
        /// <param name="externalId"></param>
        /// <param name="vector"></param>
        private void AddVectorInternal(Node entryNode, byte[] externalId, double[] vector)
        {
            if(entryNode.HoldsVectors)
            {
                var newNode = new Node()
                {
                    NodeType = Node.eType.Vector,
                    Id = storage.GetNewId(),
                    Vector = vector,
                    ExternalId = externalId
                };
                newNode.SetParentNode(entryNode);                
                entryNode.AddNode(newNode, storage);
            }
            else
            {
                var closestNode = entryNode.GetClosestNode(vector, storage);
                //closest node is a centroid coming into it to get deeply up to Centroid that holds Vectors
                AddVectorInternal(closestNode.Item1, externalId, vector);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private void RestructGraph()
        {
            bool operated = false;

            var nonRestructedCentroids = storage.ChangedNodes.Where(r => r.Value.NodeType == Node.eType.Centroid && !r.Value.Restructed).ToList();

            foreach (var cNode in nonRestructedCentroids)
            {
                operated = true;

                if (cNode.Value.ChildNodes.Count > Dense)
                {
                    //-splitting on N clusters
                    int quantityOfClusters = Convert.ToInt32(Math.Ceiling((float)cNode.Value.ChildNodes.Count / (float)Dense));                   

                    var cNodesChildVectors = cNode.Value.ChildNodes.Select(r => storage.GetNodeById(r).Vector).ToList();
                    var cNodesChildIDs = cNode.Value.ChildNodes.Select(r => r).ToList();

                    var res = Clustering.KMeansCluster(cNodesChildVectors, quantityOfClusters);

                    int k = 0;
                    Node centroid=cNode.Value;
                    Node parentCentroid = centroid.GetParentNode(storage);
                    if (parentCentroid == null)
                    {
                        //We must create new centroid on top of it that will also become an entry node
                        parentCentroid = new Node()
                        {
                            NodeType = Node.eType.Centroid,
                            Id = storage.GetNewId()                          
                        };

                        //-adding currentCentroid as a child to the new parentNode
                        parentCentroid.ChildNodes.Add(centroid.Id);
                        storage.SetEntryNode(parentCentroid);
                        storage.ChangedNodes.Add(parentCentroid.Id, parentCentroid);
                    }

                    foreach(var cl in res)
                    {
                        //Child IDs that must be in new centroid
                        var childIDsInCentroid = cl.Value.Item2.Select(r => cNodesChildIDs[r]).ToHashSet();

                        //-it can happen that some of clusters will not get any nodes, they will be sparsed between other clusters
                        if (childIDsInCentroid.Count < 1)
                            continue;

                        if (k != 0)
                        {
                            //new centroid, must be created and assigned to the parent of current centroid
                            centroid = new Node()
                            {
                                NodeType = Node.eType.Centroid,
                                Id = storage.GetNewId(),                                
                                Vector = cl.Value.Item1,
                                HoldsVectors =true
                            };

                            centroid.SetParentNode(parentCentroid);

                            //-adding currentCentroid as a child to the new parentNode
                            parentCentroid.ChildNodes.Add(centroid.Id);

                            parentCentroid.Restructed = false;

                            //-adding childs into centroid
                            bool first = true;
                            foreach (var chldNodeId in childIDsInCentroid)
                            {
                                var chldNode = storage.GetNodeById(chldNodeId); 
                                
                                if(first)
                                {
                                    //-adjusting HoldsVectors of the new node
                                    if (chldNode.NodeType != Node.eType.Vector)
                                        centroid.HoldsVectors = false;
                                    first = false;
                                }
                                chldNode.SetParentNode(centroid);
                                centroid.ChildNodes.Add(chldNode.Id);
                            }

                            storage.ChangedNodes.Add(centroid.Id, centroid);
                        }
                        else
                        {
                            //first centroid                            
                            centroid.SetParentNode(parentCentroid);
                            centroid.Vector = cl.Value.Item1;                            
                            //removing unnecessary childs                             
                            centroid.ChildNodes.RemoveAll(r => !childIDsInCentroid.Contains(r));

                        }


                        k++;
                    }//eo foreach split centroid
                }

                cNode.Value.Restructed = true;
            }

            if(operated)
                RestructGraph();
        }//eof


    }
}
#endif