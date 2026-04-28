using UnityEngine;
using TMPro;
using DG.Tweening;

/// <summary>
/// Cuenta atrás con thresholds visuales configurables.
/// El TMP_Text mostrará MM:SS y cambiará de color + parpadeará según el tiempo restante.
/// </summary>
public class CountdownTimer : MonoBehaviour
{
    [Header("Tiempo")]
    [SerializeField] private float startTime = 600f; // 10 min
    [SerializeField] private bool startOnAwake = true;
    [SerializeField] private bool useUnscaledTime = false; // útil si pausas con Time.timeScale

    [Header("Display")]
    [SerializeField] private TMP_Text label;
    [SerializeField] private Color normalColor = Color.white;

    [Header("Threshold 1 (Warning)")]
    [SerializeField] private float warningThreshold = 120f;
    [SerializeField] private Color warningColor = new Color(1f, 0.85f, 0.2f);
    [SerializeField] private float warningBlinkSpeed = 1.2f;
    [SerializeField] private float warningBlinkIntensity = 0.4f;

    [Header("Threshold 2 (Critical)")]
    [SerializeField] private float criticalThreshold = 30f;
    [SerializeField] private Color criticalColor = new Color(1f, 0.2f, 0.2f);
    [SerializeField] private float criticalBlinkSpeed = 4f;
    [SerializeField] private float criticalBlinkIntensity = 0.85f;

    [Header("Audio")]
    [SerializeField] private string tickSoundId = "timer_tick";
    [SerializeField] private string lowSoundId = "timer_low";
    [SerializeField] private string criticalSoundId = "timer_critical";

    [Header("Eventos")]
    public UnityEngine.Events.UnityEvent onTimerEnded;

    private float _remaining;
    private bool _running;
    private Tween _blinkTween;
    private int _lastWholeSecond = -1;
    private int _activeThreshold = 0; // 0=normal, 1=warning, 2=critical

    public float Remaining => _remaining;
    public bool IsRunning => _running;

    private void Awake()
    {
        _remaining = startTime;
        UpdateLabel();
        ApplyVisualState(0, true);
    }

    private void Start()
    {
        if (startOnAwake) StartTimer();
    }

    private void Update()
    {
        if (!_running) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        _remaining = Mathf.Max(0f, _remaining - dt);

        UpdateLabel();
        UpdateThreshold();

        // Tick por segundo cuando estamos en warning/critical
        int whole = Mathf.CeilToInt(_remaining);
        if (whole != _lastWholeSecond)
        {
            _lastWholeSecond = whole;
            if (_activeThreshold == 2)
                AudioManager.Instance?.PlayUI(criticalSoundId);
            else if (_activeThreshold == 1 && whole % 5 == 0)
                AudioManager.Instance?.PlayUI(lowSoundId);
        }

        if (_remaining <= 0f)
        {
            _running = false;
            onTimerEnded?.Invoke();
        }
    }

    public void StartTimer() { _running = true; }
    public void PauseTimer() { _running = false; }
    public void ResetTimer(float? newStart = null)
    {
        _remaining = newStart ?? startTime;
        _activeThreshold = 0;
        ApplyVisualState(0, true);
        UpdateLabel();
    }
    public void AddTime(float seconds) { _remaining = Mathf.Max(0, _remaining + seconds); }

    private void UpdateLabel()
    {
        if (label == null) return;
        int totalSec = Mathf.CeilToInt(_remaining);
        int m = totalSec / 60;
        int s = totalSec % 60;
        label.text = $"{m:00}:{s:00}";
    }

    private void UpdateThreshold()
    {
        int target = 0;
        if (_remaining <= criticalThreshold) target = 2;
        else if (_remaining <= warningThreshold) target = 1;

        if (target != _activeThreshold) ApplyVisualState(target, false);
    }

    private void ApplyVisualState(int threshold, bool instant)
    {
        _activeThreshold = threshold;
        _blinkTween?.Kill();
        if (label == null) return;

        switch (threshold)
        {
            case 0:
                label.color = normalColor;
                break;
            case 1:
                StartBlink(warningColor, warningBlinkSpeed, warningBlinkIntensity);
                break;
            case 2:
                StartBlink(criticalColor, criticalBlinkSpeed, criticalBlinkIntensity);
                break;
        }
    }

    private void StartBlink(Color baseColor, float speed, float intensity)
    {
        float duration = 1f / Mathf.Max(0.1f, speed);
        Color dim = Color.Lerp(baseColor, Color.black, intensity);
        label.color = baseColor;
        _blinkTween = label.DOColor(dim, duration * 0.5f)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetUpdate(useUnscaledTime)
            .SetLink(label.gameObject);
    }

    private void OnDestroy() { _blinkTween?.Kill(); }
}