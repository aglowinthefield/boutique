using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Boutique.Controls;

public class FilterableSelector : Control
{
    private TextBox? _textBox;
    private ListBox? _listBox;
    private ListCollectionView? _filteredView;
    private bool _isUpdatingText;

    static FilterableSelector()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(FilterableSelector),
            new FrameworkPropertyMetadata(typeof(FilterableSelector)));
    }

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(FilterableSelector),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(
            nameof(SelectedItem),
            typeof(object),
            typeof(FilterableSelector),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

    public static readonly DependencyProperty DisplayMemberPathProperty =
        DependencyProperty.Register(
            nameof(DisplayMemberPath),
            typeof(string),
            typeof(FilterableSelector),
            new PropertyMetadata(null));

    public static readonly DependencyProperty FilterPathProperty =
        DependencyProperty.Register(
            nameof(FilterPath),
            typeof(string),
            typeof(FilterableSelector),
            new PropertyMetadata(null));

    public static readonly DependencyProperty MaxDropDownHeightProperty =
        DependencyProperty.Register(
            nameof(MaxDropDownHeight),
            typeof(double),
            typeof(FilterableSelector),
            new PropertyMetadata(300.0));

    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register(
            nameof(IsDropDownOpen),
            typeof(bool),
            typeof(FilterableSelector),
            new PropertyMetadata(false, OnIsDropDownOpenChanged));

    public event EventHandler? DropDownOpened;

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public string? DisplayMemberPath
    {
        get => (string?)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    public string? FilterPath
    {
        get => (string?)GetValue(FilterPathProperty);
        set => SetValue(FilterPathProperty, value);
    }

    public double MaxDropDownHeight
    {
        get => (double)GetValue(MaxDropDownHeightProperty);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _textBox = GetTemplateChild("PART_TextBox") as TextBox;
        _listBox = GetTemplateChild("PART_ListBox") as ListBox;

        if (_textBox != null)
        {
            _textBox.TextChanged += OnTextChanged;
            _textBox.GotFocus += OnTextBoxGotFocus;
            _textBox.LostFocus += OnTextBoxLostFocus;
            _textBox.PreviewKeyDown += OnTextBoxKeyDown;
        }

        if (_listBox != null)
        {
            _listBox.SelectionChanged += OnListBoxSelectionChanged;
            _listBox.PreviewMouseLeftButtonUp += OnListBoxMouseUp;
        }

        SetupFilteredView();
        UpdateTextFromSelection();
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FilterableSelector selector)
            selector.SetupFilteredView();
    }

    private static void OnIsDropDownOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FilterableSelector selector && e.NewValue is true)
            selector.DropDownOpened?.Invoke(selector, EventArgs.Empty);
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FilterableSelector selector)
            selector.UpdateTextFromSelection();
    }

    private void SetupFilteredView()
    {
        if (ItemsSource is IList list)
        {
            _filteredView = new ListCollectionView(list);
            if (_listBox != null)
                _listBox.ItemsSource = _filteredView;
        }
    }

    private void UpdateTextFromSelection()
    {
        if (_textBox == null || _isUpdatingText)
            return;

        _isUpdatingText = true;
        try
        {
            if (SelectedItem != null && !string.IsNullOrEmpty(DisplayMemberPath))
            {
                var value = GetPropertyValue(SelectedItem, DisplayMemberPath);
                _textBox.Text = value ?? string.Empty;
            }
            else
            {
                _textBox.Text = SelectedItem?.ToString() ?? string.Empty;
            }
        }
        finally
        {
            _isUpdatingText = false;
        }
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingText || _filteredView == null)
            return;

        var filterText = _textBox?.Text?.Trim() ?? "";
        var filterPath = FilterPath ?? DisplayMemberPath;

        if (string.IsNullOrEmpty(filterText))
        {
            _filteredView.Filter = null;
        }
        else
        {
            _filteredView.Filter = item =>
            {
                if (item == null || string.IsNullOrEmpty(filterPath))
                    return true;

                var value = GetPropertyValue(item, filterPath);
                return value?.Contains(filterText, StringComparison.OrdinalIgnoreCase) == true;
            };
        }

        if (!IsDropDownOpen && !string.IsNullOrEmpty(filterText))
            IsDropDownOpen = true;
    }

    private void OnTextBoxGotFocus(object sender, RoutedEventArgs e)
    {
        _textBox?.SelectAll();
        IsDropDownOpen = true;
    }

    private void OnTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        // Delay closing to allow click on listbox item
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_listBox?.IsKeyboardFocusWithin != true)
                IsDropDownOpen = false;
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (_listBox == null)
            return;

        switch (e.Key)
        {
            case Key.Down:
                if (!IsDropDownOpen)
                    IsDropDownOpen = true;
                else if (_listBox.Items.Count > 0)
                {
                    _listBox.SelectedIndex = Math.Min(_listBox.SelectedIndex + 1, _listBox.Items.Count - 1);
                    _listBox.ScrollIntoView(_listBox.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Up:
                if (_listBox.Items.Count > 0 && _listBox.SelectedIndex > 0)
                {
                    _listBox.SelectedIndex--;
                    _listBox.ScrollIntoView(_listBox.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Enter:
                if (IsDropDownOpen && _listBox.SelectedItem != null)
                {
                    SelectItem(_listBox.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Escape:
                IsDropDownOpen = false;
                UpdateTextFromSelection();
                e.Handled = true;
                break;
        }
    }

    private void OnListBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Don't auto-select on navigation, wait for click or Enter
    }

    private void OnListBoxMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_listBox?.SelectedItem != null)
            SelectItem(_listBox.SelectedItem);
    }

    private void SelectItem(object item)
    {
        SelectedItem = item;
        IsDropDownOpen = false;
        _filteredView?.Refresh();
        _filteredView!.Filter = null;
        UpdateTextFromSelection();
    }

    private static string? GetPropertyValue(object item, string propertyPath)
    {
        var type = item.GetType();
        var prop = type.GetProperty(propertyPath);
        return prop?.GetValue(item)?.ToString();
    }
}
