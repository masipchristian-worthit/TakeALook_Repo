using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Vignette de daño que respira y cambia de color según el HP del jugador.
/// >= 60 HP : Sin vignette
/// < 60 HP  : Amarillo respirante (suave)
/// < 45 HP  : Naranja respirante (medio)
/// < 30 HP  : Rojo respirante (intenso)
/// 
/// Espera una Image full-screen (UI canvas) con un sprite de vignette (transparente en el centro).
/// </summary>
[RequireComponent(typeof(Image))]
public class DamageVignette : MonoBehaviour
{
    [System.Serializable]
    public class Threshold
    {
        public float maxHP;          // se activa cuando HP < maxHP
        public Color color = Color.yellow;
        public float baseAlpha = 0.4f;
        public float pulseAmount = 0.2f; // amplitud de la respiración (alpha)
        public float pulseSpeed = 1f;    // pulsos por segundo
    }

    [Header("Referencias")]
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private Image vignetteImage;

    [Header("Thresholds (de mayor HP a menor)")]
    [SerializeField]
    private Threshold yellowState = new Threshold
    {
        maxHP = 60f,
        color = new Color(1f, 0.85f, 0.2f),
        baseAlpha = 0.25f,
        pulseAmount = 0.15f,
        pulseSpeed = 0.6f
    };
    [SerializeField]
    private Threshold orangeState = new Threshold
    {
        maxHP = 45f,
        color = new Color(1f, 0.5f, 0.1f),
        baseAlpha = 0.4f,
        pulseAmount = 0.25f,
        pulseSpeed = 1.2f
    };
    [SerializeField]
    private Threshold redState = new Threshold
    {
        maxHP = 30f,
        color = new Color(1f, 0.1f, 0.1f),
        baseAlpha = 0.6f,
        pulseAmount = 0.35f,
        pulseSpeed = 2.2f
    };

    [Header("Hit Flash")]
    [SerializeField] private float hitFlashAlpha = 0.85f;
    [SerializeField] private float hitFlashTime = 0.18f;

    private Threshold _activeState;
    private Tween _pulseTween;
    private Tween _flashTween;

    private void Awake()
    {
        if (vignetteImage == null) vignetteImage = GetComponent<Image>();
        if (vignetteImage != null)
        {
            var c = vignetteImage.color; c.a = 0f; vignetteImage.color = c;
        }
    }

    private void OnEnable()
    {
        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged += HandleHealthChanged;
            playerHealth.OnDamaged += HandleDamaged;
            HandleHealthChanged(playerHealth.CurrentHP, playerHealth.MaxHP);
        }
    }

    private void OnDisable()
    {
        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged -= HandleHealthChanged;
            playerHealth.OnDamaged -= HandleDamaged;
        }
        _pulseTween?.Kill();
        _flashTween?.Kill();
    }

    private void HandleHealthChanged(float current, float max)
    {
        Threshold target = null;
        if (current < redState.maxHP) target = redState;
        else if (current < orangeState.maxHP) target = orangeState;
        else if (current < yellowState.maxHP) target = yellowState;

        if (target == _activeState) return;
        _activeState = target;
        ApplyState(target);
    }

    private void HandleDamaged(float amount)
    {
        if (vignetteImage == null) return;
        _flashTween?.Kill();

        Color hitColor = (_activeState != null) ? _activeState.color : new Color(1f, 0.2f, 0.2f);
        vignetteImage.color = new Color(hitColor.r, hitColor.g, hitColor.b, hitFlashAlpha);

        // Vuelve al alpha base del estado actual
        float backAlpha = (_activeState != null) ? _activeState.baseAlpha : 0f;
        _flashTween = vignetteImage.DOFade(backAlpha, hitFlashTime).SetEase(Ease.OutQuad);
    }

    private void ApplyState(Threshold state)
    {
        _pulseTween?.Kill();

        if (vignetteImage == null) return;

        if (state == null)
        {
            vignetteImage.DOFade(0f, 0.6f).SetEase(Ease.OutQuad);
            return;
        }

        Color c = state.color;
        c.a = state.baseAlpha;
        vignetteImage.color = c;

        // Respiración
        float minA = Mathf.Max(0f, state.baseAlpha - state.pulseAmount * 0.5f);
        float maxA = Mathf.Min(1f, state.baseAlpha + state.pulseAmount * 0.5f);
        float halfDuration = 1f / Mathf.Max(0.1f, state.pulseSpeed) * 0.5f;

        vignetteImage.color = new Color(c.r, c.g, c.b, minA);
        _pulseTween = vignetteImage.DOFade(maxA, halfDuration)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetLink(gameObject);
    }
}