# Harness Benchmark Experiment Design

**Date:** 2026-10-07
**Status:** Designed, not yet executed
**Author:** Adarsh + Claude Opus 4.6

## Purpose

Evaluate whether the Unity agent harness (installed in MultiplayerLiveOpsToolkit) measurably improves AI coding agent output compared to raw Claude Code without scaffolding. This experiment is designed to be reproducible and, if results are meaningful, formalizable as an open benchmark for game development AI agent evaluation.

## Motivation

No open benchmarks exist for evaluating AI coding agents on Unity/game development tasks. SWE-bench covers Python only. Terminal-Bench 2 covers Python, C, Rust, OCaml, R, SQL. AgentBench has a text-based card game but nothing involving game engine development. The space is completely empty.

This experiment fills two gaps:
1. **Harness evaluation** — does structured scaffolding (enforcement rules, conventions docs, domain knowledge, cross-session memory) improve agent output?
2. **Game dev agent benchmarking** — a reproducible task set with automated scoring for Unity/C# agent work.

### Prior Art

| Benchmark | Covers | Game/Unity? |
|---|---|---|
| SWE-bench | Python issue resolution (12 repos) | No |
| Terminal-Bench 2 | Broad terminal tasks (Python, C, Rust, etc.) | No |
| AgentBench | 8 environments including a text card game | Strategy reasoning, not game dev |
| Repo2RLEnv (HuggingFace) | Auto-generate tasks from any repo | Framework exists but no Unity instance |
| Agentic Harness Engineering | Harness quality measurement | Python tasks only |

## Methodology: Paired Task Matrix

### Approach

Run 8 identical tasks under two configurations:
- **Configuration H (Harness):** Full harness scaffolding — AGENTS.md, docs/ (architecture, conventions, domain, testing), enforcement/ (7 rules), 5 skills, memory/ system
- **Configuration B (Baseline):** Bare CLAUDE.md with only: "This is a Unity multiplayer project using Netcode for GameObjects. C# with NUnit tests."

Each task runs in a fresh session under both configurations. A separate scoring agent evaluates outputs blind — it receives "Output A" and "Output B" with randomized assignment (sometimes H is A, sometimes B).

### Why Paired Comparison

Same task, two conditions, blind scoring. Controls for task difficulty, eliminates ordering bias via randomized labeling, and produces per-task deltas that are directly comparable. The alternative (A/B split with different tasks per configuration) would confound task difficulty with harness effect.

## Test Project: HarnessTestBed

A fresh Unity project built solely for this experiment. Not the MultiplayerLiveOpsToolkit — that project has history and context that would bias results.

### Stack
- Unity 2022.3+ LTS
- Netcode for GameObjects 1.9.1
- Newtonsoft.Json
- TextMeshPro
- Unity Test Framework (NUnit)

### Starter Skeleton

The project contains enough surface area to exercise all harness concerns:

**Core scripts:**
- `Player/PlayerController.cs` — NetworkBehaviour with basic movement and a NetworkVariable for player state
- `Player/PlayerState.cs` — INetworkSerializable struct wrapping JSON
- `Data/ItemData.cs` — serializable DTO for items
- `Data/ItemType.cs` — enum for item categories
- `Registry/ItemCatalog.cs` — ScriptableObject with item definitions, dictionary lookups
- `Registry/ItemRegistry.cs` — singleton facade, DontDestroyOnLoad
- `API/GameAPI.cs` — stub REST client returning mock JSON responses
- `API/APIConfig.cs` — static base URL
- `UI/ShopController.cs` — MonoBehaviour handling UI, API calls, and business logic in one class (deliberately messy for refactor tasks)
- `UI/HUDPanel.cs` — basic HUD display

**Deliberate landmines** (problems the agent must discover or work around):
1. `ItemRegistry.cs` line ~30: `if (Instance == null) return null;` — silent null consumption on uninitialized singleton
2. `ShopController.cs` line ~15: `var mgr = GetComponent<ItemRegistry>();` — abbreviated variable name
3. `ShopController.cs` line ~45: `GetComponent<Renderer>()` inside a method called from Update — performance violation
4. `PlayerController.cs` line ~20: `OnValueChanged` subscription in `Awake()` instead of `OnNetworkSpawn()` — Netcode lifecycle violation
5. `GameAPI.cs` line ~35: `JsonConvert.DeserializeObject<T>(json)` without null check — unvalidated deserialization

These landmines are not the tasks themselves — they're environmental. The question is whether the agent notices and addresses them when working nearby, and whether the harness causes it to catch them more reliably.

### Project Copies

Before the experiment begins:
- `HarnessTestBed-H/` — starter skeleton + full harness installed
- `HarnessTestBed-B/` — starter skeleton + bare CLAUDE.md only

Both directories reset to the starter snapshot between tasks (except tasks 7 and 8 which are multi-step/multi-session by design).

## Task Matrix

8 tasks across 4 categories. Every task uses an identical natural-language prompt — no hints toward specific conventions, enforcement rules, or coding standards. The question is whether the harness causes the agent to apply quality standards unprompted.

### Category A: Feature Implementation

Tests planning, TDD, convention adherence on new code.

#### Task 1 — Inventory System

**Prompt:** "Add an inventory system to the player. Players should be able to hold items by ID with a maximum capacity. The inventory needs to work in multiplayer — all players should see each other's held items."

**What it exercises:**
- New data structures (inventory container — dictionary, list, or custom)
- Pure C# class for inventory logic vs MonoBehaviour for sync
- NetworkVariable or RPC for multiplayer sync
- EditMode tests for pure logic, PlayMode tests for sync
- Null safety: what happens when inventory is full? When item ID doesn't exist?
- Naming: are inventory variables descriptive?
- SOLID: is inventory logic separate from player controller?

**Expected harness advantage:** Conventions doc guides data structure choice. Enforcement rules catch null consumption and naming issues. Domain doc guides Netcode sync approach.

#### Task 2 — Buff/Debuff System

**Prompt:** "Add a buff and debuff system. Players can have timed status effects (speed boost, damage reduction, etc.) that are visible to all players. Effects should expire after their duration. New effect types should be easy to add without modifying existing code."

**What it exercises:**
- ScriptableObject for effect definitions (open/closed principle)
- NetworkVariable for active effects sync
- Timed expiry logic in Update (allocation-free)
- Object pooling for effect instances
- Data lifecycle: SO is authoring-time, active effects are runtime-mutable
- Serialization: effect state across network

**Expected harness advantage:** Patterns guidance (object pool, flyweight). Performance red flags (no allocation in Update). Netcode lifecycle rules. Open/closed principle from SOLID enforcement.

### Category B: Bug Fix

Tests debugging ability and whether the agent applies enforcement rules to existing code.

#### Task 3 — Silent Rendering Bug

**Prompt:** "Players report their avatar sometimes doesn't render items after joining a match. The bug is intermittent — it happens more often when joining late. The game doesn't crash, items just don't appear."

**What it exercises:**
- Diagnosing the `OnValueChanged` subscription in `Awake()` (landmine #4)
- Recognizing that late joiners miss the initial value
- Moving subscription to `OnNetworkSpawn` and handling initial value explicitly
- Whether the agent also notices adjacent issues (landmine #1: silent null return in ItemRegistry)

**Expected harness advantage:** Netcode lifecycle enforcement rule directly describes this bug pattern. Domain doc lists it as pitfall #6. Without the harness, the agent must derive this from general Netcode knowledge.

#### Task 4 — Mysterious Null Reference

**Prompt:** "We're getting occasional NullReferenceException in ShopController but can't reproduce it consistently. Stack trace points to the line where we access the item catalog. It seems to happen more in builds than in the editor."

**What it exercises:**
- Diagnosing landmine #1 (silent null return from ItemRegistry making downstream code fail unpredictably)
- The null safety rule: the fix should throw at the point of absence, not add another null check downstream
- Whether the agent fixes the root cause (singleton initialization ordering) or just patches the symptom

**Expected harness advantage:** Null safety enforcement rule explicitly forbids the pattern causing this bug. Architecture doc describes bootstrap ordering. Without the harness, the agent might add a null check (treating the symptom) rather than throw + fix ordering (treating the cause).

### Category C: Refactor

Tests code quality judgment, naming discipline, and architectural awareness.

#### Task 5 — Clean Up Messy Script

**Prompt:** "ShopController.cs is getting hard to maintain. It handles too many things. Improve it."

**What it exercises:**
- Splitting ShopController into separate responsibilities (UI, business logic, API communication)
- Renaming abbreviated variables (`mgr`, `btn`, `cb` → full names)
- Removing the `GetComponent` in hot path (landmine #3)
- Moving business logic into testable pure C# classes
- Maintaining correct layer boundaries in the split
- Removing what-comments, keeping why-comments

**Expected harness advantage:** Naming enforcement, SOLID enforcement, layer-imports enforcement, performance red flags, deslop skill. Without the harness, the agent may partially refactor but miss naming issues or performance violations.

#### Task 6 — Extract Testable Logic

**Prompt:** "We need to add tests for the purchase flow but the logic is baked into UI button handlers. Make it testable and add the tests."

**What it exercises:**
- Extracting business logic into pure C# (testability guidance from docs/testing.md)
- Creating EditMode tests for the extracted logic
- Keeping the MonoBehaviour thin (wiring only)
- Interface segregation if multiple consumers need the logic
- Test quality: AAA pattern, edge cases, null-throw verification

**Expected harness advantage:** Testing doc explicitly covers this exact pattern (before/after extraction example). Create-tests skill guides tier classification. Without the harness, the agent knows to extract but may not follow the project's testing conventions.

### Category D: Multi-Step / Context Retention

Tests whether quality holds across long sessions and whether knowledge transfers across sessions.

#### Task 7 — Three-Feature Sprint

**Prompt (all three given at once):** "Build these three features in order: (1) Add a currency display to the HUD showing the player's gold count. (2) Add a purchase confirmation dialog that shows the item name, cost, and remaining gold before buying. (3) Add a purchase history panel that logs all purchases with timestamps."

**What it exercises:**
- Context retention: does step 3 code maintain the same quality standards as step 1?
- Plan adherence: does the agent build them in the specified order?
- Consistency: same naming conventions, same null safety patterns, same architecture across all three
- Cross-feature coherence: do the three features share data structures sensibly?

**Scoring focus:** Compare rubric scores for step 1 vs step 3 code. A large delta indicates context degradation. The harness's plan re-reading and compression triggers should maintain consistency.

**Expected harness advantage:** Context engineering features (plan re-reading per step, scratch checkpoints from step 2 onward, observation masking to preserve context budget). Without the harness, quality may degrade as conversation length grows.

#### Task 8 — Cross-Session Memory

**Session 1 prompt:** "Add a player status indicator that syncs a custom enum (Ready, InGame, Away) across all clients using a NetworkVariable."

The agent will likely encounter the `OnValueChanged`-initial-value pitfall during implementation. Whether it discovers and fixes it is recorded.

**Session 2 prompt (new session, same project state from session 1):** "Add a player title system — each player has a display title (string) synced to all clients via NetworkVariable, editable by the owning player."

**What it exercises:**
- Does the harness agent's retrospective from session 1 capture the initial-value pitfall?
- Does the harness agent's memory system cause it to handle the initial value correctly in session 2 without re-discovering the bug?
- Does the baseline agent (no memory system) repeat the same mistake?

**Scoring focus:** Binary — did the agent handle the initial value correctly in session 2 without encountering the bug first? Plus the standard rubrics on the session 2 code.

**Expected harness advantage:** Retrospective skill captures the pitfall to `memory/fixes.md`. Session 2 reads memory at start and avoids the bug proactively. Baseline agent has no cross-session knowledge.

## Scoring System

### Rubrics

7 dimensions. Rubrics 1-4 are universal (any language, any domain). Rubric 5 is domain-specific (Unity/Netcode). Rubric 6 is universal but domain-configured. Rubric 7 is objective metrics.

#### Rubric 1: Correctness (0-10)

Does the code do what the task asked? No runtime errors, no logic bugs, edge cases handled.

| Score | Criteria |
|---|---|
| 0-2 | Code doesn't compile or has fundamental logic errors |
| 3-4 | Compiles but has runtime bugs or misses core requirements |
| 5-6 | Core functionality works, but edge cases cause failures |
| 7-8 | All requirements met, edge cases handled, no runtime errors |
| 9-10 | Perfect — all requirements, edge cases, and interaction with existing code handled correctly |

#### Rubric 2: Robustness (0-10)

Defensive programming: how does the code handle unexpected state?

| Score | Criteria |
|---|---|
| 0-2 | No defensive programming. Null dereferences, unvalidated inputs, silent failures. |
| 3-4 | Some error handling but inconsistent. Mix of throws and silent consumption. |
| 5-6 | Most error paths handled, but a few silent null consumptions or unvalidated inputs remain. |
| 7-8 | Consistent defensive programming. Nulls throw with context messages. Inputs validated at boundaries. Deserialization checked. |
| 9-10 | Perfect — every failure mode is explicit. Throw messages identify what's missing and where. Optional vs required null paths are deliberate and distinguishable. |

#### Rubric 3: Readability (0-10)

How quickly can a human understand what this code does?

| Score | Criteria |
|---|---|
| 0-2 | Abbreviated names, unclear control flow, misleading or excessive comments. |
| 3-4 | Mix of descriptive and cryptic names. Some methods are clear, others require tracing. |
| 5-6 | Mostly readable. Names are descriptive but a few abbreviations or ambiguous identifiers remain. Control flow is straightforward. |
| 7-8 | Clear throughout. Every identifier communicates intent. Booleans read as questions. Collections named by contents. Methods describe their action. Comments only where why is non-obvious. |
| 9-10 | Perfect — code is self-documenting. A developer unfamiliar with the project can understand each method without external context. File organization is logical and consistent. |

**Readability sub-components (for detailed analysis):**
- Naming: variable, method, and class name quality
- Structure: file organization, method length, control flow clarity
- Comments: appropriate (why-comments) vs inappropriate (what-comments, redundant)
- Abstraction level: neither too clever nor too verbose

#### Rubric 4: Architecture (0-10)

Structural quality: separation of concerns, dependency direction, pattern usage.

| Score | Criteria |
|---|---|
| 0-2 | God classes. Everything in one file/layer. Tight coupling everywhere. |
| 3-4 | Some separation but responsibilities bleed across classes. Dependencies point in mixed directions. |
| 5-6 | Single responsibility mostly followed. Layer boundaries exist but have shortcuts. |
| 7-8 | Clean separation. Each class has one reason to change. Dependencies point downward (toward data/abstractions). Appropriate patterns used without over-engineering. |
| 9-10 | Perfect — new code lands in the correct layer. Interfaces used for cross-layer communication. Open for extension without modification. No unnecessary abstractions. |

#### Rubric 5: Domain Correctness (0-10)

Unity/Netcode-specific knowledge applied correctly.

| Score | Criteria |
|---|---|
| 0-2 | Fundamental engine misunderstandings. NetworkVariable access before spawn. Missing base calls. Wrong lifecycle method. |
| 3-4 | Basic lifecycle correct but edge cases missed. Subscription/unsubscription mismatched. |
| 5-6 | Lifecycle ordering correct. Event cleanup present. But misses edge cases like initial value handling or late-join scenarios. |
| 7-8 | All lifecycle rules followed. Edge cases handled. Serialization validated. Event pairs matched. Editor/runtime separation correct. |
| 9-10 | Perfect — lifecycle, ownership, event cleanup, serialization validation, performance patterns, and edge cases all handled. Code demonstrates deep engine understanding. |

**Domain-specific checks:**
- OnNetworkSpawn vs Awake for NetworkVariable access
- base.OnNetworkSpawn() / base.OnNetworkDespawn() calls present
- OnValueChanged initial value handling
- Event +=/-= lifecycle pairing
- No GetComponent/Find in Update
- No allocations in hot paths
- #if UNITY_EDITOR guards on UnityEditor references
- Correct WritePerm/ReadPerm on NetworkVariables

#### Rubric 6: Test Quality (0-10)

Coverage, tier classification, assertion quality.

| Score | Criteria |
|---|---|
| 0-2 | No tests, or tests that assert nothing meaningful. |
| 3-4 | Some tests exist but poor coverage. Happy path only. Wrong tier (PlayMode test for pure logic). |
| 5-6 | Happy path covered. AAA pattern used. Correct tier classification. But error paths and edge cases untested. |
| 7-8 | Good coverage. EditMode for pure logic, PlayMode for MonoBehaviour. Edge cases tested. Null-throw behavior verified. Descriptive test names. |
| 9-10 | Thorough — happy path, error paths, boundary values, null-throw verification, serialization edge cases. Test names describe scenario and expectation. Setup/teardown clean. |

#### Rubric 7: Efficiency (Objective Metrics)

Not scored 0-10. Recorded as raw numbers.

| Metric | How to measure |
|---|---|
| **User corrections** | Count of times the user had to redirect, correct, or clarify during the task |
| **Compilation errors** | Count of compilation errors the agent encountered |
| **Self-fix rate** | Percentage of compilation errors the agent fixed without user intervention |
| **Test failures** | Count of test failures encountered during TDD |
| **Total iterations** | Number of edit-compile-test cycles before task completion |
| **Context retention delta** | (Task 7 only) Rubric score difference between step 1 and step 3 |
| **Memory application** | (Task 8 only) Binary — did the agent handle the known pitfall correctly in session 2? |

### Scoring Agent Protocol

The scoring agent is a separate Claude session. It receives:

1. **System prompt:** The 7 rubrics above, instructions to evaluate blind, and the directive to provide one-sentence justifications per score.

2. **Per-task input:**
   - Task description (the exact prompt given to both configurations)
   - Starter project snapshot (all .cs files before the task)
   - "Output A" — all .cs files after the task (configuration randomly assigned)
   - "Output B" — all .cs files after the task (the other configuration)
   - Conversation logs for both (for efficiency metrics)

3. **Per-task output:**
   - Rubric 1-6 scores for Output A
   - Rubric 1-6 scores for Output B
   - One-sentence justification per score
   - Specific violations flagged with file:line references
   - Rubric 7 metrics for both
   - Head-to-head verdict: A wins / B wins / tie, with margin and reasoning

4. **Blinding protocol:**
   - For each task, randomly assign harness → A or harness → B (coin flip)
   - The scoring agent is never told which configuration produced which output
   - Randomization record is kept separately and only revealed during analysis

## Analysis Plan

After all 8 tasks are scored:

### Primary Analysis

1. **Overall score comparison** — average of rubrics 1-6 for harness vs baseline
2. **Per-rubric delta** — which dimensions show the largest harness advantage (or disadvantage)
3. **Per-category breakdown:**
   - Feature implementation (tasks 1-2): does the harness help plan and build new code?
   - Bug fix (tasks 3-4): does the harness help diagnose and fix correctly?
   - Refactor (tasks 5-6): does the harness produce cleaner refactored code?
   - Multi-step (tasks 7-8): does the harness maintain quality over time?

### Hypotheses (pre-registered)

Before running the experiment, we predict:

1. **Robustness** will show the largest delta — enforcement rules directly target defensive programming
2. **Domain Correctness** will show a significant delta — the domain doc contains specific pitfall knowledge
3. **Readability** will show a moderate delta — naming enforcement helps but readability has many components
4. **Correctness** will show the smallest delta — both configurations use the same model; basic functionality shouldn't differ much
5. **Context Retention** (task 7) will favor the harness — context engineering features prevent quality degradation
6. **Memory Application** (task 8) will be binary: harness applies knowledge, baseline re-discovers the bug

### Statistical Note

8 tasks is a small sample. Results are directional indicators, not statistically significant conclusions. The analysis should:
- Note which findings are strong signals (consistent across tasks) vs noise (one task outlier)
- Use per-rubric variance to assess reliability
- Acknowledge that the same model powers both configurations — differences are attributable to scaffolding, not capability

### Open Benchmark Formalization

If results show meaningful differences, this experiment can be formalized as an open benchmark:

**Stable core (any domain):**
- Rubrics 1-4 (Correctness, Robustness, Readability, Architecture) + Rubric 6 (Test Quality)
- Paired comparison methodology
- Blind scoring protocol
- Task categories: feature, bugfix, refactor, multi-step

**Domain-specific module (swappable):**
- Rubric 5 (Domain Correctness) — swap Unity/Netcode checks for web/mobile/embedded
- Starter project — swap HarnessTestBed for a domain-appropriate skeleton
- Landmines — domain-specific anti-patterns planted in the starter code

**Harness evaluation extension (optional):**
- Rubric 7 (Efficiency metrics)
- Cross-session memory test (task 8 pattern)
- Context retention test (task 7 pattern)

## Execution Checklist

- [ ] Build HarnessTestBed Unity project with starter skeleton
- [ ] Plant 5 deliberate landmines in starter code
- [ ] Verify project compiles and runs in both copies
- [ ] Install harness in HarnessTestBed-H
- [ ] Write bare CLAUDE.md in HarnessTestBed-B
- [ ] Prepare randomization sequence for A/B assignment
- [ ] Prepare scoring agent system prompt with rubrics
- [ ] Run tasks 1-6 (paired, project reset between tasks)
- [ ] Run task 7 (three-feature sprint, no reset between steps)
- [ ] Run task 8 session 1 (both configurations)
- [ ] Run task 8 session 2 (both configurations, same project state)
- [ ] Collect all outputs and conversation logs
- [ ] Run scoring agent on all 8 task pairs
- [ ] Compile analysis
- [ ] Write findings report
