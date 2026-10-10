using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

// The pre-game lobby, server-authoritative, on one scene-placed
// NetworkObject (the "Lobby" object - an Editor step). Owns everything
// lobby-related: the phase, who the navigator is, one LobbyEntry per
// connected client (name, Ready, Admitted, confirmed picks). Nothing
// lobby-related lives on player objects - before Start there aren't any:
// NetworkBootstrap's approval callback only creates player objects once
// GameStarted is set, and StartGameRpc below is what sets it and spawns
// everyone who was admitted.
//
// Every client->server call is an [Rpc(SendTo.Server)] whose sender comes
// from RpcParams.Receive.SenderClientId, never from an argument; replies to
// one client go through [Rpc(SendTo.SpecifiedInParams)] with
// RpcTarget.Single. ([ServerRpc(RequireOwnership = false)] is obsolete in
// NGO 2.13 - this object is server-owned, so plain [ServerRpc] would reject
// every client.) The transition table is in CLAUDE.md; the pure parts are
// LobbyRules/NavigatorSelector/NameRules.
public class LobbyState : NetworkBehaviour
{
    public const ulong NoNavigator = NavigatorSelector.None;

    // FixedString512Bytes holds 509 UTF-8 bytes - a confirm whose joined Id
    // string couldn't be stored is ignored outright rather than truncated.
    private const int MaxJoinedIdsBytes = 509;

    // The one in-scene instance, for MainMenu/PlayerIdentity to read; null
    // whenever no session has it spawned (pre-connect, after a disconnect).
    public static LobbyState Instance { get; private set; }

    public readonly NetworkVariable<LobbyPhase> Phase =
        new NetworkVariable<LobbyPhase>(LobbyPhase.MainMenu, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<ulong> Navigator =
        new NetworkVariable<ulong>(NoNavigator, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkList<LobbyEntry> Entries = new NetworkList<LobbyEntry>();

    // Raised on the one client the server addressed a reply to (static:
    // MainMenu subscribes once, before any session exists).
    public static event Action<string> NameRejected;
    public static event Action<LobbyPicksKind, string> PicksValidated;

    private readonly List<ulong> idScratch = new List<ulong>();
    private readonly List<string> nameScratch = new List<string>();
    private readonly List<LobbyReadiness> readinessScratch = new List<LobbyReadiness>();

    public override void OnNetworkSpawn()
    {
        Instance = this;
        if (!IsServer) return;

        // Explicit re-seed rather than trusting field defaults: with domain
        // reload off, or on re-host, this same object spawns again with
        // whatever the previous session left.
        Entries.Clear();
        Navigator.Value = NoNavigator;
        Phase.Value = LobbyPhase.MainMenu;

        // Whoever is already connected when this spawns (the host itself -
        // in-scene objects spawn after its own connection is approved but
        // before its OnClientConnected fires) is in from the start.
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds) AddEntry(clientId, admitted: true);

        NetworkManager.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
        if (!IsServer || NetworkManager == null) return;
        NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    public int IndexOf(ulong clientId)
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].ClientId == clientId) return i;
        }
        return -1;
    }

    public bool TryGetEntry(ulong clientId, out LobbyEntry entry)
    {
        int index = IndexOf(clientId);
        entry = index >= 0 ? Entries[index] : default;
        return index >= 0;
    }

    // Idempotent: the host's OnClientConnected fires after the seeding in
    // OnNetworkSpawn already added it.
    private void AddEntry(ulong clientId, bool admitted)
    {
        if (IndexOf(clientId) >= 0) return;
        Entries.Add(new LobbyEntry { ClientId = clientId, Admitted = admitted });
    }

    private void HandleClientConnected(ulong clientId)
    {
        AddEntry(clientId, LobbyRules.IsAdmittedJoin(Phase.Value));
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        int index = IndexOf(clientId);
        if (index >= 0) Entries.RemoveAt(index);

        // Whatever the client had claimed is up for grabs again. After Start
        // the player object's own despawn does this too - releasing twice is
        // a no-op, and before Start there's no player object to do it.
        CharacterEquipment.ReleaseAllClaims(clientId);
        PlayerAbilities.ReleaseAllClaims(clientId);

        switch (Phase.Value)
        {
            case LobbyPhase.Started:
                return;
            case LobbyPhase.MainMenu:
                // The leaver may have been the one holding the team back.
                CheckAllReady();
                return;
        }

        // Choice/Spells/Equipment: only the navigator leaving changes
        // anything (a non-navigator leaving still leaves the navigator, who
        // is admitted, so someone admitted remains).
        NavigatorOutcome outcome = LobbyRules.ResolveNavigatorAfterLeave(
            clientId, Navigator.Value, AdmittedIds(), UnityEngine.Random.value, out ulong nextNavigator);
        if (outcome == NavigatorOutcome.NoneLeft) EnterMainMenu();
        else if (outcome == NavigatorOutcome.Repicked) Navigator.Value = nextNavigator;
    }

    private List<ulong> AdmittedIds()
    {
        idScratch.Clear();
        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Admitted) idScratch.Add(Entries[i].ClientId);
        }
        return idScratch;
    }

    // MainMenu -> Choice once every admitted player is ready. Navigator is
    // written before Phase so a client reacting to the phase change already
    // sees who's driving.
    private void CheckAllReady()
    {
        if (Phase.Value != LobbyPhase.MainMenu) return;

        readinessScratch.Clear();
        for (int i = 0; i < Entries.Count; i++)
        {
            LobbyEntry entry = Entries[i];
            readinessScratch.Add(new LobbyReadiness(entry.ClientId, entry.Admitted, entry.Ready));
        }
        if (!LobbyRules.AllReady(readinessScratch)) return;

        idScratch.Clear();
        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Admitted && Entries[i].Ready) idScratch.Add(Entries[i].ClientId);
        }
        Navigator.Value = NavigatorSelector.Pick(idScratch, UnityEngine.Random.value);
        Phase.Value = LobbyPhase.Choice;
    }

    // Every way back to the main-menu screen (navigator's "Back to main
    // menu", or the last admitted player leaving) admits everyone present -
    // the only way a mid-pick joiner gets in - and resets Ready for all, so
    // the team re-confirms before moving on. Confirmed picks are kept.
    private void EnterMainMenu()
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            LobbyEntry entry = Entries[i];
            entry.Admitted = true;
            entry.Ready = false;
            Entries[i] = entry;
        }
        Navigator.Value = NoNavigator;
        Phase.Value = LobbyPhase.MainMenu;
    }

    private List<string> OtherNames(ulong clientId)
    {
        nameScratch.Clear();
        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].ClientId == clientId || Entries[i].Name.Length == 0) continue;
            nameScratch.Add(Entries[i].Name.ToString());
        }
        return nameScratch;
    }

    // Ready carries the name with it (there's no separate Apply). Honoured
    // while the lobby is on the main-menu screen, and for a waiter in any
    // phase - a waiter's Ready is stored but not counted (LobbyRules.AllReady
    // ignores the non-admitted) and is reset with everyone else's when the
    // team comes back to the main-menu screen.
    [Rpc(SendTo.Server)]
    public void SubmitReadyRpc(string name, bool ready, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        int index = IndexOf(sender);
        if (index < 0) return;

        LobbyEntry entry = Entries[index];
        if (Phase.Value != LobbyPhase.MainMenu && entry.Admitted) return;

        if (!ready)
        {
            entry.Ready = false;
            Entries[index] = entry;
            return;
        }

        string reason = NameRules.Validate(name, OtherNames(sender), out string normalized);
        if (reason != null)
        {
            entry.Ready = false;
            Entries[index] = entry;
            NameRejectedRpc(reason, RpcTarget.Single(sender, RpcTargetUse.Temp));
            return;
        }

        entry.Name = normalized;
        entry.Ready = true;
        Entries[index] = entry;
        CheckAllReady();
    }

    // A client's confirm on leaving a picking screen - the validated,
    // claimed result goes into the entry (so everyone's picks sections see
    // it) and back to the sender (so its Profile drops what it lost).
    // Accepted from an admitted sender in every phase but MainMenu: Choice
    // because a confirm triggered by the navigator yanking everyone back
    // arrives just after the phase already changed, Started because a slow
    // confirm can land after Start and is still that client's own picks.
    [Rpc(SendTo.Server)]
    public void SetPicksRpc(LobbyPicksKind kind, string joinedIds, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        int index = IndexOf(sender);
        if (index < 0) return;

        LobbyEntry entry = Entries[index];
        if (!entry.Admitted) return;
        if (Phase.Value == LobbyPhase.MainMenu) return;

        if (joinedIds == null) joinedIds = "";
        if (Encoding.UTF8.GetByteCount(joinedIds) > MaxJoinedIdsBytes) return;

        string validated;
        if (kind == LobbyPicksKind.Spells)
        {
            validated = PlayerAbilities.JoinIds(PlayerAbilities.ValidateAndClaimLoadout(joinedIds, sender));
            entry.Spells = validated;
        }
        else
        {
            validated = CharacterEquipment.JoinIds(CharacterEquipment.ValidateAndClaim(joinedIds, sender));
            entry.Gear = validated;
        }
        Entries[index] = entry;

        PicksValidatedRpc(kind, validated, RpcTarget.Single(sender, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.Server)]
    public void NavigateRpc(LobbyPhase target, RpcParams rpcParams = default)
    {
        if (!IsNavigator(rpcParams.Receive.SenderClientId)) return;
        if (!LobbyRules.IsLegalNavigation(Phase.Value, target)) return;

        if (target == LobbyPhase.MainMenu) EnterMainMenu();
        else Phase.Value = target;
    }

    // Choice only (a picking screen goes back to Choice first). Flags the
    // session started so later joiners spawn on connect, then spawns a
    // player for every admitted connected client - a waiter present now
    // stays out until they reconnect.
    [Rpc(SendTo.Server)]
    public void StartGameRpc(RpcParams rpcParams = default)
    {
        if (!IsNavigator(rpcParams.Receive.SenderClientId)) return;
        if (Phase.Value != LobbyPhase.Choice) return;

        NetworkBootstrap.MarkGameStarted();
        Navigator.Value = NoNavigator;
        Phase.Value = LobbyPhase.Started;

        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Admitted) SpawnPlayer(Entries[i].ClientId);
        }
    }

    private bool IsNavigator(ulong clientId)
    {
        return Navigator.Value != NoNavigator && clientId == Navigator.Value;
    }

    private void SpawnPlayer(ulong clientId)
    {
        if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) return;
        if (client.PlayerObject != null) return;

        GameObject prefab = NetworkManager.NetworkConfig.PlayerPrefab;
        if (prefab == null)
        {
            Debug.LogError("LobbyState: NetworkManager has no Player Prefab - nobody can be spawned.");
            return;
        }

        // Same prefab, same owner semantics NGO's own auto-spawn would have
        // used at connect time - only the moment differs. The prefab's own
        // position is the spawn point; the owner's terrain snap runs as
        // usual once it lands.
        GameObject instance = Instantiate(prefab);
        instance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void NameRejectedRpc(string reason, RpcParams rpcParams)
    {
        NameRejected?.Invoke(reason);
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void PicksValidatedRpc(LobbyPicksKind kind, string joinedIds, RpcParams rpcParams)
    {
        PicksValidated?.Invoke(kind, joinedIds);
    }
}
