// <copyright file="DistanceUtils.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

#if NET6FUNC
namespace DBreeze.HNSW
{
    using System;

    internal static class DistanceUtils
    {
        public static bool LowerThan<TDistance>(TDistance x, TDistance y) where TDistance : IComparable<TDistance>
        {
            return x.CompareTo(y) < 0;
        }

        public static bool GreaterThan<TDistance>(TDistance x, TDistance y) where TDistance : IComparable<TDistance>
        {
            return x.CompareTo(y) > 0;
        }

        public static bool IsEqual<TDistance>(TDistance x, TDistance y) where TDistance : IComparable<TDistance>
        {
            return x.CompareTo(y) == 0;
        }
    }
}
#endif