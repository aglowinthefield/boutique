using System.Reactive;
using System.Reactive.Disposables;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Boutique.Services;
using Boutique.ViewModels;

namespace Boutique.Views;

public partial class MainWindow : Window
{
  private readonly CompositeDisposable _bindings = [];
  private readonly GuiSettingsService  _guiSettings;
  private readonly ThemeService        _themeService;
  private          bool                _initialized;

  public MainWindow(
    MainViewModel viewModel,
    ThemeService themeService,
    GuiSettingsService guiSettings)
  {
    InitializeComponent();
    DataContext   = viewModel;
    _themeService = themeService;
    _guiSettings  = guiSettings;

    _guiSettings.RestoreWindowGeometry(this);

    SourceInitialized += (_, _) =>
    {
      _themeService.ApplyTitleBarTheme(this);
      ApplyFontScale(_themeService.CurrentFontScale);
    };
    _themeService.ThemeChanged     += OnThemeChanged;
    _themeService.FontScaleChanged += OnFontScaleChanged;

    var notificationDisposable = viewModel.PatchCreatedNotification.RegisterHandler(async interaction =>
    {
      var message = interaction.Input;
      await Dispatcher.InvokeAsync(() =>
                                     MessageBox.Show(
                                       this,
                                       message,
                                       "Patch Created",
                                       MessageBoxButton.OK,
                                       MessageBoxImage.Information));
      interaction.SetOutput(Unit.Default);
    });
    _bindings.Add(notificationDisposable);

    var confirmDisposable = viewModel.ConfirmOverwritePatch.RegisterHandler(async interaction =>
    {
      var message = interaction.Input;
      var result = await Dispatcher.InvokeAsync(() =>
                                                  MessageBox.Show(
                                                    this,
                                                    message,
                                                    "Overwrite Existing Patch?",
                                                    MessageBoxButton.YesNo,
                                                    MessageBoxImage.Warning,
                                                    MessageBoxResult.No));
      interaction.SetOutput(result == MessageBoxResult.Yes);
    });
    _bindings.Add(confirmDisposable);

    var confirmDeleteDisposable = viewModel.ConfirmDelete.RegisterHandler(async interaction =>
    {
      var message = interaction.Input;
      var result = await Dispatcher.InvokeAsync(() =>
                                                  MessageBox.Show(
                                                    this,
                                                    message,
                                                    "Confirm Delete",
                                                    MessageBoxButton.YesNo,
                                                    MessageBoxImage.Question,
                                                    MessageBoxResult.No));
      interaction.SetOutput(result == MessageBoxResult.Yes);
    });
    _bindings.Add(confirmDeleteDisposable);

    var outfitNameDisposable = viewModel.OutfitCreator.RequestOutfitName.RegisterHandler(async interaction =>
    {
      var (prompt, defaultValue) = interaction.Input;
      var result = await Dispatcher.InvokeAsync(() =>
                                                  InputDialog.Show(this, prompt, "Create Outfit", defaultValue));
      interaction.SetOutput(result);
    });
    _bindings.Add(outfitNameDisposable);

    var previewDisposable = viewModel.ShowPreview.RegisterHandler(async interaction =>
    {
      var sceneCollection = interaction.Input;
      await Dispatcher.InvokeAsync(() =>
      {
        var window = new OutfitPreviewWindow(sceneCollection, _themeService) { Owner = this };
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
        var dialog = new MissingMastersDialog(result) { Owner = this };
        dialog.ShowDialog();
        return dialog.CleanPatch;
      });
      interaction.SetOutput(shouldClean);
    });
    _bindings.Add(missingMastersDisposable);

    var errorDisposable = viewModel.ShowError.RegisterHandler(async interaction =>
    {
      var (title, message) = interaction.Input;
      await Dispatcher.InvokeAsync(() =>
                                     MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error));
      interaction.SetOutput(Unit.Default);
    });
    _bindings.Add(errorDisposable);

    Closing += (_, _) => _guiSettings.SaveWindowGeometry(this);
    Closed += (_, _) =>
    {
      _bindings.Dispose();
      _themeService.ThemeChanged     -= OnThemeChanged;
      _themeService.FontScaleChanged -= OnFontScaleChanged;
    };
    Loaded += OnLoaded;
  }

  private void OnThemeChanged(object? sender, bool isDark)
  {
    var hwnd = new WindowInteropHelper(this).Handle;
    ThemeService.ApplyTitleBarTheme(hwnd, isDark);
  }

  private void OnFontScaleChanged(object? sender, double scale) => Dispatcher.Invoke(() => ApplyFontScale(scale));

  private void ApplyFontScale(double scale)
  {
    RootScaleTransform.ScaleX = scale;
    RootScaleTransform.ScaleY = scale;
  }

  private async void TabControl_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (!Equals(e.Source, sender))
    {
      return;
    }

    if (sender is not TabControl)
    {
      return;
    }

    if (DataContext is not MainViewModel viewModel)
    {
      return;
    }

    if (e.AddedItems.Count == 0)
    {
      return;
    }

    if (e.AddedItems[0] is not TabItem tabItem)
    {
      return;
    }

    if (tabItem.Header is not string header)
    {
      return;
    }

    switch (header)
    {
      case "Armor Patch":
        await viewModel.LoadTargetPluginAsync();
        break;
      case "Outfit Creator":
        await viewModel.OutfitCreator.LoadOutfitPluginAsync();
        break;
    }
  }

  private void OnLoaded(object? sender, RoutedEventArgs e)
  {
    if (_initialized)
    {
      return;
    }

    if (DataContext is MainViewModel viewModel)
    {
      viewModel.InitializeCommand.Execute().Subscribe();
      _initialized = true;
    }
  }
}
