using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using DG.Tweening;

/// <summary>
/// Feedback visual (escala + color) y de audio al hacer hover y click sobre un botón del menú.
/// Añadir este componente en el mismo GameObject que el Button de Unity.
/// El label TMP_Text es opcional; si no se asigna, solo se anima la escala.
/// </summary>
public class MenuButton : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    [Header("Escala")]
    [SerializeField] private float hoverScale = 1.08f;
    [SerializeField] private float clickScale = 0.93f;
    [SerializeField] private float scaleDuration = 0.14f;

    [Header("Color del Texto")]
    [SerializeField] private TMP_Text label;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color hoverColor = new Color(1f, 0.85f, 0.3f);
    [SerializeField] private Color clickColor = new Color(1f, 0.6f, 0.2f);
    [SerializeField] private float colorDuration = 0.12f;

    [Header("Audio")]
    [SerializeField] private string hoverSfxId = "ui_swap";

    private Vector3 _baseScale;
    private Tween _scaleTween;
    private Tween _colorTween;
    private bool _isHovered;

    private void Awake()
    {
        _baseScale = transform.localScale;
        if (label != null) label.color = normalColor;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _isHovered = true;
        AnimateTo(hoverScale, hoverColor);
        AudioManager.Instance?.PlayUI(hoverSfxId);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isHovered = false;
        AnimateTo(1f, normalColor);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        AnimateTo(clickScale, clickColor);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        // Vuelve a hover si el ratón sigue encima, o a normal si ya salió
        AnimateTo(_isHovered ? hoverScale : 1f, _isHovered ? hoverColor : normalColor);
    }

    private void AnimateTo(float targetScale, Color targetColor)
    {
        _scaleTween?.Kill();
        _colorTween?.Kill();

        _scaleTween = transform
            .DOScale(_baseScale * targetScale, scaleDuration)
            .SetEase(Ease.OutBack)
            .SetLink(gameObject);

        if (label != null)
            _colorTween = label
                .DOColor(targetColor, colorDuration)
                .SetLink(gameObject);
    }

    private void OnDestroy()
    {
        _scaleTween?.Kill();
        _colorTween?.Kill();
    }
}
