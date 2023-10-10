/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Oleksiy Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
*/

#if KNNSearch

using DBreeze.TextSearch;
using DBreeze.Utils;
using System;
using System.Collections.Generic;
using System.Runtime.ConstrainedExecution;
using System.Security.Cryptography;
using System.Text;

namespace DBreeze.HNSW
{

    /// <summary>
    /// DBreeze mapping of Nodes and Items
    /// </summary>
    /// <typeparam name="TItem"></typeparam>
    internal class DBStorage<TItem>
    {
        DBreeze.Transactions.Transaction tran = null;
        string tableName = null;



        public DBStorage(DBreeze.Transactions.Transaction tran, string tableName)
        {
            //case var type when type == typeof(float[]):  check that through serializers to DB when adding new types
            switch (typeof(TItem))
            {
                case var type when type == typeof(float[]):
                    break;
                default:

                    throw new Exception($"TItem type:  {typeof(TItem).ToString()} is not supported. Supported: float[].");
            }

            this.tran = tran;
            this.tableName = tableName;

            Nodes = new NodeList(tran, tableName);
            Items = new ItemList<TItem>(tran, tableName);


        }

        public NodeList Nodes { get; private set; }

        public ItemList<TItem> Items { get; private set; }

        /* DBreeze Definition
            1.ToIndex() - VectorStat (holds monotonic counter and other world info)
            2.ToIndex(itemId);V: serialized Item (1536 float[]/double[] from OAI) (ItemInDbFloatArray holds EmbeddingVector and External Doc Id for fast returns in KNNsearch)
            3.ToIndex(itemId);V: serialized Node (NodeInDb holds mostly connections)
            4.ToIndex() - Node entryPoint

            5.ToIndex(byte[] - external documentId); Value: (int) IdOf the Node/Item; helps to remove node

         */



       
        /// <summary>
        /// 
        /// </summary>
        /// <param name="items"></param>
        /// <param name="generator"></param>
        /// <param name="funcAddNode"></param>
        /// <param name="deferredIndexing"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public List<int> AddItems(IReadOnlyDictionary<byte[], TItem> items, IProvideRandomValues generator, Func<int, IProvideRandomValues, Node> funcAddNode, bool deferredIndexing = false)
        {
            CacheIsActive = true;

            List<int> newIDs = new List<int>();
            if (items.Count < 1)
                return newIDs;

            var valueLazy = tran.ValuesLazyLoadingIsOn;
            tran.ValuesLazyLoadingIsOn = false;
                        
            VectorStat vstat = null;
            var rowVectorStat = tran.Select<byte[], byte[]>(tableName, 1.ToIndex());
            if (rowVectorStat.Exists)
                vstat = VectorStat.BiserDecode(rowVectorStat.Value);
            else
                vstat = new VectorStat();

            foreach (var item in items)
            {
                if(item.Key == null)
                    throw new Exception($"External ID, can't be null.");
                                

                //-skipping items adding if it already exists
                var exitem = Items.GetItemByExternalID(item.Key);

                if (exitem.Item1 != null)
                    continue;
                                
                vstat.Id++;
                newIDs.Add(vstat.Id);

                int itemLength = Items.InsertItem(vstat.Id, item.Key, item.Value);

                if(vstat.VectorDimension == -1)
                {
                    //first ever inserted element
                    vstat.VectorDimension = itemLength;
                }
                else if(vstat.VectorDimension != itemLength)
                    throw new Exception($"All vectors in one Vector Table must be of the same dimensionality, if first vector was 1563 elements, then the others must be also 1536 elements etc.");

                var node = funcAddNode(vstat.Id, generator);
                Nodes.Add(node);

                //adding to deferred indexer
                if(deferredIndexing)
                {
                    if (this.tran.tsh == null)
                        this.tran.tsh = new TextSearchHandler(this.tran);

                    if (!this.tran.tsh.DeferredVectors.TryGetValue(this.tableName, out var hs))
                    {
                        this.tran.tsh.DeferredVectors[tableName] = new HashSet<uint>();
                    }

                    this.tran.tsh.DeferredVectors[tableName].Add((uint)vstat.Id);

                    this.tran.tsh.InsertWasPerformed = true;
                }                
            }

            //----------saving new itemID
            if (newIDs.Count > 0)
            {
                tran.Insert<byte[], byte[]>(tableName, 1.ToIndex(), vstat.BiserEncoder().Encode());

                
            }

            tran.ValuesLazyLoadingIsOn = valueLazy;
            return newIDs;
        }

        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="items"></param>
        ///// <param name="generator"></param>
        ///// <returns></returns>
        //public List<int> AddItems(IReadOnlyList<TItem> items, IProvideRandomValues generator, Func<int, IProvideRandomValues, Node> funcAddNode)
        //{
        //    CacheIsActive = true;

        //    List<int> newIDs = new List<int>();
        //    if (items.Count < 1)
        //        return newIDs;

        //    var valueLazy = tran.ValuesLazyLoadingIsOn;
        //    tran.ValuesLazyLoadingIsOn = false;

        //    int itemId = -1;
        //    var rowItemId = tran.Select<byte[], int>(tableName, 1.ToIndex());
        //    if (rowItemId.Exists)
        //        itemId = rowItemId.Value;

        //    foreach (var item in items)
        //    {

        //        itemId++;
        //        newIDs.Add(itemId);

        //        Items.InsertItem(itemId, item);

        //        var node = funcAddNode(itemId, generator);
        //        Nodes.Add(node);
        //    }

        //    //----------saving new itemID
        //    if (newIDs.Count > 0)
        //        tran.Insert<byte[], int>(tableName, 1.ToIndex(), itemId);

        //    tran.ValuesLazyLoadingIsOn = valueLazy;
        //    return newIDs;
        //}

        /// <summary>
        /// 
        /// </summary>
        /// <param name="entryPoint"></param>
        public void FinilizeAddItems(Node entryPoint)
        {

            this.Nodes.FlushNodes();
            //this.Items.FinilizeAddNodesActive();

            SetEntryPoint(entryPoint);

            CacheIsActive = false;                       
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="entryPoint"></param>
        public void FinilizeAddItemsDeferred()
        {

            this.Nodes.FlushNodes();
            CacheIsActive = false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="entryPoint"></param>
        void SetEntryPoint(Node entryPoint)
        {
            tran.Insert<byte[], byte[]>(tableName, 4.ToIndex(), NodeList.FromNode(entryPoint));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Node GetEntryPoint()
        {
            var valueLazy = this.tran.ValuesLazyLoadingIsOn;
            this.tran.ValuesLazyLoadingIsOn = false;
            var rowEntryPont = tran.Select<byte[], byte[]>(tableName, 4.ToIndex());
            this.tran.ValuesLazyLoadingIsOn = valueLazy;
            if (rowEntryPont.Exists)
                return NodeList.ToNode(rowEntryPont.Value);

            return new Node
            {
                Id = -1,
                Connections = new List<List<int>>()
            };

        }

              
        /// <summary>
        /// Both caches are bound
        /// </summary>
        public bool CacheIsActive
        {
            get { return Nodes.CacheIsActive; }
            set
            {
                if (Nodes.CacheIsActive == value)
                    return;

                Nodes.CacheIsActive = value;
                Items.CacheIsActive = value;
            }
        }


    }//eoc DBStorage




    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TItem"></typeparam>
    internal class ItemList<TItem> : List<TItem>
    {
        private bool _CacheIsActive = false;
        public bool CacheIsActive
        {
            get { return this._CacheIsActive; }
            set
            {
                this._CacheIsActive = value;
                if (!this._CacheIsActive)
                {
                    _d.Clear();
                }
            }
        }

        DBreeze.Transactions.Transaction tran = null;
        string tableName = null;


        public ItemList(DBreeze.Transactions.Transaction tran, string tableName)
        {
            this.tran = tran;
            this.tableName = tableName;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="externalId"></param>
        /// <returns>second par internalID</returns>
        public (ItemInDbFloatArray, int) GetItemByExternalID(byte[] externalId)
        {
            var valueLazy = this.tran.ValuesLazyLoadingIsOn;
            this.tran.ValuesLazyLoadingIsOn = false;

            var rowEx = tran.Select<byte[], int>(this.tableName, 5.ToIndex(externalId));

            this.tran.ValuesLazyLoadingIsOn = valueLazy;

            if (rowEx.Exists)
            {
                return (this.GetItemInDB(rowEx.Value), rowEx.Value);
            }

            return (null, 0);
        }
       
        //public void FinilizeAddNodesActive()
        //{
        //    if (this._CacheIsActive)
        //        _d.Clear();
        //}

        /// <summary>
        /// Can return NULL, only For KNN search
        /// </summary>
        /// <param name="index"></param>
        /// <returns>Item, internalId, External ID (can be null, if was not supplied)</returns>
        public (TItem, int, byte[]) GetItem(int index)
        {
            if (this._CacheIsActive)
            {
                if (_d.ContainsKey(index))
                {
                    var itemDB = _d[index];
                    return ((TItem)(object)itemDB.Vector, index, itemDB.ExternalID);
                }
            }

            var valueLazy = this.tran.ValuesLazyLoadingIsOn;
            this.tran.ValuesLazyLoadingIsOn = false;

            var row = tran.Select<byte[], byte[]>(this.tableName, 2.ToIndex(index));
            this.tran.ValuesLazyLoadingIsOn = valueLazy;

            if (!row.Exists)
                return (default(TItem), 0, null);

            switch (typeof(TItem))
            {
                case var type when type == typeof(float[]):
                    {
                        var itemDB = ItemInDbFloatArray.BiserDecode(row.Value);
                        return ((TItem)(object)itemDB.Vector, index, itemDB.ExternalID);                     
                    }
                default:
                    throw new Exception($"TItem type:  {typeof(TItem).ToString()} is not supported");
            }

        }

        /// <summary>
        /// Cache for AddItems
        /// </summary>
        Dictionary<int, ItemInDbFloatArray> _d = new Dictionary<int, ItemInDbFloatArray>();

        public ItemInDbFloatArray GetItemInDB(int index)
        {
            if (this._CacheIsActive)
            {
                if (_d.ContainsKey(index))
                    return _d[index];
            }

            var valueLazy = this.tran.ValuesLazyLoadingIsOn;
            this.tran.ValuesLazyLoadingIsOn = false;
            var row = tran.Select<byte[], byte[]>(this.tableName, 2.ToIndex(index));
            this.tran.ValuesLazyLoadingIsOn = valueLazy;

            if (!row.Exists)
                return null;

            switch (typeof(TItem))
            {
                case var type when type == typeof(float[]):
                    {

                        var itemInDb = ItemInDbFloatArray.BiserDecode(row.Value);
                        if (this._CacheIsActive)
                        {
                            _d[index] = itemInDb;
                        }
                        return itemInDb;
                    }
                default:
                    throw new Exception($"TItem type:  {typeof(TItem).ToString()} is not supported");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public new TItem this[int index]
        {
            get
            {
                
                if (this._CacheIsActive)
                {
                    if (_d.ContainsKey(index))
                        return (TItem)(object)_d[index].Vector;
                }

                var valueLazy = this.tran.ValuesLazyLoadingIsOn;
                this.tran.ValuesLazyLoadingIsOn = false;
                var row = tran.Select<byte[], byte[]>(this.tableName, 2.ToIndex(index));
                this.tran.ValuesLazyLoadingIsOn = valueLazy;

                if (!row.Exists)
                    return (TItem)(object)default(TItem);

                //Deserializing

                switch (typeof(TItem))
                {
                    case var type when type == typeof(float[]):
                        {
                           
                            var itemInDb = ItemInDbFloatArray.BiserDecode(row.Value);
                            if (this._CacheIsActive)
                            {
                                _d[index] = itemInDb;
                            }
                            return (TItem)(object)itemInDb.Vector;
                        }                        
                    default:
                        throw new Exception($"TItem type:  {typeof(TItem).ToString()} is not supported");
                }

            }
            //set
            //{
            //    base[index] = value;
            //}
        }


        /// <summary>
        /// Marks node as deleted
        /// </summary>
        /// <param name="externalItemIds"></param>
        internal void ActivateItems(List<byte[]> externalDocumentsId, bool activate)
        {

            var valueLazy = tran.ValuesLazyLoadingIsOn;
            tran.ValuesLazyLoadingIsOn = false;
            foreach (var eId in externalDocumentsId)
            {
                var rowEx = tran.Select<byte[], int>(this.tableName, 5.ToIndex(eId));
                if (rowEx.Exists)
                {
                    var item = this.GetItemInDB(rowEx.Value);
                    if (item.Deleted != !activate)
                    {
                        item.Deleted = !activate;
                        var btNode = item.BiserEncoder().Encode();
                        tran.Insert<byte[], byte[]>(this.tableName, 2.ToIndex(rowEx.Value), btNode);
                    }
                }
            }

            tran.ValuesLazyLoadingIsOn = valueLazy;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="itemId"></param>
        /// <param name="item"></param>
        /// <exception cref="Exception"></exception>
        public void InsertItem(int itemId, TItem item)
        {
            ItemInDbFloatArray ItemInDb = null;

            byte[] ser = null;

            switch (typeof(TItem))
            {
                case var type when type == typeof(float[]):
                    {
                        ItemInDb = new ItemInDbFloatArray()
                        {
                            Vector = (float[])(object)item
                        };
                        ser = ItemInDb.BiserEncoder().Encode();

                        if (this._CacheIsActive)
                        {
                            // _d[itemId] = item;
                            _d[itemId] = ItemInDb;
                        }
                    }
                    break;
                default:
                    throw new Exception($"TItem type:  {typeof(TItem).ToString()} is not supported");
            }

            tran.Insert<byte[], byte[]>(this.tableName, 2.ToIndex(itemId), ser);
        }
               
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="itemId"></param>
        /// <param name="externalEmbeddingId"></param>
        /// <param name="item"></param>
        /// <returns>Item Length</returns>
        /// <exception cref="Exception"></exception>
        public int InsertItem(int itemId, byte[] externalEmbeddingId, TItem item)
        {
            ItemInDbFloatArray ItemInDb = null;
            int itemLength = -1;
            byte[] ser = null;

            switch (typeof(TItem))
            {
                case var type when type == typeof(float[]):
                    {
                        ItemInDb = new ItemInDbFloatArray()
                        {
                            Vector = (float[])(object)item,
                            ExternalID = externalEmbeddingId
                        };
                        //checking dimension
                        itemLength = ItemInDb.Vector.Length;

                        ser = ItemInDb.BiserEncoder().Encode();

                        if (this._CacheIsActive)
                        {
                            //_d[itemId] = item;
                            _d[itemId] = ItemInDb;
                        }
                    }
                    break;
                default:
                    throw new Exception($"TItem type:  {typeof(TItem).ToString()} is not supported");
            }

            tran.Insert<byte[], byte[]>(this.tableName, 2.ToIndex(itemId), ser);            
            tran.Insert<byte[], int>(this.tableName, 5.ToIndex(externalEmbeddingId), itemId);

            return itemLength;
        }
    }//eoc

   
    /// <summary>
    /// 
    /// </summary>
    internal class NodeList : List<Node>
    {       

        private bool _CacheIsActive = false;
        public bool CacheIsActive
        {
            get { return this._CacheIsActive; }
            set
            {
                this._CacheIsActive = value;
                if(!this._CacheIsActive)
                {
                    _forFlush.Clear();
                }
            }
        }


        DBreeze.Transactions.Transaction tran = null;
        string tableName = null;

        public NodeList(DBreeze.Transactions.Transaction tran, string tableName)
        {
            this.tran = tran;
            this.tableName = tableName;
        }

        /// <summary>
        /// Cache and calculator while AddNodesActive
        /// </summary>
        Dictionary<int, Node> _forFlush = new Dictionary<int, Node>();



        /// <summary>
        /// 
        /// </summary>
        public void FlushNodes()
        {

            if (!this._CacheIsActive)
                return;

            foreach (var n in _forFlush)
            {
                var btNode = NodeList.FromNode(n.Value);
                tran.Insert<byte[], byte[]>(this.tableName, 3.ToIndex(n.Value.Id), btNode);
            }
           
            //this.Clear();
        }

        public new void Add(Node node)
        {
            if (this._CacheIsActive)
                _forFlush[node.Id] = node;

           // base.Add(node);
        }

        // Override the Count property from the List<T> class
        public new int Count
        {
            get
            {
                var rowVectorStat = tran.Select<byte[], byte[]>(tableName, 1.ToIndex());
                if (rowVectorStat.Exists)
                {
                    var vstat = VectorStat.BiserDecode(rowVectorStat.Value);
                    return vstat.Id + 1;
                }
                else
                    return 0;

            }
        }

       
        // Override the indexer (this[]) from the List<T> class
        public new Node this[int index]
        {
            get
            {
                if (this._CacheIsActive)
                {
                    if (_forFlush.ContainsKey(index))
                        return _forFlush[index];
                }

                var valueLazy = this.tran.ValuesLazyLoadingIsOn;

                this.tran.ValuesLazyLoadingIsOn = false;
                //var tbl = GetTableSelect();
                var row = tran.Select<byte[], byte[]>(this.tableName, 3.ToIndex(index));
                this.tran.ValuesLazyLoadingIsOn = valueLazy;
                if (row.Exists)
                {
                    //var btee = NodeList.ToNode(row.Value);
                    var node = NodeList.ToNode(row.Value);

                    if (this._CacheIsActive)
                        _forFlush[index] = node;

                    return node;
                }

                //-should not be here
                return new Node { Id = 0, Connections = new List<List<int>>() };
                //return base[index];
            }
            //set
            //{

            //    base[index] = value;
            //}
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="btNode"></param>
        /// <returns></returns>
        public static Node ToNode(byte[] btNode)
        {
            var nodeInDb = NodeInDb.BiserDecode(btNode.Substring(4));
            
            return new Node
            {
                Id = btNode.Substring(0, 4).To_Int32_BigEndian(),
                Connections = nodeInDb.Connections,               
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Node"></param>
        /// <returns></returns>
        public static byte[] FromNode(Node node)
        {
            NodeInDb n = new NodeInDb() { 
                Connections = node.Connections,                 
            };

            return node.Id.To_4_bytes_array_BigEndian()
                .Concat(n.BiserEncoder().Encode());
        }

    }//eoc

    /*
     
         - Biser prepeared serializers for Nodes and items
     
     */

    internal partial class VectorStat
    {
        public int Id { get; set; } = -1;

        public int VectorDimension { get; set; } = -1;

        public string VectorType { get; set; } = null;

    }

    internal partial class VectorStat : Biser.IEncoder
    {


        public Biser.Encoder BiserEncoder(Biser.Encoder existingEncoder = null)
        {
            Biser.Encoder encoder = new Biser.Encoder(existingEncoder);


            encoder.Add(Id);
            encoder.Add(VectorDimension);
            encoder.Add(VectorType);

            return encoder;
        }


        public static VectorStat BiserDecode(byte[] enc = null, Biser.Decoder extDecoder = null)
        {
            Biser.Decoder decoder = null;
            if (extDecoder == null)
            {
                if (enc == null || enc.Length == 0)
                    return null;
                decoder = new Biser.Decoder(enc);
            }
            else
            {
                if (extDecoder.CheckNull())
                    return null;
                else
                    decoder = extDecoder;
            }

            VectorStat m = new VectorStat();



            m.Id = decoder.GetInt();
            m.VectorDimension = decoder.GetInt();
            m.VectorType = decoder.GetString();


            return m;
        }


    }

    internal partial class NodeInDb
    {
        public List<List<int>> Connections { get; set; }                      
    }

    internal partial class NodeInDb : Biser.IEncoder
    {


        public Biser.Encoder BiserEncoder(Biser.Encoder existingEncoder = null)
        {
            Biser.Encoder encoder = new Biser.Encoder(existingEncoder);


            encoder.Add(Connections, (r1) => {
                encoder.Add(r1, (r2) => {
                    encoder.Add(r2);
                });
            });

            return encoder;
        }


        public static NodeInDb BiserDecode(byte[] enc = null, Biser.Decoder extDecoder = null)
        {
            Biser.Decoder decoder = null;
            if (extDecoder == null)
            {
                if (enc == null || enc.Length == 0)
                    return null;
                decoder = new Biser.Decoder(enc);
            }
            else
            {
                if (extDecoder.CheckNull())
                    return null;
                else
                    decoder = extDecoder;
            }

            NodeInDb m = new NodeInDb();



            m.Connections = decoder.CheckNull() ? null : new System.Collections.Generic.List<System.Collections.Generic.List<System.Int32>>();
            if (m.Connections != null)
            {
                decoder.GetCollection(() => {
                    var pvar1 = decoder.CheckNull() ? null : new System.Collections.Generic.List<System.Int32>();
                    if (pvar1 != null)
                    {
                        decoder.GetCollection(() => {
                            var pvar2 = decoder.GetInt();
                            return pvar2;
                        }, pvar1, true);
                    }
                    return pvar1;
                }, m.Connections, true);
            }


            return m;
        }


    }


    internal partial class ItemInDbFloatArray
    {
        public float[] Vector { get; set; }
        public byte[] ExternalID { get; set; }
        public bool Deleted { get; set; } = false;

    }

    internal partial class ItemInDbFloatArray : Biser.IEncoder
    {


        public Biser.Encoder BiserEncoder(Biser.Encoder existingEncoder = null)
        {
            Biser.Encoder encoder = new Biser.Encoder(existingEncoder);


            if (Vector == null)
                encoder.Add((byte)1);
            else
            {
                encoder.Add((byte)0);
                for (int it1 = 0; it1 < Vector.Rank; it1++)
                    encoder.Add(Vector.GetLength(it1));
                foreach (var el2 in Vector)
                    encoder.Add(el2);
            }
            encoder.Add(ExternalID);
            encoder.Add(Deleted);

            return encoder;
        }


        public static ItemInDbFloatArray BiserDecode(byte[] enc = null, Biser.Decoder extDecoder = null)
        {
            Biser.Decoder decoder = null;
            if (extDecoder == null)
            {
                if (enc == null || enc.Length == 0)
                    return null;
                decoder = new Biser.Decoder(enc);
            }
            else
            {
                if (extDecoder.CheckNull())
                    return null;
                else
                    decoder = extDecoder;
            }

            ItemInDbFloatArray m = new ItemInDbFloatArray();



            m.Vector = decoder.CheckNull() ? null : new System.Single[decoder.GetInt()];
            if (m.Vector != null)
            {
                for (int ard1_0 = 0; ard1_0 < m.Vector.GetLength(0); ard1_0++)
                {
                    m.Vector[ard1_0] = decoder.GetFloat();
                }
            }
            m.ExternalID = decoder.GetByteArray();
            m.Deleted = decoder.GetBool();


            return m;
        }


    }
}
#endif