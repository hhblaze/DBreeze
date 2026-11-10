# Working on DBreeze

## Read before changing code

- Read [the production usage skill](Documentation/LLM/DBreeze_skill.md) and
  [the OKF index](Documentation/LLM/okf/index.md), then load concepts relevant to
  the change. Check claims against the current implementation and project files.
- Inspect the affected `.csproj` files and conditional compilation symbols.
  Several targets link shared sources from `DBreeze/`; `DBreeze.Net8/` also owns
  modern implementations. Do not assume a change in one project reaches every target.
- Preserve existing public API and persistent storage compatibility unless the
  requested work explicitly changes those contracts. Use syntax and APIs supported
  by every target compiling the changed source.

## Maintain knowledge with the code

Every functionality addition, change, fix, or deprecation must include a review of
both documentation surfaces below. Update affected guidance in the same change;
avoid unrelated documentation rewrites.

### Detailed knowledge: `Documentation/LLM/okf/`

Use [Open Knowledge Format (OKF)](https://github.com/GoogleCloudPlatform/open-knowledge-format/blob/main/SPEC.md).
This repository's bundle targets OKF **0.2**, declared in its root `index.md`.

- Store durable implementation knowledge here: behavior, architecture, invariants,
  compatibility, pitfalls, and evidence from relevant checks. Keep one focused
  concept per file; extend an existing concept before creating a duplicate.
- Write UTF-8 Markdown with YAML frontmatter. Every concept needs a non-empty
  `type`; include `title`, a one-sentence `description`, and `sources` pointing to
  the implementation, tests, or authoritative documentation. Repository source
  paths must resolve relative to the concept file; paths beginning with `/` are
  relative to the OKF bundle, not the repository.
- Keep names and links stable. Link related concepts with Markdown links. When
  adding, moving, or retiring concepts, update `index.md` and add a short entry
  to `log.md` under an ISO date (`YYYY-MM-DD`), newest first.
- Reserve `index.md` for navigation and `log.md` for history. Neither has
  frontmatter, except the bundle-root index may declare `okf_version`.
- Record provenance honestly. If using `generated` or `verified`, follow the
  specification's actor and timestamp conventions; never invent a human review,
  successful test, benchmark result, or verification event. Recheck or remove
  stale verification when the documented behavior changes.
- Source code and verified behavior take precedence over stale knowledge. Correct
  the affected concept when a discrepancy is discovered.

### Compact production skill: `Documentation/LLM/DBreeze_skill.md`

- Keep this a compact, laconic, standalone reference for LLMs using DBreeze in
  production. Update it whenever functionality changes how a consumer should use
  DBreeze, including relevant fixes and deprecations.
- Prefer exact API names, target availability, defaults, short examples, and
  actionable lifetime, threading, durability, or performance constraints.
- Edit the existing section; avoid repetition, development history, exhaustive
  implementation detail, and unsupported performance promises. Put deeper
  explanations and evidence in OKF while retaining essential usage rules here.
- Keep examples, API counts, quick-map links, and version claims consistent with
  the affected public surface. Preserve unrelated user edits.

## Verify and finish

- For code changes, build the affected targets and run the relevant existing
  regression checks. Inspect the runner before selecting commands: for example,
  `DBreeze.Net8.Tests` is a console executable with selectable checks in
  `Program.cs`. Use isolated temporary database folders for write tests.
- For documentation changes, verify API signatures, examples, target guards,
  local links, and OKF frontmatter against their sources. A documentation-only
  change does not require a full solution build.
- Review the diff for unintended edits. Report what changed, which checks ran,
  and any material limitation. If neither documentation surface needs a change,
  briefly explain why in the change summary.
