using UnityEngine;

/// <summary>
/// Gestiona el ciclo de vida de un charco de sangre:
/// crece al recibir impactos de partículas, cambia de color (rojo → marrón oscuro)
/// y desaparece con fade al final de su vida.
///
/// Notas:
///  - PropertyIDs cacheados (string lookups solo una vez por clase).
///  - Antes hacía SetActive(false) al final, lo que dejaba GameObjects "fantasma" en
///    la escena. Ahora se DESTRUYE limpiamente.
///  - Se asegura un Collider para que BloodCollisionHandler pueda detectarlo y crecerlo.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class BloodPuddle : MonoBehaviour
{
    [SerializeField] float growAmount = 0.25f;
    [SerializeField] float maxScale = 3f;
    [SerializeField] float lifeTime = 12f;
    [SerializeField] Color freshColor = Color.red;
    [SerializeField] Color driedColor = new Color(0.1f, 0f, 0f, 1f);

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

        // Garantizar collider para que BloodCollisionHandler pueda detectarlo y crecerlo.
        if (GetComponent<Collider>() == null)
        {
            var col = gameObject.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.5f;
        }
    }

    public void Grow()
    {
        _scale = Mathf.Min(_scale + growAmount, maxScale);
        transform.localScale = new Vector3(_scale, _scale, 1f);
        _timer = Mathf.Max(0f, _timer - 1.5f);
    }

    private void Update()
    {
        if (_rend == null) return;

        _timer += Time.deltaTime;
        float t = Mathf.Clamp01(_timer / lifeTime);

        _rend.GetPropertyBlock(_block);
        _block.SetColor(ColorProp, Color.Lerp(freshColor, driedColor, t));
        _block.SetFloat(CutoffProp, t > 0.7f
            ? Mathf.Lerp(0.1f, 1f, (t - 0.7f) / 0.3f)
            : 0.1f);
        _rend.SetPropertyBlock(_block);

        if (t >= 1f) Destroy(gameObject);
    }
}
