using UnityEngine;

/// <summary>
/// Attach this script to the MiniCar child object on every car prefab.
///
/// At runtime, it walks up to the car's root GameObject, reads its tag
/// (set by RaceManager to "Player" or "AICar"), and paints this MiniCar
/// object accordingly:
///
///   Player  →  Orange  (Color.#FF8C00 - easy to spot on any map colour)
///   AI      →  Red     (Color.red)
///   Unknown →  White   (fallback, so something always shows up)
///
/// No manual colour changes needed on prefabs - selecting a different car
/// in the Showroom is handled automatically because RaceManager always
/// re-instantiates the correct prefab and re-tags it before this Start()
/// runs.
///
/// HOW TO SET UP:
///   1. On each car prefab find the child GameObject whose tag is "MiniCar"
///      (the small icon shown on the minimap / overhead camera).
///   2. Add this component (MiniCarColorSetup) to that child.
///   3. Done - no Inspector fields to fill in.
///
/// OPTIONAL TWEAKS:
///   Adjust playerColor / aiColor below, or expose them in the Inspector
///   if you want to change them without recompiling.
/// </summary>
public class MiniCarColorSetup : MonoBehaviour
{
    // ── Colour definitions ────────────────────────────────────────────────
    // Change these freely; nothing in the rest of the project depends on them.

    [Tooltip("Colour applied to the player's MiniCar icon on the minimap.")]
    public Color playerColor = new Color(1f, 0.55f, 0f, 1f);   // vivid orange

    [Tooltip("Colour applied to every AI car's MiniCar icon on the minimap.")]
    public Color aiColor    = Color.red;

    [Tooltip("Fallback colour used when the parent car has neither 'Player' nor 'AICar' tag (shouldn't normally happen).")]
    public Color unknownColor = Color.white;

    // ── Bootstrap ─────────────────────────────────────────────────────────

    private void Start()
    {
        Color chosen = ResolveColor();
        ApplyColor(chosen);
    }

    // ── Internal helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Walks up the hierarchy to find the first ancestor (or self) tagged
    /// "Player" or "AICar" and returns the matching colour.
    /// Falls back to unknownColor if neither tag is found.
    /// </summary>
    private Color ResolveColor()
    {
        // Start from this GameObject and climb toward the scene root.
        Transform current = transform;

        while (current != null)
        {
            if (current.CompareTag("Player"))
            {
                return playerColor;
            }

            if (current.CompareTag("AICar"))
            {
                return aiColor;
            }

            current = current.parent;
        }

        // No matching tag found anywhere up the chain.
        Debug.LogWarning(
            $"[MiniCarColorSetup] '{gameObject.name}' could not find a parent tagged " +
            $"'Player' or 'AICar'. Applying unknownColor instead. " +
            $"Make sure RaceManager has tagged the root car GameObject before this Start() runs.",
            gameObject
        );

        return unknownColor;
    }

    /// <summary>
    /// Sets <paramref name="color"/> on every Renderer found on this
    /// GameObject and all of its children. Works for MeshRenderer,
    /// SpriteRenderer, and any other Renderer subclass.
    ///
    /// Uses MaterialPropertyBlock so each car instance gets its own colour
    /// without creating new Material assets (no memory leak, no
    /// "are you sure?" leak warnings in the Editor).
    /// </summary>
    private void ApplyColor(Color color)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(includeInactive: true);

        if (renderers.Length == 0)
        {
            Debug.LogWarning(
                $"[MiniCarColorSetup] '{gameObject.name}' has no Renderer components " +
                $"(including children). Nothing to colour.",
                gameObject
            );
            return;
        }

        MaterialPropertyBlock block = new MaterialPropertyBlock();

        foreach (Renderer rend in renderers)
        {
            // Preserve any existing properties set on this renderer,
            // then override only the colour.
            rend.GetPropertyBlock(block);
            block.SetColor("_Color", color);
            // Also set _BaseColor for URP / HDRP Lit shaders.
            block.SetColor("_BaseColor", color);
            rend.SetPropertyBlock(block);
        }

        Debug.Log(
            $"[MiniCarColorSetup] '{gameObject.name}' → colour set to {color} " +
            $"across {renderers.Length} renderer(s).",
            gameObject
        );
    }
}