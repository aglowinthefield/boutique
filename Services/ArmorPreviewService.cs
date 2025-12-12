using System.IO;
using System.Numerics;
using Boutique.Models;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using NiflySharp;
using NiflySharp.Blocks;
using NiflySharp.Structs;
using Serilog;

namespace Boutique.Services;

public class ArmorPreviewService(MutagenService mutagenService, GameAssetLocator assetLocator, ILogger logger)
{
  private const string FemaleBodyRelativePath = "meshes/actors/character/character assets/femalebody_0.nif";
  private const string MaleBodyRelativePath = "meshes/actors/character/character assets/malebody_0.nif";
  private static readonly ModKey _skyrimBaseModKey = ModKey.FromNameAndExtension("Skyrim.esm");

  private static readonly HashSet<string> _nonDiffuseSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "n",
        "msn",
        "spec",
        "s",
        "g",
        "glow",
        "env",
        "emit",
        "em",
        "mask",
        "rough",
        "metal",
        "m",
        "etc",
        "sk",
        "alpha",
        "cube",
        "cmap",
        "height",
        "disp",
        "opacity",
        "normal",
        "emis",
        "metallic",
        "roughness",
        "gloss"
    };

  private static readonly string[] _nonDiffuseSubstrings =
  {
        "normalmap",
        "_normal",
        "_nmap",
        "_smap",
        "_msn",
        "_spec",
        "_specmap",
        "_glow",
        "_env",
        "_envmap",
        "_cubemap",
        "_cmap",
        "_emit",
        "_emissive",
        "_mask",
        "_rough",
        "_roughness",
        "_metal",
        "_metallic",
        "_height",
        "_displace",
        "_opacity",
        "_alpha"
    };

  private readonly ILogger _logger = logger.ForContext<ArmorPreviewService>();

  public async Task<ArmorPreviewScene> BuildPreviewAsync(
      IEnumerable<ArmorRecordViewModel> armorPieces,
      GenderedModelVariant preferredGender,
      CancellationToken cancellationToken = default)
  {
    if (!mutagenService.IsInitialized)
      throw new InvalidOperationException("Mutagen service has not been initialized.");

    var dataPath = mutagenService.DataFolderPath;
    var linkCache = mutagenService.LinkCache;

    if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath))
      throw new DirectoryNotFoundException("Skyrim Data path is not set or does not exist.");

    if (linkCache == null)
      throw new InvalidOperationException("Link cache is not available.");

    var pieces = armorPieces?.ToList() ?? new List<ArmorRecordViewModel>();
    return await Task.Run(
        () => BuildPreviewInternal(pieces, preferredGender, dataPath, linkCache, cancellationToken),
        cancellationToken);
  }

  private ArmorPreviewScene BuildPreviewInternal(
      List<ArmorRecordViewModel> pieces,
      GenderedModelVariant preferredGender,
      string dataPath,
      ILinkCache linkCache,
      CancellationToken cancellationToken)
  {
    var gender = DetermineEffectiveGender(pieces, preferredGender, linkCache);
    _logger.Debug("Building preview for {PieceCount} armor pieces with preferred gender {PreferredGender}",
        pieces.Count, preferredGender);
    var meshes = new List<PreviewMeshShape>();
    var missingAssets = new List<string>();

    // Always add baseline body mesh
    var bodyRelative = GetBodyRelativePath(gender);
    var bodyAssetKey = NormalizeAssetPath(bodyRelative);
    var bodyPath = assetLocator.ResolveAssetPath(bodyAssetKey, _skyrimBaseModKey);
    if (!string.IsNullOrWhiteSpace(bodyPath) && File.Exists(bodyPath))
    {
      meshes.AddRange(LoadMeshesFromNif("Base Body", bodyPath, gender, _skyrimBaseModKey, cancellationToken));
    }
    else
    {
      var expected = FormatExpectedPath(dataPath, bodyAssetKey);
      missingAssets.Add(expected);
      _logger.Warning("Base body mesh not found at {BodyPath}", expected);
    }

    var visitedParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var piece in pieces)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var armor = piece.Armor;
      var armorName = armor.Name?.String ?? armor.EditorID ?? "Unknown Armor";

      foreach (var addonLink in armor.Armature)
      {
        cancellationToken.ThrowIfCancellationRequested();

        if (!linkCache.TryResolve<IArmorAddonGetter>(addonLink.FormKey, out var addon) || addon is null)
        {
          _logger.Warning("Failed to resolve ArmorAddon {FormKey} for armor {Armor}", addonLink.FormKey,
              armorName);
          continue;
        }

        var model = SelectModel(addon.WorldModel, gender, out var variantForAddon);
        if (model == null)
        {
          _logger.Information("ArmorAddon {Addon} has no usable models for gender {Gender}", addon.EditorID,
              gender);
          continue;
        }

        var modelPath = ResolveModelPath(model);
        if (string.IsNullOrWhiteSpace(modelPath))
        {
          _logger.Information("ArmorAddon {Addon} model is missing a file path.", addon.EditorID);
          continue;
        }

        var meshAssetKey = NormalizeAssetPath(modelPath);
        var fullPath = assetLocator.ResolveAssetPath(meshAssetKey, addon.FormKey.ModKey);
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
        {
          var expected = FormatExpectedPath(dataPath, meshAssetKey);
          missingAssets.Add(expected);
          _logger.Warning("Mesh file {Path} not found for ArmorAddon {Addon}", expected, addon.EditorID);
          continue;
        }

        var identity = $"{variantForAddon}:{meshAssetKey}";
        if (!visitedParts.Add(identity))
          continue; // Avoid loading identical meshes multiple times

        var partName = $"{armorName} ({addon.EditorID ?? addon.FormKey.ToString()})";
        meshes.AddRange(LoadMeshesFromNif(partName, fullPath, variantForAddon, addon.FormKey.ModKey,
            cancellationToken));
      }
    }

    return new ArmorPreviewScene(gender, meshes, missingAssets);
  }

  private static GenderedModelVariant DetermineEffectiveGender(
      IReadOnlyList<ArmorRecordViewModel> pieces,
      GenderedModelVariant preferredGender,
      ILinkCache linkCache)
  {
    if (preferredGender == GenderedModelVariant.Male)
      return GenderedModelVariant.Male;

    foreach (var piece in pieces)
      foreach (var addonLink in piece.Armor.Armature)
      {
        if (!linkCache.TryResolve<IArmorAddonGetter>(addonLink.FormKey, out var addon))
          continue;

        var worldModel = addon.WorldModel;
        if (worldModel == null)
          continue;

        if (worldModel.Female != null)
          continue;

        if (worldModel.Male != null)
          return GenderedModelVariant.Male;
      }

    return GenderedModelVariant.Female;
  }

  private List<PreviewMeshShape> LoadMeshesFromNif(
      string partName,
      string meshPath,
      GenderedModelVariant variant,
      ModKey? ownerModKey,
      CancellationToken cancellationToken)
  {
    var meshes = new List<PreviewMeshShape>();
    var nif = new NifFile();

    try
    {
      _logger.Debug("Loading NIF mesh from {FullPath}", meshPath);
      var loadResult = nif.Load(meshPath);
      if (loadResult != 0 || !nif.Valid)
      {
        _logger.Warning("Failed to load NIF {FullPath}. Result={Result} Valid={Valid}", meshPath, loadResult,
            nif.Valid);
        return meshes;
      }

      var shapes = nif.GetShapes().OfType<INiShape>().ToList();
      _logger.Debug("Found {ShapeCount} shapes in {FullPath}", shapes.Count, meshPath);

      foreach (var shape in shapes)
      {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryExtractMesh(nif, shape, ownerModKey, out var meshData))
        {
          _logger.Debug("Skipping shape {ShapeName} in {FullPath} due to missing geometry or texture data.",
              shape.Name?.ToString() ?? "<unnamed>", meshPath);
          continue;
        }

        if (meshData.DiffuseTexturePath == null)
        {
          _logger.Debug("Skipping shape {ShapeName} because it has no diffuse texture.",
              shape.Name?.ToString() ?? "<unnamed>");
          continue;
        }

        var shapeName = shape.Name?.ToString();
        var name = string.IsNullOrWhiteSpace(shapeName) ? partName : $"{partName} - {shapeName}";
        meshes.Add(new PreviewMeshShape(
            name,
            meshPath,
            variant,
            meshData.Vertices,
            meshData.Normals,
            meshData.TextureCoordinates,
            meshData.Indices,
            meshData.Transform,
            meshData.DiffuseTexturePath));
      }
    }
    catch
    {
      // Swallow individual mesh issues and continue,
      // caller already logs missing files.
    }

    return meshes;
  }

  private bool TryExtractMesh(NifFile nif, INiShape shape, ModKey? ownerModKey, out MeshData meshData)
  {
    meshData = default;

    var vertices = ExtractVertices(shape);
    if (vertices == null || vertices.Count == 0)
    {
      _logger.Debug("Shape {ShapeName} has no vertices.", shape.Name?.ToString() ?? "<unnamed>");
      return false;
    }

    var indices = ExtractIndices(shape);
    if (indices == null || indices.Count == 0)
    {
      _logger.Debug("Shape {ShapeName} has no indices.", shape.Name?.ToString() ?? "<unnamed>");
      return false;
    }

    var extractedNormals = ExtractNormals(shape);
    List<Vector3> normals;

    if (extractedNormals != null && extractedNormals.Count == vertices.Count)
    {
      normals = extractedNormals;
    }
    else
    {
      normals = ComputeNormals(vertices, indices);
      var shapeName = shape.Name?.ToString() ?? "<unnamed>";
      if (extractedNormals == null)
        _logger.Debug("Shape {ShapeName} provided no normals; computed fallback.", shapeName);
      else
        _logger.Debug(
            "Shape {ShapeName} normals count {ProvidedCount} mismatched vertex count {VertexCount}; computed fallback.",
            shapeName, extractedNormals.Count, vertices.Count);
    }

    var textureCoordinates = ExtractTextureCoordinates(shape);
    if (textureCoordinates != null && textureCoordinates.Count != vertices.Count)
    {
      _logger.Debug(
          "Shape {ShapeName} texture coordinate count {TexCount} does not match vertex count {VertexCount}. Ignoring UVs.",
          shape.Name?.ToString() ?? "<unnamed>", textureCoordinates.Count, vertices.Count);
      textureCoordinates = null;
    }
    else if (textureCoordinates != null)
    {
      _logger.Debug("Shape {ShapeName} extracted {TexCount} UV coordinates.",
          shape.Name?.ToString() ?? "<unnamed>", textureCoordinates.Count);
    }

    var transform = ComputeWorldTransform(nif, shape);
    var diffuse = ExtractDiffuseTexturePath(nif, shape, ownerModKey);

    if (diffuse == null)
      _logger.Debug("Shape {ShapeName} has no diffuse texture.", shape.Name?.ToString() ?? "<unnamed>");

    meshData = new MeshData(vertices, normals, textureCoordinates, indices, transform, diffuse);
    return true;
  }

  private static List<Vector3>? ExtractVertices(INiShape shape)
  {
    switch (shape)
    {
      case BSTriShape { VertexPositions: not null } bsTriShape:
        return bsTriShape.VertexPositions.Select(v => v).ToList();
      case NiTriShape niTriShape:
        var data = niTriShape.GeometryData;
        if (data?.Vertices != null)
          return data.Vertices.Select(v => v).ToList();
        break;
    }

    return null;
  }

  private static List<int>? ExtractIndices(INiShape shape)
  {
    IEnumerable<Triangle>? triangles = shape switch
    {
      BSTriShape { Triangles: not null } bsTriShape => bsTriShape.Triangles,
      NiTriShape niTriShape => niTriShape.Triangles ?? niTriShape.GeometryData?.Triangles,
      _ => null
    };

    if (triangles == null)
      return null;

    var result = new List<int>();
    foreach (var tri in triangles)
    {
      result.Add(tri.V1);
      result.Add(tri.V2);
      result.Add(tri.V3);
    }

    return result;
  }

  private static List<Vector3>? ExtractNormals(INiShape shape)
  {
    switch (shape)
    {
      case BSTriShape { Normals.Count: > 0 } bsTriShape:
        return bsTriShape.Normals.Select(n => n).ToList();
      case NiTriShape niTriShape:
        var data = niTriShape.GeometryData;
        if (data?.Normals is { Count: > 0 })
          return data.Normals.Select(n => n).ToList();
        break;
    }

    return null;
  }

  private static List<Vector2>? ExtractTextureCoordinates(INiShape shape)
  {
    return shape switch
    {
      BSTriShape bsTriShape => ExtractFromBsTriShape(bsTriShape),
      NiTriShape niTriShape => ExtractFromNiTriShape(niTriShape),
      _ => null
    };
  }

  private static List<Vector2>? ExtractFromBsTriShape(BSTriShape shape)
  {
    var count = shape.VertexPositions?.Count ?? shape.VertexCount;
    if (count <= 0)
      return null;

    var fromSse = TryExtractFromVertexData(shape.VertexDataSSE, count);
    return fromSse ?? TryExtractFromVertexData(shape.VertexData, count);
  }

  private static List<Vector2>? TryExtractFromVertexData(List<BSVertexDataSSE>? data, int count)
  {
    if (data == null || data.Count < count)
      return null;

    var list = new List<Vector2>(count);
    for (var i = 0; i < count; i++)
    {
      var uv = data[i].UV;
      list.Add(new Vector2((float)uv.U, (float)uv.V));
    }

    return list;
  }

  private static List<Vector2>? TryExtractFromVertexData(List<BSVertexData>? data, int count)
  {
    if (data == null || data.Count < count)
      return null;

    var list = new List<Vector2>(count);
    for (var i = 0; i < count; i++)
    {
      var uv = data[i].UV;
      list.Add(new Vector2((float)uv.U, (float)uv.V));
    }

    return list;
  }

  private static List<Vector2>? ExtractFromNiTriShape(NiTriShape shape)
  {
    var data = shape.GeometryData;
    var vertexCount = data?.Vertices?.Count ?? data?.NumVertices ?? shape.VertexCount;
    if (vertexCount <= 0)
      return null;

    var uvList = data?.UVSets;
    if (uvList == null || uvList.Count == 0)
      return null;

    if (uvList.Count < vertexCount)
      return null;

    var result = new List<Vector2>(vertexCount);
    for (var i = 0; i < vertexCount; i++)
    {
      var uv = uvList[i];
      result.Add(new Vector2(uv.U, uv.V));
    }

    return result;
  }

  private static List<Vector3> ComputeNormals(List<Vector3> vertices, List<int> indices)
  {
    var normals = Enumerable.Repeat(Vector3.Zero, vertices.Count).ToList();

    for (var i = 0; i < indices.Count; i += 3)
    {
      var i0 = indices[i];
      var i1 = indices[i + 1];
      var i2 = indices[i + 2];

      if (i0 >= vertices.Count || i1 >= vertices.Count || i2 >= vertices.Count)
        continue;

      var a = vertices[i0];
      var b = vertices[i1];
      var c = vertices[i2];

      var normal = Vector3.Cross(b - a, c - a);
      if (normal != Vector3.Zero)
        normal = Vector3.Normalize(normal);

      normals[i0] += normal;
      normals[i1] += normal;
      normals[i2] += normal;
    }

    for (var i = 0; i < normals.Count; i++)
      if (normals[i] != Vector3.Zero)
        normals[i] = Vector3.Normalize(normals[i]);
      else
        normals[i] = Vector3.UnitZ;

    return normals;
  }

  private static Matrix4x4 ComputeWorldTransform(NifFile nif, INiShape shape)
  {
    var world = Matrix4x4.Identity;
    const int MaxDepth = 256;

    INiObject? current = shape;
    var depth = 0;

    while (current is NiAVObject avObject)
    {
      var local = CreateLocalTransform(avObject);
      world = Matrix4x4.Multiply(local, world);

      current = nif.GetParentBlock(avObject);
      depth++;
      if (depth > MaxDepth)
        break;
    }

    return world;
  }

  private static Matrix4x4 CreateLocalTransform(NiAVObject avObject)
  {
    var scale = avObject.Scale == 0 ? 1f : avObject.Scale;
    var scaleMatrix = Matrix4x4.CreateScale(scale);

    var rot = avObject.Rotation;
    var rotationMatrix = new Matrix4x4(
        rot.M11, rot.M12, rot.M13, 0,
        rot.M21, rot.M22, rot.M23, 0,
        rot.M31, rot.M32, rot.M33, 0,
        0, 0, 0, 1);

    var translationMatrix = Matrix4x4.CreateTranslation(avObject.Translation);

    var result = Matrix4x4.Multiply(scaleMatrix, rotationMatrix);
    result = Matrix4x4.Multiply(result, translationMatrix);
    return result;
  }

  private string? ExtractDiffuseTexturePath(NifFile nif, INiShape shape, ModKey? ownerModKey)
  {
    var shapeName = shape.Name?.ToString() ?? "<unnamed>";
    var candidates = new List<string>();

    CollectCandidates(nif.GetBlock<BSLightingShaderProperty>(shape.ShaderPropertyRef));

    if (candidates.Count == 0 && shape.Properties != null)
      foreach (var propRef in shape.Properties.References)
        CollectCandidates(nif.GetBlock<BSLightingShaderProperty>(propRef));

    foreach (var candidate in candidates)
    {
      if (!IsLikelyDiffuseTexture(candidate))
      {
        _logger.Debug("Skipping non-diffuse texture candidate {Texture} for shape {Shape}", candidate,
            shapeName);
        continue;
      }

      if (Path.IsPathRooted(candidate) && File.Exists(candidate))
      {
        _logger.Debug("Using absolute texture path {TexturePath} for shape {Shape}", candidate, shapeName);
        return candidate;
      }

      var normalized = NormalizeAssetPath(candidate);
      var resolved = assetLocator.ResolveAssetPath(normalized, ownerModKey);
      if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
      {
        _logger.Debug("Resolved texture candidate {Texture} to {ResolvedPath} for shape {Shape}", candidate,
            resolved, shapeName);
        return resolved;
      }

      _logger.Debug("Texture candidate {Texture} not found for shape {Shape}", candidate, shapeName);
    }

    if (candidates.Count > 0)
      _logger.Debug("Found {CandidateCount} texture candidates for shape {Shape} but none looked diffuse.",
          candidates.Count, shapeName);
    else
      _logger.Debug("No texture path resolved for shape {Shape}", shapeName);

    return null;

    void CollectCandidates(BSLightingShaderProperty? shader)
    {
      candidates.AddRange(EnumerateTexturePaths(nif, shader));
    }
  }

  private static IEnumerable<string> EnumerateTexturePaths(NifFile nif, BSLightingShaderProperty? shader)
  {
    if (shader == null)
      yield break;

    if (shader.TextureSetRef == null || shader.TextureSetRef.IsEmpty())
      yield break;

    var set = nif.GetBlock<BSShaderTextureSet>(shader.TextureSetRef);
    if (set?.Textures == null)
      yield break;

    foreach (var textureRef in set.Textures)
    {
      var path = textureRef?.Content;
      if (string.IsNullOrWhiteSpace(path))
        path = textureRef?.ToString();

      if (!string.IsNullOrWhiteSpace(path))
        yield return path!;
    }
  }

  private static bool IsLikelyDiffuseTexture(string texturePath)
  {
    var name = Path.GetFileNameWithoutExtension(texturePath);
    if (string.IsNullOrWhiteSpace(name))
      return true;

    var lower = name.ToLowerInvariant();
    var segments = lower.Split(['_', '-', ' '], StringSplitOptions.RemoveEmptyEntries);

    return !segments.Any(segment => _nonDiffuseSegments.Contains(segment)) && _nonDiffuseSubstrings.All(keyword => !lower.Contains(keyword));
  }

  private static IModelGetter? SelectModel(
      IGenderedItemGetter<IModelGetter?>? worldModel,
      GenderedModelVariant preferred,
      out GenderedModelVariant resolvedVariant)
  {
    resolvedVariant = preferred;

    if (worldModel == null)
      return null;

    if (preferred == GenderedModelVariant.Female)
    {
      if (worldModel.Female != null)
      {
        resolvedVariant = GenderedModelVariant.Female;
        return worldModel.Female;
      }

      if (worldModel.Male != null)
      {
        resolvedVariant = GenderedModelVariant.Male;
        return worldModel.Male;
      }
    }
    else
    {
      if (worldModel.Male != null)
      {
        resolvedVariant = GenderedModelVariant.Male;
        return worldModel.Male;
      }

      if (worldModel.Female != null)
      {
        resolvedVariant = GenderedModelVariant.Female;
        return worldModel.Female;
      }
    }

    return worldModel.Male ?? worldModel.Female;
  }

  private static string? ResolveModelPath(ISimpleModelGetter model)
  {
    var file = model.File;

    if (!string.IsNullOrWhiteSpace(file.DataRelativePath.Path))
      return NormalizeAssetPath(file.DataRelativePath.Path);

    return !string.IsNullOrWhiteSpace(file.GivenPath) ? NormalizeAssetPath(file.GivenPath) : null;
  }

  private static string GetBodyRelativePath(GenderedModelVariant gender)
  {
    return gender == GenderedModelVariant.Female ? FemaleBodyRelativePath : MaleBodyRelativePath;
  }

  private static string NormalizeAssetPath(string path)
  {
    var normalized = path.Replace('\\', '/').Trim();
    while ("/".StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
      normalized = normalized[1..];
    return normalized;
  }

  private static string FormatExpectedPath(string dataPath, string assetKey)
  {
    if (Path.IsPathRooted(assetKey))
      return assetKey;

    var systemRelative = assetKey.Replace('/', Path.DirectorySeparatorChar);
    return Path.Combine(dataPath, systemRelative);
  }

  private readonly record struct MeshData(
      IReadOnlyList<Vector3> Vertices,
      IReadOnlyList<Vector3> Normals,
      IReadOnlyList<Vector2>? TextureCoordinates,
      IReadOnlyList<int> Indices,
      Matrix4x4 Transform,
      string? DiffuseTexturePath);
}
