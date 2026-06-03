/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Oleksiy Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
  
  TurboQuant integration: online vector quantization with near-optimal distortion rate.
  Based on: "TurboQuant: Online Vector Quantization with Near-optimal Distortion Rate" 
  by Zandieh, Daliri, Hadian, Mirrokni (Google Research, 2025)
*/
#if NET6FUNC || NET472
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBreeze.HNSW
{
    /// <summary>
    /// TurboQuant quantizer type
    /// </summary>
    public enum eTurboQuantMode
    {
        /// <summary>
        /// No quantization enabled (default, backward compatible)
        /// </summary>
        None = 0,
        /// <summary>
        /// MSE-optimal quantizer. Minimizes L2 reconstruction error.
        /// Uses random rotation + per-coordinate Lloyd-Max scalar quantization.
        /// Best for storage efficiency and general-purpose compression.
        /// </summary>
        MSE = 1,
        /// <summary>
        /// Inner-product unbiased quantizer.
        /// Two-stage: MSE quantizer at (b-1) bits + QJL 1-bit on residual.
        /// Provides unbiased inner product estimates, ideal for KNN search.
        /// </summary>
        InnerProduct = 2
    }

    /// <summary>
    /// TurboQuant configuration parameters.
    /// When BitWidth = 0, quantization is disabled (full precision).
    /// </summary>
    public class TurboQuantParams
    {
        /// <summary>
        /// Number of bits per coordinate. 
        /// 0 = disabled (full precision, backward compatible).
        /// 1..8 = enabled. Recommended: 2-4 for good quality, 4 for near-lossless.
        /// </summary>
        public int BitWidth = 4;

        /// <summary>
        /// Quantizer mode: MSE or InnerProduct unbiased.
        /// Ignored when BitWidth = 0.
        /// </summary>
        public eTurboQuantMode Mode = eTurboQuantMode.None;

        /// <summary>
        /// Seed for deterministic generation of rotation matrix Π and QJL matrix S.
        /// Combined with table name hash to produce unique per-table matrices.
        /// Default = 42.
        /// </summary>
        public int RandomSeed = 42;

        ///// <summary>
        ///// Number of outlier channels (top-k by magnitude) treated with higher precision.
        ///// 0 = no outlier handling. Used for fractional bit-width setups (e.g. 2.5-bit).
        ///// Default = 0.
        ///// </summary>
        //public int OutlierChannels = 0;

        ///// <summary>
        ///// Bit-width for outlier channels (only used when OutlierChannels > 0).
        ///// Must be greater than BitWidth. Default = 4.
        ///// </summary>
        //public int OutlierBitWidth = 4;

        /// <summary>
        /// Returns true when quantization is enabled.
        /// </summary>
        public bool IsEnabled => this.Mode != eTurboQuantMode.None && BitWidth > 0;

        ///// <summary>
        ///// Returns the effective per-coordinate bit-width (including outlier channels if configured).
        ///// For non-outlier mode, equals BitWidth.
        ///// </summary>
        //public double EffectiveBitWidth
        //{
        //    get
        //    {
        //        if (BitWidth <= 0) return 0;
        //        if (OutlierChannels <= 0) return BitWidth;
        //        // Fractional bit-width calculation needs dimension, handled dynamically where needed
        //        return 0;
        //    }
        //}

        /// <summary>
        /// Creates a clone of these parameters.
        /// </summary>
        public TurboQuantParams Clone()
        {
            return new TurboQuantParams
            {
                BitWidth = this.BitWidth,
                Mode = this.Mode,
                RandomSeed = this.RandomSeed,
                //OutlierChannels = this.OutlierChannels,
                //OutlierBitWidth = this.OutlierBitWidth
            };
        }
    }
}
#endif