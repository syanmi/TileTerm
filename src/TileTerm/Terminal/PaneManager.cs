using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TileTerm.Terminal;

/// <summary>
/// Owns the pane layout tree for one window: creating the first pane,
/// splitting a pane into two, and closing a pane by collapsing it back into
/// its sibling. This is the RLogin-style "one window, many panes" piece of
/// the concept — <see cref="PaneTree"/> nodes describe the layout, this
/// class is what actually mutates it and keeps the WPF visual tree in sync.
/// </summary>
public sealed class PaneManager
{
    private readonly ContentControl _host;
    private readonly ProfileStore _profileStore;
    private PaneNode _root;
    private LeafNode? _active;

    public PaneManager(ContentControl host, ProfileStore profileStore, ProfileDefinition initialProfile)
    {
        _host = host;
        _profileStore = profileStore;

        var initialLeaf = CreateLeaf(initialProfile);
        _root = initialLeaf;
        _host.Content = initialLeaf.Visual;
        SetActive(initialLeaf);
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

    /// <summary>Closes <paramref name="target"/>, giving its sibling the freed space.</summary>
    public void Close(LeafNode target)
    {
        var parent = target.Parent;
        if (parent is null)
            return; // sole remaining pane — nothing to collapse into, so refuse.

        var sibling = parent.First == target ? parent.Second : parent.First;
        var grandparent = parent.Parent;

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

        target.Control.Shutdown();

        var nextActive = FindFirstLeaf(sibling);
        SetActive(nextActive);
        nextActive.Control.FocusCanvas();
    }

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
        var control = new TerminalPaneControl(profile, _profileStore);
        var leaf = new LeafNode(control);
        control.Activated += () => SetActive(leaf);
        control.SplitRequested += (dir, p) => Split(leaf, dir, p);
        control.CloseRequested += () => Close(leaf);
        return leaf;
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
    /// it's actually still sitting in <paramref name="parent"/>'s grid — in <see cref="Split"/>
    /// that visual has usually already been moved into a new split grid by the time this runs,
    /// so detaching it here would rip it back out of its new (correct) parent instead.
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
            var splitter = MakeSplitter(vertical: true);
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
            var splitter = MakeSplitter(vertical: false);
            Grid.SetRow(splitter, 1);
            Grid.SetRow(second, 2);

            grid.Children.Add(first);
            grid.Children.Add(splitter);
            grid.Children.Add(second);
        }

        return grid;
    }

    private static GridSplitter MakeSplitter(bool vertical) => new()
    {
        Width = vertical ? 5 : double.NaN,
        Height = vertical ? double.NaN : 5,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Background = Brushes.DimGray,
        ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        Cursor = vertical ? Cursors.SizeWE : Cursors.SizeNS,
    };
}
