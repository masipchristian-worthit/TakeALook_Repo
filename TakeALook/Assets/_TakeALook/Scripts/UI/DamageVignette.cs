using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

[RequireComponent(typeof(Image))]
public class DamageVignette : MonoBehaviour
{
    [System.Serializable]
    public class Threshold
    {
        public float maxHP;
        public Color color = Color.yellow;
        public float baseAlpha = 0.4f;
        public float pulseAmount = 0.2f;
        public float pulseSpeed = 1f;
    }

    [Header("Referencias")]
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private Image vignetteImage;

    [Header("Thresholds")]
    [SerializeField] private Threshold yellowState = new Threshold { maxHP = 60f, color = new Color(1f, 0.85f, 0.2f), baseAlpha = 0.25f, pulseAmount = 0.15f, pulseSpeed = 0.6f };
    [SerializeField] private Threshold orangeState = new Threshold { maxHP = 45f, color = new Color(1f, 0.5f, 0.1f), baseAlpha = 0.4f, pulseAmount = 0.25f, pulseSpeed = 1.2f };
    [SerializeField] private Threshold redState   = new Threshold { maxHP = 30f, color = new Color(1f, 0.1f, 0.1f), baseAlpha = 0.6f, pulseAmount = 0.35f, pulseSpeed = 2.2f };

    [Header("Hit Flash")]
    [SerializeField] private float hitFlashAlpha = 0.85f;
    [SerializeField] private float hitFlashTime  = 0.18f;

    private Threshold _activeState;
    private Tween _pulseTween;
    private Tween _flashTween;

    private void Awake()
    {
        if (vignetteImage == null) vignetteImage = GetComponent<Image>();
        if (vignetteImage != null)
        {
            var c = vignetteImage.color; c.a = 0f; vignetteImage.color = c;
            vignetteImage.enabled = false;
        }

        if (playerHealth == null)
        {
            var playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null) playerHealth = playerObj.GetComponentInChildren<PlayerHealth>();
        }

        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged += HandleHealthChanged;
            playerHealth.OnDamaged       += HandleDamaged;
        }
    }

    private void Update()
    {
        if (playerHealth == null) return;
        Threshold next = GetThreshold(playerHealth.CurrentHP);
        if (next == _activeState) return;
        _activeState = next;
        ApplyState(next);
    }

    private void Start()
    {
        // Forzar evaluación inicial una vez que todos los Awake ya corrieron
        if (playerHealth != null)
            HandleHealthChanged(playerHealth.CurrentHP, playerHealth.MaxHP);
    }

    private void OnEnable()
    {
        // Al reactivar el Canvas, forzamos re-aplicar el estado actual
        if (playerHealth == null) return;
        _activeState = null;
        HandleHealthChanged(playerHealth.CurrentHP, playerHealth.MaxHP);
        // Si vida llena y la imagen quedó visible de antes, ocultarla
        if (_activeState == null && vignetteImage != null)
        {
            _pulseTween?.Kill(); _flashTween?.Kill();
            var c = vignetteImage.color; c.a = 0f; vignetteImage.color = c;
            vignetteImage.enabled = false;
        }
    }

    private void OnDisable()
    {
        _pulseTween?.Kill();
        _flashTween?.Kill();
    }

    private void OnDestroy()
    {
        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged -= HandleHealthChanged;
            playerHealth.OnDamaged       -= HandleDamaged;
        }
        _pulseTween?.Kill();
        _flashTween?.Kill();
    }

    private Threshold GetThreshold(float hp)
    {
        if (hp < redState.maxHP)    return redState;
        if (hp < orangeState.maxHP) return orangeState;
        if (hp < yellowState.maxHP) return yellowState;
        return null;
    }

    private void HandleHealthChanged(float current, float max)
    {
        Threshold target = GetThreshold(current);
        if (target == _activeState) return;
        _activeState = target;
        ApplyState(target);
    }

    private void HandleDamaged(float amount)
    {
        if (vignetteImage == null) return;

        // Interrumpir cualquier tween activo y mostrar flash
        _pulseTween?.Kill();
        _flashTween?.Kill();

        vignetteImage.enabled = true;
        Color hitColor = (_activeState != null) ? _activeState.color : new Color(1f, 0.2f, 0.2f);
        vignetteImage.color = new Color(hitColor.r, hitColor.g, hitColor.b, hitFlashAlpha);

        float backAlpha = (_activeState != null) ? _activeState.baseAlpha : 0f;
        _flashTween = vignetteImage.DOFade(backAlpha, hitFlashTime).SetEase(Ease.OutQuad)
            .OnComplete(() =>
            {
                if (_activeState != null) ApplyState(_activeState);
                else vignetteImage.enabled = false;
            });
    }

    private void ApplyState(Threshold state)
    {
        // Matar ambos tweens para evitar conflictos de DOTween sobre el mismo canal
        _pulseTween?.Kill();
        _flashTween?.Kill();
        if (vignetteImage == null) return;

        if (state == null)
        {
            _pulseTween = vignetteImage.DOFade(0f, 0.6f).SetEase(Ease.OutQuad)
                .OnComplete(() => vignetteImage.enabled = false);
            return;
        }

        vignetteImage.enabled = true;
        Color c = state.color;

        float minA        = Mathf.Max(0f, state.baseAlpha - state.pulseAmount * 0.5f);
        float maxA        = Mathf.Min(1f, state.baseAlpha + state.pulseAmount * 0.5f);
        float halfDuration = 1f / Mathf.Max(0.1f, state.pulseSpeed) * 0.5f;

        vignetteImage.color = new Color(c.r, c.g, c.b, minA);
        _pulseTween = vignetteImage.DOFade(maxA, halfDuration)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetLink(gameObject);
    }
}
