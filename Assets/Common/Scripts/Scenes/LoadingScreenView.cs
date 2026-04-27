using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.UI;

namespace Ape.Scenes
{
    [MovedFrom(false, sourceNamespace: "")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasScaler))]
    [RequireComponent(typeof(GraphicRaycaster))]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class LoadingScreenView : MonoBehaviour
    {
        private const float EnterDuration = 0.35f;
        private const float ExitDuration = 0.30f;
        private const float SpinnerRotationDuration = 1.1f;
        private const string DefaultStatus = "Loading";

    [Header("Structure")]
    [SerializeField] private RectTransform _rootRect;
    [SerializeField] private Canvas _canvas;
    [SerializeField] private CanvasScaler _canvasScaler;
    [SerializeField] private GraphicRaycaster _graphicRaycaster;
    [SerializeField] private CanvasGroup _canvasGroup;

    [Header("Animation")]
    [SerializeField] private RectTransform _panelRect;
    [SerializeField] private RectTransform _spinnerRect;

    [Header("Content")]
    [SerializeField] private Image _progressFillImage;
    [SerializeField] private TextMeshProUGUI _statusText;

    private Tween _spinnerTween;
    private Sequence _transitionSequence;
    private AppSceneManager _sceneManager;
    private bool _isInitialized;

    public void Initialize()
    {
        if (_isInitialized)
            return;

        _statusText.text = DefaultStatus;
        _progressFillImage.fillAmount = 0f;
        _isInitialized = true;
    }

    public void Bind(AppSceneManager sceneManager)
    {
        if (_sceneManager == sceneManager)
            return;

        Unbind();
        Initialize();
        _sceneManager = sceneManager;

        if (_sceneManager == null)
            return;

        _sceneManager.SceneLoadStarted += HandleSceneLoadStarted;
        _sceneManager.SceneLoadCompleted += HandleSceneLoadCompleted;
    }

    public void Unbind()
    {
        if (_sceneManager == null)
            return;

        _sceneManager.SceneLoadStarted -= HandleSceneLoadStarted;
        _sceneManager.SceneLoadCompleted -= HandleSceneLoadCompleted;
        _sceneManager = null;
    }

    public void SetStatus(string status)
    {
        Initialize();
        _statusText.text = string.IsNullOrWhiteSpace(status) ? DefaultStatus : status;
    }

    public void SetProgress(float progress)
    {
        Initialize();
        _progressFillImage.fillAmount = Mathf.Clamp01(progress);
    }

    public void Show(string status = null)
    {
        Initialize();
        KillTransition();

        SetStatus(status);
        SetProgress(0f);
        SetVisible(true);

        _canvasGroup.alpha = 0f;
        _panelRect.localScale = new Vector3(0.96f, 0.96f, 1f);
        StartSpinner();

        _transitionSequence = DOTween.Sequence().SetUpdate(true);
        _transitionSequence.Join(_canvasGroup.DOFade(1f, EnterDuration).SetEase(Ease.OutCubic));
        _transitionSequence.Join(_panelRect.DOScale(1f, EnterDuration).SetEase(Ease.OutBack));
        _transitionSequence.OnKill(() => _transitionSequence = null);
    }

    public void Hide()
    {
        Initialize();
        if (!_rootRect.gameObject.activeSelf)
            return;

        KillTransition();

        _transitionSequence = DOTween.Sequence().SetUpdate(true);
        _transitionSequence.Join(_canvasGroup.DOFade(0f, ExitDuration).SetEase(Ease.InCubic));
        _transitionSequence.Join(_panelRect.DOScale(1.03f, ExitDuration).SetEase(Ease.InCubic));
        _transitionSequence.OnComplete(() =>
        {
            StopSpinner();
            SetVisible(false);
        });
        _transitionSequence.OnKill(() => _transitionSequence = null);
    }

    private void OnDestroy()
    {
        Unbind();
        KillTransition();
        StopSpinner();
    }

    private void OnValidate()
    {
        _rootRect ??= GetComponent<RectTransform>();
        _canvas ??= GetComponent<Canvas>();
        _canvasScaler ??= GetComponent<CanvasScaler>();
        _graphicRaycaster ??= GetComponent<GraphicRaycaster>();
        _canvasGroup ??= GetComponent<CanvasGroup>();
    }

    private void SetVisible(bool isVisible)
    {
        _rootRect.gameObject.SetActive(isVisible);
        _canvasGroup.blocksRaycasts = isVisible;
    }

    private void StartSpinner()
    {
        StopSpinner();
        _spinnerTween = _spinnerRect.DORotate(new Vector3(0f, 0f, -360f), SpinnerRotationDuration, RotateMode.FastBeyond360)
            .SetEase(Ease.Linear)
            .SetLoops(-1)
            .SetUpdate(true);
    }

    private void StopSpinner()
    {
        if (_spinnerTween != null && _spinnerTween.IsActive())
            _spinnerTween.Kill();

        _spinnerTween = null;
    }

    private void KillTransition()
    {
        if (_transitionSequence != null && _transitionSequence.IsActive())
            _transitionSequence.Kill();

        _transitionSequence = null;
    }

    private void HandleSceneLoadStarted(string sceneName)
    {
        Show();
    }

    private void HandleSceneLoadCompleted(string sceneName)
    {
        Hide();
    }
    }
}
