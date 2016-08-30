/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBreeze.Utils
{
    //Support of tuples in .NET35
#if NET35

    /// <summary>
    /// Tuple
    /// </summary>
    /// <typeparam name="T1"></typeparam>
    public class Tuple<T1>
    {
        public Tuple(T1 item1)
        {
            Item1 = item1;
        }

        public T1 Item1 { get; set; }
    }

    public class Tuple<T1, T2> : Tuple<T1>
    {
        public Tuple(T1 item1, T2 item2) : base(item1)
        {
            Item2 = item2;
        }

        public T2 Item2 { get; set; }
    }

    public class Tuple<T1, T2, T3> : Tuple<T1, T2>
    {
        public Tuple(T1 item1, T2 item2, T3 item3) : base(item1, item2)
        {
            Item3 = item3;
        }

        public T3 Item3 { get; set; }
    }

    public class Tuple<T1, T2, T3,T4> : Tuple<T1, T2, T3>
    {
        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4) : base(item1, item2, item3)
        {
            Item4 = item4;
        }

        public T4 Item4 { get; set; }
    }

    public class Tuple<T1, T2, T3, T4, T5> : Tuple<T1, T2, T3, T4>
    {
        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5) : base(item1, item2, item3, item4)
        {
            Item5 = item5;
        }

        public T5 Item5 { get; set; }
    }

    public class Tuple<T1, T2, T3, T4, T5, T6> : Tuple<T1, T2, T3, T4, T5>
    {
        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6) : base(item1, item2, item3, item4, item5)
        {
            Item6 = item6;
        }

        public T6 Item6 { get; set; }
    }

    public class Tuple<T1, T2, T3, T4, T5, T6, T7> : Tuple<T1, T2, T3, T4, T5, T6>
    {
        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7) : base(item1, item2, item3, item4, item5, item6)
        {
            Item7 = item7;
        }

        public T7 Item7 { get; set; }
    }

    public class Tuple<T1, T2, T3, T4, T5, T6, T7, T8> : Tuple<T1, T2, T3, T4, T5, T6, T7>
    {
        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7, T8 item8) : base(item1, item2, item3, item4, item5, item6, item7)
        {
            Item8 = item8;
        }

        public T8 Item8 { get; set; }
    }

    public static class Tuple
    {
        public static Tuple<T1> Create<T1>(T1 item1)
        {
            return new Tuple<T1>(item1);
        }

        public static Tuple<T1, T2> Create<T1, T2>(T1 item1, T2 item2)
        {
            return new Tuple<T1, T2>(item1, item2);
        }

        public static Tuple<T1, T2, T3> Create<T1, T2, T3>(T1 item1, T2 item2, T3 item3)
        {
            return new Tuple<T1, T2, T3>(item1, item2, item3);
        }
    }


#endif
}
