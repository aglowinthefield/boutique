using Boutique.Models;

namespace Boutique.ViewModels;

public class KeywordRecordViewModel(KeywordRecord keywordRecord)
    : SelectableRecordViewModel<KeywordRecord>(keywordRecord)
{
  public KeywordRecord KeywordRecord => Record;
}
