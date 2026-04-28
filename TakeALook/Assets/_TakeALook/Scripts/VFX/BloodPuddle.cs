using UnityEngine;

/// <summary>
/// Gestiona el ciclo de vida de un charco de sangre:
/// crece al recibir impactos de partículas, cambia de color (rojo → marrón oscuro)
/// y desaparece con fade al final de su vida.
/// Usa Shader.PropertyToID para evitar string lookups en cada frame.
/// </summary>
public class BloodPuddle : MonoBehaviour
{
    [SerializeField] float growAmount = 0.25f;
    [SerializeField] float maxScale = 3f;
    [SerializeField] float lifeTime = 12f;
    [SerializeField] Color freshColor = Color.red;
    [SerializeField] Color driedColor = new Color(0.1f, 0f, 0f, 1f);

    // PropertyIDs cacheados (evaluados una sola vez por clase, no por frame)
    private static readonly int ColorProp = Shader.PropertyToID("_Color");
    private static readonly int CutoffProp = Shader.PropertyToID("_Cutoff");

    private Renderer _rend;
    private MaterialPropertyBlock _block;
    private float _timer;
    private float _scale = 0.4f;

    private void Awake()
    {
        _rend = GetComponent<Renderer>();
        _block = new MaterialPropertyBlock();
        transform.localScale = new Vector3(_scale, _scale, 1f);
    }

    public void Grow()
    {
        _scale = Mathf.Min(_scale + growAmount, maxScale);
        transform.localScale = new Vector3(_scale, _scale, 1f);
        // Rejuvenece ligeramente el charco al crecer
        _timer = Mathf.Max(0f, _timer - 1.5f);
    }

    private void Update()
    {
        _timer += Time.deltaTime;
        float t = Mathf.Clamp01(_timer / lifeTime);

        _rend.GetPropertyBlock(_block);
        _block.SetColor(ColorProp, Color.Lerp(freshColor, driedColor, t));
        // Fade-out empieza al 70% del tiempo de vida
        _block.SetFloat(CutoffProp, t > 0.7f
            ? Mathf.Lerp(0.1f, 1f, (t - 0.7f) / 0.3f)
            : 0.1f);
        _rend.SetPropertyBlock(_block);

        if (t >= 1f) gameObject.SetActive(false);
    }
}
