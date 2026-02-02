using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Boutique.Utilities;
using Boutique.ViewModels;

namespace Boutique.Views;

public partial class OutfitCreatorView
{
    private const string ArmorDragDataFormat = "Boutique.ArmorRecords";
    private const string QueueItemDragDataFormat = "Boutique.QueueItem";
    private MainViewModel? _currentViewModel;

    private Point? _outfitDragStartPoint;
    private Point? _draftDragStartPoint;
    private IOutfitQueueItem? _draggedItem;
    private bool _syncingOutfitSelection;

    public static readonly DependencyProperty IsDraggingProperty =
        DependencyProperty.RegisterAttached(
            "IsDragging",
            typeof(bool),
            typeof(OutfitCreatorView),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsDragging(DependencyObject obj) => (bool)obj.GetValue(IsDraggingProperty);
    public static void SetIsDragging(DependencyObject obj, bool value) => obj.SetValue(IsDraggingProperty, value);

    public OutfitCreatorView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        OutfitArmorsGrid.Loaded += (_, _) => SynchronizeOutfitSelection();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e) => AttachToViewModel(ViewModel);

    private void OnUnloaded(object sender, RoutedEventArgs e) => AttachToViewModel(null);

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        AttachToViewModel(e.NewValue as MainViewModel);

    private void AttachToViewModel(MainViewModel? viewModel)
    {
        if (ReferenceEquals(viewModel, _currentViewModel))
        {
            return;
        }

        if (_currentViewModel is not null)
        {
            _currentViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        }

        _currentViewModel = viewModel;

        if (_currentViewModel is null)
        {
            return;
        }

        _currentViewModel.PropertyChanged += ViewModelOnPropertyChanged;
        SynchronizeOutfitSelection();
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
        {
            return;
        }

        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var selected = OutfitArmorsGrid.SelectedItems.Cast<object>().ToList();
        viewModel.SelectedOutfitArmors = selected;
    }

    private void SynchronizeOutfitSelection()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

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
        {
            return;
        }

        var row = FindAncestor<DataGridRow>(source);
        if (row?.Item == null)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None && !OutfitArmorsGrid.SelectedItems.Contains(row.Item))
        {
            OutfitArmorsGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }
    }

    private void OutfitArmorsGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _outfitDragStartPoint == null)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _outfitDragStartPoint.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _outfitDragStartPoint.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _outfitDragStartPoint = null;

        if (ViewModel is null)
        {
            return;
        }

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
        {
            return;
        }

        var data = new DataObject(ArmorDragDataFormat, selected);
        DragDrop.DoDragDrop(OutfitArmorsGrid, data, DragDropEffects.Copy);
        e.Handled = true;
    }

    private async void OutfitArmorsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

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
        {
            return;
        }

        await viewModel.CreateOutfitFromPiecesAsync(pieces);
        e.Handled = true;
    }

    private async void PreviewArmorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ArmorRecordViewModel armor })
        {
            return;
        }

        if (ViewModel is not { } viewModel)
        {
            return;
        }

        await viewModel.PreviewArmorAsync(armor);
    }

    private void AddSeparator_Click(object sender, RoutedEventArgs e) => ViewModel?.AddSeparator();

    private void SeparatorIconButton_Click(object sender, RoutedEventArgs e) =>
        OpenIconPicker((sender as FrameworkElement)?.DataContext as OutfitSeparatorViewModel);

    private void SeparatorIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        OpenIconPicker((sender as FrameworkElement)?.DataContext as OutfitSeparatorViewModel);

    private void OpenIconPicker(OutfitSeparatorViewModel? separator)
    {
        if (separator == null)
        {
            return;
        }

        var (icon, wasCleared) = IconPickerDialog.Show(Window.GetWindow(this), separator.Icon);

        if (wasCleared)
        {
            separator.Icon = null;
        }
        else if (icon != null)
        {
            separator.Icon = icon;
        }
    }

    private void SeparatorName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Grid grid)
        {
            return;
        }

        var textBlock = grid.Children.OfType<TextBlock>().FirstOrDefault();
        var textBox = grid.Children.OfType<TextBox>().FirstOrDefault();
        if (textBlock == null || textBox == null)
        {
            return;
        }

        textBlock.Visibility = Visibility.Collapsed;
        textBox.Visibility = Visibility.Visible;
        textBox.Focus();
        textBox.SelectAll();
        e.Handled = true;
    }

    private void SeparatorNameEditor_LostFocus(object sender, RoutedEventArgs e) =>
        HideSeparatorNameEditor(sender as TextBox);

    private void SeparatorNameEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Escape)
        {
            HideSeparatorNameEditor(sender as TextBox);
            e.Handled = true;
        }
    }

    private static void HideSeparatorNameEditor(TextBox? textBox)
    {
        if (textBox == null)
        {
            return;
        }

        var parent = textBox.Parent as Grid;
        var textBlock = parent?.Children.OfType<TextBlock>().FirstOrDefault();
        if (textBlock == null)
        {
            return;
        }

        textBox.Visibility = Visibility.Collapsed;
        textBlock.Visibility = Visibility.Visible;
    }

    private void OutfitNameTextBox_OnPreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !InputPatterns.Identifier.IsValid(e.Text);

    private void OutfitNameTextBox_OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        if (!e.DataObject.GetDataPresent(DataFormats.Text) ||
            e.DataObject.GetData(DataFormats.Text) is not string rawText)
        {
            e.CancelCommand();
            return;
        }

        var sanitized = InputPatterns.Identifier.Sanitize(rawText);
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

    private void NewOutfitDropZone_OnDrop(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            SetDropTargetState(border, false);
        }

        e.Handled = true; // Handle immediately to let drag-drop modal loop complete

        if (ViewModel is not MainViewModel viewModel)
        {
            return;
        }

        if (!TryExtractArmorRecords(e.Data, out var pieces) || pieces.Count == 0)
        {
            return;
        }

        // Schedule async work after drag-drop modal loop releases
        _ = Dispatcher.InvokeAsync(
            async () => await viewModel.CreateOutfitFromPiecesAsync(pieces),
            DispatcherPriority.Background);
    }

    private void QueueItem_OnDragEnter(object sender, DragEventArgs e) =>
        UpdateDropIndicator(sender as Grid, e);

    private void QueueItem_OnDragOver(object sender, DragEventArgs e) =>
        UpdateDropIndicator(sender as Grid, e);

    private void QueueItem_OnDragLeave(object sender, DragEventArgs e)
    {
        HideAllIndicators(sender as Grid);
        ClearConflictHighlights(sender as Grid);
        e.Handled = true;
    }

    private void QueueItem_OnDrop(object sender, DragEventArgs e)
    {
        var insertBefore = IsInTopHalf(sender as Grid, e);
        HideAllIndicators(sender as Grid);
        ClearConflictHighlights(sender as Grid);

        if (ViewModel is not MainViewModel viewModel)
        {
            e.Handled = true;
            return;
        }

        if (sender is not Grid { DataContext: IOutfitQueueItem targetItem })
        {
            e.Handled = true;
            return;
        }

        if (TryExtractQueueItem(e.Data, out var sourceItem) && sourceItem != targetItem)
        {
            if (sourceItem is OutfitSeparatorViewModel sourceSeparator)
            {
                var groupedItems = viewModel.GetGroupedItems(sourceSeparator);
                if (groupedItems.Contains(targetItem))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (sourceItem is OutfitDraftViewModel && targetItem is OutfitSeparatorViewModel targetSeparator)
            {
                var groupedItems = viewModel.GetGroupedItems(targetSeparator);
                var lastItem = groupedItems[^1];
                var lastIndex = viewModel.OutfitDrafts.IndexOf(lastItem);
                if (lastIndex >= 0)
                {
                    viewModel.MoveDraft(sourceItem, lastIndex, insertBefore: false);
                }

                e.Handled = true;
                return;
            }

            var targetIndex = viewModel.OutfitDrafts.IndexOf(targetItem);
            if (targetIndex >= 0)
            {
                viewModel.MoveDraft(sourceItem, targetIndex, insertBefore);
            }

            e.Handled = true;
            return;
        }

        if (targetItem is OutfitDraftViewModel targetDraft &&
            TryExtractArmorRecords(e.Data, out var pieces) && pieces.Count > 0)
        {
            viewModel.TryAddPiecesToDraft(targetDraft, pieces);
        }

        e.Handled = true;
    }

    private void UpdateDropIndicator(Grid? container, DragEventArgs e)
    {
        if (container == null)
        {
            return;
        }

        if (HasQueueItem(e.Data) && TryExtractQueueItem(e.Data, out var sourceItem))
        {
            if (sourceItem is OutfitSeparatorViewModel sourceSeparator &&
                container.DataContext is IOutfitQueueItem targetItem &&
                ViewModel is MainViewModel viewModel)
            {
                var groupedItems = viewModel.GetGroupedItems(sourceSeparator);
                if (groupedItems.Contains(targetItem))
                {
                    HideAllIndicators(container);
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }
            }

            if (sourceItem is OutfitDraftViewModel &&
                container.DataContext is OutfitSeparatorViewModel)
            {
                HideDropIndicators(container);
                SetSeparatorHighlight(container, true);
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                SetSeparatorHighlight(container, false);
                var topHalf = IsInTopHalf(container, e);
                ShowDropIndicator(container, topHalf);
                e.Effects = DragDropEffects.Move;
            }
        }
        else if (HasArmorRecords(e.Data))
        {
            HideAllIndicators(container);
            UpdateConflictHighlights(container, e.Data);
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            HideAllIndicators(container);
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private static void UpdateConflictHighlights(Grid? container, IDataObject data)
    {
        if (container?.DataContext is not OutfitDraftViewModel draft)
        {
            return;
        }

        if (!TryExtractArmorRecords(data, out var draggedPieces))
        {
            return;
        }

        foreach (var existingPiece in draft.Pieces)
        {
            existingPiece.IsConflicting = draggedPieces.Any(dp => dp.ConflictsWithSlot(existingPiece));
        }
    }

    private static void ClearConflictHighlights(Grid? container)
    {
        if (container?.DataContext is not OutfitDraftViewModel draft)
        {
            return;
        }

        foreach (var piece in draft.Pieces)
        {
            piece.IsConflicting = false;
        }
    }

    private static bool IsInTopHalf(Grid? container, DragEventArgs e)
    {
        if (container == null)
        {
            return true;
        }

        var position = e.GetPosition(container);
        return position.Y < container.ActualHeight / 2;
    }

    private static void ShowDropIndicator(Grid container, bool showTop)
    {
        var topIndicator = FindChild<Border>(container, "DropIndicatorTop");
        var bottomIndicator = FindChild<Border>(container, "DropIndicatorBottom");

        topIndicator?.Visibility = showTop ? Visibility.Visible : Visibility.Collapsed;

        bottomIndicator?.Visibility = showTop ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void HideDropIndicators(Grid? container)
    {
        if (container == null)
        {
            return;
        }

        var topIndicator = FindChild<Border>(container, "DropIndicatorTop");
        var bottomIndicator = FindChild<Border>(container, "DropIndicatorBottom");

        topIndicator?.Visibility = Visibility.Collapsed;

        bottomIndicator?.Visibility = Visibility.Collapsed;
    }

    private static void SetSeparatorHighlight(Grid? container, bool highlight)
    {
        if (container == null)
        {
            return;
        }

        var separatorBorder = FindChild<Border>(container, "SeparatorBorder");
        if (separatorBorder == null)
        {
            return;
        }

        separatorBorder.BorderBrush = highlight
            ? new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4))
            : Brushes.Transparent;
    }

    private static void HideAllIndicators(Grid? container)
    {
        HideDropIndicators(container);
        SetSeparatorHighlight(container, false);
    }

    private static T? FindChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name)
            {
                return element;
            }

            var result = FindChild<T>(child, name);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: IOutfitQueueItem item })
        {
            return;
        }

        _draftDragStartPoint = e.GetPosition(null);
        _draggedItem = item;
        e.Handled = true;
    }

    private void DragHandle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draftDragStartPoint == null || _draggedItem == null)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _draftDragStartPoint.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _draftDragStartPoint.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = _draggedItem;
        _draftDragStartPoint = null;
        _draggedItem = null;

        SetIsDragging(this, true);
        try
        {
            var data = new DataObject(QueueItemDragDataFormat, item);
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        }
        finally
        {
            SetIsDragging(this, false);
        }

        e.Handled = true;
    }

    private void DragHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draftDragStartPoint = null;
        _draggedItem = null;
    }

    private static void HandleDropTargetDrag(Border? border, DragEventArgs e)
    {
        if (border == null)
        {
            return;
        }

        if (HasQueueItem(e.Data))
        {
            SetDropTargetState(border, true);
            e.Effects = DragDropEffects.Move;
        }
        else if (HasArmorRecords(e.Data))
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

    private static bool HasArmorRecords(IDataObject data) => data.GetDataPresent(ArmorDragDataFormat);

    private static bool HasQueueItem(IDataObject data) => data.GetDataPresent(QueueItemDragDataFormat);

    private static bool TryExtractQueueItem(IDataObject data, out IOutfitQueueItem item)
    {
        item = null!;
        if (!HasQueueItem(data))
        {
            return false;
        }

        if (data.GetData(QueueItemDragDataFormat) is IOutfitQueueItem i)
        {
            item = i;
            return true;
        }

        return false;
    }

    private static bool TryExtractArmorRecords(IDataObject data, out List<ArmorRecordViewModel> pieces)
    {
        if (!HasArmorRecords(data))
        {
            pieces = [];
            return false;
        }

        if (data.GetData(ArmorDragDataFormat) is IEnumerable<ArmorRecordViewModel> records)
        {
            pieces = records
                .Where(r => r != null)
                .ToList();
            return pieces.Count > 0;
        }

        pieces = [];
        return false;
    }

    private static void SetDropTargetState(Border? border, bool isActive)
    {
        if (border == null)
        {
            return;
        }

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

    private static ArmorRecordViewModel? GetArmorRecordFromEvent(MouseEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return null;
        }

        var row = FindAncestor<DataGridRow>(source);
        return row?.Item as ArmorRecordViewModel;
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private sealed record DropVisualSnapshot(Brush BorderBrush, Brush Background);
}
