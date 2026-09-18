using System;
using System.Windows;
using System.Windows.Controls;
using TileTerm;

namespace TileTerm.Terminal;

/// <summary>
/// Owns the pane layout tree for one window: creating the first pane,
/// splitting a pane into two, closing a pane by collapsing it back into its
/// sibling, and — the VSCode-style drag/dock gestures — swapping two panes'
/// positions or moving a pane to dock against another one's edge. This is the
/// RLogin-style "one window, many panes" piece of the concept —
/// <see cref="PaneTree"/> nodes describe the layout, this class is what
/// actually mutates it and keeps the WPF visual tree in sync.
/// </summary>
public sealed class PaneManager
{
    private readonly ContentControl _host;
    private PaneNode _root;
    private LeafNode? _active;

    public PaneManager(ContentControl host, ProfileDefinition initialProfile)
    {
        _host = host;

        var initialLeaf = CreateLeaf(initialProfile);
        _root = initialLeaf;
        _host.Content = initialLeaf.Visual;
        SetActive(initialLeaf);
    }

    /// <summary>Splits whichever pane is currently active. No-op if there is somehow no active pane.</summary>
    public void SplitActive(SplitDirection direction, ProfileDefinition profile)
    {
        if (_active is not null) Split(_active, direction, profile);
    }

    /// <summary>Closes whichever pane is currently active.</summary>
    public void CloseActive()
    {
        if (_active is not null) Close(_active);
    }

    /// <summary>Splits <paramref name="target"/>'s area, opening a new session next to it.</summary>
    public void Split(LeafNode target, SplitDirection direction, ProfileDefinition profile)
    {
        var newLeaf = CreateLeaf(profile);

        DetachVisual(target.Visual);
        var grid = BuildSplitGrid(direction, target.Visual, newLeaf.Visual);
        var splitNode = new SplitNode(direction, target, newLeaf, grid);

        var oldParent = target.Parent;
        if (oldParent is null)
        {
            _root = splitNode;
            _host.Content = splitNode.Visual;
        }
        else
        {
            ReplaceChild(oldParent, target, splitNode);
        }

        target.Parent = splitNode;
        newLeaf.Parent = splitNode;

        newLeaf.Control.Loaded += (_, _) => newLeaf.Control.FocusCanvas();
        SetActive(newLeaf);
    }

    /// <summary>Closes <paramref name="target"/>, giving its sibling the freed space. If
    /// <paramref name="target"/> is the sole remaining pane, there's nothing to collapse
    /// into — closing "the last tile" is then the same thing as closing the window, so this
    /// defers to <see cref="LastPaneCloseRequested"/> instead of closing the pane itself.</summary>
    public void Close(LeafNode target)
    {
        var sibling = DetachLeafFromTree(target);
        if (sibling is null)
        {
            LastPaneCloseRequested?.Invoke();
            return;
        }

        target.Control.Shutdown();

        var nextActive = FindFirstLeaf(sibling);
        SetActive(nextActive);
        nextActive.Control.FocusCanvas();
    }

    /// <summary>Raised when closing the sole remaining pane is requested — the caller
    /// (<see cref="MainWindow"/>) should treat this the same as its own close button, e.g.
    /// showing the same confirmation dialog before actually shutting down.</summary>
    public event Action? LastPaneCloseRequested;

    /// <summary>
    /// Swaps two panes' positions in the layout (they trade places; each keeps its
    /// own running session). Used for the "drop in the middle of another tile" gesture.
    /// </summary>
    public void SwapPanes(LeafNode a, LeafNode b)
    {
        if (a == b) return;
        var aParent = a.Parent;
        var bParent = b.Parent;
        if (aParent is null || bParent is null) return; // shouldn't happen once there's more than one pane

        bool aWasFirst = aParent.First == a;
        bool bWasFirst = bParent.First == b;
        int aCol = Grid.GetColumn(a.Visual), aRow = Grid.GetRow(a.Visual);
        int bCol = Grid.GetColumn(b.Visual), bRow = Grid.GetRow(b.Visual);

        DetachVisual(a.Visual);
        DetachVisual(b.Visual);

        Grid.SetColumn(a.Visual, bCol);
        Grid.SetRow(a.Visual, bRow);
        Grid.SetColumn(b.Visual, aCol);
        Grid.SetRow(b.Visual, aRow);

        bParent.GridVisual.Children.Add(a.Visual);
        aParent.GridVisual.Children.Add(b.Visual);

        if (aWasFirst) aParent.First = b; else aParent.Second = b;
        if (bWasFirst) bParent.First = a; else bParent.Second = a;

        a.Parent = bParent;
        b.Parent = aParent;

        SetActive(a);
        a.Control.FocusCanvas();
    }

    /// <summary>
    /// Moves <paramref name="source"/> out of its current spot and docks it against
    /// <paramref name="target"/>'s edge — <paramref name="direction"/> is the new split's
    /// orientation (Right = side-by-side, Down = stacked) and <paramref name="sourceGoesFirst"/>
    /// says whether the moved pane lands on the left/top (true) or right/bottom (false).
    /// Used for the "drop on an edge of another tile" gesture.
    /// </summary>
    public void DockPane(LeafNode source, LeafNode target, SplitDirection direction, bool sourceGoesFirst)
    {
        if (source == target) return;

        DetachLeafFromTree(source); // promotes source's old sibling; source.Visual is now parentless
        DetachVisual(target.Visual);

        var first = sourceGoesFirst ? source : target;
        var second = sourceGoesFirst ? target : source;
        var grid = BuildSplitGrid(direction, first.Visual, second.Visual);
        var splitNode = new SplitNode(direction, first, second, grid);

        var oldParent = target.Parent;
        if (oldParent is null)
        {
            _root = splitNode;
            _host.Content = splitNode.Visual;
        }
        else
        {
            ReplaceChild(oldParent, target, splitNode);
        }

        target.Parent = splitNode;
        source.Parent = splitNode;

        SetActive(source);
        source.Control.FocusCanvas();
    }

    /// <summary>Finds the pane a drag operation started from, by the id carried in the drag data.</summary>
    public LeafNode? FindLeafByPaneId(Guid id) => FindLeafByPaneId(_root, id);

    private static LeafNode? FindLeafByPaneId(PaneNode node, Guid id) => node switch
    {
        LeafNode leaf => leaf.Control.PaneId == id ? leaf : null,
        SplitNode split => FindLeafByPaneId(split.First, id) ?? FindLeafByPaneId(split.Second, id),
        _ => null,
    };

    /// <summary>Disposes every session in the tree. Call once when the window is closing.</summary>
    public void ShutdownAll() => ShutdownRecursive(_root);

    private static void ShutdownRecursive(PaneNode node)
    {
        switch (node)
        {
            case LeafNode leaf:
                leaf.Control.Shutdown();
                break;
            case SplitNode split:
                ShutdownRecursive(split.First);
                ShutdownRecursive(split.Second);
                break;
        }
    }

    private LeafNode CreateLeaf(ProfileDefinition profile)
    {
        var control = new TerminalPaneControl(profile);
        var leaf = new LeafNode(control);
        control.Activated += () => SetActive(leaf);
        control.CloseRequested += () => Close(leaf);
        control.DockRequested += (sourcePaneId, zone) => HandleDockRequest(leaf, sourcePaneId, zone);
        return leaf;
    }

    private void HandleDockRequest(LeafNode target, Guid sourcePaneId, DropZone zone)
    {
        var source = FindLeafByPaneId(sourcePaneId);
        if (source is null || source == target) return;

        switch (zone)
        {
            case DropZone.Center:
                SwapPanes(source, target);
                break;
            case DropZone.Left:
                DockPane(source, target, SplitDirection.Right, sourceGoesFirst: true);
                break;
            case DropZone.Right:
                DockPane(source, target, SplitDirection.Right, sourceGoesFirst: false);
                break;
            case DropZone.Top:
                DockPane(source, target, SplitDirection.Down, sourceGoesFirst: true);
                break;
            case DropZone.Bottom:
                DockPane(source, target, SplitDirection.Down, sourceGoesFirst: false);
                break;
        }
    }

    private void SetActive(LeafNode leaf)
    {
        if (_active == leaf) return;
        _active?.Control.SetActive(false);
        _active = leaf;
        _active.Control.SetActive(true);
    }

    private static LeafNode FindFirstLeaf(PaneNode node) => node switch
    {
        LeafNode leaf => leaf,
        SplitNode split => FindFirstLeaf(split.First),
        _ => throw new InvalidOperationException("Unknown PaneNode type."),
    };

    /// <summary>
    /// Removes <paramref name="leaf"/> from the tree, collapsing its parent split so the
    /// sibling takes over the freed space (promoted to the grandparent's slot, or to root).
    /// Does <em>not</em> touch the leaf's session — callers decide whether to reuse it
    /// (dock elsewhere) or shut it down (close). Returns the promoted sibling, or null if
    /// <paramref name="leaf"/> was the sole remaining pane (nothing to collapse into).
    /// </summary>
    private PaneNode? DetachLeafFromTree(LeafNode leaf)
    {
        var parent = leaf.Parent;
        if (parent is null) return null;

        var sibling = parent.First == leaf ? parent.Second : parent.First;
        var grandparent = parent.Parent;

        DetachVisual(leaf.Visual);
        DetachVisual(sibling.Visual);

        if (grandparent is null)
        {
            _root = sibling;
            sibling.Parent = null;
            _host.Content = sibling.Visual;
        }
        else
        {
            DetachVisual(parent.Visual); // parent's grid is still grandparent's child at this point
            ReplaceChild(grandparent, parent, sibling);
            sibling.Parent = grandparent;
        }

        leaf.Parent = null;
        return sibling;
    }

    /// <summary>Removes a pane's visual from wherever it currently lives (a split's Grid, or the host slot).</summary>
    private static void DetachVisual(FrameworkElement visual)
    {
        switch (visual.Parent)
        {
            case Panel panel:
                panel.Children.Remove(visual);
                break;
            case ContentControl cc when cc.Content == visual:
                cc.Content = null;
                break;
        }
    }

    /// <summary>
    /// Places <paramref name="newChild"/> into <paramref name="parent"/>'s grid at the cell
    /// <paramref name="oldChild"/> used to occupy, and updates the tree pointer.
    /// Callers must detach <paramref name="oldChild"/>'s visual themselves, at whatever point
    /// it's actually still sitting in <paramref name="parent"/>'s grid — by the time this runs
    /// that visual has often already been moved into a new grid, so detaching it here would
    /// rip it back out of its new (correct) parent instead.
    /// </summary>
    private static void ReplaceChild(SplitNode parent, PaneNode oldChild, PaneNode newChild)
    {
        if (parent.Direction == SplitDirection.Right)
            Grid.SetColumn(newChild.Visual, Grid.GetColumn(oldChild.Visual));
        else
            Grid.SetRow(newChild.Visual, Grid.GetRow(oldChild.Visual));

        parent.GridVisual.Children.Add(newChild.Visual);

        if (parent.First == oldChild) parent.First = newChild;
        else parent.Second = newChild;
    }

    private static Grid BuildSplitGrid(SplitDirection direction, FrameworkElement first, FrameworkElement second)
    {
        var grid = new Grid();

        if (direction == SplitDirection.Right)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetColumn(first, 0);
            var splitter = Theme.Splitter(vertical: true);
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(second, 2);

            grid.Children.Add(first);
            grid.Children.Add(splitter);
            grid.Children.Add(second);
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(first, 0);
            var splitter = Theme.Splitter(vertical: false);
            Grid.SetRow(splitter, 1);
            Grid.SetRow(second, 2);

            grid.Children.Add(first);
            grid.Children.Add(splitter);
            grid.Children.Add(second);
        }

        return grid;
    }
}
