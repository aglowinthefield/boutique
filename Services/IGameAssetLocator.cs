using Mutagen.Bethesda.Plugins;

namespace Boutique.Services;

public interface IGameAssetLocator
{
    /// <summary>
    /// Attempts to resolve a relative asset path into a physical file on disk, extracting from archives if necessary.
    /// </summary>
    /// <param name="relativePath">Data-relative path (e.g. textures/armor/foo.dds). Absolute paths are returned as-is when they exist.</param>
    /// <param name="modKeyHint">Optional mod hint to prioritize associated archives.</param>
    /// <returns>Full path to a readable file if found; otherwise null.</returns>
    string? ResolveAssetPath(string relativePath, ModKey? modKeyHint = null);
}
