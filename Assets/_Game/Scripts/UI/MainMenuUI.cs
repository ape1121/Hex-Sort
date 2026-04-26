using Ape.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Main menu canvas shown inside the game scene. Has a Play button (with a label that shows
/// the current level number) and a Reset Progress button that clears the persistent profile.
/// </summary>
public sealed class MainMenuUI : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("References")]
    [Tooltip("Optional: HexSortManager. Auto-found if left null. Reset Progress tells the manager to transition to level 0 in-place so Play loads the reset state immediately.")]
    [SerializeField] private HexSortManager manager;

    [Header("Buttons")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button resetProgressButton;

    [Header("Play Button Label")]
    [Tooltip("Optional. Text component on the Play button — refreshed to show the current level when this menu shows.")]
    [SerializeField] private TMP_Text playButtonLabel;
    [Tooltip("Format string for the Play button label. {0} = current level number (1-based).")]
    [SerializeField] private string playLabelFormat = "Play Level {0}";

    [Header("Audio")]
    [Tooltip("SoundManager clip name played when the Play button is pressed. Empty = no sound.")]
    [SerializeField] private string playButtonSound = "pop";

    [Header("Events")]
    [Tooltip("Invoked after the menu hides itself. Wire this to GameUI / scene starter if needed; otherwise the player just sees the gameplay underneath.")]
    [SerializeField] private UnityEvent onPlay;
    [Tooltip("Invoked after persistent progress has been cleared. Useful to refresh other UI that displays progress.")]
    [SerializeField] private UnityEvent onResetProgress;

    private void Awake()
    {
        if (manager == null)
        {
            manager = FindFirstObjectByType<HexSortManager>(FindObjectsInactive.Include);
        }
        if (playButton != null)
        {
            playButton.onClick.AddListener(OnPlayClicked);
        }
        if (resetProgressButton != null)
        {
            resetProgressButton.onClick.AddListener(OnResetProgressClicked);
        }
    }

    private void OnEnable()
    {
        RefreshPlayLabel();
    }

    private void OnDestroy()
    {
        if (playButton != null)
        {
            playButton.onClick.RemoveListener(OnPlayClicked);
        }
        if (resetProgressButton != null)
        {
            resetProgressButton.onClick.RemoveListener(OnResetProgressClicked);
        }
    }

    public void Show()
    {
        gameObject.SetActive(true);
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
        RefreshPlayLabel();
    }

    public void Hide()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        gameObject.SetActive(false);
    }

    private void RefreshPlayLabel()
    {
        if (playButtonLabel == null)
        {
            return;
        }
        int savedLevel = App.Profile != null ? App.Profile.CurrentData.Level : 0;
        // Display 1-based for the player; SaveData.Level is the 0-based index of the next level to play.
        playButtonLabel.text = string.Format(playLabelFormat, savedLevel + 1);
    }

    private void OnPlayClicked()
    {
        TryPlaySfx(playButtonSound);
        Hide();
        if (manager != null && manager.IsInitialized)
        {
            manager.PlayIntro();
        }
        onPlay?.Invoke();
    }

    private static void TryPlaySfx(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }
        if (App.Sound == null)
        {
            Debug.LogWarning($"MainMenuUI: cannot play '{name}' — App.Sound is null. Check that App is in the scene and initialised before this UI.");
            return;
        }
        App.Sound.PlaySound(name, isUI: true);
    }

    private void OnResetProgressClicked()
    {
        if (App.Profile != null)
        {
            App.Profile.Reset();
        }

        // Wipe in-memory state too: the manager cached `currentLevelIndex` at scene start, so
        // without this the next Play would still show the pre-reset level until the manager
        // syncs again. GoToLevel(0) re-resolves and applies the new layout immediately.
        if (manager != null && manager.IsInitialized)
        {
            manager.GoToLevel(0);
        }

        RefreshPlayLabel();
        onResetProgress?.Invoke();
    }
}
