using UnityEngine;
using UnityEngine.SceneManagement;
using DG.Tweening;

/// <summary>
/// Router global que se encarga de reposicionar al jugador al cargar una escena
/// pedida por un <see cref="LadderWarp"/>. También recupera el fade del CanvasGroup
/// de la nueva escena (si encuentra uno con tag "FadeCanvas") tras cargar.
///
/// Se auto-instala mediante un atributo RuntimeInitializeOnLoadMethod, así que no
/// hace falta arrastrarlo manualmente a la escena.
/// </summary>
public static class LadderWarpRouter
{
    public static string PendingSpawnPointId;
    public static bool PendingFadeIn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (string.IsNullOrEmpty(PendingSpawnPointId)) return;

        // Buscar spawn point con el ID solicitado
        LadderSpawnPoint[] points = Object.FindObjectsByType<LadderSpawnPoint>(FindObjectsSortMode.None);
        LadderSpawnPoint match = null;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] != null && points[i].SpawnPointId == PendingSpawnPointId)
            {
                match = points[i];
                break;
            }
        }

        if (match != null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                // Si el jugador tiene Rigidbody, paramos su velocidad para evitar que
                // arrastre velocidad de la escena anterior (bugs visuales y físicos).
                Rigidbody rb = playerObj.GetComponent<Rigidbody>();
                if (rb != null)
                {
#if UNITY_6000_0_OR_NEWER
                    rb.linearVelocity = Vector3.zero;
#else
                    rb.velocity = Vector3.zero;
#endif
                    rb.angularVelocity = Vector3.zero;
                }

                CharacterController cc = playerObj.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;

                playerObj.transform.SetPositionAndRotation(match.transform.position, match.transform.rotation);

                if (cc != null) cc.enabled = true;
            }
        }
        else
        {
            Debug.LogWarning($"[LadderWarpRouter] No se encontró LadderSpawnPoint con id '{PendingSpawnPointId}' en la escena '{scene.name}'.");
        }

        if (PendingFadeIn)
        {
            // Buscamos cualquier CanvasGroup con el tag "FadeCanvas" para hacer fade-in
            GameObject fadeGo = GameObject.FindGameObjectWithTag("FadeCanvas");
            if (fadeGo != null && fadeGo.TryGetComponent(out CanvasGroup cg))
            {
                cg.alpha = 1f;
                cg.blocksRaycasts = true;
                cg.DOFade(0f, 0.5f).SetUpdate(true).OnComplete(() =>
                {
                    cg.blocksRaycasts = false;
                });
            }
        }

        PendingSpawnPointId = null;
        PendingFadeIn = false;
    }
}
