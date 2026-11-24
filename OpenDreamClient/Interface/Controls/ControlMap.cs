using System.Diagnostics.CodeAnalysis;
using OpenDreamClient.Input;
using OpenDreamClient.Interface.Controls.UI;
using OpenDreamShared.Interface.Descriptors;
using OpenDreamShared.Interface.DMF;
using OpenDreamClient.Rendering;
using OpenDreamShared.Dream;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Graphics;
using Robust.Shared.Input;

namespace OpenDreamClient.Interface.Controls;

public sealed partial class ControlMap(ControlDescriptor controlDescriptor, ControlWindow window) : InterfaceControl(controlDescriptor, window) {
    public ScalingViewport Viewport { get; private set; }

    [Dependency] private IEntitySystemManager _entitySystemManager = default!;
    private MouseInputSystem? _mouseInput;
    private ClientAppearanceSystem? _appearanceSystem;

    private ControlDescriptorMap MapDescriptor => (ControlDescriptorMap)ElementDescriptor;

    private ClientObjectReference? _atomUnderMouse;

    private readonly Eye _eye = new();
    private Vector2i _reportedSize;
    private float _timeSinceSizeReport;

    protected override void UpdateElementDescriptor() {
        base.UpdateElementDescriptor();

        // Don't attempt to render any non-main viewports
        Viewport.Visible = MapDescriptor.IsDefault.Value;

        Viewport.StretchMode = MapDescriptor.ZoomMode.Value switch {
            "blur" => ScalingViewportStretchMode.Bilinear,
            "distort" => ScalingViewportStretchMode.Nearest,

            // TODO: "tries to keep the look of individual pixels,
            //          but will adjust to non-integer zooms (like 1.1x) by blending neighboring pixels"
            "normal" or _ => ScalingViewportStretchMode.Nearest
        };

        UpdateViewRange(InterfaceManager.View, InterfaceManager.Borders);
    }

    public void UpdateViewRange(ViewRange view, ViewBorders borders) {
        var viewWidth = Math.Max(view.Width, 1) + borders.Size.X;
        var viewHeight = Math.Max(view.Height, 1) + borders.Size.Y;

        Viewport.ViewportSize = new Vector2i(viewWidth, viewHeight) * InterfaceManager.IconSize;
        if (MapDescriptor.IconSize.Value != 0) {
            // BYOND supports a negative number here (flips the view), but we're gonna enforce a positive number instead
            var iconSize = Math.Max(MapDescriptor.IconSize.Value, 1);

            Viewport.SetWidth = iconSize * viewWidth;
            Viewport.SetHeight = iconSize * viewHeight;
        } else {
            // icon-size of 0 means stretch to fit the available space
            Viewport.SetWidth = float.NaN;
            Viewport.SetHeight = float.NaN;
        }
    }

    /// <summary>
    /// Called every frame, only on the default map
    /// </summary>
    public void Update(IEye eye, ViewBorders borders) {
        // The viewport is centered on the eye, but the view isn't centered in the viewport if its borders are uneven
        _eye.Position = eye.Position;
        _eye.Rotation = eye.Rotation;
        _eye.Zoom = eye.Zoom;
        _eye.DrawFov = eye.DrawFov;
        _eye.DrawLight = eye.DrawLight;
        _eye.Offset = eye.Offset + new Vector2(borders.Right - borders.Left, borders.Top - borders.Bottom) / 2f;
        Viewport.Eye = _eye;
    }

    /// <summary>
    /// Runs the on-size command if this has been resized
    /// </summary>
    /// <remarks>Resizing is continuous while a splitter is dragged, so this limits how often the command is run</remarks>
    public void UpdateOnSize(float frameTime) {
        _timeSinceSizeReport += frameTime;

        var size = (Vector2i)UIElement.Size;
        if (size == _reportedSize || _timeSinceSizeReport < 0.2f)
            return;

        _reportedSize = size;
        _timeSinceSizeReport = 0f;

        var command = ControlDescriptor.OnSize.AsRaw();
        if (string.IsNullOrWhiteSpace(command) || size.X <= 0 || size.Y <= 0)
            return;

        InterfaceManager.RunCommand(command
            .Replace("[[width]]", size.X.ToString())
            .Replace("[[height]]", size.Y.ToString()));
    }

    protected override Control CreateUIElement() {
        Viewport = new ScalingViewport { MouseFilter = Control.MouseFilterMode.Stop };
        Viewport.OnKeyBindDown += OnViewportKeyBindEvent;
        Viewport.OnKeyBindUp += OnViewportKeyBindEvent;
        Viewport.OnMouseMove += OnViewportMouseMoveEvent;
        Viewport.OnMouseExited += OnViewportMouseExitedEvent;
        Viewport.OnVisibilityChanged += (args) => {
            if (args.Visible) {
                OnShowEvent();
            } else {
                OnHideEvent();
            }
        };

        if(ControlDescriptor.IsVisible.Value)
            OnShowEvent();
        else
            OnHideEvent();

        UpdateViewRange(InterfaceManager.View, InterfaceManager.Borders);

        return new PanelContainer { StyleClasses = {"MapBackground"}, Children = { Viewport } };
    }

    private void OnViewportKeyBindEvent(GUIBoundKeyEventArgs e) {
        if (e.Function == EngineKeyFunctions.Use || e.Function == EngineKeyFunctions.TextCursorSelect ||
            e.Function == EngineKeyFunctions.UIRightClick || e.Function == OpenDreamKeyFunctions.MouseMiddle) {
            _entitySystemManager.Resolve(ref _mouseInput);

            if (_mouseInput.HandleViewportEvent(Viewport, e, ControlDescriptor)) {
                e.Handle();
            }
        }
    }

    private void OnViewportMouseMoveEvent(GUIMouseMoveEventArgs e) {
        if (_mouseInput == null)
            return;

        var underMouse = _mouseInput.GetAtomUnderMouse(Viewport, e.RelativePixelPosition, e.GlobalPixelPosition);
        UpdateAtomUnderMouse(underMouse?.Atom, e.RelativePixelPosition, underMouse?.IconPosition ?? Vector2i.Zero);
    }

    private void OnViewportMouseExitedEvent(GUIMouseHoverEventArgs e) {
        UpdateAtomUnderMouse(null, Vector2.Zero, Vector2i.Zero);
    }

    public void OnShowEvent() {
        ControlDescriptorMap controlDescriptor = (ControlDescriptorMap)ControlDescriptor;
        if (!string.IsNullOrWhiteSpace(controlDescriptor.OnShowCommand.Value)) {
            InterfaceManager.RunCommand(controlDescriptor.OnShowCommand.AsRaw());
        }
    }

    public void OnHideEvent() {
        ControlDescriptorMap controlDescriptor = (ControlDescriptorMap)ControlDescriptor;
        if (!string.IsNullOrWhiteSpace(controlDescriptor.OnHideCommand.Value)) {
            InterfaceManager.RunCommand(controlDescriptor.OnHideCommand.AsRaw());
        }
    }

    public override bool TryGetProperty(string property, [NotNullWhen(true)] out IDMFProperty? value) {
        switch (property) {
            case "view-size": // Size of the final viewport (resized and all) rather than the whole container
                value = new DMFPropertyVec2(Viewport.GetDrawBox().Size);
                return true;
            case "mouse-pos":
            case "inner-mouse-pos":
                var mousePos = IoCManager.Resolve<IUserInterfaceManager>().MousePositionScaled.Position;
                mousePos -= Viewport.GlobalPosition;

                value = new DMFPropertyVec2(mousePos);
                return true;
            default:
                return base.TryGetProperty(property, out value);
        }
    }

    private void UpdateAtomUnderMouse(ClientObjectReference? atom, Vector2 relativePos, Vector2i iconPos) {
        if (!_atomUnderMouse.Equals(atom)) {
            _entitySystemManager.Resolve(ref _appearanceSystem);
            var name = (atom != null) ? _appearanceSystem.GetNameUnformatted(atom.Value) : string.Empty;
            Window?.SetStatus(name);

            if (_atomUnderMouse != null)
                _mouseInput?.HandleAtomMouseExited(Viewport, _atomUnderMouse.Value);
            if (atom != null) {
                _mouseInput?.HandleAtomMouseEntered(Viewport, relativePos, atom.Value, iconPos);
            }
        } else if (atom.HasValue) {
            _mouseInput?.HandleAtomMouseMove(Viewport, relativePos, atom.Value, iconPos);
        }

        _atomUnderMouse = atom;
    }
}
