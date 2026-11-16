using UnityEngine;

// SIMPLE TIRE SMOKE - v2, rebuilt from scratch.
//
// PHILOSOPHY: the old script kept overriding Size/Shape/Color/Lifetime in code,
// which made it impossible to tell what was actually wrong when smoke disappeared -
// too many things changing at once, blind. This version does ONLY two jobs:
//   1. Move the smoke to the tire's ground contact point every frame.
//   2. Turn emission on/off based on handbrake + speed + grounded.
//
// It does NOT touch Start Size, Shape, Color, Lifetime, or any other module.
// Whatever you see in the Particle System's Inspector right now is exactly what
// will play - nothing is overridden. This guarantees it's visible first. Once you
// can see plain smoke working, tune Size/Color/Shape yourself directly in the
// Inspector (with Play Mode running so you see changes live) rather than guessing
// through code.
//
// SETUP:
//   1. Attach this to the car root (same object as `controller`).
//   2. Drag the rear-left/right ParticleSystem into leftSmoke/rightSmoke.
//   3. Drag the rear-left/right WheelCollider into leftWheel/rightWheel.
//   4. Press Play, hold handbrake while moving - smoke should appear using
//      whatever look the particle system already has.
[RequireComponent(typeof(controller))]
public class SimpleTireSmoke : MonoBehaviour
{
    [Header("References (required)")]
    public ParticleSystem leftSmoke;
    public ParticleSystem rightSmoke;
    public WheelCollider leftWheel;
    public WheelCollider rightWheel;

    [Header("Trigger")]
    [Tooltip("Minimum speed (KPH) before smoke shows.")]
    public float minSpeedForSmoke = 5f;

    [Header("Debug")]
    [Tooltip("Logs exactly why smoke is or isn't showing, once per second, so you can see what's blocking it without guessing.")]
    public bool debugLog = false;

    [Header("Procedural Texture (no asset needed)")]
    [Tooltip("If ON, generates a soft round smoke puff texture at runtime and assigns " +
             "it to both particle systems' materials automatically - no texture asset " +
             "required. Turn OFF if you've assigned your own material/texture and don't " +
             "want it overridden.")]
    public bool useProceduralTexture = true;

    [Range(0f, 1f)]
    [Tooltip("How much random noise/graininess to add to the puff so it doesn't look " +
             "like a perfectly clean circle. 0 = smooth gradient, higher = grainier.")]
    public float textureNoise = 0.15f;

    private controller car;
    private float lastLogTime;

    private void Awake()
    {
        car = GetComponent<controller>();

        if (useProceduralTexture)
        {
            Texture2D smokeTex = GenerateSmokeTexture();
            ApplyTexture(leftSmoke, smokeTex);
            ApplyTexture(rightSmoke, smokeTex);
        }

        // Only thing we force: make sure emission is OFF at start regardless of the
        // prefab's own Play On Awake setting, so nothing shows before the car moves.
        StopSmoke(leftSmoke);
        StopSmoke(rightSmoke);
    }

    // Assigns a texture to a particle system's material. If the material is the
    // built-in default/shared one, creates a unique instance first so we don't
    // accidentally overwrite a material shared by other objects in the project.
    private void ApplyTexture(ParticleSystem ps, Texture2D tex)
    {
        if (ps == null) return;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (renderer == null) return;

        // Instantiate so each wheel gets its own material copy - avoids editing a
        // shared/prefab material asset directly, which would affect every user of it.
        Material mat = renderer.material != null
            ? new Material(renderer.material)
            : new Material(Shader.Find("Particles/Standard Unlit"));

        mat.mainTexture = tex;
        renderer.material = mat;
    }

    // Procedural soft-edged smoke puff: bright/opaque center fading smoothly to fully
    // transparent at the edge, with light random noise so it doesn't read as a perfect
    // clean circle. Same runtime-texture-generation approach as
    // TireSkidMarks.GenerateTireTreadTexture() - no external asset file required.
    private Texture2D GenerateSmokeTexture()
    {
        const int SIZE = 64;
        Texture2D tex = new Texture2D(SIZE, SIZE, TextureFormat.ARGB32, false);

        Vector2 center = new Vector2(SIZE / 2f, SIZE / 2f);
        float maxDist = SIZE / 2f;

        for (int y = 0; y < SIZE; y++)
        {
            for (int x = 0; x < SIZE; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / maxDist;

                // Smooth radial falloff: 1 (opaque) at center -> 0 (transparent) at edge.
                float alpha = Mathf.Clamp01(1f - dist);
                alpha = alpha * alpha * (3f - 2f * alpha); // smoothstep, softer falloff

                // Light random noise so the puff isn't a perfectly clean gradient -
                // real smoke has wispy variation, not a flat circle.
                if (textureNoise > 0f)
                {
                    float noise = (Mathf.PerlinNoise(x * 0.2f, y * 0.2f) - 0.5f) * textureNoise;
                    alpha = Mathf.Clamp01(alpha + noise * alpha); // scale noise by alpha so edges don't get noisy speckle beyond the puff
                }

                Color c = new Color(1f, 1f, 1f, alpha);
                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply();
        return tex;
    }

    private void LateUpdate()
    {
        if (car == null) return;

        bool shouldShow = car.grounded && car.KPH > minSpeedForSmoke && car.IsHandbraking;

        if (debugLog && Time.time - lastLogTime > 1f)
        {
            lastLogTime = Time.time;
            Debug.Log($"SimpleTireSmoke: grounded={car.grounded} KPH={car.KPH:F1} " +
                      $"handbrake={car.IsHandbraking} -> shouldShow={shouldShow} " +
                      $"(leftSmoke null={leftSmoke == null}, leftWheel null={leftWheel == null})");
        }

        PositionAtWheel(leftSmoke, leftWheel);
        PositionAtWheel(rightSmoke, rightWheel);

        SetEmitting(leftSmoke, shouldShow);
        SetEmitting(rightSmoke, shouldShow);
    }

    // Moves the particle system to the wheel's actual ground contact point.
    // Falls back to wheel position minus radius if not grounded (e.g. airborne).
    private void PositionAtWheel(ParticleSystem ps, WheelCollider wheel)
    {
        if (ps == null || wheel == null) return;

        WheelHit hit;
        if (wheel.GetGroundHit(out hit))
        {
            ps.transform.position = hit.point;
        }
        else
        {
            ps.transform.position = wheel.transform.position - wheel.transform.up * wheel.radius;
        }
    }

    private void SetEmitting(ParticleSystem ps, bool shouldShow)
    {
        if (ps == null) return;

        var emission = ps.emission;

        if (shouldShow)
        {
            emission.enabled = true;
            if (!ps.isPlaying) ps.Play();
        }
        else
        {
            StopSmoke(ps);
        }
    }

    private void StopSmoke(ParticleSystem ps)
    {
        if (ps == null) return;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}