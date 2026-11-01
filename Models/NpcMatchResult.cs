using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

public sealed class NpcMatchResult(NpcFilterData npc, string criteria, bool isConfirmed = true)
{
  public NpcFilterData Npc { get; } = npc;
  public string DisplayName => Npc.DisplayName;
  public string? EditorId => Npc.EditorId;
  public FormKey FormKey => Npc.FormKey;
  public string Criteria { get; } = criteria;

  /// <summary>False when supported criteria match but remaining restrictions cannot be evaluated.</summary>
  public bool IsConfirmed { get; } = isConfirmed;
}
