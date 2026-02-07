using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Models;

public record LeveledListCreationRequest(
  string Name,
  string EditorId,
  IReadOnlyList<LeveledListEntryRequest> Entries,
  bool UseAll = false,
  LeveledItem.Flag Flags = LeveledItem.Flag.CalculateFromAllLevelsLessThanOrEqualPlayer,
  FormKey? ExistingFormKey = null);

public record LeveledListEntryRequest(
  FormKey ItemFormKey,
  short Level = 1,
  short Count = 1);

public record LeveledListCreationResult(
  string EditorId,
  FormKey FormKey);
