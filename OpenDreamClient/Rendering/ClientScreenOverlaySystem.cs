using OpenDreamShared.Dream;
using OpenDreamShared.Rendering;
using Robust.Client.Player;

namespace OpenDreamClient.Rendering;

internal sealed partial class ClientScreenOverlaySystem : SharedScreenOverlaySystem {
    public readonly HashSet<EntityUid> ScreenObjects = new();

    /// <summary>
    /// Keeps a misplaced screen object from creating an enormous viewport
    /// </summary>
    private const int MaxBorder = 32;

    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private DMISpriteSystem _spriteSystem = default!;

    public override void Initialize() {
        SubscribeNetworkEvent<AddScreenObjectEvent>(OnAddScreenObject);
        SubscribeNetworkEvent<RemoveScreenObjectEvent>(OnRemoveScreenObject);
    }

    public override void Shutdown() {
        ScreenObjects.Clear();
    }

    private void OnAddScreenObject(AddScreenObjectEvent e) {
        EntityUid ent = _entityManager.GetEntity(e.ScreenObject);
        ScreenObjects.Add(ent);
    }

    private void OnRemoveScreenObject(RemoveScreenObjectEvent e) {
        EntityUid ent = _entityManager.GetEntity(e.ScreenObject);
        ScreenObjects.Remove(ent);
    }

    /// <summary>
    /// Calculates how far the screen needs to extend past the view in order to fit every screen object
    /// </summary>
    public ViewBorders CalculateBorders(ViewRange view, int iconSize) {
        var borders = new ViewBorders();
        if (_playerManager.LocalSession?.AttachedEntity is not { } eye || iconSize <= 0)
            return borders;

        var seeVis = _entityManager.TryGetComponent(eye, out DreamMobSightComponent? mobSight) ? mobSight.SeeInvisibility : 127;

        foreach (EntityUid uid in ScreenObjects) {
            if (!_entityManager.TryGetComponent(uid, out DMISpriteComponent? sprite) || sprite.ScreenLocation == null)
                continue;
            if (sprite.ScreenLocation.MapControl != null || sprite.Icon.Appearance == null)
                continue;
            if (!_spriteSystem.IsVisible(sprite, null, seeVis, null))
                continue;

            var iconTiles = Vector2i.One;
            if (sprite.Icon.DMI != null && (sprite.Icon.Appearance.AppearanceFlags & AppearanceFlags.TileBound) == 0) {
                iconTiles = new Vector2i(
                    Math.Max(1, (sprite.Icon.DMI.IconSize.X + iconSize - 1) / iconSize),
                    Math.Max(1, (sprite.Icon.DMI.IconSize.Y + iconSize - 1) / iconSize));
            }

            sprite.ScreenLocation.ExpandBorders(view, iconTiles, ref borders);
        }

        return new ViewBorders(
            Math.Min(borders.Left, MaxBorder), Math.Min(borders.Right, MaxBorder),
            Math.Min(borders.Bottom, MaxBorder), Math.Min(borders.Top, MaxBorder));
    }
}
