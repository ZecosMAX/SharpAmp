using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SharpAmp.Avalonia.ViewModels;

namespace SharpAmp.Avalonia.Controls;

/// <summary>An explicit patch board. Cables alone define the signal path; card positions never change it.</summary>
public sealed class PedalboardCanvas : Canvas
{
    private readonly WireLayer wires;
    private readonly Button inputPort;
    private readonly Button outputPort;
    private readonly Dictionary<ProcessorViewModel, ProcessorBox> cards = new();
    private static readonly IBrush Mint = Brush.Parse("#85E0BE");
    private static readonly IBrush Dot = Brush.Parse("#2A3239");
    private ProcessorChainViewModel? model;
    private ProcessorViewModel? dragging;
    private Port? connecting;
    private Port? connectionTarget;
    private (string Source, string Target)? rewiring;
    private IPointer? connectionPointer;
    private Point connectionPress;
    private bool connectionMoved;
    private bool completingClick;
    private Cursor? wireCursor;
    private Point dragOffset;
    private Point pointer;
    private Size viewportSize;
    private Vector viewportOffset;
    private Size contentExtent;
    private string? dropEffectName;
    private Point dropPosition;
    public bool SnapToGrid { get; set; } = true;
    public bool ShowHints { get; set; } = true;
    public void RefreshViewport() => InvalidateVisual();

    /// <summary>Fills the visible workspace without changing saved card coordinates.</summary>
    public void UpdateViewport(Size viewport, Vector offset)
    {
        if (!double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height)) return;
        if (viewportSize == viewport && viewportOffset == offset) return;
        viewportSize = viewport;
        viewportOffset = offset;
        InvalidateVisual();
    }

    /// <summary>Places a newly dropped card under the pointer using the same grid as card dragging.</summary>
    public Point GetDropPosition(Point canvasPointer) => ConstrainPosition(new Point(canvasPointer.X - 118, canvasPointer.Y - 72));

    public void ShowDropPreview(string? effectName, Point point)
    {
        dropEffectName = effectName;
        dropPosition = GetDropPosition(point);
        InvalidateVisual();
    }

    public PedalboardCanvas()
    {
        wires = new WireLayer(this) { IsHitTestVisible = false };
        inputPort = CreateTerminal("BoardInputPort", new Port(ProcessorChainViewModel.InputId, true), "Audio input");
        outputPort = CreateTerminal("BoardOutputPort", new Port(ProcessorChainViewModel.OutputId, false), "Audio output");
        Children.Add(wires);
        Children.Add(inputPort);
        Children.Add(outputPort);
        Background = Brush.Parse("#14191D");
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Focusable = true;
        ClipToBounds = true;
        DataContextChanged += (_, _) => Bind(DataContext as ProcessorChainViewModel);
        DetachedFromVisualTree += (_, _) => Bind(null);
        AttachedToVisualTree += (_, _) =>
        {
            inputPort.Cursor = outputPort.Cursor = new Cursor(StandardCursorType.Cross);
            Bind(DataContext as ProcessorChainViewModel);
        };
    }

    private void Bind(ProcessorChainViewModel? next)
    {
        if (model is not null)
        {
            model.Processors.CollectionChanged -= CollectionChanged;
            model.PropertyChanged -= ModelChanged;
        }
        foreach (var node in cards.Keys) node.PropertyChanged -= NodeChanged;
        CancelConnection();
        cards.Clear();
        Children.Clear();
        Children.Add(wires);
        Children.Add(inputPort);
        Children.Add(outputPort);
        model = next;
        if (model is not null)
        {
            model.Processors.CollectionChanged += CollectionChanged;
            model.PropertyChanged += ModelChanged;
        }
        Rebuild();
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateTerminalAppearance();
        InvalidateVisual();
    }
    private void Rebuild()
    {
        var nodes = model?.Processors.ToArray() ?? [];
        foreach (var removed in cards.Keys.Except(nodes).ToArray())
        {
            removed.PropertyChanged -= NodeChanged;
            Children.Remove(cards[removed]);
            cards.Remove(removed);
            if (connecting?.Id == removed.Id || connectionTarget?.Id == removed.Id) CancelConnection();
        }
        foreach (var node in nodes.Where(node => !cards.ContainsKey(node)))
        {
            var card = new ProcessorBox { DataContext = node };
            card.AddHandler(PointerPressedEvent, (_, _) => model!.SelectedProcessor = node, RoutingStrategies.Tunnel);
            card.GotFocus += (_, _) => model!.SelectedProcessor = node;
            card.DragRequested += (_, e) => BeginDrag(node, e);
            card.OutputPressed += (_, e) => BeginConnection(new Port(node.Id, true), e);
            card.InputPressed += (_, e) => BeginConnection(new Port(node.Id, false), e);
            card.OutputClicked += (_, _) => ClickPort(new Port(node.Id, true));
            card.InputClicked += (_, _) => ClickPort(new Port(node.Id, false));
            card.DisconnectInputRequested += (_, _) => { CancelConnection(); model?.DisconnectInput(node.Id); };
            card.DisconnectOutputRequested += (_, _) => { CancelConnection(); model?.DisconnectOutput(node.Id); };
            node.PropertyChanged += NodeChanged;
            cards.Add(node, card);
            Children.Add(card);
        }
        UpdateTerminalAppearance();
        UpdatePositions();
    }

    private void NodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProcessorViewModel.X) or nameof(ProcessorViewModel.Y)) UpdatePositions();
        else InvalidateVisual();
    }

    private void UpdatePositions()
    {
        foreach (var (node, card) in cards)
        {
            SetLeft(card, node.X);
            SetTop(card, node.Y);
        }
        contentExtent = cards.Count == 0 ? default : new Size(cards.Keys.Max(n => n.X) + 380, cards.Keys.Max(n => n.Y) + 244);
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);
        // Only real content contributes to the scroll extent. The presenter stretches the board
        // to its current viewport; feeding that viewport back into measurement makes the two
        // scrollbars compete when one briefly reduces the space available to the other.
        return contentExtent;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        base.ArrangeOverride(finalSize);
        wires.Arrange(new Rect(finalSize));
        inputPort.Arrange(new Rect(new Point(24, 20), new Size(24, 24)));
        outputPort.Arrange(new Rect(new Point(Math.Max(164, finalSize.Width - 36) - 12, 20), new Size(24, 24)));
        return finalSize;
    }

    private Point ConstrainPosition(Point position)
    {
        var x = Math.Clamp(position.X, 24, 100000);
        var y = Math.Clamp(position.Y, 60, 100000);
        return SnapToGrid ? new Point(Math.Round(x / 16) * 16, Math.Round(y / 16) * 16) : new Point(x, y);
    }

    private readonly record struct Port(string Id, bool IsOutput);

    private Button CreateTerminal(string name, Port port, string label)
    {
        var button = new Button
        {
            Name = name, Width = 24, Height = 24, MinHeight = 24, MinWidth = 24,
            Padding = new Thickness(0), CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(2), Background = Brush.Parse("#14191D"),
            BorderBrush = Mint
        };
        global::Avalonia.Automation.AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, $"{label} · drag to an effect port, or click the two ports. Right-click to disconnect.");
        button.AddHandler(PointerPressedEvent, (_, e) => BeginConnection(port, e), RoutingStrategies.Tunnel);
        button.Click += (_, _) => ClickPort(port);
        var disconnect = new MenuItem { Header = port.IsOutput ? "Disconnect input cable" : "Disconnect output cable" };
        disconnect.Click += (_, _) =>
        {
            CancelConnection();
            if (port.IsOutput) model?.DisconnectOutput(port.Id);
            else model?.DisconnectInput(port.Id);
        };
        var menu = new ContextMenu { ItemsSource = new[] { disconnect } };
        menu.Opening += (_, _) => disconnect.IsEnabled = PortIsConnected(port);
        button.ContextMenu = menu;
        return button;
    }

    private bool PortIsConnected(Port port) => model?.Connections.Any(connection =>
        port.IsOutput ? connection.SourceId == port.Id : connection.TargetId == port.Id) == true;

    private void UpdateTerminalAppearance()
    {
        inputPort.BorderBrush = PortIsConnected(new Port(ProcessorChainViewModel.InputId, true)) ? Mint : Brush.Parse("#687881");
        outputPort.BorderBrush = PortIsConnected(new Port(ProcessorChainViewModel.OutputId, false)) ? Mint : Brush.Parse("#687881");
    }

    private Point PortPosition(Port port)
    {
        if (port.Id == ProcessorChainViewModel.InputId) return new Point(36, 32);
        if (port.Id == ProcessorChainViewModel.OutputId) return new Point(Math.Max(164, Bounds.Width - 36), 32);
        var node = cards.Keys.FirstOrDefault(node => node.Id == port.Id);
        return node is null ? default : new Point(node.X + (port.IsOutput ? 224 : 12), node.Y + 72);
    }

    private Port? FindPort(Point point)
    {
        if (!new Rect(Bounds.Size).Contains(point)) return null;
        if (viewportSize.Width > 0 && viewportSize.Height > 0 &&
            !new Rect(viewportOffset.X, viewportOffset.Y, viewportSize.Width, viewportSize.Height).Contains(point)) return null;
        foreach (var node in cards.Keys.Reverse())
        {
            var input = new Port(node.Id, false);
            if (Distance(point, PortPosition(input)) <= 18) return input;
            var output = new Port(node.Id, true);
            if (Distance(point, PortPosition(output)) <= 18) return output;
        }
        var boardInput = new Port(ProcessorChainViewModel.InputId, true);
        if (Distance(point, PortPosition(boardInput)) <= 18) return boardInput;
        var boardOutput = new Port(ProcessorChainViewModel.OutputId, false);
        return Distance(point, PortPosition(boardOutput)) <= 18 ? boardOutput : null;
    }

    private static bool IsMatchingPort(Port origin, Port target) => origin.IsOutput != target.IsOutput && origin.Id != target.Id;

    private void BeginConnection(Port port, PointerPressedEventArgs e)
    {
        if (model is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        completingClick = connecting is { } pending && IsMatchingPort(pending, port);
        if (!completingClick)
        {
            CancelConnection();
            connecting = port;
        }
        connectionPress = pointer = e.GetPosition(this);
        connectionMoved = false;
        connectionPointer = e.Pointer;
        e.Pointer.Capture(this);
        model.StatusMessage = "Drag to the other port and release, or click it. Escape cancels; right-click a port to disconnect.";
        UpdateConnectionTarget();
        e.Handled = true;
    }

    // Button.Click remains available to keyboard and assistive technology users.
    private void ClickPort(Port port)
    {
        if (model is null) return;
        if (connecting is { } origin && IsMatchingPort(origin, port))
        {
            CompleteConnection(port);
            return;
        }
        CancelConnection();
        connecting = port;
        pointer = PortPosition(port);
        model.StatusMessage = "Choose the other port to connect. Escape cancels; right-click a port to disconnect.";
        InvalidateVisual();
    }

    private void CompleteConnection(Port? target)
    {
        var origin = connecting;
        // Release capture before applying the model change so capture-loss cannot cancel a successful commit.
        CancelConnection();
        if (model is null || origin is null) return;
        if (target is { } end && IsMatchingPort(origin.Value, end))
        {
            var source = origin.Value.IsOutput ? origin.Value.Id : end.Id;
            var destination = origin.Value.IsOutput ? end.Id : origin.Value.Id;
            model.Connect(source, destination);
        }
        else model.StatusMessage = "Connection cancelled. Release on the other port to connect.";
    }

    private void CancelConnection()
    {
        var captured = connectionPointer;
        connectionPointer = null;
        connecting = null;
        connectionTarget = null;
        rewiring = null;
        completingClick = false;
        connectionMoved = false;
        foreach (var card in cards.Values) card.HighlightPort(false, false);
        if (inputPort is not null) inputPort.Background = Brush.Parse("#14191D");
        if (outputPort is not null) outputPort.Background = Brush.Parse("#14191D");
        captured?.Capture(null);
        InvalidateVisual();
    }

    private void UpdateConnectionTarget()
    {
        var target = FindPort(pointer);
        connectionTarget = connecting is { } origin && target is { } candidate && IsMatchingPort(origin, candidate) ? target : null;
        foreach (var (node, card) in cards)
            card.HighlightPort(connectionTarget == new Port(node.Id, false), connectionTarget == new Port(node.Id, true));
        inputPort.Background = connectionTarget == new Port(ProcessorChainViewModel.InputId, true) ? Mint : Brush.Parse("#14191D");
        outputPort.Background = connectionTarget == new Port(ProcessorChainViewModel.OutputId, false) ? Mint : Brush.Parse("#14191D");
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled || model is null || e.Source != this && e.Source != wires) return;
        var point = e.GetPosition(this);
        var cable = FindWire(point);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed && cable is { } contextCable)
        {
            CancelConnection();
            var item = new MenuItem { Header = "Disconnect cable" };
            item.Click += (_, _) => model?.Disconnect(contextCable.Source, contextCable.Target);
            var menu = new ContextMenu { ItemsSource = new[] { item }, Placement = PlacementMode.Pointer };
            menu.Open(this);
            e.Handled = true;
            return;
        }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        CancelConnection();
        if (cable is not { } grabbed) return;
        Focus();
        // Keep the distant end anchored; the original cable is only replaced after a valid drop.
        connecting = grabbed.NearSource ? new Port(grabbed.Target, false) : new Port(grabbed.Source, true);
        rewiring = (grabbed.Source, grabbed.Target);
        connectionPress = pointer = point;
        connectionMoved = true;
        connectionPointer = e.Pointer;
        e.Pointer.Capture(this);
        model.StatusMessage = "Move this cable end to another port. Release elsewhere or press Escape to keep the original cable.";
        UpdateConnectionTarget();
        e.Handled = true;
    }

    private void BeginDrag(ProcessorViewModel node, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        CancelConnection();
        Focus();
        model!.SelectedProcessor = node;
        dragging = node;
        var position = e.GetPosition(this);
        dragOffset = new Point(position.X - node.X, position.Y - node.Y);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        pointer = e.GetPosition(this);
        if (dragging is not null)
        {
            var position = ConstrainPosition(new Point(pointer.X - dragOffset.X, pointer.Y - dragOffset.Y));
            dragging.X = position.X;
            dragging.Y = position.Y;
        }
        if (connecting is not null)
        {
            if (connectionPointer is not null && Distance(pointer, connectionPress) >= 6) connectionMoved = true;
            UpdateConnectionTarget();
        }
        else if (dragging is null)
            Cursor = e.Source == this && FindWire(pointer) is not null ? wireCursor ??= new Cursor(StandardCursorType.Hand) : null;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (connectionPointer == e.Pointer)
        {
            pointer = e.GetPosition(this);
            if (connectionMoved || completingClick || Distance(pointer, connectionPress) >= 6) CompleteConnection(FindPort(pointer));
            else
            {
                connectionPointer = null;
                e.Pointer.Capture(null);
                UpdateConnectionTarget();
            }
            e.Handled = true;
        }
        dragging = null;
        if (e.Pointer.Captured == this) e.Pointer.Capture(null);
        base.OnPointerReleased(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        dragging = null;
        if (connectionPointer is not null) CancelConnection();
        base.OnPointerCaptureLost(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelConnection();
            if (model is not null) model.StatusMessage = "Connection cancelled.";
            InvalidateVisual();
            e.Handled = true;
        }
        else if (model?.SelectedProcessor is { } selected)
        {
            if (e.Key == Key.Delete) { model.RemoveSelected(); e.Handled = true; }
            else if (e.Key == Key.B) { selected.IsBypassed = !selected.IsBypassed; e.Handled = true; }
            else if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Left or Key.Right)
            { model.MoveSelected(e.Key == Key.Left ? -1 : 1); e.Handled = true; }
        }
        base.OnKeyDown(e);
    }

    private new void InvalidateVisual()
    {
        base.InvalidateVisual();
        wires.InvalidateVisual();
    }

    private sealed class WireLayer(PedalboardCanvas board) : Control
    {
        public override void Render(DrawingContext context) => board.RenderBoard(context);
    }

    private void RenderBoard(DrawingContext context)
    {
        // Draw only the viewport's grid; a distant node must not generate a huge drawing list.
        var clip = new Rect(0, 0, Math.Min(Bounds.Width, 4000), Math.Min(Bounds.Height, 3000));
        if (viewportSize.Width > 0 && viewportSize.Height > 0)
            clip = new Rect(viewportOffset.X, viewportOffset.Y, viewportSize.Width, viewportSize.Height).Intersect(new Rect(Bounds.Size));
        for (var x = Math.Floor(clip.X / 24) * 24; x < clip.Right; x += 24)
            for (var y = Math.Floor(clip.Y / 24) * 24; y < clip.Bottom; y += 24)
                context.DrawEllipse(Dot, null, new Point(x, y), 1, 1);

        RenderConnections(context);
        if (dropEffectName is not null)
        {
            var rect = new Rect(dropPosition.X + 10, dropPosition.Y, 216, 144);
            context.DrawRectangle(Brush.Parse("#263D35"), new Pen(Mint, 2, DashStyle.Dash), rect, 10, 10);
            var label = new FormattedText(dropEffectName, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Inter, Segoe UI"), 16, Mint);
            context.DrawText(label, new Point(rect.X + 15, rect.Y + 38));
            var hint = new FormattedText("Release to add", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Inter, Segoe UI"), 11, Mint);
            context.DrawText(hint, new Point(rect.X + 15, rect.Y + 72));
        }
    }

    private void RenderConnections(DrawingContext context)
    {
        if (model is null) return;
        DrawEndpoint(context, PortPosition(new Port(ProcessorChainViewModel.InputId, true)), "INPUT");
        DrawEndpoint(context, PortPosition(new Port(ProcessorChainViewModel.OutputId, false)), "OUTPUT");
        foreach (var cable in model.Connections)
        {
            if (rewiring == (cable.SourceId, cable.TargetId)) continue;
            var source = cards.Keys.FirstOrDefault(node => node.Id == cable.SourceId);
            var active = model.HasCompleteRoute && (cable.SourceId == ProcessorChainViewModel.InputId || source?.IsInSignalPath == true);
            DrawWire(context, PortPosition(new Port(cable.SourceId, true)), PortPosition(new Port(cable.TargetId, false)),
                source?.IsBypassed == true || !active, active ? Mint : Brush.Parse("#687881"));
        }
        if (connecting is { } origin)
        {
            var freeEnd = connectionTarget is { } target ? PortPosition(target) : pointer;
            DrawWire(context, origin.IsOutput ? PortPosition(origin) : freeEnd,
                origin.IsOutput ? freeEnd : PortPosition(origin), true, Mint);
        }
    }

    private static void DrawEndpoint(DrawingContext context, Point point, string label)
    {
        var text = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Inter, Segoe UI"), 10, Brush.Parse("#95A2AC"));
        context.DrawText(text, new Point(point.X - text.Width / 2, point.Y + 17));
    }

    private (string Source, string Target, bool NearSource)? FindWire(Point point)
    {
        if (model is null || FindPort(point) is not null) return null;
        (string, string, bool)? nearest = null;
        var nearestDistance = 8.0;
        foreach (var cable in model.Connections)
        {
            var start = PortPosition(new Port(cable.SourceId, true));
            var end = PortPosition(new Port(cable.TargetId, false));
            var bend = WireBend(start, end);
            var steps = Math.Clamp((int)(Distance(end, start) / 12), 24, 512);
            var previous = start;
            for (var step = 1; step <= steps; step++)
            {
                var t = step / (double)steps;
                var current = BezierPoint(start, new Point(start.X + bend, start.Y), new Point(end.X - bend, end.Y), end, t);
                var segment = current - previous;
                var lengthSquared = segment.X * segment.X + segment.Y * segment.Y;
                var fromStart = point - previous;
                var fraction = lengthSquared == 0 ? 0 : Math.Clamp((fromStart.X * segment.X + fromStart.Y * segment.Y) / lengthSquared, 0, 1);
                var distance = Distance(point, previous + segment * fraction);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = (cable.SourceId, cable.TargetId, (step - 1 + fraction) / steps < 0.5);
                }
                previous = current;
            }
        }
        return nearest;
    }

    private static Point BezierPoint(Point a, Point b, Point c, Point d, double t)
    {
        var u = 1 - t;
        return new Point(u * u * u * a.X + 3 * u * u * t * b.X + 3 * u * t * t * c.X + t * t * t * d.X,
            u * u * u * a.Y + 3 * u * u * t * b.Y + 3 * u * t * t * c.Y + t * t * t * d.Y);
    }

    private static double WireBend(Point start, Point end) => Math.Max(44, Math.Abs(end.X - start.X) * 0.5);
    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static void DrawWire(DrawingContext context, Point start, Point end, bool dashed, IBrush brush)
    {
        var bend = WireBend(start, end);
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(start, false);
            path.CubicBezierTo(new Point(start.X + bend, start.Y), new Point(end.X - bend, end.Y), end);
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(brush, 2, dashed ? DashStyle.Dash : null), geometry);
        context.DrawLine(new Pen(brush, 1.5), new Point(end.X - 7, end.Y - 4), end);
        context.DrawLine(new Pen(brush, 1.5), new Point(end.X - 7, end.Y + 4), end);
    }
}
