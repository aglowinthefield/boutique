using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

public interface IGameRecord
{
  FormKey FormKey { get; }
  string? EditorID { get; }
  ModKey ModKey { get; }
  string DisplayName { get; }
  string FormKeyString { get; }
  string ModDisplayName { get; }
}
