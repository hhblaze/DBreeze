/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using DBreeze;
using DBreeze.Utils;
using DBreeze.DataTypes;

namespace DBreeze.DataStructures
{
    /// <summary>
    /// DBreeze Graph Node
    /// </summary>
    public class DGNode
    {
        /*
        
        Graph table must by Synced 


            Node:
            1 + externalId + internalID(for group parent is needed) (or externalID becomes automatically internal ID if empty) in value content (IF IT MUST BE UNIQUE among nodes), 
                //like "Vegetables" or "tro158po".TOByte + DateTime...
            //2 + internalID in value content

            NodeGroup (we need group, because it can have non-unique name among nodes-namespace)
            10 + Node(parent)InternalId + GroupExternalID 
                    (may be not unique, like "Vegetables": Masha(Node).Vegetables(group),Misha(unique node name).Vegetables)+internalID
            
            InternalID = 0 means GroupNode has no parent

            GroupLinkToNode
            10 + GroupInternalID + { (Link2Node)[1 + externalId + internalID] || (Link2Group)[10+] }

            Scenarios:
            - GetRootGroupNode.GetNodeByExternalID.GetGroupByExternalID.GetAnyLinkByExternalId Select+Forward+Backward possibilities
            - GetNodeByExternalID.GetGroupByExternalID.GetAnyLinkByExternalId Select+Forward+Backward possibilities



            InternalNodeId is uint 4bytes
            Protocol 1 byte
            0 - internal nodes ID counter
            1 - node externalID (byte[]) + internalID (uint) - (binding between externalID and internalID) - HERE also content
            2??? - node internalID (uint) + externalID (byte[]) - (binding between internalID and externalID)             

            3 - internalID (Parent)(uint)+internalID(or better externalID(!null)+internalID) (Kid)(uint) - get all kid (referencing) nodes *-->
            4 - internalID (Node)(uint)+internalID(Parent)(uint) - get all parent (referenced by) nodes *<--

            5? - internalID (Node)(uint) + uint - referencing quantity
            6? - internalID (Node)(uint) + uint - referenced by quantity

               
            -> no need (3,4 is enough + SelectStartFrom and stopping when comparing Key not equal to internalID) 7 - ONLY IF EXIST KID's externalID -> internalID (Parent)(uint)+externalID (if exist) internalID(for uniqness) (Kid)(uint) - get all kid (referencing) nodes *-->
                Searching Kids(Links) of that node by ExternalId
            -> no need 7???we got such 2 internal+external -> GetPeople->GetVasya, Vasya can be many times under different 

            Scenario_
                1.GetNode(People).Get("Vasya")  - IEnum - external ID's must not be unique
                2. GetNode(Vasya) - IEnum 

            Notes:
                - ExternalID can't be null
                - ExternalID can'T be renamed
                

         Node links? From-To-Both
         Node ID(int,uint,long,ulong,short,ushort,byte[]), Name
         Node Content

        Node with BiggestQuantity of links
        Node with SmallestQuantity of links
         ---------------

        Node asks to see all kids
        Node asks to see all parental links
        (Both are together)

            Search by Id, Name
            Get, remove services
            voids may return ID's
         */

        #region Constructor

        public DGNode(byte[] externalId)
        {
            //if (externalId == null)
            //    throw new Exception("ExternalID of DGNode can't be null in constructor");
            this.externalId = externalId;
        }
        
        #endregion

        /// <summary>
        /// Internally setup only. 0 - no ID
        /// </summary>
        internal uint internalId = 0;

        /// <summary>
        /// Will be instatiated internally
        /// </summary>
        internal DGraph graph = null;

        ///// <summary>
        ///// If externalId was modified after internal setup, becomes true, helping to avoid unnecessary checks and inserts concerning externalID
        ///// </summary>
        //bool externalIdWasModified = false;
        internal byte[] externalId = null;

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T ExternalId<T>()
        {
            return DataTypesConvertor.ConvertBack<T>(externalId);
        }

        /// <summary>
        /// Internal ID uint, is set automatically from the system, 0 - not assigned
        /// </summary>
        public uint InternalId { get { return internalId; }  }

        /// <summary>
        /// Indicates that node once was saved to db
        /// </summary>
        public bool Exists { get { return internalId>0; } }

        internal byte[] content = null;
        internal bool contentWasModified = false;
        ///// <summary>
        ///// Any content bound to node from outside
        ///// </summary>
        //public byte[] Content
        //{
        //    get
        //    {
        //        return content;
        //    }
        //    set
        //    {
        //        if (content._ByteArrayEquals(value))
        //            return;
        //        content = value;
        //        contentWasModified = true;
        //    }
        //}

        /// <summary>
        /// Sets content and returns this node back
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="content"></param>
        /// <returns></returns>
        public DGNode SetContent<T>(T content)
        {
            byte[] c = DataTypesConvertor.ConvertValue<T>(content);
            if (c._ByteArrayEquals(this.content))
                return this;
            this.content = c;
            contentWasModified = true;
            return this;
        }

        /// <summary>
        /// Returns content back
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T GetContent<T>()
        {
            return DataTypesConvertor.ConvertBack<T>(this.content);           
        }

        /// <summary>
        /// Will create connection to from the Node to Kids
        /// </summary>
        public List<DGNode> LinksKids = new List<DGNode>();

        void CheckGraph()
        {
            if (this.graph == null)
                throw new Exception("Node must be returned by DGraph instance");
        }

        /// <summary>
        /// Performs correspondent Inserts into DBreeze table
        /// </summary>
        public void Update()
        {
            CheckGraph();
            this.graph.AddNode(this);
        }

        /// <summary>
        /// Adds referencing links, parents can be added only as a kid of a parent node.
        /// </summary>
        /// <param name="kidNodes"></param>
        /// <returns></returns>
        public DGNode AddKids(List<DGNode> kidNodes)
        {
            LinksKids.AddRange(kidNodes);
            return this;
        }
               

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="externalId"></param>
        /// <param name="AsReadVisibilityScope"></param>
        /// <returns></returns>
        public IEnumerable<DGNode> GetKid<T>(T externalId, bool AsReadVisibilityScope = false)
        {
            if (this.internalId == 0)   //Not existing in DB node
                yield return null;

            CheckGraph();

            if (externalId == null)
                throw new Exception("Searched ID can't be null");

            byte[] btExId = DataTypesConvertor.ConvertKey<T>(externalId);


            
            
            byte[] key = new byte[] { 3 }.ConcatMany(this.internalId.To_4_bytes_array_BigEndian(), btExId);
            byte[] key1 = null;
            DBreeze.DataTypes.Row<byte[], byte[]> row = null;
            DGNode node = null;
            foreach (var n in this.graph.tran.SelectForwardStartFrom<byte[], byte[]>(this.graph.tableName, key, false, AsReadVisibilityScope))
            {                
                if (!key.Substring(0, key.Length)._ByteArrayEquals(n.Key.Substring(0, key.Length)))
                {
                    break;
                }
                else
                {
                    node = new DGNode(btExId) { content = n.Value, graph = this.graph, internalId = n.Key.Substring(key.Length).To_UInt32_BigEndian() };
                    key1 = new byte[] { 1 }.ConcatMany(btExId, node.internalId.To_4_bytes_array_BigEndian());
                    row = this.graph.tran.Select<byte[], byte[]>(this.graph.tableName, key1, true);
                    node.content = row.Value;
                    yield return node;
                }
            }
        }
       
        

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="externalId"></param>
        /// <param name="AsReadVisibilityScope"></param>
        /// <returns></returns>
        public IEnumerable<DGNode> GetParent<T>(T externalId, bool AsReadVisibilityScope = false)
        {
            if (this.internalId == 0)   //Not existing in DB node
                yield return null;

            CheckGraph();

            if (externalId == null)
                throw new Exception("Searched ID can't be null");

            byte[] btExId = DataTypesConvertor.ConvertKey<T>(externalId);

            byte[] key = new byte[] { 4 }.ConcatMany(this.internalId.To_4_bytes_array_BigEndian(), btExId);
            byte[] key1 = null;
            DBreeze.DataTypes.Row<byte[], byte[]> row = null;
            DGNode node = null;
            foreach (var n in this.graph.tran.SelectForwardStartFrom<byte[], byte[]>(this.graph.tableName, key, false, AsReadVisibilityScope))
            {
                //if (n.Key.Length <= 5) //1 - protocol + 4 bytes internalID
                //{
                //    break;
                //}
                //else 
                if (!key.Substring(0, key.Length)._ByteArrayEquals(n.Key.Substring(0, key.Length)))
                {
                    break;
                }
                else
                {
                    node = new DGNode(btExId) { content = n.Value, graph = this.graph, internalId = n.Key.Substring(key.Length).To_UInt32_BigEndian() };
                    key1 = new byte[] { 1 }.ConcatMany(btExId, node.internalId.To_4_bytes_array_BigEndian());
                    row = this.graph.tran.Select<byte[], byte[]>(this.graph.tableName, key1, true);
                    node.content = row.Value;
                    yield return node;
                }
            }
        }



        ///// <summary>
        ///// 
        ///// </summary>
        ///// <typeparam name="T"></typeparam>
        ///// <param name="startExternalId"></param>
        ///// <param name="includeStartKey"></param>
        ///// <param name="stopExternalId"></param>
        ///// <param name="includeStopKey"></param>
        ///// <param name="AsReadVisibilityScope"></param>
        ///// <returns></returns>
        //public IEnumerable<DGNode> GetKidsForwardFromTo<T>(T startExternalId, bool includeStartKey, T stopExternalId, bool includeStopKey, bool AsReadVisibilityScope = false)
        //{
        //    if (this.internalId == 0)   //Not existing in DB node
        //        yield return null;

        //    CheckGraph();

        //    if (externalId == null)
        //        throw new Exception("Searched ID can't be null");

        //    byte[] btStartExId = DataTypesConvertor.ConvertKey<T>(startExternalId);
        //    byte[] btStopExId = DataTypesConvertor.ConvertKey<T>(stopExternalId);

        //    byte[] keyA = new byte[] { 3 }.ConcatMany(this.internalId.To_4_bytes_array_BigEndian(), btStartExId);
        //    byte[] keyZ = new byte[] { 3 }.ConcatMany(this.internalId.To_4_bytes_array_BigEndian(), btStopExId);
        //    byte[] key1 = null;
        //    DBreeze.DataTypes.Row<byte[], byte[]> row = null;
        //    DGNode node = null;
        //    foreach (var n in this.graph.tran.SelectForwardFromTo<byte[], byte[]>(this.graph.tableName, keyA, includeStartKey, keyZ, includeStopKey, AsReadVisibilityScope))
        //    {
        //        if (!key.Substring(0, key.Length)._ByteArrayEquals(n.Key.Substring(0, key.Length)))
        //        {
        //            break;
        //        }
        //        else
        //        {
        //            node = new DGNode(btExId) { content = n.Value, graph = this.graph, internalId = n.Key.Substring(key.Length).To_UInt32_BigEndian() };
        //            key1 = new byte[] { 1 }.ConcatMany(btExId, node.internalId.To_4_bytes_array_BigEndian());
        //            row = this.graph.tran.Select<byte[], byte[]>(this.graph.tableName, key1, true);
        //            node.content = row.Value;
        //            yield return node;
        //        }
        //    }
        //}



    }//eoc
}
