using System.Reactive;
using System.Reactive.Disposables;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Boutique.Services;
using Boutique.ViewModels;
using GuideLine.Core;
using GuideLine.WPF.View;
using Microsoft.VisualBasic;

namespace Boutique.Views;

public partial class MainWindow : Window
{
    private readonly CompositeDisposable _bindings = [];
    private readonly ThemeService _themeService;
    private readonly TutorialService _tutorialService;
    private readonly GuiSettingsService _guiSettings;
    private bool _initialized;

    public MainWindow(MainViewModel viewModel, ThemeService themeService, TutorialService tutorialService, GuiSettingsService guiSettings)
    {
        InitializeComponent();
        DataContext = viewModel;
        _themeService = themeService;
        _tutorialService = tutorialService;
        _guiSettings = guiSettings;
        _tutorialService.Initialize(MainGuideline);

        _guiSettings.RestoreWindowGeometry(this);

        // Apply title bar theme on initialization and when theme changes
        SourceInitialized += (_, _) => _themeService.ApplyTitleBarTheme(this);
        _themeService.ThemeChanged += OnThemeChanged;

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
                var window = new OutfitPreviewWindow(scene, _themeService)
                {
                    Owner = this
                };
                window.Show();
            });
            interaction.SetOutput(Unit.Default);
        });
        _bindings.Add(previewDisposable);

        var missingMastersDisposable = viewModel.HandleMissingMasters.RegisterHandler(async interaction =>
        {
            var result = interaction.Input;
            var shouldClean = await Dispatcher.InvokeAsync(() =>
            {
                var dialog = new MissingMastersDialog(result)
                {
                    Owner = this
                };
                dialog.ShowDialog();
                return dialog.CleanPatch;
            });
            interaction.SetOutput(shouldClean);
        });
        _bindings.Add(missingMastersDisposable);

        Closing += (_, _) => _guiSettings.SaveWindowGeometry(this);
        Closed += (_, _) =>
        {
            _bindings.Dispose();
            _themeService.ThemeChanged -= OnThemeChanged;
        };
        Loaded += OnLoaded;
    }

    private void OnThemeChanged(object? sender, bool isDark)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        ThemeService.ApplyTitleBarTheme(hwnd, isDark);
    }

    private async void TabControl_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!Equals(e.Source, sender))
            return;

        if (sender is not TabControl)
            return;

        if (DataContext is not MainViewModel viewModel)
            return;

        if (e.AddedItems.Count == 0)
            return;

        if (e.AddedItems[0] is not TabItem tabItem)
            return;

        if (tabItem.Header is not string header)
            return;

        switch (header)
        {
            case "Armor Patch":
                await viewModel.LoadTargetPluginAsync();
                break;
            case "Outfit Creator":
                await viewModel.LoadOutfitPluginAsync();
                break;
        }
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

        if (FeatureFlags.TutorialEnabled && !_tutorialService.HasCompletedTutorial)
        {
            Dispatcher.BeginInvoke(() => _tutorialService.StartTutorial(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    private void MainGuideline_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!FeatureFlags.TutorialEnabled)
            return;

        if (sender is not GuideLine_View { DataContext: GuideLineManager manager })
            return;

        switch (e.Key)
        {
            case Key.Left:
                manager.CurrentGuideLine?.ShowPreviousStep();
                e.Handled = true;
                break;
            case Key.Right:
                manager.CurrentGuideLine?.ShowNextStep();
                e.Handled = true;
                break;
            case Key.Escape:
                manager.StopGuideLine();
                _tutorialService.CompleteTutorial();
                e.Handled = true;
                break;
        }
    }

    public void StartTutorial()
    {
        if (FeatureFlags.TutorialEnabled)
            _tutorialService.StartTutorial();
    }
}
