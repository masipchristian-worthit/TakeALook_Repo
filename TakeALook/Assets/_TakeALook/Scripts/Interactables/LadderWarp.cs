using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using DG.Tweening;

/// <summary>
/// Trigger de escalera para cambiar de escena. Se asigna a un Collider con isTrigger
/// junto a la base/arriba de una escalera.
///
/// Uso:
///   1. Crear un GameObject vacío con un BoxCollider (isTrigger=true) en el punto donde
///      el jugador interactúa con la escalera. Añadirle este componente.
///   2. Configurar `targetSceneName` con el nombre EXACTO de la escena destino (debe
///      estar añadida en Build Settings).
///   3. Configurar `spawnPointId` con un identificador (ej. "BasementTop",
///      "AtticBottom"). En la escena destino, colocar un GameObject con el componente
///      <see cref="LadderSpawnPoint"/> y el mismo `spawnPointId`.
///
/// Cuando el jugador entra al trigger y pulsa Interact (E), se hace fade-out, se carga
/// la escena y, al cargar, el LadderWarpRouter reposiciona al jugador en el
/// LadderSpawnPoint correspondiente.
/// </summary>
public class LadderWarp : MonoBehaviour
{
    [Header("Destination")]
    [Tooltip("Nombre EXACTO de la escena destino (Build Settings).")]
    [SerializeField] private string targetSceneName;
    [Tooltip("ID del LadderSpawnPoint en la escena destino donde aparecer.")]
    [SerializeField] private string spawnPointId = "Default";

    [Header("Trigger Behavior")]
    [Tooltip("Si está marcado, el cambio de escena es automático al entrar al trigger.")]
    [SerializeField] private bool autoWarpOnEnter = false;
    [Tooltip("Si NO es automático, el jugador debe pulsar la tecla Interact (E) estando dentro del trigger.")]
    [SerializeField] private Key interactKey = Key.E;

    [Header("Fade")]
    [Tooltip("CanvasGroup negro que cubre la pantalla. Debe estar en un Canvas con DontDestroyOnLoad o pertenecer al UIManager global.")]
    [SerializeField] private CanvasGroup fadeCanvas;
    [SerializeField] private float fadeDuration = 0.6f;

    [Header("Audio")]
    [SerializeField] private string sfxLadderId = "ladder_use";

    private bool _playerInside;
    private bool _isWarping;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInside = true;
        if (autoWarpOnEnter) TryWarp();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInside = false;
    }

    private void Update()
    {
        if (_playerInside && !autoWarpOnEnter && !_isWarping
            && Keyboard.current != null && Keyboard.current[interactKey].wasPressedThisFrame)
        {
            TryWarp();
        }
    }

    public void TryWarp()
    {
        if (_isWarping || string.IsNullOrEmpty(targetSceneName)) return;
        StartCoroutine(WarpRoutine());
    }

    private IEnumerator WarpRoutine()
    {
        _isWarping = true;
        AudioManager.Instance?.PlaySFX(sfxLadderId, transform.position);

        if (fadeCanvas != null)
        {
            fadeCanvas.blocksRaycasts = true;
            fadeCanvas.DOFade(1f, fadeDuration).SetUpdate(true);
            yield return new WaitForSecondsRealtime(fadeDuration);
        }

        LadderWarpRouter.PendingSpawnPointId = spawnPointId;
        LadderWarpRouter.PendingFadeIn = (fadeCanvas != null);

        var op = SceneManager.LoadSceneAsync(targetSceneName);
        while (op != null && !op.isDone) yield return null;
    }
}
