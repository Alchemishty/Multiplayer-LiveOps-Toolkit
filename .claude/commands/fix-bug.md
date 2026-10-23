# Fix Bug

Systematic debugging: reproduce → root cause → test → fix → verify.

No fixes without root cause investigation first. Symptom fixes are failure.

## Prerequisites

- Read `AGENTS.md` for agent rules
- Read `docs/domain.md` if the bug involves multiplayer/Netcode
- Read `enforcement/` rules — the root cause may be an enforcement violation

## Phase 1 — Root Cause Investigation

Do NOT propose fixes until this phase is complete.

1. **Read the error carefully.** Stack traces, error messages, line numbers. Don't skip past them.

2. **Reproduce.** Can you identify the exact trigger? If the bug is intermittent, identify the conditions that make it more likely (late join, build vs editor, timing, load order).

3. **Check what changed.** Read recently modified files. What's different from when it last worked?

4. **Trace the data flow backward.** Start at the crash/symptom and trace upstream:
   - Where does the bad value come from?
   - What called this method with that value?
   - Keep going until you find the source — that's the root cause.

5. **Check enforcement rules.** Many bugs are enforcement violations:
   - NullReferenceException → check `enforcement/null-safety.md` — is something silently returning null?
   - Late-join bugs → check `enforcement/netcode-lifecycle.md` — is something subscribed in `Awake` instead of `OnNetworkSpawn`?
   - Intermittent in builds → check `enforcement/editor-runtime.md` — is there an unguarded `UnityEditor` reference?
   - Data loss after rename → check `enforcement/serialized-field-rename.md`

6. **State your root cause.** Write one sentence: "The root cause is [X] because [Y]." If you can't write this sentence, you're not done investigating.

## Phase 2 — Write the Reproduction Test

Before touching the broken code, re-read `enforcement/test-coverage.md` now.

1. Write a test that **reproduces the bug** — it should FAIL right now.
   - EditMode if the bug is in pure logic
   - PlayMode if it involves MonoBehaviour lifecycle or Netcode
   - Tests must be behavioral (assert return values, exceptions, state changes) — not structural (no reflection, no source parsing)

2. The test must be specific enough that fixing the bug makes it pass, and reverting the fix makes it fail again.

```csharp
[UnityTest]
public IEnumerator LateJoiningClient_ReceivesCurrentPlayerState()
{
    // This test should FAIL before the fix (reproduces the bug)
    // and PASS after the fix
}
```

3. Run the test. Confirm it fails. If it passes, your test doesn't capture the bug — rewrite it.

## Phase 3 — Fix the Root Cause

1. **Fix at the source, not the symptom.** If `ItemRegistry.GetItem` silently returns null and that causes a downstream NullReferenceException, fix `GetItem` to throw — don't add a null check at the crash site.

2. **One change at a time.** Don't fix multiple things. Don't refactor while debugging. Don't "improve" adjacent code (unless it's an enforcement violation — rule 9 in AGENTS.md).

3. **Fix adjacent enforcement violations.** If you find enforcement violations near the bug (silent null consumption, missing base calls, abbreviated names), fix them. But keep bug-fix commits separate from cleanup.

## Phase 4 — Verify

1. Run the reproduction test. It must pass.
2. Run `refresh_unity` → `read_console` — zero errors.
3. Run `run_tests` — all tests pass (not just the new one).
4. Check: did you introduce any enforcement violations? (null safety, naming, lifecycle, serialization)

**If the fix doesn't work:**
- Count how many fixes you've tried.
- Less than 3: return to Phase 1 with the new information.
- 3 or more: stop. The problem may be architectural. Report to the user before trying again.

## Phase 5 — Document

1. If this bug pattern could recur, write it to `memory/fixes.md`:
   ```markdown
   ## [Date] — Brief Title
   **Symptom:** What the user reported
   **Root cause:** What was actually wrong
   **Fix:** What was changed
   **Apply when:** When to watch for this pattern
   ```

2. If the bug revealed a missing enforcement rule, propose adding one.

## Red Flags — Return to Phase 1

If you catch yourself thinking any of these, stop:
- "Let me just try changing this and see if it works"
- "Quick fix for now, investigate later"
- "It's probably X" (without tracing the data flow)
- "I'll add a null check here" (treating symptom, not cause)
- "I don't fully understand why but this should work"

## Unity-Specific Debugging Checklist

When the root cause isn't obvious, check these common Unity gotchas:

- [ ] Is something using `Awake()` that should use `OnNetworkSpawn()`?
- [ ] Is `OnValueChanged` missing the initial value read?
- [ ] Is `base.OnNetworkSpawn()` / `base.OnNetworkDespawn()` called?
- [ ] Is a singleton accessed before its `Awake()` has run? (check execution order)
- [ ] Is `GetComponent` returning null because the component is on a child/parent?
- [ ] Is JSON deserialization returning null on malformed input?
- [ ] Is a `[SerializeField]` field null because it was renamed without `[FormerlySerializedAs]`?
- [ ] Is the bug build-only? Check for `#if UNITY_EDITOR` differences.
- [ ] Is it a timing bug? Check for race conditions between `Start()` on different objects.
