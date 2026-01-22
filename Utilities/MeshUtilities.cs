using System.IO;
using System.Numerics;
using NiflySharp;
using NiflySharp.Blocks;
using NiflySharp.Structs;

namespace Boutique.Utilities;

public static class MeshUtilities
{
    private static readonly HashSet<string> NonDiffuseSegments = new(StringComparer.OrdinalIgnoreCase)
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

    private static readonly string[] NonDiffuseSubstrings =
    [
        "normalmap", "_normal", "_nmap", "_smap", "_msn", "_spec", "_specmap", "_glow", "_env", "_envmap",
        "_cubemap", "_cmap", "_emit", "_emissive", "_mask", "_rough", "_roughness", "_metal", "_metallic",
        "_height", "_displace", "_opacity", "_alpha"
    ];

    public static List<Vector3>? ExtractVertices(INiShape shape)
    {
        switch (shape)
        {
            case BSTriShape { VertexPositions: not null } bsTriShape:
                return bsTriShape.VertexPositions.Select(v => v).ToList();
            case NiTriShape niTriShape:
                var data = niTriShape.GeometryData;
                if (data?.Vertices != null)
                {
                    return data.Vertices.Select(v => v).ToList();
                }

                break;
        }

        return null;
    }

    public static List<int>? ExtractIndices(INiShape shape)
    {
        IEnumerable<Triangle>? triangles = shape switch
        {
            BSTriShape { Triangles: not null } bsTriShape => bsTriShape.Triangles,
            NiTriShape niTriShape => niTriShape.Triangles ?? niTriShape.GeometryData?.Triangles,
            _ => null
        };

        if (triangles == null)
        {
            return null;
        }

        var result = new List<int>();
        foreach (var tri in triangles)
        {
            result.Add(tri.V1);
            result.Add(tri.V2);
            result.Add(tri.V3);
        }

        return result;
    }

    public static List<Vector3>? ExtractNormals(INiShape shape)
    {
        switch (shape)
        {
            case BSTriShape { Normals.Count: > 0 } bsTriShape:
                return bsTriShape.Normals.Select(n => n).ToList();
            case NiTriShape niTriShape:
                var data = niTriShape.GeometryData;
                if (data?.Normals is { Count: > 0 })
                {
                    return data.Normals.Select(n => n).ToList();
                }

                break;
        }

        return null;
    }

    public static List<Vector2>? ExtractTextureCoordinates(INiShape shape) =>
        shape switch
        {
            BSTriShape bsTriShape => ExtractFromBsTriShape(bsTriShape),
            NiTriShape niTriShape => ExtractFromNiTriShape(niTriShape),
            _ => null
        };

    public static List<Vector3> ComputeNormals(List<Vector3> vertices, List<int> indices)
    {
        var normals = Enumerable.Repeat(Vector3.Zero, vertices.Count).ToList();

        for (var i = 0; i < indices.Count; i += 3)
        {
            var i0 = indices[i];
            var i1 = indices[i + 1];
            var i2 = indices[i + 2];

            if (i0 >= vertices.Count || i1 >= vertices.Count || i2 >= vertices.Count)
            {
                continue;
            }

            var a = vertices[i0];
            var b = vertices[i1];
            var c = vertices[i2];

            var normal = Vector3.Cross(b - a, c - a);
            if (normal != Vector3.Zero)
            {
                normal = Vector3.Normalize(normal);
            }

            normals[i0] += normal;
            normals[i1] += normal;
            normals[i2] += normal;
        }

        for (var i = 0; i < normals.Count; i++)
        {
            if (normals[i] != Vector3.Zero)
            {
                normals[i] = Vector3.Normalize(normals[i]);
            }
            else
            {
                normals[i] = Vector3.UnitZ;
            }
        }

        return normals;
    }

    public static Matrix4x4 ComputeWorldTransform(NifFile nif, INiShape shape)
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
            {
                break;
            }
        }

        return world;
    }

    public static IEnumerable<string> EnumerateTexturePaths(NifFile nif, BSLightingShaderProperty? shader)
    {
        if (shader == null)
        {
            yield break;
        }

        if (shader.TextureSetRef == null || shader.TextureSetRef.IsEmpty())
        {
            yield break;
        }

        var set = nif.GetBlock<BSShaderTextureSet>(shader.TextureSetRef);
        if (set?.Textures == null)
        {
            yield break;
        }

        foreach (var textureRef in set.Textures)
        {
            var path = textureRef?.Content;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = textureRef?.ToString();
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return path!;
            }
        }
    }

    public static bool IsLikelyDiffuseTexture(string texturePath)
    {
        var name = Path.GetFileNameWithoutExtension(texturePath);
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        var lower = name.ToLowerInvariant();
        var segments = lower.Split(['_', '-', ' '], StringSplitOptions.RemoveEmptyEntries);

        return !segments.Any(segment => NonDiffuseSegments.Contains(segment)) &&
               NonDiffuseSubstrings.All(keyword => !lower.Contains(keyword));
    }

    private static List<Vector2>? ExtractFromBsTriShape(BSTriShape shape)
    {
        var count = shape.VertexPositions?.Count ?? shape.VertexCount;
        if (count <= 0)
        {
            return null;
        }

        var fromSse = TryExtractFromVertexData(shape.VertexDataSSE, count);
        return fromSse ?? TryExtractFromVertexData(shape.VertexData, count);
    }

    private static List<Vector2>? TryExtractFromVertexData(List<BSVertexDataSSE>? data, int count)
    {
        if (data == null || data.Count < count)
        {
            return null;
        }

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
        {
            return null;
        }

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
        {
            return null;
        }

        var uvList = data?.UVSets;
        if (uvList == null || uvList.Count == 0)
        {
            return null;
        }

        if (uvList.Count < vertexCount)
        {
            return null;
        }

        var result = new List<Vector2>(vertexCount);
        for (var i = 0; i < vertexCount; i++)
        {
            var uv = uvList[i];
            result.Add(new Vector2(uv.U, uv.V));
        }

        return result;
    }

    private static Matrix4x4 CreateLocalTransform(NiAVObject avObject)
    {
        var scale = avObject.Scale == 0 ? 1f : avObject.Scale;
        var scaleMatrix = Matrix4x4.CreateScale(scale);

        var rot = avObject.Rotation;
        var rotationMatrix = new Matrix4x4(
            rot.M11,
            rot.M12,
            rot.M13,
            0,
            rot.M21,
            rot.M22,
            rot.M23,
            0,
            rot.M31,
            rot.M32,
            rot.M33,
            0,
            0,
            0,
            0,
            1);

        var translationMatrix = Matrix4x4.CreateTranslation(avObject.Translation);

        var result = Matrix4x4.Multiply(scaleMatrix, rotationMatrix);
        result = Matrix4x4.Multiply(result, translationMatrix);
        return result;
    }
}
