using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class GunSystem : MonoBehaviour
{
    public enum BulletType { Wolf, Bull, Eagle }

    #region Serialized Fields
    [Header("Weapon State")]
    [SerializeField] private BulletType currentBullet = BulletType.Wolf;
    [SerializeField] private float weaponSwapCooldown = 0.5f;

    [Header("Weapon Transitions")]
    [SerializeField] GameObject arms;
    [SerializeField] private float drawCooldown = 0.5f;
    private bool _isTransitioning = false;

    [Header("General References")]
    [SerializeField] private Camera fpsCam;
    [SerializeField] private LayerMask impactLayer;
    [Tooltip("Arrastra aquí manualmente el GameObject Manos_Pistola desde el Inspector")]
    [SerializeField] private Animator anim;
    [SerializeField] private GameObject shootVFX;

    [Header("Ammo Settings (Wolf / Bull / Eagle)")]
    [SerializeField] private GunStats wolfStats;
    [SerializeField] private GunStats bullStats;
    [SerializeField] private GunStats eagleStats;

    [Header("Dev - Live State")]
    [SerializeField] private bool _isShooting;
    [SerializeField] private bool _isReloading;
    [SerializeField] private bool _isGunDrawn;
    #endregion

    #region Data Structures
    [System.Serializable]
    private struct GunStats
    {
        public int damage;
        public float range;
        public float spread;
        public float shootingCooldown;
        public int magCapacity;
        public int currentMag;
        public int reserveAmmo;
        public int maxReserveAmmo;
        public GameObject impactEffect;
    }
    #endregion

    private GunStats _activeStats;
    private bool _canShoot = true;
    private bool _canSwap = true;
    private bool _canReload = true;
    private RaycastHit _hit;

    private void Awake()
    {
        // Survival Horror: Por defecto el arma está guardada
        _isGunDrawn = false;

        // DEBUG TDAH: Balas infinitas temporales para que puedas testear que dispara
        wolfStats.currentMag = wolfStats.magCapacity;
        wolfStats.reserveAmmo = 99;

        bullStats.currentMag = 0; bullStats.reserveAmmo = 0;
        eagleStats.currentMag = 0; eagleStats.reserveAmmo = 0;

        UpdateActiveStats();
    }

    private void Start()
    {
        // CRÍTICO: Aseguramos que al darle al Play, los brazos empiecen apagados
        // Así forzamos al jugador a usar OnDrawn para sacarlos.
        if (arms != null) arms.SetActive(false);
    }

    private void Update()
    {
        if (_isGunDrawn && !_isShooting && !_isReloading && _canSwap)
        {
            HandleScrollInput();
        }
    }

    #region Data Management
    private void UpdateActiveStats()
    {
        switch (currentBullet)
        {
            case BulletType.Wolf: _activeStats = wolfStats; break;
            case BulletType.Bull: _activeStats = bullStats; break;
            case BulletType.Eagle: _activeStats = eagleStats; break;
        }
    }

    private void SaveActiveStats()
    {
        switch (currentBullet)
        {
            case BulletType.Wolf: wolfStats = _activeStats; break;
            case BulletType.Bull: bullStats = _activeStats; break;
            case BulletType.Eagle: eagleStats = _activeStats; break;
        }
    }

    public void AddAmmo(BulletType type, int amount)
    {
        switch (type)
        {
            case BulletType.Wolf:
                wolfStats.reserveAmmo = Mathf.Min(wolfStats.reserveAmmo + amount, wolfStats.maxReserveAmmo);
                break;
            case BulletType.Bull:
                bullStats.reserveAmmo = Mathf.Min(bullStats.reserveAmmo + amount, bullStats.maxReserveAmmo);
                break;
            case BulletType.Eagle:
                eagleStats.reserveAmmo = Mathf.Min(eagleStats.reserveAmmo + amount, eagleStats.maxReserveAmmo);
                break;
        }
        if (type == currentBullet) UpdateActiveStats();
    }
    #endregion

    private void HandleScrollInput()
    {
        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.1f)
        {
            int next = (int)currentBullet + (scroll > 0 ? 1 : -1);
            if (next < 0) next = 2;
            if (next > 2) next = 0;

            currentBullet = (BulletType)next;
            UpdateActiveStats();
            StartCoroutine(SwapCooldownRoutine());
        }
    }

    #region Coroutines & Logic
    private IEnumerator ShootRoutine()
    {
        _isShooting = true;
        _canShoot = false;

        ExecuteRaycast();
        _activeStats.currentMag--;
        SaveActiveStats();

        anim.SetTrigger("Fire");

        yield return null;
        float animDuration = anim.GetCurrentAnimatorStateInfo(0).length;

        yield return new WaitForSeconds(Mathf.Max(animDuration, _activeStats.shootingCooldown));

        _isShooting = false;
        _canShoot = true;
    }

    private void ExecuteRaycast()
    {
        Vector3 direction = fpsCam.transform.forward;
        direction.x += Random.Range(-_activeStats.spread, _activeStats.spread);
        direction.y += Random.Range(-_activeStats.spread, _activeStats.spread);

        if (Physics.Raycast(fpsCam.transform.position, direction, out _hit, _activeStats.range, impactLayer))
        {
            if (_activeStats.impactEffect)
                Instantiate(_activeStats.impactEffect, _hit.point, Quaternion.LookRotation(_hit.normal));

            if (_hit.collider.CompareTag("Enemy"))
            {
                var health = _hit.collider.GetComponent<EnemyHealth>();
                if (health != null)
                {
                    health.TakeDamage(_activeStats.damage);
                }
            }
        }
    }

    private IEnumerator ReloadRoutine()
    {
        _isReloading = true;
        anim.SetTrigger("Reload");

        yield return null;
        float animDuration = anim.GetCurrentAnimatorStateInfo(0).length;
        yield return new WaitForSeconds(animDuration);

        int bulletsNeeded = _activeStats.magCapacity - _activeStats.currentMag;
        int bulletsToReload = Mathf.Min(bulletsNeeded, _activeStats.reserveAmmo);

        _activeStats.currentMag += bulletsToReload;
        _activeStats.reserveAmmo -= bulletsToReload;

        SaveActiveStats();
        _isReloading = false;
    }

    private IEnumerator SwapCooldownRoutine()
    {
        _canSwap = false;
        yield return new WaitForSeconds(weaponSwapCooldown);
        _canSwap = true;
    }
    #endregion

    #region Input Methods
    public void OnShoot(InputAction.CallbackContext context)
    {
        // Chivato de Debug. Si no ves esto en consola, el Input System no encuentra el script.
        if (context.performed) Debug.Log("Señal de Disparo recibida en GunSystem.");

        if (context.performed && _isGunDrawn && _canShoot && !_isReloading && _activeStats.currentMag > 0)
        {
            StartCoroutine(ShootRoutine());
        }
    }

    public void OnReload(InputAction.CallbackContext context)
    {
        if (context.performed) Debug.Log("Señal de Recarga recibida en GunSystem.");

        bool needsReload = _activeStats.currentMag < _activeStats.magCapacity;
        bool hasReserveAmmo = _activeStats.reserveAmmo > 0;

        if (context.performed && _isGunDrawn && needsReload && hasReserveAmmo && !_isReloading && !_isShooting)
        {
            StartCoroutine(ReloadRoutine());
        }
    }

    public void OnInspect(InputAction.CallbackContext context)
    {
        if (context.performed && !_isShooting && !_isReloading && _isGunDrawn)
        {
            anim.SetTrigger("Inspect");
        }
    }

    public void OnDrawn(InputAction.CallbackContext context)
    {
        if (context.performed) Debug.Log("Señal de DRAW recibida en GunSystem. Arma sacada: " + _isGunDrawn);

        if (!context.performed || _isShooting || _isReloading || _isTransitioning) return;

        if (!_isGunDrawn)
        {
            StartCoroutine(DrawWeaponRoutine());
        }
        else
        {
            StartCoroutine(SheathWeaponRoutine());
        }
    }

    private IEnumerator DrawWeaponRoutine()
    {
        _isTransitioning = true;
        _isGunDrawn = true;

        // 1. Encendemos los brazos
        arms.gameObject.SetActive(true);

        // 2. EL FRAME VITAL PARA QUE UNITY REACCIONE
        yield return null;

        // 3. Mandamos las órdenes al Animator
        anim.SetFloat("DrawSpeed", 1f);
        anim.SetTrigger("Draw");

        // 4. Esperamos otro frame para que el Animator actualice el estado interno
        yield return null;

        float animDuration = anim.GetCurrentAnimatorStateInfo(0).length;
        yield return new WaitForSeconds(animDuration);

        if (drawCooldown > 0) yield return new WaitForSeconds(drawCooldown);

        _canShoot = true;
        _canSwap = true;
        _canReload = true;

        _isTransitioning = false;
        Debug.Log("Proceso de Draw completado.");
    }

    private IEnumerator SheathWeaponRoutine()
    {
        _isTransitioning = true;

        _canShoot = false;
        _canSwap = false;
        _canReload = false;

        anim.SetFloat("DrawSpeed", -1f);
        anim.SetTrigger("Draw");

        yield return null;
        float animDuration = anim.GetCurrentAnimatorStateInfo(0).length;
        yield return new WaitForSeconds(animDuration);

        arms.gameObject.SetActive(false);
        _isGunDrawn = false;

        _isTransitioning = false;
        Debug.Log("Proceso de Sheath completado.");
    }
    #endregion
}