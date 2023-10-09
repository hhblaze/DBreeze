// <copyright file="ScopeLatencyTracker.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

#if KNNSearch
namespace DBreeze.HNSW
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// Latency tracker for using scope.
    /// TODO: make it ref struct in C# 8.0
    /// </summary>
    internal struct ScopeLatencyTracker : IDisposable
    {
        private long StartTimestamp;
        private Action<float> LatencyCallback;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScopeLatencyTracker"/> struct.
        /// </summary>
        /// <param name="callback">The latency reporting callback to associate with the scope.</param>
        internal ScopeLatencyTracker(Action<float> callback)
        {
            StartTimestamp = callback != null ? Stopwatch.GetTimestamp() : 0;
            LatencyCallback = callback;
        }

        /// <summary>
        /// Reports the time ellsapsed between the tracker creation and this call.
        /// </summary>
        public void Dispose()
        {
            const long ticksPerMicroSecond = TimeSpan.TicksPerMillisecond / 1000;
            if (LatencyCallback != null)
            {
                long ellapsedMuS = (Stopwatch.GetTimestamp() - StartTimestamp) / ticksPerMicroSecond;
                LatencyCallback(ellapsedMuS);
            }
        }
    }
}
#endif