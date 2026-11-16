using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CircularSpeedometerUI : MonoBehaviour
{
    [Header("Speed References")]
    public GameObject Player;
    private controller RR; // Your racing controller
    public float maxDisplaySpeed = 200f; // Max speed (0-10 on dial)

    [Header("UI Components")]
    [Tooltip("The circular background image (gauge dial)")]
    public Image speedometerDial;
    
    [Tooltip("The fill image that grows in circular arc (white bar)")]
    public Image speedFillArc;
    
    [Tooltip("The needle image that rotates")]
    public Image speedNeedle;
    
    [Tooltip("Text showing current speed KPH")]
    public Text speedText;

    [Header("Circular Settings")]
    [Tooltip("Max rotation angle (e.g., 270 degrees for 3/4 circle)")]
    public float maxRotationAngle = 270f;
    
    [Tooltip("Starting rotation angle (usually -135 for left side start)")]
    public float startRotationAngle = -135f;
    
    [Range(0, 2)] public float needleFollowSpeed = 0.15f;
    [Range(0, 2)] public float fillFollowSpeed = 0.1f;

    private float currentNeedleRotation = 0f;
    private float targetNeedleRotation = 0f;
    private float currentFillAmount = 0f;
    private float targetFillAmount = 0f;

    public void Start()
    {
        Player = GameObject.FindGameObjectWithTag("Player");
        if (Player == null)
        {
            Debug.LogError("CircularSpeedometerUI: No GameObject tagged 'Player' found.");
            return;
        }

        RR = Player.GetComponent<controller>();
        if (RR == null)
        {
            Debug.LogError("CircularSpeedometerUI: Player has no 'controller' component.");
            return;
        }

        // Guard checks
        if (speedometerDial == null)
            Debug.LogWarning("CircularSpeedometerUI: speedometerDial not assigned.");
        if (speedFillArc == null)
            Debug.LogWarning("CircularSpeedometerUI: speedFillArc not assigned.");
        if (speedNeedle == null)
            Debug.LogWarning("CircularSpeedometerUI: speedNeedle not assigned.");

        // Initialize rotation
        if (speedNeedle != null)
            currentNeedleRotation = startRotationAngle;
    }

    private void Update()
    {
        if (Player == null || RR == null) return;
        UpdateCircularSpeedometer();
    }

    private void UpdateCircularSpeedometer()
    {
        // Normalize speed (0 to 1)
        float normalizedSpeed = Mathf.Clamp01(RR.KPH / maxDisplaySpeed);

        // Calculate target needle rotation
        // Map 0-1 speed to the rotation range
        targetNeedleRotation = startRotationAngle + (normalizedSpeed * maxRotationAngle);

        // Smoothly interpolate needle rotation
        currentNeedleRotation = Mathf.Lerp(currentNeedleRotation, targetNeedleRotation, needleFollowSpeed * Time.deltaTime);

        // Apply rotation to needle
        if (speedNeedle != null)
        {
            speedNeedle.transform.rotation = Quaternion.AngleAxis(currentNeedleRotation, Vector3.forward);
        }

        // Update fill arc
        targetFillAmount = normalizedSpeed;
        currentFillAmount = Mathf.Lerp(currentFillAmount, targetFillAmount, fillFollowSpeed * Time.deltaTime);

        if (speedFillArc != null)
        {
            speedFillArc.fillAmount = currentFillAmount;
        }

        // Update speed text
        if (speedText != null)
        {
            speedText.text = Mathf.RoundToInt(RR.KPH).ToString("D3");
        }
    }

    // Public methods to adjust settings if needed
    public void SetMaxSpeed(float newMaxSpeed)
    {
        maxDisplaySpeed = Mathf.Max(newMaxSpeed, 1f);
    }

    public void SetRotationRange(float startAngle, float maxAngle)
    {
        startRotationAngle = startAngle;
        maxRotationAngle = maxAngle;
    }
}