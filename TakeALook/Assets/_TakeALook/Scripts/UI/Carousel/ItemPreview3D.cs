using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Singleton que muestra el modelo 3D del item central del carrusel.
/// Usa una cámara dedicada que renderiza a una RenderTexture mostrada en una RawImage.
/// Cuando el item está seleccionado: rotación + hover (bob). Cuando se deselecciona: freeze.
/// 
/// Setup: ItemPreview3D necesita
///   - Una cámara configurada con Target Texture (RenderTexture)
///   - Un Transform "modelAnchor" donde se instancia el modelo
///   - Una RawImage en la UI con la misma RenderTexture asignada
/// </summary>
public class ItemPreview3D : MonoBehaviour
{
    public static ItemPreview3D Instance { get; private set; }

    [Header("Setup")]
    [SerializeField] private Camera previewCamera;
    [SerializeField] private Transform modelAnchor;
    [SerializeField] private RawImage targetRawImage; // se enseña/oculta automáticamente

    [Header("Render Texture")]
    [SerializeField] private int rtWidth = 256;
    [SerializeField] private int rtHeight = 256;
    [Tooltip("Profundidad del depth buffer (24 = recomendado para evitar warning de SRP).")]
    [SerializeField] private int rtDepthBits = 24;

    [Header("Animación seleccionado")]
    [SerializeField] private float rotationSpeed = 45f;
    [SerializeField] private float hoverAmount = 0.05f;
    [SerializeField] private float hoverSpeed = 1f;

    private GameObject _currentModel;
    private ItemData _currentData;
    private bool _isSelected;
    private Tween _hoverTween;
    private Vector3 _baseLocalPos;
    private RenderTexture _rt;

    /// <summary>RenderTexture activa de la cámara. La usa CarouselSlot para mostrarla en su modelPreview.</summary>
    public RenderTexture RT => _rt;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // El RectTransform de este nodo en la escena venía con Scale 0/0/0 (probable
        // herencia de un tween de cierre que se quedó pegado), lo que hacía que la
        // RawImage no se renderizase nunca aunque la RT se generase OK. Forzamos
        // escala 1 al arrancar para garantizar que el preview siempre sea visible.
        var rt = transform as RectTransform;
        if (rt != null && rt.localScale.sqrMagnitude < 0.0001f)
            rt.localScale = Vector3.one;

        AutoWireRefs();
        SetupRenderTexture();
        if (targetRawImage != null) targetRawImage.enabled = false;

        if (previewCamera == null)
            Debug.LogError($"[ItemPreview3D:{name}] previewCamera no asignada — el modelo 3D no se renderizará.", this);
        if (modelAnchor == null)
            Debug.LogError($"[ItemPreview3D:{name}] modelAnchor no asignado — no hay dónde instanciar el modelo.", this);
        if (targetRawImage == null)
            Debug.LogError($"[ItemPreview3D:{name}] targetRawImage no asignada — no hay dónde mostrar la RenderTexture.", this);
    }

    private void AutoWireRefs()
    {
        // Auto-wire por hijos para que un setup desde el editor con sólo el
        // gameobject del preview funcione sin tener que arrastrar refs a mano.
        if (previewCamera == null) previewCamera = GetComponentInChildren<Camera>(true);
        if (targetRawImage == null) targetRawImage = GetComponentInChildren<RawImage>(true);
        if (modelAnchor == null)
        {
            // Busca un hijo cuyo nombre contenga "anchor" o "model".
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t == transform) continue;
                if (previewCamera != null && t == previewCamera.transform) continue;
                string n = t.name.ToLowerInvariant();
                if (n.Contains("anchor") || n.Contains("model")) { modelAnchor = t; break; }
            }
        }
        // Última red de seguridad: si no hay anchor, créalo como hijo de la cámara
        // a una distancia razonable hacia adelante para que el modelo entre en cuadro.
        EnsureModelAnchor();
    }

    private void EnsureModelAnchor()
    {
        if (modelAnchor != null) return;
        if (previewCamera == null) return;

        var go = new GameObject("AutoModelAnchor");
        go.transform.SetParent(previewCamera.transform, worldPositionStays: false);
        go.transform.localPosition = new Vector3(0f, 0f, 1.5f);
        go.transform.localRotation = Quaternion.identity;
        // Layer dentro de la culling mask de la cámara: si la cámara está en otro layer
        // (p.ej. UI) pero su culling mask sólo incluye "ItemUI", queremos el del mask.
        go.layer = GetCullingMaskLayer();
        modelAnchor = go.transform;
    }

    // Devuelve un layer que la previewCamera realmente vea según su culling mask.
    // Si la cámara está en layer UI pero su culling mask es ItemUI, propagamos ItemUI
    // al modelo — propagar el layer del GameObject de la cámara haría que la propia
    // cámara descartase el modelo por culling.
    private int GetCullingMaskLayer()
    {
        if (previewCamera == null) return 0;
        int mask = previewCamera.cullingMask;
        if (mask == 0) return previewCamera.gameObject.layer;
        for (int i = 0; i < 32; i++)
        {
            if ((mask & (1 << i)) != 0) return i;
        }
        return previewCamera.gameObject.layer;
    }

    // Crea una RenderTexture en runtime con depthStencilFormat válido y la asigna a la
    // cámara y a la RawImage. Render Graph API (Unity 6 / URP) exige depthStencilFormat
    // distinto de None: el constructor antiguo con int "depth" ya no es suficiente, así
    // que usamos RenderTextureDescriptor con D24_UNorm_S8_UInt explícito.
    // Además, antes de hacer el setup desactivamos brevemente la cámara para que no intente
    // renderizar la RT serializada (rota) que pueda traer asignada desde la escena.
    private void SetupRenderTexture()
    {
        if (previewCamera == null) return;

        bool wasEnabled = previewCamera.enabled;
        previewCamera.enabled = false;
        previewCamera.targetTexture = null;

        var desc = new RenderTextureDescriptor(rtWidth, rtHeight, RenderTextureFormat.ARGB32, 0)
        {
            depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.D24_UNorm_S8_UInt,
            msaaSamples = 1,
            useMipMap = false
        };

        _rt = new RenderTexture(desc)
        {
            name = "ItemPreview3D_RT_runtime",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        _rt.Create();

        previewCamera.targetTexture = _rt;
        previewCamera.enabled = wasEnabled;
        if (targetRawImage != null) targetRawImage.texture = _rt;
    }

    private void Update()
    {
        if (_currentModel == null || !_isSelected) return;
        _currentModel.transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.Self);
    }

    /// <summary>
    /// Cambia el modelo mostrado. Si data es null o no tiene modelo 3D, se oculta.
    /// </summary>
    public void Show(ItemData data)
    {
        // Importante: si el data coincide pero no llegó a instanciarse el modelo
        // (porque al primer Show el anchor o la RawImage no estaban listos),
        // dejamos que vuelva a entrar para reintentar.
        if (data == _currentData && _currentModel != null) return;

        _currentData = data;
        ClearCurrent();

        if (data == null || data.previewMode != ItemData.PreviewMode.Model3D || data.model3D == null)
        {
            if (targetRawImage != null) targetRawImage.enabled = false;
            return;
        }

        // Si todavía no hay anchor, intenta crearlo (puede pasar si previewCamera
        // se asigna programáticamente después del Awake).
        EnsureModelAnchor();
        if (modelAnchor == null)
        {
            Debug.LogWarning("[ItemPreview3D] No hay modelAnchor disponible: el modelo 3D no se instanciará.", this);
            if (targetRawImage != null) targetRawImage.enabled = false;
            return;
        }

        _currentModel = Instantiate(data.model3D, modelAnchor);
        _currentModel.transform.localPosition = Vector3.zero;
        _currentModel.transform.localRotation = Quaternion.identity;
        _baseLocalPos = _currentModel.transform.localPosition;

        // Para que el camera culling mask lo recoja, propaga un layer que la cámara
        // SÍ renderice (basado en culling mask, no en el layer del GameObject de la
        // cámara): los prefabs vienen del mundo en layer "Interactable" y la cámara
        // de preview suele tener otra culling mask dedicada.
        if (previewCamera != null)
            SetLayerRecursively(_currentModel, GetCullingMaskLayer());

        if (targetRawImage != null) targetRawImage.enabled = true;

        SetSelected(_isSelected);
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null) return;
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    /// <summary>
    /// Activa/desactiva el estado "seleccionado". Cuando true: hover anim + rotación.
    /// Cuando false: el modelo se queda quieto en su rotación actual.
    /// </summary>
    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        _hoverTween?.Kill();

        if (_currentModel == null) return;

        if (selected)
        {
            // Hover suave en Y
            float duration = 1f / Mathf.Max(0.1f, hoverSpeed);
            _hoverTween = _currentModel.transform.DOLocalMoveY(_baseLocalPos.y + hoverAmount, duration * 0.5f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(_currentModel);
        }
        else
        {
            // Vuelve a posición base sin hover. Rotación se queda como esté.
            _currentModel.transform.DOLocalMove(_baseLocalPos, 0.2f).SetEase(Ease.OutQuad);
        }
    }

    public void Hide()
    {
        ClearCurrent();
        _currentData = null;
        if (targetRawImage != null) targetRawImage.enabled = false;
    }

    public void SetTargetRawImage(RawImage rawImage)
    {
        targetRawImage = rawImage;
        if (targetRawImage != null && _rt != null)
            targetRawImage.texture = _rt;

        // Si la primera vez que llamaron Show la RawImage o el anchor no estaban,
        // _currentModel se quedó null y los siguientes Show() con el mismo data
        // se descartaban. Forzamos un retry borrando la cache.
        if (_currentModel == null && _currentData != null)
        {
            var data = _currentData;
            _currentData = null;
            Show(data);
        }
    }

    private void ClearCurrent()
    {
        _hoverTween?.Kill();
        if (_currentModel != null)
        {
            Destroy(_currentModel);
            _currentModel = null;
        }
    }

    private void OnDestroy()
    {
        _hoverTween?.Kill();
        if (_rt != null)
        {
            if (previewCamera != null && previewCamera.targetTexture == _rt) previewCamera.targetTexture = null;
            _rt.Release();
            Destroy(_rt);
        }
    }
}