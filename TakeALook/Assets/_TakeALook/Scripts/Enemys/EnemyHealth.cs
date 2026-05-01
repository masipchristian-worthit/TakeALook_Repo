using System.Collections;
using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] int maxHealth = 100;

    [Header("Hit Feedback")]
    [SerializeField] Material damagedMat;

    [Header("Refs")]
    [Tooltip("Renderers de la malla del enemigo. Se auto-detectan si se dejan vacíos.")]
    [SerializeField] Renderer[] meshRenderers;

    [Header("Audio")]
    [SerializeField] string sfxHeadshotId  = "enemy_headshot";
    [SerializeField] string sfxBodyshotId  = "enemy_bodyshot";
    [SerializeField] string sfxDeathId     = "enemy_death";
    [SerializeField] string sfxDeathBullId = "enemy_death_electric";

    int _currentHealth;
    bool _isDead;
    bool _isCorpse;
    Material _baseMat;
    Renderer _firstRenderer;
    EnemyAIBase _ai;
    Animator _animator;

    public float HealthPercent => _isDead ? 0f : (float)_currentHealth / Mathf.Max(1, maxHealth);
    public bool IsDead => _isDead;

    void Awake()
    {
        _currentHealth = maxHealth;
        _ai = GetComponent<EnemyAIBase>();
        _animator = GetComponentInChildren<Animator>();

        if (meshRenderers == null || meshRenderers.Length == 0)
            meshRenderers = GetComponentsInChildren<Renderer>();

        if (meshRenderers != null && meshRenderers.Length > 0)
        {
            _firstRenderer = meshRenderers[0];
            _baseMat = _firstRenderer.sharedMaterial;
        }
    }

    /// <summary>
    /// Aplica daño normal de bala (cuerpo). isBullBullet = true mata de un golpe con animación Electrocuted.
    /// </summary>
    public void TakeDamage(int damage, bool isBullBullet = false)
    {
        if (_isDead) return;

        if (isBullBullet)
        {
            AudioManager.Instance?.PlaySFX(sfxBodyshotId, transform.position);
            Kill(isBull: true);
            return;
        }

        if (_ai != null && _ai.IsInvulnerable) return;

        // Feedback visual de impacto
        if (damagedMat != null && _firstRenderer != null)
        {
            _firstRenderer.material = damagedMat;
            CancelInvoke(nameof(ResetMat));
            Invoke(nameof(ResetMat), 0.1f);
        }

        AudioManager.Instance?.PlaySFX(sfxBodyshotId, transform.position);

        _currentHealth = Mathf.Max(0, _currentHealth - damage);

        if (_currentHealth <= 0)
            Kill(isBull: false);
        else
        {
            _ai?.OnHitByNormalBullet();
            if (_animator != null) _animator.SetTrigger("Shot");
        }
    }

    /// <summary>
    /// Llamado por HeadshotCollider. Reproduce sonido de headshot y aplica daño multiplicado.
    /// </summary>
    public void TakeHeadshotDamage(int damage, bool isBullBullet)
    {
        if (_isDead) return;
        AudioManager.Instance?.PlaySFX(sfxHeadshotId, transform.position);

        if (isBullBullet)
        {
            Kill(isBull: true);
            return;
        }

        if (_ai != null && _ai.IsInvulnerable) return;

        if (damagedMat != null && _firstRenderer != null)
        {
            _firstRenderer.material = damagedMat;
            CancelInvoke(nameof(ResetMat));
            Invoke(nameof(ResetMat), 0.1f);
        }

        _currentHealth = Mathf.Max(0, _currentHealth - damage);
        if (_currentHealth <= 0)
            Kill(isBull: false);
        else
        {
            _ai?.OnHitByNormalBullet();
            if (_animator != null) _animator.SetTrigger("Shot");
        }
    }

    void Kill(bool isBull)
    {
        if (_isDead) return;
        _isDead = true;
        _currentHealth = 0;
        CancelInvoke(nameof(ResetMat));

        AudioManager.Instance?.PlaySFX(isBull ? sfxDeathBullId : sfxDeathId, transform.position);

        if (isBull) _ai?.OnKilledByBullBullet();
        else        _ai?.OnKilledByNormalBullet();
    }

    void ResetMat()
    {
        if (_firstRenderer != null && _baseMat != null)
            _firstRenderer.material = _baseMat;
    }

    /// <summary>
    /// API antigua que se conservaba para compatibilidad. Ya no hace fade,
    /// simplemente delega en MarkAsCorpse para no romper llamadas externas.
    /// </summary>
    public void StartDeathFade() => MarkAsCorpse();

    /// <summary>
    /// Convierte el enemigo en cadáver permanente y lo persiste entre escenas.
    /// Lo llama EnemyAIBase al terminar la animación de muerte.
    /// </summary>
    public void MarkAsCorpse()
    {
        if (_isCorpse) return;
        _isCorpse = true;

        // Quitar todo lo que pueda interferir con el gameplay tras la muerte
        var ai = GetComponent<EnemyAIBase>();
        if (ai != null) ai.enabled = false;

        var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null) agent.enabled = false;

        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
        }

        // El Animator se queda fijo en el último frame de la death animation
        var anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.speed = 0f;
        }

        // Persistir el cadáver entre escenas
        EnemyCorpseManager.RegisterCorpse(gameObject);
    }
}
