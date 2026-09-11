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
}
