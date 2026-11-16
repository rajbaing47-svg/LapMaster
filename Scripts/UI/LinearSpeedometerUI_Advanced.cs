using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LinearSpeedometerUI_Advanced : MonoBehaviour
{
    [Header("Speed References")]
    public GameObject Player;
    private controller RR;
    public float maxDisplaySpeed = 200f;
    
    [Header("UI Components")]
    public RectTransform speedBarBackground;
    public RectTransform speedNeedle;
    public Image speedFillLine;
    public Text speedText;
    public Text speedLabelMax; // "10" label

    [Header("Needle Settings")]
    [Range(0, 2)] public float needleFollowSpeed = 0.15f;
    private float targetNeedlePosition = 0f;
    private float currentNeedlePosition = 0f;

    [Header("Fill Settings")]
    [Range(0, 2)] public float fillFollowSpeed = 0.1f;
    private float targetFillAmount = 0f;
    private float currentFillAmount = 0f;

    [Header("Visual Feedback")]
    [Tooltip("Adds slight scale pulse when accelerating hard")]
    public bool enableSpeedPulse = true;
    [Range(0.05f, 0.3f)] public float pulseAmount = 0.1f;
    private Vector3 originalNeedleScale;

    [Header("Debug")]
    public bool showDebugInfo = false;

    private float barWidthInPixels = 0f;

    public void Start()
    {
        Player = GameObject.FindGameObjectWithTag("Player");
        if (Player == null)
        {
            Debug.LogError("LinearSpeedometerUI_Advanced: No GameObject tagged 'Player' found.");
            return;
        }

        RR = Player.GetComponent<controller>();
        if (RR == null)
        {
            Debug.LogError("LinearSpeedometerUI_Advanced: Player object has no 'controller' component.");
            return;
        }

        if (speedBarBackground == null)
        {
            Debug.LogError("LinearSpeedometerUI_Advanced: speedBarBackground not assigned!");
            return;
        }

        if (speedNeedle == null)
            Debug.LogWarning("LinearSpeedometerUI_Advanced: speedNeedle not assigned.");
        if (speedFillLine == null)
            Debug.LogWarning("LinearSpeedometerUI_Advanced: speedFillLine not assigned.");

        // Cache original needle scale for pulse effect
        if (speedNeedle != null)
            originalNeedleScale = speedNeedle.localScale;

        // Get bar width once
        barWidthInPixels = speedBarBackground.rect.width;
        
        if (showDebugInfo)
            Debug.Log($"LinearSpeedometerUI_Advanced initialized. Bar width: {barWidthInPixels}px, Max speed: {maxDisplaySpeed} KPH");
    }

    private void Update()
    {
        if (Player == null || RR == null) return;
        UpdateSpeedometer();
    }

    private void UpdateSpeedometer()
    {
        // Normalize speed to 0-1 range
        float normalizedSpeed = Mathf.Clamp01(RR.KPH / maxDisplaySpeed);
        
        // Calculate target needle position based on bar width
        targetNeedlePosition = normalizedSpeed * barWidthInPixels;

        // Smooth needle movement
        currentNeedlePosition = Mathf.Lerp(currentNeedlePosition, targetNeedlePosition, needleFollowSpeed * Time.deltaTime);

        // Update needle position (X axis only, left to right)
        if (speedNeedle != null)
        {
            Vector2 needlePos = speedNeedle.anchoredPosition;
            needlePos.x = currentNeedlePosition;
            speedNeedle.anchoredPosition = needlePos;

            // Optional: Add speed pulse effect
            if (enableSpeedPulse)
            {
                float pulse = 1f + (normalizedSpeed * pulseAmount);
                speedNeedle.localScale = originalNeedleScale * pulse;
            }
        }

        // Update fill line
        targetFillAmount = normalizedSpeed;
        currentFillAmount = Mathf.Lerp(currentFillAmount, targetFillAmount, fillFollowSpeed * Time.deltaTime);

        if (speedFillLine != null)
        {
            speedFillLine.fillAmount = currentFillAmount;
        }

        // Update speed text
        if (speedText != null)
        {
            speedText.text = Mathf.RoundToInt(RR.KPH).ToString("D3");
        }

        // Debug output
        if (showDebugInfo)
        {
            Debug.Log($"KPH: {RR.KPH:F1} | Normalized: {normalizedSpeed:F2} | Needle X: {currentNeedlePosition:F1} | Fill: {currentFillAmount:F2}");
        }
    }

    // Call this if you want to change max speed dynamically (e.g., different car tunes)
    public void SetMaxSpeed(float newMaxSpeed)
    {
        maxDisplaySpeed = newMaxSpeed;
        if (speedLabelMax != null && speedLabelMax.text != "10")
        {
            speedLabelMax.text = "10"; // Still shows 10, but maps to different KPH
        }
    }

    // Call this to change follow speeds on the fly (e.g., arcade vs sim mode)
    public void SetFollowSpeeds(float needleSpeed, float fillSpeed)
    {
        needleFollowSpeed = Mathf.Clamp01(needleSpeed);
        fillFollowSpeed = Mathf.Clamp01(fillSpeed);
    }
}