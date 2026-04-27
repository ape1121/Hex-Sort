using System;
using System.Collections;
using Ape.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ape.Scenes
{
    public sealed class AppSceneManager : IManager
    {
        public event Action<string> SceneLoadStarted;
        public event Action<string> SceneLoadCompleted;

        private bool _isLoading;
        private Coroutine _loadSceneCoroutine;

    public void Initialize()
    {
        _isLoading = false;
        _loadSceneCoroutine = null;

        LoadingScreenView loadingScreen = App.Dependencies.LoadingScreen;
        if (loadingScreen != null)
        {
            loadingScreen.Initialize();
            loadingScreen.Bind(this);
        }
    }

    public bool LoadScene(string sceneName, LoadSceneMode loadSceneMode = LoadSceneMode.Single)
    {
        if (_isLoading)
            return false;

        if (string.IsNullOrWhiteSpace(sceneName))
            throw new ArgumentException("A valid scene name is required.", nameof(sceneName));

        _isLoading = true;
        SceneLoadStarted?.Invoke(sceneName);
        _loadSceneCoroutine = App.Instance.StartCoroutine(LoadSceneRoutine(sceneName, loadSceneMode));
        return true;
    }

    private IEnumerator LoadSceneRoutine(string sceneName, LoadSceneMode loadSceneMode)
    {
        float delay = Mathf.Max(0f, App.Config.SceneTransitionDuration);
        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(sceneName, loadSceneMode);
        if (loadOperation == null)
        {
            _isLoading = false;
            _loadSceneCoroutine = null;
            throw new MissingReferenceException($"Failed to start loading scene '{sceneName}'.");
        }

        _loadSceneCoroutine = null;
        loadOperation.completed += _ => CompleteLoad(sceneName);
    }

    private void CompleteLoad(string sceneName)
    {
        _isLoading = false;
        SceneLoadCompleted?.Invoke(sceneName);
    }

    public void Shutdown()
    {
        if (_loadSceneCoroutine != null && App.Instance != null)
            App.Instance.StopCoroutine(_loadSceneCoroutine);

        LoadingScreenView loadingScreen = App.Dependencies.LoadingScreen;
        if (loadingScreen != null)
            loadingScreen.Unbind();

        SceneLoadStarted = null;
        SceneLoadCompleted = null;
        _isLoading = false;
        _loadSceneCoroutine = null;
    }
    }
}
