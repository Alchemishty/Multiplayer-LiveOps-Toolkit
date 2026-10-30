# Coding conventions

These standards apply to new and modified code. See [architecture](architecture.md) for ownership and assembly boundaries, and [enforcement](../enforcement/) for review rules.

## Dependencies and responsibility

Keep business rules in the engine-free mechanic assemblies. Unity components own scene integration, rendering, network lifecycle or UI; Django adapters own HTTP/JSON mapping. Depend on focused service/backend interfaces. Do not let package runtime reference demo code, TMP or uGUI.

Use composition and existing owners. Add abstractions only for an implemented consumer and identify the responsibility they replace. Bug fixes should address the root cause with minimal changes; avoid unrelated restructuring.

## Null and state handling

Throw ArgumentNullException for required null arguments and InvalidOperationException for broken configuration or invalid lifecycle use. Diagnostics must identify the missing dependency and its owner. Required scene references and required cosmetic mappings must fail explicitly.

Absence can be valid: an optional integration, a snapshot not yet loaded, or an avatar not yet spawned. Guard those states deliberately. Null-conditional callbacks are appropriate only when the callback is optional. Validate deserialized objects and their complete shape before publishing state. Never publish partial account snapshots or consume a malformed response silently.

## Names and serialization

Use PascalCase for types/methods/properties, camelCase without prefixes for private and serialized fields, UPPER_SNAKE_CASE for compile-time constants, and PascalCase for static readonly fields. Interfaces start with I.

Names must describe their contents and purpose: equippedItemsBySlot, playerSession, isLoading, hasSnapshot. Avoid abbreviations except established terms such as id, url and api.

Preserve Unity .meta files and GUIDs. Use FormerlySerializedAs when renaming serialized fields. Update authored scene/prefab wiring alongside code changes. JSON DTO names must match their wire contracts explicitly, using JsonProperty where mapping is needed. Immutable service snapshots are distinct from mutable transport DTOs and authored configuration assets.

## Lifetimes and asynchronous work

Use IEnumerator operations with explicit completion callbacks, consistent with the existing modules and adapters. Durable account owners drive and dispose operations; hiding a panel must not cancel a purchase or claim. Dispose an unstarted reserved iterator if a synchronous observer ended its owner before coroutine startup.

Deliver completion once, reject stale-account responses and clear private state on shutdown. Uncertain mutations require authoritative refresh; never automatically replay a mutation. Session renewal and bounded authenticated dispatch belong to DjangoPlayerSession, through injected IAuthenticatedSession contracts.

Pair event subscriptions with cleanup in the same lifetime. NetworkVariable subscriptions belong in OnNetworkSpawn/OnNetworkDespawn, with explicit initial-state reads. Call base lifecycle methods. Keep all NetworkBehaviours on the prefab before spawn.

## Visuals and authority

CosmeticRenderer implements IEquipmentRenderer and owns per-avatar pools, slot bindings and mesh restoration. EquipmentVisualPresenter selects local preview or canonical presentation. PlayerAccessory publishes only the verification gate's approved equipment snapshot; previews never write network state.

The supplied movement and animation are owner-authoritative through ClientNetworkTransform and ClientNetworkAnimator. Account mechanics remain backend-authoritative. Never replicate credentials, wallets, full inventories or private progress.

## Performance

Cache components and lookups outside per-frame loops. Avoid allocations and repeated hierarchy searches in hot paths. Use dictionary lookups and pools where they serve current consumers. Cache Animator.StringToHash values as static readonly fields. Keep routine development logging out of builds; retain explicit actionable failure diagnostics without credentials or raw assertions.

## Verification and review

Every code change needs appropriate regression coverage: engine-free logic in EditMode, Unity integration in PlayMode, backend mechanics in Django tests. See [testing](testing.md). Refresh Unity and inspect the console after code changes. Review changed code against enforcement rules and fix safe adjacent violations.

Saved demo assets are authoritative. After demo scripts/assets change, synchronize Full Demo with tools/sync_full_demo.py and verify with --check. Scene generators are not part of the project.
