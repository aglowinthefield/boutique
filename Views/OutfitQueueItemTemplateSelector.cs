using System.Windows;
using System.Windows.Controls;
using Boutique.ViewModels;

namespace Boutique.Views;

public class OutfitQueueItemTemplateSelector : DataTemplateSelector
{
  public DataTemplate? DraftTemplate { get; set; }
  public DataTemplate? SeparatorTemplate { get; set; }

  public override DataTemplate? SelectTemplate(object? item, DependencyObject container) =>
    item switch
    {
      OutfitDraftViewModel     => DraftTemplate,
      OutfitSeparatorViewModel => SeparatorTemplate,
      _                        => base.SelectTemplate(item, container)
    };
}
