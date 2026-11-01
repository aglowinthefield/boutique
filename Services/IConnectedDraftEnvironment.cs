using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services;

/// <summary>The external loaded-plugin environment; patch construction and validation are not substituted.</summary>
public interface IConnectedDraftEnvironment
{
  SkyrimRelease Release { get; }
  IReadOnlyList<ISkyrimModGetter> LoadedPlugins { get; }
  void ReleaseHandles();
  Task RefreshAsync(string outputPath);
}

public sealed class MutagenConnectedDraftEnvironment(MutagenService service) : IConnectedDraftEnvironment
{
  public SkyrimRelease Release => service.SkyrimRelease;
  public IReadOnlyList<ISkyrimModGetter> LoadedPlugins => service.GetInspectableLoadedPlugins();

  public void ReleaseHandles() => service.ReleaseLinkCache();

  public async Task RefreshAsync(string outputPath)
  {
    // Refresh loaded data only. The writer separately validates the exact output path,
    // unless recovering a failed first write which never created that output.
    await service.RefreshLinkCacheAsync();
    _ = LoadedPlugins;

  }
}
