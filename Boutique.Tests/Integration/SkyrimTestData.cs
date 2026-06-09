using System.IO;

namespace Boutique.Tests.Integration;

public static class SkyrimTestData
{
    public const string EsmFileName = "Skyrim.esm";

    public const string MissingMessage =
        "Skyrim.esm not found. Drop it in Boutique.Tests/TestData/Game/ or set " +
        "BOUTIQUE_SKYRIM_DATA / BOUTIQUE_SKYRIM_ESM. See TestData/Game/README.md.";

    public static bool IsAvailable => ResolveEsmPath() is not null;

    public static string? ResolveEsmPath()
    {
        var direct = Environment.GetEnvironmentVariable("BOUTIQUE_SKYRIM_ESM");
        if (!string.IsNullOrWhiteSpace(direct) && File.Exists(direct))
        {
            return direct;
        }

        var dataFolder = Environment.GetEnvironmentVariable("BOUTIQUE_SKYRIM_DATA");
        if (!string.IsNullOrWhiteSpace(dataFolder))
        {
            var fromEnv = Path.Combine(dataFolder, EsmFileName);
            if (File.Exists(fromEnv))
            {
                return fromEnv;
            }
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "TestData", "Game", EsmFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
