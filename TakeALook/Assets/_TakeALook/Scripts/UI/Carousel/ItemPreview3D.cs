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

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SetupRenderTexture();
        if (targetRawImage != null) targetRawImage.enabled = false;
    }

    // Crea una RenderTexture en runtime con depth buffer válido y la asigna a la
    // cámara y a la RawImage. Esto evita el warning del Render Graph API que aparece
    // cuando una RT serializada en disco no tiene depth (URP la rechaza).
    private void SetupRenderTexture()
    {
        if (previewCamera == null) return;

        _rt = new RenderTexture(rtWidth, rtHeight, Mathf.Max(16, rtDepthBits), RenderTextureFormat.ARGB32)
        {
            name = "ItemPreview3D_RT_runtime",
            antiAliasing = 1,
            useMipMap = false,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        _rt.Create();

        previewCamera.targetTexture = _rt;
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
        if (data == _currentData) return;

        _currentData = data;
        ClearCurrent();

        if (data == null || data.previewMode != ItemData.PreviewMode.Model3D || data.model3D == null || modelAnchor == null)
        {
            if (targetRawImage != null) targetRawImage.enabled = false;
            return;
        }

        _currentModel = Instantiate(data.model3D, modelAnchor);
        _currentModel.transform.localPosition = Vector3.zero;
        _currentModel.transform.localRotation = Quaternion.identity;
        _baseLocalPos = _currentModel.transform.localPosition;

        if (targetRawImage != null) targetRawImage.enabled = true;

        SetSelected(_isSelected);
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