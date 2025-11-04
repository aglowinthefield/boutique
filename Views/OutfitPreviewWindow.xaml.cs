using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Pfim;
using RequiemGlamPatcher.Models;
using Serilog;

namespace RequiemGlamPatcher.Views;

public partial class OutfitPreviewWindow : Window
{
    private readonly ArmorPreviewScene _scene;
    private readonly Transform3DGroup _transformGroup = new();
    private readonly AxisAngleRotation3D _rotationX;
    private readonly AxisAngleRotation3D _rotationY;
    private readonly TranslateTransform3D _translation;
    private PerspectiveCamera? _camera;
    private Point _lastPosition;
    private bool _isDragging;
    private double _zoomDistance = 500;
    private double _baseDistance = 500;

    public OutfitPreviewWindow(ArmorPreviewScene scene)
    {
        InitializeComponent();
        _scene = scene;

        _rotationX = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
        _rotationY = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
        _translation = new TranslateTransform3D();
        _transformGroup.Children.Add(new RotateTransform3D(_rotationX));
        _transformGroup.Children.Add(new RotateTransform3D(_rotationY));
        _transformGroup.Children.Add(_translation);

        BuildScene();

        PreviewViewport.MouseDown += OnViewportMouseDown;
        PreviewViewport.MouseMove += OnViewportMouseMove;
        PreviewViewport.MouseUp += OnViewportMouseUp;
        PreviewViewport.MouseWheel += OnViewportMouseWheel;
    }

    private void BuildScene()
    {
        GenderLabel.Text = $"Gender: {_scene.Gender}";
        if (_scene.MissingAssets.Any())
        {
            MissingAssetsPanel.Visibility = Visibility.Visible;
            MissingAssetsList.ItemsSource = _scene.MissingAssets;
        }
        else
        {
            MissingAssetsPanel.Visibility = Visibility.Collapsed;
        }

        var evaluatedMeshes = new List<EvaluatedMesh>();
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        foreach (var mesh in _scene.Meshes)
        {
            var transformedVertices = new List<Vector3>(mesh.Vertices.Count);
            var transformedNormals = new List<Vector3>(mesh.Normals.Count);

            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                var v = mesh.Vertices[i];
                var world = Vector3.Transform(v, mesh.Transform);
                transformedVertices.Add(world);

                min = Vector3.Min(min, world);
                max = Vector3.Max(max, world);
            }

            for (int i = 0; i < mesh.Normals.Count; i++)
            {
                var n = mesh.Normals[i];
                var normal = Vector3.TransformNormal(n, mesh.Transform);
                if (normal != Vector3.Zero)
                    normal = Vector3.Normalize(normal);
                transformedNormals.Add(normal);
            }

            evaluatedMeshes.Add(new EvaluatedMesh(mesh, transformedVertices, transformedNormals));
        }

        if (!evaluatedMeshes.Any())
        {
            MissingAssetsPanel.Visibility = Visibility.Visible;
            MissingAssetsList.ItemsSource = new[] { "No geometry available to render." };
            return;
        }

        var center = (min + max) * 0.5f;
        var extents = max - min;
        var radius = Math.Max(Math.Max(extents.X, extents.Y), extents.Z);
        if (radius <= 0)
            radius = 1f;
        radius *= 0.5f;

        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(80, 80, 80)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(220, 220, 220), new Vector3D(-0.3, -0.5, -0.7)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(160, 160, 160), new Vector3D(0.45, 0.1, -0.35)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(120, 120, 120), new Vector3D(-0.2, 0.4, -0.5)));

        foreach (var evaluated in evaluatedMeshes)
        {
            var positions = new Point3DCollection(evaluated.Vertices.Select(v => new Point3D(v.X - center.X, v.Y - center.Y, v.Z - center.Z)));
            var indices = new Int32Collection(evaluated.Shape.Indices);

            var geometry = new MeshGeometry3D
            {
                Positions = positions,
                TriangleIndices = indices
            };

            if (evaluated.Normals.Count == evaluated.Vertices.Count)
            {
                geometry.Normals = new Vector3DCollection(evaluated.Normals.Select(n => new Vector3D(n.X, n.Y, n.Z)));
            }

            var uvs = evaluated.Shape.TextureCoordinates;
            if (uvs != null)
            {
                if (uvs.Count == evaluated.Vertices.Count)
                {
                    geometry.TextureCoordinates = new PointCollection(uvs.Select(tc => new Point(tc.X, tc.Y)));
                }
                else
                {
                    Log.Warning("Texture coordinate count {UvCount} does not match vertex count {VertexCount} for mesh {MeshName}", uvs.Count, evaluated.Vertices.Count, evaluated.Shape.Name);
                }
            }

            var material = CreateMaterialForMesh(evaluated.Shape);
            var model = new GeometryModel3D(geometry, material)
            {
                BackMaterial = material
            };

            group.Children.Add(model);
        }

        var visual = new ModelVisual3D
        {
            Content = group,
            Transform = _transformGroup
        };

        PreviewViewport.Children.Clear();
        PreviewViewport.Children.Add(visual);

        _camera = new PerspectiveCamera
        {
            FieldOfView = 45,
            UpDirection = new Vector3D(0, 0, 1)
        };

        _baseDistance = Math.Max(radius * 3.0, 150.0);
        _zoomDistance = _baseDistance;
        UpdateCamera();

        PreviewViewport.Camera = _camera;
        PreviewViewport.Focus();
    }

    private static Material CreateMaterialForMesh(PreviewMeshShape mesh)
    {
        var baseMaterial = TryCreateTextureMaterial(mesh.DiffuseTexturePath);
        if (baseMaterial == null)
        {
            var color = GetFallbackColor(mesh);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            var diffuse = new DiffuseMaterial(brush);
            diffuse.Freeze();
            Log.Debug("Using fallback color material for mesh {MeshName}", mesh.Name);
            baseMaterial = diffuse;
        }

        if (baseMaterial is MaterialGroup group)
            return group;

        var materialGroup = new MaterialGroup();
        materialGroup.Children.Add(baseMaterial);

        var specBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
        specBrush.Freeze();
        var specular = new SpecularMaterial(specBrush, 32);
        materialGroup.Children.Add(specular);

        return materialGroup;
    }

    private static Material? TryCreateTextureMaterial(string? texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
        {
            Log.Debug("No diffuse texture provided for mesh.");
            return null;
        }

        if (!File.Exists(texturePath))
        {
            Log.Warning("Diffuse texture path {TexturePath} does not exist on disk.", texturePath);
            return null;
        }

        try
        {
            var bitmap = LoadBitmap(texturePath);
            if (bitmap == null)
            {
                Log.Warning("Bitmap decoding returned null for texture {TexturePath}", texturePath);
                return null;
            }

            var brush = new ImageBrush(bitmap)
            {
                ViewportUnits = BrushMappingMode.Absolute,
                TileMode = TileMode.None,
                Stretch = Stretch.Uniform
            };
            brush.Freeze();

            var material = new DiffuseMaterial(brush);
            material.Freeze();
            Log.Debug("Successfully created textured material for {TexturePath}", texturePath);
            return material;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create textured material for {TexturePath}", texturePath);
            return null;
        }
    }

    private static BitmapSource? LoadBitmap(string texturePath)
    {
        try
        {
            var extension = Path.GetExtension(texturePath);
            if (string.Equals(extension, ".dds", StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug("Loading DDS texture via Pfim: {TexturePath}", texturePath);
                return LoadDdsTexture(texturePath);
            }

            Log.Debug("Loading non-DDS texture via WIC decoder: {TexturePath}", texturePath);
            using var stream = File.OpenRead(texturePath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
            {
                Log.Warning("Failed to decode texture {TexturePath}: decoder produced no frames.", texturePath);
                return null;
            }

            var frame = decoder.Frames[0];
            if (frame.CanFreeze && !frame.IsFrozen)
            {
                frame.Freeze();
            }

            return frame;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load texture {TexturePath}", texturePath);
            return null;
        }
    }

    private static BitmapSource? LoadDdsTexture(string texturePath)
    {
        try
        {
            Log.Debug("Decoding DDS texture {TexturePath}", texturePath);
            using var image = Pfim.Pfimage.FromFile(texturePath);
            if (image.Format != ImageFormat.Rgba32 && image.Format != ImageFormat.Rgb24)
            {
                Log.Debug("Decompressing DDS texture {TexturePath} from format {Format}", texturePath, image.Format);
                image.Decompress();
            }

            if (image.Format != ImageFormat.Rgba32 && image.Format != ImageFormat.Rgb24)
            {
                Log.Warning("Unsupported DDS format {Format} for texture {TexturePath}", image.Format, texturePath);
                return null;
            }

            var buffer = ConvertToBgra32(image, texturePath);
            if (buffer == null)
            {
                Log.Warning("Conversion to BGRA32 returned null for texture {TexturePath}", texturePath);
                return null;
            }

            var bitmap = BitmapSource.Create(
                image.Width,
                image.Height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                buffer,
                image.Width * 4);

            if (bitmap.CanFreeze && !bitmap.IsFrozen)
            {
                bitmap.Freeze();
            }

            Log.Debug("DDS texture {TexturePath} decoded successfully ({Width}x{Height})", texturePath, image.Width, image.Height);
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to decode DDS texture {TexturePath}", texturePath);
            return null;
        }
    }

    private static byte[]? ConvertToBgra32(IImage image, string texturePath)
    {
        try
        {
            switch (image.Format)
            {
                case ImageFormat.Rgba32:
                    {
                        Log.Debug("Converting RGBA32 texture {TexturePath} to BGRA32 buffer.", texturePath);
                        var data = image.Data;
                        var buffer = new byte[data.Length];
                        Buffer.BlockCopy(data, 0, buffer, 0, data.Length);
                        for (int i = 0; i < buffer.Length; i += 4)
                        {
                            (buffer[i], buffer[i + 2]) = (buffer[i + 2], buffer[i]);
                        }

                        return buffer;
                    }

                case ImageFormat.Rgb24:
                    {
                        Log.Debug("Expanding RGB24 texture {TexturePath} to BGRA32 buffer.", texturePath);
                        var data = image.Data;
                        var totalPixels = data.Length / 3;
                        var buffer = new byte[image.Width * image.Height * 4];
                        int source = 0;
                        int target = 0;
                        while (source + 2 < data.Length && target + 3 < buffer.Length)
                        {
                            var r = data[source];
                            var g = data[source + 1];
                            var b = data[source + 2];
                            buffer[target] = b;
                            buffer[target + 1] = g;
                            buffer[target + 2] = r;
                            buffer[target + 3] = 255;
                            source += 3;
                            target += 4;
                        }

                        return buffer;
                    }

                default:
                    Log.Warning("Cannot convert DDS format {Format} to BGRA32 for texture {TexturePath}.", image.Format, texturePath);
                    return null;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to convert DDS texture {TexturePath} to BGRA32.", texturePath);
            return null;
        }
    }

    private static Color GetFallbackColor(PreviewMeshShape mesh)
    {
        if (mesh.Name.Contains("Base Body", StringComparison.OrdinalIgnoreCase))
            return Color.FromRgb(200, 200, 200);

        var hash = mesh.SourcePath.GetHashCode(StringComparison.OrdinalIgnoreCase);
        byte r = (byte)((hash >> 16) & 0xFF);
        byte g = (byte)((hash >> 8) & 0xFF);
        byte b = (byte)(hash & 0xFF);

        const double scale = 0.6;
        const byte min = 70;
        r = (byte)(min + r * scale);
        g = (byte)(min + g * scale);
        b = (byte)(min + b * scale);

        return Color.FromRgb(r, g, b);
    }

    private void UpdateCamera()
    {
        if (_camera == null)
            return;

        var distance = _zoomDistance;
        var height = distance * 0.6;
        _camera.Position = new Point3D(0, -distance, height);
        _camera.LookDirection = new Vector3D(0, distance, -height);
    }

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDragging = true;
            _lastPosition = e.GetPosition(this);
            PreviewViewport.CaptureMouse();
        }
    }

    private void OnViewportMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_isDragging)
            return;

        var position = e.GetPosition(this);
        var delta = position - _lastPosition;
        _lastPosition = position;

        _rotationY.Angle += delta.X * 0.4;
        _rotationX.Angle += delta.Y * 0.4;
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Released && _isDragging)
        {
            _isDragging = false;
            PreviewViewport.ReleaseMouseCapture();
        }
    }

    private void OnViewportMouseWheel(object? sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 0.9 : 1.1;
        _zoomDistance *= factor;
        _zoomDistance = Math.Clamp(_zoomDistance, _baseDistance * 0.25, _baseDistance * 4.0);
        UpdateCamera();
    }

    private void OnResetView(object sender, RoutedEventArgs e)
    {
        _rotationX.Angle = 0;
        _rotationY.Angle = 0;
        _zoomDistance = _baseDistance;
        UpdateCamera();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private record EvaluatedMesh(PreviewMeshShape Shape, IReadOnlyList<Vector3> Vertices, IReadOnlyList<Vector3> Normals);
}
