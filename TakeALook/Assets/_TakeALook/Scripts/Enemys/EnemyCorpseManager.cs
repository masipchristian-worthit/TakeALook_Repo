using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Mantiene los cadáveres de enemigos en su sitio aunque cambies de escena.
///
/// Cómo funciona:
///   - EnemyHealth.MarkAsCorpse() llama a RegisterCorpse(go).
///   - Movemos el GO a una escena DontDestroyOnLoad (Unity los pone bajo un root persistente).
///   - Guardamos a qué escena PERTENECE el cuerpo (la escena donde murió) en _byScene.
///   - Cuando el SceneManager carga una escena, mostramos los cadáveres asociados a ella
///     y ocultamos los demás (si los enemigos viven en escenas distintas, por inmersión
///     ningún cadáver de la cocina aparece visible en el sótano).
/// </summary>
public class EnemyCorpseManager : MonoBehaviour
{
    public static EnemyCorpseManager Instance { get; private set; }

    private readonly Dictionary<string, List<GameObject>> _byScene = new Dictionary<string, List<GameObject>>();

    public static void RegisterCorpse(GameObject corpse)
    {
        if (corpse == null) return;
        EnsureInstance();
        Instance.AddCorpse(corpse);
    }

    static void EnsureInstance()
    {
        if (Instance != null) return;
        var go = new GameObject("[EnemyCorpseManager]");
        Instance = go.AddComponent<EnemyCorpseManager>();
        DontDestroyOnLoad(go);
        SceneManager.sceneLoaded += Instance.OnSceneLoaded;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void AddCorpse(GameObject corpse)
    {
        // Sacamos al cadáver de cualquier padre (los waypoints o spawners no deben llevárselo)
        corpse.transform.SetParent(null);

        string sceneName = corpse.scene.name;
        if (string.IsNullOrEmpty(sceneName)) sceneName = SceneManager.GetActiveScene().name;

        DontDestroyOnLoad(corpse);

        if (!_byScene.TryGetValue(sceneName, out var list))
        {
            list = new List<GameObject>();
            _byScene[sceneName] = list;
        }
        list.Add(corpse);

        // Mantener visible solo si seguimos en la escena de origen
        corpse.SetActive(SceneManager.GetActiveScene().name == sceneName);
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Limpiar referencias destruidas y mostrar/ocultar según escena activa
        foreach (var kv in _byScene)
        {
            string sceneName = kv.Key;
            var list = kv.Value;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null) { list.RemoveAt(i); continue; }
                list[i].SetActive(sceneName == scene.name);
            }
        }
    }

    /// <summary>
    /// Útil para depuración o para reiniciar la partida.
    /// </summary>
    public void ClearAll()
    {
        foreach (var kv in _byScene)
        {
            var list = kv.Value;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) Destroy(list[i]);
            list.Clear();
        }
        _byScene.Clear();
    }
}
