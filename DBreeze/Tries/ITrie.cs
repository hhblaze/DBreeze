/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBreeze.Tries
{
    public interface ITrie
    {
        ITrieRootNode GetTrieReadNode(out long modifiedDt);
        long DtTableFixed {get;set;}
    }
}
