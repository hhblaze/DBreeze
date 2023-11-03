using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VectorLayer
{
    /// <summary>
    /// Emulates a Storage of Nodes in DBreeze, 
    /// !!!!!!!!!!!!! implement via IFace, to connect different storages
    /// </summary>
    internal class Storage
    {
        uint id = 0;

        /// <summary>
        /// !!!!!!!!!!!!!!  Interfaced to DB etc
        /// !!! public temp for testing
        /// </summary>
        public Dictionary<uint, Node> nodesStorage = new Dictionary<uint, Node>();
        Node EntryNode = null;
        bool EntryNodeChanged = false;

        /// <summary>
        /// Interfaced 
        /// </summary>
        /// <returns></returns>
        public uint GetNewId()
        {
            id++;
            return id;
        }

        /// <summary>
        ///  !!!!!!!!!!!!!!  Interfaced to DB etc
        ///  Never returns NULL
        /// </summary>
        /// <returns></returns>
        public Node GetEntryNode(bool forInsert=false)
        {
            if (EntryNode == null)
            {
                if (forInsert)
                {
                    EntryNode = new Node()
                    {
                        NodeType = Node.eType.Centroid,
                        Id = GetNewId(),
                        HoldsVectors = true
                    };

                    //-only for the first time, when not from DB
                    ChangedNodes[EntryNode.Id] = EntryNode;
                }
            }

            if (EntryNode == null)
                return new Node() { NodeType = Node.eType.Centroid };
            return EntryNode;
        }

        /// <summary>
        /// 
        /// </summary>
        public void SetEntryNode(Node entryNode)
        {
            EntryNodeChanged = true;
            EntryNode = entryNode;
        }


        /// <summary>
        /// Cached nodes just help to speed up the process, they can be duplicated with changedNodes
        /// </summary>
        public Dictionary<uint, Node> ChangedNodes = new Dictionary<uint, Node>();
        Dictionary<uint, Node> CachedNodes = new Dictionary<uint, Node>();

        /// <summary>
        /// can return Null
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public Node GetNodeById(uint id)
        {
            Node node = null;

            if (ChangedNodes.TryGetValue(id, out node))
                return node;

            if (CachedNodes.TryGetValue(id, out node))
                return node;

            if (nodesStorage.TryGetValue(id, out node))
            {
                //Putting to cache
                CachedNodes[id] = node;
                return node;
            }

            return null;
        }

        /// <summary>
        /// Interfaced
        /// </summary>
        public void SaveNodes()
        {
            if (EntryNode == null)
                return;

            if (EntryNodeChanged)
            {
                if (!ChangedNodes.ContainsKey(EntryNode.Id))
                {
                    ChangedNodes[EntryNode.Id] = EntryNode;
                }
                EntryNodeChanged = false;

                //!!!!!!!!!! -Save in DB Mark which nodeId is EntryNode
            }

            //!!!!!!!-Save new ID cycle in DB

            //!!!!!!- Saving all changed nodes
            foreach (var el in ChangedNodes)
            {
                //reassigning cache
                CachedNodes[el.Key] = el.Value;
                //!!!!!!!!!! -Saving nodes to DB
                nodesStorage[el.Key] = el.Value;
            }

            ChangedNodes.Clear();           

        }
    }
}
