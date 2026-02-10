using Boutique.Models;

namespace Boutique.ViewModels;

public class LocationRecordViewModel(LocationRecord locationRecord)
  : SelectableRecordViewModel<LocationRecord>(locationRecord)
{
  public LocationRecord LocationRecord => Record;
}
