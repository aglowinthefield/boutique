using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Boutique.Models;
using Boutique.ViewModels;

namespace Boutique.Services;

public interface IArmorPreviewService
{
    Task<ArmorPreviewScene> BuildPreviewAsync(
        IEnumerable<ArmorRecordViewModel> armorPieces,
        GenderedModelVariant preferredGender,
        CancellationToken cancellationToken = default);
}
