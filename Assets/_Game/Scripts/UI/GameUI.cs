using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-game HUD: reset button, main-menu button, and the level-complete popup. Subscribes to
/// <see cref="HexSortManager.LevelCompleted"/> to surface the popup. Restart and Next Level
/// transitions are handled in-scene by <see cref="HexSortManager"/> — no scene reload.
/// </summary>
public sealed class GameUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HexSortManager manager;
    [SerializeField] private LevelCompletePopup levelCompletePopup;
    [Tooltip("Main menu canvas in this scene. Shown when the player taps Main Menu.")]
    [SerializeField] private MainMenuUI mainMenuUI;

    [Header("Buttons")]
    [SerializeField] private Button resetButton;
    [SerializeField] private Button mainMenuButton;

    [Header("HUD Labels")]
    [Tooltip("Displays the current level number (1-based).")]
    [SerializeField] private TMP_Text levelLabel;
    [Tooltip("Displays the player's move count for the current level.")]
    [SerializeField] private TMP_Text moveCountLabel;

    private int lastDisplayedMoveCount = -1;

    private void Awake()
    {
        if (manager == null)
        {
            manager = FindFirstObjectByType<HexSortManager>();
        }
        if (mainMenuUI == null)
        {
            // Use FindObjectsInactive.Include so we can still resolve the menu when it has
            // already been hidden (gameObject deactivated) by a previous Play click.
            mainMenuUI = FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
        }

        if (resetButton != null)
        {
            resetButton.onClick.AddListener(OnResetClicked);
        }
        if (mainMenuButton != null)
        {
            mainMenuButton.onClick.AddListener(OnMainMenuClicked);
        }
        if (levelCompletePopup != null)
        {
            levelCompletePopup.Hide();
        }
    }

    private void OnEnable()
    {
        if (manager != null)
        {
            manager.LevelCompleted += HandleLevelCompleted;
        }
    }

    private void OnDisable()
    {
        if (manager != null)
        {
            manager.LevelCompleted -= HandleLevelCompleted;
        }
    }

    private void OnDestroy()
    {
        if (resetButton != null)
        {
            resetButton.onClick.RemoveListener(OnResetClicked);
        }
        if (mainMenuButton != null)
        {
            mainMenuButton.onClick.RemoveListener(OnMainMenuClicked);
        }
    }

    private void Update()
    {
        if (manager == null)
        {
            return;
        }

        // Cheap HUD refresh — only touch the labels when the displayed value changes.
        if (moveCountLabel != null && manager.CurrentMoveCount != lastDisplayedMoveCount)
        {
            lastDisplayedMoveCount = manager.CurrentMoveCount;
            moveCountLabel.text = $"Moves: {lastDisplayedMoveCount}";
        }
        if (levelLabel != null)
        {
            // Display 1-based to the player.
            levelLabel.text = $"Level {manager.CurrentLevelIndex + 1}";
        }
    }

    private void HandleLevelCompleted(LevelResult result)
    {
        if (levelCompletePopup != null)
        {
            levelCompletePopup.Show(result, manager);
        }
    }

    private void OnResetClicked()
    {
        if (manager != null)
        {
            manager.RestartLevel();
        }
    }

    private void OnMainMenuClicked()
    {
        if (mainMenuUI == null)
        {
            Debug.LogWarning("GameUI: Main Menu button pressed but no MainMenuUI is assigned or present in the scene.");
            return;
        }
        mainMenuUI.Show();
    }
}
