using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Media3D;
using Boutique.Models;
using Boutique.Services;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.Wpf.SharpDX.Model.Scene;
using Serilog;
using SharpDX.Direct3D11;
using Color = System.Windows.Media.Color;
using Color4 = SharpDX.Color4;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;
using PerspectiveCamera = HelixToolkit.Wpf.SharpDX.PerspectiveCamera;
using ProjectionCamera = HelixToolkit.Wpf.SharpDX.ProjectionCamera;
using SharpDXVector2 = SharpDX.Vector2;
using SharpDXVector3 = SharpDX.Vector3;
using Vector3 = System.Numerics.Vector3;

namespace Boutique.Views;

public sealed partial class OutfitPreviewWindow : IDisposable
{
    private bool _disposed;
    private const float AmbientSrgb = 0.2f;
    private const float KeyFillSrgb = 0.6f;
    private const float RimSrgb = 0.85f;
    private const float FrontalSrgb = 0.2f;

    private const float MaterialDiffuseMultiplier = 5f;
    private const float MaterialAmbientMultiplier = 2.3f;
    private const float MaterialSpecularMultiplier = 0.3f;
    private const float MaterialShininess = 0f;
    private readonly AmbientLight3D _ambientLight = new();
    private readonly DirectionalLight3D _backLight = new();
    private readonly DefaultEffectsManager _effectsManager = new();
    private readonly DirectionalLight3D _frontalLight = new();
    private readonly DirectionalLight3D _frontLeftLight = new();
    private readonly DirectionalLight3D _frontRightLight = new();
    private readonly GroupModel3D _meshGroup = new();
    private readonly ArmorPreviewSceneCollection _sceneCollection;
    private readonly ThemeService _themeService;

    private float _ambientMultiplier;
    private float _frontalMultiplier = 7f;
    private PerspectiveCamera? _initialCamera;
    private float _keyFillMultiplier = 1.6f;
    private float _rimMultiplier = 1f;
    private int _currentSceneIndex;
    private GenderedModelVariant _currentGender;

    public OutfitPreviewWindow(ArmorPreviewSceneCollection sceneCollection, ThemeService themeService)
    {
        InitializeComponent();
        _sceneCollection = sceneCollection ?? throw new ArgumentNullException(nameof(sceneCollection));
        _themeService = themeService;
        _currentSceneIndex = sceneCollection.InitialIndex;
        _currentGender = sceneCollection.InitialGender;

        SourceInitialized += (_, _) =>
        {
            _themeService.ApplyTitleBarTheme(this);
            RootScaleTransform.ScaleX = _themeService.CurrentFontScale;
            RootScaleTransform.ScaleY = _themeService.CurrentFontScale;
        };
        _themeService.ThemeChanged += OnThemeChanged;

        InitializeViewport();
        InitializeGenderDropdown();
        BuildScene();

        // Add window-level keyboard shortcuts
        PreviewKeyDown += OnWindowPreviewKeyDown;
    }

    private void OnThemeChanged(object? sender, bool isDark)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        ThemeService.ApplyTitleBarTheme(hwnd, isDark);
    }

    private void InitializeGenderDropdown()
    {
        GenderComboBox.SelectedIndex = _currentGender == GenderedModelVariant.Female ? 0 : 1;
    }

    private void InitializeViewport()
    {
        PreviewViewport.EffectsManager = _effectsManager;
        PreviewViewport.Items.Clear();
        PreviewViewport.MSAA = MSAALevel.Eight;
        PreviewViewport.CameraMode = CameraMode.Inspect;
        PreviewViewport.CameraRotationMode = CameraRotationMode.Trackball;
        PreviewViewport.RotateAroundMouseDownPoint = true;
        PreviewViewport.ZoomAroundMouseDownPoint = true;
        PreviewViewport.IsPanEnabled = true;
        PreviewViewport.IsZoomEnabled = true;
        PreviewViewport.IsRotationEnabled = true;
        PreviewViewport.IsInertiaEnabled = false;
        PreviewViewport.InfiniteSpin = false;

        PreviewViewport.UseDefaultGestures = false;
        PreviewViewport.InputBindings.Clear();
        PreviewViewport.InputBindings.Add(new MouseBinding(
            ViewportCommands.Rotate,
            new MouseGesture(MouseAction.LeftClick)));
        PreviewViewport.InputBindings.Add(new MouseBinding(
            ViewportCommands.Pan,
            new MouseGesture(MouseAction.MiddleClick)));
        PreviewViewport.InputBindings.Add(new MouseBinding(
            ViewportCommands.Zoom,
            new MouseGesture(MouseAction.RightClick)));
        PreviewViewport.InputBindings.Add(new MouseBinding(
            ViewportCommands.ZoomExtents,
            new MouseGesture(MouseAction.LeftDoubleClick)));
        PreviewViewport.InputBindings.Add(new MouseBinding(
            ViewportCommands.ZoomExtents,
            new MouseGesture(MouseAction.RightDoubleClick)));
        PreviewViewport.InputBindings.Add(new KeyBinding(ViewportCommands.ZoomExtents, Key.F, ModifierKeys.Control));
        PreviewViewport.BackgroundColor = GetViewportBackgroundColor();

        _meshGroup.Transform = Transform3D.Identity;

        ConfigureLights();

        PreviewViewport.Items.Add(_ambientLight);
        PreviewViewport.Items.Add(_frontLeftLight);
        PreviewViewport.Items.Add(_frontRightLight);
        PreviewViewport.Items.Add(_backLight);
        PreviewViewport.Items.Add(_frontalLight);
        UpdateFrontalLightDirection();

        PreviewViewport.Items.Add(_meshGroup);
        PreviewViewport.CameraChanged += OnViewportCameraChanged;

        PreviewViewport.KeyDown += OnViewportKeyDown;

        var hasMetadata = _sceneCollection.Metadata.Any(m =>
            !string.IsNullOrWhiteSpace(m.OutfitLabel) || !string.IsNullOrWhiteSpace(m.SourceFile));
        var isSingleScene = _sceneCollection.Count <= 1;

        NavigationHeader.Visibility = hasMetadata ? Visibility.Visible : Visibility.Collapsed;

        if (isSingleScene)
        {
            PreviousOutfitButton.Visibility = Visibility.Collapsed;
            NextOutfitButton.Visibility = Visibility.Collapsed;
            OutfitCounterText.Visibility = Visibility.Collapsed;
        }
    }

    private async void BuildScene()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        MissingAssetsPanel.Visibility = Visibility.Collapsed;

        var scene = await _sceneCollection.GetSceneAsync(_currentSceneIndex, _currentGender);

        LoadingPanel.Visibility = Visibility.Collapsed;

        UpdateMetadataDisplay(_currentSceneIndex);
        UpdateMissingAssetsPanel(scene);
        RenderScene(scene);
    }

    private void UpdateMetadataDisplay(int sceneIndex)
    {
        var metadata = _sceneCollection.Metadata[sceneIndex];

        if (!string.IsNullOrWhiteSpace(metadata.OutfitLabel) || !string.IsNullOrWhiteSpace(metadata.SourceFile))
        {
            if (_sceneCollection.Count > 1)
                OutfitCounterText.Text = $"Outfit {sceneIndex + 1} of {_sceneCollection.Count}";

            OutfitLabelText.Text = metadata.OutfitLabel ?? "Unknown Outfit";

            if (!string.IsNullOrWhiteSpace(metadata.SourceFile))
            {
                OutfitSourceText.Text = metadata.IsWinner
                    ? $"from {metadata.SourceFile} (Winner)"
                    : $"from {metadata.SourceFile}";
                var brushKey = metadata.IsWinner ? "Brush.Accent" : "Brush.TextSecondary";
                OutfitSourceText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, brushKey);
            }
            else
            {
                OutfitSourceText.Text = string.Empty;
            }
        }
    }

    private void UpdateMissingAssetsPanel(ArmorPreviewScene scene)
    {
        if (scene.MissingAssets.Any())
        {
            MissingAssetsPanel.Visibility = Visibility.Visible;
            MissingAssetsList.ItemsSource = scene.MissingAssets;
        }
        else
        {
            MissingAssetsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void RenderScene(ArmorPreviewScene scene)
    {
        var evaluatedMeshes = EvaluateMeshes(scene, out var center, out var radius);
        _meshGroup.Children.Clear();

        if (evaluatedMeshes.Count == 0)
        {
            MissingAssetsPanel.Visibility = Visibility.Visible;
            MissingAssetsList.ItemsSource = new[] { "No geometry available to render." };
            return;
        }

        foreach (var evaluated in evaluatedMeshes)
        {
            var geometry = CreateGeometry(evaluated, center);
            if (geometry == null)
                continue;

            var material = CreateMaterialForMesh(evaluated.Shape);
            var model = new MeshGeometryModel3D
            {
                Geometry = geometry,
                Material = material,
                CullMode = CullMode.None,
                IsHitTestVisible = false
            };

            _meshGroup.Children.Add(model);
        }

        ConfigureCamera(radius);
        PreviewViewport.InvalidateRender();
    }

    private List<EvaluatedMesh> EvaluateMeshes(ArmorPreviewScene scene, out Vector3 center, out float radius)
    {
        var evaluatedMeshes = new List<EvaluatedMesh>();
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        foreach (var mesh in scene.Meshes)
        {
            var transformedVertices = new List<Vector3>(mesh.Vertices.Count);
            var transformedNormals = new List<Vector3>(mesh.Normals.Count);

            foreach (var t in mesh.Vertices)
            {
                var world = Vector3.Transform(t, mesh.Transform);
                transformedVertices.Add(world);

                min = Vector3.Min(min, world);
                max = Vector3.Max(max, world);
            }

            foreach (var t in mesh.Normals)
            {
                var normal = Vector3.TransformNormal(t, mesh.Transform);
                if (normal != Vector3.Zero)
                    normal = Vector3.Normalize(normal);
                transformedNormals.Add(normal);
            }

            evaluatedMeshes.Add(new EvaluatedMesh(mesh, transformedVertices, transformedNormals));
        }

        if (evaluatedMeshes.Count == 0)
        {
            center = Vector3.Zero;
            radius = 1f;
            return evaluatedMeshes;
        }

        center = (min + max) * 0.5f;
        var extents = max - min;
        radius = Math.Max(Math.Max(extents.X, extents.Y), extents.Z);
        if (radius <= 0)
            radius = 1f;
        radius *= 0.5f;

        return evaluatedMeshes;
    }

    private static MeshGeometry3D? CreateGeometry(EvaluatedMesh evaluated, Vector3 center)
    {
        if (evaluated.Vertices.Count == 0 || evaluated.Shape.Indices.Count == 0)
            return null;

        var positions = new Vector3Collection(
            evaluated.Vertices.Select(v => new SharpDXVector3(v.X - center.X, v.Y - center.Y, v.Z - center.Z)));

        var geometry = new MeshGeometry3D
        {
            Positions = positions,
            Indices = new IntCollection(evaluated.Shape.Indices)
        };

        if (evaluated.Normals.Count == evaluated.Vertices.Count)
        {
            geometry.Normals = new Vector3Collection(
                evaluated.Normals.Select(n => new SharpDXVector3(n.X, n.Y, n.Z)));
        }

        var uvs = evaluated.Shape.TextureCoordinates;
        if (uvs != null && uvs.Count == evaluated.Vertices.Count)
        {
            geometry.TextureCoordinates = new Vector2Collection(
                uvs.Select(tc => new SharpDXVector2(tc.X, tc.Y)));
        }
        else if (uvs != null)
        {
            Log.Warning(
                "Texture coordinate count {UvCount} does not match vertex count {VertexCount} for mesh {MeshName}",
                uvs.Count, evaluated.Vertices.Count, evaluated.Shape.Name);
        }

        geometry.UpdateBounds();
        return geometry;
    }

    private void ConfigureCamera(float radius)
    {
        var baseDistance = Math.Max(radius * 2.6f, 115.0f);
        var height = baseDistance * 0.2;

        var camera = new PerspectiveCamera
        {
            FieldOfView = 47,
            Position = new Point3D(0, baseDistance, height),
            LookDirection = new Vector3D(0, -baseDistance, -height),
            UpDirection = new Vector3D(0, 0, 1)
        };

        PreviewViewport.Camera = camera;
        _initialCamera = (PerspectiveCamera)camera.Clone();

        UpdateFrontalLightDirection();
    }

    private void ConfigureLights()
    {
        // Directions line up with BodySlide's preview rig; intensities are applied separately.
        _frontLeftLight.Direction = new Vector3D(-0.667124384994991, 0.07412493166611012, 0.7412493166611012);
        _frontRightLight.Direction = new Vector3D(0.5715476066494083, 0.08164965809277261, 0.8164965809277261);
        _backLight.Direction = new Vector3D(0.2822162605150792, 0.18814417367671948, -0.9407208683835974);
        _frontalLight.Direction = new Vector3D(0, 0, -1);

        ApplyLightIntensities();
    }

    private void ApplyLightIntensities()
    {
        var ambientValue = ApplyExposure(SrgbToLinear(AmbientSrgb), _ambientMultiplier);
        var ambientColor = new Color4(ambientValue, ambientValue, ambientValue, 1f);
        _ambientLight.Color = ToMediaColor(ambientColor);
        SetSceneLightColor(_ambientLight, ambientColor);

        var keyFillValue = ApplyExposure(SrgbToLinear(KeyFillSrgb), _keyFillMultiplier);
        var keyFillColor = new Color4(keyFillValue, keyFillValue, keyFillValue, 1f);
        _frontLeftLight.Color = ToMediaColor(keyFillColor);
        SetSceneLightColor(_frontLeftLight, keyFillColor);
        _frontRightLight.Color = ToMediaColor(keyFillColor);
        SetSceneLightColor(_frontRightLight, keyFillColor);

        var rimValue = ApplyExposure(SrgbToLinear(RimSrgb), _rimMultiplier);
        var rimColor = new Color4(rimValue, rimValue, rimValue, 1f);
        _backLight.Color = ToMediaColor(rimColor);
        SetSceneLightColor(_backLight, rimColor);

        var frontalValue = ApplyExposure(SrgbToLinear(FrontalSrgb), _frontalMultiplier);
        var frontalColor = new Color4(frontalValue, frontalValue, frontalValue, 1f);
        _frontalLight.Color = ToMediaColor(frontalColor);
        SetSceneLightColor(_frontalLight, frontalColor);

        PreviewViewport?.InvalidateRender();
    }

    private void OnAmbientMultiplierChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _ambientMultiplier = (float)e.NewValue;
        ApplyLightIntensities();
    }

    private void OnKeyFillMultiplierChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _keyFillMultiplier = (float)e.NewValue;
        ApplyLightIntensities();
    }

    private void OnRimMultiplierChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _rimMultiplier = (float)e.NewValue;
        ApplyLightIntensities();
    }

    private void OnFrontalMultiplierChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _frontalMultiplier = (float)e.NewValue;
        ApplyLightIntensities();
    }

    private static float SrgbToLinear(float srgbValue)
    {
        srgbValue = Math.Max(0f, srgbValue);
        return MathF.Pow(srgbValue, 2.2f);
    }

    private static float ApplyExposure(float linearValue, float exposureMultiplier) => Math.Max(0f, linearValue * exposureMultiplier);

    private static void SetSceneLightColor(Light3D light, Color4 color)
    {
        switch (light.SceneNode)
        {
            case AmbientLightNode ambientNode:
                ambientNode.Color = color;
                break;
            case DirectionalLightNode directionalNode:
                directionalNode.Color = color;
                break;
        }
    }

    private static Color4 ScaleColor(Color4 baseColor, float multiplier)
    {
        return new Color4(baseColor.Red * multiplier, baseColor.Green * multiplier, baseColor.Blue * multiplier,
            baseColor.Alpha);
    }

    private static PhongMaterial CreateMaterialForMesh(PreviewMeshShape mesh)
    {
        var material = TryCreateTextureMaterial(mesh);
        if (material != null)
            return material;

        var fallbackColor = GetFallbackColor(mesh);
        var baseDiffuse = ToColor4(fallbackColor);
        var baseAmbient = baseDiffuse;
        var baseSpecular = new Color4(0.2f, 0.2f, 0.2f, 1f);

        return new PhongMaterial
        {
            DiffuseColor = ScaleColor(baseDiffuse, MaterialDiffuseMultiplier),
            AmbientColor = ScaleColor(baseAmbient, MaterialAmbientMultiplier),
            SpecularColor = ScaleColor(baseSpecular, MaterialSpecularMultiplier),
            SpecularShininess = Math.Max(0f, MaterialShininess),
            EmissiveColor = new Color4(0f, 0f, 0f, 1f)
        };
    }

    private static PhongMaterial? TryCreateTextureMaterial(PreviewMeshShape mesh)
    {
        var texturePath = mesh.DiffuseTexturePath;
        if (string.IsNullOrWhiteSpace(texturePath))
        {
            Log.Debug("No diffuse texture provided for mesh {MeshName}", mesh.Name);
            return null;
        }

        if (!File.Exists(texturePath))
        {
            Log.Warning("Diffuse texture path {TexturePath} does not exist on disk.", texturePath);
            return null;
        }

        try
        {
            var baseDiffuse = new Color4(1f, 1f, 1f, 1f);
            var baseAmbient = new Color4(0.2f, 0.2f, 0.2f, 1f);
            var baseSpecular = new Color4(0.2f, 0.2f, 0.2f, 1f);

            var material = new PhongMaterial
            {
                DiffuseMap = new TextureModel(texturePath),
                DiffuseColor = ScaleColor(baseDiffuse, MaterialDiffuseMultiplier),
                AmbientColor = ScaleColor(baseAmbient, MaterialAmbientMultiplier),
                SpecularColor = ScaleColor(baseSpecular, MaterialSpecularMultiplier),
                SpecularShininess = Math.Max(0f, MaterialShininess),
                EmissiveColor = new Color4(0f, 0f, 0f, 1f)
            };

            Log.Debug("Successfully created textured material for {TexturePath}", texturePath);
            return material;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create textured material for {TexturePath}", texturePath);
            return null;
        }
    }

    private void OnViewportCameraChanged(object? sender, RoutedEventArgs e) => UpdateFrontalLightDirection();

    private void UpdateFrontalLightDirection()
    {
        if (PreviewViewport.Camera is not ProjectionCamera camera)
            return;
        var direction = camera.LookDirection;
        if (direction.LengthSquared > 1e-6)
        {
            direction.Normalize();
            _frontalLight.Direction = new Vector3D(direction.X, direction.Y, direction.Z);
        }
    }

    private static Color GetFallbackColor(PreviewMeshShape mesh)
    {
        if (mesh.Name.Contains("Base Body", StringComparison.OrdinalIgnoreCase))
            return Color.FromRgb(200, 200, 200);

        var hash = mesh.SourcePath.GetHashCode(StringComparison.OrdinalIgnoreCase);
        var r = (byte)((hash >> 16) & 0xFF);
        var g = (byte)((hash >> 8) & 0xFF);
        var b = (byte)(hash & 0xFF);

        const double scale = 0.6;
        const byte min = 70;
        r = (byte)(min + (r * scale));
        g = (byte)(min + (g * scale));
        b = (byte)(min + (b * scale));

        return Color.FromRgb(r, g, b);
    }

    private static Color ToMediaColor(Color4 color) => Color.FromScRgb(color.Alpha, color.Red, color.Green, color.Blue);

    private static Color4 ToColor4(Color color) => new Color4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    private static Color GetViewportBackgroundColor() =>

        // Match BodySlide's light preview background (RGB 210,210,210).
        Color.FromRgb(210, 210, 210);

    private void OnResetView(object sender, RoutedEventArgs e)
    {
        if (_initialCamera == null)
            return;

        if (PreviewViewport.Camera is PerspectiveCamera activeCamera)
        {
            activeCamera.Position = _initialCamera.Position;
            activeCamera.LookDirection = _initialCamera.LookDirection;
            activeCamera.UpDirection = _initialCamera.UpDirection;
            activeCamera.FieldOfView = _initialCamera.FieldOfView;
        }
        else
        {
            PreviewViewport.Camera = (PerspectiveCamera)_initialCamera.Clone();
        }

        UpdateFrontalLightDirection();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnGenderChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (GenderComboBox.SelectedItem is not System.Windows.Controls.ComboBoxItem selectedItem)
            return;

        var newGender = selectedItem.Tag?.ToString() == "Male"
            ? GenderedModelVariant.Male
            : GenderedModelVariant.Female;

        if (newGender == _currentGender)
            return;

        _currentGender = newGender;
        BuildScene();
    }

    private void ToggleGender()
    {
        _currentGender = _currentGender == GenderedModelVariant.Female
            ? GenderedModelVariant.Male
            : GenderedModelVariant.Female;

        GenderComboBox.SelectedIndex = _currentGender == GenderedModelVariant.Female ? 0 : 1;
        BuildScene();
    }

    private void OnPreviousOutfit(object sender, RoutedEventArgs e) => NavigateOutfit(-1);

    private void OnNextOutfit(object sender, RoutedEventArgs e) => NavigateOutfit(1);

    private void NavigateOutfit(int direction)
    {
        if (_sceneCollection.Count <= 1)
            return;

        _currentSceneIndex = (_currentSceneIndex + direction + _sceneCollection.Count)
            % _sceneCollection.Count;
        BuildScene();
    }

    private void OnWindowPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.IsRepeat)
            return;

        if (e.Key == Key.Tab)
        {
            ToggleGender();
            e.Handled = true;
            return;
        }

        if (_sceneCollection.Count > 1)
        {
            switch (e.Key)
            {
                case Key.Left:
                    NavigateOutfit(-1);
                    e.Handled = true;
                    break;
                case Key.Right:
                    NavigateOutfit(1);
                    e.Handled = true;
                    break;
            }
        }
    }

    private void OnViewportKeyDown(object sender, KeyEventArgs e)
    {
        if (_sceneCollection.Count <= 1)
            return;

        switch (e.Key)
        {
            case Key.Left:
                NavigateOutfit(-1);
                e.Handled = true;
                break;
            case Key.Right:
                NavigateOutfit(1);
                e.Handled = true;
                break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Dispose();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _themeService.ThemeChanged -= OnThemeChanged;

            _meshGroup.Children.Clear();
            PreviewViewport.Items.Clear();

            _ambientLight.Dispose();
            _backLight.Dispose();
            _frontalLight.Dispose();
            _frontLeftLight.Dispose();
            _frontRightLight.Dispose();
            _meshGroup.Dispose();
            _effectsManager.Dispose();
        }

        _disposed = true;
    }

    private record EvaluatedMesh(
        PreviewMeshShape Shape,
        IReadOnlyList<Vector3> Vertices,
        IReadOnlyList<Vector3> Normals);
}
