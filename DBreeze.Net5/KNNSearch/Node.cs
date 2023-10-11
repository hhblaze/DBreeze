// <copyright file="Node.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

#if NET6FUNC
namespace DBreeze.HNSW
{
    using DBreeze.DataTypes;
    using DBreeze.Utils;
    //using MessagePack.Resolvers;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Reflection.Emit;
    using System.Runtime.CompilerServices;
    using System.Runtime.ConstrainedExecution;
    using System.Security;
   // using System.Threading.Tasks.Sources;
    using System.Transactions;
    using System.Xml.Linq;

    /// <summary>
    /// The implementation of the node in hnsw graph.
    /// </summary>   
    internal struct Node
    {

        /// <summary>
        /// First List is a LayerID, Second list Connections with otherNodeIDs
        /// </summary>      
        public List<List<int>> Connections;

        public int Id;

        /// <summary>
        /// Gets the max layer where the node is presented.
        /// </summary>
       
        public int MaxLayer
        {
            get
            {
                return Connections.Count - 1;
            }
        }

        /// <summary>
        /// Gets connections ids of the node at the given layer
        /// </summary>
        /// <param name="layer">The layer to get connections at.</param>
        /// <returns>The connections of the node at the given layer.</returns>
        public List<int> this[int layer]
        {
            get
            {
                return Connections[layer];
            }
            set
            {
                Connections[layer] = value;
            }
        }
    }


   

}//eon
#endif