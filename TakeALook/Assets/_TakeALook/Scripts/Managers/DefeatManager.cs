using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using DG.Tweening;

/// <summary>
/// Gestiona la pantalla de derrota del juego.
/// Se activa cuando el jugador muere o cuando el temporizador llega a 0.
/// Muestra una pantalla de derrota con un único botón para volver al Main Menu.
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

    [Header("Botón")]
    [SerializeField] private Button mainMenuButton;

    [Header("Textos")]
    [SerializeField] private string titleText = "DEFEAT";
    [SerializeField] private string subtitleDeathText = "You died.";
    [SerializeField] private string subtitleTimerText = "Time has run out.";

    [Header("Transición")]
    [SerializeField] private float fadeInTime = 1.2f;
    [SerializeField] private string mainMenuSceneName = "SCN_MainMenu";

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

        if (defeatCanvasGroup != null)
        {
            defeatCanvasGroup.alpha = 0f;
            defeatCanvasGroup.blocksRaycasts = false;
            defeatCanvasGroup.interactable = false;
        }

        if (mainMenuButton != null)
        {
            mainMenuButton.gameObject.SetActive(false);
            mainMenuButton.onClick.RemoveAllListeners();
            mainMenuButton.onClick.AddListener(GoToMainMenu);
        }
    }

    private void Start()
    {
        if (playerHealth != null)
            playerHealth.OnDied += HandlePlayerDeath;

        if (countdownTimer != null)
            countdownTimer.onTimerEnded.AddListener(HandleTimerEnd);
    }

    private void OnDestroy()
    {
        if (playerHealth != null)
            playerHealth.OnDied -= HandlePlayerDeath;

        if (countdownTimer != null)
            countdownTimer.onTimerEnded.RemoveListener(HandleTimerEnd);
    }

    private void HandlePlayerDeath()
    {
        TriggerDefeat(subtitleDeathText);
    }

    private void HandleTimerEnd()
    {
        TriggerDefeat(subtitleTimerText);
    }

    public void TriggerDefeat(string reason = "")
    {
        if (_isDefeated) return;
        _isDefeated = true;

        Time.timeScale = 0f;
        Time.fixedDeltaTime = 0.02f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        AudioManager.Instance?.PlayUI(defeatSfxId);

        if (defeatTitleLabel != null)
            defeatTitleLabel.text = titleText;

        if (defeatSubtitleLabel != null)
            defeatSubtitleLabel.text = reason;

        if (mainMenuButton != null)
            mainMenuButton.gameObject.SetActive(true);

        if (defeatCanvasGroup != null)
        {
            defeatCanvasGroup.blocksRaycasts = true;
            defeatCanvasGroup.interactable = true;

            defeatCanvasGroup
                .DOFade(1f, fadeInTime)
                .SetEase(Ease.InQuad)
                .SetUpdate(true);
        }
    }

    public void GoToMainMenu()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        GameManager.Instance?.ResetRunState();

        if (SceneFader.Instance != null)
        {
            SceneFader.Instance.FadeToSceneWithFadeIn(mainMenuSceneName, 0.5f, 0.7f);
        }
        else
        {
            SceneManager.LoadScene(mainMenuSceneName);
        }
    }
}