using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Keeps the spectator crowd cheap while you race.
//
// Every spectator is in one of 3 states, decided by distance from the camera:
//   ANIMATED  - close:  visible + Animator running
//   FROZEN    - medium: visible, but Animator OFF (holds its pose -> almost no CPU cost)
//   HIDDEN    - far, or behind the camera: renderers + Animator OFF
//
// WHY THIS IS FASTER THAN THE OLD VERSION
//  * No more SetActive() on animated characters. Re-activating an Animator re-initialises it,
//    which caused the stutter spikes when you drove past the crowd. We only flip renderers/animator.
//  * Everything starts hidden and is sorted in ONE pass the moment the camera exists,
//    instead of the whole crowd running for the first seconds of the race.
//  * Animators use CullCompletely (no animation work for anything off-screen).
//  * Crowd shadows are removed (every skinned person was being drawn again for each shadow cascade).
//  * People behind the camera are hidden, and the number of changes per frame is capped.
//
// SETUP: same as before - keep it on the same object as CrowdSpawner. Nothing needs to be assigned.
public class CrowdDistanceManager : MonoBehaviour
{
    [Tooltip("Optional. Leave EMPTY - found automatically by the 'Player' tag. Only used if there is no camera.")]
    public Transform player;

    [Tooltip("Crowd members closer than this are shown (frozen pose). Lower = faster. 45-60 is plenty on a race track.")]
    public float activeDistance = 50f;

    [Tooltip("Extra buffer so people don't flicker on/off at the edge. They hide at activeDistance + this.")]
    public float hysteresis = 8f;

    [Tooltip("Crowd members closer than this also PLAY their animation. Beyond it they hold a still pose.")]
    public float animateDistance = 22f;

    [Tooltip("People BEHIND the camera are hidden once they are further than this.")]
    public float behindCameraKeepDistance = 12f;

    [Tooltip("How many crowd members are checked per frame. Spreads the work out so there's no hitch.")]
    public int checksPerFrame = 200;

    [Tooltip("Max visibility changes applied per frame (prevents spikes).")]
    public int maxChangesPerFrame = 40;

    [Header("Render savings")]
    [Tooltip("Crowd stops casting shadows. Big GPU saving, barely noticeable.")]
    public bool disableCrowdShadows = true;

    [Tooltip("Optional. Auto-found if empty.")]
    public CrowdSpawner spawner;

    private const byte Hidden = 0, Frozen = 1, Animated = 2;

    private GameObject[] people;
    private Vector3[] positions;
    private Renderer[][] renderers;
    private Animator[] animators;
    private byte[] state;
    private bool[] primed;       // has this person's pose been initialised yet?

    private int cursor;
    private bool firstPassDone;
    private Transform cam;
    private float nextCamLookup;

    private void Start()
    {
        if (spawner == null) spawner = FindObjectOfType<CrowdSpawner>();
        BuildList();
    }

    private void BuildList()
    {
        var list = new List<GameObject>();

        if (spawner != null)
        {
            foreach (var go in spawner.SpawnedInstances)
                if (go != null) list.Add(go);

            // Fallback: crowd baked in the container but the list wasn't saved.
            if (list.Count == 0 && spawner.CrowdContainer != null)
                foreach (Transform child in spawner.CrowdContainer)
                    list.Add(child.gameObject);
        }

        int n = list.Count;
        people = list.ToArray();
        positions = new Vector3[n];
        renderers = new Renderer[n][];
        animators = new Animator[n];
        state = new byte[n];
        primed = new bool[n];

        for (int i = 0; i < n; i++)
        {
            GameObject go = people[i];
            positions[i] = go.transform.position; // spectators are static, cache once

            if (!go.activeSelf) go.SetActive(true); // old version may have left it off in the editor

            renderers[i] = go.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers[i].Length; r++)
            {
                Renderer rend = renderers[i][r];
                if (disableCrowdShadows)
                {
                    rend.shadowCastingMode = ShadowCastingMode.Off;
                    rend.receiveShadows = false;
                }
                var smr = rend as SkinnedMeshRenderer;
                if (smr != null)
                {
                    smr.updateWhenOffscreen = false;
                    smr.skinnedMotionVectors = false;
                }
                rend.enabled = false; // start hidden, sorted on the first pass
            }

            Animator anim = go.GetComponentInChildren<Animator>(true);
            if (anim != null)
            {
                anim.cullingMode = AnimatorCullingMode.CullCompletely;
                anim.applyRootMotion = false;
                anim.enabled = false;
            }
            animators[i] = anim;
            state[i] = Hidden;
        }

        if (n == 0)
            Debug.LogWarning("CrowdDistanceManager: found 0 crowd members - nothing to cull. Is the CrowdSpawner assigned / crowd spawned?");
    }

    private bool TryGetReference(out Vector3 pos, out Vector3 forward, out bool hasForward)
    {
        if (cam == null && Time.unscaledTime >= nextCamLookup)
        {
            nextCamLookup = Time.unscaledTime + 1f;
            Camera c = Camera.main;
            if (c != null) cam = c.transform;
        }

        if (cam != null)
        {
            pos = cam.position;
            forward = cam.forward;
            hasForward = true;
            return true;
        }

        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }

        if (player != null)
        {
            pos = player.position;
            forward = player.forward;
            hasForward = false; // car heading is a worse guide than the camera, skip the behind test
            return true;
        }

        pos = forward = Vector3.zero;
        hasForward = false;
        return false;
    }

    private void Update()
    {
        if (people == null || people.Length == 0) return;
        if (!TryGetReference(out Vector3 refPos, out Vector3 refFwd, out bool hasFwd)) return;

        float showSqr = activeDistance * activeDistance;
        float hideD = activeDistance + hysteresis;
        float hideSqr = hideD * hideD;
        float animSqr = animateDistance * animateDistance;
        float stopAnimD = animateDistance + hysteresis * 0.5f;
        float stopAnimSqr = stopAnimD * stopAnimD;
        float behindSqr = behindCameraKeepDistance * behindCameraKeepDistance;

        // First time we have a camera: sort the WHOLE crowd at once (no per-frame cap) so
        // the race never starts with the full crowd running.
        bool full = !firstPassDone;
        int count = full ? people.Length : Mathf.Min(checksPerFrame, people.Length);
        int changes = 0;
        int changeCap = full ? int.MaxValue : Mathf.Max(1, maxChangesPerFrame);

        for (int n = 0; n < count; n++)
        {
            if (cursor >= people.Length) cursor = 0;
            int i = cursor;

            if (people[i] != null)
            {
                Vector3 d = positions[i] - refPos;
                float sqr = d.sqrMagnitude;
                byte current = state[i];

                bool visible = current != Hidden ? sqr <= hideSqr : sqr < showSqr;
                if (visible && hasFwd && sqr > behindSqr && Vector3.Dot(d, refFwd) < 0f)
                    visible = false; // behind the camera

                byte wanted = Hidden;
                if (visible)
                {
                    bool animate = current == Animated ? sqr <= stopAnimSqr : sqr < animSqr;
                    wanted = animate ? Animated : Frozen;
                }

                if (wanted != current)
                {
                    if (changes >= changeCap) break; // try this one again next frame
                    Apply(i, wanted);
                    changes++;
                }
            }
            cursor++;
        }

        if (full) firstPassDone = true;
    }

    private void Apply(int i, byte wanted)
    {
        Animator anim = animators[i];

        // First time a person becomes visible: give them a random pose and evaluate it once,
        // otherwise a "frozen" person would be stuck in the T-pose and everyone would be in sync.
        if (wanted != Hidden && !primed[i])
        {
            primed[i] = true;
            if (anim != null && anim.runtimeAnimatorController != null)
            {
                anim.enabled = true;
                anim.Play(0, 0, Random.value);
                anim.Update(0f);
            }
        }

        if (anim != null)
            anim.enabled = (wanted == Animated);

        bool show = wanted != Hidden;
        Renderer[] rs = renderers[i];
        for (int r = 0; r < rs.Length; r++)
            if (rs[r] != null) rs[r].enabled = show;

        state[i] = wanted;
    }
}