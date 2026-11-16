using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Trackside spectator spawner. Drop this on an empty GameObject in a track scene,
// assign character prefabs and spawn-zone Transforms (empty objects placed along
// the barrier, outside the racing-line clearance), then use Spawn Crowd in the
// Inspector or the component context menu. Does not touch race/car/track logic.
//
// Character prefabs for Downtown Speedway live under:
//   Assets/Assets/Assets/Assets/TrackDesign/Character Prefabs/DenysAlmaral/CityPeople/Prefabs/
// Use city, downtown, elder, little_kids, professions. Skip PROPS and tools.
public class CrowdSpawner : MonoBehaviour
{
    public const string ContainerName = "CrowdContainer";
    public const string SpawnedPrefix = "Crowd_";

    [Header("Spawn Zones")]
    [Tooltip("Parent whose children are spawn-zone empties along the track edge. Spawn Crowd places one person at each child.")]
    public Transform spawnZonesParent;

    [Tooltip("Optional extra spawn-zone Transforms. Combined with children of Spawn Zones Parent.")]
    public List<Transform> spawnZones = new List<Transform>();

    [Header("Character Prefabs")]
    [Tooltip("Drag in people prefabs (city, downtown, elder, little_kids, professions). PROPS and tools should not be included.")]
    public List<GameObject> characterPrefabs = new List<GameObject>();

    [Header("Placement Variety")]
    [Tooltip("Horizontal jitter around each spawn zone so people are not grid-aligned.")]
    public float positionJitter = 1f;
    [Tooltip("Uniform scale range applied to each spawned character.")]
    public Vector2 scaleRange = new Vector2(0.95f, 1.05f);
    [Tooltip("Random Y rotation in degrees (0-360 uses the full circle). Used when Face Toward Track is off, or when no racing line is available.")]
    public float randomYRotationMax = 360f;
    [Tooltip("When enabled, people yaw toward the closest point on the racing line / spline instead of using a random Y rotation.")]
    public bool faceTowardTrack = true;

    [Header("Track Clearance")]
    [Tooltip("Optional Unity Spline Container for the racing line. Used first when facing the track.")]
    public SplineContainer racingLineSpline;
    [Tooltip("Optional racing-line / centerline waypoints. If assigned, spawned people are pushed so they stay at least this far from the line.")]
    public Transform[] racingLineWaypoints;
    [Tooltip("Minimum distance from the racing line / track centerline. Downtown Speedway barrier gap is 10 units.")]
    public float minClearanceFromRacingLine = 10f;

    [Header("Paint Brush")]
    [Tooltip("Scene-view paint radius, in world units.")]
    public float brushRadius = 8f;
    [Tooltip("How many people to try to place with each paint click.")]
    public int peoplePerClick = 10;
    [Tooltip("Layers treated as ground/track for the paint brush raycast and height snapping.")]
    public LayerMask paintSurfaceLayers = ~0;
    [Tooltip("Minimum distance between people spawned in the same paint click.")]
    public float minSpacingBetweenPeople = 1.5f;

    [Header("Runtime")]
    [Tooltip("If true, crowd is spawned on Start when the container is empty. Leave off if you bake crowd in the Editor.")]
    public bool spawnOnStart;

    [SerializeField]
    [HideInInspector]
    private Transform crowdContainer;

    [SerializeField]
    [HideInInspector]
    private List<GameObject> spawnedInstances = new List<GameObject>();

    public Transform CrowdContainer => crowdContainer;
    public List<GameObject> SpawnedInstances => spawnedInstances;

    private void Start()
    {
        if (!spawnOnStart)
            return;

        if (crowdContainer != null && crowdContainer.childCount > 0)
            return;

        SpawnCrowd();
    }

    [ContextMenu("Spawn Crowd")]
    public void SpawnCrowd()
    {
        if (characterPrefabs == null || CountValidPrefabs() == 0)
        {
            Debug.LogWarning("CrowdSpawner: no character prefabs assigned. Drag people prefabs into Character Prefabs.");
            return;
        }

        List<Transform> zones = CollectSpawnZones();
        if (zones.Count == 0)
        {
            Debug.LogWarning("CrowdSpawner: no spawn zones assigned. Place empty GameObjects under Spawn Zones Parent (or drag them into Spawn Zones).");
            return;
        }

        ClearCrowd();
        EnsureContainer();

        for (int i = 0; i < zones.Count; i++)
        {
            Transform zone = zones[i];
            if (zone == null)
                continue;

            GameObject prefab = PickRandomPrefab();
            if (prefab == null)
                continue;

            Vector3 position = GetSpawnPosition(zone.position);
            Quaternion rotation = faceTowardTrack
                ? GetFacingRotation(position)
                : GetRandomYRotation();
            float scale = UnityEngine.Random.Range(scaleRange.x, scaleRange.y);

            GameObject instance = InstantiateCrowdMember(prefab, position, rotation);
            if (instance == null)
                continue;

            instance.name = SpawnedPrefix + prefab.name;
            instance.transform.SetParent(crowdContainer, true);
            instance.transform.localScale = prefab.transform.localScale * scale;

            TryPlayRandomIdle(instance);
            spawnedInstances.Add(instance);
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(this);
#endif
    }

    [ContextMenu("Clear Crowd")]
    public void ClearCrowd()
    {
        for (int i = spawnedInstances.Count - 1; i >= 0; i--)
        {
            GameObject instance = spawnedInstances[i];
            if (instance != null)
                DestroySpawned(instance);
        }

        spawnedInstances.Clear();

        if (crowdContainer != null)
        {
            for (int i = crowdContainer.childCount - 1; i >= 0; i--)
            {
                Transform child = crowdContainer.GetChild(i);
                if (child != null)
                    DestroySpawned(child.gameObject);
            }
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(this);
#endif
    }

    public void EnsureContainer()
    {
        if (crowdContainer != null)
            return;

        Transform existing = transform.Find(ContainerName);
        if (existing != null)
        {
            crowdContainer = existing;
            return;
        }

        GameObject containerObject = new GameObject(ContainerName);
        crowdContainer = containerObject.transform;
        crowdContainer.SetParent(transform, false);
        crowdContainer.localPosition = Vector3.zero;
        crowdContainer.localRotation = Quaternion.identity;
        crowdContainer.localScale = Vector3.one;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Undo.RegisterCreatedObjectUndo(containerObject, "Create Crowd Container");
            EditorUtility.SetDirty(this);
        }
#endif
    }

    public GameObject InstantiateCrowdMember(GameObject prefab, Vector3 position, Quaternion rotation, string undoName = "Spawn Crowd")
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            GameObject editorInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (editorInstance == null)
                editorInstance = Instantiate(prefab);

            editorInstance.transform.SetPositionAndRotation(position, rotation);
            Undo.RegisterCreatedObjectUndo(editorInstance, undoName);
            return editorInstance;
        }
#endif
        return Instantiate(prefab, position, rotation);
    }

    public void DestroySpawnedInstance(GameObject instance)
    {
        if (instance == null)
            return;

        spawnedInstances.Remove(instance);
        DestroySpawned(instance);

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(this);
#endif
    }

    public void RegisterSpawnedInstance(GameObject instance)
    {
        if (instance == null)
            return;
        if (!spawnedInstances.Contains(instance))
            spawnedInstances.Add(instance);

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(this);
#endif
    }

    private static void DestroySpawned(GameObject instance)
    {
        if (instance == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Undo.DestroyObjectImmediate(instance);
            return;
        }
#endif
        Destroy(instance);
    }

    public GameObject PickRandomPrefab()
    {
        int valid = CountValidPrefabs();
        if (valid == 0)
            return null;

        int pick = UnityEngine.Random.Range(0, valid);
        for (int i = 0; i < characterPrefabs.Count; i++)
        {
            if (characterPrefabs[i] == null)
                continue;
            if (pick == 0)
                return characterPrefabs[i];
            pick--;
        }

        return null;
    }

    public int CountValidPrefabs()
    {
        int count = 0;
        if (characterPrefabs == null)
            return 0;
        for (int i = 0; i < characterPrefabs.Count; i++)
        {
            if (characterPrefabs[i] != null)
                count++;
        }
        return count;
    }

    private List<Transform> CollectSpawnZones()
    {
        List<Transform> zones = new List<Transform>();

        if (spawnZones != null)
        {
            for (int i = 0; i < spawnZones.Count; i++)
            {
                Transform zone = spawnZones[i];
                if (zone != null && !zones.Contains(zone))
                    zones.Add(zone);
            }
        }

        if (spawnZonesParent != null)
        {
            for (int i = 0; i < spawnZonesParent.childCount; i++)
            {
                Transform child = spawnZonesParent.GetChild(i);
                if (child != null && !zones.Contains(child))
                    zones.Add(child);
            }
        }

        return zones;
    }

    private Vector3 GetSpawnPosition(Vector3 zonePosition)
    {
        Vector3 position = zonePosition;
        position.x += UnityEngine.Random.Range(-positionJitter, positionJitter);
        position.z += UnityEngine.Random.Range(-positionJitter, positionJitter);
        return EnforceRacingLineClearance(position);
    }

    // If racing-line waypoints are assigned, keep this point at least
    // minClearanceFromRacingLine away from the polyline they form.
    public Vector3 EnforceRacingLineClearance(Vector3 position)
    {
        if (!TryGetClosestPointOnWaypoints(position, out Vector3 closest, out float closestSqr))
            return position;

        float min = Mathf.Max(0f, minClearanceFromRacingLine);
        float dist = Mathf.Sqrt(closestSqr);
        if (dist >= min)
            return position;

        Vector3 away = position - closest;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
            away = Vector3.right;

        return closest + away.normalized * min;
    }

    public Quaternion GetFacingRotation(Vector3 spawnPosition)
    {
        if (TryGetClosestRacingLinePoint(spawnPosition, out Vector3 closest))
        {
            Vector3 direction = closest - spawnPosition;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
                return Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        return GetRandomYRotation();
    }

    public Quaternion GetRandomYRotation()
    {
        return Quaternion.Euler(0f, UnityEngine.Random.Range(0f, randomYRotationMax), 0f);
    }

    private bool TryGetClosestRacingLinePoint(Vector3 position, out Vector3 closest)
    {
        if (TryGetClosestPointOnSpline(position, out closest))
            return true;

        return TryGetClosestPointOnWaypoints(position, out closest, out _);
    }

    private bool TryGetClosestPointOnSpline(Vector3 position, out Vector3 closest)
    {
        closest = position;
        SplineContainer container = ResolveRacingLineSpline();
        if (container == null || container.Splines == null || container.Splines.Count == 0)
            return false;

        float3 localPoint = container.transform.InverseTransformPoint(position);
        float bestDistance = float.PositiveInfinity;
        bool found = false;
        Vector3 bestWorld = position;

        for (int i = 0; i < container.Splines.Count; i++)
        {
            Spline spline = container.Splines[i];
            if (spline == null || spline.Count < 1)
                continue;

            SplineUtility.GetNearestPoint(spline, localPoint, out float3 nearest, out _);
            if (!math.isfinite(nearest.x) || !math.isfinite(nearest.y) || !math.isfinite(nearest.z))
                continue;

            Vector3 world = container.transform.TransformPoint(nearest);
            float distance = Vector3.Distance(position, world);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestWorld = world;
                found = true;
            }
        }

        if (!found)
            return false;

        closest = bestWorld;
        return true;
    }

    private SplineContainer ResolveRacingLineSpline()
    {
        if (racingLineSpline != null)
            return racingLineSpline;

        if (TryGetComponent(out SplineContainer onSelf))
            return onSelf;

        return FindFirstObjectByType<SplineContainer>();
    }

    private bool TryGetClosestPointOnWaypoints(Vector3 position, out Vector3 closest, out float closestSqr)
    {
        closest = position;
        closestSqr = float.MaxValue;

        if (racingLineWaypoints == null || racingLineWaypoints.Length < 1)
            return false;

        bool found = false;

        for (int i = 0; i < racingLineWaypoints.Length; i++)
        {
            Transform a = racingLineWaypoints[i];
            if (a == null)
                continue;

            Vector3 pointA = a.position;
            pointA.y = position.y;

            Transform b = null;
            for (int j = i + 1; j < racingLineWaypoints.Length; j++)
            {
                if (racingLineWaypoints[j] != null)
                {
                    b = racingLineWaypoints[j];
                    break;
                }
            }

            Vector3 candidate;
            if (b != null)
            {
                Vector3 pointB = b.position;
                pointB.y = position.y;
                candidate = ClosestPointOnSegment(pointA, pointB, position);
            }
            else
            {
                candidate = pointA;
            }

            float sqr = (position - candidate).sqrMagnitude;
            if (sqr < closestSqr)
            {
                closestSqr = sqr;
                closest = candidate;
                found = true;
            }
        }

        return found;
    }

    private static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 point)
    {
        Vector3 ab = b - a;
        float lengthSqr = ab.sqrMagnitude;
        if (lengthSqr < 0.0001f)
            return a;

        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSqr);
        return a + ab * t;
    }

    public static void TryPlayRandomIdle(GameObject instance)
    {
        Animator animator = instance.GetComponentInChildren<Animator>();
        if (animator == null)
            return;

        RuntimeAnimatorController controller = animator.runtimeAnimatorController;
        if (controller == null)
            return;

        AnimationClip[] clips = controller.animationClips;
        if (clips == null || clips.Length == 0)
            return;

        animator.applyRootMotion = false;

        AnimationClip chosen = PickIdleOrAnyClip(clips);
        if (chosen == null)
            return;

        try
        {
            int hash = Animator.StringToHash(chosen.name);
            if (animator.HasState(0, hash))
                animator.Play(hash, 0, UnityEngine.Random.value);
            else
                animator.Play(0, 0, UnityEngine.Random.value);
        }
        catch (Exception)
        {
            // Prefab has an Animator but no playable state — leave it as-is.
        }
    }

    private static AnimationClip PickIdleOrAnyClip(AnimationClip[] clips)
    {
        int idleCount = 0;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null && clips[i].name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0)
                idleCount++;
        }

        if (idleCount > 0)
        {
            int pick = UnityEngine.Random.Range(0, idleCount);
            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip clip = clips[i];
                if (clip == null)
                    continue;
                if (clip.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (pick == 0)
                    return clip;
                pick--;
            }
        }

        int anyCount = 0;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null)
                anyCount++;
        }

        if (anyCount == 0)
            return null;

        int anyPick = UnityEngine.Random.Range(0, anyCount);
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == null)
                continue;
            if (anyPick == 0)
                return clips[i];
            anyPick--;
        }

        return null;
    }

    public bool IsCrowdHit(Transform hitTransform)
    {
        if (hitTransform == null)
            return false;

        if (crowdContainer != null && hitTransform.IsChildOf(crowdContainer))
            return true;

        for (int i = 0; i < spawnedInstances.Count; i++)
        {
            GameObject instance = spawnedInstances[i];
            if (instance != null && hitTransform.IsChildOf(instance.transform))
                return true;
        }

        return false;
    }

    public bool TryRaycastPaintSurface(Ray ray, out RaycastHit hit)
    {
        hit = default;
        RaycastHit[] hits = Physics.RaycastAll(ray, 10000f, paintSurfaceLayers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return false;

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            if (IsCrowdHit(hits[i].transform))
                continue;
            hit = hits[i];
            return true;
        }

        return false;
    }

    public bool TrySnapToPaintSurface(Vector3 origin, Vector3 down, float maxDistance, out RaycastHit hit)
    {
        hit = default;
        RaycastHit[] hits = Physics.RaycastAll(origin, down, maxDistance, paintSurfaceLayers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return false;

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            if (IsCrowdHit(hits[i].transform))
                continue;
            hit = hits[i];
            return true;
        }

        return false;
    }

    public static Vector3 SampleUniformDisc(Vector3 center, Vector3 normal, float radius)
    {
        Vector3 tangent = Vector3.Cross(normal, Vector3.up);
        if (tangent.sqrMagnitude < 0.0001f)
            tangent = Vector3.Cross(normal, Vector3.right);
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;

        float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float r = radius * Mathf.Sqrt(UnityEngine.Random.value);
        return center + (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * r;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.7f);
        List<Transform> zones = CollectSpawnZones();
        for (int i = 0; i < zones.Count; i++)
        {
            Transform zone = zones[i];
            if (zone == null)
                continue;

            Gizmos.DrawWireSphere(zone.position, Mathf.Max(0.25f, positionJitter));
        }

        if (racingLineWaypoints == null || racingLineWaypoints.Length < 2)
            return;

        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.8f);
        Vector3? previous = null;
        for (int i = 0; i < racingLineWaypoints.Length; i++)
        {
            if (racingLineWaypoints[i] == null)
                continue;

            Vector3 current = racingLineWaypoints[i].position;
            if (previous.HasValue)
                Gizmos.DrawLine(previous.Value, current);
            previous = current;
        }
    }
}
