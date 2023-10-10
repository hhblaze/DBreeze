/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Oleksiy Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
*/


using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;


namespace DBreeze.Transactions
{
    public partial class Transaction : IDisposable
    {


        /// <summary>
        /// A cap, this functionality is supported only for .NET STANDARD2.1> and .NET6> .NetCore3.1>
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="internalIDs">must be already sorted ascending</param>
        internal void VectorsDoIndexing(string tableName, List<int> internalIDs)
        {            
            //A cap, this functionality is supported only for .NET STANDARD2.1> and .NET6> .NetCore3.1>
        }


    }
}
