using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

public sealed record ContainerContentItem(
  FormKey FormKey,
  string Name,
  string EditorId,
  int Count,
  string ModName)
{
  public string DisplayName
  {
    get
    {
      if (!string.IsNullOrEmpty(Name))
      {
        return Name;
      }

      if (!string.IsNullOrEmpty(EditorId))
      {
        return EditorId;
      }

      return FormKey.ToString();
    }
  }
}
