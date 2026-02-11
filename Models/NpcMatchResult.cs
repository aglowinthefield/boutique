using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

public sealed class NpcMatchResult(NpcFilterData npc, string criteria)
{
  public NpcFilterData Npc { get; } = npc;
  public string DisplayName => Npc.DisplayName;
  public string? EditorId => Npc.EditorId;
  public FormKey FormKey => Npc.FormKey;
  public string Criteria { get; } = criteria;
}
