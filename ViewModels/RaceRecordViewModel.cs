using Boutique.Models;

namespace Boutique.ViewModels;

public class RaceRecordViewModel(RaceRecord raceRecord)
  : SelectableRecordViewModel<RaceRecord>(raceRecord)
{
  public RaceRecord RaceRecord => Record;
}
