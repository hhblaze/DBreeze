using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using DBreeze.Utils;
using DBreeze.DataTypes;

namespace DBreeze.DataStructures
{
    //del "D:\temp\DBR1\*.*" /Q

    /// <summary>
    /// Represents data as a graph
    /// </summary>
    public class DGraph
    {
        internal Transactions.Transaction tran = null;
        internal string tableName = "";

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="tran"></param>
        /// <param name="tableName"></param>
        public DGraph(Transactions.Transaction tran, string tableName)
        {
            if (tran == null)
                throw new Exception("DGraph. Transaction is not instantiated");
            if (String.IsNullOrEmpty(tableName))
                throw new Exception("DGraph. tableName is not supplied");

            this.tran = tran;
            this.tableName = tableName;
        }



        /// <summary>
        /// Tries to grab from DBreeze first found node by supplied externalId
        /// or returns new DGNode (not yet saved to DB) with Node.Exists = false.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="externalId"></param>
        /// <param name="AsReadVisibilityScope"></param>
        /// <returns></returns>
        public DGNode GetNode<T>(T externalId, bool AsReadVisibilityScope = false)
        {
            DGNode node = null;
            byte[] btExId = DataTypesConvertor.ConvertKey<T>(externalId);

            if (externalId != null)
            {   
                byte[] key = new byte[] { 1 }.Concat(btExId);
                
                foreach (var n in tran.SelectForwardStartFrom<byte[], byte[]>(tableName, key, false, AsReadVisibilityScope))
                {
                    if (!key.Substring(0, key.Length)._ByteArrayEquals(n.Key.Substring(0, key.Length)))
                    {
                        break;
                    }
                    else
                    {
                        node = new DGNode(btExId)
                        {
                            content = n.Value,
                            graph = this,
                            internalId = n.Key.Substring(key.Length).To_UInt32_BigEndian()
                        };
                        break;
                    }
                }
            }            

            return node ?? new DGNode(btExId) { graph = this };
        }


        /// <summary>
        /// Creates node (not yet saved to DB). Can be use node.Update, after node fill up.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="externalId"></param>
        /// <returns></returns>
        public DGNode NewNode<T>(T externalId)
        {            
            var btExId = DataTypesConvertor.ConvertKey<T>(externalId);            
            return new DGNode(btExId) { graph = this };
        }
        /// <summary>
        ///  Creates node (not yet saved to DB) with ExternalID=Internal Node Id of type uint. Can be use node.Update, after node fill up.
        /// </summary>
        /// <returns></returns>
        public DGNode CreateNode()
        {            
            return new DGNode(null) { graph = this };
        }
        
        /// <summary>
        /// 
        /// </summary>        
        /// <param name="node"></param>        
        /// <returns></returns>
        public DGNode AddNode(DGNode node)
        {
            if (tran == null || node == null)
                return null;

            return AddNodes(new List<DGNode> { node })[0];
        }

        /// <summary>
        /// If links are supplied, they will be inserted recursive
        /// </summary>        
        /// <param name="nodes"></param>
        public List<DGNode> AddNodes(List<DGNode> nodes)
        {
            if (tran == null || nodes == null || nodes.Count == 0)
                return null;

            uint id = 0;

            AddNodesRecursive(null, nodes, ref id);           

            //Inserting ID counter back
            if (id>0)
                tran.Insert<byte[], byte[]>(tableName, new byte[] { 0 },id.To_4_bytes_array_BigEndian());

            return nodes;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="parent">can be null</param>
        /// <param name="nodes"></param>
        /// <param name="id"></param>
        private void AddNodesRecursive(DGNode parent, List<DGNode> nodes, ref uint id)
        {
            DBreeze.DataTypes.Row<byte[], byte[]> row = null;
            bool newNode = false;
            byte[] key = null;

            foreach (var n in nodes)
            {
                if (n == null)
                    continue;

                newNode = false;

                //Getting new ID
                if (n.internalId == 0)
                {
                    if (id == 0)
                    {
                        row = tran.Select<byte[], byte[]>(tableName, new byte[] { 0 });
                        if (row.Exists)
                            id = row.Value.To_UInt32_BigEndian();
                    }
                    id++;
                    n.internalId = id;
                    if (n.externalId == null)
                        n.externalId = n.internalId.To_4_bytes_array_BigEndian();   //If externalID is empty it becomes uint
                    newNode = true;
                }

                //Instantiating Graph from node, so other operations could be done via it.
                n.graph = this;

                /* Binding between externalID and internalID
                  1 - node externalID (byte[]) + internalID (uint) - (binding between externalID and internalID)
                  2??? - node internalID (uint) + externalID (byte[]) - (binding between internalID and externalID) 
                 */
                //Inserting links from parent to it
                if (newNode)
                {
                    key = new byte[] { 1 }.ConcatMany(n.externalId, n.internalId.To_4_bytes_array_BigEndian());                    
                    tran.Insert<byte[], byte[]>(tableName, key, n.content); //CONTENT

                    ////????????????????? probably we dont need such connection
                    //key = new byte[] { 2 }.ConcatMany(n.internalId.To_4_bytes_array_BigEndian(), n.externalId);
                    //tran.Insert<byte[], byte[]>(tableName, key, null);  //FOR NOW NO CONTENT

                }
                else
                {
                    if (n.contentWasModified)    //Saving new content
                    {
                        key = new byte[] { 1 }.ConcatMany(n.externalId, n.internalId.To_4_bytes_array_BigEndian());
                        tran.Insert<byte[], byte[]>(tableName, key, n.content);
                    }
                }

                if (parent != null)
                {
                    /*
                     * 
                       Current concept
                        To search kids by ExternalId from Parent     
                        3 - internalID (Parent)(uint)+internalID(or better externalID(!null)+internalID) (Kid)(uint) - get all kid (referencing) nodes *-->
                        To search Parent by ExternalId from Node
                        4 - internalID (Node)(uint)+(externalID+internalId)(Parent)(uint) - get all parent (referenced by) nodes *<--

                        Alternative concept (not so good, because, gonna be very difficult to get internal via externalID - are not unique)
                        To search kids by ExternalId from Parent                        
                        3 - internalID (Parent)(uint) + internalID(Kid)(uint) - get all kid (referencing) nodes *-->
                        To search Parent by ExternalId from Node
                        4 - internalID (Node)(uint) + internalId(Parent)(uint) - get all parent (referenced by) nodes *<--
                                                                    
                     */

                    //Filling kid of the parent node links
                    key = new byte[] { 3 }.ConcatMany(parent.internalId.To_4_bytes_array_BigEndian(), n.externalId, n.internalId.To_4_bytes_array_BigEndian());
                    //key = new byte[] { 3 }.ConcatMany(parent.internalId.To_4_bytes_array_BigEndian(), n.internalId.To_4_bytes_array_BigEndian());
                    tran.Insert<byte[], byte[]>(tableName, key, null);

                    //Filling parent of the node link
                    //key = new byte[] { 4 }.ConcatMany(n.internalId.To_4_bytes_array_BigEndian(), parent.externalId,parent.internalId.To_4_bytes_array_BigEndian());
                    //key = new byte[] { 4 }.ConcatMany(n.internalId.To_4_bytes_array_BigEndian(), parent.internalId.To_4_bytes_array_BigEndian());
                    //tran.Insert<byte[], byte[]>(tableName, key, null);
                }

                //Handling kids
                if (n.LinksKids != null && n.LinksKids.Count > 0)
                {
                    AddNodesRecursive(n, n.LinksKids, ref id);
                }               

            }
        }




     

    }//eoc
}//eon
