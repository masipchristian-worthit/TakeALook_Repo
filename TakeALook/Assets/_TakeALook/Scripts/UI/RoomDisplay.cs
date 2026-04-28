using UnityEngine;
using TMPro;
using DG.Tweening;

/// <summary>
/// Muestra la sala actual del jugador escuchando GameManager.OnRoomChanged.
/// Animación de transición tipo "punch + fade" al cambiar de sala.
/// </summary>
public class RoomDisplay : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float fadeOutTime = 0.15f;
    [SerializeField] private float fadeInTime = 0.3f;
    [SerializeField] private string changeSfxId = "ui_swap";

    private Tween _fadeTween;

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnRoomChanged += HandleRoomChanged;
            ApplyText(GameManager.Instance.currentRoomName, instant: true);
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null) GameManager.Instance.OnRoomChanged -= HandleRoomChanged;
        _fadeTween?.Kill();
    }

    private void HandleRoomChanged(string newRoom) => ApplyText(newRoom, instant: false);

    private void ApplyText(string text, bool instant)
    {
        if (label == null) return;

        if (instant || canvasGroup == null)
        {
            label.text = text;
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            return;
        }

        _fadeTween?.Kill();
        AudioManager.Instance?.PlayUI(changeSfxId);

        Sequence seq = DOTween.Sequence();
        seq.Append(canvasGroup.DOFade(0f, fadeOutTime).SetEase(Ease.InQuad));
        seq.AppendCallback(() => label.text = text);
        seq.Append(canvasGroup.DOFade(1f, fadeInTime).SetEase(Ease.OutQuad));
        seq.Join(label.transform.DOPunchScale(Vector3.one * 0.1f, 0.25f, 6, 0.5f));
        seq.SetLink(gameObject);
        _fadeTween = seq;
    }
}
