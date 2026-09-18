using System.Windows;
using System.Windows.Controls;

namespace TileTerm.Terminal;

/// <summary>Which side of the target pane the new pane appears on.</summary>
public enum SplitDirection
{
    Right,
    Down,
}

/// <summary>
/// Where a drag-and-drop tile was released over its target, VSCode-style:
/// the four edges dock the dragged pane against that side, and the center
/// swaps the two panes' positions.
/// </summary>
public enum DropZone
{
    Left,
    Right,
    Top,
    Bottom,
    Center,
}

/// <summary>
/// One node of the pane layout tree. A window's whole layout is a binary
/// tree of these: every <see cref="SplitNode"/> divides its area between two
/// children (which may themselves be splits), and every <see cref="LeafNode"/>
/// is one live terminal pane. This mirrors how RLogin/tmux-style layouts
/// work, and keeps the splitting/closing logic in <see cref="PaneManager"/>
/// independent of WPF's own visual tree.
/// </summary>
public abstract class PaneNode
{
    public SplitNode? Parent { get; set; }
    public abstract FrameworkElement Visual { get; }
}

public sealed class LeafNode : PaneNode
{
    public TerminalPaneControl Control { get; }
    public override FrameworkElement Visual => Control;

    public LeafNode(TerminalPaneControl control) => Control = control;
}

public sealed class SplitNode : PaneNode
{
    public SplitDirection Direction { get; }
    public PaneNode First { get; set; }
    public PaneNode Second { get; set; }
    public Grid GridVisual { get; }
    public override FrameworkElement Visual => GridVisual;

    public SplitNode(SplitDirection direction, PaneNode first, PaneNode second, Grid gridVisual)
    {
        Direction = direction;
        First = first;
        Second = second;
        GridVisual = gridVisual;
    }
}
