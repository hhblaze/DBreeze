using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VectorLayer
{
    internal class Node 
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
            ///// <summary>
            ///// Entry Node
            ///// </summary>
            //Entry
        }
        
        public eType NodeType = eType.Centroid;
        
        
        private uint? ParentNodeId = null;

        public Node GetParentNode(Storage storage)
        {
            if (ParentNodeId == null)
                return null;
            return storage.GetNodeById((uint)ParentNodeId);
        }

        public void SetParentNode(Node parentNode)
        {
            ParentNodeId = parentNode.Id; 
        }


        //public List<Node> ChildNodes { get; set; } = new List<Node>();
        public List<uint> ChildNodes = new List<uint>();
        public double[] Vector;
        public byte[] ExternalId;
        public bool HoldsVectors=false;
        
        //public Node ParentNode = null;
        //public bool NewNode=false;
        //public bool Changed=false;
        public uint Id = 0;
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
                //!!!!Check Math.Abs
                var dist = Math.Abs(VectorMath.Distance_SIMDForUnits(node.Vector, vector));

                d[dist] = node;

                if(dist < minDistance)
                {
                    minDistance = dist; 
                    minDistanceNode = node;
                }
            }

            return (minDistanceNode, d);
        }


    }


    //internal abstract class NodeBase
    //{
    //    public enum eType
    //    {
    //        /// <summary>
    //        /// Vector itself
    //        /// </summary>
    //        Vector,
    //        /// <summary>
    //        /// Centroid Node
    //        /// </summary>
    //        Centroid,
    //        /// <summary>
    //        /// Entry Node
    //        /// </summary>
    //        Entry
    //    }

    //    public eType NodeType { get; set; }
    //    public double[] Vector { get; set; }
    //}

    //internal class Node : NodeBase
    //{
       
    //}

    //internal class EntryNode : NodeBase
    //{
    //    INode.eType _nodeType = INode.eType.Entry;
    //    public INode.eType NodeType
    //    { get => _nodeType; set => _nodeType = value; }

    //    public List<INode> Nodes { get; set; } = new List<INode>();
    //    public double[] Vector { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    //    public INode GetClosestNode(double[] vector)
    //    {
    //        if (Nodes.Count == 0)
    //            return null;

    //        foreach(var node in Nodes)
    //        {

    //        }
    //    }
    //}

    //internal class CentroidNode : INode
    //{
    //    INode.eType _nodeType = INode.eType.Centroid;
    //    public INode.eType NodeType
    //    { get => _nodeType; set => _nodeType = value; }
    //    public double[] Vector { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    //}
}
