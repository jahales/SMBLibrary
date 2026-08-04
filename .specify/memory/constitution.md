<!--
Sync Impact Report
- Version change: 1.1.0 → 2.0.0 (Principle VI inverted: tests now ship
  with the upstream PR, per Tal merging tests in PR #352)
- Previous: 1.0.0 → 1.1.0 (two amendments from Tal's PR #346 feedback)
- Amended principles:
  - I. Zero Breakage → Minimal Modification (relaxed to allow maintainer-directed changes)
  - II. Upstream Style Match → ArgumentNullException uses nameof() instead of string literals
- Templates requiring updates:
  - .specify/templates/plan-template.md — ✅ compatible (Constitution Check section is dynamic)
  - .specify/templates/spec-template.md — ✅ compatible (no principle-specific content)
  - .specify/templates/tasks-template.md — ✅ compatible (phase structure is dynamic)
- Follow-up TODOs: none
-->

# SMBLibrary DFS Client Contribution Constitution

## Core Principles

### I. Minimal Modification (NON-NEGOTIABLE)

Changes to existing files in TalAloni/SMBLibrary MUST be minimal and
MUST NOT alter existing behavior for current callers. New functionality
SHOULD be in new files where possible. When the upstream maintainer
explicitly directs modification of an existing file (e.g., adding a
flag check in TreeConnect), the modification is permitted provided it
is backward-compatible and clearly scoped.

- Rationale: The upstream maintainer accepts contributions when they
  carry zero regression risk. Purely additive new files are preferred,
  but small, backward-compatible modifications to existing files are
  acceptable when the maintainer requests them.
- Amendment (v1.1.0, 2026-04-18): Relaxed from "no existing file may
  be modified" after Tal explicitly directed TreeConnect() modification
  for DFS flag detection (PR #346 review feedback). Per Governance
  §Disputes: "maintainer's preference wins."

### II. Upstream Style Match

All contributed code MUST match Tal Aloni's coding conventions exactly.
Deviations will be rejected at PR review.

- Field naming: use `m_` prefix for private/protected instance fields.
- Copyright headers: every new `.cs` file MUST carry the LGPL license
  header matching the format used in existing upstream files.
- Static helper classes: use `*Helper` suffix naming convention
  (e.g., `DFSHelper`, `TreeConnectHelper`).
- No XML doc comments on private or internal members.
- Argument validation: use `ArgumentNullException(nameof(paramName))`
  with `nameof()` expressions per Tal's explicit feedback on PR #346.
  (Amended v1.1.0, 2026-04-18: changed from string literals to
  `nameof()` after Tal's review comment: "please use nameof — this is
  a good habit.")
- Brace style, spacing, and formatting MUST match existing files in
  the same directory.

### III. Spec Compliance

All protocol behavior MUST conform to Microsoft's published
specifications: MS-DFSC (Distributed File System: Referral Protocol)
and MS-SMB2 (Server Message Block Protocol Versions 2 and 3).

- Cite specific specification sections in code comments where behavior
  is dictated by the spec (e.g., `// MS-SMB2 §2.2.31: sentinel FileId`).
- When spec language is ambiguous, document the interpretation chosen
  and the reasoning in a code comment.
- Do not invent protocol behaviors not described in the specifications.

### IV. Minimal Footprint

Each upstream PR MUST be small enough for the repository owner to
review in one sitting.

- Target approximately 200 lines of diff and 3 or fewer new files per
  PR chunk.
- Avoid scope creep: if implementation reveals a need beyond the
  current chunk, defer it to a subsequent chunk.
- Every PR description MUST state what it adds and what it
  intentionally defers.

### V. Piecemeal Extraction

Large features MUST be decomposed into independently reviewable chunks.
Each chunk MUST be:

- Self-contained: compiles and passes all existing tests without the
  subsequent chunks.
- Independently testable: can be verified in isolation during
  development.
- Ordered: later chunks may depend on earlier ones, but never the
  reverse.
- Documented: a brief description of the chunk's purpose and its
  position in the overall feature sequence.

### VI. Tests Ship With The PR

Tests MUST be written using MSTest to verify correctness of every new
class and method, and they are submitted upstream together with the
code they cover.

- Tests live in the `SMBLibrary.Tests/` project.
- Prefer high-leverage tests over exhaustive ones: an end-to-end test
  against an in-process `SMBServer` (see
  `SMBLibrary.Tests/IntegrationTests/`) is worth more than many mocked
  unit tests, and needs no production seams to exist.
- Add a focused unit test when a behavior cannot be reached end-to-end
  (e.g. the server implementation cannot advertise the condition under
  test). Prefer test-local mechanisms over widening the public API.
- Test coverage MUST exercise both happy-path and error/edge-case
  scenarios.
- Amendment (v2.0.0, 2026-08-04): Inverted from "test files MUST NOT be
  included in the upstream PR submission". Tal merged
  `SMBLibrary.Tests/Client/SMB2DfsFileStoreTests.cs` as part of PR #352,
  so the original directive no longer reflects his preference. Per
  Governance §Disputes: "maintainer's preference wins."

## Technical Context

- **Language**: C# targeting .NET Framework 4.7.2 and .NET 6.0
  (multi-target).
- **Solution**: `SMBServer.sln` built with MSBuild.
- **Test Framework**: MSTest 2.2.10 (development only; not submitted
  upstream).
- **Upstream Repository**: TalAloni/SMBLibrary on GitHub.
- **License**: LGPL (all new files MUST carry the LGPL header).
- **Relevant Specs**: MS-DFSC, MS-SMB2, MS-SMB (where applicable).

## Development Workflow

1. **Design**: Identify the next chunk per Principle V. Verify the
   design satisfies Principles I and IV before writing code.
2. **Implement**: Write new files only (Principle I). Match upstream
   style exactly (Principle II). Cite spec sections (Principle III).
3. **Test**: Write MSTest unit tests covering the new code
   (Principle VI). Run `dotnet test` to confirm all tests pass.
4. **Review locally**: Verify the diff contains only new files. Confirm
   line count is within the ~200-line target (Principle IV).
5. **Include tests**: Keep the test files for the chunk in the commit,
   favouring high-leverage integration tests (Principle VI).
6. **Submit PR**: Open a PR against TalAloni/SMBLibrary with a clear
   description of what is added and what is deferred.

## Governance

This constitution is the authoritative reference for all contribution
decisions in this project. When any principle conflicts with a proposed
change, the principle takes precedence and the change MUST be reworked.

- **Amendments**: Any change to this constitution MUST be documented
  with a version bump, rationale, and updated date. MAJOR version for
  principle removal or redefinition; MINOR for new principles or
  material expansion; PATCH for clarifications and wording.
- **Compliance**: Every spec, plan, and task MUST include a
  Constitution Check gate verifying adherence to all six principles
  before implementation proceeds.
- **Disputes**: When a principle appears to conflict with upstream
  maintainer feedback, the maintainer's preference wins and the
  constitution MUST be amended to reflect the resolution.

**Version**: 2.0.0 | **Ratified**: 2026-04-17 | **Last Amended**: 2026-08-04
