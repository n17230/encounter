using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerTargeting))]
public class TargetRingIndicator : NetworkBehaviour
{
    [SerializeField] private float innerRadius = 0.5f;
    [SerializeField] private float outerRadius = 0.95f;
    [SerializeField] private int segments = 32;
    [SerializeField] private Color ringColor = Color.yellow;

    private PlayerTargeting targeting;
    private GameObject ringObject;

    private void Awake()
    {
        targeting = GetComponent<PlayerTargeting>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;
        ringObject = CreateRing();
        ringObject.SetActive(false);
    }

    public override void OnNetworkDespawn()
    {
        if (ringObject != null) Destroy(ringObject);
    }

    private void LateUpdate()
    {
        if (!IsOwner || ringObject == null) return;

        Targetable target = targeting.CurrentTarget;
        if (target == null)
        {
            ringObject.SetActive(false);
            return;
        }

        ringObject.SetActive(true);
        Vector3 position = target.transform.position;
        ringObject.transform.position = new Vector3(position.x, 0.05f, position.z);
    }

    private GameObject CreateRing()
    {
        GameObject go = new GameObject("TargetRing");
        MeshFilter filter = go.AddComponent<MeshFilter>();
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();

        filter.mesh = BuildRingMesh(innerRadius, outerRadius, segments);

        Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.color = ringColor;
        material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        renderer.material = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return go;
    }

    private static Mesh BuildRingMesh(float innerR, float outerR, int segmentCount)
    {
        Mesh mesh = new Mesh { name = "RingMesh" };

        Vector3[] vertices = new Vector3[(segmentCount + 1) * 2];
        int[] triangles = new int[segmentCount * 6];

        for (int i = 0; i <= segmentCount; i++)
        {
            float angle = (i / (float)segmentCount) * Mathf.PI * 2f;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            vertices[i * 2] = new Vector3(cos * innerR, 0f, sin * innerR);
            vertices[i * 2 + 1] = new Vector3(cos * outerR, 0f, sin * outerR);
        }

        for (int i = 0; i < segmentCount; i++)
        {
            int baseIndex = i * 2;
            int triIndex = i * 6;

            triangles[triIndex] = baseIndex;
            triangles[triIndex + 1] = baseIndex + 1;
            triangles[triIndex + 2] = baseIndex + 2;

            triangles[triIndex + 3] = baseIndex + 1;
            triangles[triIndex + 4] = baseIndex + 3;
            triangles[triIndex + 5] = baseIndex + 2;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }
}
