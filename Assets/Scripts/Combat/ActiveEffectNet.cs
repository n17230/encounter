using System;
using Unity.Collections;
using Unity.Netcode;

// Client-visible mirror of one active status effect: enough for UI to
// show a name and a countdown. Expiry is in server time so every client
// computes the same remaining duration.
public struct ActiveEffectNet : INetworkSerializable, IEquatable<ActiveEffectNet>
{
    public FixedString32Bytes EffectId;
    public double ExpireServerTime;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref EffectId);
        serializer.SerializeValue(ref ExpireServerTime);
    }

    public bool Equals(ActiveEffectNet other) =>
        EffectId.Equals(other.EffectId) && ExpireServerTime.Equals(other.ExpireServerTime);
}
