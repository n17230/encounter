using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// The character name a player chose in the lobby, on their player object so
// every client can read it off any player (party frames, the target frame).
// Server-written: the server copies it out of the matching LobbyState entry
// when the player spawns (at Start, LobbyState has been up for the whole
// lobby). Empty for a player that never went through the lobby - a joiner
// after Start - in which case the HUD falls back to its old labels.
public class PlayerIdentity : NetworkBehaviour
{
    public readonly NetworkVariable<FixedString64Bytes> DisplayName =
        new NetworkVariable<FixedString64Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        LobbyState lobby = LobbyState.Instance;
        if (lobby != null && lobby.TryGetEntry(OwnerClientId, out LobbyEntry entry)) DisplayName.Value = entry.Name;
    }

    // The lobby name of whichever player a character belongs to, or null
    // when there isn't one (a mob, or a nameless player) - callers supply
    // their own fallback label.
    public static string NameOf(Component character)
    {
        if (character == null) return null;
        PlayerIdentity identity = character.GetComponent<PlayerIdentity>();
        if (identity == null || identity.DisplayName.Value.Length == 0) return null;
        return identity.DisplayName.Value.ToString();
    }
}
