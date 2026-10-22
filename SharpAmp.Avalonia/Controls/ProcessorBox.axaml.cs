using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SharpAmp.Avalonia.ViewModels;

namespace SharpAmp.Avalonia.Controls;

public partial class ProcessorBox : UserControl
{
    public event EventHandler<PointerPressedEventArgs>? DragRequested;
    public event EventHandler? InputClicked;
    public event EventHandler? OutputClicked;
    public event EventHandler<PointerPressedEventArgs>? InputPressed;
    public event EventHandler<PointerPressedEventArgs>? OutputPressed;
    public event EventHandler? DisconnectInputRequested;
    public event EventHandler? DisconnectOutputRequested;

    public ProcessorBox()
    {
        InitializeComponent();
        InputPort.AddHandler(PointerPressedEvent, (_, e) => InputPressed?.Invoke(this, e), RoutingStrategies.Tunnel);
        OutputPort.AddHandler(PointerPressedEvent, (_, e) => OutputPressed?.Invoke(this, e), RoutingStrategies.Tunnel);
        InputPort.ContextMenu = MakeDisconnectMenu("Disconnect input", () => (DataContext as ProcessorViewModel)?.InputConnected == true,
            () => DisconnectInputRequested?.Invoke(this, EventArgs.Empty));
        OutputPort.ContextMenu = MakeDisconnectMenu("Disconnect output", () => (DataContext as ProcessorViewModel)?.OutputConnected == true,
            () => DisconnectOutputRequested?.Invoke(this, EventArgs.Empty));
    }

    internal void HighlightPort(bool input, bool output)
    {
        InputPort.Classes.Set("target", input);
        OutputPort.Classes.Set("target", output);
    }

    private static ContextMenu MakeDisconnectMenu(string label, Func<bool> isConnected, Action disconnect)
    {
        var item = new MenuItem { Header = label };
        item.Click += (_, _) => disconnect();
        var menu = new ContextMenu { ItemsSource = new[] { item } };
        menu.Opening += (_, _) => item.IsEnabled = isConnected();
        return menu;
    }
    private void OnDragPressed(object? sender, PointerPressedEventArgs e) => DragRequested?.Invoke(this, e);
    private void OnInputClicked(object? sender, RoutedEventArgs e) => InputClicked?.Invoke(this, EventArgs.Empty);
    private void OnOutputClicked(object? sender, RoutedEventArgs e) => OutputClicked?.Invoke(this, EventArgs.Empty);
}
