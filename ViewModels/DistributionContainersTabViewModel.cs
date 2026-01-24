using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Boutique.ViewModels;

public partial class DistributionContainersTabViewModel : ReactiveObject
{
    [Reactive] private bool _isLoading;

    [Reactive] private string _statusMessage = "Container distribution coming soon...";
}
