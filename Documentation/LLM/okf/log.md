# DBreeze Knowledge Update Log

## 2026-11-11

- **Correction**: Clarified single-table read-modify-write reservation in
  [the production skill](../DBreeze_skill.md#33-gettransaction-table-locks-versus-synchronizetables)
  and added [implementation guidance](transaction-read-modify-write.md), including
  the limits of the early multi-table warning. DBreeze behavior is unchanged.

## 2026-11-10

- **Initialization**: Established the OKF 0.2 bundle and repository instructions
  for maintaining knowledge and the compact production skill with code changes.
- **Creation**: Documented [CloneByExpressionTree](clone-by-expression-tree.md),
  including nested cloning, cached delegates, target availability, and production
  constraints; added its usage guidance to `DBreeze_skill.md`.
