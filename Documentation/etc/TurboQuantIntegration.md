Documentation/etc/TurboQuant.pdf · lines 2001-2762‎

DBreeze/VectorLayer/‎

DBreeze/VectorLayer/Vectors.cs · lines 1-358‎

DBreeze/VectorLayer/Node.cs · lines 1-150‎

DBreeze/Transactions/Transaction.Vector.cs · lines 1-463‎

DBreeze/VectorLayer/HNSW/VectorTran.cs · lines 1-133‎

DBreeze/VectorLayer/VectorMath.cs · lines 1-106‎







\## TurboQuant Integration - Complete



\### Files Created (3 new + 1 copy)



| File | Lines | Description |

|------|-------|-------------|

| `DBreeze/VectorLayer/HNSW/TurboQuantConfig.cs` | \~200 | Types, enums, precomputed codebooks |

| `DBreeze/VectorLayer/HNSW/TurboQuantMath.cs` | \~900 | NET472 scalar math (rotation, QJL, quantize/dequantize) |

| `DBreeze.Net8/VectorLayer/HNSW/TurboQuantMath.cs` | \~1200 | .NET8 SIMD-accelerated math |

| `DBreeze.Net8/VectorLayer/HNSW/TurboQuantConfig.cs` | \~150 | .NET8 copy of config types |



\### Files Modified (5)



| File | Changes |

|------|---------|

| `Transaction.Vector.cs` | Added `TurboQuantParams` field to `VectorTableParameters<TVector>` |

| `SmallWorld.Storage.cs` | Added quantized key schema (prefix 6/7), `SetTurboQuantParams` on `IStorage`, compression/decompression utils for MSE and InnerProduct formats |

| `HNSW.Composer.cs` | Added `TurboQuantParams` field, `using DBreeze.VectorLayer` |

| `VectorTran.cs` | Passes `TurboQuantParams` from `VectorTableParameters` → `Composer` → `Storage` |

| `DBreeze.csproj` | Added new files to compilation |



\### DBreeze Storage Schema Additions



```

6- Key: {6, (long)externalId} -> Value: \[dim:2]\[bitWidth:1]\[norm:4]\[indices:dim]  (MSE)

7- Key: {7, (long)externalId} -> Value: \[dim:2]\[bitWidth:1]\[norm:4]\[resNorm:4]\[mseIndices:dim]\[qjlSigns:dim] (InnerProduct)

```



\### Usage



```csharp

// Standard usage (backward compatible - no changes)

tran.VectorsInsert("myTable", vectors);



// With 4-bit MSE TurboQuant (8× compression vs float32, 4× vs float16)

var tqp = new VectorTableParameters<float\[]> {

&#x20;   TurboQuant = new TurboQuantParams { BitWidth = 4, Mode = eTurboQuantMode.MSE }

};

tran.VectorsInsert("myTable", vectors, tqp);



// With 3-bit InnerProduct (unbiased KNN search)

var tqp2 = new VectorTableParameters<float\[]> {

&#x20;   TurboQuant = new TurboQuantParams { BitWidth = 3, Mode = eTurboQuantMode.InnerProduct }

};

tran.VectorsSearchSimilar("myTable", query, 10, tqp2);

```



\### Key Algorithms Implemented



| Algorithm | Paper Reference | Implementation |

|-----------|----------------|----------------|

| Random Rotation | Sec 3.1, Householder reflections | `ApplyRandomRotation`, `ApplyInverseRotation` (deterministic from seed) |

| Lloyd-Max Scalar Quantization | Eq (4), Algorithm 1 | `QuantizeCoordinates` with binary search on precomputed codebooks |

| QJL 1-bit Transform | Definition 1, Lemma 4 | `QJLQuantize`, `QJLDequantize` with sqrt(pi/2)/d scaling |

| TurboQuant\_mse | Algorithm 1 | `QuantizeMse`, `DequantizeMse` |

| TurboQuant\_prod | Algorithm 2 | `QuantizeProdSafe`, `DequantizeProd` (MSE + QJL on residual) |



\### Distortion Guarantees (from paper)



| Bit-width | MSE Distortion | Inner Product Distortion |

|-----------|---------------|-------------------------|

| b=1 | \~0.36 | \~1.57/d |

| b=2 | \~0.117 | \~0.56/d |

| b=3 | \~0.03 | \~0.18/d |

| b=4 | \~0.009 | \~0.047/d |

