using UnityEngine;

public class BloodPuddle : MonoBehaviour
{
    public float growAmount = 0.25f;
    public float maxScale = 3f;
    public float lifeTime = 12f;
    public Color freshColor = Color.red;
    public Color driedColor = new Color(0.1f, 0, 0, 1);

    private Renderer rend;
    private MaterialPropertyBlock block;
    private float timer;
    private float scale = 0.4f;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        block = new MaterialPropertyBlock();
        transform.localScale = new Vector3(scale, scale, 1f);
    }

    public void Grow()
    {
        scale = Mathf.Min(scale + growAmount, maxScale);
        transform.localScale = new Vector3(scale, scale, 1f);
        timer = Mathf.Max(0, timer - 1.5f);
    }

    void Update()
    {
        timer += Time.deltaTime;
        float p = Mathf.Clamp01(timer / lifeTime);

        rend.GetPropertyBlock(block);
        block.SetColor("_Color", Color.Lerp(freshColor, driedColor, p));
        block.SetFloat("_Cutoff", (p > 0.7f) ? Mathf.Lerp(0.1f, 1f, (p - 0.7f) / 0.3f) : 0.1f);
        rend.SetPropertyBlock(block);

        if (p >= 1f) gameObject.SetActive(false);
    }
}