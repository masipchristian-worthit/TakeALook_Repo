using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Overlay de estática pixelada estilo CRT/VHS. Se dispara con Flash() para hacer
/// un "refresco" visual breve cuando cambia el contenido del carrusel o se consume
/// un objeto. La textura es procedural (Texture2D + FilterMode.Point) — no necesita assets.
///
/// Uso típico:
///   - Asignar este componente a un RawImage que cubra el panel del carrusel.
///   - Llamar Flash() desde CarouselUI al cambiar/consumir item.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class StaticNoiseOverlay : MonoBehaviour
{
    [Header("Textura procedural")]
    [Tooltip("Resolución del ruido (cuanto más bajo, más 'gordo' el píxel).")]
    [SerializeField] private int noiseResolution = 64;
    [Tooltip("Qué tan denso/oscuro queda el ruido.")]
    [Range(0f, 1f)]
    [SerializeField] private float noiseContrast = 1f;

    [Header("Animación")]
    [Tooltip("Duración total del flash (in + hold + out).")]
    [SerializeField] private float flashDuration = 0.18f;
    [Tooltip("Fracción del flash que se queda al alpha máximo (resto se reparte fade-in/out).")]
    [Range(0f, 1f)]
    [SerializeField] private float holdRatio = 0.35f;
    [Tooltip("Alpha máximo del overlay durante el hold.")]
    [Range(0f, 1f)]
    [SerializeField] private float maxAlpha = 0.85f;
    [Tooltip("Cada cuántos segundos durante el hold se regenera la textura (chasquido).")]
    [SerializeField] private float regenInterval = 0.04f;

    private RawImage _rawImage;
    private Texture2D _noiseTex;
    private Color32[] _pixelBuffer;
    private System.Random _rng;
    private float _flashTimer;
    private float _regenTimer;
    private bool _isFlashing;

    private void Awake()
    {
        _rawImage = GetComponent<RawImage>();
        _rawImage.raycastTarget = false;

        _rng = new System.Random();
        BuildNoiseTexture();
        SetAlpha(0f);
    }

    private void BuildNoiseTexture()
    {
        int res = Mathf.Max(8, noiseResolution);
        _noiseTex = new Texture2D(res, res, TextureFormat.R8, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            name = "StaticNoise(Procedural)"
        };
        _pixelBuffer = new Color32[res * res];
        RegenerateNoise();
        _rawImage.texture = _noiseTex;
    }

    private void RegenerateNoise()
    {
        for (int i = 0; i < _pixelBuffer.Length; i++)
        {
            int v = _rng.Next(0, 256);
            // Aplicar contraste: empuja hacia los extremos.
            float t = v / 255f;
            t = Mathf.Lerp(0.5f, t, noiseContrast);
            byte b = (byte)(t * 255f);
            _pixelBuffer[i] = new Color32(b, b, b, 255);
        }
        _noiseTex.SetPixels32(_pixelBuffer);
        _noiseTex.Apply(false);
    }

    /// <summary>Dispara un flash de estática. Se puede llamar repetidamente — reinicia el timer.</summary>
    public void Flash()
    {
        _flashTimer = 0f;
        _regenTimer = 0f;
        _isFlashing = true;
        RegenerateNoise();
        OffsetUV();
    }

    private void Update()
    {
        if (!_isFlashing) return;

        _flashTimer += Time.unscaledDeltaTime;
        _regenTimer += Time.unscaledDeltaTime;

        if (_regenTimer >= regenInterval)
        {
            _regenTimer = 0f;
            RegenerateNoise();
            OffsetUV();
        }

        float t = Mathf.Clamp01(_flashTimer / Mathf.Max(0.0001f, flashDuration));
        float hold = Mathf.Clamp01(holdRatio);
        float halfFade = (1f - hold) * 0.5f;
        float a;
        if (t < halfFade) a = Mathf.Lerp(0f, maxAlpha, t / halfFade);                       // fade-in
        else if (t < halfFade + hold) a = maxAlpha;                                          // hold
        else a = Mathf.Lerp(maxAlpha, 0f, (t - halfFade - hold) / Mathf.Max(0.0001f, halfFade)); // fade-out
        SetAlpha(a);

        if (t >= 1f)
        {
            _isFlashing = false;
            SetAlpha(0f);
        }
    }

    private void OffsetUV()
    {
        // Desplazar el UV produce sensación de "chasquido" entre frames.
        var r = _rawImage.uvRect;
        r.x = (float)_rng.NextDouble();
        r.y = (float)_rng.NextDouble();
        _rawImage.uvRect = r;
    }

    private void SetAlpha(float a)
    {
        var c = _rawImage.color;
        c.a = a;
        _rawImage.color = c;
    }

    private void OnDestroy()
    {
        if (_noiseTex != null) Destroy(_noiseTex);
    }
}
