using System.IO;
using System.Numerics;
using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using NiflySharp;
using NiflySharp.Blocks;
using Serilog;

namespace Boutique.Services;

public class ArmorPreviewService(MutagenService mutagenService, GameAssetLocator assetLocator, ILogger logger)
{
    private const string FemaleBodyRelativePath = "meshes/actors/character/character assets/femalebody_0.nif";
    private const string MaleBodyRelativePath = "meshes/actors/character/character assets/malebody_0.nif";
    private static readonly ModKey _skyrimBaseModKey = ModKey.FromNameAndExtension("Skyrim.esm");

    private readonly ILogger _logger = logger.ForContext<ArmorPreviewService>();

    public async Task<ArmorPreviewScene> BuildPreviewAsync(
        IEnumerable<ArmorRecordViewModel> armorPieces,
        GenderedModelVariant preferredGender,
        CancellationToken cancellationToken = default)
    {
        if (!mutagenService.IsInitialized)
        {
            throw new InvalidOperationException("Mutagen service has not been initialized.");
        }

        var dataPath = mutagenService.DataFolderPath;
        var linkCache = mutagenService.LinkCache;

        if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath))
        {
            throw new DirectoryNotFoundException("Skyrim Data path is not set or does not exist.");
        }

        if (linkCache == null)
        {
            throw new InvalidOperationException("Link cache is not available.");
        }

        var pieces = armorPieces?.ToList() ?? [];
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
        _logger.Debug(
            "Building preview for {PieceCount} armor pieces with preferred gender {PreferredGender}",
            pieces.Count, preferredGender);
        var meshes = new List<PreviewMeshShape>();
        var missingAssets = new List<string>();

        var bodyRelative = GetBodyRelativePath(gender);
        var bodyAssetKey = PathUtilities.NormalizeAssetPath(bodyRelative);
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

                var (model, variantForAddon) = SelectModel(addon.WorldModel, gender);
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

                var meshAssetKey = PathUtilities.NormalizeAssetPath(modelPath);
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
                {
                    continue;
                }

                var partName = $"{armorName} ({addon.EditorID ?? addon.FormKey.ToString()})";
                meshes.AddRange(LoadMeshesFromNif(
                    partName,
                    fullPath,
                    variantForAddon,
                    addon.FormKey.ModKey,
                    cancellationToken,
                    addon,
                    linkCache));
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
        {
            return GenderedModelVariant.Male;
        }

        var hasMaleOnlyAddon = pieces
            .SelectMany(p => p.Armor.Armature)
            .Select(link => linkCache.TryResolve<IArmorAddonGetter>(link.FormKey, out var addon) ? addon : null)
            .Where(addon => addon?.WorldModel != null)
            .Any(addon => addon!.WorldModel!.Female == null && addon.WorldModel.Male != null);

        return hasMaleOnlyAddon ? GenderedModelVariant.Male : GenderedModelVariant.Female;
    }

    private List<PreviewMeshShape> LoadMeshesFromNif(
        string partName,
        string meshPath,
        GenderedModelVariant variant,
        ModKey? ownerModKey,
        CancellationToken cancellationToken,
        IArmorAddonGetter? addon = null,
        ILinkCache? linkCache = null)
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

                if (!TryExtractMesh(nif, shape, ownerModKey, variant, addon, linkCache, out var meshData))
                {
                    _logger.Debug(
                        "Skipping shape {ShapeName} in {FullPath} due to missing geometry or texture data.",
                        shape.Name?.ToString() ?? "<unnamed>", meshPath);
                    continue;
                }

                if (meshData.DiffuseTexturePath == null)
                {
                    _logger.Debug(
                        "Skipping shape {ShapeName} because it has no diffuse texture.",
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
        }

        return meshes;
    }

    private bool TryExtractMesh(
        NifFile nif,
        INiShape shape,
        ModKey? ownerModKey,
        GenderedModelVariant variant,
        IArmorAddonGetter? addon,
        ILinkCache? linkCache,
        out MeshData meshData)
    {
        meshData = default;

        var vertices = MeshUtilities.ExtractVertices(shape);
        if (vertices == null || vertices.Count == 0)
        {
            _logger.Debug("Shape {ShapeName} has no vertices.", shape.Name?.ToString() ?? "<unnamed>");
            return false;
        }

        var indices = MeshUtilities.ExtractIndices(shape);
        if (indices == null || indices.Count == 0)
        {
            _logger.Debug("Shape {ShapeName} has no indices.", shape.Name?.ToString() ?? "<unnamed>");
            return false;
        }

        var extractedNormals = MeshUtilities.ExtractNormals(shape);
        List<Vector3> normals;

        if (extractedNormals != null && extractedNormals.Count == vertices.Count)
        {
            normals = extractedNormals;
        }
        else
        {
            normals = MeshUtilities.ComputeNormals(vertices, indices);
            var shapeName = shape.Name?.ToString() ?? "<unnamed>";
            if (extractedNormals == null)
            {
                _logger.Debug("Shape {ShapeName} provided no normals; computed fallback.", shapeName);
            }
            else
            {
                _logger.Debug(
                    "Shape {ShapeName} normals count {ProvidedCount} mismatched vertex count {VertexCount}; computed fallback.",
                    shapeName, extractedNormals.Count, vertices.Count);
            }
        }

        var textureCoordinates = MeshUtilities.ExtractTextureCoordinates(shape);
        if (textureCoordinates != null && textureCoordinates.Count != vertices.Count)
        {
            _logger.Debug(
                "Shape {ShapeName} texture coordinate count {TexCount} does not match vertex count {VertexCount}. Ignoring UVs.",
                shape.Name?.ToString() ?? "<unnamed>", textureCoordinates.Count, vertices.Count);
            textureCoordinates = null;
        }
        else if (textureCoordinates != null)
        {
            _logger.Debug(
                "Shape {ShapeName} extracted {TexCount} UV coordinates.",
                shape.Name?.ToString() ?? "<unnamed>", textureCoordinates.Count);
        }

        var transform = MeshUtilities.ComputeWorldTransform(nif, shape);
        var diffuse = ExtractDiffuseTexturePath(nif, shape, ownerModKey, addon, variant, linkCache);

        if (diffuse == null)
        {
            _logger.Debug("Shape {ShapeName} has no diffuse texture.", shape.Name?.ToString() ?? "<unnamed>");
        }

        meshData = new MeshData(vertices, normals, textureCoordinates, indices, transform, diffuse);
        return true;
    }

    private string? ExtractDiffuseTexturePath(
        NifFile nif,
        INiShape shape,
        ModKey? ownerModKey,
        IArmorAddonGetter? addon,
        GenderedModelVariant variant,
        ILinkCache? linkCache)
    {
        var shapeName = shape.Name?.String ?? shape.Name?.ToString() ?? "<unnamed>";
        var candidates = new List<string>();

        if (addon?.WorldModel == null || linkCache == null)
        {
            goto FallbackToNif;
        }

        var model = variant == GenderedModelVariant.Female
            ? addon.WorldModel.Female
            : addon.WorldModel.Male;

        if (model?.AlternateTextures == null)
        {
            goto FallbackToNif;
        }

        var matchingTexture = model.AlternateTextures
            .Where(t => t?.NewTexture != null && !t.NewTexture.IsNull)
            .FirstOrDefault(t =>
            {
                var meshName = t.Name ?? string.Empty;
                return string.IsNullOrEmpty(meshName) || shapeName.Equals(meshName, StringComparison.OrdinalIgnoreCase);
            });

        if (matchingTexture != null &&
            linkCache.TryResolve<ITextureSetGetter>(matchingTexture.NewTexture.FormKey, out var textureSet) &&
            !string.IsNullOrWhiteSpace(textureSet.Diffuse))
        {
            var textureSetModKey = matchingTexture.NewTexture.FormKey.ModKey;
            var resolvedPath = assetLocator.ResolveAssetPath(textureSet.Diffuse.ToString(), textureSetModKey);

            if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
            {
                _logger.Debug(
                    "Using alternate texture for ArmorAddon {Addon} shape '{Shape}': {Texture}",
                    addon.EditorID,
                    shapeName,
                    textureSet.Diffuse);

                return resolvedPath;
            }
        }

        FallbackToNif:

        CollectCandidates(nif.GetBlock<BSLightingShaderProperty>(shape.ShaderPropertyRef));

        if (candidates.Count == 0 && shape.Properties != null)
        {
            foreach (var propRef in shape.Properties.References)
            {
                CollectCandidates(nif.GetBlock<BSLightingShaderProperty>(propRef));
            }
        }

        foreach (var candidate in candidates)
        {
            if (!MeshUtilities.IsLikelyDiffuseTexture(candidate))
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

            var normalized = PathUtilities.NormalizeAssetPath(candidate);
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
        {
            _logger.Debug(
                "Found {CandidateCount} texture candidates for shape {Shape} but none looked diffuse.",
                candidates.Count, shapeName);
        }
        else
        {
            _logger.Debug("No texture path resolved for shape {Shape}", shapeName);
        }

        return null;

        void CollectCandidates(BSLightingShaderProperty? shader) =>
            candidates.AddRange(MeshUtilities.EnumerateTexturePaths(nif, shader));
    }

    private static (IModelGetter? Model, GenderedModelVariant Variant) SelectModel(
        IGenderedItemGetter<IModelGetter?>? worldModel,
        GenderedModelVariant preferred)
    {
        if (worldModel == null)
        {
            return (null, preferred);
        }

        var (primary, secondary) = preferred == GenderedModelVariant.Female
            ? (worldModel.Female, worldModel.Male)
            : (worldModel.Male, worldModel.Female);

        var alternateVariant = preferred == GenderedModelVariant.Female
            ? GenderedModelVariant.Male
            : GenderedModelVariant.Female;

        return primary != null
            ? (primary, preferred)
            : (secondary, secondary != null ? alternateVariant : preferred);
    }

    private static string? ResolveModelPath(ISimpleModelGetter model)
    {
        var file = model.File;

        if (!string.IsNullOrWhiteSpace(file.DataRelativePath.Path))
        {
            return PathUtilities.NormalizeAssetPath(file.DataRelativePath.Path);
        }

        return !string.IsNullOrWhiteSpace(file.GivenPath) ? PathUtilities.NormalizeAssetPath(file.GivenPath) : null;
    }

    private static string GetBodyRelativePath(GenderedModelVariant gender) =>
        gender == GenderedModelVariant.Female ? FemaleBodyRelativePath : MaleBodyRelativePath;

    private static string FormatExpectedPath(string dataPath, string assetKey)
    {
        if (Path.IsPathRooted(assetKey))
        {
            return assetKey;
        }

        var systemRelative = PathUtilities.ToSystemPath(assetKey);
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
