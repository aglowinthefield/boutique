# Boutique

Boutique edits Skyrim distributions, outfit drafts, and leveled-list drafts, and writes records to an output plugin.

## Language

### Distributions

**Distribution document**:
The editable contents of a distribution file, including its entries, retained lines, format, destination, and last saved state.

**Distribution entry**:
A single distribution instruction, including its targeting conditions and any applicable chance.

**Affected NPC**:
An NPC targeted by a distribution entry, either through explicit selection or through its supported filters. Being affected describes eligibility, not a guaranteed assignment after chance and competing distributions are considered.
_Avoid_: Explicitly selected NPC (when referring to all affected NPCs)

### Drafts and output records

**Outfit draft**:
An editable workspace representation of an outfit, either new or associated with an existing output record.

**Leveled-list draft**:
An editable workspace representation of a leveled list, either new or associated with an existing output record.

**Connected drafts**:
Outfit and leveled-list drafts linked by leveled-list references. A reference can identify a list already saved in a plugin or another draft not yet saved.

**Output record**:
A game record stored in Boutique's output plugin, distinct from any draft used to edit it.

**Draft removal**:
Removal of a draft from the editing workspace, not deletion of its output record.
_Avoid_: Delete (when only removing a draft)

**Output-record deletion**:
Removal of a persisted record from the output plugin, distinct from removing its editing draft.
_Avoid_: Remove draft (when deleting an output record)

**Pending output-record deletion**:
A confirmed intention to delete an output record on an explicit save. The record still exists in the output plugin until that save succeeds.
