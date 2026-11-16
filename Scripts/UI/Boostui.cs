using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to a UI object. Drag in the player's "controller" script and a fillable Image
/// (Image Type = Filled) plus optionally a Text label. Shows boost as a radial/bar fill
/// that drains while boost is on cooldown, and flips color/text when ready.
/// </summary>
public class BoostUI : MonoBehaviour
{
    [Header("References")]
    public controller playerCar;      // drag the car's controller component here
    public Image fillImage;           // Image Type: Filled (Radial 360 or Horizontal)
    public Text statusText;           // optional, shows "READY" / "3.2s"

    [Header("Colors")]
    public Color readyColor = Color.cyan;
    public Color cooldownColor = Color.gray;

    void Update()
    {
        if (playerCar == null) return;

        bool ready = !playerCar.IsBoostOnCooldown;

        if (fillImage != null)
        {
            // Fill goes from 0 (just used) back up to 1 (ready)
            fillImage.fillAmount = playerCar.BoostCooldownProgress01;
            fillImage.color = ready ? readyColor : cooldownColor;
        }

        if (statusText != null)
        {
            statusText.text = ready
                ? "BOOST READY"
                : $"{playerCar.BoostCooldownRemaining:F1}s";
        }
    }
}