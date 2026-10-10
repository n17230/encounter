using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// A persistent, server-spawned wall made of 1-unit-wide segments that each
// follow the terrain under them (Summon Wall). One NetworkObject per cast;
// every peer builds the colliders locally from WallSpec, so nothing but the
// spec crosses the network. Unlike GroundPatch (a trigger hazard) the
// segments are real obstacles. No lifetime of its own - it only despawns
// when PlayerAbilities replaces or removes it.
public class SegmentedWall : NetworkBehaviour
{
    public const float SegmentWidth = 1f;
    public const float BelowGroundDepth = 1f;
    // A segment whose ground is further than this from the aimed point's
    // ground is left out - a cliff the wall shouldn't climb.
    public const float MaxGroundDelta = 4f;

    // Everything a peer needs to rebuild the same wall. skipMask is decided
    // once on the server so peers can never disagree about which segments
    // exist (terrain samples could differ by a float at a delta of exactly
    // MaxGroundDelta).
    public struct WallSpec : INetworkSerializable, IEquatable<WallSpec>
    {
        public int count;
        public float segmentWidth;
        public float height;
        public float thickness;
        public float depth;
        public uint skipMask;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref count);
            serializer.SerializeValue(ref segmentWidth);
            serializer.SerializeValue(ref height);
            serializer.SerializeValue(ref thickness);
            serializer.SerializeValue(ref depth);
            serializer.SerializeValue(ref skipMask);
        }

        public bool Equals(WallSpec other) =>
            count == other.count && segmentWidth == other.segmentWidth && height == other.height &&
            thickness == other.thickness && depth == other.depth && skipMask == other.skipMask;
    }

    private readonly NetworkVariable<WallSpec> spec = new NetworkVariable<WallSpec>();

    // Set by Configure before Spawn(); written into the NetworkVariable in
    // OnNetworkSpawn, since a NetworkVariable can't be written before its
    // behaviour is initialised.
    private WallSpec pendingSpec;
    private bool built;

    // Ground height under (x, z); with no terrain loaded, the fallback.
    // Shared by the server (skip mask, anchor) and every peer's build so
    // they sample identically.
    public static float GroundY(float x, float z, float fallbackY)
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null) return fallbackY;
        return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
    }

    // Server only, before Spawn(): which segments exist for a wall centred
    // on aimedPoint across `right`. anchorY is the terrain height at the
    // aimed X/Z (not the raycast hit's Y, which can sit on a roof or prop).
    public static WallSpec BuildSpec(Vector3 aimedPoint, Vector3 right, float totalWidth, float height, float thickness, out float anchorY)
    {
        anchorY = GroundY(aimedPoint.x, aimedPoint.z, aimedPoint.y);

        int count = WallSegmentLayout.SegmentCount(totalWidth, SegmentWidth);
        uint skipMask = FillHeightsAndMask(aimedPoint, right, anchorY, new float[count]);

        return new WallSpec
        {
            count = count,
            segmentWidth = SegmentWidth,
            height = height,
            thickness = thickness,
            depth = BelowGroundDepth,
            skipMask = skipMask,
        };
    }

    // Fills `heights` with the ground height under each segment of a wall
    // centred on aimedPoint across `right`, and returns the skip mask. The
    // segment count IS heights.Length (so a longer buffer can't leak stale
    // bits into the mask) - callers pass a buffer exactly as long as the
    // wall's count. The single math path for the server's spawn and the aim
    // preview, so the preview shows exactly the segments the wall gets.
    public static uint FillHeightsAndMask(Vector3 aimedPoint, Vector3 right, float anchorY, float[] heights)
    {
        int count = heights.Length;
        Vector2 centre = new Vector2(aimedPoint.x, aimedPoint.z);
        for (int i = 0; i < count; i++)
        {
            Vector2 xz = WallSegmentLayout.SegmentWorldXZ(centre, right, WallSegmentLayout.Offset(i, count, SegmentWidth));
            heights[i] = GroundY(xz.x, xz.y, anchorY);
        }
        return WallSegmentLayout.ComputeSkipMask(heights, anchorY, MaxGroundDelta);
    }

    public void Configure(WallSpec wallSpec)
    {
        pendingSpec = wallSpec;
    }

    public override void OnNetworkSpawn()
    {
        // Children inherit the root's scale; the prefab's authored scale
        // would otherwise resize every segment.
        transform.localScale = Vector3.one;

        // A client whose spawn ran with the default (empty) spec still
        // builds once the real value arrives.
        spec.OnValueChanged += OnSpecChanged;

        if (IsServer) spec.Value = pendingSpec;
        Build(spec.Value);
    }

    public override void OnNetworkDespawn()
    {
        spec.OnValueChanged -= OnSpecChanged;
    }

    private void OnSpecChanged(WallSpec previous, WallSpec current)
    {
        Build(current);
    }

    private void Build(WallSpec wallSpec)
    {
        // An empty spec must never consume the one build.
        if (built || wallSpec.count <= 0) return;
        built = true;

        Vector3 right = transform.right;
        Vector3 origin = transform.position;
        Vector2 centre = new Vector2(origin.x, origin.z);

        for (int i = 0; i < wallSpec.count; i++)
        {
            if (WallSegmentLayout.IsSkipped(wallSpec.skipMask, i)) continue;

            Vector2 xz = WallSegmentLayout.SegmentWorldXZ(centre, right, WallSegmentLayout.Offset(i, wallSpec.count, wallSpec.segmentWidth));
            float groundY = GroundY(xz.x, xz.y, origin.y);

            GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            segment.name = "WallSegment" + i;
            segment.transform.SetParent(transform, worldPositionStays: false);
            segment.transform.SetPositionAndRotation(
                new Vector3(xz.x, WallSegmentLayout.BoxCenterY(groundY, wallSpec.height, wallSpec.depth), xz.y),
                transform.rotation);
            segment.transform.localScale = new Vector3(
                wallSpec.segmentWidth, WallSegmentLayout.BoxHeight(wallSpec.height, wallSpec.depth), wallSpec.thickness);

            // Carve the NavMesh so mobs path around the wall rather than
            // through it. Server only - it's the one peer that ever queries
            // the mesh (EnemyAI is fully server-driven). A unit box in the
            // cube's own local space, so the segment's scale makes it the
            // segment's exact extent. carveOnlyStationary is off: the
            // default waits ~0.5s of stillness before cutting the hole, and
            // a chasing mob presses into the wall in that window; the
            // segment never moves, so nothing is lost by not waiting. The
            // hole goes away with the segment on despawn.
            if (IsServer)
            {
                NavMeshObstacle obstacle = segment.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = Vector3.zero;
                obstacle.size = Vector3.one;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = false;
            }
        }
    }
}
