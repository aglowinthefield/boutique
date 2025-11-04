using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RequiemGlamPatcher.ViewModels;

namespace RequiemGlamPatcher.Views;

public partial class OutfitCreatorView : UserControl
{
    private const string ArmorDragDataFormat = "RequiemGlamPatcher.ArmorRecords";
    private static readonly Regex AlphaInputRegex = new("^[A-Za-z]+$", RegexOptions.Compiled);
    private static readonly Regex AlphaSanitizerRegex = new("[^A-Za-z]", RegexOptions.Compiled);

    private sealed record DropVisualSnapshot(Brush BorderBrush, Brush Background);

    private Point? _outfitDragStartPoint;
    private bool _syncingOutfitSelection;
    private MainViewModel? _currentViewModel;

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    public OutfitCreatorView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        OutfitArmorsGrid.Loaded += (_, _) => SynchronizeOutfitSelection();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachToViewModel(ViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AttachToViewModel(null);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        AttachToViewModel(e.NewValue as MainViewModel);
    }

    private void AttachToViewModel(MainViewModel? viewModel)
    {
        if (ReferenceEquals(viewModel, _currentViewModel))
            return;

        if (_currentViewModel is not null)
        {
            _currentViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        }

        _currentViewModel = viewModel;

        if (_currentViewModel is not null)
        {
            _currentViewModel.PropertyChanged += ViewModelOnPropertyChanged;
            SynchronizeOutfitSelection();
        }
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedOutfitArmors))
        {
            SynchronizeOutfitSelection();
        }
    }

    private void OutfitArmorsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingOutfitSelection)
            return;

        if (ViewModel is not MainViewModel viewModel)
            return;

        var selected = OutfitArmorsGrid.SelectedItems.Cast<object>().ToList();
        viewModel.SelectedOutfitArmors = selected;
    }

    private void SynchronizeOutfitSelection()
    {
        if (ViewModel is not MainViewModel viewModel)
            return;

        _syncingOutfitSelection = true;
        try
        {
            OutfitArmorsGrid.SelectedItems.Clear();
            foreach (var armor in viewModel.SelectedOutfitArmors.OfType<object>())
            {
                OutfitArmorsGrid.SelectedItems.Add(armor);
            }
        }
        finally
        {
            _syncingOutfitSelection = false;
        }
    }

    private void OutfitArmorsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _outfitDragStartPoint = e.GetPosition(null);

        if (e.OriginalSource is not DependencyObject source)
            return;

        var row = FindAncestor<DataGridRow>(source);
        if (row?.Item == null)
            return;

        if (Keyboard.Modifiers == ModifierKeys.None && !OutfitArmorsGrid.SelectedItems.Contains(row.Item))
        {
            OutfitArmorsGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }
    }

    private void OutfitArmorsGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _outfitDragStartPoint == null)
            return;

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _outfitDragStartPoint.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _outfitDragStartPoint.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _outfitDragStartPoint = null;

        if (ViewModel is not MainViewModel viewModel)
            return;

        var selected = OutfitArmorsGrid.SelectedItems
            .OfType<ArmorRecordViewModel>()
            .ToList();

        if (selected.Count == 0)
        {
            var underMouse = GetArmorRecordFromEvent(e);
            if (underMouse != null)
            {
                selected.Add(underMouse);
            }
        }

        if (selected.Count == 0)
            return;

        var data = new DataObject(ArmorDragDataFormat, selected);
        DragDrop.DoDragDrop(OutfitArmorsGrid, data, DragDropEffects.Copy);
        e.Handled = true;
    }

    private async void OutfitArmorsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not MainViewModel viewModel)
            return;

        var pieces = OutfitArmorsGrid.SelectedItems
            .OfType<ArmorRecordViewModel>()
            .ToList();

        if (pieces.Count == 0)
        {
            var underMouse = GetArmorRecordFromEvent(e);
            if (underMouse != null)
            {
                pieces.Add(underMouse);
            }
        }

        if (pieces.Count == 0)
            return;

        await viewModel.CreateOutfitFromPiecesAsync(pieces);
        e.Handled = true;
    }

    private void OutfitNameTextBox_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !AlphaInputRegex.IsMatch(e.Text);
    }

    private void OutfitNameTextBox_OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        if (e.DataObject.GetData(DataFormats.Text) is not string rawText)
        {
            e.CancelCommand();
            return;
        }

        var sanitized = AlphaSanitizerRegex.Replace(rawText, string.Empty);
        if (string.IsNullOrEmpty(sanitized))
        {
            e.CancelCommand();
            return;
        }

        var selectionStart = textBox.SelectionStart;
        var selectionLength = textBox.SelectionLength;

        var newText = textBox.Text.Remove(selectionStart, selectionLength);
        newText = newText.Insert(selectionStart, sanitized);

        textBox.Text = newText;
        textBox.SelectionStart = selectionStart + sanitized.Length;
        textBox.SelectionLength = 0;
        e.CancelCommand();
    }

    private void NewOutfitDropZone_OnDragEnter(object sender, DragEventArgs e) =>
        HandleDropTargetDrag(sender as Border, e);

    private void NewOutfitDropZone_OnDragOver(object sender, DragEventArgs e) =>
        HandleDropTargetDrag(sender as Border, e);

    private void NewOutfitDropZone_OnDragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            SetDropTargetState(border, false);
        }

        e.Handled = true;
    }

    private async void NewOutfitDropZone_OnDrop(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            SetDropTargetState(border, false);
        }

        if (ViewModel is not MainViewModel viewModel)
        {
            e.Handled = true;
            return;
        }

        if (!TryExtractArmorRecords(e.Data, out var pieces) || pieces.Count == 0)
        {
            e.Handled = true;
            return;
        }

        await viewModel.CreateOutfitFromPiecesAsync(pieces);
        e.Handled = true;
    }

    private void OutfitDraftBorder_OnDragEnter(object sender, DragEventArgs e) =>
        HandleDropTargetDrag(sender as Border, e);

    private void OutfitDraftBorder_OnDragOver(object sender, DragEventArgs e) =>
        HandleDropTargetDrag(sender as Border, e);

    private void OutfitDraftBorder_OnDragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            SetDropTargetState(border, false);
        }

        e.Handled = true;
    }

    private void OutfitDraftBorder_OnDrop(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            SetDropTargetState(border, false);
        }

        if (ViewModel is not MainViewModel viewModel)
        {
            e.Handled = true;
            return;
        }

        if (sender is not Border draftBorder || draftBorder.DataContext is not OutfitDraftViewModel draft)
        {
            e.Handled = true;
            return;
        }

        if (!TryExtractArmorRecords(e.Data, out var pieces) || pieces.Count == 0)
        {
            e.Handled = true;
            return;
        }

        viewModel.TryAddPiecesToDraft(draft, pieces);
        e.Handled = true;
    }

    private void HandleDropTargetDrag(Border? border, DragEventArgs e)
    {
        if (border == null)
            return;

        if (HasArmorRecords(e.Data))
        {
            SetDropTargetState(border, true);
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            SetDropTargetState(border, false);
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private static bool HasArmorRecords(IDataObject data) =>
        data.GetDataPresent(ArmorDragDataFormat);

    private static bool TryExtractArmorRecords(IDataObject data, out List<ArmorRecordViewModel> pieces)
    {
        if (!HasArmorRecords(data))
        {
            pieces = new List<ArmorRecordViewModel>();
            return false;
        }

        if (data.GetData(ArmorDragDataFormat) is IEnumerable<ArmorRecordViewModel> records)
        {
            pieces = records
                .Where(r => r != null)
                .ToList();
            return pieces.Count > 0;
        }

        pieces = new List<ArmorRecordViewModel>();
        return false;
    }

    private void SetDropTargetState(Border? border, bool isActive)
    {
        if (border == null)
            return;

        if (border.Tag is not DropVisualSnapshot snapshot)
        {
            snapshot = new DropVisualSnapshot(border.BorderBrush, border.Background);
            border.Tag = snapshot;
        }

        if (isActive)
        {
            border.BorderBrush = Brushes.DodgerBlue;
            border.Background = new SolidColorBrush(Color.FromArgb(48, 30, 144, 255));
        }
        else
        {
            border.BorderBrush = snapshot.BorderBrush;
            border.Background = snapshot.Background;
        }
    }

    private ArmorRecordViewModel? GetArmorRecordFromEvent(MouseEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return null;

        var row = FindAncestor<DataGridRow>(source);
        return row?.Item as ArmorRecordViewModel;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
