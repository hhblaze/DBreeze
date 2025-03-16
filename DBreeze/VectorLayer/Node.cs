/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Oleksiy Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
*/
#if NET6FUNC || NET472
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBreeze.VectorLayer
{
    internal partial class Node 
    {
        public enum eType
        {
            /// <summary>
            /// Vector itself
            /// </summary>
            Vector,
            /// <summary>
            /// Centroid Node, can refer either on other centroids of the lower generation, or on vectors
            /// </summary>
            Centroid,
           
        }

        public eType NodeType { get; set; } = eType.Centroid;       

        public long ParentNodeId { get; set; } = -1;       

        /// <summary>
        /// 
        /// </summary>
        /// <param name="storage"></param>
        /// <returns></returns>
        public Node GetParentNode(Storage storage)
        {
            if (ParentNodeId == -1)
                return null;
            return storage.GetNodeById(ParentNodeId);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="parentNode"></param>
        public void SetParentNode(Node parentNode)
        {
            ParentNodeId = parentNode.Id; 
        }

        public List<long> ChildNodes { get; set; } = new List<long>();
        
        public double[] Vector { get; set; }
        public byte[] ExternalId { get; set; }

        public bool HoldsVectors { get; set; } = false;
            
        public long Id { get; set; } = 0;
        
        /// <summary>
        /// Vector is stored under 7.
        /// </summary>
        public bool VectorStored { get; set; } = false;

        /// <summary>
        /// Centroid radius
        /// </summary>
        public double Radius { get; set; } = 0;

        /// <summary>
        /// After Adding this node already was restructed by Restruct Graph
        /// </summary>
        public bool Restructed = false;
        
        /// <summary>
        /// Centroids
        /// </summary>
        /// <param name="node"></param>
        public void AddNode(Node node, Storage storage)
        { 
            node.SetParentNode(this);                      
            this.ChildNodes.Add(node.Id);

            storage.ChangedNodes[Id] = this;
            storage.ChangedNodes[node.Id] = node;

        }

        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="vector"></param>
        /// <param name="storage"></param>
        /// <returns></returns>
        public (Node, SortedDictionary<double, Node>) GetClosestNode(double[] vector, Storage storage)
        {
            if (ChildNodes.Count == 0)
                return (null, null);

            Node minDistanceNode=null;
            double minDistance = double.MaxValue;

            //adds distances
            SortedDictionary<double, Node> d=new SortedDictionary<double, Node>();  

            foreach (var nodeId in ChildNodes)
            {
                var node = storage.GetNodeById(nodeId);
               
                var dist = Math.Abs(VectorMath.Distance_SIMDForUnits(node.Vector, vector));

                double adjustedDist = Math.Max(0, dist - node.Radius);

                if (!d.ContainsKey(adjustedDist)) 
                {
                    d[adjustedDist] = node;
                }
                else
                {  
                    d[adjustedDist + 0.0000000001 * d.Count] = node;
                }


                if (adjustedDist < minDistance)
                {
                    minDistance = adjustedDist;
                    minDistanceNode = node;
                }

                //d[dist] = node;

                //if(dist < minDistance)
                //{
                //    minDistance = dist; 
                //    minDistanceNode = node;
                //}
            }

            return (minDistanceNode, d);
        }


    }

}
#endif