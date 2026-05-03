using UnityEngine;
using UnityEngine.SceneManagement;

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
        // BUG FIX: Se añade el estado del ID pendiente en el log para facilitar el debug.
        // El ID lo limpia LadderSpawnPoint una vez que coloca al jugador correctamente.
        Debug.Log($"[LadderWarpRouter] Escena cargada: {scene.name} | PendingSpawnPointId: {(string.IsNullOrEmpty(PendingSpawnPointId) ? "—ninguno—" : PendingSpawnPointId)}");
    }

    public static void ClearPendingSpawn()
    {
        Debug.Log($"[LadderWarpRouter] Limpiando spawn pendiente: {PendingSpawnPointId}");
        PendingSpawnPointId = null;
        PendingFadeIn = false;
    }
}
