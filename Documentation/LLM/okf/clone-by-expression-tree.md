---
type: API Reference
title: CloneByExpressionTree
description: Deep cloning of nested application data using cached expression-tree delegates, with production constraints.
tags: [utils, cloning, expression-trees, performance]
sources:
  - id: shared-cloner
    resource: ../../../DBreeze/Utils/DeepCloneExpressionTree.cs
    title: Shared expression-tree cloning implementation
  - id: modern-cloner
    resource: ../../../DBreeze.Net8/Utils/DeepCopyByExpressionTrees.cs
    title: Net8 expression-tree cloning implementation
  - id: framework-project
    resource: ../../../DBreeze/DBreeze.csproj
  - id: net6-project
    resource: ../../../DBreeze.Net5/DBreeze.Net5.csproj
  - id: standard-project
    resource: ../../../DBreeze.NetStandard/DBreeze.NetStandard.csproj
  - id: core-project
    resource: ../../../DBreeze.NetCoreApp/DBreeze.NetCoreApp.csproj
  - id: modern-project
    resource: ../../../DBreeze.Net8/DBreeze.Net8.csproj
---
# API

Namespace: `DBreeze.Utils`. Static class: `DeepCopyByExpressionTrees`.

```csharp
public static T CloneByExpressionTree<T>(
    this T original,
    Dictionary<object, object> copiedReferencesDict = null)
```

`DeepCopyByExpressionTree<T>()` is an equivalent alias. The Net8 source annotates
the optional dictionary as nullable. Both methods return the same generic `T`
and clone according to each object's runtime type.[^shared-cloner][^modern-cloner]

# Examples

```csharp
using System.Collections.Generic;
using DBreeze.Utils;

var original = new List<List<int>> { new List<int> { 1, 2 } };
var clone = original.CloneByExpressionTree();
clone[0].Add(3); // original[0].Count remains 2
```

# Behavior

- Starts with `MemberwiseClone`, then recursively replaces fields requiring deep
  copies. Includes public, private, and inherited instance fields, arrays, and
  structs containing references. It does not call constructors or property
  accessors; auto-property backing fields are copied as fields.
- Basic values are copied and immutable strings are reused. Null input returns
  null. Delegates, including event backing fields, become null.
- The default per-call reference map records class clones before descending into
  fields, supporting cycles and shared class references. Bare `System.Object`
  instances bypass map insertion, so repeated occurrences do not preserve identity;
  boxed value-type identity is also not preserved.
- Normally omit `copiedReferencesDict`. A custom dictionary must compare keys by
  reference identity. Treat it as state for one graph-copy operation; reuse can
  return an earlier clone and retain both originals and copies. Do not share it
  concurrently.[^shared-cloner][^modern-cloner]

# Performance and production use

Prefer this helper over repeatedly traversing application DTOs with reflection:
type discovery and expression compilation are amortized through cached delegates
per runtime type. Writable fields use compiled assignments; readonly reference
fields still use `FieldInfo.SetValue`. The shared implementation replaces locked
cache snapshots; Net8 uses concurrent caches. First-use compilation has a cost;
measure cold and warm calls against representative object graphs. The source's
historical Protobuf speed comment is not a reproducible benchmark or a performance
guarantee.[^shared-cloner][^modern-cloner]

Use for application data, not engines, transactions, streams, locks, or other live
resource owners: field copying cannot recreate external resources or ownership
contracts. Finish loading any database-backed data inside its transaction before
cloning detached application objects. Expression compilation and non-public field
access require checking the actual deployment's trimming/AOT compatibility.
Unusual layouts, custom collections, and resource-bearing types need application
validation; the helper is not a serialization or database backup mechanism.

# Target availability and maintenance

The shared source is guarded by
`NET6FUNC || NET472 || NETSTANDARD2_1 || NETCOREAPP2_0`.[^shared-cloner]
It is included by the current Framework 4.7.2, Net5 (currently targeting net6.0),
NetStandard 2.1, and NetCoreApp projects.[^framework-project][^net6-project][^standard-project][^core-project]
Net8 owns its separate implementation.[^modern-cloner][^modern-project]
Check project includes and symbols before extending availability claims to other
builds. When either implementation changes, check the other implementation and
update the compact [production skill](../DBreeze_skill.md#84-deep-cloning-with-clonebyexpressiontree).

[^shared-cloner]: Shared cloning source, including guards, caches, field copying, and reference tracking.
[^modern-cloner]: Net8 cloning source, including concurrent caches and nullable annotations.
[^framework-project]: Framework target and source includes.
[^net6-project]: Net5 project's current target, symbols, and linked source.
[^standard-project]: NetStandard target, symbols, and linked source.
[^core-project]: NetCoreApp target, symbols, and linked source.
[^modern-project]: Net8 target, symbols, and project-owned source compilation.
