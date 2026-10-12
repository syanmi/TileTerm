using System;
using System.Collections.Generic;
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
///
/// Panes also move between windows (all of them live in this one process, see <see cref="SingleInstance"/>):
/// dropping a tile on another window's tile docks or swaps it there, and dropping it outside every window
/// tears it off into a window of its own. The pane's control — and with it the running session — is handed
/// over as is, so the program in it keeps running. A window whose last pane leaves raises <see cref="Emptied"/>.
/// </summary>
public sealed class PaneManager
{
    /// <summary>Every open window's manager, so a drop can find a pane dragged from another window.</summary>
    private static readonly List<PaneManager> s_all = new();

    private readonly ContentControl _host;
    private PaneNode? _root;   // null only after the window's last pane has moved to another window
    private LeafNode? _active;

    /// <summary>The handlers this manager put on each of its panes' events, so a pane that moves to another
    /// window can be unhooked from this one.</summary>
    private readonly Dictionary<TerminalPaneControl, (Action Activated, Action Close, Action<Guid, DropZone> Dock, Action<Point> TearOff)> _hooks = new();

    public PaneManager(ContentControl host, ProfileDefinition initialProfile)
        : this(host, new TerminalPaneControl(initialProfile))
    {
    }

    /// <summary>Starts the window with an existing pane, such as one torn off another window.</summary>
    public PaneManager(ContentControl host, TerminalPaneControl initialPane)
    {
        _host = host;
        s_all.Add(this);

        var initialLeaf = Adopt(initialPane);
        _root = initialLeaf;
        _host.Content = initialLeaf.Visual;
        SetActive(initialLeaf);
    }

    /// <summary>Raised when the last pane has moved to another window: the window has nothing left to
    /// show and should close — without asking, and without shutting anything down.</summary>
    public event Action? Emptied;

    /// <summary>Raised when a pane was dragged out of every window and dropped there: the caller opens a
    /// new window for it at <c>screenPoint</c> (physical pixels). The pane is already out of this tree.</summary>
    public event Action<TerminalPaneControl, Point>? PaneTornOff;

    /// <summary>Call once the window has closed.</summary>
    public void Unregister() => s_all.Remove(this);

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
        var newLeaf = Adopt(new TerminalPaneControl(profile));
        PlaceNextTo(target, newLeaf, direction, newGoesFirst: false);

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

        Unhook(target.Control);
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
        PlaceNextTo(target, source, direction, sourceGoesFirst);

        SetActive(source);
        source.Control.FocusCanvas();
    }

    /// <summary>Finds the pane a drag operation started from, by the id carried in the drag data.</summary>
    public LeafNode? FindLeafByPaneId(Guid id) => _root is null ? null : FindLeafByPaneId(_root, id);

    private static LeafNode? FindLeafByPaneId(PaneNode node, Guid id) => node switch
    {
        LeafNode leaf => leaf.Control.PaneId == id ? leaf : null,
        SplitNode split => FindLeafByPaneId(split.First, id) ?? FindLeafByPaneId(split.Second, id),
        _ => null,
    };

    private LeafNode? FindLeafByControl(TerminalPaneControl control) => _root is null ? null : FindLeafByControl(_root, control);

    private static LeafNode? FindLeafByControl(PaneNode node, TerminalPaneControl control) => node switch
    {
        LeafNode leaf => leaf.Control == control ? leaf : null,
        SplitNode split => FindLeafByControl(split.First, control) ?? FindLeafByControl(split.Second, control),
        _ => null,
    };

    /// <summary>Disposes every session in the tree. Call once when the window is closing.</summary>
    public void ShutdownAll()
    {
        if (_root is not null) ShutdownRecursive(_root);
    }

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

    /// <summary>Makes <paramref name="control"/> one of this window's panes (not yet placed in the layout)
    /// and listens to it. Its handlers look the pane up by control rather than holding on to the leaf, so they
    /// stay right when two windows trade panes.</summary>
    private LeafNode Adopt(TerminalPaneControl control)
    {
        Hook(control);
        return new LeafNode(control);
    }

    private void Hook(TerminalPaneControl control)
    {
        Action activated = () => { if (FindLeafByControl(control) is { } l) SetActive(l); };
        Action close = () => { if (FindLeafByControl(control) is { } l) Close(l); };
        Action<Guid, DropZone> dock = (sourcePaneId, zone) => { if (FindLeafByControl(control) is { } l) HandleDockRequest(l, sourcePaneId, zone); };
        Action<Point> tearOff = point => { if (FindLeafByControl(control) is { } l) TearOff(l, point); };
        control.Activated += activated;
        control.CloseRequested += close;
        control.DockRequested += dock;
        control.TearOffRequested += tearOff;
        _hooks[control] = (activated, close, dock, tearOff);
    }

    private void Unhook(TerminalPaneControl control)
    {
        if (!_hooks.Remove(control, out var hooks)) return;
        control.Activated -= hooks.Activated;
        control.CloseRequested -= hooks.Close;
        control.DockRequested -= hooks.Dock;
        control.TearOffRequested -= hooks.TearOff;
    }

    private void HandleDockRequest(LeafNode target, Guid sourcePaneId, DropZone zone)
    {
        var source = FindLeafByPaneId(sourcePaneId);
        if (source == target) return;
        if (source is null)
        {
            HandleDropFromOtherWindow(target, sourcePaneId, zone);
            return;
        }

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

    /// <summary>A tile from another window was dropped on <paramref name="target"/>: the middle trades the
    /// two panes between the windows, an edge moves the dragged pane over and docks it there.</summary>
    private void HandleDropFromOtherWindow(LeafNode target, Guid sourcePaneId, DropZone zone)
    {
        PaneManager? owner = null;
        LeafNode? source = null;
        foreach (var manager in s_all)
        {
            if (manager == this) continue;
            source = manager.FindLeafByPaneId(sourcePaneId);
            if (source is not null) { owner = manager; break; }
        }
        if (owner is null || source is null) return;

        if (zone == DropZone.Center)
        {
            TradePanes(owner, source, this, target);
        }
        else
        {
            var control = owner.Release(source);
            var moved = Adopt(control);
            var direction = zone is DropZone.Left or DropZone.Right ? SplitDirection.Right : SplitDirection.Down;
            PlaceNextTo(target, moved, direction, newGoesFirst: zone is DropZone.Left or DropZone.Top);
            SetActive(moved);
            moved.Control.FocusCanvas();
        }

        Window.GetWindow(target.Control)?.Activate();
    }

    /// <summary>Swaps the panes of two leaves in different windows: each window keeps its layout, and the
    /// two slots exchange their panes (and sessions).</summary>
    private static void TradePanes(PaneManager aOwner, LeafNode a, PaneManager bOwner, LeafNode b)
    {
        var aControl = a.Control;
        var bControl = b.Control;
        var aSlot = SlotOf(a);
        var bSlot = SlotOf(b);

        aOwner.Unhook(aControl);
        bOwner.Unhook(bControl);
        DetachVisual(aControl);
        DetachVisual(bControl);

        // Each window takes the other's pane into the slot its own pane left.
        a.Control = bControl;
        b.Control = aControl;
        aOwner.Hook(bControl);
        bOwner.Hook(aControl);
        aOwner.PlaceInSlot(aSlot, bControl);
        bOwner.PlaceInSlot(bSlot, aControl);

        // Both windows' active pane changed under them; reset the markers without touching the other window.
        aOwner._active = null;
        aOwner.SetActive(a);
        bOwner._active = null;
        bOwner.SetActive(b);
        aControl.FocusCanvas();
    }

    /// <summary>Takes <paramref name="leaf"/>'s pane out of this window to hand it to another one; its
    /// session keeps running. If it was the last pane, the window is now empty (<see cref="Emptied"/>).</summary>
    private TerminalPaneControl Release(LeafNode leaf)
    {
        var control = leaf.Control;
        Unhook(control);

        var sibling = DetachLeafFromTree(leaf);
        if (_active == leaf) _active = null;
        if (sibling is null)
        {
            // The sole pane: nothing to collapse into.
            DetachVisual(control);
            _root = null;
            Emptied?.Invoke();
        }
        else if (_active is null)
        {
            SetActive(FindFirstLeaf(sibling));
        }
        return control;
    }

    /// <summary>The "drop outside every window" gesture: moves the pane into a window of its own. Does
    /// nothing for a window's only pane, which would just be moving the window.</summary>
    private void TearOff(LeafNode leaf, Point screenPoint)
    {
        if (leaf.Parent is null) return;
        var control = Release(leaf);
        PaneTornOff?.Invoke(control, screenPoint);
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

    /// <summary>Where a node sits: its parent split (null = the window's root) and its cell in that split's grid.</summary>
    private readonly record struct Slot(SplitNode? Parent, int Column, int Row);

    private static Slot SlotOf(PaneNode node) => new(node.Parent, Grid.GetColumn(node.Visual), Grid.GetRow(node.Visual));

    private void PlaceInSlot(Slot slot, FrameworkElement visual)
    {
        if (slot.Parent is null)
        {
            _host.Content = visual;
            return;
        }
        Grid.SetColumn(visual, slot.Column);
        Grid.SetRow(visual, slot.Row);
        slot.Parent.GridVisual.Children.Add(visual);
    }

    /// <summary>Splits <paramref name="target"/>'s area between it and <paramref name="newLeaf"/>, which
    /// must not be in the tree. Shared by splitting, docking and moving a pane in from another window.</summary>
    private void PlaceNextTo(LeafNode target, LeafNode newLeaf, SplitDirection direction, bool newGoesFirst)
    {
        DetachVisual(target.Visual);
        var first = newGoesFirst ? newLeaf : target;
        var second = newGoesFirst ? target : newLeaf;
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
        newLeaf.Parent = splitNode;
    }

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
        // Without this a split nested by Split/DockPane had no parent, and taking a pane out of it (closing,
        // moving it away) promoted the sibling to the window's root — dropping every other pane.
        newChild.Parent = parent;
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
