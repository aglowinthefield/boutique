using System.Numerics;

namespace Boutique.Models;

public enum GenderedModelVariant
{
    Female,
    Male
}

public sealed record PreviewMeshShape(
    string Name,
    string SourcePath,
    GenderedModelVariant Variant,
    IReadOnlyList<Vector3> Vertices,
    IReadOnlyList<Vector3> Normals,
    IReadOnlyList<Vector2>? TextureCoordinates,
    IReadOnlyList<int> Indices,
    Matrix4x4 Transform,
    string? DiffuseTexturePath);

public sealed record ArmorPreviewScene(
    GenderedModelVariant Gender,
    IReadOnlyList<PreviewMeshShape> Meshes,
    IReadOnlyList<string> MissingAssets,
    string? OutfitLabel = null,
    string? SourceFile = null,
    bool IsWinner = false);

public sealed record OutfitMetadata(
    string? OutfitLabel,
    string? SourceFile,
    bool IsWinner);

public sealed class ArmorPreviewSceneCollection
{
    private readonly Func<int, GenderedModelVariant, Task<ArmorPreviewScene>> _sceneBuilder;
    private readonly Dictionary<(int Index, GenderedModelVariant Gender), ArmorPreviewScene> _sceneCache = new();

    public ArmorPreviewSceneCollection(
        int count,
        int initialIndex,
        IReadOnlyList<OutfitMetadata> metadata,
        Func<int, GenderedModelVariant, Task<ArmorPreviewScene>> sceneBuilder,
        GenderedModelVariant initialGender = GenderedModelVariant.Female)
    {
        Count = count;
        InitialIndex = initialIndex;
        InitialGender = initialGender;
        Metadata = metadata;
        _sceneBuilder = sceneBuilder;
    }

    public int Count { get; }
    public int InitialIndex { get; }
    public GenderedModelVariant InitialGender { get; }
    public IReadOnlyList<OutfitMetadata> Metadata { get; }

    public async Task<ArmorPreviewScene> GetSceneAsync(int index, GenderedModelVariant gender)
    {
        if (_sceneCache.TryGetValue((index, gender), out var cached))
            return cached;

        var scene = await _sceneBuilder(index, gender);
        _sceneCache[(index, gender)] = scene;
        return scene;
    }

    public void ClearCache() => _sceneCache.Clear();
}
