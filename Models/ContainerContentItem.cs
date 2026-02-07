using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

public sealed record ContainerContentItem(
  FormKey FormKey,
  string Name,
  string EditorId,
  int Count,
  string ModName)
{
  public string DisplayName =>
    !string.IsNullOrEmpty(Name) ? Name :
    !string.IsNullOrEmpty(EditorId) ? EditorId :
    FormKey.ToString();
}
