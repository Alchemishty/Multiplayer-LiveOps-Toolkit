# Implement Feature

Full orchestrator: plan → TDD implementation → verification → cleanup → learning.

## Prerequisites

- Read `AGENTS.md` for agent rules
- Read `harness.yaml` for verification commands
- Read `docs/conventions.md` for coding standards
- Read `memory/` for cross-session context

## Phase 1 — Planning

1. Check if a plan exists in `docs/plans/active/` for this feature
2. If no plan exists: invoke superpowers:brainstorming to design the feature, then superpowers:writing-plans to create the implementation plan
3. Save the plan to `docs/plans/active/YYYY-MM-DD-<topic>.md`
4. Create a task list from the plan steps

## Phase 2 — Implementation Loop

For each plan step:

1. **Re-anchor context:** Re-read the current step's requirements from the plan file. This prevents drift in long sessions.
2. **Declare architecture before coding:** State the classes this step will create or modify, with one-sentence responsibilities for each. If any class uses "and" in its description, split it before proceeding. This is required by `enforcement/solid.md` — re-read that file now.
3. **Write tests first (TDD):**
   - Re-read `enforcement/test-coverage.md` before writing tests
   - EditMode test for pure C# logic (Data, validation, business rules)
   - PlayMode test for MonoBehaviour/NetworkBehaviour integration
   - Tests must assert behavior (return values, exceptions, state changes), not code structure
   - Tests must fail before implementation (red phase)
4. **Implement to pass tests:**
   - Follow `docs/conventions.md` strictly
   - Follow all `enforcement/` rules
   - Use game design data structures where they improve efficiency
5. **Verify:**
   - Run `refresh_unity` to trigger recompilation
   - Check `read_console` for zero errors
   - Run `run_tests` for all tests passing
6. **On verification failure:**
   - Read the error output
   - Fix the issue
   - Retry verification (up to 3 attempts per step)
   - After 3 failures: stop and report the issue to the user — do not proceed
7. **Context checkpoint** (from step 4 onward):
   - Write a structured state summary to `scratch/session-state.md`
   - Include: steps completed, current step, files created/modified, test status, open issues
   - This serves as a re-orientation point if context gets compressed

### Phase Isolation (plans with 7+ steps)

For large plans, use sub-agents per phase:
- Each sub-agent gets a fresh context window
- Sub-agents write results to files in `scratch/` (not conversation paraphrasing)
- The orchestrator reads files directly for full-fidelity handoff
- This prevents quality degradation from context length

### Observation Masking

Verbose tool output (Unity console dumps, test results, large diffs) should be:
- Captured to `scratch/` files when needed for reference
- Summarized in conversation context (1-3 lines)
- This keeps context budget available for reasoning and code quality

## Phase 3 — Cleanup

1. **Plan adherence check:** Verify every plan step was completed, all planned files exist, all tests pass
2. **Run enforcement checks:**
   - Null safety: no silent null consumption
   - Layer imports: no upward references
   - Naming: no abbreviations, descriptive names
   - Serialization: all deserialization validated
   - Netcode lifecycle: no pre-spawn access
   - Event cleanup: all subscriptions have matching teardowns
   - Editor/runtime: no unguarded UnityEditor references
3. **Run `/deslop`** on all changed files
4. **Run `/garden`** for entropy check
5. **Clean `scratch/`** of intermediate files (keep `session-state.md` for retrospective)

## Phase 4 — Learning

1. **Run `/retrospective`** to capture patterns, fixes, and domain knowledge to `memory/`
2. **Move plan** from `docs/plans/active/` to `docs/plans/completed/`
3. **Print summary:** what was built, files created/modified, tests added, any issues encountered

## Critical Rules

- **Never skip verification.** Every implementation step must compile and pass tests before the next step begins.
- **Never skip TDD.** Tests first, always. If you can't write a test for it, question whether the code belongs in its current form.
- **Never silently consume null.** This rule applies during implementation, not just in review.
- **Ask the user** if you encounter ambiguity in the plan or a decision that wasn't covered. Don't guess.
