# Enforcement: Editor vs Runtime Separation

**Rule:** No `UnityEditor` references in runtime code without `#if UNITY_EDITOR` guards.

## Violations

### Direct UnityEditor using in runtime script
```csharp
// VIOLATION — Scripts/Player/PlayerAccessory.cs
using UnityEditor;

public class PlayerAccessory : NetworkBehaviour
{
    // This will fail to compile in builds
}

// REQUIRED — guard the reference
#if UNITY_EDITOR
using UnityEditor;
#endif

public class PlayerAccessory : NetworkBehaviour
{
    #if UNITY_EDITOR
    [CustomEditor(typeof(PlayerAccessory))]
    // editor-only code here
    #endif
}
```

### UnityEditor API call without guard
```csharp
// VIOLATION
public void SaveData()
{
    EditorUtility.SetDirty(this); // compile error in builds
}

// REQUIRED
public void SaveData()
{
    #if UNITY_EDITOR
    EditorUtility.SetDirty(this);
    #endif
}
```

## Scope

- All files in `Assets/Scripts/` (except `Assets/Scripts/Editor/` and `Assets/Editor/`)
- Files in `Editor/` folders are exempt — they only compile in the editor
- Assembly definitions with `Editor` platform-only are also exempt

## Checking

Scan all `.cs` files outside of `Editor/` directories for:
- `using UnityEditor` without a surrounding `#if UNITY_EDITOR`
- Any `UnityEditor.` qualified name without a surrounding `#if UNITY_EDITOR`
