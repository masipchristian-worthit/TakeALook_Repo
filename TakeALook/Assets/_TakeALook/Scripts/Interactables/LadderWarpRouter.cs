using UnityEngine;
using UnityEngine.SceneManagement;
using DG.Tweening;

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
        if (!string.IsNullOrEmpty(PendingSpawnPointId))
        {
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
                else
                {
                    Debug.LogWarning("[LadderWarpRouter] No se encontró ningún objeto con tag Player.");
                }
            }
            else
            {
                Debug.LogWarning("[LadderWarpRouter] No se encontró LadderSpawnPoint con id: " + PendingSpawnPointId);
            }
        }

        if (PendingFadeIn)
        {
            CanvasGroup[] groups = Object.FindObjectsByType<CanvasGroup>(FindObjectsSortMode.None);

            foreach (CanvasGroup group in groups)
            {
                string lowerName = group.gameObject.name.ToLower();

                if (lowerName.Contains("fade") || lowerName.Contains("black") || group.CompareTag("FadeCanvas"))
                {
                    group.DOKill();
                    group.alpha = 1f;
                    group.blocksRaycasts = true;
                    group.interactable = true;

                    group.DOFade(0f, 0.8f)
                        .SetUpdate(true)
                        .OnComplete(() =>
                        {
                            group.blocksRaycasts = false;
                            group.interactable = false;
                        });
                }
            }
        }

        PendingSpawnPointId = null;
        PendingFadeIn = false;
    }
}