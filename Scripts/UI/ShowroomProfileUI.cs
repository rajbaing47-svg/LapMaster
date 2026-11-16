using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Drop this on the Player/HUD info panel inside your Showroom scene (the one
// ShowroomManager already reserves space for - level/currency/settings).
// Reads from PlayerProfile (name + lifetime-XP badge tier) and PlayerEconomy
// (level + currency + XP-to-next-level), and refreshes on enable so it's always
// current when the player returns to the Showroom after a race.
public class ShowroomProfileUI : MonoBehaviour
{
    [Header("Name / Level")]
    public TMP_Text usernameText;
    public TMP_Text levelText;
    public TMP_Text currencyText;

    [Header("Badge (driven by PlayerProfile.CurrentBadgeIndex)")]
    [Tooltip("One sprite per tier in PlayerProfile.Tiers, same order: Rookie, Bronze, Silver, Gold, Legend.")]
    public Sprite[] badgeSprites;
    public Image badgeIcon;
    public TMP_Text badgeNameText;

    [Header("Lifetime XP Bar (progress toward next badge)")]
    public Image badgeProgressFill;
    public TMP_Text badgeProgressText; // e.g. "1,850 / 8,000 XP"

    [Header("Level XP Bar (progress toward next level - optional, separate from badge)")]
    public Image levelProgressFill;
    public TMP_Text levelProgressText; // e.g. "1,850 / 3,000 XP"

    private void OnEnable()
    {
        Refresh();
    }

    // Call this manually too (e.g. right after ScoreManager awards XP and the
    // player returns to the Showroom) if OnEnable alone doesn't cover your flow.
    public void Refresh()
    {
        if (usernameText != null)
            usernameText.text = PlayerProfile.Username.ToUpper();

        if (levelText != null)
            levelText.text = "LV " + PlayerEconomy.Level;

        if (currencyText != null)
            currencyText.text = PlayerEconomy.Currency.ToString("N0");

        RefreshBadge();
        RefreshLevelBar();
    }

    private void RefreshBadge()
    {
        int index = PlayerProfile.CurrentBadgeIndex;

        if (badgeIcon != null)
        {
            Sprite sprite = (badgeSprites != null && index < badgeSprites.Length) ? badgeSprites[index] : null;
            badgeIcon.sprite = sprite;
            badgeIcon.enabled = sprite != null;
        }

        if (badgeNameText != null)
            badgeNameText.text = PlayerProfile.CurrentBadgeName;

        if (badgeProgressFill != null)
            badgeProgressFill.fillAmount = PlayerProfile.ProgressToNextBadge;

        if (badgeProgressText != null)
        {
            bool maxTier = index >= PlayerProfile.Tiers.Length - 1;
            if (maxTier)
            {
                badgeProgressText.text = "MAX TIER";
            }
            else
            {
                int currentFloor = PlayerProfile.Tiers[index].xpRequired;
                int nextCeiling = PlayerProfile.Tiers[index + 1].xpRequired;
                int xp = PlayerProfile.LifetimeXP;
                badgeProgressText.text = $"{(xp - currentFloor):N0} / {(nextCeiling - currentFloor):N0} XP";
            }
        }
    }

    private void RefreshLevelBar()
    {
        if (levelProgressFill != null)
        {
            float t = (float)PlayerEconomy.XP / PlayerEconomy.XPToNextLevel;
            levelProgressFill.fillAmount = Mathf.Clamp01(t);
        }

        if (levelProgressText != null)
            levelProgressText.text = $"{PlayerEconomy.XP:N0} / {PlayerEconomy.XPToNextLevel:N0} XP";
    }
}