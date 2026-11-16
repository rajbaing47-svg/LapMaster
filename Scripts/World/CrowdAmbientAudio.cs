using System.Collections;
using UnityEngine;

// Zone-based 3D crowd ambience. Place empty GameObjects along the track
// (near CrowdSpawner zones) and assign cheer/ambience loops. Independent of
// CrowdSpawner — no race or spawn logic.
//
// Playback uses TWO AudioSources that alternate/overlap: while one clip is
// finishing its tail, the other source starts the next randomly picked clip,
// so there is no silence gap between bursts - it sounds like a continuous,
// slightly overlapping crowd instead of clip -> silence -> clip.
public class CrowdAmbientAudio : MonoBehaviour
{
    private enum CrowdAudioTier
    {
        None,
        Combined,
        Intense,
        Soft,
        Ambience
    }

    [Header("Clips")]
    [Tooltip("Strong cheering + strong rhythmic, Strong cheering + soft rhythmic, Strong cheering I, Strong cheering II short.")]
    public AudioClip[] strongCheerClips;
    [Tooltip("Soft cheering I, Soft cheering II, Soft cheering and chatter.")]
    public AudioClip[] softCheerClips;
    [Tooltip("Rhythmic cheering.")]
    public AudioClip[] rhythmicCheerClips;
    [Tooltip("Ambience, Ambience and cheering.")]
    public AudioClip[] ambienceClips;

    [Header("Listener")]
    [Tooltip("Car/player transform used for distance-based intensity. If empty, Camera.main is used.")]
    public Transform listener;

    [Header("3D Falloff")]
    [Tooltip("Distance at which volume is max.")]
    public float minRadius = 5f;
    [Tooltip("Distance at which volume reaches zero.")]
    public float maxRadius = 40f;
    [Tooltip("Unity spatial rolloff between min and max radius.")]
    public AudioRolloffMode rolloffMode = AudioRolloffMode.Linear;

    [Header("Playback")]
    [Range(0f, 1f)]
    public float volume = 0.8f;
    public bool loop = true;
    public bool playOnStart = true;

    [Header("Variety")]
    [Tooltip("Random pitch range so overlapping zones using the same clip do not phase.")]
    public Vector2 pitchRange = new Vector2(0.95f, 1.05f);
    [Tooltip("How much earlier (seconds) the next clip starts before the current one ends. Creates the overlap/crossfade instead of a gap.")]
    public Vector2 retriggerLeadTime = new Vector2(0.15f, 0.4f);

    private const float TierCheckInterval = 0.5f;

    // Two alternating sources so overlap is possible (one AudioSource can only
    // play one clip at a time).
    private AudioSource sourceA;
    private AudioSource sourceB;
    private bool useSourceA = true;

    private CrowdAudioTier currentTier = CrowdAudioTier.None;
    private bool loggedEmptyWarning;
    private Coroutine retriggerRoutine;

    private void OnEnable()
    {
        CancelInvoke(nameof(EvaluateDistanceTier));
        InvokeRepeating(nameof(EvaluateDistanceTier), TierCheckInterval, TierCheckInterval);

        if (currentTier != CrowdAudioTier.None && retriggerRoutine == null)
            PlayClipFromTier(currentTier);
    }

    private void Start()
    {
        if (playOnStart)
            PlayRandomCheer();
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(EvaluateDistanceTier));
        StopRetriggerRoutine();

        if (sourceA != null && sourceA.isPlaying)
            sourceA.Stop();
        if (sourceB != null && sourceB.isPlaying)
            sourceB.Stop();
    }

    public void PlayRandomCheer()
    {
        CrowdAudioTier tier = ResolveSelectedTier();
        currentTier = tier;
        PlayClipFromTier(tier);
    }

    private void EvaluateDistanceTier()
    {
        if (ResolveListener() == null)
            return;

        if (!playOnStart && currentTier == CrowdAudioTier.None)
            return;

        CrowdAudioTier tier = ResolveSelectedTier();
        if (tier == currentTier)
            return;

        // Tier changed - cut over immediately instead of waiting for the
        // currently playing clip(s) to finish naturally.
        currentTier = tier;
        PlayClipFromTier(tier);
    }

    private void PlayClipFromTier(CrowdAudioTier tier)
    {
        StopRetriggerRoutine();

        if (sourceA != null && sourceA.isPlaying)
            sourceA.Stop();
        if (sourceB != null && sourceB.isPlaying)
            sourceB.Stop();

        if (!PlayTriggeredClip(tier, out float playbackDuration))
            return;

        if (!isActiveAndEnabled)
            return;

        retriggerRoutine = StartCoroutine(RetriggerLoop(tier, playbackDuration));
    }

    // Schedules the NEXT clip to start slightly before the CURRENT one ends,
    // alternating sources so both can briefly overlap.
    private IEnumerator RetriggerLoop(CrowdAudioTier tier, float initialPlaybackDuration)
    {
        float playbackDuration = initialPlaybackDuration;

        while (true)
        {
            float leadMin = Mathf.Min(retriggerLeadTime.x, retriggerLeadTime.y);
            float leadMax = Mathf.Max(retriggerLeadTime.x, retriggerLeadTime.y);
            float lead = Random.Range(leadMin, leadMax);

            float waitTime = Mathf.Max(0f, playbackDuration - lead);
            if (waitTime > 0f)
                yield return new WaitForSeconds(waitTime);

            if (!PlayTriggeredClip(tier, out playbackDuration))
            {
                retriggerRoutine = null;
                yield break;
            }
        }
    }

    // Plays a new random clip from the given tier on whichever source is not
    // currently the "active/most recent" one, so it overlaps with the other
    // source's still-finishing tail. Returns the new clip's playback duration.
    private bool PlayTriggeredClip(CrowdAudioTier tier, out float playbackDuration)
    {
        playbackDuration = 0f;

        AudioClip clip = PickClipForTier(tier);
        if (clip == null)
        {
            if (!loggedEmptyWarning)
            {
                Debug.LogWarning("CrowdAmbientAudio: no cheer clips assigned on " + name + ".", this);
                loggedEmptyWarning = true;
            }
            return false;
        }

        EnsureAudioSources();

        AudioSource target = useSourceA ? sourceA : sourceB;
        useSourceA = !useSourceA;

        ConfigureAudioSource(target);

        target.Stop();
        target.clip = clip;
        target.pitch = Random.Range(pitchRange.x, pitchRange.y);
        target.Play();

        float pitch = Mathf.Abs(target.pitch);
        if (pitch < 0.01f)
            pitch = 0.01f;
        playbackDuration = clip.length / pitch;

        return true;
    }

    private void StopRetriggerRoutine()
    {
        if (retriggerRoutine == null)
            return;

        StopCoroutine(retriggerRoutine);
        retriggerRoutine = null;
    }

    private CrowdAudioTier ResolveSelectedTier()
    {
        Transform resolvedListener = ResolveListener();
        if (resolvedListener == null)
            return CrowdAudioTier.Combined;

        float distance = Vector3.Distance(transform.position, resolvedListener.position);
        float minDistance = Mathf.Max(0.01f, minRadius);
        float maxDistance = Mathf.Max(minDistance, maxRadius);

        if (distance <= minDistance)
            return CrowdAudioTier.Intense;
        if (distance >= maxDistance)
            return CrowdAudioTier.Ambience;
        return CrowdAudioTier.Soft;
    }

    private Transform ResolveListener()
    {
        if (listener != null)
            return listener;

        Camera mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.transform : null;
    }

    private AudioClip PickClipForTier(CrowdAudioTier tier)
    {
        if (tier == CrowdAudioTier.Combined)
            return PickRandomClipFromCombined();

        AudioClip clip = PickRandomClipFromArray(GetPrimaryClips(tier));
        if (clip != null)
            return clip;

        if (tier != CrowdAudioTier.Intense)
        {
            clip = PickRandomClipFromArray(GetPrimaryClips(CrowdAudioTier.Intense));
            if (clip != null)
                return clip;
        }

        if (tier != CrowdAudioTier.Soft)
        {
            clip = PickRandomClipFromArray(softCheerClips);
            if (clip != null)
                return clip;
        }

        if (tier != CrowdAudioTier.Ambience)
            return PickRandomClipFromArray(ambienceClips);

        return null;
    }

    private AudioClip[] GetPrimaryClips(CrowdAudioTier tier)
    {
        switch (tier)
        {
            case CrowdAudioTier.Intense:
                return PickIntenseArray();
            case CrowdAudioTier.Soft:
                return softCheerClips;
            case CrowdAudioTier.Ambience:
                return ambienceClips;
            default:
                return null;
        }
    }

    private AudioClip[] PickIntenseArray()
    {
        bool hasStrong = HasClips(strongCheerClips);
        bool hasRhythmic = HasClips(rhythmicCheerClips);

        if (hasStrong && hasRhythmic)
            return Random.value < 0.5f ? strongCheerClips : rhythmicCheerClips;
        if (hasStrong)
            return strongCheerClips;
        if (hasRhythmic)
            return rhythmicCheerClips;
        return null;
    }

    private AudioClip PickRandomClipFromCombined()
    {
        int valid = CountValidClips(strongCheerClips)
            + CountValidClips(softCheerClips)
            + CountValidClips(rhythmicCheerClips)
            + CountValidClips(ambienceClips);

        if (valid == 0)
            return null;

        int pick = Random.Range(0, valid);
        if (TryTakeClip(strongCheerClips, ref pick, out AudioClip clip))
            return clip;
        if (TryTakeClip(softCheerClips, ref pick, out clip))
            return clip;
        if (TryTakeClip(rhythmicCheerClips, ref pick, out clip))
            return clip;
        if (TryTakeClip(ambienceClips, ref pick, out clip))
            return clip;

        return null;
    }

    private static bool TryTakeClip(AudioClip[] clips, ref int pick, out AudioClip clip)
    {
        clip = null;
        if (clips == null)
            return false;

        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == null)
                continue;
            if (pick == 0)
            {
                clip = clips[i];
                return true;
            }
            pick--;
        }

        return false;
    }

    private static AudioClip PickRandomClipFromArray(AudioClip[] clips)
    {
        int valid = CountValidClips(clips);
        if (valid == 0)
            return null;

        int pick = Random.Range(0, valid);
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == null)
                continue;
            if (pick == 0)
                return clips[i];
            pick--;
        }

        return null;
    }

    private static bool HasClips(AudioClip[] clips)
    {
        return CountValidClips(clips) > 0;
    }

    private static int CountValidClips(AudioClip[] clips)
    {
        if (clips == null)
            return 0;

        int count = 0;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null)
                count++;
        }
        return count;
    }

    private void EnsureAudioSources()
    {
        if (sourceA == null)
        {
            // Reuse an existing AudioSource on this object as sourceA if one
            // was already added manually (keeps backward compatibility).
            AudioSource existing = GetComponent<AudioSource>();
            sourceA = existing != null ? existing : gameObject.AddComponent<AudioSource>();
        }

        if (sourceB == null)
            sourceB = gameObject.AddComponent<AudioSource>();
    }

    private void ConfigureAudioSource(AudioSource source)
    {
        float minDistance = Mathf.Max(0.01f, minRadius);
        float maxDistance = Mathf.Max(minDistance + 0.01f, maxRadius);

        source.spatialBlend = 1f;
        source.rolloffMode = rolloffMode;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        // One-shot cheer bursts retrigger via coroutine, not AudioSource.loop.
        source.loop = false;
        source.volume = volume;
        source.playOnAwake = false;
        source.dopplerLevel = 0f;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = transform.position;

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.7f);
        Gizmos.DrawWireSphere(center, Mathf.Max(0.01f, minRadius));

        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(center, Mathf.Max(0.01f, maxRadius));
    }
}