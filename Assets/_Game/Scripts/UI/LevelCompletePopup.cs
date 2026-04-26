using Ape.Core;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Modal that appears when the level is solved. Dark transparent backdrop, three star icons
/// animated with DOTween, and a "Next Level" button. Driven by <see cref="GameUI"/>.
/// </summary>
public sealed class LevelCompletePopup : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform contentRoot;
    [SerializeField] private Button nextLevelButton;

    [Header("Stars")]
    [Tooltip("Three star icons in left-to-right order. Tinted to earnedStarColor when earned, emptyStarColor otherwise. Their RectTransforms are scaled in for the win animation.")]
    [SerializeField] private Image[] starIcons = new Image[3];
    [SerializeField] private Color earnedStarColor = new Color(1f, 0.85f, 0.25f, 1f);
    [SerializeField] private Color emptyStarColor = new Color(1f, 1f, 1f, 0.18f);

    [Header("Optional Stats Labels")]
    [SerializeField] private TMP_Text movesLabel;
    [SerializeField] private TMP_Text parLabel;

    [Header("Animation")]
    [SerializeField] private float backdropFadeDuration = 0.25f;
    [SerializeField] private float popupPopDuration = 0.35f;
    [SerializeField] private Ease popupPopEase = Ease.OutBack;
    [SerializeField] private float starInterval = 0.18f;
    [SerializeField] private float starPopDuration = 0.42f;
    [SerializeField] private Ease starEase = Ease.OutBack;

    [Header("Audio")]
    [Tooltip("SoundManager clip name played when each star pops in. Empty = no sound.")]
    [SerializeField] private string starPopSound = "pop";
    [Tooltip("SoundManager clip name played when Next Level is pressed. Empty = no sound.")]
    [SerializeField] private string nextLevelSound = "pop";

    private HexSortManager manager;
    private Sequence sequence;

    private void Awake()
    {
        if (nextLevelButton != null)
        {
            nextLevelButton.onClick.AddListener(OnNextLevelClicked);
        }
        Hide();
    }

    private void OnDestroy()
    {
        if (nextLevelButton != null)
        {
            nextLevelButton.onClick.RemoveListener(OnNextLevelClicked);
        }
        sequence?.Kill();
    }

    public void Show(LevelResult result, HexSortManager owner)
    {
        manager = owner;
        gameObject.SetActive(true);

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = true;
        }
        if (contentRoot != null)
        {
            contentRoot.localScale = Vector3.one * 0.7f;
        }
        if (movesLabel != null)
        {
            movesLabel.text = $"Moves: {result.Moves}";
        }
        if (parLabel != null)
        {
            parLabel.text = result.ParMoves > 0 ? $"Par: {result.ParMoves}" : string.Empty;
        }

        // Reset stars: scaled to 0 (popped in by the animation), tinted by earned-or-not.
        int earned = Mathf.Clamp(result.Stars, 0, starIcons.Length);
        for (int i = 0; i < starIcons.Length; i++)
        {
            Image icon = starIcons[i];
            if (icon == null)
            {
                continue;
            }
            icon.transform.localScale = Vector3.zero;
            icon.color = i < earned ? earnedStarColor : emptyStarColor;
        }

        sequence?.Kill();
        sequence = DOTween.Sequence().SetUpdate(true);

        if (canvasGroup != null)
        {
            sequence.Append(canvasGroup.DOFade(1f, backdropFadeDuration));
        }
        if (contentRoot != null)
        {
            sequence.Join(contentRoot.DOScale(1f, popupPopDuration).SetEase(popupPopEase));
        }

        for (int i = 0; i < starIcons.Length; i++)
        {
            Image icon = starIcons[i];
            if (icon == null)
            {
                continue;
            }
            sequence.AppendInterval(starInterval);
            sequence.AppendCallback(PlayStarSound);
            sequence.Append(icon.transform.DOScale(1f, starPopDuration).SetEase(starEase));
        }

        sequence.AppendCallback(() =>
        {
            if (canvasGroup != null)
            {
                canvasGroup.interactable = true;
            }
        });
    }

    public void Hide()
    {
        sequence?.Kill();
        sequence = null;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        gameObject.SetActive(false);
    }

    private void OnNextLevelClicked()
    {
        TryPlaySfx(nextLevelSound);
        Hide();
        if (manager != null)
        {
            manager.LoadNextLevel();
        }
    }

    private void PlayStarSound()
    {
        TryPlaySfx(starPopSound);
    }

    private static void TryPlaySfx(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }
        if (App.Sound == null)
        {
            Debug.LogWarning($"LevelCompletePopup: cannot play '{name}' — App.Sound is null.");
            return;
        }
        App.Sound.PlaySound(name, isUI: true);
    }
}
