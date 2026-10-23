# Enforcement: Serialized Field Renames

**Rule:** When renaming a `[SerializeField]` field, always add `[FormerlySerializedAs("oldName")]` to preserve serialized data.

## Why

Unity serializes `[SerializeField]` fields by name. Renaming a field without `FormerlySerializedAs` silently breaks every scene, prefab, and ScriptableObject asset that references the old name. The field appears as default/null in the inspector with no error — a silent data loss bug.

## Violations

### Renamed serialized field without migration attribute
```csharp
// BEFORE
[SerializeField] private GameObject itemPrefab;

// VIOLATION — renamed without FormerlySerializedAs
[SerializeField] private GameObject itemCardPrefab;
// Every scene/prefab referencing "itemPrefab" now has a null reference

// REQUIRED
[FormerlySerializedAs("itemPrefab")]
[SerializeField] private GameObject itemCardPrefab;
```

### Multiple renames over time
```csharp
// REQUIRED — chain preserves all historical names
[FormerlySerializedAs("prefab")]
[FormerlySerializedAs("itemPrefab")]
[SerializeField] private GameObject itemCardPrefab;
```

## Scope

Applies to any field with:
- `[SerializeField]` attribute
- `public` visibility on a MonoBehaviour or ScriptableObject (implicitly serialized)

Does NOT apply to:
- Private fields without `[SerializeField]`
- Fields on pure C# classes (not serialized by Unity)
- Fields marked `[NonSerialized]`

## Checking

For every renamed field in the diff, verify:
1. Was it serialized (SerializeField, or public on MonoBehaviour/ScriptableObject)?
2. If yes, is `[FormerlySerializedAs("oldName")]` present?
3. If the attribute is missing, flag as violation.
