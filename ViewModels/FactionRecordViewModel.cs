using Boutique.Models;

namespace Boutique.ViewModels;

public class FactionRecordViewModel(FactionRecord factionRecord)
  : SelectableRecordViewModel<FactionRecord>(factionRecord)
{
  public FactionRecord FactionRecord => Record;
}
