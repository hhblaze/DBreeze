/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.Utils;

namespace DBreeze.LianaTrie
{
    internal class LTrieKidsInNode
    {

        //Field
        byte[] _f = null;

        private ushort DefaultPointerLength = 0;
        //Kids count
        int count = 0;

        private int shift = 0;

        /// <summary>
        /// Indicates that Kids Line contains no Value-Kid
        /// </summary>
        public bool ValueIsEmpty = true;
        byte[] PtrToValue = null;

        bool NeedToSearchExtremums = false;

        /// <summary>
        /// Optimizer. We don't want to overwrite node, if really nothing has changed. In the beginning value is true.
        /// ParseKids function make it false.
        /// Later by Adding or removing elements we either make it true or leave as false.
        /// search in the code  (!AllowSave).
        /// The flag work result is visible in GetKidsForSave function.
        /// </summary>
        private bool AllowSave = true;


        public LTrieKidsInNode(ushort pointerLength)
        {
            DefaultPointerLength = pointerLength;

            PtrToValue = new byte[DefaultPointerLength];

            shift = DefaultPointerLength + 1 + 1; //one will be identifier if the value exists other will be an identifier of the link type

            //First element is always link to the value for the case of the full key meeting at this point,
            //Otherwise we bind this full value the current kid and either move further of leave  it at this point.

            _f = new byte[DefaultPointerLength + 2 + (256 * shift)];    //First is link to value empty or full + 2 fake bytes(to hold shifting) then 256 shifts (0-255)

        }

        //Holding data from 1-256
        int MaxKid = 0;
        bool MaxKidNull = true;
        int MinKid = 0;
        bool MinKidNull = true;

        //private void SetupExtremums(int kid)
        //{
        //    //Handling Extremums
        //    if (MaxKid == null || kid > MaxKid)
        //    {
        //        MaxKid = kid;
        //    }
        //    if (MinKid == null || kid < MinKid)
        //    {
        //        MinKid = kid;
        //    }
        //}



        public void AddKidPointer(int kid, byte[] ptr)
        {
            AllowSave = true;


            kid++;  //bringing kid to the real value
            int kidPlace = kid * shift;



            //Increasing count
            if (_f[kidPlace] == 0)
            {
                //SetupExtremums(kid);
                //Handling Extremums
                if (MaxKidNull || (kid > MaxKid))
                {
                    MaxKidNull = false;
                    MaxKid = kid;
                }
                if (MinKidNull || (kid < MinKid))
                {
                    MinKidNull = false;
                    MinKid = kid;
                }
                /*********************************/
                count++;

            }


            _f[kidPlace] = 1; //Kid Exists 
            _f[kidPlace + 1] = 0; //Link to the Node

            //ptr.Length or Default Pointer Length must be the same
            for (int i = 0; i < ptr.Length; i++)                        //Can be changed on substring
            {
                _f[(kidPlace + 2) + i] = ptr[i];  //Setting up pointer
            }
        }


        /////////////////////// PARSE KIDS UNSAFE CODE

        ///// <summary>
        ///// Returns quantity of reservation slots
        ///// </summary>
        ///// <param name="bKids"></param>
        ///// <returns></returns>
        //public unsafe int ParseKids(byte[] bKids)
        //{

        //    //Quantity reservation slots
        //    int qrs = 0;


        //    //first checking pointer to the value
        //    bool notEmptyPointer = false;



        //    //here switch will be slower
        //    byte bki = 0;

        //    //fixed (byte* uPtrToValue = PtrToValue, uF = _f, ubKids = bKids)
        //    fixed (byte* uF = _f)
        //    {
        //        for (int i = 0; i < DefaultPointerLength; i++)
        //        {
        //            //bki = ubKids[i];
        //            bki = bKids[i];

        //            uF[i] = bki;

        //            //Help for fast value retrieve

        //            notEmptyPointer |= (bki != 0);
        //            //uPtrToValue[i] = bki;
        //            PtrToValue[i] = bki;

        //        }

        //        ValueIsEmpty = !notEmptyPointer;
        //        /////////////////////////////



        //        //checking other kids            
        //        int kid = 0;
        //        int kidPlace = 0;

        //        byte pb2 = 0;
        //        byte pb3 = 0;
        //        byte pb4 = 0;
        //        byte pb5 = 0;
        //        byte pb6 = 0;
        //        byte pb7 = 0;
        //        byte pb8 = 0;

        //        int step = 2 + DefaultPointerLength;
        //        int kidLen = bKids.Length;

        //        for (int j = DefaultPointerLength; j < kidLen; j += step)
        //        {

        //            //notEmptyPointer = false;                  
        //            //kid = ubKids[j] + 1;
        //            kid = bKids[j] + 1;

        //            //kid++;
        //            kidPlace = kid * shift;

        //            //DBreeze.Diagnostic.SpeedStatistic.StartCounter("ParseKids");

        //            //checking if pointer is empty
        //            switch (DefaultPointerLength)
        //            {
        //                case 5:
        //                    pb2 = bKids[j + 2];
        //                    pb3 = bKids[j + 3];
        //                    pb4 = bKids[j + 4];
        //                    pb5 = bKids[j + 5];
        //                    pb6 = bKids[j + 6];

        //                    notEmptyPointer = ((pb6 != 0) || (pb5 != 0) || (pb4 != 0) || (pb3 != 0) || (pb2 != 0));

        //                    break;
        //                case 6:
        //                    pb2 = bKids[j + 2];
        //                    pb3 = bKids[j + 3];
        //                    pb4 = bKids[j + 4];
        //                    pb5 = bKids[j + 5];
        //                    pb6 = bKids[j + 6];
        //                    pb7 = bKids[j + 7];

        //                    notEmptyPointer = ((pb7 != 0) || (pb6 != 0) || (pb5 != 0) || (pb4 != 0) || (pb3 != 0) || (pb2 != 0));

        //                    break;
        //                case 7:
        //                    pb2 = bKids[j + 2];
        //                    pb3 = bKids[j + 3];
        //                    pb4 = bKids[j + 4];
        //                    pb5 = bKids[j + 5];
        //                    pb6 = bKids[j + 6];
        //                    pb7 = bKids[j + 7];
        //                    pb8 = bKids[j + 8];

        //                    notEmptyPointer = ((pb8 != 0) || (pb7 != 0) || (pb6 != 0) || (pb5 != 0) || (pb4 != 0) || (pb3 != 0) || (pb2 != 0));

        //                    break;
        //                default:
        //                    //SLOWER THEN DIRECT COMPARE
        //                    notEmptyPointer = false;
        //                    for (int ii = 0; ii < DefaultPointerLength; ii++)
        //                    {

        //                        if ((bKids[j + 2 + ii] != 0))
        //                        {
        //                            notEmptyPointer = true;
        //                            break;
        //                        }
        //                        //notEmptyPointer |= (bKids[j + 2 + ii] != 0);
        //                    }
        //                    break;

        //            }
        //            //DBreeze.Diagnostic.SpeedStatistic.StopCounter("ParseKids");



        //            if (notEmptyPointer)
        //            {
        //                switch (DefaultPointerLength)
        //                {
        //                    case 5:
        //                        uF[kidPlace + 2] = pb2;
        //                        uF[kidPlace + 3] = pb3;
        //                        uF[kidPlace + 4] = pb4;
        //                        uF[kidPlace + 5] = pb5;
        //                        uF[kidPlace + 6] = pb6;

        //                        break;
        //                    case 6:
        //                        uF[kidPlace + 2] = pb2;
        //                        uF[kidPlace + 3] = pb3;
        //                        uF[kidPlace + 4] = pb4;
        //                        uF[kidPlace + 5] = pb5;
        //                        uF[kidPlace + 6] = pb6;
        //                        uF[kidPlace + 7] = pb7;

        //                        break;
        //                    case 7:
        //                        uF[kidPlace + 2] = pb2;
        //                        uF[kidPlace + 3] = pb3;
        //                        uF[kidPlace + 4] = pb4;
        //                        uF[kidPlace + 5] = pb5;
        //                        uF[kidPlace + 6] = pb6;
        //                        uF[kidPlace + 7] = pb7;
        //                        uF[kidPlace + 8] = pb8;

        //                        break;
        //                    default:
        //                        for (int ii = 0; ii < DefaultPointerLength; ii++)
        //                        {
        //                            uF[kidPlace + 2 + ii] = bKids[j + 2 + ii];
        //                        }
        //                        break;
        //                }

        //                if (_f[kidPlace] == 0)
        //                {
        //                    //SetupExtremums(kid);
        //                    //Handling Extremums
        //                    if (MaxKidNull || (kid > MaxKid))
        //                    {
        //                        MaxKidNull = false;
        //                        MaxKid = kid;
        //                    }
        //                    if (MinKidNull || (kid < MinKid))
        //                    {
        //                        MinKidNull = false;
        //                        MinKid = kid;
        //                    }
        //                    /*********************************/
        //                    count++;
        //                }

        //                uF[kidPlace] = 1; //Kid Exists 

        //                //if (bKids[j + 1] == 1) - //Link to the value
        //                //if (bKids[j + 1] == 0) - //Link to the Node 
        //                uF[kidPlace + 1] = bKids[j + 1];


        //            }

        //            qrs++;
        //        }

        //    }

        //    return qrs;
        //}



        //PARSE KIDS SAFE CODE

        /// <summary>
        /// Returns quantity of reservation slots
        /// </summary>
        /// <param name="bKids"></param>
        /// <returns></returns>
        public int ParseKids(ref byte[] bKids)
        {
            if (bKids != null)
            {
                //Flag becomes false, telling that this value is not new.
                AllowSave = false;
            }


            //////if ((bKids.Length / 7 * 7 + 5) != bKids.Length)
            //////{

            //////    System.Diagnostics.Debug.WriteLine("Laja");
            //////}
            //Quantity reservation slots
            int qrs = 0;


            //first checking pointer to the value
            bool notEmptyPointer = false;



            //here switch will be slower
            byte bki = 0;

            for (int i = 0; i < DefaultPointerLength; i++)
            {
                bki = bKids[i];
                //_f[i] = bKids[i];
                _f[i] = bki;

                //Help for fast value retrieve
                //notEmptyPointer |= (bKids[i] != 0);
                notEmptyPointer |= (bki != 0);
                //PtrToValue[i] = bKids[i];
                PtrToValue[i] = bki;
            }

            ValueIsEmpty = !notEmptyPointer;
            /////////////////////////////



            //checking other kids            
            int kid = 0;
            int kidPlace = 0;

            byte pb2 = 0;
            byte pb3 = 0;
            byte pb4 = 0;
            byte pb5 = 0;
            byte pb6 = 0;
            byte pb7 = 0;
            byte pb8 = 0;

            int step = 2 + DefaultPointerLength;
            int kidLen = bKids.Length;

            for (int j = DefaultPointerLength; j < kidLen; j += step)
            {

                //notEmptyPointer = false;
                kid = bKids[j] + 1;
                //kid++;
                kidPlace = kid * shift;


                //checking if pointer is empty
                switch (DefaultPointerLength)
                {
                    case 5:
                        pb2 = bKids[j + 2];
                        pb3 = bKids[j + 3];
                        pb4 = bKids[j + 4];
                        pb5 = bKids[j + 5];
                        pb6 = bKids[j + 6];

                        notEmptyPointer = ((pb6 != 0) || (pb5 != 0) || (pb4 != 0) || (pb3 != 0) || (pb2 != 0));

                        break;
                    case 6:
                        pb2 = bKids[j + 2];
                        pb3 = bKids[j + 3];
                        pb4 = bKids[j + 4];
                        pb5 = bKids[j + 5];
                        pb6 = bKids[j + 6];
                        pb7 = bKids[j + 7];

                        notEmptyPointer = ((pb7 != 0) || (pb6 != 0) || (pb5 != 0) || (pb4 != 0) || (pb3 != 0) || (pb2 != 0));

                        break;
                    case 7:
                        pb2 = bKids[j + 2];
                        pb3 = bKids[j + 3];
                        pb4 = bKids[j + 4];
                        pb5 = bKids[j + 5];
                        pb6 = bKids[j + 6];
                        pb7 = bKids[j + 7];
                        pb8 = bKids[j + 8];

                        notEmptyPointer = ((pb8 != 0) || (pb7 != 0) || (pb6 != 0) || (pb5 != 0) || (pb4 != 0) || (pb3 != 0) || (pb2 != 0));

                        break;
                    default:

                        notEmptyPointer = false;

                        for (int ii = 0; ii < DefaultPointerLength; ii++)
                        {

                            if ((bKids[j + 2 + ii] != 0))
                            {
                                notEmptyPointer = true;
                                break;
                            }
                            //notEmptyPointer |= (bKids[j + 2 + ii] != 0);
                        }
                        break;

                }


                if (notEmptyPointer)
                {
                    switch (DefaultPointerLength)
                    {
                        case 5:
                            _f[kidPlace + 2] = pb2;
                            _f[kidPlace + 3] = pb3;
                            _f[kidPlace + 4] = pb4;
                            _f[kidPlace + 5] = pb5;
                            _f[kidPlace + 6] = pb6;

                            break;
                        case 6:
                            _f[kidPlace + 2] = pb2;
                            _f[kidPlace + 3] = pb3;
                            _f[kidPlace + 4] = pb4;
                            _f[kidPlace + 5] = pb5;
                            _f[kidPlace + 6] = pb6;
                            _f[kidPlace + 7] = pb7;

                            break;
                        case 7:
                            _f[kidPlace + 2] = pb2;
                            _f[kidPlace + 3] = pb3;
                            _f[kidPlace + 4] = pb4;
                            _f[kidPlace + 5] = pb5;
                            _f[kidPlace + 6] = pb6;
                            _f[kidPlace + 7] = pb7;
                            _f[kidPlace + 8] = pb8;

                            break;
                        default:
                            for (int ii = 0; ii < DefaultPointerLength; ii++)
                            {
                                _f[kidPlace + 2 + ii] = bKids[j + 2 + ii];
                            }
                            break;
                    }

                    if (_f[kidPlace] == 0)
                    {
                        //Handling Extremums
                        if (MaxKidNull || (kid > MaxKid))
                        {
                            MaxKidNull = false;
                            MaxKid = kid;
                        }
                        if (MinKidNull || (kid < MinKid))
                        {
                            MinKidNull = false;
                            MinKid = kid;
                        }
                        /*********************************/
                        count++;
                    }

                    _f[kidPlace] = 1; //Kid Exists 

                    //if (bKids[j + 1] == 1) - //Link to the value
                    //if (bKids[j + 1] == 0) - //Link to the Node 
                    _f[kidPlace + 1] = bKids[j + 1];


                }

                qrs++;
            }



            return qrs;
        }



        public void AddKid(int kid, byte[] ptr)
        {
            /*
             * Kids can be 0 - value, then 1-256 Kids corresponding to bytes [0-255]
             * also after byte definition we specify with 0 or 1 if the value is a link to the next node or link to the value.
             * Value is stored in format TOTALLEN,LEN(FULLVALUEOFTHEKEY),FULLVALUEOFTHEKEY,LEN(Value),Value
             */

            AllowSave = true;

            if (kid == 256) //value
            {
                //Settign up value element for this node
                bool notEmptyPointer = false;

                for (int i = 0; i < ptr.Length; i++)                    //Copy Into can be used
                {

                    _f[i] = ptr[i];

                    //Help for fast value retrieve
                    notEmptyPointer |= (ptr[i] != 0);
                    PtrToValue[i] = ptr[i];
                    //-------------------------------
                }


                ValueIsEmpty = !notEmptyPointer;
            }
            else
            {
                //Setting up kid
                //It's balanced on upper level, but here we save only pointer to the value, and this kid will definetely has not busy place,
                //so

                kid++;  //bringing kid to the real value
                int kidPlace = kid * shift;

                //Increasing count
                if (_f[kidPlace] == 0)
                {
                    //SetupExtremums(kid);
                    //Handling Extremums
                    if (MaxKidNull || (kid > MaxKid))
                    {
                        MaxKidNull = false;
                        MaxKid = kid;
                    }
                    if (MinKidNull || (kid < MinKid))
                    {
                        MinKidNull = false;
                        MinKid = kid;
                    }
                    /*********************************/
                    count++;


                }


                _f[kidPlace] = 1; //Kid Exists 
                _f[kidPlace + 1] = 1; //Link to the value

                //ptr.Length or Default Pointer Length must be the same
                for (int i = 0; i < ptr.Length; i++)                        //Can be changed on substring (For faster then block copy)
                {


                    _f[(kidPlace + 2) + i] = ptr[i];  //Settign up pointer
                }

                //slower then for
                //Buffer.BlockCopy(ptr, 0, _f, (kidPlace + 2), ptr.Length);
            }

            return;
        }

        public void RemoveValueKid()
        {
            AllowSave = true;

            for (int i = 0; i < DefaultPointerLength; i++)
            {
                _f[i] = 0;

                //Help for fast value retrieve
                PtrToValue[i] = 0;
                //-------------------------------
            }


            ValueIsEmpty = true;
        }

        public void RemoveAllKids()
        {
            AllowSave = true;

            _f = new byte[DefaultPointerLength + 2 + (256 * shift)];

            NeedToSearchExtremums = false;
            MaxKidNull = true;
            MinKidNull = true;

        }

        public void RemoveKid(int kid)
        {
            AllowSave = true;

            kid++;//bringing kid to the real value
            int kidPlace = kid * shift;

            if ((_f[kidPlace] == 1))    //Kid existed
            {
                count--;

                _f[kidPlace] = 0;

                NeedToSearchExtremums = true;
                SearchExtremums();
            }

        }

        private void SearchExtremums()
        {
            if (!NeedToSearchExtremums)
                return;

            NeedToSearchExtremums = false;
            FindNewMax();
            FindNewMin();
        }

        private void FindNewMax()
        {
            //Iterating backward to find new Max                    

            for (int i = 256; i >= 1; i--)      //i>0
            {
                if (_f[i * shift] == 1)
                {
                    //setting up new Maximum
                    MaxKid = i;
                    return;
                }
            }

            MaxKidNull = true;
        }

        private void FindNewMin()
        {
            //Iterating forward to find new Max                    

            for (int i = 1; i <= 256; i++)      //i<257
            {
                if (_f[i * shift] == 1)
                {
                    //setting up new Maximum
                    MinKid = i;
                    return;
                }
            }

            MinKidNull = true;
        }


        public LTrieKid GetMinKid()
        {
            LTrieKid ret = new LTrieKid();

            if (!ValueIsEmpty)
            {
                //In case if we have value then it's a minimum

                ret.Ptr = PtrToValue;
                ret.Exists = true;
                ret.ValueKid = true;
                ret.Val = 256;
            }
            else
            {
                SearchExtremums();

                if (MinKidNull)
                    return ret;

                int kidPlace = MinKid * shift;


                ret.Exists = true;
                ret.Val = MinKid - 1;  //Bringing value to [0-255]

                //Copying Pointer
                ret.Ptr = new byte[DefaultPointerLength];

                for (int i = 0; i < DefaultPointerLength; i++)          //Can be changed on substring
                {
                    ret.Ptr[i] = _f[(kidPlace + 2) + i];
                }

                //Setting up link to Node or to Value
                if (_f[kidPlace + 1] == 1)
                    ret.LinkToNode = false;

            }

            return ret;
        }

        public LTrieKid GetMaxKid()
        {
            LTrieKid ret = new LTrieKid();

            SearchExtremums();

            if (MaxKidNull)
            {
                if (!ValueIsEmpty)
                {
                    //trying to take max kid
                    ret.Ptr = PtrToValue;
                    ret.Exists = true;
                    ret.ValueKid = true;
                    ret.Val = 256;
                }

                return ret;
            }

            int kidPlace = MaxKid * shift;
            ret.Exists = true;
            ret.Val = MaxKid - 1;  //Bringing value to [0-255]

            //Copying Pointer
            ret.Ptr = new byte[DefaultPointerLength];
            for (int i = 0; i < DefaultPointerLength; i++)          //Can be changed on substring
            {
                ret.Ptr[i] = _f[(kidPlace + 2) + i];
            }

            //Setting up link to Node or to Value
            if (_f[kidPlace + 1] == 1)
                ret.LinkToNode = false;

            return ret;
        }

        //public LTrieKid GetKidBiggerThen(int kid)
        //{
        //    LTrieKid ret = new LTrieKid();

        //    kid += 2;

        //    if (kid > 256)
        //        return ret;

        //    int kidPlace = 0;

        //    for (int i = kid; i <= 256; i++) //i<257
        //    {
        //        kidPlace = i * shift;
        //        if (_f[kidPlace] == 1)
        //        {
        //            ret.Exists = true;
        //            ret.Val = i - 1;

        //            ret.Ptr = new byte[DefaultPointerLength];
        //            for (int j = 0; j < DefaultPointerLength; j++)          //Can be changed on substring
        //            {
        //                ret.Ptr[j] = _f[(kidPlace + 2) + j];
        //            }

        //            //Setting up link to Node or to Value
        //            if (_f[kidPlace + 1] == 1)
        //                ret.LinkToNode = false;

        //            return ret;
        //        }
        //    }

        //    return ret;
        //}

        public IEnumerable<LTrieKid> GetKidsForward(int startFrom)
        {
            LTrieKid ret = null;

            //USE it later if startFrom = 256
            if (startFrom == 256)
            {
                if (!ValueIsEmpty)
                {
                    ret = new LTrieKid();

                    //trying to take max kid
                    ret.Ptr = PtrToValue;
                    ret.Exists = true;
                    ret.ValueKid = true;
                    ret.Val = 256;

                    yield return ret;
                }

                startFrom = 0;
            }

            if (this.count > 0)
            {

                //Change back kid to normal
                startFrom += 1;


                //if (startFrom == 256)
                //    startFrom = 1;
                //else
                //    startFrom += 1;

                int kidPlace = 0;
                for (int i = startFrom; i <= 256; i++)
                {
                    kidPlace = i * shift;

                    if (_f[kidPlace] == 1)
                    {
                        ret = new LTrieKid();
                        ret.Exists = true;
                        ret.Val = i - 1;

                        ret.Ptr = new byte[DefaultPointerLength];
                        for (int j = 0; j < DefaultPointerLength; j++)          //Can be changed on substring
                        {
                            ret.Ptr[j] = _f[(kidPlace + 2) + j];
                        }

                        //Setting up link to Node or to Value
                        if (_f[kidPlace + 1] == 1)
                            ret.LinkToNode = false;

                        yield return ret;
                    }
                }
            }
        }


        public IEnumerable<LTrieKid> GetKidsForward()
        {
            LTrieKid ret = null;

            if (!ValueIsEmpty)
            {
                ret = new LTrieKid();

                //trying to take max kid
                ret.Ptr = PtrToValue;
                ret.Exists = true;
                ret.ValueKid = true;
                ret.Val = 256;

                yield return ret;
            }

            if (this.count > 0)
            {
                int kidPlace = 0;
                for (int i = 1; i <= 256; i++)
                {
                    kidPlace = i * shift;

                    if (_f[kidPlace] == 1)
                    {
                        ret = new LTrieKid();
                        ret.Exists = true;
                        ret.Val = i - 1;

                        ret.Ptr = new byte[DefaultPointerLength];
                        for (int j = 0; j < DefaultPointerLength; j++)          //Can be changed on substring
                        {
                            ret.Ptr[j] = _f[(kidPlace + 2) + j];
                        }

                        //Setting up link to Node or to Value
                        if (_f[kidPlace + 1] == 1)
                            ret.LinkToNode = false;

                        yield return ret;
                    }
                }
            }

        }


        public IEnumerable<LTrieKid> GetKidsBackward(int startFrom)
        {
            LTrieKid ret = null;

            if (this.count > 0 && startFrom != 256)
            {
                startFrom += 1;

                int kidPlace = 0;
                for (int i = startFrom; i > 0; i--)
                {
                    kidPlace = i * shift;

                    if (_f[kidPlace] == 1)
                    {
                        ret = new LTrieKid();
                        ret.Exists = true;
                        ret.Val = i - 1;

                        ret.Ptr = new byte[DefaultPointerLength];
                        for (int j = 0; j < DefaultPointerLength; j++)          //Can be changed on substring
                        {
                            ret.Ptr[j] = _f[(kidPlace + 2) + j];
                        }

                        //Setting up link to Node or to Value
                        if (_f[kidPlace + 1] == 1)
                            ret.LinkToNode = false;

                        yield return ret;
                    }
                }
            }

            if (!ValueIsEmpty)
            {
                ret = new LTrieKid();

                //trying to take max kid
                ret.Ptr = PtrToValue;
                ret.Exists = true;
                ret.ValueKid = true;
                ret.Val = 256;

                yield return ret;
            }
        }


        public IEnumerable<LTrieKid> GetKidsBackward()
        {
            LTrieKid ret = null;

            if (this.count > 0)
            {
                int kidPlace = 0;
                for (int i = 256; i > 0; i--)
                {
                    kidPlace = i * shift;

                    if (_f[kidPlace] == 1)
                    {
                        ret = new LTrieKid();
                        ret.Exists = true;
                        ret.Val = i - 1;

                        ret.Ptr = new byte[DefaultPointerLength];
                        for (int j = 0; j < DefaultPointerLength; j++)          //Can be changed on substring
                        {
                            ret.Ptr[j] = _f[(kidPlace + 2) + j];
                        }

                        //Setting up link to Node or to Value
                        if (_f[kidPlace + 1] == 1)
                            ret.LinkToNode = false;

                        yield return ret;
                    }
                }
            }

            if (!ValueIsEmpty)
            {
                ret = new LTrieKid();

                //trying to take max kid
                ret.Ptr = PtrToValue;
                ret.Exists = true;
                ret.ValueKid = true;
                ret.Val = 256;

                yield return ret;
            }
        }



        //public LTrieKid GetKidSmallerThen(int kid)
        //{
        //    LTrieKid ret = new LTrieKid();

        //    if (kid == 0)
        //    {
        //        if (!ValueIsEmpty)
        //        {
        //            //trying to take max kid
        //            ret.Ptr = PtrToValue;
        //            ret.Exists = true;
        //            ret.ValueKid = true;
        //            ret.Val = 256;
        //            return ret;
        //        }

        //        return ret;
        //    }


        //    int kidPlace = 0;

        //    //for (int i = kid; i >= 0; i--)
        //    for (int i = kid; i > 0; i--)
        //    {
        //        kidPlace = i * shift;

        //        if (_f[kidPlace] == 1)
        //        {
        //            ret.Exists = true;
        //            ret.Val = i - 1;

        //            ret.Ptr = new byte[DefaultPointerLength];
        //            for (int j = 0; j < DefaultPointerLength; j++)          //Can be changed on substring
        //            {
        //                ret.Ptr[j] = _f[(kidPlace + 2) + j];
        //            }

        //            //Setting up link to Node or to Value
        //            if (_f[kidPlace + 1] == 1)
        //                ret.LinkToNode = false;

        //            return ret;
        //        }
        //    }

        //    if (!ValueIsEmpty)
        //    {
        //        //trying to take max kid
        //        ret.Ptr = PtrToValue;
        //        ret.Exists = true;
        //        ret.ValueKid = true;
        //        ret.Val = 256;
        //        return ret;
        //    }

        //    return ret;
        //}




        public byte[] ReplaceValueLinkOnKidLink(int kid)
        {
            //Here we appear only in case if we want to setup kid on the busy place. This busy place can be resided by link to the other kid or link to the value.
            //If it's busy by link to the kid we return null, if by link to the value then we clean flag "Link to the value" an return back this pointer.
            AllowSave = true;

            kid++;  //bringing kid to the real value
            int kidPlace = kid * shift;


            if (_f[kidPlace + 1] == 1)
            {
                //We got link to the value
                _f[kidPlace + 1] = 0;   //We change it on link to the next node kid and then return reference on the previous value

                byte[] ptr = new byte[DefaultPointerLength];

                for (int i = 0; i < DefaultPointerLength; i++)          //Can be changed on substring
                {
                    ptr[i] = _f[(kidPlace + 2) + i];

                    //Cleaning up link, so it is empty and will be setup when we save generation node
                    _f[(kidPlace + 2) + i] = 0;
                }

                return ptr;
            }

            return null;

        }

        public bool ContainsKid(int kid)
        {
            //necessary shift due to value

            return (_f[(kid + 1) * shift] == 1);
        }

        public int Count()
        {
            return count;
        }

        /// <summary>
        /// Gets Value kid (before 0-255)
        /// </summary>
        /// <returns></returns>
        public LTrieKid GetKidValue()
        {
            LTrieKid kidDef = new LTrieKid();
            kidDef.ValueKid = true;

            if (ValueIsEmpty)
                return kidDef;

            kidDef.Exists = true;
            kidDef.Val = 256;

            kidDef.Ptr = PtrToValue;
            return kidDef;
        }


        public LTrieKid GetKid(int kid)
        {
            LTrieKid kidDef = new LTrieKid();

            //Check if it's used somewhere
            kidDef.Val = kid;

            kid++;  //bringing kid to the real value
            int kidPlace = kid * shift;

            if (_f[kidPlace] == 1)
            {
                kidDef.Exists = true;

                if (_f[kidPlace + 1] == 1)
                    kidDef.LinkToNode = false;          //Link to Value

                kidDef.Ptr = new byte[DefaultPointerLength];

                Buffer.BlockCopy(_f, (kidPlace + 2), kidDef.Ptr, 0, DefaultPointerLength);

                //for (int i = 0; i < DefaultPointerLength; i++)          //Can be changed on substring
                //{
                //    kidDef.Ptr[i] = _f[(kidPlace + 2) + i];
                //}

            }

            return kidDef;
        }

        public byte[] GetPointerToTheKid(int kid)
        {
            //Returns null if Kid doesn't exists or contains link to the value
            //Returns Ptr to the next node otherwise

            kid++;  //bringing kid to the real value
            int kidPlace = kid * shift;

            if (_f[kidPlace] == 1 && _f[kidPlace + 1] == 0)
            {
                byte[] ptr = new byte[DefaultPointerLength];

                Buffer.BlockCopy(_f, (kidPlace + 2), ptr, 0, DefaultPointerLength);

                //for (int i = 0; i < DefaultPointerLength; i++)          //Can be changed on substring
                //{
                //    ptr[i] = _f[(kidPlace + 2) + i];
                //}

                return ptr;
            }

            return null;
        }

        /// <summary>
        /// Returns null, if not necessary to save generation node (cause it didn't change).  AllowSave = false;
        /// </summary>
        /// <param name="reservation"></param>
        /// <returns></returns>
        public byte[] GetKidsForSave(int reservation)
        {
            if (!AllowSave)
                return null;
            //DBreeze.Diagnostic.SpeedStatistic.StartCounter("b");


            byte[] ret = new byte[DefaultPointerLength + 2 + (256 * shift)];
            int realcnt = DefaultPointerLength;

            Buffer.BlockCopy(_f, 0, ret, 0, DefaultPointerLength);

            int kidPlace = 0;

            for (int i = 1; i <= 256; i++)
            {
                kidPlace = i * shift;
                if (_f[kidPlace] == 1)
                {
                    ret[realcnt] = (byte)(i - 1);
                    ret[realcnt + 1] = _f[kidPlace + 1];
                    Buffer.BlockCopy(_f, kidPlace + 2, ret, realcnt + 2, DefaultPointerLength);
                    realcnt += 2 + DefaultPointerLength;
                }
            }

            int toAdd = reservation - this.count;
            byte[] ret1 = null;

            if (toAdd > 0)
            {
                int addSize = (2 + DefaultPointerLength) * toAdd;
                ret1 = new byte[realcnt + addSize];
                Buffer.BlockCopy(new byte[addSize], 0, ret1, realcnt, addSize);
                Buffer.BlockCopy(ret, 0, ret1, 0, realcnt);
            }
            else
            {
                ret1 = new byte[realcnt];
                Buffer.BlockCopy(ret, 0, ret1, 0, realcnt);
            }

            //DBreeze.Diagnostic.SpeedStatistic.StopCounter("b");

            //////if (ret1.Length != (ret1.Length / 7 * 7 + 5))
            //////{
            //////    System.Diagnostics.Debug.WriteLine(ret1.Count());
            //////}
            //System.Diagnostics.Debug.WriteLine(ret1.Count());
            return ret1;
        }



        //public byte[] GetKidsForSave(int reservation)
        //{
        //    DBreeze.Diagnostic.SpeedStatistic.StartCounter("b");

        //    byte[] ret = null;



        //    ret = ret.Concat(_f.Substring(0, DefaultPointerLength));

        //    int kidPlace = 0;

        //    for (int i = 1; i <= 256; i++)
        //    {
        //        kidPlace = i * shift;

        //        if (_f[kidPlace] == 1)
        //        {
        //            ret = ret.ConcatMany(
        //                new byte[] { (byte)(i - 1) },            //Identifier of the kid 0-255
        //                new byte[] { _f[kidPlace + 1] },          //Identifier if link to node or link to value
        //                _f.Substring(kidPlace + 2, DefaultPointerLength)   //Pointer itself
        //                );

        //        }
        //    }

        //    int toAdd = reservation - this.count;
        //    if (toAdd > 0)
        //        ret = ret.Concat(new byte[(2 + DefaultPointerLength) * toAdd]);         //2 because: 1 for kid name[0-255], 2 for id link to node or to value

        //    DBreeze.Diagnostic.SpeedStatistic.StopCounter("b");

        //    return ret;
        //}

    }
}
