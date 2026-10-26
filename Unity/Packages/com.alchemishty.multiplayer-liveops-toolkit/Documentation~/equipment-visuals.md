# Equipment visuals

The optional `MultiplayerLiveOpsToolkit.EquipmentVisuals` assembly supplies mesh attachments, skinned cosmetics, base-mesh replacement/restoration, per-avatar pooling and preview presentation. It references Equipment and Unity, with no UI, Django, Netcode or demo dependency. Headless servers need not create a renderer. Implement `IEquipmentRenderer` to replace the supplied rendering strategy.

## Configure a character

1. Add `CosmeticRenderer` to the character's root GameObject.
2. Create **LiveOps > Cosmetic Visual Catalog** from Unity's Create menu. For each cosmetic, assign its backend item ID, slot ID, prefab and attachment mode: `Bone` for an attached mesh, `Skinned` for a skin rebound to the avatar skeleton.
3. Assign the catalog. Add a slot binding for every visual slot: its backend string ID, attachment Transform and any base-mesh GameObjects to hide while occupied. Empty base objects means additive attachment; listing base meshes means replacement. Body replacement attachment can be the character root.
4. For skinned cosmetics assign `rigRoot`. Bones match by exact name by default. Use `boneAliases` to map source names to explicit avatar bones when names differ or are ambiguous. All skin bones/root bones must resolve; incompatible bindings fail explicitly before replacing the displayed look.

The model and cosmetics must have compatible bind poses, weights and geometry. Name remapping does not retarget arbitrary body proportions or generate clothing. Author offsets in the attachment Transform or children of the cosmetic prefab; the instantiated prefab root uses identity position/rotation/scale. The avatar's Animator drives rebound skins; Animators inside cosmetic instances are disabled.

## Display local equipment and preview

A durable avatar/game owner creates an `EquipmentVisualPresenter` with a renderer and a configuration-failure handler, then calls `Bind(equipmentService)`. It initially renders confirmed equipment and subscribes to state/preview changes. `equipmentService.Preview(itemIds)` displays a local override; `ClearPreview()` restores confirmed equipment. Preview is visual only; it does not grant or persist items.

```csharp
presentation = new EquipmentVisualPresenter(renderer, HandleVisualConfigurationFailure);
presentation.Bind(equipmentService);
// Dispose presentation on avatar teardown / disable; recreate and bind on re-enable.
```

The owner must dispose the presenter before destroying its renderer. Disposal unsubscribes and restores base visuals. A configuration-failure handler should report the diagnostic and terminate the affected game session according to the game's composition. It must not silently ignore missing content.

## Display multiplayer equipment

Call `presentation.ShowPublic(verifiedLoadout)` with the host/server-approved public state. For remote avatars no private service binding is needed. For the local avatar, bind its equipment service too: preview temporarily overrides the public look, but clearing preview restores the approved public loadout, even if account equipment has changed while network verification is still pending. The renderer itself does not authenticate loadouts; the network integration must supply verified state.

## Add content or replace the model

- **Accessory:** add its backend definition and local catalog ID/slot/prefab binding.
- **Set:** define its member IDs on the backend. A set uses the same individual visual bindings; no special set renderer or code is required.
- **Slot:** add a string slot ID and conflicts in backend Equipment configuration, then add an avatar slot binding and matching cosmetic definitions. The package uses no humanoid slot enum.
- **Character:** use the renderer on the replacement prefab with its rig, mount points and base meshes. The same catalog can be shared by compatible characters, with per-character aliases/bindings.
- **Runtime binding replacement:** dispose the presenter, call `renderer.ResetBindings()` before changing catalog/rig/slot references, then construct/bind a new presenter. Reset destroys cached instances and restores the previous base state.

Catalogs are shared configuration; instances and original base visibility belong to each avatar. Clear/disable deactivates pooled cosmetics and restores the captured base visibility; reset/destroy releases instances. Changing catalog definitions in place requires ResetBindings before rendering again. Slot conflicts and set validity remain Equipment/backend responsibilities, not renderer geometry inference.
