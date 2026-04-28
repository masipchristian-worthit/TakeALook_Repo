using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Carrusel UI estilo Silent Hill. Muestra una lista de items con un slot central destacado.
/// 
/// Características:
///   - N slots visibles configurable (default 5: -2, -1, center, +1, +2).
///   - Slide animation al navegar.
///   - Slot central:
///       Sprite -> hover + shake mínimo
///       3D Model -> rotación + hover (gestionado por ItemPreview3D)
///   - Soporta wrap-around opcional.
///   - El feed (lista de entries) lo proporciona quien instancia el carrusel.
/// </summary>
public class CarouselUI : MonoBehaviour
{
    public struct Entry
    {
        public ItemData data;
        public int count;
    }

    [Header("Slots")]
    [SerializeField] private CarouselSlot slotPrefab;
    [SerializeField] private RectTransform slotsContainer;
    [SerializeField] private int visibleSlots = 5;
    [SerializeField] private float slotSpacing = 140f;

    [Header("Visual")]
    [SerializeField] private float centerScale = 1.3f;
    [SerializeField] private float sideScale = 0.8f;
    [SerializeField] private float farScale = 0.55f;
    [SerializeField] private float scrollAnimTime = 0.25f;
    [SerializeField] private Ease scrollEase = Ease.OutCubic;

    [Header("Selección - Sprite (hover + shake)")]
    [SerializeField] private float spriteHoverAmount = 8f;
    [SerializeField] private float spriteHoverSpeed = 1.5f;
    [SerializeField] private float spriteShakeStrength = 2f;
    [SerializeField] private float spriteShakeInterval = 0.12f;

    [Header("Audio")]
    [SerializeField] private string scrollSfxId = "ui_swap";
    [SerializeField] private string emptySfxId = "ui_deny";

    [Header("Empty State")]
    [SerializeField] private TMP_Text emptyMessage;
    [SerializeField] private string emptyText = "— Vacío —";

    [Header("Focus (animación al recibir foco)")]
    [SerializeField] private CanvasGroup focusCanvasGroup;
    [SerializeField] private float focusedAlpha = 1f;
    [SerializeField] private float unfocusedAlpha = 0.5f;

    private readonly List<CarouselSlot> _slots = new List<CarouselSlot>();
    private List<Entry> _entries = new List<Entry>();
    private int _centerIndex = 0;
    private Tween _spriteHoverTween;
    private Tween _spriteShakeTween;
    private bool _hasFocus = true;

    public int CenterIndex => _centerIndex;
    public int Count => _entries.Count;
    public Entry? CurrentEntry => _entries.Count == 0 ? (Entry?)null : _entries[_centerIndex];

    private void Awake()
    {
        BuildSlots();
    }

    private void BuildSlots()
    {
        if (slotPrefab == null || slotsContainer == null) return;

        for (int i = 0; i < visibleSlots; i++)
        {
            var slot = Instantiate(slotPrefab, slotsContainer);
            _slots.Add(slot);
        }
        LayoutSlotsInstant();
    }

    private void LayoutSlotsInstant()
    {
        int half = visibleSlots / 2;
        for (int i = 0; i < _slots.Count; i++)
        {
            int offset = i - half;
            var rt = _slots[i].Root;
            rt.anchoredPosition = new Vector2(offset * slotSpacing, 0f);
            float scale = (offset == 0) ? centerScale : (Mathf.Abs(offset) == 1 ? sideScale : farScale);
            rt.localScale = Vector3.one * scale;
        }
    }

    public void SetEntries(List<Entry> entries, int? newCenterIndex = null, bool animate = true)
    {
        _entries = entries ?? new List<Entry>();
        if (newCenterIndex.HasValue) _centerIndex = newCenterIndex.Value;
        _centerIndex = Mathf.Clamp(_centerIndex, 0, Mathf.Max(0, _entries.Count - 1));
        Refresh(animate);
    }

    public void SetFocus(bool focused)
    {
        _hasFocus = focused;
        if (focusCanvasGroup != null)
            focusCanvasGroup.DOFade(focused ? focusedAlpha : unfocusedAlpha, 0.2f).SetLink(gameObject);

        ApplyCenterFeedback(); // re-evalúa anim según foco
    }

    public void Next()
    {
        if (_entries.Count == 0) { AudioManager.Instance?.PlayUI(emptySfxId); return; }
        int newIdx = (_centerIndex + 1) % _entries.Count;
        ScrollTo(newIdx);
    }

    public void Previous()
    {
        if (_entries.Count == 0) { AudioManager.Instance?.PlayUI(emptySfxId); return; }
        int newIdx = (_centerIndex - 1 + _entries.Count) % _entries.Count;
        ScrollTo(newIdx);
    }

    public void ScrollTo(int newCenterIndex)
    {
        if (newCenterIndex == _centerIndex) return;
        _centerIndex = newCenterIndex;
        AudioManager.Instance?.PlayUI(scrollSfxId);
        Refresh(true);
    }

    private void Refresh(bool animate)
    {
        if (emptyMessage != null) { emptyMessage.text = emptyText; emptyMessage.gameObject.SetActive(_entries.Count == 0); }

        int half = visibleSlots / 2;

        for (int i = 0; i < _slots.Count; i++)
        {
            int offset = i - half;
            int dataIdx = _centerIndex + offset;

            // Wrap-around si hay >= visibleSlots elementos; si no, dejamos slots vacíos en los extremos.
            ItemData data = null; int count = 0;
            if (_entries.Count > 0)
            {
                if (_entries.Count >= visibleSlots)
                {
                    int wrapped = ((dataIdx % _entries.Count) + _entries.Count) % _entries.Count;
                    data = _entries[wrapped].data;
                    count = _entries[wrapped].count;
                }
                else if (dataIdx >= 0 && dataIdx < _entries.Count)
                {
                    data = _entries[dataIdx].data;
                    count = _entries[dataIdx].count;
                }
            }

            _slots[i].SetData(data, count);
            _slots[i].SetVisible(data != null);
            _slots[i].SetCenter(offset == 0);

            // Animación de posición/escala
            float targetScale = (offset == 0) ? centerScale : (Mathf.Abs(offset) == 1 ? sideScale : farScale);
            Vector2 targetPos = new Vector2(offset * slotSpacing, 0f);
            var rt = _slots[i].Root;

            if (animate)
            {
                rt.DOAnchorPos(targetPos, scrollAnimTime).SetEase(scrollEase).SetLink(rt.gameObject);
                rt.DOScale(targetScale, scrollAnimTime).SetEase(scrollEase).SetLink(rt.gameObject);
            }
            else
            {
                rt.anchoredPosition = targetPos;
                rt.localScale = Vector3.one * targetScale;
            }
        }

        ApplyCenterFeedback();
    }

    private void ApplyCenterFeedback()
    {
        // Localizar el slot central
        int half = visibleSlots / 2;
        if (half >= _slots.Count) return;
        var centerSlot = _slots[half];

        // Cancelar anims previas
        _spriteHoverTween?.Kill();
        _spriteShakeTween?.Kill();
        if (centerSlot != null) centerSlot.Root.localPosition = new Vector3(0, 0, centerSlot.Root.localPosition.z);

        var entry = CurrentEntry;
        if (entry == null || entry.Value.data == null)
        {
            ItemPreview3D.Instance?.Hide();
            return;
        }

        var data = entry.Value.data;

        if (data.previewMode == ItemData.PreviewMode.Model3D)
        {
            // El modelo 3D se gestiona por el preview singleton
            ItemPreview3D.Instance?.Show(data);
            ItemPreview3D.Instance?.SetSelected(_hasFocus);
        }
        else
        {
            ItemPreview3D.Instance?.Hide();

            if (_hasFocus && centerSlot != null)
            {
                // Hover Y bob
                float dur = 1f / Mathf.Max(0.1f, spriteHoverSpeed);
                _spriteHoverTween = centerSlot.Root.DOAnchorPosY(spriteHoverAmount, dur * 0.5f)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetLink(centerSlot.gameObject);

                // Shake mínimo periódico
                _spriteShakeTween = DOVirtual.DelayedCall(spriteShakeInterval, () =>
                {
                    if (centerSlot != null)
                        centerSlot.Root.DOShakeAnchorPos(0.15f, spriteShakeStrength, 8, 90, false, true).SetLink(centerSlot.gameObject);
                }).SetLoops(-1).SetLink(centerSlot.gameObject);
            }
        }
    }

    public bool HasFocus => _hasFocus;

    private void OnDestroy()
    {
        _spriteHoverTween?.Kill();
        _spriteShakeTween?.Kill();
    }
}