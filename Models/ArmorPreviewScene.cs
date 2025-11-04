using System.Collections.Generic;
using System.Numerics;

namespace RequiemGlamPatcher.Models;

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
    IReadOnlyList<string> MissingAssets);
