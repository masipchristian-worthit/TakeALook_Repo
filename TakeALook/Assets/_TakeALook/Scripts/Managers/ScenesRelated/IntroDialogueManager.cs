using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using DG.Tweening;

/// <summary>
/// Gestiona la escena de introducción/final: fondo negro, caja de diálogo con efecto máquina de escribir,
/// botón Skip y transición automática a la siguiente escena al terminar.
/// </summary>
public class IntroDialogueManager : MonoBehaviour
{
    [Header("Escena siguiente")]
    [SerializeField] private string nextSceneName = "GameScene";

    [Header("Líneas de diálogo")]
    [TextArea(2, 6)]
    [SerializeField] private List<string> dialogueLines = new List<string>();

    [Header("Caja de Diálogo")]
    [SerializeField] private CanvasGroup dialogueBox;
    [SerializeField] private RectTransform dialogueBoxRect;
    [SerializeField] private TMP_Text dialogueText;
    [SerializeField] private float boxFadeInTime = 0.5f;

    [Tooltip("Desplazamiento desde el que aparece la caja (slide up).")]
    [SerializeField] private Vector2 boxSlideOffset = new Vector2(0f, -25f);

    [Header("Ajustes del Texto")]
    [Tooltip("Tamaño de la letra del diálogo.")]
    [SerializeField] private float textFontSize = 38f;

    [Tooltip("Separación vertical entre líneas.")]
    [SerializeField] private float textLineSpacing = 0f;

    [Tooltip("Desactiva Auto Size de TextMeshPro para que el tamaño manual funcione correctamente.")]
    [SerializeField] private bool disableAutoSize = true;

    [Header("Efecto Máquina de Escribir")]
    [SerializeField] private float charDelay = 0.042f;
    [SerializeField] private float charDelayFast = 0.006f;

    [Tooltip("Pausa extra al encontrar una coma o punto y coma.")]
    [SerializeField] private float commaPause = 0.18f;

    [Tooltip("Pausa extra al encontrar un punto, exclamación o interrogación.")]
    [SerializeField] private float periodPause = 0.38f;

    [Header("Audio")]
    [Tooltip("ID en SoundLibrary para el blip de cada carácter.")]
    [SerializeField] private string blipSoundId = "dialogue_blip";

    [Tooltip("ID en SoundLibrary para el sonido al avanzar línea.")]
    [SerializeField] private string advanceSoundId = "ui_select";

    [Tooltip("Reproducir blip cada N caracteres (1 = todos).")]
    [SerializeField][Min(1)] private int blipEveryNChars = 2;

    [Header("Botón Skip")]
    [SerializeField] private CanvasGroup skipButton;
    [SerializeField] private float skipButtonFadeDelay = 1.2f;
    [SerializeField] private float skipButtonFadeTime = 0.5f;

    [Header("Fade inicial")]
    [Tooltip("Tiempo de espera tras el fade in antes de mostrar el primer diálogo.")]
    [SerializeField] private float preFadeDialogueDelay = 0.6f;
    [SerializeField] private float fadeInDuration = 0.7f;

    [Header("Efecto de escala al comenzar línea")]
    [SerializeField] private bool useLineStartScale = true;
    [SerializeField] private float lineStartScaleAmount = 1.04f;
    [SerializeField] private float lineStartScaleTime = 0.18f;

    private int _currentLine;
    private bool _isTyping;
    private bool _fastMode;
    private bool _isTransitioning;
    private bool _inputEnabled;
    private bool _dialogueStarted;
    private int _charCount;
    private Coroutine _typeRoutine;
    private Vector2 _boxBasePos;

    #region Unity Messages

    private void Start()
    {
        _inputEnabled = false;
        _dialogueStarted = false;

        // Por si venimos de la puerta final con cámara lenta.
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        // Escena de diálogo/UI: mostramos el ratón para poder pulsar Skip.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        ApplyTextSettings();

        if (dialogueBox != null)
        {
            dialogueBox.alpha = 0f;
            dialogueBox.blocksRaycasts = false;
            dialogueBox.interactable = false;
        }

        if (skipButton != null)
        {
            skipButton.alpha = 0f;
            skipButton.blocksRaycasts = false;
            skipButton.interactable = false;
        }

        if (dialogueText != null)
            dialogueText.text = "";

        if (dialogueBoxRect != null)
            _boxBasePos = dialogueBoxRect.anchoredPosition;

        StartCoroutine(StartDialogueSafely());
    }

    private void Update()
    {
        if (!_inputEnabled || _isTransitioning) return;

        bool clickDown = Input.GetMouseButtonDown(0);
        _fastMode = Input.GetMouseButton(0);

        if (clickDown)
        {
            if (_isTyping)
            {
                // Mantener clic acelera el texto.
            }
            else
            {
                AdvanceLine();
            }
        }
    }

    #endregion

    #region Inicio seguro

    private IEnumerator StartDialogueSafely()
    {
        // Seguridad extra por si algún script FPS vuelve a ocultarlo al cargar.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (SceneFader.Instance != null)
        {
            SceneFader.Instance.FadeIn(fadeInDuration, OnFadeInComplete);

            // Protección: si por cualquier motivo el callback del fader no se ejecuta,
            // arrancamos el diálogo igualmente.
            yield return new WaitForSecondsRealtime(fadeInDuration + 0.25f);

            if (!_dialogueStarted)
                OnFadeInComplete();
        }
        else
        {
            OnFadeInComplete();
        }
    }

    private void OnFadeInComplete()
    {
        if (_dialogueStarted) return;

        // Volvemos a asegurar el cursor justo cuando empieza el diálogo.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        _dialogueStarted = true;
        StartCoroutine(DelayedStart());
    }

    #endregion

    #region Ajustes de texto

    private void ApplyTextSettings()
    {
        if (dialogueText == null) return;

        if (disableAutoSize)
            dialogueText.enableAutoSizing = false;

        dialogueText.fontSize = textFontSize;
        dialogueText.lineSpacing = textLineSpacing;
    }

    #endregion

    #region Flujo principal

    private IEnumerator DelayedStart()
    {
        yield return new WaitForSecondsRealtime(preFadeDialogueDelay);

        if (dialogueLines == null || dialogueLines.Count == 0)
        {
            GoToNextScene();
            yield break;
        }

        ShowDialogueBox();
    }

    private void ShowDialogueBox()
    {
        if (dialogueBoxRect != null)
            dialogueBoxRect.anchoredPosition = _boxBasePos + boxSlideOffset;

        Sequence seq = DOTween.Sequence();
        seq.SetUpdate(true);

        if (dialogueBox != null)
            seq.Join(dialogueBox.DOFade(1f, boxFadeInTime).SetEase(Ease.OutQuad).SetUpdate(true));

        if (dialogueBoxRect != null)
            seq.Join(dialogueBoxRect.DOAnchorPos(_boxBasePos, boxFadeInTime).SetEase(Ease.OutCubic).SetUpdate(true));

        seq.SetLink(gameObject);

        seq.OnComplete(() =>
        {
            if (dialogueBox != null)
            {
                dialogueBox.blocksRaycasts = true;
                dialogueBox.interactable = true;
            }

            if (skipButton != null)
            {
                skipButton.blocksRaycasts = true;
                skipButton.interactable = true;

                DOVirtual.DelayedCall(skipButtonFadeDelay, () =>
                {
                    if (skipButton != null)
                        skipButton.DOFade(1f, skipButtonFadeTime).SetUpdate(true).SetLink(gameObject);
                }).SetUpdate(true).SetLink(gameObject);
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _inputEnabled = true;
            PlayLine(_currentLine);
        });
    }

    private void PlayLine(int index)
    {
        if (index >= dialogueLines.Count)
        {
            GoToNextScene();
            return;
        }

        if (dialogueText != null)
            dialogueText.text = "";

        _charCount = 0;
        _isTyping = true;

        if (useLineStartScale && dialogueText != null)
        {
            dialogueText.transform.DOKill();
            dialogueText.transform.localScale = Vector3.one * lineStartScaleAmount;

            dialogueText.transform.DOScale(Vector3.one, lineStartScaleTime)
                .SetEase(Ease.OutBack)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        if (_typeRoutine != null)
            StopCoroutine(_typeRoutine);

        _typeRoutine = StartCoroutine(TypeLine(dialogueLines[index]));
    }

    private IEnumerator TypeLine(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (dialogueText != null)
                dialogueText.text += c;

            _charCount++;

            if (_charCount % blipEveryNChars == 0 && c != ' ')
                AudioManager.Instance?.PlayUI(blipSoundId);

            float delay = _fastMode ? charDelayFast : charDelay;

            if (!_fastMode)
            {
                if (c == ',' || c == ';')
                    delay = commaPause;
                else if (c == '.' || c == '!' || c == '?')
                    delay = periodPause;
            }

            yield return new WaitForSecondsRealtime(delay);

            _fastMode = Input.GetMouseButton(0);
        }

        _isTyping = false;
    }

    private void AdvanceLine()
    {
        AudioManager.Instance?.PlayUI(advanceSoundId);

        _currentLine++;

        if (_currentLine >= dialogueLines.Count)
        {
            GoToNextScene();
            return;
        }

        PlayLine(_currentLine);
    }

    private void GoToNextScene()
    {
        if (_isTransitioning) return;

        _isTransitioning = true;
        _inputEnabled = false;

        if (_typeRoutine != null)
            StopCoroutine(_typeRoutine);

        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        // Al volver al Main Menu, dejamos el cursor visible.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (SceneFader.Instance != null)
        {
            SceneFader.Instance.FadeToSceneWithFadeIn(nextSceneName);
        }
        else
        {
            SceneManager.LoadScene(nextSceneName);
        }
    }

    #endregion

    #region Botón Skip

    public void OnSkipPressed()
    {
        if (_isTransitioning) return;

        _isTransitioning = true;
        _inputEnabled = false;

        if (_typeRoutine != null)
            StopCoroutine(_typeRoutine);

        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        // Al usar Skip, también dejamos el cursor visible para la siguiente escena UI.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        AudioManager.Instance?.PlayUI(advanceSoundId);

        if (SceneFader.Instance != null)
        {
            SceneFader.Instance.FadeToSceneWithFadeIn(nextSceneName, 0.5f);
        }
        else
        {
            SceneManager.LoadScene(nextSceneName);
        }
    }

    #endregion
}