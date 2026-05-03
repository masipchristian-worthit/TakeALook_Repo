using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using DG.Tweening;

/// <summary>
/// Gestiona la pantalla de derrota del juego.
/// Se activa cuando el jugador muere (PlayerHealth.OnDied) o cuando el temporizador llega a 0 (CountdownTimer.onTimerEnded).
/// Muestra un fade-in con el motivo de derrota y carga el Main Menu automáticamente.
/// 
/// Setup:
///   1. Crea un Canvas con un CanvasGroup fullscreen oscuro + dos TMP_Text (título y subtítulo).
///   2. Asigna las referencias en el Inspector.
///   3. Asegúrate de que el nombre de la escena del Main Menu coincide con mainMenuSceneName.
/// </summary>
public class DefeatManager : MonoBehaviour
{
    public static DefeatManager Instance { get; private set; }

    [Header("Referencias de escena")]
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private CountdownTimer countdownTimer;

    [Header("Pantalla de Derrota")]
    [SerializeField] private CanvasGroup defeatCanvasGroup;
    [SerializeField] private TMP_Text defeatTitleLabel;
    [SerializeField] private TMP_Text defeatSubtitleLabel;

    [Header("Textos")]
    [SerializeField] private string titleText = "DERROTA";
    [SerializeField] private string subtitleDeathText = "Has muerto.";
    [SerializeField] private string subtitleTimerText = "Se acabó el tiempo...";

    [Header("Transición")]
    [SerializeField] private float fadeInTime = 1.2f;
    [SerializeField] private float delayBeforeMenu = 3.5f;
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("Audio")]
    [SerializeField] private string defeatSfxId = "defeat";

    private bool _isDefeated = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Aseguramos que la pantalla empieza invisible y sin bloquear input
        if (defeatCanvasGroup != null)
        {
            defeatCanvasGroup.alpha = 0f;
            defeatCanvasGroup.blocksRaycasts = false;
            defeatCanvasGroup.interactable = false;
        }
    }

    private void Start()
    {
        // Suscribirse a los eventos de derrota
        if (playerHealth != null)
            playerHealth.OnDied += HandlePlayerDeath;

        if (countdownTimer != null)
            countdownTimer.onTimerEnded.AddListener(HandleTimerEnd);
    }

    private void OnDestroy()
    {
        // Limpieza de suscripciones para evitar memory leaks
        if (playerHealth != null)
            playerHealth.OnDied -= HandlePlayerDeath;

        if (countdownTimer != null)
            countdownTimer.onTimerEnded.RemoveListener(HandleTimerEnd);
    }

    // ─── Handlers ───────────────────────────────────────────────────────────

    private void HandlePlayerDeath() => TriggerDefeat(subtitleDeathText);
    private void HandleTimerEnd() => TriggerDefeat(subtitleTimerText);

    // ─── Lógica principal ────────────────────────────────────────────────────

    /// <summary>
    /// Activa la pantalla de derrota. Se puede llamar desde código externo si es necesario.
    /// </summary>
    public void TriggerDefeat(string reason = "")
    {
        if (_isDefeated) return;
        _isDefeated = true;

       
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        AudioManager.Instance?.PlayUI(defeatSfxId);

        // Aplicar textos
        if (defeatTitleLabel != null) defeatTitleLabel.text = titleText;
        if (defeatSubtitleLabel != null) defeatSubtitleLabel.text = reason;

        // Fade-in de la pantalla de derrota
        if (defeatCanvasGroup != null)
        {
            defeatCanvasGroup.blocksRaycasts = true;
            defeatCanvasGroup.interactable = true;

            defeatCanvasGroup
                .DOFade(1f, fadeInTime)
                .SetEase(Ease.InQuad)
                .SetUpdate(true) // funciona aunque Time.timeScale == 0
                .OnComplete(ScheduleMenuLoad);
        }
        else
        {
            ScheduleMenuLoad();
        }
    }

    private void ScheduleMenuLoad()
    {
        DOVirtual.DelayedCall(delayBeforeMenu, GoToMainMenu)
            .SetUpdate(true);
    }

    private void GoToMainMenu()
    {
        GameManager.Instance?.ResetRunState();
        SceneManager.LoadScene(mainMenuSceneName);
    }
}
