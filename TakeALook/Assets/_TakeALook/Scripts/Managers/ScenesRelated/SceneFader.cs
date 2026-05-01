using UnityEngine;
using UnityEngine.SceneManagement;
using DG.Tweening;
using System;
using System.Collections;

/// <summary>
/// Singleton persistente que gestiona fades a/desde negro entre escenas.
/// Requiere un Canvas con CanvasGroup (Image negra a pantalla completa).
/// Añadir a un GameObject en la primera escena o como prefab en las escenas principales.
/// </summary>
public class SceneFader : MonoBehaviour
{
    public static SceneFader Instance { get; private set; }

    [Header("Referencias")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;

    [Header("Tiempos")]
    [SerializeField] private float defaultFadeInTime = 0.8f;
    [SerializeField] private float defaultFadeOutTime = 0.8f;

    public bool IsFading { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (fadeCanvasGroup != null)
        {
            // Al iniciar una escena directamente desde Unity, empieza en negro y hace fade in.
            fadeCanvasGroup.alpha = 1f;
            fadeCanvasGroup.blocksRaycasts = true;

            FadeIn(defaultFadeInTime);
        }
    }

    /// <summary>
    /// Fade desde negro hacia transparente. Llámalo al entrar en una escena.
    /// </summary>
    public void FadeIn(float duration = -1f, Action onComplete = null)
    {
        if (fadeCanvasGroup == null)
        {
            onComplete?.Invoke();
            return;
        }

        float t = duration < 0f ? defaultFadeInTime : duration;

        IsFading = true;

        fadeCanvasGroup.DOKill();
        fadeCanvasGroup.alpha = 1f;
        fadeCanvasGroup.blocksRaycasts = true;

        fadeCanvasGroup.DOFade(0f, t)
            .SetEase(Ease.InQuad)
            .SetUpdate(true)
            .OnComplete(() =>
            {
                fadeCanvasGroup.blocksRaycasts = false;
                IsFading = false;
                onComplete?.Invoke();
            });
    }

    /// <summary>
    /// Fade a negro y carga la escena indicada al completarse.
    /// Este método NO hace fade in después. Lo dejo igual para no romper tus otras escenas.
    /// </summary>
    public void FadeToScene(string sceneName, float duration = -1f)
    {
        if (IsFading) return;

        if (fadeCanvasGroup == null)
        {
            SceneManager.LoadScene(sceneName);
            return;
        }

        float t = duration < 0f ? defaultFadeOutTime : duration;

        IsFading = true;

        fadeCanvasGroup.DOKill();
        fadeCanvasGroup.blocksRaycasts = true;

        fadeCanvasGroup.DOFade(1f, t)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true)
            .OnComplete(() =>
            {
                IsFading = false;
                SceneManager.LoadScene(sceneName);
            });
    }

    /// <summary>
    /// Fade a negro, carga una escena y después hace fade in automáticamente.
    /// Usa este para escaleras, puertas o warps entre niveles.
    /// </summary>
    public void FadeToSceneWithFadeIn(string sceneName, float fadeOutDuration = -1f, float fadeInDuration = -1f)
    {
        if (IsFading) return;

        StartCoroutine(FadeToSceneWithFadeInRoutine(sceneName, fadeOutDuration, fadeInDuration));
    }

    private IEnumerator FadeToSceneWithFadeInRoutine(string sceneName, float fadeOutDuration, float fadeInDuration)
    {
        if (fadeCanvasGroup == null)
        {
            SceneManager.LoadScene(sceneName);
            yield break;
        }

        float fadeOutTime = fadeOutDuration < 0f ? defaultFadeOutTime : fadeOutDuration;
        float fadeInTime = fadeInDuration < 0f ? defaultFadeInTime : fadeInDuration;

        IsFading = true;

        // Fade out a negro.
        fadeCanvasGroup.DOKill();
        fadeCanvasGroup.alpha = 0f;
        fadeCanvasGroup.blocksRaycasts = true;

        yield return fadeCanvasGroup.DOFade(1f, fadeOutTime)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true)
            .WaitForCompletion();

        // Cargar escena nueva.
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);

        while (!asyncLoad.isDone)
            yield return null;

        // Esperar un frame para que la escena nueva termine de inicializarse.
        yield return null;

        // Fade in desde negro.
        fadeCanvasGroup.DOKill();
        fadeCanvasGroup.alpha = 1f;
        fadeCanvasGroup.blocksRaycasts = true;

        yield return fadeCanvasGroup.DOFade(0f, fadeInTime)
            .SetEase(Ease.InQuad)
            .SetUpdate(true)
            .WaitForCompletion();

        fadeCanvasGroup.blocksRaycasts = false;
        IsFading = false;
    }

    /// <summary>
    /// Solo fade a negro, sin cambiar de escena.
    /// </summary>
    public void FadeOut(float duration = -1f, Action onComplete = null)
    {
        if (fadeCanvasGroup == null)
        {
            onComplete?.Invoke();
            return;
        }

        float t = duration < 0f ? defaultFadeOutTime : duration;

        IsFading = true;

        fadeCanvasGroup.DOKill();
        fadeCanvasGroup.blocksRaycasts = true;

        fadeCanvasGroup.DOFade(1f, t)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true)
            .OnComplete(() =>
            {
                IsFading = false;
                onComplete?.Invoke();
            });
    }

    /// <summary>
    /// Pone la pantalla en negro instantáneamente.
    /// </summary>
    public void SetBlack()
    {
        if (fadeCanvasGroup == null) return;

        fadeCanvasGroup.DOKill();
        fadeCanvasGroup.alpha = 1f;
        fadeCanvasGroup.blocksRaycasts = true;
    }

    /// <summary>
    /// Limpia el fade instantáneamente.
    /// </summary>
    public void SetClear()
    {
        if (fadeCanvasGroup == null) return;

        fadeCanvasGroup.DOKill();
        fadeCanvasGroup.alpha = 0f;
        fadeCanvasGroup.blocksRaycasts = false;
    }
}