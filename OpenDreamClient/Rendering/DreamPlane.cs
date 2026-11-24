using OpenDreamShared.Dream;
using Robust.Client.Graphics;

namespace OpenDreamClient.Rendering;

internal sealed class DreamPlane(IRenderTexture mainRenderTarget) : IDisposable {
    /// <summary>
    /// The plane after its master's color and filters are applied
    /// </summary>
    public IRenderTexture RenderTarget => _needsMasterPass ? _masterRenderTarget! : mainRenderTarget;

    public RendererMetaData? Master;
    public bool Enabled = true;

    /// <summary>
    /// Whether there's nothing to see on this plane this frame
    /// </summary>
    public bool IsEmpty;

    /// <summary>
    /// Whether this plane is part of what ends up on the screen this frame
    /// </summary>
    public bool IsNeeded;

    /// <summary>
    /// Whether this gets drawn to the screen instead of only being the render_source of something else
    /// </summary>
    public bool DrawsToScreen => Master?.RenderTarget?.StartsWith('*') is not true;

    public readonly List<RendererMetaData> Sprites = new();

    private const int MaskInverse = 1; // The MASK_INVERSE define

    private IRenderTexture? _masterRenderTarget;
    private bool _needsMasterPass;

    // Render targets get cleared when they're drawn to, but there's no need to clear one that's already empty
    private bool _mainIsClear, _masterIsClear;

    public void Clear() {
        Master = null;
        Sprites.Clear();
        _needsMasterPass = false;
    }

    public void Dispose() {
        mainRenderTarget.Dispose();
        _masterRenderTarget?.Dispose();
        _masterRenderTarget = null;
        Clear();
    }

    /// <summary>
    /// Sets this plane's main render target<br/>
    /// Persists through calls to <see cref="Clear()"/>
    /// </summary>
    public void SetMainRenderTarget(IRenderTexture renderTarget) {
        mainRenderTarget.Dispose();
        mainRenderTarget = renderTarget;
        _mainIsClear = false;

        _masterRenderTarget?.Dispose();
        _masterRenderTarget = null;
    }

    /// <summary>
    /// Sets the plane master until the next <see cref="Clear()"/>, for one with a render_target
    /// </summary>
    /// <param name="createRenderTarget">Creates a render target with the size of the viewport</param>
    /// <returns>The render target that'll contain the plane with the master's color and filters applied</returns>
    public IRenderTexture SetMasterWithTarget(RendererMetaData master, Func<IRenderTexture> createRenderTarget) {
        Master = master;

        // Without any of these, the second pass would only be making a copy
        _needsMasterPass = master.MainIcon?.Appearance?.Filters.Length > 0 ||
                           !master.ColorMatrixToApply.Equals(ColorMatrix.Identity) ||
                           !master.AlphaToApply.Equals(1f) ||
                           master.ColorToApply != Color.White;
        if (_needsMasterPass && _masterRenderTarget == null) {
            _masterRenderTarget = createRenderTarget();
            _masterIsClear = false;
        }

        return RenderTarget;
    }

    /// <summary>
    /// Determine whether there's anything to see on this plane, going by which render sources are currently known to be empty
    /// </summary>
    public bool HasVisibleContents(DreamViewOverlay overlay) {
        if (!Enabled)
            return false;

        if (Master != null) {
            if (Master.AlphaToApply <= 0f)
                return false;

            // Masking with something that's empty leaves nothing
            foreach (var filter in Master.MainIcon?.Appearance?.Filters ?? []) {
                if (filter is DreamFilterAlpha alphaFilter && (alphaFilter.Flags & MaskInverse) == 0 &&
                    overlay.IsEmptyRenderSource(alphaFilter.RenderSource))
                    return false;
            }
        }

        foreach (var sprite in Sprites) {
            if (!overlay.IsEmptyRenderSource(sprite))
                return true;
        }

        return CanMasterDrawEmpty();
    }

    /// <summary>
    /// Clears this plane's render target, then draws all the plane's icons onto it
    /// </summary>
    public void Draw(DreamViewOverlay overlay, DrawingHandleWorld handle, Box2 worldAABB) {
        if (!IsNeeded)
            return;

        if (!IsEmpty || !_mainIsClear) {
            // Draw all icons
            handle.RenderInRenderTarget(mainRenderTarget, () => {
                if (IsEmpty) // Inside the RenderInRenderTarget() to ensure it gets cleared
                    return;

                foreach (var sprite in Sprites) {
                    if (overlay.IsEmptyRenderSource(sprite))
                        continue;

                    var positionOffset = -worldAABB.BottomLeft;

                    if (sprite.HasRenderSource && overlay.RenderSourceLookup.TryGetValue(sprite.RenderSource!, out var renderSourceTexture)) {
                        sprite.TextureOverride = renderSourceTexture.Texture;
                    }

                    overlay.DrawIcon(handle, mainRenderTarget.Size, sprite, positionOffset);
                }
            }, new Color());

            _mainIsClear = IsEmpty;
        }

        if (_needsMasterPass && (!IsEmpty || !_masterIsClear)) {
            // Draw again, but with the color applied
            handle.RenderInRenderTarget(_masterRenderTarget!, () => {
                if (Master == null || IsEmpty)
                    return;

                Master.TextureOverride = mainRenderTarget.Texture;
                var texture = Master.GetTexture(overlay, handle);
                Master.TextureOverride = null;
                if (texture == null)
                    return;

                var color = Master.ColorToApply;
                color.A *= Master.AlphaToApply;

                handle.UseShader(overlay.GetBlendAndColorShader(Master, useOverlayMode: true));
                handle.SetTransform(DreamViewOverlay.CreateRenderTargetFlipMatrix(_masterRenderTarget!.Size, Master.MainIcon?.TextureRenderOffset ?? Vector2.Zero));
                handle.DrawTextureRect(texture, new Box2(Vector2.Zero, texture.Size), color);
            }, new Color());

            _masterIsClear = IsEmpty;
        }
    }

    /// <summary>
    /// Whether the plane master's color or filters would result in something visible on an empty plane
    /// </summary>
    private bool CanMasterDrawEmpty() {
        if (Master == null)
            return false;

        if (Master.ColorMatrixToApply.GetOffsetVector() != default)
            return true;

        foreach (var filter in Master.MainIcon?.Appearance?.Filters ?? []) {
            if (filter is DreamFilterLayer or DreamFilterColor or DreamFilterRays)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Draws this plane's mouse map onto the current render target
    /// </summary>
    public void DrawMouseMap(DrawingHandleWorld handle, DreamViewOverlay overlay, Vector2i renderTargetSize, Box2 worldAABB) {
        if (Master?.MouseOpacity == MouseOpacity.Transparent || !Enabled)
            return;

        handle.UseShader(overlay.BlockColorInstance);
        foreach (var sprite in Sprites) {
            if (sprite.MouseOpacity == MouseOpacity.Transparent || sprite.ShouldPassMouse)
                continue;

            var texture = sprite.MainIcon?.LastRenderedTexture;
            if (texture == null)
                continue;

            // For mouse_opacity = 2, discard transparency but keep the size.
            var textureSize = texture.Size;
            if (sprite.MouseOpacity == MouseOpacity.Opaque) {
                texture = Texture.White;
            }

            var posOffset = -worldAABB.BottomLeft + sprite.RenderPosOffset;

            var pos = (sprite.Position + posOffset) * overlay.IconSize;
            if (sprite.MainIcon != null)
                pos += sprite.MainIcon.TextureRenderOffset;

            int hash = sprite.GetHashCode();
            var colorR = (byte)(hash & 0xFF);
            var colorG = (byte)((hash >> 8) & 0xFF);
            var colorB = (byte)((hash >> 16) & 0xFF);
            Color targetColor = new Color(colorR, colorG, colorB); //TODO - this could result in mis-clicks due to hash-collision since we ditch a whole byte.
            overlay.MouseMapLookup[targetColor] = sprite;
            handle.SetTransform(DreamViewOverlay.CalculateDrawingMatrix(sprite.TransformToApply, pos, textureSize, renderTargetSize));
            handle.DrawTextureRect(texture, new Box2(Vector2.Zero, textureSize), targetColor);
        }
    }
}
