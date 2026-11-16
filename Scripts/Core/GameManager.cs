using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class GameManager : MonoBehaviour
{
    public controller RR;
    public TMP_Text Kph;
    public TMP_Text gearNum;
    public vehicleList list;

    [Header("Speed Indicator (Radial Fill Arc)")]
    public Image speedometerArc;
    public RectTransform arcContainer;
    public float maxSpeed = 200f;

    [Header("Arc Visual Settings")]
    public float arcLineWidth = 8f;
    public Material arcMaterial;

    [Header("Redline Effect")]
    public Image speedoDial;
    public Color normalColor = Color.white;
    public Color redlineColor = new Color(0.35f, 0f, 0f, 1f);
    public Color dialNormalColor = Color.white;
    public float redlineRange = 40f;

    [Header("Redline Switch (triggers past node index)")]
    public int redlineNodeIndex = 4;

    [Header("Color Transition")]
    public float colorTransitionSpeed = 2f; // Lower = slower fade, Higher = faster

    [Header("Gear Blink Effect")]
    public float gearBlinkDuration = 1.5f;  // Total blink time in seconds
    public float gearBlinkRate = 0.15f;     // How fast it blinks (seconds per toggle)
    public Color gearBlinkColor = Color.yellow;

    public GameObject StartPosition;

    private RectTransform[] nodes;
    private float[] nodeAngles;

    // Runtime state
    private Color _currentArcColor;
    private Color _currentDialColor;
    private bool _isBlinking = false;

    private void Start()
    {
        if (arcContainer != null)
        {
            nodes = arcContainer.GetComponentsInChildren<RectTransform>();
            System.Collections.Generic.List<RectTransform> nodeList = new System.Collections.Generic.List<RectTransform>();
            for (int i = 0; i < nodes.Length; i++)
                if (nodes[i] != arcContainer)
                    nodeList.Add(nodes[i]);
            nodes = nodeList.ToArray();
        }

        CacheNodeAngles();

        // Initialise tracked colours
        _currentArcColor  = normalColor;
        _currentDialColor = dialNormalColor;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            Debug.LogError("GameManager: no GameObject tagged 'Player' found.");
            return;
        }

        RR = player.GetComponent<controller>();
        if (RR == null)
        {
            Debug.LogError($"GameManager: '{player.name}' is tagged 'Player' but has no controller component.");
            return;
        }

        // RaceManager applies showroom stats in Awake before this Start, so this is the car's real cap.
        float carMax = RR.CurrentMaxSpeedKph;
        if (carMax > 0f)
            maxSpeed = carMax;
    }

    private void CacheNodeAngles()
    {
        if (nodes == null || nodes.Length < 2) return;
        nodeAngles = new float[nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            Vector2 pos = nodes[i].anchoredPosition;
            nodeAngles[i] = Mathf.Atan2(pos.y, pos.x) * Mathf.Rad2Deg;
        }
    }

    private void FixedUpdate()
    {
        Kph.text = RR.KPH.ToString("0");
        updateRadialArc();
        updateRedline();
    }

    public void updateRadialArc()
    {
        if (speedometerArc == null || nodes == null || nodes.Length < 2 || nodeAngles == null) return;

        float clampedSpeed = Mathf.Clamp(RR.KPH, 0f, maxSpeed);
        speedometerArc.fillAmount = clampedSpeed / maxSpeed;
    }

    private float GetNodeSpeedThreshold(int nodeIndex)
    {
        if (nodes == null || nodes.Length < 2)
            return maxSpeed - redlineRange;

        int segments = nodes.Length - 1;
        float t = Mathf.Clamp01((float)nodeIndex / segments);
        return t * maxSpeed;
    }

    public void updateRedline()
    {
        // Skip colour update while gear-blink coroutine is running
        if (_isBlinking) return;

        float blinkThreshold = GetNodeSpeedThreshold(redlineNodeIndex);
        bool pastNode = RR.KPH >= blinkThreshold;

        Color targetArc  = pastNode ? redlineColor   : normalColor;
        Color targetDial = pastNode ? redlineColor    : dialNormalColor;

        // Smoothly lerp toward the target colour each frame
        _currentArcColor  = Color.Lerp(_currentArcColor,  targetArc,  Time.deltaTime * colorTransitionSpeed);
        _currentDialColor = Color.Lerp(_currentDialColor, targetDial, Time.deltaTime * colorTransitionSpeed);

        if (speedometerArc != null) speedometerArc.color = _currentArcColor;
        if (speedoDial      != null) speedoDial.color     = _currentDialColor;
    }

    public void changeGear()
    {
        gearNum.text = (!RR.reverse) ? (RR.gearNum + 1).ToString() : "R";
        TriggerGearBlink();
    }

    private void TriggerGearBlink()
    {
        if (_isBlinking) StopCoroutine("GearBlinkCoroutine");
        StartCoroutine("GearBlinkCoroutine");
    }

    private IEnumerator GearBlinkCoroutine()
    {
        _isBlinking = true;
        float elapsed = 0f;
        bool toggle   = false;

        while (elapsed < gearBlinkDuration)
        {
            Color blinkA = toggle ? gearBlinkColor : normalColor;
            Color blinkB = toggle ? gearBlinkColor : dialNormalColor;

            if (speedometerArc != null) speedometerArc.color = blinkA;
            if (speedoDial      != null) speedoDial.color     = blinkB;

            toggle   = !toggle;
            elapsed += gearBlinkRate;
            yield return new WaitForSeconds(gearBlinkRate);
        }

        // Snap tracked colours to where redline logic would have them, then let lerp take over
        float threshold = GetNodeSpeedThreshold(redlineNodeIndex);
        bool  inRedline = RR.KPH >= threshold;
        _currentArcColor  = inRedline ? redlineColor : normalColor;
        _currentDialColor = inRedline ? redlineColor : dialNormalColor;

        _isBlinking = false;
    }
}