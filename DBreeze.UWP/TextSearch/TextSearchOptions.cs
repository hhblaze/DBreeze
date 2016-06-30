/* 
  Copyright (C) 2014 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBreeze.TextSearch
{
    /// <summary>
    /// Options which are used while tran.InsertDocumentText
    /// </summary>
    public class TextSearchStorageOptions
    {
        public TextSearchStorageOptions()
        {
            FullTextOnly = true;
            SearchWordMinimalLength = 3;
            DeferredIndexing = false;
        }

        /// <summary>
        /// Will store complete word. Search StartWith will be only available. Default is true
        /// </summary>
        public bool FullTextOnly { get; set; }

        /// <summary>
        /// Minimal lenghth of the word to be searched. Default is 3. 
        /// </summary>
        public ushort SearchWordMinimalLength { get; set; }

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

        public TextSearchRequest()
        {
            SearchLogicType = eSearchLogicType.OR;
            SearchWords = String.Empty;
            Quantity = 100;
            NoisyQuantity = 1000;
        }

        /// <summary>
        /// Words separated by space or whatever to search
        /// </summary>        
        public string SearchWords { get; set; }

        /// <summary>
        /// Maximal quantity of documents to be returned. Lower value - lower RAM and speed economy.
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
    }

    /// <summary>
    /// 
    /// </summary>
    public class TextSearchResponse
    {
        public TextSearchResponse()
        {
            FoundDocumentIDs = new List<byte[]>();
            SearchCriteriaIsNoisy = false;
        }

        /// <summary>
        /// Document external IDs, supplied while insert
        /// </summary>
        public List<byte[]> FoundDocumentIDs { get; set; }

        /// <summary>
        /// SearchCriteriaIsNoisy. When one of words in search request contains more then 1000 intersections it will become true.
        /// It can mean that better is to change search word criteria.
        /// Lobster 
        /// Lopata
        /// ...
        /// Loshad
        /// Lom 
        /// .. e.g. words starting from "Lo" is more then 10000
        /// ......and we search by "Lo".
        /// This "Lo" will be automatically excluded from search
        /// </summary>        
        public bool SearchCriteriaIsNoisy { get; set; }
    }
}
