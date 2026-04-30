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
    [SerializeField] private float inspectTime = 1.5f;
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
    [Tooltip("Permite disparar m�s r�pido que la animaci�n. Si false, el cooldown se ajusta a la duraci�n visual.")]
    [SerializeField] private bool allowRapidFireOverlap = true;

    [Header("Flashlight System")]
    [SerializeField] private Light flashlight;
    [SerializeField] private float maxFlashlightIntensity = 5f;
    [SerializeField] private float flashlightTransitionTime = 0.8f;
    [Range(0f, 1f)]
    [Tooltip("0 = Transici�n suave, 1 = Parpadeo ca�tico.")]
    [SerializeField] private float flashlightFlickerAmount = 0.5f;
    [Tooltip("Empezar la partida con la linterna desbloqueada (testing). Desact�valo en producci�n.")]
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

    // Cooldown tracking para la barra de UI
    private float _lastShotTime = -999f;
    private float _lastShotCooldown = 1f;
    private float _reloadStartTime = -999f;
    private float _reloadTotalTime = 1f;

    // 1.0 = recién disparado (barra llena), 0.0 = listo para disparar (barra vacía)
    public float ShootCooldownNormalized =>
        1f - Mathf.Clamp01((Time.time - _lastShotTime) / Mathf.Max(0.001f, _lastShotCooldown));

    // Public API para WeaponHUD
    public bool IsReloading => _isReloading;
    public bool IsShooting => _isShooting;
    public float ShootCooldownProgress => ShootCooldownNormalized;
    public float ReloadProgress =>
        Mathf.Clamp01((Time.time - _reloadStartTime) / Mathf.Max(0.001f, _reloadTotalTime));

    // Renderers cacheados para evitar parpadeo de malla en draw/sheath
    private Renderer[] _armsRenderers;

    // Input buffer (rueda del rat�n)
    private readonly Queue<int> _scrollBuffer = new Queue<int>();
    private float _nextSwapAllowedTime;
    private float _bufferLastEnqueueTime;

    // Linterna
    private bool _isFlashlightOn = false;
    private bool _hasFlashlight = false;
    private Tween _flashlightTween;
    public bool HasFlashlight { get => _hasFlashlight; set => _hasFlashlight = value; }

    // Public API para UI
    public BulletType CurrentBulletType => currentBullet;
    public int GetMag(BulletType type) => GetStatsRef(type).currentMag;
    public int GetReserve(BulletType type) => GetStatsRef(type).reserveAmmo;
    public int GetMagCapacity(BulletType type) => GetStatsRef(type).magCapacity;

    // True si el cargador del arma activa está vacío (los enemigos lo usan para rushear).
    public bool IsCurrentMagEmpty => _activeStats.currentMag <= 0;

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
        // El GameObject de los brazos se mantiene SIEMPRE activo para que el Animator
        // no se reinicie cada vez que se saca/guarda el arma (eso causaba parpadeos
        // y desincronización entre la malla y el estado _isGunDrawn).
        // La visibilidad se controla únicamente toggleando los renderers.
        if (arms != null)
        {
            arms.SetActive(true);
            SetArmsRenderersEnabled(false);
        }
        if (anim != null) anim.speed = globalAnimationSpeedMultiplier;
    }

    private void Update()
    {
    }

    #region Helpers - Renderers + Input Block
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
                {
                    wolfStats.reserveAmmo = Mathf.Min(wolfStats.reserveAmmo + amount, wolfStats.maxReserveAmmo);
                    added = true;
                }
                break;
            case BulletType.Bull:
                if (bullStats.reserveAmmo < bullStats.maxReserveAmmo)
                {
                    bullStats.reserveAmmo = Mathf.Min(bullStats.reserveAmmo + amount, bullStats.maxReserveAmmo);
                    added = true;
                }
                break;
            case BulletType.Eagle:
                if (eagleStats.reserveAmmo < eagleStats.maxReserveAmmo)
                {
                    eagleStats.reserveAmmo = Mathf.Min(eagleStats.reserveAmmo + amount, eagleStats.maxReserveAmmo);
                    added = true;
                }
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
            // Evitar enqueues m�ltiples por un mismo "tick" del scroll
            if (Time.time - _bufferLastEnqueueTime > 0.04f)
            {
                _scrollBuffer.Enqueue(scroll > 0 ? 1 : -1);
                _bufferLastEnqueueTime = Time.time;

                // Limitar tama�o del buffer
                while (_scrollBuffer.Count > 4) _scrollBuffer.Dequeue();
            }
        }

        // Descartar buffer si la entrada es muy antigua
        if (_scrollBuffer.Count > 0 && Time.time - _bufferLastEnqueueTime > inputBufferWindow * 3f)
            _scrollBuffer.Clear();
    }

    private void ProcessScrollBuffer()
    {
        if (_scrollBuffer.Count == 0 || Time.time < _nextSwapAllowedTime) return;
        int dir = _scrollBuffer.Dequeue();
        BulletType next = (BulletType)(((int)currentBullet + dir + 3) % 3);
        if (next == currentBullet) return;
        _nextSwapAllowedTime = Time.time + weaponSwapCooldown + inspectTime;
        StartCoroutine(SwapViaInspectRoutine(next));
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
        {
            StartCoroutine(ShootRoutine());
        }
        else
        {
            AudioManager.Instance?.PlaySFX(emptySfxId, transform.position);
        }
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

        StartCoroutine(InspectAndSwapRoutine());
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
    /// <summary>
    /// FIX PARPADEO: el GameObject 'arms' se mantiene siempre activo (Start). Aquí solo encendemos
    /// los renderers DESPUÉS de que el Animator haya evaluado el primer frame del clip de Draw.
    /// </summary>
    private IEnumerator DrawWeaponRoutine()
    {
        _isTransitioning = true;
        _isGunDrawn = true;
        _masterActionLock = true;

        // Garantizamos que el GO esté activo y los renderers ocultos antes de empezar.
        if (arms != null && !arms.activeSelf) arms.SetActive(true);
        SetArmsRenderersEnabled(false);

        if (anim != null && anim.isActiveAndEnabled)
        {
            anim.speed = globalAnimationSpeedMultiplier;
            anim.ResetTrigger("Draw");
            anim.ResetTrigger("SheathComplete");
            anim.SetFloat("DrawSpeed", 1f);
            anim.SetTrigger("Draw");
            anim.Update(0f); // forzar evaluación inmediata del trigger
        }

        // Esperamos hasta que el Animator haya entrado de verdad en el estado de Draw,
        // para que los renderers no se enciendan mostrando una pose intermedia.
        yield return null;
        if (anim != null && anim.isActiveAndEnabled)
        {
            float guard = 0f;
            while (anim.IsInTransition(0) && guard < 0.25f)
            {
                guard += Time.deltaTime;
                yield return null;
            }
            anim.Update(0f);
        }
        yield return new WaitForEndOfFrame();

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
            anim.ResetTrigger("SheathComplete");
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

        // Solo ocultamos los renderers. NO desactivamos el GameObject para evitar
        // que el Animator se reinicie en el próximo Draw (causa raíz del parpadeo
        // y de que pareciera que "no se contaba como sacada" la primera vez).
        SetArmsRenderersEnabled(false);

        _isGunDrawn = false;
        _isTransitioning = false;
        _masterActionLock = false;
    }

    private IEnumerator ShootRoutine()
    {
        _isShooting = true;
        _canShoot = false;
        _masterActionLock = true;

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

        _lastShotTime = Time.time;
        _lastShotCooldown = dynamicCooldown;

        yield return new WaitForSeconds(dynamicCooldown);

        _isShooting = false;
        _canShoot = true;
        _masterActionLock = false;
    }

    private IEnumerator ReloadRoutine()
    {
        _isReloading = true;
        _canShoot = false;
        _masterActionLock = true;

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

        _reloadStartTime = Time.time;
        _reloadTotalTime = currentReloadTime;

        yield return new WaitForSeconds(currentReloadTime);

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
        direction.Normalize();

        bool isBullBullet = currentBullet == BulletType.Bull;

        RaycastHit[] hits = Physics.RaycastAll(fpsCam.transform.position, direction, _activeStats.range, impactLayer);
        if (hits.Length == 0) return;
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        if (currentBullet == BulletType.Eagle)
            ProcessPiercingHits(hits, isBullBullet);
        else
            ProcessStoppingHit(hits, isBullBullet);
    }

    // Wolf/Bull: para en el primer hit (headshot, body, o pared). Solo procesa ese hit.
    private void ProcessStoppingHit(RaycastHit[] hits, bool isBullBullet)
    {
        foreach (var hit in hits)
        {
            // Headshot hit - process only this
            var hs = hit.collider.GetComponent<HeadshotCollider>();
            if (hs != null && hs.Target != null)
            {
                if (_activeStats.impactEffect)
                    Instantiate(_activeStats.impactEffect, hit.point, Quaternion.LookRotation(hit.normal));
                hs.ApplyDamage(_activeStats.damage, isBullBullet);
                return;
            }

            // Enemy body hit - process only this
            if (hit.collider.CompareTag("Enemy"))
            {
                var health = hit.collider.GetComponent<EnemyHealth>() ?? hit.collider.GetComponentInParent<EnemyHealth>();
                if (health != null)
                {
                    if (_activeStats.impactEffect)
                        Instantiate(_activeStats.impactEffect, hit.point, Quaternion.LookRotation(hit.normal));
                    health.TakeDamage(_activeStats.damage, isBullBullet);
                    return;
                }
            }

            // Wall hit - stop here
            if (_activeStats.impactEffect)
                Instantiate(_activeStats.impactEffect, hit.point, Quaternion.LookRotation(hit.normal));
            return;
        }
    }

    // Eagle: atraviesa enemigos, cada uno recibe daño una sola vez (headshot O body, no ambos). Para en paredes.
    private void ProcessPiercingHits(RaycastHit[] hits, bool isBullBullet)
    {
        var damagedEnemies = new HashSet<EnemyHealth>();

        foreach (var hit in hits)
        {
            // Headshot hit
            var hs = hit.collider.GetComponent<HeadshotCollider>();
            if (hs != null && hs.Target != null && !damagedEnemies.Contains(hs.Target))
            {
                damagedEnemies.Add(hs.Target);
                if (_activeStats.impactEffect)
                    Instantiate(_activeStats.impactEffect, hit.point, Quaternion.LookRotation(hit.normal));
                hs.ApplyDamage(_activeStats.damage, isBullBullet);
                continue;
            }

            // Enemy body hit
            if (hit.collider.CompareTag("Enemy"))
            {
                var health = hit.collider.GetComponent<EnemyHealth>() ?? hit.collider.GetComponentInParent<EnemyHealth>();
                if (health != null && !damagedEnemies.Contains(health))
                {
                    damagedEnemies.Add(health);
                    if (_activeStats.impactEffect)
                        Instantiate(_activeStats.impactEffect, hit.point, Quaternion.LookRotation(hit.normal));
                    health.TakeDamage(_activeStats.damage, isBullBullet);
                }
                continue;
            }

            // Wall hit - stop
            if (_activeStats.impactEffect)
                Instantiate(_activeStats.impactEffect, hit.point, Quaternion.LookRotation(hit.normal));
            return;
        }
    }

    // Swap de bala: dispara animación Inspect y aplica el cambio al terminar
    private IEnumerator SwapViaInspectRoutine(BulletType targetType)
    {
        _canSwap = false;
        _canShoot = false;
        _canReload = false;
        _masterActionLock = true;

        float waitTime = inspectTime;

        if (anim != null && anim.isActiveAndEnabled)
        {
            anim.speed = globalAnimationSpeedMultiplier;
            anim.ResetTrigger("Inspect");
            anim.SetTrigger("Inspect");
            anim.Update(0f);

            AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.length > 0.05f)
                waitTime = stateInfo.length / Mathf.Max(0.01f, globalAnimationSpeedMultiplier);
        }

        yield return new WaitForSeconds(waitTime);

        currentBullet = targetType;
        UpdateActiveStats();
        AudioManager.Instance?.PlayUI(swapSfxId);

        _canSwap = true;
        _canShoot = true;
        _canReload = true;
        _masterActionLock = false;
    }

    private IEnumerator SwapCooldownRoutine()
    {
        _canSwap = false;
        yield return new WaitForSeconds(weaponSwapCooldown);
        _canSwap = true;
    }

    private IEnumerator InspectAndSwapRoutine()
    {
        _canSwap = false;
        _canShoot = false;
        _canReload = false;
        _masterActionLock = true;

        float waitTime = inspectTime;

        if (anim != null && anim.isActiveAndEnabled)
        {
            anim.speed = globalAnimationSpeedMultiplier;
            anim.ResetTrigger("Inspect");
            anim.SetTrigger("Inspect");
            anim.Update(0f);

            AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.length > 0.05f)
                waitTime = stateInfo.length / Mathf.Max(0.01f, globalAnimationSpeedMultiplier);
        }

        yield return new WaitForSeconds(waitTime);

        BulletType next = (BulletType)(((int)currentBullet + 1) % 3);
        currentBullet = next;
        UpdateActiveStats();
        AudioManager.Instance?.PlayUI(swapSfxId);

        _canSwap = true;
        _canShoot = true;
        _canReload = true;
        _masterActionLock = false;
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