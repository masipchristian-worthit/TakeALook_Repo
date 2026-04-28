using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using DG.Tweening;

public class GunSystem : MonoBehaviour
{
    public enum BulletType { Wolf, Bull, Eagle }

    #region Serialized Fields
    [Header("Weapon State")]
    [SerializeField] private BulletType currentBullet = BulletType.Wolf;
    [SerializeField] private float weaponSwapCooldown = 0.18f;

    [Header("Weapon Transitions")]
    [SerializeField] GameObject arms;
    [SerializeField] private float drawCooldown = 0.6f;
    [SerializeField] private float reloadTime = 1.5f;
    private bool _isTransitioning = false;

    [Header("General References")]
    [SerializeField] private Camera fpsCam;
    [SerializeField] private LayerMask impactLayer;
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

    [Header("Animation Performance Overrides")]
    [Tooltip("Multiplicador global para acelerar las animaciones del Animator. 1 = Normal.")]
    [SerializeField] private float globalAnimationSpeedMultiplier = 1.0f;
    [Tooltip("Permite disparar más rápido que la animación. Si false, el cooldown se ajusta a la duración visual.")]
    [SerializeField] private bool allowRapidFireOverlap = true;

    [Header("Flashlight System")]
    [SerializeField] private Light flashlight;
    [SerializeField] private float maxFlashlightIntensity = 5f;
    [SerializeField] private float flashlightTransitionTime = 0.8f;
    [Range(0f, 1f)]
    [Tooltip("0 = Transición suave, 1 = Parpadeo caótico.")]
    [SerializeField] private float flashlightFlickerAmount = 0.5f;
    [Tooltip("Empezar la partida con la linterna desbloqueada (testing). Desactívalo en producción.")]
    [SerializeField] private bool startWithFlashlight = true;

    [Header("Audio IDs")]
    [SerializeField] private string drawSfxId = "gun_draw";
    [SerializeField] private string sheathSfxId = "gun_sheath";
    [SerializeField] private string shootSfxId = "gun_shoot";
    [SerializeField] private string reloadSfxId = "gun_reload";
    [SerializeField] private string emptySfxId = "gun_empty";
    [SerializeField] private string swapSfxId = "gun_swap";
    [SerializeField] private string flashlightOnId = "flashlight_on";
    [SerializeField] private string flashlightOffId = "flashlight_off";

    [Header("Input Buffer")]
    [SerializeField] private float inputBufferWindow = 0.18f;
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
    private bool _masterActionLock = false;

    private Renderer[] _armsRenderers;

    private readonly Queue<int> _scrollBuffer = new Queue<int>();
    private float _nextSwapAllowedTime;
    private float _bufferLastEnqueueTime;

    // Linterna
    private bool _isFlashlightOn = false;
    private bool _hasFlashlight = false;
    private Tween _flashlightTween;

    // Progress tracking para HUD
    private float _reloadProgress = 1f;
    private float _shootCooldownProgress = 1f;

    // Public API
    public BulletType CurrentBulletType => currentBullet;
    public bool IsReloading => _isReloading;
    public bool IsShooting => _isShooting;
    public float ReloadProgress => _reloadProgress;
    public float ShootCooldownProgress => _shootCooldownProgress;
    public bool IsFlashlightOn => _isFlashlightOn;
    public bool HasFlashlight { get => _hasFlashlight; set => _hasFlashlight = value; }

    public int GetMag(BulletType type) => GetStatsRef(type).currentMag;
    public int GetReserve(BulletType type) => GetStatsRef(type).reserveAmmo;
    public int GetMagCapacity(BulletType type) => GetStatsRef(type).magCapacity;

    private GunStats GetStatsRef(BulletType type)
    {
        switch (type)
        {
            case BulletType.Wolf: return wolfStats;
            case BulletType.Bull: return bullStats;
            case BulletType.Eagle: return eagleStats;
        }
        return wolfStats;
    }

    private void Awake()
    {
        _isGunDrawn = false;
        _isTransitioning = false;
        _isShooting = false;
        _isReloading = false;
        _masterActionLock = false;
        _isFlashlightOn = false;
        _hasFlashlight = startWithFlashlight;

        if (wolfStats.magCapacity <= 0) { wolfStats.magCapacity = 30; wolfStats.currentMag = 30; wolfStats.reserveAmmo = 90; wolfStats.damage = 10; wolfStats.range = 100f; wolfStats.maxReserveAmmo = 99; }
        if (bullStats.magCapacity <= 0) { bullStats.magCapacity = 30; bullStats.currentMag = 30; bullStats.reserveAmmo = 90; bullStats.damage = 15; bullStats.range = 100f; bullStats.maxReserveAmmo = 99; }
        if (eagleStats.magCapacity <= 0) { eagleStats.magCapacity = 30; eagleStats.currentMag = 30; eagleStats.reserveAmmo = 90; eagleStats.damage = 20; eagleStats.range = 100f; eagleStats.maxReserveAmmo = 99; }

        wolfStats.currentMag = wolfStats.magCapacity;
        bullStats.currentMag = bullStats.magCapacity;
        eagleStats.currentMag = eagleStats.magCapacity;

        UpdateActiveStats();

        if (arms != null) _armsRenderers = arms.GetComponentsInChildren<Renderer>(true);

        if (flashlight != null)
        {
            flashlight.intensity = 0f;
            flashlight.gameObject.SetActive(false);
        }
    }

    private void Start()
    {
        if (arms != null) arms.SetActive(false);
        if (anim != null) anim.speed = globalAnimationSpeedMultiplier;
    }

    private void Update()
    {
        bool uiBlocked = UIManager.Instance != null && UIManager.Instance.IsUIPanelOpen();

        if (uiBlocked)
        {
            _scrollBuffer.Clear();
            return;
        }

        if (_isGunDrawn && !_isShooting && !_isReloading && _canSwap && !_masterActionLock)
        {
            ProcessScrollInput();
            ProcessScrollBuffer();
        }
    }

    #region Helpers
    private void SetArmsRenderersEnabled(bool enabled)
    {
        if (_armsRenderers == null) return;
        for (int i = 0; i < _armsRenderers.Length; i++)
            if (_armsRenderers[i] != null) _armsRenderers[i].enabled = enabled;
    }

    private bool IsInputBlocked()
    {
        return UIManager.Instance != null && UIManager.Instance.IsUIPanelOpen();
    }
    #endregion

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

    public bool AddAmmo(BulletType type, int amount)
    {
        bool added = false;
        switch (type)
        {
            case BulletType.Wolf:
                if (wolfStats.reserveAmmo < wolfStats.maxReserveAmmo)
                { wolfStats.reserveAmmo = Mathf.Min(wolfStats.reserveAmmo + amount, wolfStats.maxReserveAmmo); added = true; }
                break;
            case BulletType.Bull:
                if (bullStats.reserveAmmo < bullStats.maxReserveAmmo)
                { bullStats.reserveAmmo = Mathf.Min(bullStats.reserveAmmo + amount, bullStats.maxReserveAmmo); added = true; }
                break;
            case BulletType.Eagle:
                if (eagleStats.reserveAmmo < eagleStats.maxReserveAmmo)
                { eagleStats.reserveAmmo = Mathf.Min(eagleStats.reserveAmmo + amount, eagleStats.maxReserveAmmo); added = true; }
                break;
        }
        if (type == currentBullet) UpdateActiveStats();
        return added;
    }

    public void SetBulletType(BulletType type)
    {
        if (type == currentBullet) return;
        currentBullet = type;
        UpdateActiveStats();
        AudioManager.Instance?.PlayUI(swapSfxId);
        StartCoroutine(SwapCooldownRoutine());
    }
    #endregion

    #region Mouse Wheel - Input Buffer
    private void ProcessScrollInput()
    {
        if (Mouse.current == null) return;
        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.1f)
        {
            if (Time.time - _bufferLastEnqueueTime > 0.04f)
            {
                _scrollBuffer.Enqueue(scroll > 0 ? 1 : -1);
                _bufferLastEnqueueTime = Time.time;
                while (_scrollBuffer.Count > 4) _scrollBuffer.Dequeue();
            }
        }

        if (_scrollBuffer.Count > 0 && Time.time - _bufferLastEnqueueTime > inputBufferWindow * 3f)
            _scrollBuffer.Clear();
    }

    private void ProcessScrollBuffer()
    {
        if (_scrollBuffer.Count == 0 || Time.time < _nextSwapAllowedTime) return;
        int dir = _scrollBuffer.Dequeue();
        int next = ((int)currentBullet + dir + 3) % 3;
        SetBulletType((BulletType)next);
        _nextSwapAllowedTime = Time.time + weaponSwapCooldown;
    }
    #endregion

    #region Input Methods (PlayerInput)
    public void OnDrawn(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        if (IsInputBlocked()) return;
        if (_isShooting || _isReloading || _isTransitioning || _masterActionLock) return;

        if (!_isGunDrawn) StartCoroutine(DrawWeaponRoutine());
        else StartCoroutine(SheathWeaponRoutine());
    }

    public void OnShoot(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        if (IsInputBlocked()) return;
        if (!_isGunDrawn || !_canShoot || _isReloading || _isTransitioning || _masterActionLock) return;

        if (_activeStats.currentMag > 0)
            StartCoroutine(ShootRoutine());
        else
            AudioManager.Instance?.PlaySFX(emptySfxId, transform.position);
    }

    public void OnReload(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        if (IsInputBlocked()) return;
        if (!_isGunDrawn || _isReloading || _isShooting || _isTransitioning || _masterActionLock) return;

        bool needsReload = _activeStats.currentMag < _activeStats.magCapacity;
        bool hasReserveAmmo = _activeStats.reserveAmmo > 0;
        if (needsReload && hasReserveAmmo) StartCoroutine(ReloadRoutine());
    }

    public void OnInspect(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        if (IsInputBlocked()) return;
        if (!_isGunDrawn || _isShooting || _isReloading || _isTransitioning || _masterActionLock) return;

        if (anim != null && anim.isActiveAndEnabled)
        {
            anim.ResetTrigger("Inspect");
            anim.SetTrigger("Inspect");
        }
    }

    public void OnFlashlight(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        if (IsInputBlocked()) return;
        if (flashlight == null || !_hasFlashlight) return;

        _isFlashlightOn = !_isFlashlightOn;
        AudioManager.Instance?.PlaySFX(_isFlashlightOn ? flashlightOnId : flashlightOffId, transform.position);

        _flashlightTween?.Kill();

        if (_isFlashlightOn)
        {
            flashlight.gameObject.SetActive(true);
            _flashlightTween = DOVirtual.Float(flashlight.intensity, maxFlashlightIntensity, flashlightTransitionTime, val =>
            {
                if (flashlightFlickerAmount > 0f && Random.value < flashlightFlickerAmount * 0.4f)
                    flashlight.intensity = val * Random.Range(0f, 0.3f);
                else
                    flashlight.intensity = val;
            }).SetEase(Ease.OutQuad)
              .OnComplete(() => flashlight.intensity = maxFlashlightIntensity);
        }
        else
        {
            _flashlightTween = DOVirtual.Float(flashlight.intensity, 0f, flashlightTransitionTime, val =>
            {
                if (flashlightFlickerAmount > 0f && Random.value < flashlightFlickerAmount * 0.4f)
                    flashlight.intensity = val + Random.Range(0f, maxFlashlightIntensity * 0.6f);
                else
                    flashlight.intensity = val;
            }).SetEase(Ease.InQuad)
              .OnComplete(() =>
              {
                  flashlight.intensity = 0f;
                  flashlight.gameObject.SetActive(false);
              });
        }
    }
    #endregion

    #region Coroutines & Logic
    private IEnumerator DrawWeaponRoutine()
    {
        _isTransitioning = true;
        _isGunDrawn = true;
        _masterActionLock = true;

        if (arms != null)
        {
            arms.SetActive(true);
            SetArmsRenderersEnabled(false);
        }

        if (anim != null && anim.isActiveAndEnabled)
        {
            anim.speed = globalAnimationSpeedMultiplier;
            anim.ResetTrigger("Draw");
            anim.SetFloat("DrawSpeed", 1f);
            anim.SetTrigger("Draw");
            anim.Update(0f);
        }

        yield return null;
        yield return null;

        SetArmsRenderersEnabled(true);
        AudioManager.Instance?.PlaySFX(drawSfxId, transform.position);

        float wait = Mathf.Max(drawCooldown - Time.deltaTime * 2f, 0.05f);
        yield return new WaitForSeconds(wait);

        if (anim != null && anim.isActiveAndEnabled)
        {
            anim.ResetTrigger("SheathComplete");
            anim.SetTrigger("SheathComplete");
        }

        _canShoot = true;
        _canSwap = true;
        _canReload = true;
        _isTransitioning = false;
        _masterActionLock = false;
    }

    private IEnumerator SheathWeaponRoutine()
    {
        _isTransitioning = true;
        _canShoot = false;
        _canSwap = false;
        _canReload = false;
        _masterActionLock = true;

        AudioManager.Instance?.PlaySFX(sheathSfxId, transform.position);
        float actualSheathWait = drawCooldown;

        if (anim != null && anim.isActiveAndEnabled)
        {
            anim.speed = globalAnimationSpeedMultiplier;
            anim.ResetTrigger("Draw");
            anim.SetFloat("DrawSpeed", -1f);
            anim.SetTrigger("Draw");
            anim.Update(0f);

            yield return null;

            AnimatorStateInfo currentState = anim.GetCurrentAnimatorStateInfo(0);
            AnimatorStateInfo nextState = anim.GetNextAnimatorStateInfo(0);

            int targetStateHash = nextState.fullPathHash != 0 ? nextState.fullPathHash : currentState.fullPathHash;
            float stateLength = nextState.fullPathHash != 0 ? nextState.length : currentState.length;

            anim.Play(targetStateHash, 0, 1.0f);

            if (stateLength > 0.05f)
                actualSheathWait = stateLength / Mathf.Max(0.01f, globalAnimationSpeedMultiplier);
        }

        yield return new WaitForSeconds(actualSheathWait);

        if (anim != null && anim.isActiveAndEnabled)
        {
            anim.ResetTrigger("SheathComplete");
            anim.SetTrigger("SheathComplete");
        }

        SetArmsRenderersEnabled(false);
        if (arms != null) arms.SetActive(false);

        _isGunDrawn = false;
        _isTransitioning = false;
        _masterActionLock = false;
    }

    private IEnumerator ShootRoutine()
    {
        _isShooting = true;
        _canShoot = false;
        _masterActionLock = true;
        _shootCooldownProgress = 0f;

        ExecuteRaycast();
        _activeStats.currentMag--;
        SaveActiveStats();

        AudioManager.Instance?.PlaySFX(shootSfxId, transform.position);
        float dynamicCooldown = _activeStats.shootingCooldown;

        if (anim != null && anim.isActiveAndEnabled)
        {
            anim.speed = globalAnimationSpeedMultiplier;
            anim.ResetTrigger("Fire");
            anim.SetTrigger("Fire");
            anim.Update(0f);

            if (!allowRapidFireOverlap)
            {
                AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
                if (stateInfo.length > _activeStats.shootingCooldown)
                    dynamicCooldown = stateInfo.length / Mathf.Max(0.01f, globalAnimationSpeedMultiplier);
            }
        }

        float elapsed = 0f;
        while (elapsed < dynamicCooldown)
        {
            elapsed += Time.deltaTime;
            _shootCooldownProgress = Mathf.Clamp01(elapsed / dynamicCooldown);
            yield return null;
        }

        _shootCooldownProgress = 1f;
        _isShooting = false;
        _canShoot = true;
        _masterActionLock = false;
    }

    private IEnumerator ReloadRoutine()
    {
        _isReloading = true;
        _canShoot = false;
        _masterActionLock = true;
        _reloadProgress = 0f;

        AudioManager.Instance?.PlaySFX(reloadSfxId, transform.position);

        if (anim != null && anim.isActiveAndEnabled)
        {
            anim.speed = globalAnimationSpeedMultiplier;
            anim.ResetTrigger("Reload");
            anim.SetTrigger("Reload");
            anim.Update(0f);
        }

        float currentReloadTime = reloadTime;
        if (anim != null && anim.isActiveAndEnabled)
        {
            AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.length > reloadTime)
                currentReloadTime = stateInfo.length / Mathf.Max(0.01f, globalAnimationSpeedMultiplier);
        }

        float elapsed = 0f;
        while (elapsed < currentReloadTime)
        {
            elapsed += Time.deltaTime;
            _reloadProgress = Mathf.Clamp01(elapsed / currentReloadTime);
            yield return null;
        }

        _reloadProgress = 1f;

        int bulletsNeeded = _activeStats.magCapacity - _activeStats.currentMag;
        int bulletsToReload = Mathf.Min(bulletsNeeded, _activeStats.reserveAmmo);
        _activeStats.currentMag += bulletsToReload;
        _activeStats.reserveAmmo -= bulletsToReload;
        SaveActiveStats();

        _isReloading = false;
        _canShoot = true;
        _masterActionLock = false;
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
                    health.TakeDamage(_activeStats.damage, currentBullet, _hit.point, _hit.normal);
            }
        }
    }

    private IEnumerator SwapCooldownRoutine()
    {
        _canSwap = false;
        yield return new WaitForSeconds(weaponSwapCooldown);
        _canSwap = true;
    }
    #endregion

    #region Debug Tools
    [ContextMenu("Debug: Forzar Draw/Sheath")]
    private void DebugForceDraw()
    {
        if (!_isGunDrawn) StartCoroutine(DrawWeaponRoutine());
        else StartCoroutine(SheathWeaponRoutine());
    }

    [ContextMenu("Debug: Resetear Bloqueos")]
    private void DebugResetStates()
    {
        StopAllCoroutines();
        _isShooting = false;
        _isReloading = false;
        _isTransitioning = false;
        _canShoot = true;
        _canSwap = true;
        _canReload = true;
        _masterActionLock = false;
    }
    #endregion
}
