# Handoff — DFS issues #354 / #355

Written 2026-08-04. Branch `4-dfs-operations-flag`, based on `upstream/master` @ `2553397` (SMBLibrary 1.5.7.1).

Everything in this file is working context, not upstream content. See "Producing the upstream PR" at the bottom.

---

## Upstream situation

Two issues filed 2026-08-04 by `cf-rafal`, both tagging `@jahales`. Only other open issue is #351 (`ListShares` buffer overflow), unrelated.

| Issue | Subject | State |
|---|---|---|
| [#354](https://github.com/TalAloni/SMBLibrary/issues/354) | `SMB2_FLAGS_DFS_OPERATIONS` never set, referral loop unreachable | Fixed on this branch, **not lab-verified** |
| [#355](https://github.com/TalAloni/SMBLibrary/issues/355) | No root referral before TreeConnect, domain-based namespace fails | Not started |

Both replied to already; neither reporter nor Tal has responded since.

### Tal's position on #355, and ours

Tal proposed `public virtual IPAddress GetHostAddress()` for custom name resolution and questioned whether domain-based namespaces are in scope ("communicating with the domain controller is certainly outside the scope").

We pushed back: in the reporter's repro `Connect("example.local")` and `Login` both **succeed** — the failure is at `TreeConnect`, so name resolution is not the gap. A name-resolution hook cannot fix it, because the caller would still need to know which server hosts the namespace, which is exactly what the root referral returns. And the referral is `FSCTL_DFS_GET_REFERRALS` on `IPC$` over the SMB session we already have — no LDAP, no RPC. `DfsReferralHelper.GetDfsReferral` (public since #346) already implements it.

Where Tal is right: do not import the full MS-DFSC client (DomainCache, ReferralCache, DfsPathResolver). That was PR #326 and it was correctly rejected as too large.

---

## What #354 actually was

Root cause, confirmed in code with no server needed:

- `SMB2PacketHeaderFlags.DfsOperations` was declared and **never assigned anywhere** in the tree.
- `SMB2FileStore.CreateFile` never touched `request.Header.Flags`, and held no server/share name to build a DFS-form path from.
- `SMB2DfsFileStore` gates its whole referral loop on `STATUS_PATH_NOT_COVERED`, which a server only returns when the flag is set.

Net effect: the referral loop merged in #352 could never fire against a real server.

Why it shipped: `SMB2DfsFileStoreTests` injects `STATUS_PATH_NOT_COVERED` from a fake store, so it tested the loop while assuming away the trigger. The lab harness was env-gated and reported Inconclusive, so CI was green on a feature nobody exercised.

---

## The fix on this branch

Two production files. Public API surface is **unchanged** — this matters, we told Tal in #352 that `ResetSecurityContext` was the only public/breaking change for the whole DFS feature.

**`SMBLibrary/Client/SMB2FileStore.cs`**
- New `m_dfsSharePath` field, `"server\share"` for a DFS root and `null` otherwise, so non-null *is* the DFS predicate.
- New `internal` constructor overload takes it; the existing public constructor delegates with `null`.
- `CreateFile` sets `SMB2PacketHeaderFlags.DfsOperations` and sends the full `server\share\path` name (MS-SMB2 3.2.4.1.4 and 2.2.13).

**`SMBLibrary/Client/SMB2Client.cs`**
- `TreeConnect` computes `dfsSharePath` on the line that already detects `ShareFlags.DfsRoot`.
- New `private static IsIPAddress(string)` guard (see regression #2 below).
- `TrySendCommand(SMB2Command, bool)` and `WaitForCommand(ulong, out bool)` are now `internal virtual` — test seams, invisible outside the assembly.

### Two regressions caught in review, after the fix "worked"

Both were introduced by the first cut and are now fixed and covered by tests. Worth knowing about, because both would have been found by the reporter within minutes.

1. **Share-root idiom broke.** `ClientExamples.md:26` — Tal's own documented way to open a share root — passes `"\"` as the path. Not null-or-empty, so it hit the concatenation branch and produced `SERVER1\DfsRoot\\`. Fixed with `path.TrimStart('\\')`. Verified by reverting the fix and watching the test fail with `Actual:<SERVER1\DfsRoot\\>`.

2. **IP-connected callers broke.** `m_serverName` is the IP string when the caller used `Connect(IPAddress, ...)`, so any existing caller connecting by address to a share that happens to carry `SHAREFLAG_DFS_ROOT` would suddenly send `10.0.0.5\Namespace\...` for DFS normalization and fail — previously-working access. Fixed by the `IsIPAddress` guard: connect-by-address keeps share-relative paths and never sets the flag.

---

## Verification status

```bash
dotnet test SMBLibrary.Tests -f net6.0
```

Expect **69 passed, 0 failed, 4 skipped**. Same on `-f net472`. Library builds clean on all three TFMs (`net20`, `net40`, `netstandard2.0`); only pre-existing `TimeZone` obsolescence warnings.

The 4 skipped are the DFS lab tests (below). `When_SMB2ClientConnectsAndServerSendNonSmbData_ShouldNotReachTimeout` is a pre-existing flake in Tal's `SMB2ClientTests` — a wall-clock race on `ElapsedMilliseconds < 200`, unrelated to this work.

### Tests added

- `SMBLibrary.Tests/IntegrationTests/SMB2FileStoreIntegrationTests.cs` — three tests against a real in-process `SMBServer` over loopback: write/read-back round-trip, and both share-root idioms (`"\"` and `String.Empty`). Guards the "no regression for existing callers" property. Uses the `Interlocked.Increment` port allocation from `SMB2ClientTests` (Tal's fix in `583bccf`), not `LoginTests`' random port.
- `SMBLibrary.Tests/Client/SMB2FileStoreTests.cs` — five unit tests for the DFS flag and name composition. Needed because `SMBLibrary/Server/SMB2/TreeConnectHelper.cs` never emits `ShareFlags.DfsRoot`, so no in-process server can produce a DFS root to test end-to-end. The test double sets the private `m_isConnected` by reflection specifically to avoid making `IsConnected` public virtual.

### NOT verified — this is what the lab is for

- **That a Windows server actually returns `STATUS_PATH_NOT_COVERED` once the flag is set.** This is inference from MS-SMB2 3.3.5.9 plus the reporter's observation. It is the whole premise of the fix.
- **The exact `Name` encoding.** Believed to be `server\share\path` with no leading backslash. Confirm in Wireshark before submitting — Tal caught a spec detail this way in #326 (string placement, MS-DFSC 2.2.5), so he will look.
- **The end-to-end referral hop** against a real namespace: create a file through a DFS link and confirm it lands on the target server.
- **The IP-connected DFS root case**, to confirm the `IsIPAddress` fallback behaves as intended rather than merely avoiding the flag.

---

## Lab harness

`SMBLibrary.Tests/Client/SMB2DfsFileStoreIntegrationTests.cs` — 262 lines, four env-gated end-to-end tests against a real DFS namespace (tree connect returns a DFS store, read through a link, read-through matches direct-target read, enumerate a link directory).

**It was hidden from git by `.git/info/exclude`, which is local-only and does not travel with a push.** It is included in the handoff commit so it reaches the desktop; re-add the exclude there if you want it out of future commits:

```bash
echo "SMBLibrary.Tests/Client/SMB2DfsFileStoreIntegrationTests.cs" >> .git/info/exclude
```

Required env vars — tests report Inconclusive when unset, which is exactly how #354 slipped through, so **check for Skipped in the output, not just for green**:

```
SMB_DFS_SERVER        DFS namespace server, e.g. LAB-DC1.LAB.LOCAL
SMB_DFS_ROOT_SHARE    namespace name, e.g. Files
SMB_DFS_USER / SMB_DFS_PASSWORD / SMB_DFS_DOMAIN
SMB_DFS_LINK_PATH     file path crossing a link, e.g. Sales\readme.txt
SMB_DFS_LINK_DIR      directory under a link
SMB_DFS_TARGET_SERVER / SMB_DFS_TARGET_SHARE / SMB_DFS_TARGET_PATH
```

Note `.tmp/` is gitignored, so `.tmp/chat-summary.md` and `.tmp/pr1-body.md` from the earlier PRs did not travel.

---

## Known-open items

Left deliberately, with reasons:

1. **`TreeConnect`'s DFS-true branch is untested.** The integration tests cover the `isDfsShare == false` branch end-to-end, but nothing covers the true branch or the `IsIPAddress` guard, because the in-process server cannot advertise `ShareFlags.DfsRoot`. Closing it needs either server-side DFS support (scope creep) or the lab.
2. **Hop composition through a real `SMB2FileStore` is untested** — same blocker. `SMB2DfsFileStoreTests` uses `FakeFileStore`, so a double-prefixed name like `SERVER2\Share\SERVER2\Share\file.txt` would still pass every test.
3. **Public `SMB2FileStore` constructor yields a non-DFS store.** Intentional — exposing the DFS parameter publicly would add public API. Belongs in the PR description, not the code.

---

## PR plan

**PR A — #354 (this branch).** Ready pending lab verification. Description should state: no public API change; scoped to CREATE only; and item 3 above.

**PR B — docs for #355.** Add a `ClientExamples.md` entry showing the manual root referral with the already-public `DfsReferralHelper`: connect to the domain, `TreeConnect("IPC$")`, `GetDfsReferral(@"\example.local\Namespace")`, connect to the returned namespace server. Zero risk, satisfies the second option the reporter said he'd accept, and demonstrates the scope argument in code rather than prose.

**PR C — automatic root referral in `TreeConnect` on `STATUS_BAD_NETWORK_NAME`.** Structurally symmetric with the merged `PATH_NOT_COVERED` → referral → retry. Hold until Tal responds on scope; B makes it an easier sell.

Deliberately out of scope for all three: the referral cache flagged as a follow-up in #352. Correctness before caching.

---

## Producing the upstream PR

The branch has two commits. **Only the first goes to Tal.**

- `HEAD~1` — the fix and its tests. This is the PR.
- `HEAD` — this file, `.specify/`, `.claude/`, `.github/`, and the lab harness. Tooling and context only.

```bash
git push upstream HEAD~1:refs/heads/4-dfs-operations-flag
```

Constitution note: `.specify/memory/constitution.md` Principle VI was inverted to "Tests Ship With The PR" (v1.1.0 → v2.0.0, amended 2026-08-04). The old rule said to strip tests before submitting, which is contradicted by Tal having merged `SMB2DfsFileStoreTests.cs` in #352 — following it would have deleted this PR's regression tests.
