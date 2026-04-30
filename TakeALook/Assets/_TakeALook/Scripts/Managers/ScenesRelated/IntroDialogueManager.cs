using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using DG.Tweening;

/// <summary>
/// Gestiona la escena de introducción: fondo negro, caja de diálogo con efecto máquina de escribir,
/// botón Skip y transición automática a la siguiente escena al terminar.
///
/// Configuración mínima:
///   - Añadir líneas de diálogo en el Inspector.
///   - Asignar el nombre de la siguiente escena.
///   - Conectar las referencias de UI.
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
    private int _charCount;
    private Coroutine _typeRoutine;
    private Vector2 _boxBasePos;

    #region Unity Messages

    private void Start()
    {
        _inputEnabled = false;

        // Ocultar UI
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
        }
        if (dialogueText != null) dialogueText.text = "";

        if (dialogueBoxRect != null)
            _boxBasePos = dialogueBoxRect.anchoredPosition;

        // Fade in desde negro y luego arrancar el diálogo
        SceneFader.Instance?.FadeIn(fadeInDuration, OnFadeInComplete);
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
                // El modo fast se activa en el coroutine vía polling; nada extra aquí.
            }
            else
            {
                AdvanceLine();
            }
        }
    }

    #endregion

    #region Flujo principal

    private void OnFadeInComplete()
    {
        StartCoroutine(DelayedStart());
    }

    private IEnumerator DelayedStart()
    {
        yield return new WaitForSeconds(preFadeDialogueDelay);

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

        if (dialogueBox != null)
            seq.Join(dialogueBox.DOFade(1f, boxFadeInTime).SetEase(Ease.OutQuad));

        if (dialogueBoxRect != null)
            seq.Join(dialogueBoxRect.DOAnchorPos(_boxBasePos, boxFadeInTime).SetEase(Ease.OutCubic));

        seq.SetLink(gameObject);
        seq.OnComplete(() =>
        {
            if (dialogueBox != null)
            {
                dialogueBox.blocksRaycasts = true;
                dialogueBox.interactable = true;
            }

            // Mostrar botón Skip con retardo
            if (skipButton != null)
            {
                skipButton.blocksRaycasts = true;
                DOVirtual.DelayedCall(skipButtonFadeDelay, () =>
                {
                    skipButton.DOFade(1f, skipButtonFadeTime).SetLink(gameObject);
                }).SetLink(gameObject);
            }

            _inputEnabled = true;
            PlayLine(_currentLine);
        });
    }

    private void PlayLine(int index)
    {
        if (index >= dialogueLines.Count) { GoToNextScene(); return; }

        if (dialogueText != null) dialogueText.text = "";
        _charCount = 0;
        _isTyping = true;

        // Efecto de escala sutil al iniciar línea
        if (useLineStartScale && dialogueText != null)
        {
            dialogueText.transform.localScale = Vector3.one * lineStartScaleAmount;
            dialogueText.transform.DOScale(Vector3.one, lineStartScaleTime)
                .SetEase(Ease.OutBack)
                .SetLink(gameObject);
        }

        if (_typeRoutine != null) StopCoroutine(_typeRoutine);
        _typeRoutine = StartCoroutine(TypeLine(dialogueLines[index]));
    }

    private IEnumerator TypeLine(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (dialogueText != null) dialogueText.text += c;

            _charCount++;

            // Blip de audio
            if (_charCount % blipEveryNChars == 0 && c != ' ')
                AudioManager.Instance?.PlayUI(blipSoundId);

            // Calcular retardo
            float delay = _fastMode ? charDelayFast : charDelay;

            if (!_fastMode)
            {
                if (c == ',' || c == ';') delay = commaPause;
                else if (c == '.' || c == '!' || c == '?') delay = periodPause;
            }

            yield return new WaitForSeconds(delay);

            // Refrescar fast mode en cada frame del typing
            _fastMode = Input.GetMouseButton(0);
        }

        _isTyping = false;
    }

    private void AdvanceLine()
    {
        AudioManager.Instance?.PlayUI(advanceSoundId);

        _currentLine++;

        if (_currentLine >= dialogueLines.Count) { GoToNextScene(); return; }

        PlayLine(_currentLine);
    }

    private void GoToNextScene()
    {
        if (_isTransitioning) return;
        _isTransitioning = true;
        _inputEnabled = false;

        if (_typeRoutine != null) StopCoroutine(_typeRoutine);

        SceneFader.Instance?.FadeToScene(nextSceneName);
    }

    #endregion

    #region Botón Skip (asignar en OnClick del Inspector)

    /// <summary>Salta todo el diálogo y pasa a la siguiente escena.</summary>
    public void OnSkipPressed()
    {
        if (_isTransitioning) return;
        _isTransitioning = true;
        _inputEnabled = false;

        if (_typeRoutine != null) StopCoroutine(_typeRoutine);

        AudioManager.Instance?.PlayUI(advanceSoundId);
        SceneFader.Instance?.FadeToScene(nextSceneName, 0.5f);
    }

    #endregion
}
