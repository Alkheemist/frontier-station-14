using Content.Shared.Materials;
using Content.Shared.Stacks;
using Content.Server.Stack;
using Robust.Client.GameObjects;

namespace Content.Client.Materials;

public sealed class MaterialStorageSystem : SharedMaterialStorageSystem
{
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MaterialStorageComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnAppearanceChange(EntityUid uid, MaterialStorageComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!args.Sprite.LayerMapTryGet(MaterialStorageVisualLayers.Inserting, out var layer))
            return;

        if (!_appearance.TryGetData<bool>(uid, MaterialStorageVisuals.Inserting, out var inserting, args.Component))
            return;

        if (inserting && TryComp<InsertingMaterialStorageComponent>(uid, out var insertingComp))
        {
            args.Sprite.LayerSetAnimationTime(layer, 0f);

            args.Sprite.LayerSetVisible(layer, true);
            if (insertingComp.MaterialColor != null)
                args.Sprite.LayerSetColor(layer, insertingComp.MaterialColor.Value);
        }
        else
        {
            args.Sprite.LayerSetVisible(layer, false);
        }
    }

    public override bool TryInsertMaterialEntity(EntityUid user,
        EntityUid toInsert,
        EntityUid receiver,
        MaterialStorageComponent? storage = null,
        MaterialComponent? material = null,
        PhysicalCompositionComponent? composition = null)
    {
        // Start Frontier: Automatically split stacks if it doesn't fit

        // If the whole stack fits, great
        if (base.TryInsertMaterialEntity(user, toInsert, receiver, storage, material, composition))
        {
            _transform.DetachEntity(toInsert, Transform(toInsert));
            return true;
        }

        // Check if the reason it failed is because the stack is too large and split the stack accordingly
        if (!TryComp<StackComponent>(toInsert, out var stack) && storage is not null && storage.StorageLimit is not null)
        {
            var availableVolume = storage.StorageLimit - GetTotalMaterialAmount(receiver, storage);
            var maxSheets = availableVolume / 100; // TODO: Programatically figure out how much material is in each sheet instead of magic numbers

            // Partial stack is needed, split off what we need, ensure the new entry is moved.
            EntityUid splitStack = _stack.Split(toInsert, maxSheets, Transform(toInsert).Coordinates, stack) ?? EntityUid.Invalid;

            if (splitStack == EntityUid.Invalid)
                return false;

            if (base.TryInsertMaterialEntity(user, splitStack, receiver, storage, material, composition))
            {
                return true;
            }
        }
        return false;
        // End Frontier: Automatically split stacks if it doesn't fit
    }
}

public enum MaterialStorageVisualLayers : byte
{
    Inserting
}
