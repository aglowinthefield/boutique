using System.Reactive;
using System.Reactive.Disposables;
using System.Windows;
using Boutique.ViewModels;
using Microsoft.VisualBasic;

namespace Boutique.Views;

public partial class MainWindow : Window
{
    private readonly CompositeDisposable _bindings = new();
    private bool _initialized;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        var notificationDisposable = viewModel.PatchCreatedNotification.RegisterHandler(async interaction =>
        {
            var message = interaction.Input;
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show(this, message, "Patch Created", MessageBoxButton.OK, MessageBoxImage.Information));
            interaction.SetOutput(Unit.Default);
        });
        _bindings.Add(notificationDisposable);

        var confirmDisposable = viewModel.ConfirmOverwritePatch.RegisterHandler(async interaction =>
        {
            var message = interaction.Input;
            var result = await Dispatcher.InvokeAsync(() =>
                MessageBox.Show(this, message, "Overwrite Existing Patch?", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No));
            interaction.SetOutput(result == MessageBoxResult.Yes);
        });
        _bindings.Add(confirmDisposable);

        var outfitNameDisposable = viewModel.RequestOutfitName.RegisterHandler(async interaction =>
        {
            var prompt = interaction.Input;
            var result = await Dispatcher.InvokeAsync(() =>
            {
                var input = Interaction.InputBox(prompt, "Create Outfit", string.Empty);
                return string.IsNullOrWhiteSpace(input) ? null : input;
            });
            interaction.SetOutput(result);
        });
        _bindings.Add(outfitNameDisposable);

        var previewDisposable = viewModel.ShowPreview.RegisterHandler(async interaction =>
        {
            var scene = interaction.Input;
            await Dispatcher.InvokeAsync(() =>
            {
                var window = new OutfitPreviewWindow(scene)
                {
                    Owner = this
                };
                window.Show();
            });
            interaction.SetOutput(Unit.Default);
        });
        _bindings.Add(previewDisposable);

        Closed += (_, _) => { _bindings.Dispose(); };
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_initialized)
            return;

        if (DataContext is MainViewModel viewModel && viewModel.InitializeCommand.CanExecute(null))
        {
            viewModel.InitializeCommand.Execute(null);
            _initialized = true;
        }
    }
}