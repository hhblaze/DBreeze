---
okf_version: "0.2"
---
# DBreeze Knowledge

Implementation knowledge maintained with DBreeze changes, following the
[OKF specification](https://github.com/GoogleCloudPlatform/open-knowledge-format/blob/main/SPEC.md).
Repository workflow: [AGENTS.md](../../../AGENTS.md).
Compact production usage: [DBreeze skill](../DBreeze_skill.md).

## Transactions

- [Read-modify-write synchronization](transaction-read-modify-write.md) - Prevent lost updates by reserving tables before dependent reads, even for one table.

## Utilities

- [CloneByExpressionTree](clone-by-expression-tree.md) - Deep cloning of nested application data using cached expression-tree delegates, with production constraints.

## History

- [Update log](log.md) - Changes to this knowledge bundle.
