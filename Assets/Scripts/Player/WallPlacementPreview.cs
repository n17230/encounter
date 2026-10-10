using UnityEngine;

// Local-only aim indicator for a persistent structure (Summon Wall): one
// flat strip per segment the server would actually keep, each lying on its
// own ground height, so it shows exactly where the wall will go - including
// the gaps a cliff leaves. Never networked - purely a per-owner cursor, like
// GroundTargetReticle. Uses the same layout math as the server's spawn
// (SegmentedWall.FillHeightsAndMask / WallSegmentLayout), not a copy of it.
public class WallPlacementPreview
{
    // Same lift as GroundTargetReticle, so the strips don't z-fight terrain.
    private const float Lift = 0.05f;

    private readonly GameObject go;
    private readonly Mesh mesh;
    private readonly Material material;

    private readonly int count;
    private readonly float thickness;
    private readonly float[] heights;
    private readonly Vector3[] vertices;

    private bool hasLast;
    private Vector3 lastPoint;
    private Quaternion lastRotation;

    public WallPlacementPreview(float totalWidth, float thickness, Color color)
    {
        this.thickness = thickness;
        count = WallSegmentLayout.SegmentCount(totalWidth, SegmentedWall.SegmentWidth);
        heights = new float[count];
        vertices = new Vector3[count * 4];

        // Fixed topology: only vertex positions change per update. A skipped
        // segment's four vertices collapse to a point rather than being
        // removed.
        int[] triangles = new int[count * 6];
        for (int i = 0; i < count; i++)
        {
            int v = i * 4;
            int t = i * 6;
            triangles[t] = v;
            triangles[t + 1] = v + 1;
            triangles[t + 2] = v + 2;
            triangles[t + 3] = v;
            triangles[t + 4] = v + 2;
            triangles[t + 5] = v + 3;
        }

        mesh = new Mesh { name = "WallPlacementPreviewMesh" };
        mesh.MarkDynamic();
        mesh.vertices = vertices;
        mesh.triangles = triangles;

        go = new GameObject("WallPlacementPreview");
        MeshFilter filter = go.AddComponent<MeshFilter>();
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        filter.sharedMesh = mesh;

        material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.color = color;
        material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        go.SetActive(false);
    }

    public void SetVisible(bool visible) => go.SetActive(visible);

    // aimedPoint is the raw ground hit; rotation is the wall's flat
    // orientation (WallSegmentLayout.FacingRotation). Skips the terrain
    // sampling and mesh upload when neither changed since the last call.
    public void Update(Vector3 aimedPoint, Quaternion rotation)
    {
        if (hasLast && aimedPoint == lastPoint && rotation == lastRotation) return;
        hasLast = true;
        lastPoint = aimedPoint;
        lastRotation = rotation;

        Vector3 right = rotation * Vector3.right;
        Vector3 forward = rotation * Vector3.forward;

        // Anchor on the terrain under the aimed X/Z, exactly as the server
        // does (the raycast hit can be a roof or prop).
        float anchorY = SegmentedWall.GroundY(aimedPoint.x, aimedPoint.z, aimedPoint.y);
        uint skipMask = SegmentedWall.FillHeightsAndMask(aimedPoint, right, anchorY, heights);

        Vector2 centre = new Vector2(aimedPoint.x, aimedPoint.z);
        for (int i = 0; i < count; i++)
        {
            Vector2 xz = WallSegmentLayout.SegmentWorldXZ(centre, right, WallSegmentLayout.Offset(i, count, SegmentedWall.SegmentWidth));
            float y = heights[i] + Lift;

            if (WallSegmentLayout.IsSkipped(skipMask, i))
            {
                Vector3 point = new Vector3(xz.x, y, xz.y);
                int v = i * 4;
                vertices[v] = point;
                vertices[v + 1] = point;
                vertices[v + 2] = point;
                vertices[v + 3] = point;
            }
            else
            {
                WallSegmentLayout.WriteStripCorners(xz, right, forward, SegmentedWall.SegmentWidth, thickness, y, vertices, i * 4);
            }
        }

        mesh.SetVertices(vertices);
        mesh.RecalculateBounds();
    }

    // Destroys the mesh and material as well as the GameObject - none of
    // them is freed by destroying the GameObject alone.
    public void Destroy()
    {
        Object.Destroy(go);
        Object.Destroy(mesh);
        Object.Destroy(material);
    }
}
