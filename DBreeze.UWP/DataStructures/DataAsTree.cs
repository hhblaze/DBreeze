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

namespace DBreeze.DataStructures
{
    /// <summary>
    /// Ierarchical DataStructure. Any node can have subnodes and own binary content
    /// </summary>
    public class DataAsTree
    {
        /// <summary>
        /// Node name
        /// </summary>
        public string NodeName = String.Empty;

        public long NodeId = 0;
        public long ParentNodeId = 0;

        /// <summary>
        /// This can be filled via constructor or extra when we insert a node
        /// </summary>
        public byte[] NodeContent = null;

        /// <summary>
        /// Internal. Holds reference to DataBlock representing Content. node.GetContent must be used to read out content.
        /// </summary>
        protected byte[] ContentRef = null;

        /// <summary>
        /// Real table name in DBreeze, that will hold the structure
        /// </summary>
        protected string DBreezeTableName = String.Empty;

        protected DBreeze.Transactions.Transaction Transaction = null;

        protected DBreeze.DataTypes.NestedTable nt2Read = null;
        protected DBreeze.DataTypes.NestedTable nt3Read = null;
        protected DBreeze.DataTypes.NestedTable nt2Write = null;    //Storing structure in format ParentId(long)+NodeId(long). Value depends upon type of file or folder
        protected DBreeze.DataTypes.NestedTable nt3Write = null;    //Storing node name for easy search

        protected DataAsTree RootNode = null;
        protected bool maximalInsertSpeed = false;


        /// <summary>
        /// Initializing Root Node
        /// </summary>
        /// <param name="DBreezeTableName">Real table name in DBreeze, that will hold the structure, must be synchronized with other tables in transaction</param>
        /// <param name="tran"></param>
        /// <param name="maximalInsertSpeed">will use DBreeze Technical_SetTable_OverwriteIsNotAllowed among transaction for DBreezeTableName</param>
        public DataAsTree(string DBreezeTableName, DBreeze.Transactions.Transaction tran, bool maximalInsertSpeed = false)
        {
            if (tran == null)
                throw new Exception("Transaction is null");

            if (RootNode != null)
                throw new Exception("Can't be more then one root node, use other constructor");

            //Setting up RootNode
            this.RootNode = this;

            this.Transaction = tran;
            this.maximalInsertSpeed = maximalInsertSpeed;
            this.DBreezeTableName = DBreezeTableName;
            this.NodeId = 0;
            this.ParentNodeId = 0;
        }

        /// <summary>
        /// Init nodes for insert under another node
        /// </summary>
        /// <param name="name"></param>
        /// <param name="content">optionaly can supply NodeContent</param>
        public DataAsTree(string name, byte[] content = null)
        {
            if (String.IsNullOrEmpty(name))
                throw new Exception("Node name can't be empty");

            if (name.To_UTF8Bytes().Length > 256)
                throw new Exception("Node name can't be more then 256");

            this.NodeContent = content;
            this.NodeName = name;

        }

        /// <summary>
        /// Internal
        /// </summary>
        protected DataAsTree()
        {

        }

        /// <summary>
        /// Internal
        /// </summary>
        /// <param name="node"></param>
        void CopyInternals(DataAsTree node)
        {
            node.RootNode = this.RootNode;
            //node.DBreezeTableName = this.DBreezeTableName;
            //node.Transaction = this.Transaction;
            //node.nt2Write = this.nt2Write;
            //node.nt2Read = this.nt2Read;
            //node.nt3Write = this.nt3Write;
            //node.nt3Read = this.nt3Read;
        }

        /// <summary>
        /// Internal
        /// </summary>
        /// <param name="tran"></param>
        void SetupReadTables()
        {
            if (this.RootNode.nt2Read == null)
            {
                this.RootNode.nt2Read = this.RootNode.Transaction.SelectTable(this.RootNode.DBreezeTableName, new byte[] { 2 }, 0);
                this.RootNode.nt2Read.ValuesLazyLoadingIsOn = false;
                this.RootNode.nt3Read = this.RootNode.Transaction.SelectTable(this.RootNode.DBreezeTableName, new byte[] { 3 }, 0);
                this.RootNode.nt3Read.ValuesLazyLoadingIsOn = false;
            }
        }

        /// <summary>
        /// Internal
        /// </summary>
        /// <param name="tran"></param>
        /// <param name="maximalSpeed"></param>
        void SetupWriteTables()
        {
            if (this.RootNode.nt2Write == null)
            {
                //under new byte[] { 1 }  we store Id of the entity(long)
                this.RootNode.nt2Write = this.RootNode.Transaction.InsertTable(this.RootNode.DBreezeTableName, new byte[] { 2 }, 0);  //here is a structure table  
                this.RootNode.nt2Write.ValuesLazyLoadingIsOn = false;
                this.RootNode.nt3Write = this.RootNode.Transaction.InsertTable(this.RootNode.DBreezeTableName, new byte[] { 3 }, 0);  //here is a search by NodeName table
                this.RootNode.nt3Write.ValuesLazyLoadingIsOn = false;
            }

            if (this.RootNode.maximalInsertSpeed)
            {
                this.RootNode.nt2Write.Technical_SetTable_OverwriteIsNotAllowed();
                this.RootNode.nt3Write.Technical_SetTable_OverwriteIsNotAllowed();
            }

        }

        /// <summary>
        /// Internal
        /// </summary>
        /// <param name="tran"></param>
        void CheckTransaction()
        {
            //if (tran == null)
            //    throw new Exception("Transaction is null");

            //if (tran.ManagedThreadId != Transaction.ManagedThreadId || tran.CreatedUdt != Transaction.CreatedUdt)
            //    throw new Exception("Wrong transaction is supplied");

            if (this.RootNode == null || this.RootNode.Transaction == null)
                throw new Exception("RootNode or Transaction is null");

            //if (tran.ManagedThreadId != Transaction.ManagedThreadId || tran.CreatedUdt != Transaction.CreatedUdt)
            //    throw new Exception("Wrong transaction is supplied");
        }

        /// <summary>
        /// Returns node by ParentIdAndNodeId
        /// </summary>
        /// <param name="parentNodeId"></param>
        /// <param name="nodeId"></param>
        /// <returns></returns>
        public DataAsTree GetNodeByParentIdAndNodeId(long parentNodeId, long nodeId)
        {
            CheckTransaction();
            SetupReadTables();

            var row = this.RootNode.nt2Read.Select<byte[], byte[]>(parentNodeId.To_8_bytes_array_BigEndian().Concat(nodeId.To_8_bytes_array_BigEndian()));

            if (row.Exists)
                return SetupNodeFromRow(row);
            return null;
        }

        /// <summary>
        /// Returns nodes with suppled StartsWith name or complete name
        /// </summary>
        /// <param name="nameStartsWithPart"></param>
        /// <param name="tran"></param>
        /// <returns></returns>
        public IEnumerable<DataAsTree> GetNodesByName(string nameStartsWithPart)
        {
            CheckTransaction();
            SetupReadTables();

            byte[] val = null;
            byte[] prt = null;
            DBreeze.DataTypes.Row<byte[], byte[]> nodeRow = null;

            foreach (var row in this.RootNode.nt3Read.SelectForwardStartsWith<string, byte[]>(nameStartsWithPart))
            {
                val = row.Value;
                int i = 0;
                while ((prt = val.Substring(i, 16)) != null)
                {
                    nodeRow = this.RootNode.nt2Read.Select<byte[], byte[]>(prt);
                    if (nodeRow.Exists)
                        yield return SetupNodeFromRow(nodeRow);
                    i += 16;
                }
            }
        }



        /// <summary>
        /// Returns first level nodes by their parent. To go depper, for every returned node can be used ReadOutAllChildrenNodesFromCurrentRecursively
        /// </summary>
        /// <param name="parentId"></param>
        /// <returns></returns>
        public IEnumerable<DataAsTree> GetFirstLevelChildrenNodesByParentId(long parentId)
        {

            CheckTransaction();
            SetupReadTables();

            byte[] fromKey = parentId.To_8_bytes_array_BigEndian().Concat(long.MinValue.To_8_bytes_array_BigEndian());
            byte[] toKey = parentId.To_8_bytes_array_BigEndian().Concat(long.MaxValue.To_8_bytes_array_BigEndian());

            foreach (var row in this.RootNode.nt3Read.SelectForwardFromTo<byte[], byte[]>(fromKey, true, toKey, true))
            {
                yield return SetupNodeFromRow(row);
            }
        }

        /// <summary>
        /// Internal
        /// </summary>
        /// <param name="node"></param>
        /// <param name="protocolVersion"></param>
        /// <returns></returns>
        byte[] SetupValueRowFromNode(DataAsTree node, byte protocolVersion)
        {
            byte[] val = null;

            if (String.IsNullOrEmpty(node.NodeName))
                throw new Exception("Node name can't be empty");

            byte[] name = node.NodeName.To_UTF8Bytes();

            if (name.Length > 256)
                throw new Exception("Node name can't be more then 256");

            /*
          Protocol:
              1byte - protocol version (starting from 1)
              ...                                                
              than due to the protocol description
          */

            switch (protocolVersion)
            {
                case 1:
                    //First protocol type
                    /*
                    Protocol:
                        1byte - protocol version (starting from 1)
                        16bytes link to content (or 0)
                        1byte - lenght of NodeName                
                        Nbytes - Name                                                   
                    */

                    val = new byte[] { protocolVersion }.ConcatMany
                        (
                            (node.ContentRef != null) ? node.ContentRef : new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                            new byte[] { (byte)name.Length },
                            name
                        );
                    break;
            }

            return val;
        }

        /// <summary>
        /// Internal
        /// </summary>
        /// <param name="row"></param>
        /// <returns></returns>
        DataAsTree SetupNodeFromRow(DBreeze.DataTypes.Row<byte[], byte[]> row)
        {
            DataAsTree node = null;
            byte[] val = row.Value;

            /*
            Protocol:
                1byte - protocol version (starting from 1)
                ...                                                
                than due to the protocol description
            */

            node = new DataAsTree();

            switch (val[0])
            {
                case 1: //First protocol type
                        /*
                        Protocol:
                            1byte - protocol version (starting from 1)
                            16bytes link to content (or 0)
                            1byte - lenght of NodeName                
                            Nbytes - Name                                                   
                        */

                    if ((val[1] | val[2] | val[3] | val[4] | val[5] | val[6] | val[7] | val[8]) != 0)
                    {
                        //We got content
                        node.ContentRef = val.Substring(1, 16);
                    }
                    node.NodeName = System.Text.Encoding.UTF8.GetString(val.Substring(18, val[17]));
                    break;
            }

            node.ParentNodeId = row.Key.Substring(0, 8).To_Int64_BigEndian();
            node.NodeId = row.Key.Substring(8, 8).To_Int64_BigEndian();

            CopyInternals(node);

            return node;
        }


        /// <summary>
        /// Reading all children nodes
        /// </summary>
        /// <returns></returns>
        public IEnumerable<DataAsTree> GetChildren()
        {
            CheckTransaction();
            SetupReadTables();

            byte[] fromKey = this.NodeId.To_8_bytes_array_BigEndian().Concat(long.MinValue.To_8_bytes_array_BigEndian());
            byte[] toKey = this.NodeId.To_8_bytes_array_BigEndian().Concat(long.MaxValue.To_8_bytes_array_BigEndian());

            foreach (var row in this.RootNode.nt2Read.SelectForwardFromTo<byte[], byte[]>(fromKey, true, toKey, true))
            {
                yield return SetupNodeFromRow(row);
            }
        }


        /// <summary>
        /// Removes node
        /// </summary>
        /// <param name="parentNodeId"></param>
        /// <param name="nodeId"></param>
        /// <param name="tran"></param>
        public void RemoveNode(long parentNodeId, long nodeId)
        {
            CheckTransaction();
            SetupWriteTables();

            /*Removing from NameIndex*/
            var oldRow = this.RootNode.nt2Write.Select<byte[], byte[]>(parentNodeId.To_8_bytes_array_BigEndian().Concat(nodeId.To_8_bytes_array_BigEndian()));
            if (oldRow.Exists)
            {
                var oldNode = SetupNodeFromRow(oldRow);
                RemoveOldNodeFromNameIndex(oldNode.NodeName, parentNodeId.To_8_bytes_array_BigEndian().Concat(nodeId.To_8_bytes_array_BigEndian()));
            }
            /***************************/

            this.RootNode.nt2Write.RemoveKey<byte[]>(parentNodeId.To_8_bytes_array_BigEndian().Concat(nodeId.To_8_bytes_array_BigEndian()));

        }

        /// <summary>
        ///  Removes node
        /// </summary>
        /// <param name="node"></param>
        /// <param name="tran"></param>
        /// <param name="maximalSpeed"></param>
        public void RemoveNode(DataAsTree node)
        {
            this.RemoveNode(node.ParentNodeId, node.NodeId);
        }

        /// <summary>
        /// <para>Adding children to the node</para>
        /// Table, storing data structure, must be in tran.SynchronizeTables list.
        /// Then transaction must be Committed in the end by the programmer.
        /// </summary>
        /// <param name="nodes">Nodes to add to current node</param>
        /// <param name="tran">Existing transaction. Table, storing data structure, must be in tran.SynchronizeTables list</param>
        /// <param name="maximalSpeed">set it to true to gain maximal saving speed</param>
        public void AddNodes(IEnumerable<DataAsTree> nodes)
        {
            CheckTransaction();

            if (nodes == null || nodes.Count() == 0)
                throw new Exception("Nodes are not supplied");

            SetupWriteTables();

            byte[] val = null;

            long maxId = this.RootNode.Transaction.Select<byte[], long>(this.RootNode.DBreezeTableName, new byte[] { 1 }).Value;

            DBreeze.DataTypes.Row<string, byte[]> nodeNameIndexRow = null;
            byte[] btNodeNameIndex = null;

            bool skipToFillNameIndex = false;

            foreach (var node in nodes)
            {

                if (node == null)
                    throw new Exception("Node can't be empty");

                skipToFillNameIndex = false;

                if (node.NodeId == 0)
                {
                    //Insert
                    node.ParentNodeId = this.NodeId;
                    maxId++;
                    node.NodeId = maxId;
                }
                else
                {
                    if (node.NodeId == node.ParentNodeId)
                        throw new Exception("node.NodeId can't be equal to node.ParentNodeId");

                    //Update
                    var oldRow = this.RootNode.nt2Write.Select<byte[], byte[]>(node.ParentNodeId.To_8_bytes_array_BigEndian().Concat(node.NodeId.To_8_bytes_array_BigEndian()));
                    if (oldRow.Exists)
                    {
                        var oldNode = SetupNodeFromRow(oldRow);

                        if (!oldNode.NodeName.Equals(node.NodeName, StringComparison.OrdinalIgnoreCase))
                            RemoveOldNodeFromNameIndex(oldNode.NodeName, node.ParentNodeId.To_8_bytes_array_BigEndian().Concat(node.NodeId.To_8_bytes_array_BigEndian()));
                        else
                            skipToFillNameIndex = true;
                    }
                    else
                    {
                        if (maxId >= node.NodeId && maxId >= node.ParentNodeId)
                        {
                            //Such NodeId was probably deleted, and now wants to be reconnected.
                            //We allow that
                        }
                        else
                        {
                            if (node.NodeId == node.ParentNodeId)
                                throw new Exception("Supplied node.NodeId or node.ParentNodeId don't exist");
                        }
                    }
                }

                //ParentNodeId(long),NodeId(long)
                byte[] key = node.ParentNodeId.To_8_bytes_array_BigEndian()
                    .Concat(node.NodeId.To_8_bytes_array_BigEndian());

                if (node.NodeContent != null)
                {
                    node.ContentRef = this.RootNode.nt2Write.InsertDataBlock(node.ContentRef, node.NodeContent);
                }
                else
                    node.ContentRef = null;

                val = SetupValueRowFromNode(node, 1);

                CopyInternals(node);

                this.RootNode.nt2Write.Insert<byte[], byte[]>(key, val);

                /*node.NodeName index support*/
                if (!skipToFillNameIndex)
                {
                    nodeNameIndexRow = this.RootNode.nt3Write.Select<string, byte[]>(node.NodeName.ToLower());
                    if (nodeNameIndexRow.Exists)
                        btNodeNameIndex = nodeNameIndexRow.Value.Concat(key);
                    else
                        btNodeNameIndex = key;
                    this.RootNode.nt3Write.Insert<string, byte[]>(node.NodeName.ToLower(), btNodeNameIndex);
                }
                /*-----------------------------*/
            }
            //Latest used Id
            this.RootNode.Transaction.Insert<byte[], long>(this.RootNode.DBreezeTableName, new byte[] { 1 }, maxId);

        }//eo func


        /// <summary>
        /// Internal
        /// </summary>
        /// <param name="nameIndexRow"></param>
        /// <param name="keyToRemove"></param>
        /// <returns>new nameIndexRow</returns>
        void RemoveOldNodeFromNameIndex(string oldNodeName, byte[] keyToRemove)
        {
            var oldNodeNameIndexRow = this.RootNode.nt3Write.Select<string, byte[]>(oldNodeName.ToLower());
            if (oldNodeNameIndexRow.Exists)
            {
                byte[] val = oldNodeNameIndexRow.Value;
                byte[] prt = null;
                int i = 0;
                List<byte[]> keys = new List<byte[]>();
                int size = 0;
                while ((prt = val.Substring(i, 16)) != null)
                {
                    i += 16;
                    if (prt._ByteArrayEquals(keyToRemove))
                        continue;
                    size += 16;
                    keys.Add(prt);
                }

                if (keys.Count > 0)
                {
                    byte[] newVal = new byte[size];
                    i = 0;
                    foreach (var key in keys)
                    {
                        newVal.CopyInside(i, key);
                        i += 16;
                    }

                    this.RootNode.nt3Write.Insert<string, byte[]>(oldNodeName.ToLower(), newVal);
                }
                else
                {
                    this.RootNode.nt3Write.RemoveKey<string>(oldNodeName.ToLower());
                }

            }
        }

        /// <summary>
        /// <para>Adding children to the node</para>
        /// Table, storing data structure, must be in tran.SynchronizeTables list.
        /// Then transaction must be Committed in the end by the programmer.
        /// </summary>
        /// <param name="nodes">Nodes to add to current node</param>
        /// <param name="tran">Existing transaction. Table, storing data structure, must be in tran.SynchronizeTables list</param>
        /// <param name="maximalSpeed">set it to true to gain maximal saving speed</param>
        /// <returns>return node with setup parent id</returns>
        public DataAsTree AddNode(DataAsTree node)
        {
            CheckTransaction();

            if (node == null)
                throw new Exception("Nodes is not supplied");

            SetupWriteTables();

            byte[] val = null;

            long maxId = this.RootNode.Transaction.Select<byte[], long>(this.RootNode.DBreezeTableName, new byte[] { 1 }).Value;

            bool skipToFillNameIndex = false;

            if (node.NodeId == 0)
            {
                //Insert
                node.ParentNodeId = this.NodeId;
                maxId++;
                node.NodeId = maxId;
            }
            else
            {
                //Update
                if (node.NodeId == node.ParentNodeId)
                    throw new Exception("node.NodeId can't be equal to node.ParentNodeId");

                var oldRow = this.RootNode.nt2Write.Select<byte[], byte[]>(node.ParentNodeId.To_8_bytes_array_BigEndian().Concat(node.NodeId.To_8_bytes_array_BigEndian()));
                if (oldRow.Exists)
                {
                    var oldNode = SetupNodeFromRow(oldRow);

                    if (!oldNode.NodeName.Equals(node.NodeName, StringComparison.OrdinalIgnoreCase))
                        RemoveOldNodeFromNameIndex(oldNode.NodeName, node.ParentNodeId.To_8_bytes_array_BigEndian().Concat(node.NodeId.To_8_bytes_array_BigEndian()));
                    else
                        skipToFillNameIndex = true;
                }
                else
                {
                    if (maxId >= node.NodeId && maxId >= node.ParentNodeId)
                    {
                        //Such NodeId was probably deleted, and now wants to be reconnected.
                        //We allow that
                    }
                    else
                    {
                        if (node.NodeId == node.ParentNodeId)
                            throw new Exception("Supplied node.NodeId or node.ParentNodeId don't exist");
                    }
                }
            }


            //ParentNodeId(long),NodeId(long)
            byte[] key = node.ParentNodeId.To_8_bytes_array_BigEndian()
                .Concat(node.NodeId.To_8_bytes_array_BigEndian());

            if (node.NodeContent != null)
            {
                node.ContentRef = this.RootNode.nt2Write.InsertDataBlock(node.ContentRef, node.NodeContent);
            }
            else
                node.ContentRef = null;

            val = SetupValueRowFromNode(node, 1);

            CopyInternals(node);

            this.RootNode.nt2Write.Insert<byte[], byte[]>(key, val);

            /*node.NodeName index support*/
            if (!skipToFillNameIndex)
            {
                DBreeze.DataTypes.Row<string, byte[]> nodeNameIndexRow = null;
                byte[] btNodeNameIndex = null;

                nodeNameIndexRow = nt3Write.Select<string, byte[]>(node.NodeName.ToLower());
                if (nodeNameIndexRow.Exists)
                    btNodeNameIndex = nodeNameIndexRow.Value.Concat(key);
                else
                    btNodeNameIndex = key;
                this.RootNode.nt3Write.Insert<string, byte[]>(node.NodeName.ToLower(), btNodeNameIndex);
            }
            /*-----------------------------*/


            //Latest used Id
            this.RootNode.Transaction.Insert<byte[], long>(this.RootNode.DBreezeTableName, new byte[] { 1 }, maxId);

            return node;

        }//eo func


        /// <summary>
        /// GetContent of a node, 
        /// </summary>
        /// <returns>null if content is absent</returns>
        public byte[] GetContent()
        {
            CheckTransaction();

            if (this.ContentRef == null)
                return null;

            this.SetupReadTables();

            return this.RootNode.nt2Read.SelectDataBlock(this.ContentRef);

        }

        /// <summary>
        /// Recursively reads out all children nodes starting from this recursiverly
        /// </summary>
        /// <returns></returns>
        public IEnumerable<DataAsTree> ReadOutAllChildrenNodesFromCurrentRecursively()
        {
            CheckTransaction();

            foreach (var node in ReadOutNodes(this))
                yield return node;
        }

        IEnumerable<DataAsTree> ReadOutNodes(DataAsTree node)
        {
            foreach (var tn in node.GetChildren())
            {
                yield return tn;
                foreach (var inode in ReadOutNodes(tn))
                    yield return inode;
            }
        }

        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="tran"></param>
        //public void Test()
        //{
        //    this.SetupReadTables();

        //    byte[] val = null;
        //    DataAsTree node = null;

        //    foreach (var row in this.RootNode.nt2Read.SelectForward<byte[], byte[]>().Take(200))
        //    {
        //        val = row.Value;
        //        node = this.SetupNodeFromRow(row);

        //        Console.WriteLine("Parent: " + node.ParentNodeId + "; Node: " + node.NodeId + "; " + node.NodeName);
        //    }
        //}



    }
}
