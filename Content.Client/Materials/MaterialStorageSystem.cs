using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Client.GameObjects;

namespace Content.Client.Materials;

public sealed class MaterialStorageSystem : SharedMaterialStorageSystem
{
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
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
        if (TryComp<StackComponent>(toInsert, out var stack) &&
            storage is not null &&
            storage.StorageLimit is not null)
        {
            if (!Resolve(toInsert, ref material, ref composition, false))
                return false;

            var availableVolume = (int)storage.StorageLimit - GetTotalMaterialAmount(receiver, storage);
            var materialPerSheet = 0;
            foreach (var (_, vol) in composition.MaterialComposition)
            {
                materialPerSheet += vol;
            }
            var maxSheets = availableVolume / materialPerSheet;

            // Partial stack is needed, split off what we need, ensure the new entry is moved.
            if (!_stack.Use(toInsert, maxSheets, stack))
                return false;

            foreach (var (mat, vol) in composition.MaterialComposition)
            {
                TryChangeMaterialAmount(receiver, mat, vol * maxSheets, storage);
            }
            return true;
        }
        return false;
        // End Frontier: Automatically split stacks if it doesn't fit
    }
}

public enum MaterialStorageVisualLayers : byte
{
    Inserting
}
