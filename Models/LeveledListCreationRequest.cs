using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Models;

/// <summary>
/// References a leveled list as an outfit item or a leveled list entry. The list is identified either
/// by an existing FormKey or by the <see cref="DraftId"/> of a list being created in the same save batch.
/// </summary>
public record LeveledListRef(FormKey? ExistingFormKey = null, Guid? DraftId = null);

public record LeveledListCreationRequest(
  string EditorId,
  IReadOnlyList<LeveledListEntryRequest> Entries,
  LeveledItem.Flag Flags = LeveledItem.Flag.CalculateFromAllLevelsLessThanOrEqualPlayer,
  FormKey? ExistingFormKey = null,
  Guid? DraftId = null);

public record LeveledListEntryRequest(
  FormKey? ItemFormKey,
  short Level = 1,
  short Count = 1,
  Guid? DraftListId = null);

public record LeveledListCreationResult(
  string EditorId,
  FormKey FormKey);
