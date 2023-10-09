// <copyright file="Graph.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System;
using System.Runtime.Serialization;
using System.Threading;

namespace DBreeze.HNSW
{
    [Serializable]
    internal class GraphChangedException : Exception
    {
        public GraphChangedException()
        {
        }

        public GraphChangedException(string message) : base(message)
        {
        }

        public GraphChangedException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected GraphChangedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        internal static void ThrowIfChanged(ref long version, long versionAtStart)
        {
            if (Interlocked.Read(ref version) != versionAtStart)
            {
                throw new GraphChangedException();
            }
        }
    }
}