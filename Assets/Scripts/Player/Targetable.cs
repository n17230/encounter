using System.Collections.Generic;
using UnityEngine;

public class Targetable : MonoBehaviour
{
    [SerializeField] private string displayName = "Target";

    public string DisplayName => displayName;
    public CharacterStats Stats { get; private set; }

    // Every currently-active Targetable (players and mobs) - see Registry.
    public static IReadOnlyList<Targetable> All => Registry<Targetable>.All;

    private void Awake()
    {
        Stats = GetComponent<CharacterStats>();
    }

    private void OnEnable() => Registry<Targetable>.Add(this);
    private void OnDisable() => Registry<Targetable>.Remove(this);
}
