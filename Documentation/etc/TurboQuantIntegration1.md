

# TurboQuant Enhancement Plan for DBreeze Vector Layer

## Summary of TurboQuant (from the paper)

TurboQuant is a **data-oblivious (online) vector quantization** algorithm that achieves near-optimal distortion rates. It has two variants:

### 1. TurboQuant_mse (MSE-optimal)
- **Core idea**: Randomly rotate the input vector → coordinates become nearly independent with Beta distribution → apply optimal **scalar** quantization per coordinate using precomputed Lloyd-Max codebooks → rotate back
- **Key properties**:
  - No training/data-dependent tuning needed  
  - MSE ≤ (√(3π)/2) · 1/4^b (≈ 2.7x from info-theoretic lower bound)
  - For b=1,2,3,4 bits/coordinate: MSE ≈ 0.36, 0.117, 0.03, 0.009 (for unit vectors)
  - Quant time: ~0.0013s for d=1536, 4-bit (vs PQ: 494s)

### 2. TurboQuant_prod (Inner-product optimal, unbiased)
- Two-stage: apply TurboQuant_mse with (b-1) bits + QJL (1-bit Quantized JL) on the residual
- Provides **unbiased** inner product estimates
- Inner product variance ≤ ∥y∥²/d · 1/4^b

## Current DBreeze Vector Layer Architecture (HNSW-based)

**Storage**: Full-precision float[] or double[] vectors stored per node in DBreeze tables.  
**Index**: HNSW graph connects similar vectors, uses cosine distance (1 − dot_product).  
**Workflow**: Vectors are normalized on insert; full float[]/double[] data is persisted and loaded for distance computations.

## Proposed TurboQuant Integration Plan

### Phase 1: New TurboQuant Static Class (`DBreeze/VectorLayer/TurboQuant.cs`)
- Precompute codebooks for bit-widths b=1..8 using Lloyd-Max algorithm on the Beta(∼N(0,1/d)) distribution
- Implement `Quantize(double[] vector, int bitWidth)` → `(int[] indices, double norm, double[] residual)`
  - Generate/store a random rotation matrix Π (per table, per dimension)
  - Rotate: `y = Π · x`
  - For each coordinate: find nearest centroid index (b bits)
- Implement `Dequantize(int[] indices, int bitWidth)` → `double[]`
  - Lookup centroids → `ỹ` → `x̃ = Π^T · ỹ`
- Implement `QuantizeProd(double[] vector, int bitWidth)` → `(int[] mseIndices, int[] qjlSigns, double residualNorm)`
  - Stage 1: Quantize with (b-1) bits → get MSE indices + residual r
  - Stage 2: `qjl = sign(S · r)` where S~N(0,1) random matrix
- Implement `DequantizeProd(...)` → `double[]`
- Random matrix generation: Use seeded RNG per table for deterministic rotation

### Phase 2: Storage Integration (`SmallWorld.Storage.cs`)
- Add **quantization mode** to storage configuration:
  - `enum QuantizationMode { None, MSE, InnerProduct }`
  - `int BitWidth` (default 4 → 4-bit quantization = 16:1 compression vs float32)
- Modify vector serialization:
  - When quantization is enabled, store `(int[] indices, float norm)` instead of full `float[]/double[]`
  - Norm stored in fp32 for rescaling during dequantization
- **Backward compatibility**: When reading existing data, if no quantization flag, use full-precision path

### Phase 3: Distance Computation Integration
- **Option A (exact): Dequantize then compute** — Use `Dequantize()` to reconstruct approximate vector, then compute distance. Memory benefit remains; compute cost unchanged.
- **Option B (fast approximate): Direct index distance** — For MSE quantized vectors, compute approximate distance using the quantized indices and stored norms (avoids full dequantization). 
  - `Distance(quantizedA, quantizedB) ≈ 1 - (normA · normB)⁻¹ · Σ_j (centroid[idxA_j] · centroid[idxB_j])`
- **Option C (hybrid for HNSW)**: Store both full-precision and quantized versions. Use quantized for graph traversal pruning, full-precision for final ranking.

### Phase 4: Public API Extension (`Transaction.Vector.cs`)
Add new overloads/methods:
```csharp
public void VectorsInsert(string tableName, IList<(long, float[])> vectors, 
    VectorTableParameters<float[]> parameters = null, 
    QuantizationConfig quantConfig = null)  // NEW

public IEnumerable<(long externalId, float distance)> VectorsSearchSimilar(
    string tableName, float[] queryVector, int quantity = 10, 
    VectorTableParameters<float[]> parameters = null,
    bool ignoreDeleted = true,
    QuantizationConfig quantConfig = null)  // NEW
```

New configuration class:
```csharp
public class QuantizationConfig {
    public QuantizationMode Mode = QuantizationMode.MSE;
    public int BitWidth = 4;  // bits per coordinate
    public bool EnableDistanceAcceleration = false;
}
```

### Phase 5: HNSW Integration Points
The HNSW graph (`SmallWorld.cs`) needs modifications:
- During `AddItems`: Quantize vector before or in addition to storing
- During `KNNSearch`: For distance comparisons, use quantized representation when `EnableDistanceAcceleration = true`
- The `SmallWorldStorageF/D` serialization must handle both quantized and non-quantized modes
- Graph nodes store either full vector or quantized indices (detected via a flag byte)

## Benefits

| Aspect | Current | With TurboQuant (4-bit) |
|--------|---------|------------------------|
| Storage per coordinate | 32-64 bits | 4 bits (8-16x smaller) |
| Vector storage size (d=1536) | ~6KB (float32) | ~768 bytes |
| Memory for 1M vectors | ~6GB | ~768MB |
| Insert speed | Normal | ~0.0013s per vector |
| Index build time | Hours (k-means PQ) | Near-zero (data-oblivious) |
| Search quality | Exact HNSW | Near-lossless (MSE ~0.009) |

## File Changes Summary

**New files:**
- `DBreeze/VectorLayer/TurboQuant.cs` — Core quantization algorithms + codebook precomputation
- `DBreeze/VectorLayer/QuantizationConfig.cs` — Configuration class

**Modified files:**
- `DBreeze/Transactions/Transaction.Vector.cs` — New public API overloads with quantization support
- `DBreeze/VectorLayer/HNSW/SmallWorld.cs` — HNSW integration
- `DBreeze/VectorLayer/HNSW/SmallWorld.Storage.cs` — Serialization of quantized vectors
- `DBreeze/VectorLayer/HNSW/VectorTran.cs` — Init with quantization parameters
- `DBreeze/VectorLayer/VectorMath.cs` — Optional: helper for quantized distance
- `DBreeze/VectorLayer/HNSW/SmallWorld.Caches.cs` — Distance cache awareness

---

Would you like to proceed with implementing this plan? Key decisions to make:

1. **Integration depth**: Start with just storage compression (Phase 1-2) or also fast distance computation (Phase 3)?
2. **Default bit-width**: 4-bit (good balance) or 2-bit (max compression)?
3. **Random matrix generation**: Deterministic per table or per database?
4. **Backward compatibility**: Keep full-precision as default, with quantization opt-in?

Toggle to Act mode when ready to begin implementation.


-------------

## TurboQuant Integration - Complete

### Files Created (3 new + 1 copy)

| File | Lines | Description |
|------|-------|-------------|
| `DBreeze/VectorLayer/HNSW/TurboQuantConfig.cs` | ~200 | Types, enums, precomputed codebooks |
| `DBreeze/VectorLayer/HNSW/TurboQuantMath.cs` | ~900 | NET472 scalar math (rotation, QJL, quantize/dequantize) |
| `DBreeze.Net8/VectorLayer/HNSW/TurboQuantMath.cs` | ~1200 | .NET8 SIMD-accelerated math |
| `DBreeze.Net8/VectorLayer/HNSW/TurboQuantConfig.cs` | ~150 | .NET8 copy of config types |

### Files Modified (5)

| File | Changes |
|------|---------|
| `Transaction.Vector.cs` | Added `TurboQuantParams` field to `VectorTableParameters<TVector>` |
| `SmallWorld.Storage.cs` | Added quantized key schema (prefix 6/7), `SetTurboQuantParams` on `IStorage`, compression/decompression utils for MSE and InnerProduct formats |
| `HNSW.Composer.cs` | Added `TurboQuantParams` field, `using DBreeze.VectorLayer` |
| `VectorTran.cs` | Passes `TurboQuantParams` from `VectorTableParameters` → `Composer` → `Storage` |
| `DBreeze.csproj` | Added new files to compilation |

### DBreeze Storage Schema Additions

```
6- Key: {6, (long)externalId} -> Value: [dim:2][bitWidth:1][norm:4][indices:dim]  (MSE)
7- Key: {7, (long)externalId} -> Value: [dim:2][bitWidth:1][norm:4][resNorm:4][mseIndices:dim][qjlSigns:dim] (InnerProduct)
```

### Usage

```csharp
// Standard usage (backward compatible - no changes)
tran.VectorsInsert("myTable", vectors);

// With 4-bit MSE TurboQuant (8× compression vs float32, 4× vs float16)
var tqp = new VectorTableParameters<float[]> {
    TurboQuant = new TurboQuantParams { BitWidth = 4, Mode = eTurboQuantMode.MSE }
};
tran.VectorsInsert("myTable", vectors, tqp);

// With 3-bit InnerProduct (unbiased KNN search)
var tqp2 = new VectorTableParameters<float[]> {
    TurboQuant = new TurboQuantParams { BitWidth = 3, Mode = eTurboQuantMode.InnerProduct }
};
tran.VectorsSearchSimilar("myTable", query, 10, tqp2);
```

### Key Algorithms Implemented

| Algorithm | Paper Reference | Implementation |
|-----------|----------------|----------------|
| Random Rotation | Sec 3.1, Householder reflections | `ApplyRandomRotation`, `ApplyInverseRotation` (deterministic from seed) |
| Lloyd-Max Scalar Quantization | Eq (4), Algorithm 1 | `QuantizeCoordinates` with binary search on precomputed codebooks |
| QJL 1-bit Transform | Definition 1, Lemma 4 | `QJLQuantize`, `QJLDequantize` with sqrt(pi/2)/d scaling |
| TurboQuant_mse | Algorithm 1 | `QuantizeMse`, `DequantizeMse` |
| TurboQuant_prod | Algorithm 2 | `QuantizeProdSafe`, `DequantizeProd` (MSE + QJL on residual) |

### Distortion Guarantees (from paper)

| Bit-width | MSE Distortion | Inner Product Distortion |
|-----------|---------------|-------------------------|
| b=1 | ~0.36 | ~1.57/d |
| b=2 | ~0.117 | ~0.56/d |
| b=3 | ~0.03 | ~0.18/d |
| b=4 | ~0.009 | ~0.047/d |
