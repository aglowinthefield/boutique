using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Boutique.ViewModels;

namespace Boutique.Views;

public partial class ArmorPatchView : UserControl
{
    private MainViewModel? _currentViewModel;
    private bool _syncingSourceSelection;

    public ArmorPatchView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        SourceArmorsGrid.Loaded += (_, _) => SynchronizeSourceSelection();
        TargetArmorsGrid.Loaded += TargetArmorsGridOnLoaded;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e) => AttachToViewModel(ViewModel);

    private void OnUnloaded(object sender, RoutedEventArgs e) => AttachToViewModel(null);

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => AttachToViewModel(e.NewValue as MainViewModel);

    private void AttachToViewModel(MainViewModel? viewModel)
    {
        if (ReferenceEquals(viewModel, _currentViewModel))
            return;

        if (_currentViewModel is not null)
            _currentViewModel.PropertyChanged -= ViewModelOnPropertyChanged;

        _currentViewModel = viewModel;

        if (_currentViewModel is not null)
        {
            _currentViewModel.PropertyChanged += ViewModelOnPropertyChanged;
            SynchronizeSourceSelection();
        }
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedSourceArmors))
            SynchronizeSourceSelection();
    }

    private void TargetArmorsGridOnLoaded(object sender, RoutedEventArgs e)
    {
        if (TargetArmorsGrid.Columns.Count > 0)
            TargetArmorsGrid.Columns[0].SortDirection = ListSortDirection.Ascending;
    }

    private void TargetArmorsDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (ViewModel is not MainViewModel viewModel)
            return;

        e.Handled = true;

        var dataGrid = (DataGrid)sender;
        var newDirection = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        foreach (var column in dataGrid.Columns)
        {
            if (!ReferenceEquals(column, e.Column))
                column.SortDirection = null;
        }

        e.Column.SortDirection = newDirection;

        var sortMember = e.Column.SortMemberPath;
        if (string.IsNullOrWhiteSpace(sortMember) && e.Column is DataGridBoundColumn boundColumn)
        {
            if (boundColumn.Binding is Binding binding && binding.Path != null)
                sortMember = binding.Path.Path;
        }

        if (string.IsNullOrWhiteSpace(sortMember))
            sortMember = nameof(ArmorRecordViewModel.DisplayName);

        viewModel.ApplyTargetSort(sortMember, newDirection);
    }

    private void SourceArmorsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSourceSelection)
            return;

        if (ViewModel is not MainViewModel viewModel)
            return;

        var selected = SourceArmorsGrid.SelectedItems.Cast<object>().ToList();
        viewModel.SelectedSourceArmors = selected;
    }

    private void SynchronizeSourceSelection()
    {
        if (ViewModel is not MainViewModel viewModel)
            return;

        _syncingSourceSelection = true;
        try
        {
            SourceArmorsGrid.SelectedItems.Clear();
            foreach (var armor in viewModel.SelectedSourceArmors.OfType<object>())
                SourceArmorsGrid.SelectedItems.Add(armor);
        }
        finally
        {
            _syncingSourceSelection = false;
        }
    }
}
