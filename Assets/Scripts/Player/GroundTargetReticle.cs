using UnityEngine;

// Local-only visual ring shown while aiming a ground-targeted ability (see
// PlayerAbilities). Never networked - purely a per-owner cursor. Same
// procedural-ring-mesh technique as TargetRingIndicator.
public class GroundTargetReticle
{
    private readonly GameObject go;

    public GroundTargetReticle(float radius, Color color)
    {
        go = new GameObject("GroundTargetReticle");
        MeshFilter filter = go.AddComponent<MeshFilter>();
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        filter.mesh = BuildRingMesh(Mathf.Max(0f, radius - 0.4f), radius, 40);

        Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.color = color;
        material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        renderer.material = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        go.SetActive(false);
    }

    public void SetVisible(bool visible) => go.SetActive(visible);
    public void SetPosition(Vector3 worldPoint) => go.transform.position = worldPoint + Vector3.up * 0.05f;
    public void Destroy() => Object.Destroy(go);

    private static Mesh BuildRingMesh(float innerR, float outerR, int segmentCount)
    {
        Mesh mesh = new Mesh { name = "GroundTargetReticleMesh" };

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
