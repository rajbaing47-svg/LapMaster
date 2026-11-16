using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LinearSpeedometerUI : MonoBehaviour
{
    [Header("Speed References")]
    public GameObject Player;
    private controller RR; // Your racing controller script
    public float maxDisplaySpeed = 200f; // Max speed to display on the bar (0-10 scale in UI, but actual KPH)
    
    [Header("UI Components")]
    [Tooltip("The RectTransform of the background bar (0-10 label positions)")]
    public RectTransform speedBarBackground;
    
    [Tooltip("The small white needle/indicator that moves across the bar")]
    public RectTransform speedNeedle;
    
    [Tooltip("The fill line that grows from left to right with speed")]
    public Image speedFillLine;
    
    [Tooltip("Text to show current speed in KPH (optional)")]
    public Text speedText;

    [Header("Needle Settings")]
    [Range(0, 2)] public float needleFollowSpeed = 0.15f; // How fast needle catches up to target position
    private float targetNeedlePosition = 0f;
    private float currentNeedlePosition = 0f;

    [Header("Fill Settings")]
    [Range(0, 2)] public float fillFollowSpeed = 0.1f; // How fast the fill line grows/shrinks
    private float targetFillAmount = 0f;
    private float currentFillAmount = 0f;

    public void Start()
    {
        Player = GameObject.FindGameObjectWithTag("Player");
        if (Player == null)
        {
            Debug.LogError("LinearSpeedometerUI: No GameObject tagged 'Player' found.");
            return;
        }

        RR = Player.GetComponent<controller>();
        if (RR == null)
        {
            Debug.LogError("LinearSpeedometerUI: Player object has no 'controller' component.");
            return;
        }

        // Guard checks for UI components
        if (speedBarBackground == null)
            Debug.LogWarning("LinearSpeedometerUI: speedBarBackground not assigned - speedometer won't display properly.");
        if (speedNeedle == null)
            Debug.LogWarning("LinearSpeedometerUI: speedNeedle not assigned - needle won't move.");
        if (speedFillLine == null)
            Debug.LogWarning("LinearSpeedometerUI: speedFillLine not assigned - fill won't display.");
    }

    private void Update()
    {
        if (Player == null || RR == null) return;

        UpdateSpeedometer();
    }

    private void UpdateSpeedometer()
    {
        // Calculate target position: 0-10 scale mapped to the bar width
        // KPH / maxDisplaySpeed = normalized speed (0 to 1)
        float normalizedSpeed = Mathf.Clamp01(RR.KPH / maxDisplaySpeed);
        
        // Map to 0-10 scale for display
        float speedScale = normalizedSpeed * 10f;
        
        // Position needle across the bar
        // Get the bar width in pixels - you'll set this based on your UI layout
        float barWidth = speedBarBackground != null ? speedBarBackground.rect.width : 400f;
        targetNeedlePosition = (speedScale / 10f) * barWidth;
        
        // Smoothly interpolate needle position
        currentNeedlePosition = Mathf.Lerp(currentNeedlePosition, targetNeedlePosition, needleFollowSpeed * Time.deltaTime);
        
        // Update needle position
        if (speedNeedle != null)
        {
            Vector2 needlePos = speedNeedle.anchoredPosition;
            needlePos.x = currentNeedlePosition;
            speedNeedle.anchoredPosition = needlePos;
        }

        // Update fill line
        targetFillAmount = normalizedSpeed;
        currentFillAmount = Mathf.Lerp(currentFillAmount, targetFillAmount, fillFollowSpeed * Time.deltaTime);
        
        if (speedFillLine != null)
        {
            speedFillLine.fillAmount = currentFillAmount;
        }

        // Update speed text (optional)
        if (speedText != null)
        {
            speedText.text = Mathf.RoundToInt(RR.KPH).ToString("D3");
        }
    }
}