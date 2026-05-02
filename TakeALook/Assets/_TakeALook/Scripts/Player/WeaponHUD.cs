using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// HUD de combate y movimiento. Gestiona tres barras:
///   - Recarga: visible durante la animación de recarga.
///   - Cooldown de disparo: visible mientras se dispara o el cooldown no termina.
///   - Sprint: visible mientras el jugador está esprintando o la stamina se está recuperando.
/// Todas las barras aparecen y desaparecen con fades DOTween.
/// Si ninguna condición está activa, el HUD es completamente invisible.
/// </summary>
public class WeaponHUD : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private GunSystem gunSystem;
    [SerializeField] private FPS_Controller fpsController;

    [Header("Barra de Recarga")]
    [SerializeField] private CanvasGroup reloadBarGroup;
    [SerializeField] private Slider reloadBar;

    [Header("Barra de Cooldown de Disparo")]
    [SerializeField] private CanvasGroup shootBarGroup;
    [SerializeField] private Slider shootBar;

    [Header("Barra de Sprint")]
    [SerializeField] private CanvasGroup sprintBarGroup;
    [SerializeField] private Slider sprintBar;

    [Header("Fade")]
    [SerializeField] private float fadeInDuration  = 0.08f;
    [SerializeField] private float fadeOutDuration = 0.25f;

    private bool _reloadVisible;
    private bool _shootVisible;
    private bool _sprintVisible;

    private void Awake()
    {
        SetAlpha(reloadBarGroup,  0f);
        SetAlpha(shootBarGroup,   0f);
        SetAlpha(sprintBarGroup,  0f);
    }

    private void Update()
    {
        UpdateReloadBar();
        UpdateShootBar();
        UpdateSprintBar();
    }

    // ── Reload ──────────────────────────────────────────────────────────────
    private void UpdateReloadBar()
    {
        if (gunSystem == null) return;

        bool show = gunSystem.IsReloading;
        SetVisible(reloadBarGroup, ref _reloadVisible, show);

        if (reloadBar != null && show)
            reloadBar.value = gunSystem.ReloadProgress;
    }

    // ── Shoot cooldown ───────────────────────────────────────────────────────
    private void UpdateShootBar()
    {
        if (gunSystem == null) return;

        bool show = gunSystem.IsShooting ||
                    (!gunSystem.IsReloading && gunSystem.ShootCooldownProgress > 0.01f);
        SetVisible(shootBarGroup, ref _shootVisible, show);

        if (shootBar != null && show)
            shootBar.value = gunSystem.ShootCooldownProgress;
    }

    // ── Sprint stamina ───────────────────────────────────────────────────────
    private void UpdateSprintBar()
    {
        if (fpsController == null) return;

        // Visible mientras esprintamos O mientras la stamina no se ha recuperado del todo.
        bool show = fpsController.IsSprinting ||
                    fpsController.SprintStaminaNormalized < 0.99f;
        SetVisible(sprintBarGroup, ref _sprintVisible, show);

        if (sprintBar != null && show)
            sprintBar.value = fpsController.SprintStaminaNormalized;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private void SetVisible(CanvasGroup group, ref bool current, bool target)
    {
        if (target == current) return;
        current = target;
        FadeTo(group, target ? 1f : 0f, target ? fadeInDuration : fadeOutDuration);
    }

    private static void SetAlpha(CanvasGroup g, float a)
    {
        if (g != null) g.alpha = a;
    }

    private static void FadeTo(CanvasGroup g, float target, float duration)
    {
        if (g == null) return;
        g.DOKill();
        g.DOFade(target, duration).SetUpdate(true);
    }
}
