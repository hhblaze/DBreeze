/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBreeze.Exceptions
{
    /// <summary>
    /// This is a specific Exception which will bring to DB is not opearable state, we need to analyze this type of exception separately,
    /// that's why extra class.
    /// </summary>
    public class TableNotOperableException:Exception
    {
   
        public TableNotOperableException()
        {
        }

        public TableNotOperableException(string message)
            : base(message)
        {
        }

        public TableNotOperableException(string message,Exception innerException)
            : base(message, innerException)
        {
        }

    }
}
