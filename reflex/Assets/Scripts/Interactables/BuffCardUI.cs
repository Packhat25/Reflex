using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class BuffCardUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private RewardManager manager;
    private BuffCardData cardData;

    public void Setup(BuffCardData data)
    {
        cardData = data;

        nameText.text = data.cardName;
        descriptionText.text = data.description;
    }

    public void ClearBuffText()
    {
        cardData = null;
        nameText.text = "";
        descriptionText.text = "";
    }

    public void OnCardClicked()
    {
        if (cardData == null)
        {
            return;
        }

        if (manager == null)
        {
            manager = RewardManager.Instance;
            if (manager == null)
            {
                manager = FindFirstObjectByType<RewardManager>();
            }
        }

        if (manager == null)
        {
            Debug.LogWarning("BuffCardUI click ignored because no RewardManager instance is available.");
            return;
        }

        manager.SelectCard(cardData);
    }
}
