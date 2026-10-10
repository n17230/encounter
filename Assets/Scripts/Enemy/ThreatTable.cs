using System.Collections.Generic;

// Super simple threat system: 1 point of threat per 1 point of damage
// dealt. Server-only bookkeeping, never read by clients - EnemyAI uses it
// to pick which player to attack instead of pure proximity. Also keeps a
// separate healing-credit tally per client (fed by
// CharacterStats.GenerateHealingThreat, so it only ever grows on a mob that
// is already fighting the healed player) for TargetingMode.MostHealing -
// kept apart from threat so healing credit never changes who HighestThreat
// picks.
public class ThreatTable : UnityEngine.MonoBehaviour
{
    private readonly Dictionary<ulong, float> threatByClientId = new Dictionary<ulong, float>();
    private readonly Dictionary<ulong, float> healingByClientId = new Dictionary<ulong, float>();

    public IReadOnlyDictionary<ulong, float> ThreatByClientId => threatByClientId;
    public IReadOnlyDictionary<ulong, float> HealingByClientId => healingByClientId;

    // Every currently-active ThreatTable - see Registry. Healing threat and
    // death-time threat wipes both need "every mob's table".
    public static IReadOnlyList<ThreatTable> All => Registry<ThreatTable>.All;

    private void OnEnable() => Registry<ThreatTable>.Add(this);
    private void OnDisable() => Registry<ThreatTable>.Remove(this);

    public void AddThreat(ulong clientId, float amount)
    {
        if (amount <= 0f) return;
        threatByClientId.TryGetValue(clientId, out float current);
        threatByClientId[clientId] = current + amount;
    }

    public void AddHealing(ulong clientId, float amount)
    {
        if (amount <= 0f) return;
        healingByClientId.TryGetValue(clientId, out float current);
        healingByClientId[clientId] = current + amount;
    }

    // Wipes this one client's own entries only - e.g. on death (see
    // PlayerRespawn.HandleDeath), not a full-table clear, so other
    // players' threat on this mob is unaffected. Healing credit goes with
    // it: a dead healer shouldn't stay a MostHealing mob's pick.
    public void RemoveThreatFor(ulong clientId)
    {
        threatByClientId.Remove(clientId);
        healingByClientId.Remove(clientId);
    }
}
