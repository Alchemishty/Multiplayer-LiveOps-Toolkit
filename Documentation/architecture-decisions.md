# Architecture decisions

These choices describe how the toolkit handles account state, multiplayer and asynchronous work. Each has alternatives that suit different needs. Replaceable backends, selectable modules and custom UI are toolkit capabilities; they are described in the [integration guide](package-integration.md).

## 1. Separate account authority from gameplay authority

**Choice.** Persistent account mechanics are backend-authoritative. The demo uses client-authoritative movement and animation; it does not run an authoritative gameplay simulation.

**Alternative.** Let a trusted simulation server control movement/combat and supply the evidence used by account mechanics.

**Why.** The toolkit combines persistent account mechanics with host/client and dedicated-server sessions. Public identity and equipment validation are independent of how a game simulates movement or combat. Keeping those responsibilities separate allows the account toolkit to integrate with the game's own gameplay networking.

**Tradeoff.** Backend approval protects the covered account operations; it does not establish that movement, combat, scores or match results are legitimate. A game needing authoritative gameplay must supply that simulation and its trusted evidence path. The demo's movement model is an example integration, not a requirement for consumers.

## 2. Clients perform account operations directly

**Choice.** Each player's client authenticates and performs its own account operations with the backend. The host or dedicated server verifies public identity/equipment for its network session.

**Alternative.** Route account reads and commands through the game server, which acts as the gateway to the account backend.

**Why.** This keeps ownership explicit: private account work belongs to each player, while the network server owns peer approval and public replication. A server-only session needs neither a local player account nor another player's account credentials.

**Tradeoff.** Account state and session-visible state have separate update paths. A confirmed equipment change must also pass the network verification/publication flow. A game-server gateway can coordinate account actions with authoritative match rules more directly; the current toolkit supplies no such gameplay orchestration.

## 3. Verify signed assertions locally at the host/server

**Choice.** The backend issues short-lived identity/equipment assertions. Hosts and dedicated servers verify them using provisioned public trust, bound to the peer's session and fresh challenge.

**Alternative.** Have the game server contact an authenticated backend verification endpoint for each approval or equipment publication.

**Why.** Public verification material can be distributed to player hosts without distributing a signing secret or privileged account access. The network server can verify an issued assertion locally.

**Tradeoff.** Signing keys, public trust distribution, rotation, clocks and proof expiry require configuration. Local verification does not itself observe an account revocation or equipment change after issuance; fresh proofs establish later state. The client still needs backend access to obtain proofs. This model also cannot make a malicious player host trustworthy.

## 4. Keep equipment preview local and confirm persistent/public changes

**Choice.** Preview is immediate and local. Equipment mutations persist through the backend; only verified canonical equipment enters multiplayer public state. Revision checks prevent stale writes/publications.

**Alternative.** Optimistically publish equipment and reconcile it after backend approval, or persist each preview selection.

**Why.** Browsing a look and committing it to an account serve different purposes. A player can try a look without changing the account or other players' views.

**Tradeoff.** Equip confirmation and remote visibility require request/proof round trips. Optimistic publication could feel faster, but would need rollback and could briefly show equipment that was rejected. Revision conflicts require refreshing rather than silently overwriting newer equipment.

## 5. Evaluate achievements from backend-observable facts

**Choice.** The current backend evaluates supported ownership, attendance and purchase/spend facts. Clients request reads/claims; they do not submit arbitrary trusted progress counts. Completion and reward claiming are separate.

**Alternative.** Evaluate gameplay events on the client and persist reported progress, or build an authoritative gameplay-event ingestion pipeline.

**Why.** Ownership, attendance and purchase/spend facts are already available to the backend. They can justify these achievements without trusting arbitrary client progress or adding a gameplay-event ingestion service.

**Tradeoff.** Client evaluation is flexible and immediate but cannot independently justify valuable grants. A trusted event pipeline supports match-based achievements but needs a trusted producer and additional infrastructure. Current achievements therefore do not establish arbitrary kills, wins or scores from gameplay.

## 6. Refresh uncertain mutations instead of automatically replaying them

**Choice.** An unconfirmed mutation produces an unknown-outcome state and requires authoritative refresh before further affected actions. The toolkit does not automatically repeat the mutation.

**Alternative.** Replay requests using backend-supported idempotency keys, durable receipts and reconciliation.

**Why.** The Django backend is a small reference implementation, leaving receipt/history and game-specific idempotency policies to consuming games. A missing response cannot establish that the backend failed to commit.

**Tradeoff.** Recovery can require another read and renewed player intent. Durable deduplicated operations can provide stronger completion tracking and smoother retries, but require backend storage, retention and reconciliation policies beyond the current sample. A refresh confirms current state; it is not a transaction receipt.

## 7. Use a shared backend day and reject rollover claims

**Choice.** Daily Rewards uses the backend's UTC date and requires the claim to identify the date shown to the player. A changed backend day requires a calendar reload.

**Alternative.** Reset per player timezone, use elapsed time since the last claim, or automatically claim whichever day is current when the request arrives.

**Why.** A monthly calendar with backend-owned eligibility and explicit displayed-date claims keeps the displayed action tied to its intended day.

**Tradeoff.** UTC midnight does not coincide with every player's local midnight. A stale calendar needs another read before claiming. Local-day or rolling-cooldown rewards are different mechanics and would require corresponding backend eligibility rules.

## 8. Retain coroutines as the asynchronous execution model

**Choice.** Use IEnumerator operations with callbacks and explicit ownership/disposal, consistent with the existing Unity integration.

**Alternative.** Migrate to Task-based async/await or adopt a Unity-specific async library.

**Why.** The project retained its existing coroutine model for consistency, avoiding a migration and two competing execution models. This does not mean that standard async/await requires an extra library.

**Tradeoff.** Async return values and composition can be clearer with async/await. Coroutines require explicit completion, nested operation driving and cancellation cleanup; durable owners must keep pending account work independent of view lifetime.

## Further reading

- [System overview](../README.md#how-it-works)
- [Account and avatar integration](../Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/full-demo.md)
- [Identity/equipment verification protocol](multiplayer-identity-verification.md)
- [Backend configuration and limits](backend-operations.md)
