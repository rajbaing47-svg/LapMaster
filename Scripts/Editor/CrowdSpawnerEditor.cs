using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CrowdSpawner))]
public class CrowdSpawnerEditor : Editor
{
    private static bool paintMode;
    private static CrowdSpawnerEditor activePainter;
    private Tool previousTool = Tool.None;
    private bool hookedSceneGui;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CrowdSpawner spawner = (CrowdSpawner)target;

        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox(
            "Place empty spawn-zone objects along the track edge (as children of Spawn Zones Parent, or in Spawn Zones), outside the 10-unit racing-line clearance. " +
            "Assign people prefabs from city, downtown, elder, little_kids, or professions (not PROPS/tools). " +
            "Spawn Crowd bakes instances under CrowdContainer. Paint Crowd lets you click the track mesh in the Scene view instead.",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Spawn Crowd", GUILayout.Height(28f)))
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Spawn Crowd");
            spawner.SpawnCrowd();
            Undo.CollapseUndoOperations(undoGroup);
        }

        if (GUILayout.Button("Clear Crowd", GUILayout.Height(28f)))
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Clear Crowd");
            spawner.ClearCrowd();
            Undo.CollapseUndoOperations(undoGroup);
        }

        EditorGUILayout.EndHorizontal();

        Color previous = GUI.backgroundColor;
        GUI.backgroundColor = paintMode && activePainter == this ? new Color(0.45f, 0.85f, 0.45f) : previous;
        string paintLabel = paintMode && activePainter == this ? "Paint Crowd (On)" : "Paint Crowd";
        if (GUILayout.Button(paintLabel, GUILayout.Height(28f)))
            TogglePaintMode();
        GUI.backgroundColor = previous;

        if (paintMode && activePainter == this)
        {
            EditorGUILayout.HelpBox(
                "Paint Crowd is active. Left-click the track mesh to scatter people. Right-click or Shift+click to erase. Alt still orbits. Escape or this button exits paint mode.",
                MessageType.None);
        }
    }

    private void OnDisable()
    {
        if (activePainter == this)
            SetPaintMode(false);
    }

    private void OnDestroy()
    {
        if (activePainter == this)
            SetPaintMode(false);
    }

    private void TogglePaintMode()
    {
        SetPaintMode(!(paintMode && activePainter == this));
    }

    private void SetPaintMode(bool enabled)
    {
        if (enabled)
        {
            if (activePainter != null && activePainter != this)
                activePainter.SetPaintMode(false);

            paintMode = true;
            activePainter = this;
            previousTool = Tools.current;
            Tools.current = Tool.None;
            HookSceneGui();
            SceneView.RepaintAll();
        }
        else
        {
            paintMode = false;
            if (activePainter == this)
                activePainter = null;
            UnhookSceneGui();
            if (Tools.current == Tool.None)
                Tools.current = previousTool != Tool.None ? previousTool : Tool.Move;
            SceneView.RepaintAll();
            Repaint();
        }
    }

    private void HookSceneGui()
    {
        if (hookedSceneGui)
            return;
        SceneView.duringSceneGui += DuringSceneGui;
        hookedSceneGui = true;
    }

    private void UnhookSceneGui()
    {
        if (!hookedSceneGui)
            return;
        SceneView.duringSceneGui -= DuringSceneGui;
        hookedSceneGui = false;
    }

    private void DuringSceneGui(SceneView sceneView)
    {
        if (!paintMode || activePainter != this)
        {
            UnhookSceneGui();
            return;
        }

        CrowdSpawner spawner = target as CrowdSpawner;
        if (spawner == null)
        {
            SetPaintMode(false);
            return;
        }

        Event e = Event.current;
        if (e == null)
            return;

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            SetPaintMode(false);
            e.Use();
            return;
        }

        int controlId = GUIUtility.GetControlID(FocusType.Passive);

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        bool hasHit = spawner.TryRaycastPaintSurface(ray, out RaycastHit hit);

        if (hasHit)
        {
            Handles.color = e.shift || e.button == 1
                ? new Color(1f, 0.35f, 0.25f, 0.95f)
                : new Color(0.3f, 0.9f, 0.45f, 0.95f);
            Handles.DrawWireDisc(hit.point, hit.normal.sqrMagnitude > 0.0001f ? hit.normal : Vector3.up, Mathf.Max(0.05f, spawner.brushRadius));
        }

        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            sceneView.Repaint();

        // Leave Alt+mouse to Scene view orbit/pan/zoom.
        if (e.alt)
            return;

        HandleUtility.AddDefaultControl(controlId);

        if (e.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
        {
            GUIUtility.hotControl = 0;
            e.Use();
            return;
        }

        if (e.type != EventType.MouseDown)
            return;

        if (!hasHit)
            return;

        bool erase = e.button == 1 || (e.button == 0 && e.shift);
        bool paint = e.button == 0 && !e.shift;
        if (!erase && !paint)
            return;

        if (erase)
            EraseAt(spawner, hit.point);
        else
            PaintAt(spawner, hit);

        GUIUtility.hotControl = controlId;
        e.Use();
        sceneView.Repaint();
    }

    private static void PaintAt(CrowdSpawner spawner, RaycastHit surfaceHit)
    {
        if (spawner.CountValidPrefabs() == 0)
        {
            Debug.LogWarning("CrowdSpawner: no character prefabs assigned. Drag people prefabs into Character Prefabs.");
            return;
        }

        int wanted = Mathf.Max(0, spawner.peoplePerClick);
        if (wanted == 0)
            return;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Paint Crowd");

        spawner.EnsureContainer();

        Vector3 up = surfaceHit.normal.sqrMagnitude > 0.0001f ? surfaceHit.normal.normalized : Vector3.up;
        float radius = Mathf.Max(0.05f, spawner.brushRadius);
        float minSpacing = Mathf.Max(0f, spawner.minSpacingBetweenPeople);
        float minSpacingSqr = minSpacing * minSpacing;
        float lift = Mathf.Max(2f, radius * 0.25f);

        List<Vector3> batch = new List<Vector3>(wanted);
        int attempts = Mathf.Max(wanted * 8, wanted);

        for (int i = 0; i < attempts && batch.Count < wanted; i++)
        {
            Vector3 candidate = CrowdSpawner.SampleUniformDisc(surfaceHit.point, up, radius);
            if (!TryResolvePaintPoint(spawner, candidate, up, lift, out Vector3 placed))
                continue;

            if (minSpacingSqr > 0f && IsTooClose(placed, batch, minSpacingSqr))
                continue;

            GameObject prefab = spawner.PickRandomPrefab();
            if (prefab == null)
                continue;

            Quaternion rotation = spawner.faceTowardTrack
                ? spawner.GetFacingRotation(placed)
                : spawner.GetRandomYRotation();
            float scale = Random.Range(spawner.scaleRange.x, spawner.scaleRange.y);

            GameObject instance = spawner.InstantiateCrowdMember(prefab, placed, rotation, "Paint Crowd");
            if (instance == null)
                continue;

            instance.name = CrowdSpawner.SpawnedPrefix + prefab.name;
            instance.transform.SetParent(spawner.CrowdContainer, true);
            instance.transform.localScale = prefab.transform.localScale * scale;
            CrowdSpawner.TryPlayRandomIdle(instance);
            spawner.RegisterSpawnedInstance(instance);
            batch.Add(placed);
        }

        Undo.CollapseUndoOperations(undoGroup);
    }

    private static bool TryResolvePaintPoint(CrowdSpawner spawner, Vector3 candidate, Vector3 up, float lift, out Vector3 placed)
    {
        placed = candidate;
        Vector3 down = -up;
        if (!spawner.TrySnapToPaintSurface(candidate + up * lift, down, lift * 2f + 4f, out RaycastHit snap))
            return false;

        Vector3 cleared = spawner.EnforceRacingLineClearance(snap.point);
        if ((cleared - snap.point).sqrMagnitude > 0.0001f)
        {
            if (spawner.TrySnapToPaintSurface(cleared + up * lift, down, lift * 2f + 4f, out RaycastHit resnap))
                placed = resnap.point;
            else
                placed = cleared;
        }
        else
        {
            placed = snap.point;
        }

        return true;
    }

    private static bool IsTooClose(Vector3 point, List<Vector3> batch, float minSpacingSqr)
    {
        for (int i = 0; i < batch.Count; i++)
        {
            if ((batch[i] - point).sqrMagnitude < minSpacingSqr)
                return true;
        }

        return false;
    }

    private static void EraseAt(CrowdSpawner spawner, Vector3 center)
    {
        float radius = Mathf.Max(0.05f, spawner.brushRadius);
        float radiusSqr = radius * radius;

        List<GameObject> toRemove = new List<GameObject>();
        List<GameObject> instances = spawner.SpawnedInstances;
        for (int i = 0; i < instances.Count; i++)
        {
            GameObject instance = instances[i];
            if (instance == null)
                continue;
            if ((instance.transform.position - center).sqrMagnitude <= radiusSqr)
                toRemove.Add(instance);
        }

        Transform container = spawner.CrowdContainer;
        if (container != null)
        {
            for (int i = 0; i < container.childCount; i++)
            {
                Transform child = container.GetChild(i);
                if (child == null)
                    continue;
                if ((child.position - center).sqrMagnitude > radiusSqr)
                    continue;
                if (!toRemove.Contains(child.gameObject))
                    toRemove.Add(child.gameObject);
            }
        }

        if (toRemove.Count == 0)
            return;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Erase Crowd");

        for (int i = 0; i < toRemove.Count; i++)
            spawner.DestroySpawnedInstance(toRemove[i]);

        Undo.CollapseUndoOperations(undoGroup);
    }
}
