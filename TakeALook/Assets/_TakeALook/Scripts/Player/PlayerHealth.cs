using UnityEngine;
using DG.Tweening;

/// <summary>
/// Sistema de salud del jugador. Dispara eventos para que el vignette y el HUD reaccionen.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [Header("HP")]
    [SerializeField] private float maxHP = 100f;
    [SerializeField] private float currentHP = 100f;

    [Header("Damage Feedback")]
    [SerializeField] private float invulnerabilityTime = 0.4f;

    [Header("Audio")]
    [SerializeField] private string hurtSoundId = "player_hurt";
    [SerializeField] private string healSoundId = "player_heal";
    [SerializeField] private string deathSoundId = "player_death";

    public float MaxHP => maxHP;
    public float CurrentHP => currentHP;
    public float HPPercent => Mathf.Clamp01(currentHP / maxHP);
    public bool IsAlive => currentHP > 0f;

    public event System.Action<float, float> OnHealthChanged; // (current, max)
    public event System.Action<float> OnDamaged;              // (amount)
    public event System.Action OnDied;

    private float _lastDamageTime = -999f;

    private void Start()
    {
        OnHealthChanged?.Invoke(currentHP, maxHP);
    }

    public void TakeDamage(float amount)
    {
        if (!IsAlive || amount <= 0f) return;
        if (Time.time - _lastDamageTime < invulnerabilityTime) return;

        _lastDamageTime = Time.time;
        currentHP = Mathf.Max(0f, currentHP - amount);

        OnDamaged?.Invoke(amount);
        OnHealthChanged?.Invoke(currentHP, maxHP);
        AudioManager.Instance?.PlaySFX(hurtSoundId, transform.position);

        if (currentHP <= 0f)
        {
            AudioManager.Instance?.PlaySFX(deathSoundId, transform.position);
            OnDied?.Invoke();
        }
    }

    public void Heal(float amount)
    {
        if (!IsAlive || amount <= 0f) return;
        currentHP = Mathf.Min(maxHP, currentHP + amount);
        OnHealthChanged?.Invoke(currentHP, maxHP);
        AudioManager.Instance?.PlaySFX(healSoundId, transform.position);
    }

    public void SetMaxHP(float value, bool refill = false)
    {
        maxHP = Mathf.Max(1f, value);
        if (refill) currentHP = maxHP;
        else currentHP = Mathf.Min(currentHP, maxHP);
        OnHealthChanged?.Invoke(currentHP, maxHP);
    }
}
