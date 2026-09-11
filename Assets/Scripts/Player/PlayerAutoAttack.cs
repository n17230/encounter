using Unity.Netcode;
using UnityEngine;

// MMO-style auto-attack: right-clicking a mob targets it and arms the
// attack; from then on the server swings the equipped main-hand weapon
// (or Fists) every SwingInterval whenever the target is in range and in
// front of the player. It stays armed while closing distance and follows
// target changes (Tab), and disarms when the target is dropped or dies.
[RequireComponent(typeof(PlayerTargeting))]
[RequireComponent(typeof(CharacterStats))]
[RequireComponent(typeof(CharacterEquipment))]
public class PlayerAutoAttack : NetworkBehaviour
{
    [SerializeField] private WeaponData unarmedWeapon;
    [SerializeField] private float facingConeAngle = 120f;

    private PlayerTargeting targeting;
    private CharacterEquipment equipment;

    // Owner-side state, mirrored to the server via SetAutoAttackServerRpc.
    public bool IsArmed { get; private set; }
    private Targetable armedFor;

    private bool serverArmed;
    private ulong serverTargetId;
    private float nextSwingTime;

    private void Awake()
    {
        targeting = GetComponent<PlayerTargeting>();
        equipment = GetComponent<CharacterEquipment>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner) targeting.AttackRequested += Arm;
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner) targeting.AttackRequested -= Arm;
    }

    // What the local player would swing with - for the HUD.
    public WeaponData LocalWeapon
    {
        get
        {
            ItemData mainHand = ProfileStore.Current.GetGear(GearSlot.MainHand);
            return mainHand != null && mainHand.Weapon != null ? mainHand.Weapon : unarmedWeapon;
        }
    }

    private void Arm(Targetable target)
    {
        IsArmed = true;
        armedFor = target;
        SendState();
    }

    private void Update()
    {
        if (!IsOwner || !IsArmed) return;

        Targetable target = targeting.CurrentTarget;
        if (target == null || target.Stats == null || target.Stats.CurrentHealth.Value <= 0f)
        {
            Disarm();
            return;
        }

        if (target != armedFor)
        {
            armedFor = target;
            SendState();
        }
    }

    private void Disarm()
    {
        IsArmed = false;
        armedFor = null;
        SetAutoAttackServerRpc(false, 0);
    }

    private void SendState()
    {
        NetworkObject targetObject = armedFor != null ? armedFor.GetComponent<NetworkObject>() : null;
        if (targetObject == null)
        {
            Disarm();
            return;
        }
        SetAutoAttackServerRpc(true, targetObject.NetworkObjectId);
    }

    [ServerRpc]
    private void SetAutoAttackServerRpc(bool armed, ulong targetNetworkObjectId)
    {
        serverArmed = armed;
        serverTargetId = targetNetworkObjectId;
    }

    private void FixedUpdate()
    {
        if (!IsServer || !serverArmed) return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(serverTargetId, out NetworkObject targetObject))
        {
            serverArmed = false;
            return;
        }

        Targetable target = targetObject.GetComponent<Targetable>();
        if (target == null || target.Stats == null || target.Stats.CurrentHealth.Value <= 0f)
        {
            serverArmed = false;
            return;
        }

        WeaponData weapon = equipment.MainHandWeapon != null ? equipment.MainHandWeapon : unarmedWeapon;
        if (weapon == null) return;
        if (Time.time < nextSwingTime) return;

        Vector3 toTarget = targetObject.transform.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.magnitude > WeaponData.BasicAttackRange) return; // armed, waiting to get in reach
        if (!FacingCone.IsWithin(transform, targetObject.transform.position, facingConeAngle)) return;

        nextSwingTime = Time.time + weapon.SwingInterval;
        target.Stats.ReceiveHit(new HitInfo
        {
            Damage = weapon.Damage,
            AttackerClientId = OwnerClientId,
            Effect = weapon.Effect,
        });
    }
}
