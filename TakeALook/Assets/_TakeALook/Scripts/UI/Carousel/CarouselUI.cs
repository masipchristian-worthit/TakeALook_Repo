using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Carrusel UI estilo Silent Hill / RE4. Sin animaciones internas DOTween: cuando el
/// contenido cambia o se consume un item se dispara un overlay de estática pixelada
/// (StaticNoiseOverlay) que actúa como "transición". Posiciones, escalas y alphas
/// se aplican instantáneamente bajo el ruido.
///
/// Los textos (Name/Count/Description) son COMPARTIDOS a nivel del carrusel y siempre
/// reflejan el item central. Cada slot sólo muestra su sprite/model preview.
/// </summary>
public class CarouselUI : MonoBehaviour
{
    public enum FocusState { Unfocused = 0, Preview = 1, Focused = 2 }
    public enum CarouselKind { Auto = 0, Ammo = 1, Inventory = 2 }

    public struct Entry
    {
        public ItemData data;
        public int count;
        // Si > 0, se muestra junto a count en el reserveLabel: "count / maxCount".
        // Para Ammo: count = balas en cargador, maxCount = reserva máxima del tipo.
        // Para Inventory: count = stack actual, maxCount = ItemData.maxStack.
        public int maxCount;
    }

    [Header("Slots")]
    [Tooltip("GameObject base del slot (se le añade CarouselSlot si no lo tiene).")]
    [SerializeField] private GameObject slotTemplate;
    [SerializeField] private RectTransform slotsContainer;
    [SerializeField] private int visibleSlots = 3;
    [SerializeField] private float slotSpacing = 140f;

    [Header("Visual")]
    [SerializeField] private float centerScale = 1.3f;
    [SerializeField] private float sideScale = 0.8f;
    [SerializeField] private float farScale = 0.55f;

    [Header("Audio")]
    [SerializeField] private string scrollSfxId = "ui_swap";
    [SerializeField] private string emptySfxId = "ui_deny";

    [Header("Tipo de carrusel")]
    [Tooltip("Auto = se detecta por el nombre del GameObject (busca 'ammo' o 'inventory'). Forzar a Ammo/Inventory si el nombre no es claro.")]
    [SerializeField] private CarouselKind carouselKind = CarouselKind.Auto;

    [Header("Labels compartidos (auto-wire si están vacíos)")]
    [Tooltip("Muestra el nombre del item central. Si está vacío, se busca un hijo cuyo nombre contenga 'name' o 'title'.")]
    [SerializeField] private TMP_Text nameLabel;
    [Tooltip("Muestra el contador del item central. Auto-wire por nombre 'count' o 'amount'.")]
    [SerializeField] private TMP_Text countLabel;
    [Tooltip("Muestra la descripción del item central. Auto-wire por nombre 'desc'.")]
    [SerializeField] private TMP_Text descriptionLabel;
    [Tooltip("Muestra 'balas en inventario / balas máximas' (o stack/maxStack para inventario). Auto-wire por nombre 'reserve', 'max', 'capacity' o 'ammo'.")]
    [SerializeField] private TMP_Text reserveLabel;
    [Tooltip("Formato: {0}=actual {1}=máximo. Por defecto '{0} / {1}'.")]
    [SerializeField] private string reserveFormat = "{0} / {1}";

    [Header("Empty State")]
    [SerializeField] private TMP_Text emptyMessage;
    [SerializeField] private string emptyText = "— Vacío —";

    [Header("Focus")]
    [SerializeField] private CanvasGroup focusCanvasGroup;
    [Range(0f, 1f)] [SerializeField] private float focusedAlpha = 1f;
    [Range(0f, 1f)] [SerializeField] private float unfocusedAlpha = 0.5f;

    [Header("Borde de foco")]
    [SerializeField] private Image focusBorder;
    [Tooltip("Color del borde cuando el carrusel tiene foco completo (estás dentro de items, has pulsado E).")]
    [SerializeField] private Color focusedBorderColor = new Color(1f, 0.85f, 0.3f, 1f);
    [Tooltip("Color del borde cuando el carrusel está preseleccionado (pestaña activa pero sin entrar todavía con E).")]
    [SerializeField] private Color previewBorderColor = new Color(1f, 1f, 1f, 0.6f);
    [Tooltip("Color del borde cuando el carrusel no es la pestaña activa.")]
    [SerializeField] private Color unfocusedBorderColor = new Color(1f, 1f, 1f, 0.25f);

    [Header("Highlight (pulse en Update sobre el slot central con foco)")]
    [Tooltip("Velocidad del pulse de selección en rad/s.")]
    [SerializeField] private float highlightPulseSpeed = 6f;
    [Tooltip("Amplitud del pulse de escala (fracción de centerScale).")]
    [Range(0f, 0.2f)]
    [SerializeField] private float highlightScaleAmount = 0.06f;
    [Tooltip("Amplitud del pulse de alpha del borde de foco.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float highlightBorderAlphaAmount = 0.25f;

    [Header("Estática (refresco al cambiar/consumir)")]
    [SerializeField] private StaticNoiseOverlay staticOverlay;

    [Header("Diagnóstico")]
    [Tooltip("Si está activo, comprueba al arrancar errores de setup (LayoutGroup, ContentSizeFitter, etc).")]
    [SerializeField] private bool diagnoseLayout = true;

    private readonly List<CarouselSlot> _slots = new List<CarouselSlot>();
    private List<Entry> _entries = new List<Entry>();
    private int _centerIndex = 0;
    private bool _hasFocus = true;
    private FocusState _focusState = FocusState.Unfocused;
    private int _lastAmmoIndex = 0;
    private int _lastInventoryIndex = 0;
    private bool _isAmmoCarousel = true;

    public int CenterIndex => _centerIndex;
    public int Count => _entries.Count;
    public Entry? CurrentEntry => _entries.Count == 0 ? (Entry?)null : _entries[_centerIndex];
    public bool HasFocus => _hasFocus;
    public CarouselKind Kind => carouselKind != CarouselKind.Auto
        ? carouselKind
        : (_isAmmoCarousel ? CarouselKind.Ammo : CarouselKind.Inventory);

    private void Awake()
    {
        // Resuelve el tipo: el campo del inspector tiene prioridad; en Auto se mira el nombre.
        if (carouselKind == CarouselKind.Auto)
            _isAmmoCarousel = gameObject.name.ToLowerInvariant().Contains("ammo");
        else
            _isAmmoCarousel = (carouselKind == CarouselKind.Ammo);

        AutoWireLabels();
        DiagnoseLayout();
        BuildSlots();
        // Nota: ya no se asigna un targetRawImage centralizado al ItemPreview3D.
        // Cada CarouselSlot enciende su propio modelPreview cuando es el central
        // y le pega la RenderTexture del singleton (ver CarouselSlot.ApplyVisuals).
    }

    private void AutoWireLabels()
    {
        if (nameLabel == null) nameLabel = FindLabel("name", "title");
        if (countLabel == null) countLabel = FindLabel("count", "amount");
        if (descriptionLabel == null) descriptionLabel = FindLabel("desc");
        if (reserveLabel == null) reserveLabel = FindLabel("reserve", "max", "capacity", "ammo");
    }

    private TMP_Text FindLabel(params string[] keywords)
    {
        var all = GetComponentsInChildren<TMP_Text>(true);
        foreach (var t in all)
        {
            string n = t.gameObject.name.ToLowerInvariant();
            for (int i = 0; i < keywords.Length; i++)
                if (n.Contains(keywords[i])) return t;
        }
        return null;
    }

    private void DiagnoseLayout()
    {
        if (!diagnoseLayout) return;

        if (slotsContainer == null)
        {
            Debug.LogError($"[CarouselUI:{name}] slotsContainer es null. Los slots no se podrán colocar.", this);
            return;
        }

        var lg = slotsContainer.GetComponent<LayoutGroup>();
        if (lg != null)
            Debug.LogError($"[CarouselUI:{name}] El slotsContainer tiene un {lg.GetType().Name}. ELIMÍNALO: sobrescribe las anchoredPosition que asigna el carrusel y los slots aparecen apilados.", lg);

        var csf = slotsContainer.GetComponent<ContentSizeFitter>();
        if (csf != null)
            Debug.LogWarning($"[CarouselUI:{name}] El slotsContainer tiene un ContentSizeFitter. Puede recortar el rect y ocultar slots laterales.", csf);

        if (slotSpacing <= 0.01f)
            Debug.LogError($"[CarouselUI:{name}] slotSpacing={slotSpacing}. Los slots quedarán todos en x=0 (apilados).", this);

        var rect = slotsContainer.rect;
        if (rect.width < slotSpacing * visibleSlots)
            Debug.LogWarning($"[CarouselUI:{name}] slotsContainer width={rect.width:0.0} < slotSpacing*visibleSlots={slotSpacing * visibleSlots:0.0}. Si hay un Mask/RectMask2D, los laterales pueden quedar recortados.", this);

        if (slotsContainer.GetComponent<RectMask2D>() != null || slotsContainer.GetComponent<Mask>() != null)
            Debug.Log($"[CarouselUI:{name}] slotsContainer tiene Mask. Asegúrate de que su rect cubre todos los slots ({slotSpacing * visibleSlots:0.0} de ancho mínimo).", this);

        if (slotTemplate != null)
        {
            var tplRT = slotTemplate.transform as RectTransform;
            if (tplRT != null && (tplRT.anchorMin != tplRT.anchorMax))
                Debug.LogWarning($"[CarouselUI:{name}] slotTemplate tiene anchors en stretch (min={tplRT.anchorMin}, max={tplRT.anchorMax}). Pon el anchor en un punto único (p.ej. center/middle) o los slots se estirarán al tamaño del padre y se solaparán.", slotTemplate);
        }
    }

    private void BuildSlots()
    {
        if (slotTemplate == null || slotsContainer == null)
        {
            Debug.LogError($"[CarouselUI:{name}] No se pueden construir slots: slotTemplate o slotsContainer es null.", this);
            return;
        }

        // Si el template es un objeto de escena (no un prefab asset puro), ocultarlo
        // para que la copia original no aparezca junto a los clones gestionados.
        // scene.IsValid() distingue objetos de escena de prefab assets en disco.
        if (slotTemplate.scene.IsValid())
            slotTemplate.SetActive(false);

        for (int i = 0; i < visibleSlots; i++)
        {
            var go = Instantiate(slotTemplate, slotsContainer);
            go.SetActive(true);
            var slot = go.GetComponent<CarouselSlot>() ?? go.AddComponent<CarouselSlot>();
            _slots.Add(slot);
        }
        LayoutSlots();
        Debug.Log($"[CarouselUI:{name}] BuildSlots OK — {_slots.Count} slots creados, spacing={slotSpacing}.", this);
    }

    private void LayoutSlots()
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

    public void SetEntries(List<Entry> entries, int? newCenterIndex = null, bool flashOnChange = false)
    {
        _entries = entries ?? new List<Entry>();
        if (newCenterIndex.HasValue) _centerIndex = newCenterIndex.Value;
        _centerIndex = Mathf.Clamp(_centerIndex, 0, Mathf.Max(0, _entries.Count - 1));
        Refresh();
        if (flashOnChange) staticOverlay?.Flash();
    }

    public void SetFocus(bool focused)
    {
        SetFocusState(focused ? FocusState.Focused : FocusState.Unfocused);
    }

    public void SetFocusState(FocusState focusState)
    {
        FocusState oldState = _focusState;
        _focusState = focusState;
        _hasFocus = (focusState == FocusState.Focused);

        if (focusState != FocusState.Unfocused && oldState == FocusState.Unfocused)
        {
            RestoreSavedIndex();
        }
        else if (focusState == FocusState.Unfocused && oldState != FocusState.Unfocused)
        {
            SaveCurrentIndex();
        }

        // El alpha del carrusel SIEMPRE se mantiene a focusedAlpha: queremos que
        // los slots de ambos carruseles permanezcan visibles aunque sólo uno tenga
        // el foco. La distinción visual es el borde + el pulse del slot central.
        if (focusCanvasGroup != null)
            focusCanvasGroup.alpha = focusedAlpha;

        // El color del borde lo escribe Update() cada frame en función de _focusState,
        // así que aquí basta con dejar la inicialización para el primer frame.
        if (focusBorder != null)
            focusBorder.color = ColorForState(_focusState);

        ApplyCenterPreview();
    }

    private void SaveCurrentIndex()
    {
        if (_isAmmoCarousel)
            _lastAmmoIndex = _centerIndex;
        else
            _lastInventoryIndex = _centerIndex;
    }

    private void RestoreSavedIndex()
    {
        int savedIndex = _isAmmoCarousel ? _lastAmmoIndex : _lastInventoryIndex;
        _centerIndex = Mathf.Clamp(savedIndex, 0, Mathf.Max(0, _entries.Count - 1));
        Refresh();
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
        staticOverlay?.Flash();
        Refresh();
    }

    /// <summary>Dispara la estática manualmente (p.ej. tras consumir un item).</summary>
    public void FlashStatic() => staticOverlay?.Flash();

    /// <summary>Intenta consumir el item seleccionado del carrusel.</summary>
    public bool TryConsumeCurrentItem(GameObject user)
    {
        var entry = CurrentEntry;
        if (entry == null || entry.Value.data == null)
            return false;

        bool consumed = entry.Value.data.Use(user);
        if (consumed)
            FlashStatic();

        return consumed;
    }

    private void Refresh()
    {
        if (emptyMessage != null)
        {
            emptyMessage.text = emptyText;
            emptyMessage.gameObject.SetActive(_entries.Count == 0);
        }

        int half = visibleSlots / 2;
        for (int i = 0; i < _slots.Count; i++)
        {
            int offset = i - half;
            int dataIdx = _centerIndex + offset;

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
            // El slot central NUNCA se desactiva, aunque no haya item: siempre debe verse
            // como "marco" del carrusel para que la transición entre pestañas no parpadee.
            bool isCenter = (offset == 0);
            _slots[i].SetVisible(isCenter || data != null);
            _slots[i].SetCenter(isCenter);

            // Posición y escala instantáneas (la transición la cubre la estática)
            var rt = _slots[i].Root;
            rt.anchoredPosition = new Vector2(offset * slotSpacing, 0f);
            float targetScale = (offset == 0) ? centerScale : (Mathf.Abs(offset) == 1 ? sideScale : farScale);
            rt.localScale = Vector3.one * targetScale;
        }

        // El slot central debe renderizarse POR ENCIMA de los laterales (último sibling).
        BringCenterToFront();

        UpdateCenterLabels();
        ApplyCenterPreview();
    }

    private void BringCenterToFront()
    {
        int half = visibleSlots / 2;
        if (half >= _slots.Count) return;
        var center = _slots[half];
        if (center != null) center.transform.SetAsLastSibling();
    }

    // Pulse continuo del slot central cuando el carrusel tiene el foco. Driven from Update
    // para que sea visible aunque no haya navegación. No usa DOTween (instantáneo + sine).
    private void Update()
    {
        if (_slots.Count == 0) return;
        int half = visibleSlots / 2;
        if (half >= _slots.Count) return;

        var center = _slots[half];
        if (center == null) return;

        if (_focusState == FocusState.Focused)
        {
            float wave = Mathf.Sin(Time.unscaledTime * highlightPulseSpeed); // -1..1
            float pulse = 1f + (wave * 0.5f + 0.5f) * highlightScaleAmount;  // 1..1+amount
            center.Root.localScale = Vector3.one * centerScale * pulse;

            if (focusBorder != null)
            {
                Color c = focusedBorderColor;
                c.a = Mathf.Clamp01(focusedBorderColor.a + wave * highlightBorderAlphaAmount);
                focusBorder.color = c;
            }
        }
        else
        {
            // Preview / Unfocused: escala fija, borde en su color correspondiente.
            center.Root.localScale = Vector3.one * centerScale;
            if (focusBorder != null)
                focusBorder.color = ColorForState(_focusState);
        }
    }

    private Color ColorForState(FocusState state)
    {
        switch (state)
        {
            case FocusState.Focused: return focusedBorderColor;
            case FocusState.Preview: return previewBorderColor;
            default: return unfocusedBorderColor;
        }
    }

    private void UpdateCenterLabels()
    {
        var entry = CurrentEntry;
        bool hasItem = entry != null && entry.Value.data != null;

        if (nameLabel != null) nameLabel.text = hasItem ? entry.Value.data.displayName : "";
        if (countLabel != null)
        {
            if (hasItem && _isAmmoCarousel)
                countLabel.text = $"{entry.Value.count}";
            else
                countLabel.text = (hasItem && entry.Value.data.isStackable) ? $"x{entry.Value.count}" : "";
        }
        if (descriptionLabel != null)
            descriptionLabel.text = hasItem ? (entry.Value.data.description ?? "") : "";

        // Reserve / max: sólo se muestra si la entrada tiene un maxCount > 0.
        if (reserveLabel != null)
        {
            if (hasItem && entry.Value.maxCount > 0)
                reserveLabel.text = string.Format(reserveFormat, entry.Value.count, entry.Value.maxCount);
            else
                reserveLabel.text = "";
        }
    }

    private void ApplyCenterPreview()
    {
        var entry = CurrentEntry;
        if (entry == null || entry.Value.data == null)
        {
            ItemPreview3D.Instance?.Hide();
            return;
        }

        var data = entry.Value.data;
        if (data.previewMode == ItemData.PreviewMode.Model3D)
        {
            ItemPreview3D.Instance?.Show(data);
            ItemPreview3D.Instance?.SetSelected(_hasFocus);
        }
        else
        {
            ItemPreview3D.Instance?.Hide();
        }
    }
}
