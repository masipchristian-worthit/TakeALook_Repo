using UnityEngine;
using DG.Tweening;

/// <summary>
/// Gestiona el menú principal: botones Play, Controls y Exit.
/// Incluye fade in inicial desde negro, panel de controles animado y música de fondo.
///
/// Dependencias:
///   - SceneFader (singleton persistente, necesita estar en escena o llegar de una anterior)
///   - AudioManager (singleton persistente)
///   - MenuButton (componente en cada botón para hover/click)
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    [Header("Escena a cargar al pulsar Play")]
    [SerializeField] private string nextSceneName = "IntroScene";

    [Header("Fade inicial")]
    [SerializeField] private float initialFadeInTime = 1.4f;

    [Header("Panel de Controles")]
    [SerializeField] private CanvasGroup controlsPanel;
    [SerializeField] private RectTransform controlsPanelRect;
    [SerializeField] private float controlsAnimTime = 0.32f;
    [Tooltip("Desplazamiento desde el que aparece el panel (ej. desde abajo).")]
    [SerializeField] private Vector2 controlsSlideOffset = new Vector2(0f, -60f);

    [Header("Música de Fondo")]
    [SerializeField] private AudioClip menuMusicClip;
    [SerializeField] private float musicFadeTime = 2f;
    [SerializeField] private float musicVolume = 0.6f;

    [Header("Audio - IDs en SoundLibrary")]
    [SerializeField] private string confirmSfxId = "ui_select";
    [SerializeField] private string openSfxId = "ui_open";
    [SerializeField] private string closeSfxId = "ui_close";

    private Vector2 _controlsBasePos;
    private bool _controlsOpen;
    private bool _isTransitioning;

    private void Start()
    {
        // Ocultar panel de controles desde el principio
        if (controlsPanel != null)
        {
            controlsPanel.alpha = 0f;
            controlsPanel.blocksRaycasts = false;
            controlsPanel.interactable = false;
        }

        if (controlsPanelRect != null)
            _controlsBasePos = controlsPanelRect.anchoredPosition;

        // Fade in inicial desde negro
        SceneFader.Instance?.FadeIn(initialFadeInTime);

        // Música con fade in
        if (menuMusicClip != null)
            AudioManager.Instance?.PlayMusic(menuMusicClip, musicFadeTime, musicVolume);
    }

    #region Botones públicos (asignar en los OnClick del Inspector)

    /// <summary>Llamar desde el botón Play/Start.</summary>
    public void OnPlayPressed()
    {
        if (_isTransitioning) return;
        _isTransitioning = true;

        AudioManager.Instance?.PlayUI(confirmSfxId);
        SceneFader.Instance?.FadeToScene(nextSceneName);
    }

    /// <summary>Llamar desde el botón Controls. Abre o cierra el panel.</summary>
    public void OnControlsPressed()
    {
        if (_controlsOpen) CloseControlsPanel();
        else OpenControlsPanel();
    }

    /// <summary>Llamar desde el botón Volver dentro del panel de controles.</summary>
    public void OnBackFromControls()
    {
        AudioManager.Instance?.PlayUI(closeSfxId);
        CloseControlsPanel();
    }

    /// <summary>Llamar desde el botón Exit/Quit.</summary>
    public void OnExitPressed()
    {
        AudioManager.Instance?.PlayUI(confirmSfxId);

#if UNITY_EDITOR
        Debug.Log("[MainMenuManager] Exit pulsado — en una build cerraría el juego.");
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    #endregion

    #region Panel de Controles

    private void OpenControlsPanel()
    {
        if (controlsPanel == null || _controlsOpen) return;
        _controlsOpen = true;

        AudioManager.Instance?.PlayUI(openSfxId);

        // Posición de entrada (desplazada)
        if (controlsPanelRect != null)
            controlsPanelRect.anchoredPosition = _controlsBasePos + controlsSlideOffset;

        controlsPanel.blocksRaycasts = true;
        controlsPanel.interactable = true;

        Sequence seq = DOTween.Sequence().SetUpdate(true);
        seq.Join(controlsPanel.DOFade(1f, controlsAnimTime).SetEase(Ease.OutQuad));
        if (controlsPanelRect != null)
            seq.Join(controlsPanelRect.DOAnchorPos(_controlsBasePos, controlsAnimTime).SetEase(Ease.OutCubic));
        seq.SetLink(gameObject);
    }

    private void CloseControlsPanel()
    {
        if (controlsPanel == null || !_controlsOpen) return;
        _controlsOpen = false;

        Sequence seq = DOTween.Sequence().SetUpdate(true);
        seq.Join(controlsPanel.DOFade(0f, controlsAnimTime).SetEase(Ease.InQuad));
        if (controlsPanelRect != null)
            seq.Join(controlsPanelRect.DOAnchorPos(_controlsBasePos + controlsSlideOffset, controlsAnimTime).SetEase(Ease.InCubic));
        seq.OnComplete(() =>
        {
            controlsPanel.blocksRaycasts = false;
            controlsPanel.interactable = false;
        });
        seq.SetLink(gameObject);
    }

    #endregion
}
