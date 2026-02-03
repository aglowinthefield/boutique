using Boutique.Models;

namespace Boutique.ViewModels;

public class ClassRecordViewModel(ClassRecord classRecord)
    : SelectableRecordViewModel<ClassRecord>(classRecord)
{
  public ClassRecord ClassRecord => Record;
}
