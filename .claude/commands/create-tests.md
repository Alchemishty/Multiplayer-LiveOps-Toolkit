# Create Tests

Generate EditMode and PlayMode tests for existing code.

## Usage

Specify which files to generate tests for. Since there are no git operations, provide explicit file paths or patterns:
- `/create-tests Assets/Scripts/Data/ShopCatalog.cs`
- `/create-tests Assets/Scripts/Player/PlayerAccessory.cs`
- `/create-tests Assets/Scripts/API/` (all files in directory)

## Prerequisites

- Read `harness.yaml` for test commands
- Read `docs/testing.md` for test conventions and patterns
- Read `docs/conventions.md` for coding standards (null safety, naming)

## Workflow

### Step 1 — Classify Target Files

For each target file, classify into test tier:

| File Layer | Test Tier | Location |
|---|---|---|
| Data (enums, DTOs, structs) | EditMode | `Assets/Tests/EditMode/` |
| Pure C# logic (validators, helpers) | EditMode | `Assets/Tests/EditMode/` |
| ScriptableObject logic | EditMode | `Assets/Tests/EditMode/` |
| MonoBehaviour (no Netcode) | PlayMode | `Assets/Tests/PlayMode/` |
| NetworkBehaviour | PlayMode | `Assets/Tests/PlayMode/` |
| UI interaction | PlayMode | `Assets/Tests/PlayMode/` |

### Step 2 — Read Source Files

For each target file:
1. Read the full source
2. Identify public methods, properties, and constructors
3. Identify edge cases: null inputs, empty collections, boundary values, error paths
4. Identify what can be tested in isolation vs what needs Unity runtime

### Step 3 — Generate Tests

For each target file, create a corresponding test file:
- `ShopCatalog.cs` → `Assets/Tests/EditMode/ShopCatalogTests.cs`
- `PlayerAccessory.cs` → `Assets/Tests/PlayMode/PlayerAccessoryTests.cs`

**Test structure:**
```csharp
using NUnit.Framework;
// using UnityEngine.TestTools; // for PlayMode [UnityTest]

[TestFixture]
public class <ClassName>Tests
{
    [SetUp]
    public void SetUp()
    {
        // Create fresh test fixtures
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up spawned objects
    }

    [Test] // or [UnityTest] for PlayMode
    public void MethodName_Condition_ExpectedResult()
    {
        // Arrange
        // Act
        // Assert
    }
}
```

**Rules:**
- One concept per test method
- AAA pattern: Arrange, Act, Assert — clearly separated with blank lines
- Test method names: `MethodUnderTest_Condition_ExpectedResult`
- Test null-throw behavior: verify `InvalidOperationException` or `ArgumentNullException` is thrown when null safety rules apply
- No `Debug.Log` assertions — use NUnit `Assert` methods
- Descriptive variable names in tests too — `expectedAccessoryCount`, not `cnt`

### Step 4 — Verify Compilation

1. Run `refresh_unity`
2. Check `read_console` for zero errors
3. Fix any compilation issues

### Step 5 — Run Tests

1. Run `run_tests` (or mode-specific: `--testMode=EditMode` / `--testMode=PlayMode`)
2. If tests fail: read failure output, determine if it's a test bug or a source bug
   - Test bug → fix the test
   - Source bug → report to user (do not modify source without explicit approval)
3. Retry up to 3 times

### Step 6 — Report

Summarize:
- Files tested
- Tests created (count and names)
- EditMode vs PlayMode breakdown
- Any source bugs discovered
- Any untestable code identified (recommend extraction for testability)

## Ensure Test Directories Exist

Before writing test files, create directories if needed:
- `Assets/Tests/EditMode/`
- `Assets/Tests/PlayMode/`

Also ensure assembly definitions exist for test assemblies if the project uses asmdef files.
