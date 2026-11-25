using Meitou.Modding;

namespace Meitou.BuildModeGizmo;

/// <summary>
/// The original's settings that apply in Meitou (its <c>BuildModeGizmo.defaults.ini</c> and MCM page; labels and hints are its English texts, so
/// its catalogues translate them): Enabled, SiteArrows and SnapToGround. Its developer-menu rows (EditorAnyBuilding, EditorArrows,
/// SkipDuringPlacement, ApplyChanges) are for the original's Shift+F12 editor, which Meitou does not have.
/// </summary>
public sealed class GizmoSettings
{
    [Setting("Enabled", Hint = "Turns the whole plugin on or off.")]
    public bool Enabled { get; set; } = true;

    [Setting("Arrows on your construction site", Hint = "Select your building that is not built yet: move arrows appear on it. Drag an arrow to "
        + "shift it, the window button switches to rotation. Close the window to hide them.", SubGroup = "Construction sites")]
    public bool SiteArrows { get; set; } = true;

    [Setting("Settle on the ground after a move", Hint = "When the arrow is released, the building is rebuilt where it now stands, and the game "
        + "puts it on the ground. Off - it stays at the height you raised it to.", SubGroup = "Construction sites")]
    public bool SnapToGround { get; set; } = true;
}
