using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using SharpAmp.Avalonia.Models;
using SharpAmp.Avalonia.ViewModels;

namespace SharpAmp.Avalonia.Views;

public partial class ProcessorChainView : UserControl
{
    private ProcessorChainViewModel? Model => DataContext as ProcessorChainViewModel;
    private ApplicationPreferences? preferences;
    private bool? compact;
    private bool fileOperation;
    private EffectDescriptor? draggedEffect;
    private IPointer? libraryPointer;
    private Point libraryPressPoint;
    private bool libraryDragStarted;
    private static readonly FilePickerFileType JsonFiles = new("SharpAmp JSON") { Patterns = ["*.json"] };

    public ProcessorChainView()
    {
        InitializeComponent();
        Focusable = true;
        BoardScroll.ScrollChanged += (_, _) => UpdateBoardViewport();
        BoardScroll.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.ViewportProperty || e.Property == ScrollViewer.OffsetProperty)
                UpdateBoardViewport();
        };
        SizeChanged += (_, _) => UpdateLayoutMode();
        AttachedToVisualTree += (_, _) =>
        {
            preferences = (TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel)?.Settings.Preferences;
            if (preferences is not null) preferences.PropertyChanged += PreferencesChanged;
            ApplyPreferences();
            UpdateLayoutMode();
            UpdateBoardViewport();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            CancelLibraryDrag();
            if (preferences is not null) preferences.PropertyChanged -= PreferencesChanged;
            preferences = null;
        };
    }

    private void UpdateBoardViewport() => Board.UpdateViewport(BoardScroll.Viewport, BoardScroll.Offset);

    private void PreferencesChanged(object? sender, PropertyChangedEventArgs e) => ApplyPreferences();
    private void ApplyPreferences()
    {
        Board.SnapToGrid = preferences?.SnapToGrid ?? true;
        HelpText.IsVisible = preferences?.ShowHints ?? true;
        BoardHint.IsVisible = compact != true && HelpText.IsVisible;
    }

    private void UpdateLayoutMode()
    {
        var next = Bounds.Width < 1080;
        if (compact != next)
        {
            compact = next;
            Library.IsVisible = !next;
            QuickEffectPicker.IsVisible = next;
            QuickAddButton.IsVisible = next;
            Workspace.ColumnDefinitions = new ColumnDefinitions(next ? "*,264" : "212,*,292");
            Grid.SetColumn(BoardBorder, next ? 0 : 1);
            Grid.SetColumn(Inspector, next ? 1 : 2);
        }
        // Side panes grow with the window, with caps that leave the board most of a large screen.
        if (next)
            Workspace.ColumnDefinitions[1].Width = new GridLength(Math.Clamp(Bounds.Width * 0.29, 252, 300));
        else
        {
            Workspace.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(Bounds.Width * 0.17, 212, 320));
            Workspace.ColumnDefinitions[2].Width = new GridLength(Math.Clamp(Bounds.Width * 0.23, 292, 400));
        }
        ApplyPreferences();
    }

    private void OnLibraryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Mouse || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || (sender as Control)?.DataContext is not EffectDescriptor effect) return;
        // The explicit add button keeps its normal click and keyboard behavior.
        if (e.Source is Button || (e.Source as Visual)?.GetVisualAncestors().OfType<Button>().Any() == true) return;
        draggedEffect = effect;
        libraryPointer = e.Pointer;
        libraryPressPoint = e.GetPosition(this);
        libraryDragStarted = false;
        Focus();
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (draggedEffect is null || libraryPointer != e.Pointer) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { CancelLibraryDrag(); return; }
        var distance = e.GetPosition(this) - libraryPressPoint;
        if (!libraryDragStarted && distance.X * distance.X + distance.Y * distance.Y < 36) return;
        libraryDragStarted = true;
        var canDrop = IsOverBoard(e) && Model?.CanAddEffects == true;
        Cursor = new Cursor(canDrop ? StandardCursorType.DragCopy : StandardCursorType.No);
        Board.ShowDropPreview(canDrop ? draggedEffect.Name : null, e.GetPosition(Board));
        e.Handled = true;
    }

    private bool IsOverBoard(PointerEventArgs e)
    {
        var presenter = BoardScroll.GetVisualDescendants().OfType<ScrollContentPresenter>().FirstOrDefault();
        return presenter is not null && new Rect(presenter.Bounds.Size).Contains(e.GetPosition(presenter));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (draggedEffect is { } effect && libraryPointer == e.Pointer)
        {
            var drop = libraryDragStarted && IsOverBoard(e);
            var position = Board.GetDropPosition(e.GetPosition(Board));
            CancelLibraryDrag();
            if (drop) Edit(() =>
            {
                var node = Model!.AddEffect(effect.Type);
                node.X = position.X;
                node.Y = position.Y;
                Board.Focus();
                Model.StatusMessage = $"Added {node.Name}, unconnected. Drag between ports to wire it into your sound.";
            });
            e.Handled = true;
        }
        base.OnPointerReleased(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (libraryPointer == e.Pointer) CancelLibraryDrag();
        base.OnPointerCaptureLost(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && draggedEffect is not null)
        {
            CancelLibraryDrag();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void CancelLibraryDrag()
    {
        var pointer = libraryPointer;
        libraryPointer = null;
        draggedEffect = null;
        libraryDragStarted = false;
        Cursor = null;
        Board.ShowDropPreview(null, default);
        if (pointer?.Captured == this) pointer.Capture(null);
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (Model is not { } vm || EffectLibrary is null) return;
        var query = EffectSearch.Text?.Trim() ?? "";
        EffectLibrary.ItemsSource = vm.AvailableEffects.Where(effect =>
            effect.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            effect.Category.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void OnAddEffect(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is EffectDescriptor effect) AddEffect(effect);
    }
    private void OnQuickAdd(object? sender, RoutedEventArgs e)
    {
        if (QuickEffectPicker.SelectedItem is EffectDescriptor effect) AddEffect(effect);
    }
    private void AddEffect(EffectDescriptor effect) => Edit(() => { Model!.AddEffect(effect.Type); RevealSelection(); });
    private void OnRemove(object? sender, RoutedEventArgs e) => Edit(() => Model!.RemoveSelected());
    private void OnDuplicate(object? sender, RoutedEventArgs e) => Edit(() => { Model!.DuplicateSelected(); RevealSelection(); });
    private void OnEarlier(object? sender, RoutedEventArgs e) => Edit(() => Model!.MoveSelected(-1));
    private void OnLater(object? sender, RoutedEventArgs e) => Edit(() => Model!.MoveSelected(1));
    private void OnArrange(object? sender, RoutedEventArgs e) => Edit(() =>
    {
        Model!.Arrange(Math.Max(1, (int)((BoardScroll.Bounds.Width - 180) / 316)));
        BoardScroll.Offset = default;
    });
    private void RevealSelection()
    {
        if (Model?.SelectedProcessor is { } selected)
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => BoardScroll.Offset = new Vector(
                Math.Max(0, selected.X - 70), Math.Max(0, selected.Y - 70)));
    }
    private void Edit(Action action)
    {
        if (Model is null) return;
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { Model.StatusMessage = error.Message; }
    }

    private async void OnNew(object? sender, RoutedEventArgs e)
    {
        if (fileOperation) return;
        if (await ConfirmDiscardAsync()) { Model?.NewBoard(); BoardScroll.Offset = default; }
    }
    private async void OnOpen(object? sender, RoutedEventArgs e) => await OpenAsync(false);
    private async void OnSave(object? sender, RoutedEventArgs e) => await SaveAsync(false);
    private async void OnOpenLayout(object? sender, RoutedEventArgs e) => await OpenAsync(true);
    private async void OnSaveLayout(object? sender, RoutedEventArgs e) => await SaveAsync(true);

    private async Task OpenAsync(bool layout)
    {
        if (fileOperation || Model is not { } vm || TopLevel.GetTopLevel(this) is not { } top) return;
        if (!layout && !await ConfirmDiscardAsync()) return;
        fileOperation = true;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = layout ? "Load pedalboard layout" : "Open sound preset",
                AllowMultiple = false, FileTypeFilter = [JsonFiles]
            });
            if (files.Count == 0) return;
            using var file = files[0];
            await using var stream = await file.OpenReadAsync();
            // A board of 64 effects fits comfortably; reject unexpectedly large files before parsing.
            using var reader = new StreamReader(stream);
            var buffer = new char[1_048_577];
            var length = await reader.ReadBlockAsync(buffer.AsMemory());
            if (length == buffer.Length) throw new InvalidDataException("This file is too large for a SharpAmp preset or layout.");
            var json = new string(buffer, 0, length);
            if (layout) vm.LoadLayoutJson(json); else vm.LoadPresetJson(json);
            BoardScroll.Offset = default;
        }
        catch (Exception error) { vm.StatusMessage = $"Could not load: {error.Message}"; }
        finally { fileOperation = false; }
    }

    private async Task<bool> SaveAsync(bool layout)
    {
        if (fileOperation || Model is not { } vm || TopLevel.GetTopLevel(this) is not { } top) return false;
        fileOperation = true;
        try
        {
            var filename = string.Concat(vm.PresetName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));
            using var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = layout ? "Save pedalboard layout" : "Save sound preset",
                SuggestedFileName = filename + (layout ? ".layout.json" : ".sharpamp.json"),
                DefaultExtension = "json", FileTypeChoices = [JsonFiles], ShowOverwritePrompt = true
            });
            if (file is null) return false;
            var json = layout ? vm.ToLayoutJson() : vm.ToPresetJson();
            if (file.TryGetLocalPath() is { } path)
            {
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(temporary, json);
                    File.Move(temporary, path, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            else
            {
                await using var stream = await file.OpenWriteAsync();
                if (stream.CanSeek) stream.SetLength(0);
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(json);
            }
            if (!layout && vm.ToPresetJson() == json) vm.MarkSaved();
            vm.StatusMessage = layout ? $"Layout saved to {file.Name}." : $"Preset saved to {file.Name}.";
            return true;
        }
        catch (Exception error) { vm.StatusMessage = $"Could not save: {error.Message}"; return false; }
        finally { fileOperation = false; }
    }

    public async Task<bool> ConfirmDiscardAsync()
    {
        if (fileOperation) return false;
        if (Model?.HasChanges != true) return true;
        if (TopLevel.GetTopLevel(this) is not Window owner) return false;
        var result = await CreateUnsavedChangesDialog().ShowDialog<string?>(owner);
        return result == "discard" || (result == "save" && await SaveAsync(false) && Model?.HasChanges != true);
    }

    private static Window CreateUnsavedChangesDialog()
    {
        var dialog = new Window
        {
            Title = "Save your sound?", Width = 460, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = global::Avalonia.Media.Brush.Parse("#191E23")
        };
        var save = new Button { Content = "Save preset", Classes = { "primary" } };
        var discard = new Button { Content = "Discard changes", Classes = { "quiet" } };
        var cancel = new Button { Content = "Cancel", Classes = { "quiet" } };
        save.Click += (_, _) => dialog.Close("save");
        discard.Click += (_, _) => dialog.Close("discard");
        cancel.Click += (_, _) => dialog.Close("cancel");
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 18,
            Children =
            {
                new TextBlock { Text = "Keep your changes?", FontSize = 22, FontWeight = global::Avalonia.Media.FontWeight.SemiBold },
                new TextBlock { Text = "This sound has unsaved changes. Save a preset to return to it later.", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10, Children = { cancel, discard, save } }
            }
        };
        return dialog;
    }
}
