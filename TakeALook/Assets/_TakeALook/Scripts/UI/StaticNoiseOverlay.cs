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
    [Tooltip("Color 1 para interpolación de ruido.")]
    [SerializeField] private Color noiseColor1 = Color.black;
    [Tooltip("Color 2 para interpolación de ruido.")]
    [SerializeField] private Color noiseColor2 = Color.white;
    [Tooltip("Qué tan denso/oscuro queda el ruido.")]
    [Range(0f, 1f)]
    [SerializeField] private float noiseContrast = 1f;

    [Header("Edge Fade")]
    [Tooltip("Intensidad del fade radial hacia los bordes (0 = sin fade, 1 = fade máximo).")]
    [Range(0f, 1f)]
    [SerializeField] private float edgeFadeStrength = 0.5f;
    [Tooltip("Distancia desde el centro donde comienza el fade (0-0.5 normalizado).")]
    [Range(0f, 0.5f)]
    [SerializeField] private float edgeFadeDistance = 0.45f;

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
        // RGBA32 (no R8): R8 sólo guarda el canal rojo, por eso el ruido salía
        // siempre en rojo/negro y el fade alpha era ignorado.
        _noiseTex = new Texture2D(res, res, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            name = "StaticNoise(Procedural)"
        };
        _pixelBuffer = new Color32[res * res];
        RegenerateNoise();
        _rawImage.texture = _noiseTex;
        // Aseguramos que el tint de la RawImage no anule los colores de la textura.
        var c = _rawImage.color; c.r = 1f; c.g = 1f; c.b = 1f; _rawImage.color = c;
    }

    private void RegenerateNoise()
    {
        int res = _noiseTex.width;
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                int i = y * res + x;

                // Generar ruido random y aplicar contraste
                int randomValue = _rng.Next(0, 256);
                float t = randomValue / 255f;
                t = Mathf.Lerp(0.5f, t, noiseContrast);

                // Interpolar entre los dos colores configurados
                Color noiseColor = Color.Lerp(noiseColor1, noiseColor2, t);

                // Calcular fade radial desde el centro
                float u = x / (float)res;
                float v = y / (float)res;
                float distFromCenter = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f));

                // Fade alpha: máximo en el centro, desvanece hacia los bordes
                float alpha = 1f;
                if (distFromCenter > edgeFadeDistance)
                {
                    alpha = 0f;
                }
                else if (distFromCenter > edgeFadeDistance * (1f - edgeFadeStrength))
                {
                    // Zona de transición
                    float fadeStart = edgeFadeDistance * (1f - edgeFadeStrength);
                    float fadeRange = edgeFadeDistance - fadeStart;
                    float fadeProgress = (distFromCenter - fadeStart) / fadeRange;
                    alpha = Mathf.Lerp(1f, 0f, fadeProgress);
                }

                noiseColor.a = alpha;
                _pixelBuffer[i] = new Color32(
                    (byte)(noiseColor.r * 255f),
                    (byte)(noiseColor.g * 255f),
                    (byte)(noiseColor.b * 255f),
                    (byte)(alpha * 255f)
                );
            }
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
