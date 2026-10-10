using System.Collections.Generic;

// Server-wide "who holds this Id" map - one instance per kind of thing that
// must be unique across connected players (CharacterEquipment's items,
// PlayerAbilities' spells). Claiming is first-come: an Id someone else
// holds can't be taken, but re-claiming your own is a harmless no-op, which
// is what lets the lobby confirm and the spawn-time sync both claim the
// same Ids for the same client. Pure (no Unity types) so the claim rules
// are testable; each owner is a static, server-only instance reset through
// NetworkBootstrap.ResetServerSessionState.
public class OwnershipMap
{
    private readonly Dictionary<string, ulong> owners = new Dictionary<string, ulong>();
    private readonly List<string> removeScratch = new List<string>();

    public bool TryClaim(string id, ulong clientId)
    {
        if (owners.TryGetValue(id, out ulong owner) && owner != clientId) return false;
        owners[id] = clientId;
        return true;
    }

    // No production caller yet (the claim cores only use TryClaim and the
    // ReleaseAll* pair) - kept as the map's natural query, exercised by tests.
    public bool IsOwnedBy(string id, ulong clientId)
    {
        return owners.TryGetValue(id, out ulong owner) && owner == clientId;
    }

    // Only the holder can release - a release by anyone else is a no-op, so
    // a stale or malicious release can't free someone else's claim. No
    // production caller yet (single-Id releases all go through
    // ReleaseAllExcept); kept with IsOwnedBy, exercised by tests.
    public void Release(string id, ulong clientId)
    {
        if (id != null && owners.TryGetValue(id, out ulong owner) && owner == clientId) owners.Remove(id);
    }

    public void ReleaseAll(ulong clientId) => ReleaseAllExcept(clientId, null);

    // Frees everything the client holds that isn't in kept - how a confirm
    // or re-sync drops the Ids the client no longer wants without touching
    // what it's keeping.
    public void ReleaseAllExcept(ulong clientId, ICollection<string> kept)
    {
        removeScratch.Clear();
        foreach (KeyValuePair<string, ulong> pair in owners)
        {
            if (pair.Value != clientId) continue;
            if (kept != null && kept.Contains(pair.Key)) continue;
            removeScratch.Add(pair.Key);
        }
        foreach (string id in removeScratch) owners.Remove(id);
    }

    public void Clear() => owners.Clear();
}
