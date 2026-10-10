using System;
using Unity.Collections;
using Unity.Netcode;

// One connected client's lobby record, replicated to everyone through
// LobbyState.Entries (same NetworkList shape as CharacterStats.ActiveEffects).
// Server-written only; clients read it for the player lists and the picks
// sections. Name is FixedString64Bytes rather than 32 because the limit is
// bytes, not characters - 16 characters of 4-byte UTF-8 need 64 (see
// NameRules). Spells/Gear are the ';'-joined Ids the server actually
// validated and claimed at the last confirm - the same joined format the
// spawn-time loadout/equipment RPCs send.
public struct LobbyEntry : INetworkSerializable, IEquatable<LobbyEntry>
{
    public ulong ClientId;
    public FixedString64Bytes Name;
    public bool Ready;
    public bool Admitted;
    public FixedString512Bytes Spells;
    public FixedString512Bytes Gear;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Name);
        serializer.SerializeValue(ref Ready);
        serializer.SerializeValue(ref Admitted);
        serializer.SerializeValue(ref Spells);
        serializer.SerializeValue(ref Gear);
    }

    public bool Equals(LobbyEntry other) =>
        ClientId == other.ClientId
        && Name.Equals(other.Name)
        && Ready == other.Ready
        && Admitted == other.Admitted
        && Spells.Equals(other.Spells)
        && Gear.Equals(other.Gear);
}
