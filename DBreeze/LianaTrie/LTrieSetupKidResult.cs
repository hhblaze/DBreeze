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
    public class LTrieSetupKidResult
    {

        //public LTrieSetupKidResult()
        //{
        //    KeyOldKid = null;
        //    IterateFurther = true;
        //    ValPtrOldKid = null;
        //}

        //public bool IterateFurther { get; set; }
        //public byte[] KeyOldKid { get; set; }
        //public byte[] ValPtrOldKid { get; set; }

        //OPTed

        //public LTrieSetupKidResult()
        //{
        //    //KeyOldKid = null;
        //    //IterateFurther = true;
        //    //ValPtrOldKid = null;
        //}

        public bool IterateFurther = true;
        public byte[] KeyOldKid = null;
        public byte[] ValPtrOldKid = null;

        /// <summary>
        /// Link to the full value line (together with the key)
        /// </summary>
        public byte[] ValueLink = null;
    }
}
