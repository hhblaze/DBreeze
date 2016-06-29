/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBreeze.LianaTrie
{    
    internal class LTrieKid
    {

        //public LTrieKid()
        //{
        //    Exists = false;
        //    LinkToNode = true;
        //    ValueKid = false;
        //}

        ///// <summary>
        ///// 0-255, in case if ValueKid = false, 256 if ValueKid = true;
        ///// </summary>
        //public int Val { get; set; }
        ///// <summary>
        ///// Pointer to the next node or to the value
        ///// </summary>
        //public byte[] Ptr { get; set; }

        ///// <summary>
        ///// Default value is false
        ///// </summary>
        //public bool Exists { get; set; }

        ///// <summary>
        ///// Works when ValueKid = false.
        ///// True is link to node
        ///// False if link to value.
        ///// Default is true.
        ///// </summary>
        //public bool LinkToNode { get; set; }

        ///// <summary>
        ///// Identifies that it's a value for this node, not the kid from 0-255.
        ///// If true, Ptr has link to the value.
        ///// Default is false
        ///// </summary>
        //public bool ValueKid { get; set; }

        ////OPTed

        //public LTrieKid()
        //{
        //    //Exists = false;
        //    //LinkToNode = true;
        //    //ValueKid = false;
        //}

        /// <summary>
        /// 0-255, in case if ValueKid = false, 256 if ValueKid = true;
        /// </summary>
        public int Val = 0;
        /// <summary>
        /// Pointer to the next node or to the value
        /// </summary>
        public byte[] Ptr = null;

        /// <summary>
        /// Default value is false
        /// </summary>
        public bool Exists = false;

        /// <summary>
        /// Works when ValueKid = false.
        /// True is link to node
        /// False if link to value.
        /// Default is true.
        /// </summary>
        public bool LinkToNode = true;

        /// <summary>
        /// Identifies that it's a value for this node, not the kid from 0-255.
        /// If true, Ptr has link to the value.
        /// Default is false
        /// </summary>
        public bool ValueKid = false;

    }
}
