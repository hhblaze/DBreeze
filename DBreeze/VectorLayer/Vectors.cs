using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VectorLayer
{
    internal class Vectors
    {
        /// <summary>
        /// Quantity of vectors per Edge, when reached splitting Edge on two bringing their new centroids on higher level
        /// </summary>
        public int Dense = 100;

        Storage storage = new Storage();

        public Vectors() 
        {            
        }

        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="queryVector"></param>
        ///// <returns></returns>
        //public IEnumerable<Node> GetSimilar(double[] queryVector)
        //{
        //    //-starting iteration from the entry point
        //    var ep = storage.GetEntryNode();
        //    var closestNode = ep.GetClosestNode(queryVector);

        //    if(closestNode.Item1 == null)
        //        return Enumerable.Empty<Node>();
            
        //    return GetSimilarInternal(queryVector, closestNode.Item1);
        //}

        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="queryVector"></param>
        ///// <param name="nd"></param>
        ///// <returns></returns>
        //private IEnumerable<Node> GetSimilarInternal(double[] queryVector, Node nd)
        //{
        //    var closestNode = nd.GetClosestNode(queryVector);
        //    if(closestNode.Item1 != null)
        //    {
        //        if (closestNode.Item1.NodeType == Node.eType.Centroid)
        //        {
        //            foreach(var node in GetSimilarInternal(queryVector, closestNode.Item1))
        //                yield return node;
        //        }

        //        foreach (var node in closestNode.Item2.OrderBy(r => r.Key))
        //        {
        //            yield return node.Value;
        //        }
        //    }             
          
        //}


        public IEnumerable<Node> GetSimilar(double[] queryVector)
        {
            //-starting iteration from the entry point
            var ep = storage.GetEntryNode();

            foreach (var nod in GetSimilarInternal(queryVector, ep))
                yield return nod;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="queryVector"></param>
        /// <param name="nd"></param>
        /// <returns></returns>
        private IEnumerable<Node> GetSimilarInternal(double[] queryVector, Node nd)
        {
            var closestNode = nd.GetClosestNode(queryVector, storage);
            if (closestNode.Item1 != null)
            {
                if(nd.HoldsVectors)
                {
                    foreach (var node in closestNode.Item2) //.OrderBy(r => r.Key)
                    {
                        yield return node.Value;
                    }
                }
                else
                {
                    foreach (var centroidNode in closestNode.Item2)
                    {
                        foreach (var node in GetSimilarInternal(queryVector, centroidNode.Value))
                            yield return node;
                    }
                   
                }
            }
            else
            {

            }

        }


        /// <summary>
        /// Key ExternalId
        /// </summary>
        /// <param name="vectors"></param>
        public void AddVectors(Dictionary<byte[], double[]> vectors)
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

            testInsert();
        }

        /// <summary>
        /// 
        /// </summary>
        private void testInsert()
        {
            int totalChilds = 0;
            foreach(var el in storage.nodesStorage.Where(r=>r.Value.NodeType != Node.eType.Vector)
                .OrderBy(r=>r.Value.HoldsVectors)
                )
            {
                var parentNode = el.Value.GetParentNode(this.storage);
                if (el.Value.HoldsVectors)
                    totalChilds += el.Value.ChildNodes.Count;
                Debug.WriteLine($"Centroid. ID {el.Key}; ParentId: { (parentNode == null ? -1 : (int)parentNode.Id)}; VHold: {el.Value.HoldsVectors}; Childs: {el.Value.ChildNodes.Count} ");
            }

            Debug.WriteLine($"Total Childs: {totalChilds}");
            Debug.WriteLine($"-------------------------------");
        }

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

                    var res = Clustering.KMeansCluster(cNodesChildVectors, quantityOfClusters, VectorMath.Distance_SIMDForUnits);

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
