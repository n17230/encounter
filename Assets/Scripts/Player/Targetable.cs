using UnityEngine;

public class Targetable : MonoBehaviour
{
    [SerializeField] private string displayName = "Target";

    public string DisplayName => displayName;
    public CharacterStats Stats { get; private set; }

    private void Awake()
    {
        Stats = GetComponent<CharacterStats>();
    }
}
