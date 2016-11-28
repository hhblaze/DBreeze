/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;

namespace DBreeze.TextSearch
{
    /// <summary>
    /// Options which are used while tran.InsertDocumentText
    /// </summary>
    public class TextSearchStorageOptions
    {
        /// <summary>
        /// TextSearchStorageOptions
        /// </summary>
        public TextSearchStorageOptions()
        {           
            SearchWordContainsLogicMinimalLength = 3;
            DeferredIndexing = false;   
        }
        
        /// <summary>
        /// Minimal lenght of the word to be searched using "contains" logic. Default is 3. 
        /// </summary>
        public ushort SearchWordContainsLogicMinimalLength { get; set; }

        /// <summary>
        /// Means that document will be indexed in parallel thread and possible search will be available a bit later after commit. 
        /// Is good for the fast main entity Commit and relatively large searchables-set .
        /// Default value is false, means that searchables will be indexed together with Commit and will be available at the same time.
        /// </summary>
        public bool DeferredIndexing { get; set; }
        
    }

    /// <summary>
    /// 
    /// </summary>
    public class TextSearchRequest
    {
        /// <summary>
        /// eSearchLogicType for multiple words 
        /// </summary>
        public enum eSearchLogicType
        {
            /// <summary>
            /// Occurance of all search words in a document is expected.
            /// </summary>
            AND,
            /// <summary>
            /// Occurance of any of the search words in a documented is expected.
            /// </summary>
            OR
        }

        /// <summary>
        /// TextSearchRequest
        /// </summary>
        public TextSearchRequest()
        {
            SearchLogicType = eSearchLogicType.OR;
            SearchWords = String.Empty;            
            Quantity = 100;            
            NoisyQuantity = 1000;
            OrBlocks = new List<List<string>>();          
        }

        /// <summary>
        /// Words separated by space to search, using contains logic (minimal Length is 2 chars)
        /// </summary>        
        public string SearchWords { get; set; }

        /// <summary>
        /// Maximal quantity of documents to be returned.
        /// If equals 0, then IEnumerable TextSearchResponse.GetDocumentIDs() has no limitations of fetching
        /// </summary>        
        public int Quantity { get; set; }

        /// <summary>
        /// AND/OR. Default OR
        /// </summary>        
        public eSearchLogicType SearchLogicType { get; set; }

        /// <summary>
        /// Default value is 1000. It means if such word's Starts With found more than "NoisyQuantity" times, 
        /// it will be excluded from the search.
        /// For example, after uploading 122 russian books and having 700000 unique words, we try to search combination of "ал".
        /// We have found it 3240 times:
        /// ал
        /// ала
        /// алабан
        /// алаберная
        /// алаберный
        /// алаболки
        /// алаболь
        /// ...
        /// etc.
        /// !This is not the quantity of documents where such pattern exists, but StartsWith result of all unique words
        /// </summary>        
        public uint NoisyQuantity { get; set; }

        /// <summary>
        /// Can be used without SearchWords. If exists together with SearchWords then each SearchWord will be copied 
        /// into separate OR-block.                
        /// <para>e.g. we want to find documents containing words "(boy girl) (with) (black red) (umbrella)"</para>
        /// OR-block is inside of brackets, between OR-blocks works AND logic.
        /// <para>In this case documents containing such words will fit: "boy with red umbrella" "boy with black umbrella" "girl with red umbrella" "girl with black umbrella"</para>
        /// <para>If the word starts from the space it will be searched only by "full-word-match", otherwise "contains-logic" is on.</para>
        /// OrBlocks=new List&lt;List&lt;string&gt;&gt;() { new List&lt;string&gt; {"boy","girl" }, new List&lt;string&gt; { " with"}, new List&lt;string&gt; { " red"," black" }, new List&lt;string&gt; { "umbrella" } }
        /// <para>Note, the space in front of some words - activation of full match, "without" - doesn't fit, "boys","girls" - fit, etc..</para>
        /// </summary>
        public List<List<string>> OrBlocks { get; set; }

    }

    ///// <summary>
    ///// 
    ///// </summary>
    //public class TextSearchResponse
    //{
    //    /// <summary>
    //    /// 
    //    /// </summary>
    //    public TextSearchResponse()
    //    {
    //        //FoundDocumentIDs = new List<byte[]>();
    //        SearchCriteriaIsNoisy = false;
    //    }

    //    //internal DataTypes.NestedTable i2e = null;
    //    //internal IEnumerable<uint> q = null;
    //    //internal int ResponseQuantity = 0;    

    //    ///// <summary>
    //    ///// IEnumerable to return found document IDs
    //    ///// </summary>
    //    //public IEnumerable<byte[]> GetDocumentIDs()
    //    //{
    //    //    if (q != null)
    //    //    {
    //    //        DBreeze.DataTypes.Row<int, byte[]> docRow = null;
    //    //        if (ResponseQuantity > 0)
    //    //            q = q.Take(ResponseQuantity);
    //    //        foreach (var el in q)
    //    //        {
    //    //            ////Getting document external ID
    //    //            docRow = i2e.Select<int, byte[]>((int)el);

    //    //            if (docRow.Exists)
    //    //                yield return docRow.Value;
    //    //        }
    //    //    }
    //    //}

    //    ///// <summary>
    //    ///// Document external IDs, supplied while insert
    //    ///// </summary>
    //    //public List<byte[]> FoundDocumentIDs {
    //    //    get
    //    //    {
    //    //        if (ResponseQuantity < 1)
    //    //            ResponseQuantity = 100;
    //    //        return this.GetDocumentIDs().Take(ResponseQuantity).ToList();                
    //    //    }
    //    //}
        
    //    /// <summary>
    //    /// SearchCriteriaIsNoisy. When one of words in search request contains more then 1000 intersections it will become true.
    //    /// It can mean that better is to change search word criteria.
    //    /// Lobster 
    //    /// Lopata
    //    /// ...
    //    /// Loshad
    //    /// Lom 
    //    /// .. e.g. words starting from "Lo" is more then 10000
    //    /// ......and we search by "Lo".
    //    /// This "Lo" will be automatically excluded from search
    //    /// </summary>        
    //    public bool SearchCriteriaIsNoisy { get; set; }
    //}
}
