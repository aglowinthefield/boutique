using System.Diagnostics.CodeAnalysis;
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
  IReadOnlyList<Vector3> Vertices,
  IReadOnlyList<Vector3> Normals,
  IReadOnlyList<Vector2>? TextureCoordinates,
  IReadOnlyList<int> Indices,
  Matrix4x4 Transform,
  string? DiffuseTexturePath);

public sealed record ArmorPreviewScene(
  IReadOnlyList<PreviewMeshShape> Meshes,
  IReadOnlyList<string> MissingAssets);

public sealed record OutfitMetadata(
  string? OutfitLabel,
  string? SourceFile,
  bool IsWinner,
  bool ContainsLeveledItems = false);

[SuppressMessage(
  "Naming",
  "CA1711:Identifiers should not have incorrect suffix",
  Justification = "ArmorPreviewSceneCollection is a logical name for a collection of preview scenes")]
public sealed class ArmorPreviewSceneCollection(
  int count,
  int initialIndex,
  IReadOnlyList<OutfitMetadata> metadata,
  Func<int, GenderedModelVariant, Task<ArmorPreviewScene>> sceneBuilder,
  GenderedModelVariant initialGender = GenderedModelVariant.Female)
{
  private readonly Dictionary<(int Index, GenderedModelVariant Gender), ArmorPreviewScene> _sceneCache = new();

  public int Count { get; } = count;
  public int InitialIndex { get; } = initialIndex;
  public GenderedModelVariant InitialGender { get; } = initialGender;
  public IReadOnlyList<OutfitMetadata> Metadata { get; } = metadata;

  public async Task<ArmorPreviewScene> GetSceneAsync(int index, GenderedModelVariant gender)
  {
    if (_sceneCache.TryGetValue((index, gender), out var cached))
    {
      return cached;
    }

    var scene = await sceneBuilder(index, gender);
    _sceneCache[(index, gender)] = scene;
    return scene;
  }

  public void ClearCache() => _sceneCache.Clear();
}
