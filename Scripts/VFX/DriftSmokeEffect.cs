using UnityEngine;

// Attach this to the SAME GameObject as your `controller` script (the car root).
// Smoke ONLY appears while the handbrake (Space) is held - nothing else triggers it,
// and smoke will NOT show anything at spawn/race-start regardless of the particle
// prefab's own "Play On Awake" setting.
//
// UPGRADE (this version): tire smoke used to inherit whatever Shape/Size/Speed/Gravity
// values happened to be saved on the prefab, which produced a huge cloud floating above
// and around the whole car. That's fixed below by:
//   1. Repositioning each smoke system EVERY FRAME at the wheel's actual ground contact
//      point (using WheelCollider.GetGroundHit - the same math TireSkidMarks.cs uses to
//      draw the mark mesh), instead of only once in Awake() based on wheel.transform.
//      This is what makes the smoke actually follow the tire mark path instead of
//      drifting away from it as the car moves/turns.
//   2. Orienting the emitter to point backward along the wheel's velocity, and narrowing
//      the emission shape, so smoke trails out along the skid line instead of blooming
//      outward in a sphere/cone around the car.
//   3. Forcing the correct Shape/Start Size/Start Speed/Gravity/Drag values on the
//      ParticleSystem in code in PrepareSystem(), so the look is correct no matter what
//      the source prefab was authored with.
//
// UPGRADE NOTE: tire skid marks used to be handled here via TrailRenderer[] tireMarks.
// That's been replaced by TireSkidMarks.cs (dynamic mesh trail, same handbrake trigger,
// better-looking fade/contact-accurate marks). Add TireSkidMarks as a separate component
// on this same GameObject - this script now only handles the smoke particles + skid sound.
[RequireComponent(typeof(controller))]
public class DriftSmokeEffect : MonoBehaviour
{
    [Header("Smoke Particle Systems")]
    [Tooltip("Drag the rear-LEFT smoke ParticleSystem here (e.g. LSmoke). Required.")]
    public ParticleSystem leftSmoke;

    [Tooltip("Drag the rear-RIGHT smoke ParticleSystem here (e.g. RSmoke). Required.")]
    public ParticleSystem rightSmoke;

    [Header("Wheel References (for tire-height positioning)")]
    [Tooltip("Rear-left WheelCollider. Required for contact-accurate smoke positioning.")]
    public WheelCollider leftWheel;

    [Tooltip("Rear-right WheelCollider. Required for contact-accurate smoke positioning.")]
    public WheelCollider rightWheel;

    [Header("Mark Alignment")]
    [Tooltip("Small offset above the ground along the surface normal, so smoke doesn't z-fight/clip into the road. Matches TireSkidMarks' own 0.02 offset.")]
    public float groundOffset = 0.02f;

    [Range(1f, 45f)]
    [Tooltip("Emission cone angle. Narrow = smoke stays tight to the tire's path (recommended). Wide = spreads out like the old 'cloud around the car' look.")]
    public float emissionConeAngle = 5f;

    [Range(0.01f, 0.5f)]
    [Tooltip("Emission cone radius at the contact patch. Keep small so smoke originates right at the tire, not spread across a wide area.")]
    public float emissionConeRadius = 0.03f;

    [Tooltip("Orient smoke emission opposite the wheel's velocity so it trails backward along the skid mark instead of spreading sideways/around the car.")]
    public bool orientAlongVelocity = true;

    [Header("Mark Appearance (size/behavior)")]
    [Range(0.01f, 1f)]
    [Tooltip("Direct control of each particle's Start Size, in world units. This is the " +
             "actual size of a puff - NOT a transform scale multiplier, so it behaves " +
             "predictably regardless of the object's own scale/hierarchy. Start small " +
             "(0.1-0.2) and increase only if puffs look too tiny.")]
    public float smokeSize = 0.15f;

    [Range(0f, 1f)]
    [Tooltip("Controls how HIGH smoke can climb before sinking back down - 0 = stays low " +
             "right at tire height, 1 = allowed to climb as high as the original effect " +
             "did. Works via gravity pulling particles down, so it doesn't distort or " +
             "shrink individual puffs the way squashing the whole effect did.")]
    public float smokeHeightLimit = 0.2f;

    [Tooltip("If the smoke looks like a stretched wake/streak instead of a puffy cloud, " +
             "keep this ON - it forces round Billboard particles instead of velocity-" +
             "stretched ones. Turn OFF only if you specifically want streaky smoke.")]
    public bool forceBillboardMode = true;

    [Tooltip("Caps how long (seconds) each puff can live. The original effect's lifetime " +
             "was long enough that smoke kept piling up faster than it faded, eventually " +
             "filling the whole screen during a long drift. This does NOT change the " +
             "look/shape - it only stops buildup by making old puffs clear out sooner.")]
    public float maxLifetime = 0.6f;

    [Tooltip("Caps the max particle count so smoke can't keep accumulating indefinitely " +
             "during a long drift. Lower = clears the screen faster; only kicks in if the " +
             "original prefab's own Max Particles was set higher than this.")]
    public int maxParticleCount = 25;

    [Range(0.2f, 4f)]
    [Tooltip("Multiplies how often new puffs spawn. Raise this if smoke looks sparse/" +
             "faint with visible gaps; lower it if it looks too dense/solid.")]
    public float emissionDensity = 0.8f;

    [Header("Natural Smoke Behavior (OFF by default - enable one at a time)")]
    [Tooltip("Makes puffs grow larger as they age instead of staying a fixed size. " +
             "Start with this OFF, get plain puffs looking right first, then try it.")]
    public bool growOverLifetime = false;

    [Tooltip("Fades each puff's alpha to fully transparent by end of life. Safe to " +
             "leave ON - this just prevents a hard visible 'pop' when a puff despawns.")]
    public bool fadeOverLifetime = true;

    [Range(0f, 90f)]
    [Tooltip("Random rotation speed (deg/sec) per-particle. 0 = disabled. Leave at 0 " +
             "until basic puffs look right.")]
    public float randomRotationSpeed = 0f;

    [Range(0f, 1f)]
    [Tooltip("Turbulence via the Noise module. 0 = disabled. This is the most likely " +
             "setting to cause streaky/wispy artifacts if your particle count is low - " +
             "leave at 0 until basic puffs look right.")]
    public float turbulence = 0f;

    [Range(0f, 1f)]
    [Tooltip("Damps particle velocity over lifetime. 0 = disabled (recommended to start " +
             "- this can interact badly with a narrow emission cone and cause streaking).")]
    public float velocityDamping = 0f;

    [Tooltip("Randomizes start size per-particle. Safe to leave ON.")]
    public bool randomizeStartSize = true;

    [Range(0f, 1f)]
    [Tooltip("How much smaller the low end of the random start-size range can go, as a " +
             "fraction of the base size (e.g. 0.4 = puffs range from 40%-100% of base).")]
    public float startSizeVariation = 0.4f;

    [Header("Trigger Settings")]
    [Tooltip("Car must be moving at least this fast (KPH) for smoke to show while handbraking - stops the effect at a near-standstill handbrake tap.")]
    public float minSpeedForSmoke = 5f;

    [Tooltip("Optional: audio source playing the skidding/screech sound while drifting. Leave empty if you don't have one.")]
    public AudioSource skidClip;

    private bool drifting = false;
    private controller car;

    private void Awake()
    {
        car = GetComponent<controller>();

        // NOTE: material override removed per request - using the original smoke
        // effect's own material/texture as-is. Only positioning/size/lifetime/gravity
        // tuning below (ported from the green car reference) is applied on top of it.

        // FIX: positioning used to happen once here via wheel.transform. That's been
        // moved to LateUpdate()/UpdateSmokePosition() below so smoke tracks the actual
        // ground contact point every frame instead of going stale as the car moves.

        PrepareSystem(leftSmoke);
        PrepareSystem(rightSmoke);
    }

    private void PrepareSystem(ParticleSystem ps)
    {
        if (ps == null) return;

        var main = ps.main;
        main.loop = true;

        // UPGRADE: caps ONLY apply downward - if the original prefab's lifetime/max
        // particles were already lower than these caps, nothing changes. This stops the
        // "fills the whole screen" buildup without touching shape, color, speed, or any
        // other look-related setting.
        if (main.startLifetime.constantMax > maxLifetime || main.startLifetime.constant > maxLifetime)
        {
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                Mathf.Min(main.startLifetime.constantMin, maxLifetime),
                maxLifetime);
        }
        if (main.maxParticles > maxParticleCount)
        {
            main.maxParticles = maxParticleCount;
        }

        // UPGRADE: multiplies spawn rate to fill gaps between puffs so smoke reads as a
        // continuous cloud instead of sparse, separated blobs.
        var emission = ps.emission;
        emission.rateOverTimeMultiplier = emission.rateOverTimeMultiplier * emissionDensity;
        emission.rateOverDistanceMultiplier = emission.rateOverDistanceMultiplier * emissionDensity;

        // FIX (alignment): force a narrow Cone shape right at the contact patch instead
        // of whatever wide Sphere/Cone the prefab was authored with. This is what stops
        // smoke from ballooning outward over a wide area - it now only spawns in a tight
        // zone directly at the tire, matching where TireSkidMarks draws the mark mesh.
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = emissionConeAngle;
        shape.radius = emissionConeRadius;

        // FIX: smokeSize now sets the particle's actual Start Size directly, in world
        // units, instead of scaling the ParticleSystem's transform. Scaling the transform
        // depends on the Renderer's Scaling Mode (Hierarchy/Local/Shape) and this object's
        // own (possibly non-uniform) parent scale - which is what was causing puffs to
        // balloon out much bigger than expected regardless of what smokeSize was set to.
        // Setting Start Size directly is unaffected by any of that.
        main.startSize = new ParticleSystem.MinMaxCurve(
            randomizeStartSize ? smokeSize * (1f - startSizeVariation) : smokeSize,
            smokeSize);
        ps.transform.localScale = Vector3.one;

        // UPGRADE: height is now controlled via gravity instead of squashing the effect.
        // smokeHeightLimit of 0 = strong downward pull (smoke stays low at tire height),
        // 1 = no extra pull (smoke behaves as originally authored and can climb higher).
        var main2 = ps.main;
        float extraGravity = Mathf.Lerp(2f, 0f, smokeHeightLimit);
        main2.gravityModifier = new ParticleSystem.MinMaxCurve(main.gravityModifier.constant + extraGravity);

        // UPGRADE: fixes the "sea-wake" streak look. If the original prefab's Renderer
        // module uses Stretched Billboard, each particle's LENGTH is drawn from its
        // velocity, not its size - so after scaling the system down, particles become
        // small blobs stretched into long thin streaks by their speed, which reads as a
        // water wake instead of a puffy cloud. Switching to plain Billboard makes
        // particles round/size-based instead of velocity-stretched.
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (forceBillboardMode && renderer != null && renderer.renderMode == ParticleSystemRenderMode.Stretch)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
        }

        // NEW: grow puffs over their lifetime instead of staying a fixed size. Real
        // smoke expands as it dissipates - this is the single biggest fix for the
        // "static cloud stamp" look.
        if (growOverLifetime)
        {
            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve growCurve = new AnimationCurve(
                new Keyframe(0f, 0.3f),
                new Keyframe(0.4f, 0.9f),
                new Keyframe(1f, 1.6f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, growCurve);
        }

        // NEW: fade fully to transparent by end of life instead of just dimming/holding
        // a flat alpha - stops smoke from disappearing as a hard-edged "pop" and instead
        // dissolves it away naturally.
        if (fadeOverLifetime)
        {
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient fadeGradient = new Gradient();
            fadeGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.8f, 0.15f),
                    new GradientAlphaKey(0.5f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = fadeGradient;
        }

        // NEW: random per-particle rotation speed so puffs don't all look like the same
        // sprite copy-pasted - breaks up visual repetition at a glance.
        if (randomRotationSpeed > 0f)
        {
            var rotationOverLifetime = ps.rotationOverLifetime;
            rotationOverLifetime.enabled = true;
            float rad = randomRotationSpeed * Mathf.Deg2Rad;
            rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-rad, rad);
        }

        // NEW: light turbulence breaks up the clean sphere/cone silhouette into
        // something wispier - a flat, noise-free puff is what reads as "cartoon cloud"
        // rather than smoke.
        if (turbulence > 0f)
        {
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = turbulence;
            noise.frequency = 0.3f;
            noise.scrollSpeed = 0.2f;
            noise.damping = true;
        }

        // NEW: damp velocity over lifetime so particles lose the tire's kick-off speed
        // and settle into a gentle drift, instead of shooting outward at constant speed
        // for their whole life (which reads as an expanding blob rather than smoke that
        // trails off and hangs in the air).
        if (velocityDamping > 0f)
        {
            var limitVelocity = ps.limitVelocityOverLifetime;
            limitVelocity.enabled = true;
            limitVelocity.dampen = velocityDamping;
            limitVelocity.limit = new ParticleSystem.MinMaxCurve(main.startSpeed.constant * 0.5f);
        }

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void LateUpdate()
    {
        if (car == null) return;

        bool handbraking = car.IsHandbraking;
        bool fastEnough = car.KPH > minSpeedForSmoke;
        bool shouldDrift = car.grounded && fastEnough && handbraking;

        // FIX (alignment): update smoke position/orientation every frame at the real
        // tire contact point - same source data (WheelCollider.GetGroundHit) that
        // TireSkidMarks.cs uses to place its mark mesh, so smoke and marks originate
        // from the same spot instead of the smoke drifting off as the car moves/turns.
        UpdateSmokePosition(leftSmoke, leftWheel);
        UpdateSmokePosition(rightSmoke, rightWheel);

        SetActive(leftSmoke, shouldDrift);
        SetActive(rightSmoke, shouldDrift);
        CheckDrift(shouldDrift);
    }

    // Repositions/reorients a smoke emitter at the wheel's actual ground contact point
    // every frame, instead of the old one-time Awake() placement based on wheel.transform
    // (which is the wheel's pivot, not the contact patch, and never updated afterward).
    private void UpdateSmokePosition(ParticleSystem ps, WheelCollider wheel)
    {
        if (ps == null || wheel == null) return;

        WheelHit hit;
        if (wheel.GetGroundHit(out hit))
        {
            // same contact-point math TireSkidMarks.cs uses (ground point + small normal
            // offset), so smoke and marks originate from the exact same spot
            Vector3 contactPoint = hit.point + hit.normal * groundOffset;
            ps.transform.position = contactPoint;

            // FIX (alignment): point the emitter opposite the wheel's velocity so smoke
            // trails backward along the skid mark instead of spreading sideways/around
            // the car. Falls back to keeping current rotation if velocity is negligible
            // (e.g. wheel momentarily stationary) to avoid a jittery LookRotation flip.
            if (orientAlongVelocity && wheel.attachedRigidbody != null)
            {
                Vector3 vel = wheel.attachedRigidbody.velocity;
                if (vel.sqrMagnitude > 0.1f)
                {
                    ps.transform.rotation = Quaternion.LookRotation(-vel.normalized, hit.normal);
                }
            }
        }
        else
        {
            // fallback if airborne / no ground hit - behave like the old Awake() placement
            ps.transform.position = wheel.transform.position - Vector3.up * wheel.radius;
        }
    }

    // Exposed so other scripts (e.g. TireSkidMarks.cs, if it wants to mirror this exact
    // gate instead of recomputing it) can check whether smoke/drift is currently active.
    public bool IsDrifting => drifting;

    private void SetActive(ParticleSystem ps, bool active)
    {
        if (ps == null) return;

        if (active && !ps.isEmitting)
        {
            ps.Play();
        }
        else if (!active && ps.isEmitting)
        {
            // FIX: using StopEmitting (no Clear) left the system's internal loop-cycle
            // timer wherever it happened to be when released. Since Looping is ON with
            // a short Duration, resuming with Play() afterward could land mid-cycle and
            // never re-trigger emission again - this is why it worked once at the start
            // then went silent. Fully stopping + clearing here guarantees every future
            // Play() restarts the cycle cleanly from time zero.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    // FIX: this was defined as chectDrift() but never actually called from anywhere,
    // so this logic never ran at all. Renamed and wired into LateUpdate() above, and now
    // takes the already-computed shouldDrift bool instead of the nonexistent IM.
    // Now only handles the skid sound - tire marks are TireSkidMarks.cs's job.
    private void CheckDrift(bool shouldDrift)
    {
        if (shouldDrift) StartDrift();
        else StopDrift();
    }

    private void StartDrift()
    {
        if (drifting) return;

        if (skidClip != null && !skidClip.isPlaying) skidClip.Play();

        drifting = true;
    }

    private void StopDrift()
    {
        if (!drifting) return;

        if (skidClip != null && skidClip.isPlaying) skidClip.Stop();

        drifting = false;
    }
}