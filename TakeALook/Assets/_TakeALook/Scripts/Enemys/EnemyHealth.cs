using System.Collections;
using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] int maxHealth = 100;

    [Header("Hit Feedback")]
    [SerializeField] Material damagedMat;

    [Header("Death Fade")]
    [Tooltip("Segundos de espera tras acabar la animación de muerte antes del fade.")]
    [SerializeField] float deathWaitBeforeFade = 0.5f;
    [Tooltip("Duración en segundos del dissolve/fade granulado.")]
    [SerializeField] float fadeDuration = 2f;
    [Tooltip("Nombre del float de dissolve en el shader (e.g. _DissolveAmount). Fallback: alpha.")]
    [SerializeField] string dissolveProperty = "_DissolveAmount";

    [Header("Refs")]
    [Tooltip("Renderers de la malla del enemigo. Se auto-detectan si se dejan vacíos.")]
    [SerializeField] Renderer[] meshRenderers;

    int _currentHealth;
    bool _isDead;
    Material _baseMat;
    Renderer _firstRenderer;
    EnemyAIBase _ai;

    public float HealthPercent => _isDead ? 0f : (float)_currentHealth / Mathf.Max(1, maxHealth);

    void Awake()
    {
        _currentHealth = maxHealth;
        _ai = GetComponent<EnemyAIBase>();

        if (meshRenderers == null || meshRenderers.Length == 0)
            meshRenderers = GetComponentsInChildren<Renderer>();

        if (meshRenderers.Length > 0)
        {
            _firstRenderer = meshRenderers[0];
            _baseMat = _firstRenderer.material;
        }
    }

    // isBullBullet = true mata de un golpe con animación Electrocuted
    public void TakeDamage(int damage, bool isBullBullet = false)
    {
        if (_isDead) return;

        if (isBullBullet)
        {
            Kill(isBull: true);
            return;
        }

        if (_ai != null && _ai.IsInvulnerable) return;

        // Feedback visual de impacto
        if (damagedMat != null && _firstRenderer != null)
        {
            _firstRenderer.material = damagedMat;
            Invoke(nameof(ResetMat), 0.1f);
        }

        _currentHealth = Mathf.Max(0, _currentHealth - damage);

        if (_currentHealth <= 0)
            Kill(isBull: false);
        else
            _ai?.OnHitByNormalBullet();
    }

    void Kill(bool isBull)
    {
        if (_isDead) return;
        _isDead = true;
        _currentHealth = 0;
        CancelInvoke(nameof(ResetMat));

        if (isBull) _ai?.OnKilledByBullBullet();
        else _ai?.OnKilledByNormalBullet();
    }

    void ResetMat()
    {
        if (_firstRenderer != null && _baseMat != null)
            _firstRenderer.material = _baseMat;
    }

    // Llamado por EnemyAIBase al terminar la animación de muerte
    public void StartDeathFade() => StartCoroutine(FadeOut());

    IEnumerator FadeOut()
    {
        yield return new WaitForSeconds(deathWaitBeforeFade);

        // Crear instancias de material por instancia para no afectar otros enemigos
        Material[] mats = new Material[meshRenderers.Length];
        for (int i = 0; i < meshRenderers.Length; i++)
        {
            if (meshRenderers[i] != null)
                mats[i] = meshRenderers[i].material; // crea instancia
        }

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);

            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;

                if (mats[i].HasProperty(dissolveProperty))
                {
                    mats[i].SetFloat(dissolveProperty, t);
                }
                else
                {
                    // Fallback: fade de alpha (requiere modo Transparent en el material)
                    Color c = mats[i].color;
                    c.a = 1f - t;
                    mats[i].color = c;
                }
            }
            yield return null;
        }

        Destroy(gameObject);
    }
}