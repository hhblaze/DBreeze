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

        DBreeze.Transactions.Transaction Transaction = null;

        protected DBreeze.DataTypes.NestedTable nt2Read = null;
        protected DBreeze.DataTypes.NestedTable nt3Read = null;
        protected DBreeze.DataTypes.NestedTable nt2Write = null;    //Storing structure in format ParentId(long)+NodeId(long). Value depends upon type of file or folder
        protected DBreeze.DataTypes.NestedTable nt3Write = null;    //Storing node name for easy search

        /// <summary>
        /// Initializing Root Node like this
        /// </summary>
        /// <param name="DBreezeTableName">Real table name in DBreeze, that will hold the structure</param>
        /// <param name="engine"></param>
        public DataAsTree(string DBreezeTableName, DBreeze.Transactions.Transaction tran)
        {
            if (tran == null)
                throw new Exception("Transaction is null");

            this.Transaction = tran;

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
            node.DBreezeTableName = this.DBreezeTableName;
            node.Transaction = this.Transaction;
            node.nt2Write = this.nt2Write;
            node.nt2Read = this.nt2Read;
            node.nt3Write = this.nt3Write;
            node.nt3Read = this.nt3Read;
        }

        /// <summary>
        /// Internal
        /// </summary>
        /// <param name="tran"></param>
        void SetupReadTables(DBreeze.Transactions.Transaction tran)
        {
            if (nt2Read == null)
            {
                nt2Read = tran.SelectTable(DBreezeTableName, new byte[] { 2 }, 0);
                nt2Read.ValuesLazyLoadingIsOn = false;
                nt3Read = tran.SelectTable(DBreezeTableName, new byte[] { 3 }, 0);
                nt3Read.ValuesLazyLoadingIsOn = false;
            }
        }

        /// <summary>
        /// Internal
        /// </summary>
        /// <param name="tran"></param>
        /// <param name="maximalSpeed"></param>
        void SetupWriteTables(DBreeze.Transactions.Transaction tran, bool maximalSpeed = false)
        {
            if (nt2Write == null)
            {
                //under new byte[] { 1 }  we store Id of the entity(long)
                nt2Write = tran.InsertTable(DBreezeTableName, new byte[] { 2 }, 0);  //here is a structure table  
                nt2Write.ValuesLazyLoadingIsOn = false;
                nt3Write = tran.InsertTable(DBreezeTableName, new byte[] { 3 }, 0);  //here is a search by NodeName table
                nt3Write.ValuesLazyLoadingIsOn = false;
            }

            if (maximalSpeed)
            {
                nt2Write.Technical_SetTable_OverwriteIsNotAllowed();
                nt3Write.Technical_SetTable_OverwriteIsNotAllowed();
            }
        }

        /// <summary>
        /// Internal
        /// </summary>
        /// <param name="tran"></param>
        void CheckTransaction(DBreeze.Transactions.Transaction tran)
        {
            if (tran == null)
                throw new Exception("Transaction is null");

            if (tran.ManagedThreadId != Transaction.ManagedThreadId || tran.CreatedUdt != Transaction.CreatedUdt)
                throw new Exception("Wrong transaction is supplied");
        }

        /// <summary>
        /// Returns node by ParentIdAndNodeId
        /// </summary>
        /// <param name="parentNodeId"></param>
        /// <param name="nodeId"></param>
        /// <returns></returns>
        public DataAsTree GetNodeByParentIdAndNodeId(long parentNodeId, long nodeId, DBreeze.Transactions.Transaction tran)
        {
            CheckTransaction(tran);
            SetupReadTables(tran);

            var row = nt2Read.Select<byte[], byte[]>(parentNodeId.To_8_bytes_array_BigEndian().Concat(nodeId.To_8_bytes_array_BigEndian()));

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
        public IEnumerable<DataAsTree> GetNodesByName(string nameStartsWithPart, DBreeze.Transactions.Transaction tran)
        {
            HashSet<DataAsTree> ret = new HashSet<DataAsTree>();

            CheckTransaction(tran);
            SetupReadTables(tran);

            byte[] val = null;
            byte[] prt = null;
            DBreeze.DataTypes.Row<byte[], byte[]> nodeRow = null;

            foreach (var row in nt3Read.SelectForwardStartsWith<string, byte[]>(nameStartsWithPart))
            {
                val = row.Value;
                int i = 0;
                while ((prt = val.Substring(i, 16)) != null)
                {
                    nodeRow = nt2Read.Select<byte[], byte[]>(prt);
                    if (nodeRow.Exists)
                        yield return SetupNodeFromRow(nodeRow);
                    i += 16;
                }
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
        public IEnumerable<DataAsTree> GetChildren(DBreeze.Transactions.Transaction tran)
        {
            CheckTransaction(tran);
            SetupReadTables(tran);

            byte[] fromKey = this.NodeId.To_8_bytes_array_BigEndian().Concat(long.MinValue.To_8_bytes_array_BigEndian());
            byte[] toKey = this.NodeId.To_8_bytes_array_BigEndian().Concat(long.MaxValue.To_8_bytes_array_BigEndian());

            foreach (var row in nt2Read.SelectForwardFromTo<byte[], byte[]>(fromKey, true, toKey, true))
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
        public void RemoveNode(long parentNodeId, long nodeId, DBreeze.Transactions.Transaction tran, bool maximalSpeed = false)
        {
            CheckTransaction(tran);
            SetupWriteTables(tran, maximalSpeed);

            nt2Write.RemoveKey<byte[]>(parentNodeId.To_8_bytes_array_BigEndian().Concat(nodeId.To_8_bytes_array_BigEndian()));

        }

        /// <summary>
        ///  Removes node
        /// </summary>
        /// <param name="node"></param>
        /// <param name="tran"></param>
        /// <param name="maximalSpeed"></param>
        public void RemoveNode(DataAsTree node, DBreeze.Transactions.Transaction tran, bool maximalSpeed = false)
        {
            this.RemoveNode(node.ParentNodeId, node.NodeId, tran, maximalSpeed);
        }

        /// <summary>
        /// <para>Adding children to the node</para>
        /// Table, storing data structure, must be in tran.SynchronizeTables list.
        /// Then transaction must be Committed in the end by the programmer.
        /// </summary>
        /// <param name="nodes">Nodes to add to current node</param>
        /// <param name="tran">Existing transaction. Table, storing data structure, must be in tran.SynchronizeTables list</param>
        /// <param name="maximalSpeed">set it to true to gain maximal saving speed</param>
        public void AddNodes(IEnumerable<DataAsTree> nodes, DBreeze.Transactions.Transaction tran, bool maximalSpeed = false)
        {
            CheckTransaction(tran);

            if (nodes == null || nodes.Count() == 0)
                throw new Exception("Nodes are not supplied");

            SetupWriteTables(tran, maximalSpeed);

            byte[] val = null;

            long maxId = tran.Select<byte[], long>(this.DBreezeTableName, new byte[] { 1 }).Value;

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
                    //Update
                    var oldRow = nt2Write.Select<byte[], byte[]>(node.ParentNodeId.To_8_bytes_array_BigEndian().Concat(node.NodeId.To_8_bytes_array_BigEndian()));
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
                        //Fake? Creating new Row
                        node.ParentNodeId = this.NodeId;
                        maxId++;
                        node.NodeId = maxId;
                    }
                }

                //ParentNodeId(long),NodeId(long)
                byte[] key = node.ParentNodeId.To_8_bytes_array_BigEndian()
                    .Concat(node.NodeId.To_8_bytes_array_BigEndian());

                if (node.NodeContent != null)
                {
                    node.ContentRef = nt2Write.InsertDataBlock(node.ContentRef, node.NodeContent);
                }
                else
                    node.ContentRef = null;

                val = SetupValueRowFromNode(node, 1);

                CopyInternals(node);

                nt2Write.Insert<byte[], byte[]>(key, val);

                /*node.NodeName index support*/
                if (!skipToFillNameIndex)
                {
                    nodeNameIndexRow = nt3Write.Select<string, byte[]>(node.NodeName.ToLower());
                    if (nodeNameIndexRow.Exists)
                        btNodeNameIndex = nodeNameIndexRow.Value.Concat(key);
                    else
                        btNodeNameIndex = key;
                    nt3Write.Insert<string, byte[]>(node.NodeName.ToLower(), btNodeNameIndex);
                }
                /*-----------------------------*/
            }
            //Latest used Id
            tran.Insert<byte[], long>(this.DBreezeTableName, new byte[] { 1 }, maxId);

        }//eo func


        /// <summary>
        /// Internal
        /// </summary>
        /// <param name="nameIndexRow"></param>
        /// <param name="keyToRemove"></param>
        /// <returns>new nameIndexRow</returns>
        void RemoveOldNodeFromNameIndex(string oldNodeName, byte[] keyToRemove)
        {
            var oldNodeNameIndexRow = nt3Write.Select<string, byte[]>(oldNodeName.ToLower());
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

                    nt3Write.Insert<string, byte[]>(oldNodeName.ToLower(), newVal);
                }
                else
                {
                    nt3Write.RemoveKey<string>(oldNodeName.ToLower());
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
        public DataAsTree AddNode(DataAsTree node, DBreeze.Transactions.Transaction tran, bool maximalSpeed = false)
        {
            CheckTransaction(tran);

            if (node == null)
                throw new Exception("Nodes is not supplied");

            SetupWriteTables(tran, maximalSpeed);

            byte[] val = null;

            long maxId = tran.Select<byte[], long>(this.DBreezeTableName, new byte[] { 1 }).Value;

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

                var oldRow = nt2Write.Select<byte[], byte[]>(node.ParentNodeId.To_8_bytes_array_BigEndian().Concat(node.NodeId.To_8_bytes_array_BigEndian()));
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
                    //Fake? We create new row
                    node.ParentNodeId = this.NodeId;
                    maxId++;
                    node.NodeId = maxId;
                }
            }


            //ParentNodeId(long),NodeId(long)
            byte[] key = node.ParentNodeId.To_8_bytes_array_BigEndian()
                .Concat(node.NodeId.To_8_bytes_array_BigEndian());

            if (node.NodeContent != null)
            {
                node.ContentRef = nt2Write.InsertDataBlock(node.ContentRef, node.NodeContent);
            }
            else
                node.ContentRef = null;

            val = SetupValueRowFromNode(node, 1);

            CopyInternals(node);

            nt2Write.Insert<byte[], byte[]>(key, val);

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
                nt3Write.Insert<string, byte[]>(node.NodeName.ToLower(), btNodeNameIndex);
            }
            /*-----------------------------*/


            //Latest used Id
            tran.Insert<byte[], long>(this.DBreezeTableName, new byte[] { 1 }, maxId);

            return node;

        }//eo func


        /// <summary>
        /// GetContent of a node, 
        /// </summary>
        /// <returns>null if content is absent</returns>
        public byte[] GetContent(DBreeze.Transactions.Transaction tran)
        {
            CheckTransaction(tran);

            if (this.ContentRef == null)
                return null;

            this.SetupReadTables(tran);

            return nt2Read.SelectDataBlock(this.ContentRef);

        }

        /// <summary>
        /// Recursively reads out all nodes starting from this 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<DataAsTree> ReadOutAllNodesFromCurrentRecursively(DBreeze.Transactions.Transaction tran)
        {
            CheckTransaction(tran);

            foreach (var node in ReadOutNodes(this, tran))
                yield return node;
        }

        IEnumerable<DataAsTree> ReadOutNodes(DataAsTree node, DBreeze.Transactions.Transaction tran)
        {
            foreach (var tn in node.GetChildren(tran))
            {
                yield return tn;
                foreach (var inode in ReadOutNodes(tn, tran))
                    yield return inode;
            }
        }

        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="tran"></param>
        //public void Test(DBreeze.Transactions.Transaction tran)
        //{
        //    this.SetupReadTables(tran);

        //    byte[] val = null;            
        //    DataAsTree node = null;

        //    foreach (var row in nt2Read.SelectForward<byte[], byte[]>().Take(200))
        //    {
        //        val = row.Value;
        //        node = this.SetupNodeFromRow(row);

        //        Console.WriteLine("Parent: " + node.ParentNodeId + "; Node: " + node.NodeId + "; " + node.NodeName);
        //    }
        //}



    }
}
