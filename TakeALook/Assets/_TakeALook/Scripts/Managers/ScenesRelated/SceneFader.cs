using UnityEngine;
using UnityEngine.SceneManagement;
using DG.Tweening;
using System;

/// <summary>
/// Singleton persistente que gestiona fades a/desde negro entre escenas.
/// Requiere un Canvas con CanvasGroup (Image negra a pantalla completa).
/// Añadir a un GameObject en la primera escena (MainMenu).
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
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (fadeCanvasGroup != null)
        {
            // Empezamos en negro para hacer fade in desde escena
            fadeCanvasGroup.alpha = 1f;
            fadeCanvasGroup.blocksRaycasts = true;
        }
    }

    /// <summary>Fade desde negro hacia transparente. Llámalo al entrar en una escena.</summary>
    public void FadeIn(float duration = -1f, Action onComplete = null)
    {
        if (fadeCanvasGroup == null) { onComplete?.Invoke(); return; }

        float t = duration < 0f ? defaultFadeInTime : duration;
        IsFading = true;

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

    /// <summary>Fade a negro y carga la escena indicada al completarse.</summary>
    public void FadeToScene(string sceneName, float duration = -1f)
    {
        if (IsFading) return;
        if (fadeCanvasGroup == null) { SceneManager.LoadScene(sceneName); return; }

        float t = duration < 0f ? defaultFadeOutTime : duration;
        IsFading = true;

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

    /// <summary>Solo fade a negro, sin cambiar de escena.</summary>
    public void FadeOut(float duration = -1f, Action onComplete = null)
    {
        if (fadeCanvasGroup == null) { onComplete?.Invoke(); return; }

        float t = duration < 0f ? defaultFadeOutTime : duration;
        IsFading = true;

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

    /// <summary>Pone la pantalla en negro instantáneamente.</summary>
    public void SetBlack()
    {
        if (fadeCanvasGroup == null) return;
        fadeCanvasGroup.alpha = 1f;
        fadeCanvasGroup.blocksRaycasts = true;
    }

    /// <summary>Limpia el fade instantáneamente.</summary>
    public void SetClear()
    {
        if (fadeCanvasGroup == null) return;
        fadeCanvasGroup.alpha = 0f;
        fadeCanvasGroup.blocksRaycasts = false;
    }
}
