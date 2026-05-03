using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

/// <summary>
/// Slot individual del carrusel. Sólo gestiona la parte visual (sprite/model preview).
/// Los textos (Name/Count/Description) viven a nivel del CarouselUI, no del slot.
///
/// El campo "spriteGraphic" acepta Image o RawImage indistintamente:
///   - Si es Image: se asigna data.icon como sprite.
///   - Si es RawImage: se asigna data.icon.texture.
/// Esto permite que el prefab del slot use cualquiera de los dos componentes.
/// </summary>
public class CarouselSlot : MonoBehaviour
{
    [Header("Refs UI (auto-wire si están vacías)")]
    [SerializeField] private RectTransform root;
    [Tooltip("Image o RawImage que muestra el icono 2D del item.")]
    [FormerlySerializedAs("iconImage")]
    [SerializeField] private Graphic spriteGraphic;
    [Tooltip("RawImage opcional para preview 3D (queda apagado salvo en el central por ItemPreview3D).")]
    [SerializeField] private RawImage modelPreview;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Visual")]
    [Tooltip("Alpha del slot cuando está en el centro (seleccionado).")]
    [SerializeField] [Range(0f, 1f)] private float centerAlpha = 1f;
    [Tooltip("Alpha de los slots laterales (translúcidos/apagados).")]
    [SerializeField] [Range(0f, 1f)] private float sideAlpha = 0.45f;

    private ItemData _data;
    private int _count;
    private bool _isCenter;

    public ItemData Data => _data;
    public bool IsCenter => _isCenter;
    public RectTransform Root => root != null ? root : (RectTransform)transform;

    private void Awake()
    {
        if (root == null) root = (RectTransform)transform;
        AutoWireFromChildren();
    }

    // Búsqueda recursiva, case-insensitive, por substring del nombre del GameObject.
    // spriteGraphic: cualquier Graphic (Image o RawImage) cuyo nombre contenga "sprite" o "icon".
    // modelPreview:  RawImage cuyo nombre contenga "model" o "preview3d".
    private void AutoWireFromChildren()
    {
        if (spriteGraphic == null) spriteGraphic = FindByNameContains<Graphic>("sprite", "icon");
        if (modelPreview == null) modelPreview = FindByNameContains<RawImage>("model", "preview3d");
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
    }

    private T FindByNameContains<T>(params string[] keywords) where T : Component
    {
        var all = GetComponentsInChildren<T>(true);
        foreach (var c in all)
        {
            string n = c.gameObject.name.ToLowerInvariant();
            for (int i = 0; i < keywords.Length; i++)
                if (n.Contains(keywords[i])) return c;
        }
        return null; // sin fallback: si no hay match por nombre, mejor null que el componente equivocado
    }

    public void SetData(ItemData data, int count)
    {
        _data = data;
        _count = count;
        ApplyVisuals();
    }

    public void SetCenter(bool isCenter)
    {
        _isCenter = isCenter;
        if (canvasGroup != null)
            canvasGroup.alpha = isCenter ? centerAlpha : sideAlpha;
        ApplyVisuals();
    }

    // Decide qué se ve (sprite vs model preview) en función del data y de si este
    // slot es el central. Se llama desde SetData y desde SetCenter para que el
    // estado siempre refleje la combinación actual de ambos.
    private void ApplyVisuals()
    {
        if (_data == null)
        {
            if (spriteGraphic != null) spriteGraphic.enabled = false;
            if (modelPreview != null) modelPreview.enabled = false;
            return;
        }

        bool isModel = (_data.previewMode == ItemData.PreviewMode.Model3D);

        // Model preview: SOLO en el slot central y SOLO si el item es modo Model3D.
        // En ese caso le asignamos la RenderTexture del singleton ItemPreview3D para
        // que muestre el modelo que la cámara renderiza. Los laterales / no-Model3D
        // tienen el RawImage apagado.
        if (modelPreview != null)
        {
            bool showModel = isModel && _isCenter;
            modelPreview.enabled = showModel;
            if (showModel && ItemPreview3D.Instance != null && ItemPreview3D.Instance.RT != null)
                modelPreview.texture = ItemPreview3D.Instance.RT;
        }

        // Sprite: se muestra siempre que NO sea Model3D, o aunque sea Model3D si
        // este slot no es el central (queremos que los slots laterales muestren
        // un icono 2D del item en lugar de quedarse vacíos).
        if (spriteGraphic != null)
        {
            bool showSprite = !isModel || !_isCenter;
            bool hasIcon = _data.icon != null;
            spriteGraphic.enabled = showSprite && hasIcon;

            if (showSprite && hasIcon)
            {
                if (spriteGraphic is Image img)
                {
                    img.sprite = _data.icon;
                    img.preserveAspect = true;
                }
                else if (spriteGraphic is RawImage raw)
                {
                    raw.texture = _data.icon.texture;
                }
            }
        }
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }
}
