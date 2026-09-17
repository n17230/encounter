using System.Collections.Generic;

// Super simple threat system: 1 point of threat per 1 point of damage
// dealt. Server-only bookkeeping, never read by clients - EnemyAI uses it
// to pick which player to attack instead of pure proximity.
public class ThreatTable : UnityEngine.MonoBehaviour
{
    private readonly Dictionary<ulong, float> threatByClientId = new Dictionary<ulong, float>();

    public IReadOnlyDictionary<ulong, float> ThreatByClientId => threatByClientId;

    public void AddThreat(ulong clientId, float amount)
    {
        if (amount <= 0f) return;
        threatByClientId.TryGetValue(clientId, out float current);
        threatByClientId[clientId] = current + amount;
    }

    // Wipes this one client's own entry only - e.g. on death (see
    // PlayerRespawn.HandleDeath), not a full-table clear, so other
    // players' threat on this mob is unaffected.
    public void RemoveThreatFor(ulong clientId)
    {
        threatByClientId.Remove(clientId);
    }
}
