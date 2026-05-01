using UnityEngine;
using UnityEngine.SceneManagement;
using DG.Tweening;

public class SceneWarp : MonoBehaviour
{
    [Header("Scene Warp")]
    [SerializeField] private string sceneName;

    [Header("Spawn opcional")]
    [Tooltip("Si usas LadderSpawnPoint en la escena nueva, pon aquí su ID.")]
    [SerializeField] private string spawnPointId;

    [Tooltip("Usar el LadderWarpRouter para colocar al jugador en un spawn concreto.")]
    [SerializeField] private bool useSpawnPoint;

    [Header("Fade")]
    [SerializeField] private bool useSceneFader = true;
    [SerializeField] private float fadeOutTime = 0.5f;

    [Tooltip("Activa fade in al cargar la escena nueva usando el sistema de LadderWarpRouter.")]
    [SerializeField] private bool fadeInAfterLoad = true;

    [Header("Fallback Fade")]
    [Tooltip("CanvasGroup opcional por si no quieres usar SceneFader.")]
    [SerializeField] private CanvasGroup fallbackFadeCanvas;

    public void Warp()
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("[SceneWarp] No hay sceneName asignado.");
            return;
        }

        if (useSpawnPoint && !string.IsNullOrEmpty(spawnPointId))
        {
            LadderWarpRouter.PendingSpawnPointId = spawnPointId;
            LadderWarpRouter.PendingFadeIn = fadeInAfterLoad;
        }

        if (useSceneFader && SceneFader.Instance != null)
        {
            SceneFader.Instance.FadeToScene(sceneName, fadeOutTime);
            return;
        }

        if (fallbackFadeCanvas != null)
        {
            fallbackFadeCanvas.alpha = 0f;
            fallbackFadeCanvas.blocksRaycasts = true;

            fallbackFadeCanvas.DOFade(1f, fadeOutTime)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    SceneManager.LoadScene(sceneName);
                });

            return;
        }

        SceneManager.LoadScene(sceneName);
    }

    public void WarpToScene(string newSceneName)
    {
        sceneName = newSceneName;
        Warp();
    }
}