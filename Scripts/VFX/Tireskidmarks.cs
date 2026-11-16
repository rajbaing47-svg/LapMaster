using System.Collections.Generic;
using UnityEngine;

// Upgrade for the old TrailRenderer-based tire marks in DriftSmokeEffect.cs.
//
// Instead of a fixed-width TrailRenderer ribbon, this builds a real dynamic mesh per
// wheel at runtime and grows it frame-by-frame at the wheel's ground contact point,
// with vertex-alpha fade at the edges - so marks actually look like tire rubber laid
// on the road instead of a flat decal strip.
//
// TRIGGER: kept identical to your current setup on purpose (handbrake-only, same as
// DriftSmokeEffect's tireMarks[] TrailRenderer logic) - only the VISUALS are upgraded.
// If you ever want marks from braking lockup / wheelspin too, that's a one-line change
// in ShouldMark() below.
//
// Attach this to the same GameObject as `controller` (the car root), same as
// DriftSmokeEffect. Only the REAR wheels (indices 2 and 3) generate marks, matching
// controller.cs's existing handbrake behavior (wheels[3]/wheels[2] are the ones that
// get brakeTorque on handbrake).
[RequireComponent(typeof(controller))]
public class TireSkidMarks : MonoBehaviour
{
    [Header("Required")]
    [Tooltip("Assign the SkidMarks shader asset here. Use SkidMarksURP.shader if your project is on Universal Render Pipeline, or the original SkidMarks.shader if you're on Built-in RP. Marks will not generate (and will render pink/error) if the wrong one is assigned for your pipeline.")]
    public Shader skidMarksShader;

    [Header("Mark Appearance")]
    [Range(0.05f, 1.0f)]
    [Tooltip("Width of the skid mark trail.")]
    public float markWidth = 0.25f;

    [Tooltip("Base color/opacity of the marks. With standard alpha blending, black/dark colors = visible dark rubber marks (what you want), white = faint/invisible marks. Alpha controls overall mark opacity.")]
    public Color markColor = new Color(0.05f, 0.05f, 0.05f, 0.9f);

    [Header("Trigger")]
    [Tooltip("Same minimum speed gate DriftSmokeEffect already uses, so marks and smoke start/stop together.")]
    public float minSpeedForMarks = 5f;

    // ---- internals ----
    private controller car;
    private const int REAR_LEFT = 2;
    private const int REAR_RIGHT = 3;

    private Material skidMaterial;
    private Mesh[] wheelMarkMesh = new Mesh[4];      // only [2] and [3] actually used
    private bool[] generatedLastFrame = new bool[4];
    private Vector3[] lastPoint = new Vector3[4];

    private const int MAX_VERTS = 1200;
    private readonly Dictionary<Mesh, int> currentIndexes = new Dictionary<Mesh, int>();

    // scratch buffers reused every call to avoid per-frame GC allocation
    private List<Vector3> vertices = new List<Vector3>(MAX_VERTS);
    private List<Vector3> normals = new List<Vector3>(MAX_VERTS);
    private List<Color> colors = new List<Color>(MAX_VERTS);
    private List<Vector2> uv = new List<Vector2>(MAX_VERTS);
    private List<int> tris = new List<int>(MAX_VERTS * 3);

    private void Awake()
    {
        car = GetComponent<controller>();
    }

    private void Start()
    {
        if (skidMarksShader == null)
        {
            Debug.LogWarning($"{name}: TireSkidMarks has no shader assigned - skid marks are disabled. Assign SkidMarks.shader in the Inspector.");
            enabled = false;
            return;
        }

        skidMaterial = new Material(skidMarksShader);
        skidMaterial.mainTexture = GenerateTireTreadTexture(true);
        skidMaterial.SetTexture("_NormalMap", GenerateTireTreadTexture(false));
        skidMaterial.SetFloat("_NormFactor", 0.7f);
        skidMaterial.SetFloat("_Glossiness", 0f);
        skidMaterial.SetFloat("_Metallic", 0f);

        wheelMarkMesh[REAR_LEFT] = CreateMarkMeshObject("RL");
        wheelMarkMesh[REAR_RIGHT] = CreateMarkMeshObject("RR");
    }

    private Mesh CreateMarkMeshObject(string label)
    {
        GameObject go = new GameObject($"SkidMesh_{label}_{transform.name}");
        // IMPORTANT: do NOT parent this to the car. Vertices below are written in world
        // space every frame - if this object were parented to the moving/rotating car,
        // Unity would reinterpret those same world-space numbers as LOCAL coordinates
        // under the car's transform, warping/hiding the whole trail as the car moves.
        // Leaving it unparented (world space, identity transform) is what the original
        // system relied on by parenting to a stationary scene root instead of the vehicle.
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;

        Mesh mesh = new Mesh();
        mesh.vertices = new Vector3[MAX_VERTS];
        mesh.normals = new Vector3[MAX_VERTS];
        mesh.uv = new Vector2[MAX_VERTS];
        mesh.colors = new Color[MAX_VERTS];
        mesh.triangles = new int[MAX_VERTS * 3];
        mesh.MarkDynamic();

        var mf = go.AddComponent<MeshFilter>();
        mf.mesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.material = skidMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        return mesh;
    }

    private void LateUpdate()
    {
        if (car == null || skidMaterial == null) return;

        bool shouldMark = ShouldMark();

        UpdateWheelMark(REAR_LEFT, shouldMark);
        UpdateWheelMark(REAR_RIGHT, shouldMark);
    }

    // Same gate as DriftSmokeEffect's shouldDrift: grounded + fast enough + handbrake held.
    private bool ShouldMark()
    {
        return car.grounded && car.KPH > minSpeedForMarks && car.IsHandbraking;
    }

    // DEBUG: flip this on temporarily if marks still don't show, to see exactly which
    // gate is blocking generation each frame. Remove once confirmed working.
    public bool debugLog = false;

    private void UpdateWheelMark(int wheelIndex, bool shouldMark)
    {
        if (!shouldMark)
        {
            if (debugLog) Debug.Log($"TireSkidMarks[{wheelIndex}]: not marking - grounded={car.grounded} KPH={car.KPH} handbrake={car.IsHandbraking}");
            generatedLastFrame[wheelIndex] = false;
            return;
        }

        WheelCollider wc = car.GetWheel(wheelIndex);
        if (wc == null || !wc.isGrounded)
        {
            if (debugLog) Debug.Log($"TireSkidMarks[{wheelIndex}]: wheel null or not grounded (wc.isGrounded={(wc != null ? wc.isGrounded.ToString() : "null")})");
            generatedLastFrame[wheelIndex] = false;
            return;
        }

        Vector3 wheelWorldPos = car.GetWheelWorldPosition(wheelIndex);
        generatedLastFrame[wheelIndex] = GenerateMark(
            wc,
            wheelWorldPos,
            wheelMarkMesh[wheelIndex],
            generatedLastFrame[wheelIndex],
            wheelIndex
        );
    }

    // Ported from MS Vehicle System's GenerateSkidMarks - builds one quad per call,
    // appended into the wheel's dynamic mesh, with alpha derived from slip magnitude.
    private bool GenerateMark(WheelCollider wheelCollider, Vector3 wheelPos, Mesh mesh, bool generatedBefore, int markIndex)
    {
        WheelHit hit;
        if (!wheelCollider.GetGroundHit(out hit) || !wheelCollider.isGrounded)
            return false;

        // Contact point derived from the wheel mesh position (matches the visual wheel,
        // not the WheelCollider's own point) so marks sit exactly under the rendered tire.
        Vector3 contactPoint = wheelPos - wheelCollider.transform.up * wheelCollider.radius;

        float slipAlpha = Mathf.Max(Mathf.Abs(hit.sidewaysSlip), Mathf.Abs(hit.forwardSlip));
        float alpha = Mathf.Clamp01(slipAlpha);
        if (alpha < 0.05f)
        {
            if (debugLog) Debug.Log($"TireSkidMarks[{markIndex}]: grounded+handbraking but slip too low (sideways={hit.sidewaysSlip:F3} forward={hit.forwardSlip:F3})");
            return false; // slip too small to bother drawing this frame
        }

        Vector3 skidOffset = hit.sidewaysDir * (markWidth / 2f)
            * Vector3.Dot(wheelCollider.attachedRigidbody.velocity.normalized, hit.forwardDir);

        float distanceSq = (lastPoint[markIndex] - contactPoint - skidOffset).sqrMagnitude;
        if (generatedBefore && distanceSq < 0.01f)
            return true; // hasn't moved far enough yet, keep the last quad as-is

        mesh.GetVertices(vertices);
        mesh.GetNormals(normals);
        mesh.GetTriangles(tris, 0);
        mesh.GetColors(colors);
        mesh.GetUVs(0, uv);

        int vertLength = GetNextVertexIndex(mesh);
        int triLength = vertLength * 3;

        vertices[Wrap(vertices, vertLength - 1)] = contactPoint + hit.normal * 0.02f - skidOffset;
        vertices[Wrap(vertices, vertLength - 2)] = contactPoint + hit.normal * 0.02f + skidOffset;
        normals[Wrap(normals, vertLength - 1)] = normals[Wrap(normals, vertLength - 2)] = hit.normal;

        Color c = markColor;
        c.a = Mathf.Clamp(markColor.a * alpha, 0.01f, 1f);
        colors[Wrap(colors, vertLength - 1)] = colors[Wrap(colors, vertLength - 2)] = c;

        // stitch the new quad's 12 triangle-index slots
        tris[Wrap(tris, triLength + 0)] = tris[Wrap(tris, triLength + 3)] =
            tris[Wrap(tris, triLength + 1)] = tris[Wrap(tris, triLength + 4)] =
                tris[Wrap(tris, triLength + 2)] = tris[Wrap(tris, triLength + 5)] =
                    tris[Wrap(tris, triLength + 6)] = tris[Wrap(tris, triLength + 9)] =
                        tris[Wrap(tris, triLength + 7)] = tris[Wrap(tris, triLength + 10)] =
                            tris[Wrap(tris, triLength + 8)] = tris[Wrap(tris, triLength + 11)];

        if (generatedBefore)
        {
            tris[Wrap(tris, triLength - 1)] = Wrap(vertices, vertLength - 2);
            tris[Wrap(tris, triLength - 2)] = Wrap(vertices, vertLength - 1);
            tris[Wrap(tris, triLength - 3)] = Wrap(vertices, vertLength - 3);
            tris[Wrap(tris, triLength - 4)] = Wrap(vertices, vertLength - 3);
            tris[Wrap(tris, triLength - 5)] = Wrap(vertices, vertLength - 4);
            tris[Wrap(tris, triLength - 6)] = Wrap(vertices, vertLength - 2);

            uv[Wrap(uv, vertLength - 1)] = uv[Wrap(uv, vertLength - 3)] + Vector2.right * distanceSq * 0.01f;
            uv[Wrap(uv, vertLength - 2)] = uv[Wrap(uv, vertLength - 4)] + Vector2.right * distanceSq * 0.01f;
        }
        else
        {
            uv[Wrap(uv, vertLength - 1)] = Vector2.zero;
            uv[Wrap(uv, vertLength - 2)] = Vector2.up;
        }

        lastPoint[markIndex] = vertices[Wrap(vertices, vertLength - 1)];

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uv);
        mesh.RecalculateBounds();

        return true;
    }

    private int GetNextVertexIndex(Mesh mesh)
    {
        int result;
        currentIndexes.TryGetValue(mesh, out result);
        result += 2;
        result %= mesh.vertexCount;
        result += mesh.vertexCount;
        currentIndexes[mesh] = result;
        return result;
    }

    private static int Wrap<T>(List<T> list, int index)
    {
        return (index + list.Count) % list.Count;
    }

    // Procedural tire-tread-pattern texture, so no external texture asset is required.
    // isAlbedo=true -> RGB carries dark tread grooves against a mid-gray rubber base
    // (this is what actually darkens the road, since the shader multiplies this by
    // vertex color). isAlbedo=false -> a basic flat normal map (tangent-space up),
    // just enough to keep the shader's normal map slot non-null.
    private Texture GenerateTireTreadTexture(bool isAlbedo)
    {
        Texture2D tex = new Texture2D(32, 32, TextureFormat.ARGB32, false);

        if (isAlbedo)
        {
            Color rubber = new Color(0.35f, 0.35f, 0.35f, 1f);   // base tire rubber tone
            Color groove = new Color(0.08f, 0.08f, 0.08f, 1f);   // dark tread groove
            Color edgeFade = new Color(0.35f, 0.35f, 0.35f, 0f); // fully transparent at trail edges

            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    Color c = rubber;

                    // tread groove bands running across the mark
                    if (y % 8 == 0 || y % 8 == 1) c = groove;

                    // fade alpha out near the top/bottom edge of the trail strip
                    if (y <= 1 || y >= 30)
                    {
                        float edgeT = y <= 1 ? y / 2f : (31 - y) / 2f;
                        c.a = Mathf.Lerp(0f, 1f, edgeT);
                    }

                    tex.SetPixel(x, y, c);
                }
            }
        }
        else
        {
            Color flatNormal = new Color(0.5f, 0.5f, 1f, 1f); // tangent-space "pointing straight up"
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    tex.SetPixel(x, y, flatNormal);
        }

        tex.Apply();
        return tex;
    }
}