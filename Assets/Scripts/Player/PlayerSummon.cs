using Unity.Netcode;
using UnityEngine;

// Testing-lobby feature only: lets players spawn extra mobs to fight. Not
// part of the real game (real content spawns mobs via encounter design,
// not player choice).
public class PlayerSummon : NetworkBehaviour
{
    [SerializeField] private GameObject[] summonableMobs;
    [SerializeField] private float mapHalfExtent = 15f;
    [SerializeField] private int maxSummonCount = 20;

    private bool panelOpen;
    private int selectedMobIndex;
    private string countInput = "1";

    private void OnGUI()
    {
        if (!IsOwner) return;
        if (summonableMobs == null || summonableMobs.Length == 0) return;

        DevGui.Begin();
        float x = UIScale.Width - 210;

        if (GUI.Button(new Rect(x, 10, 200, 20), panelOpen ? "Close Summon" : "Summon Mob"))
        {
            panelOpen = !panelOpen;
        }

        if (!panelOpen) return;

        GUILayout.BeginArea(new Rect(x, 35, 200, 40 + summonableMobs.Length * 22 + 60));
        GUILayout.Label("Mob:");
        for (int i = 0; i < summonableMobs.Length; i++)
        {
            Targetable targetable = summonableMobs[i].GetComponent<Targetable>();
            string mobLabel = targetable != null ? targetable.DisplayName : summonableMobs[i].name;
            string label = (i == selectedMobIndex ? "> " : "") + mobLabel;
            if (GUILayout.Button(label))
            {
                selectedMobIndex = i;
            }
        }

        GUILayout.Label("Count:");
        countInput = GUILayout.TextField(countInput);

        if (GUILayout.Button("Summon") && int.TryParse(countInput, out int count))
        {
            RequestSummonServerRpc(selectedMobIndex, count);
        }
        GUILayout.EndArea();
    }

    [ServerRpc]
    private void RequestSummonServerRpc(int mobIndex, int count)
    {
        if (mobIndex < 0 || mobIndex >= summonableMobs.Length) return;

        count = Mathf.Clamp(count, 1, maxSummonCount);
        GameObject prefab = summonableMobs[mobIndex];

        for (int i = 0; i < count; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float x = Mathf.Cos(angle) * mapHalfExtent;
            float z = Mathf.Sin(angle) * mapHalfExtent;
            Vector3 spawnPosition = new Vector3(x, GetSpawnHeight(x, z), z);

            GameObject instance = Instantiate(prefab, spawnPosition, Quaternion.identity);
            instance.GetComponent<NetworkObject>().Spawn();
        }
    }

    // Terrain height varies across the map (and keeps changing as it's
    // sculpted), so mobs spawn a safe margin above the actual terrain
    // surface at their landing spot rather than a hardcoded Y - each mob's
    // own EnemyAI gravity handling settles it onto the ground correctly
    // from there regardless of its own height/scale.
    private float GetSpawnHeight(float x, float z)
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null) return 1f;

        return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y + 15f;
    }
}
