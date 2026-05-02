using System;
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
    [Tooltip("VFX que se reproduce en cada disparo. Puede ser un ParticleSystem o un GameObject con VFX hijos.")]
    [SerializeField] private GameObject shootVFX;
    [Tooltip("VFX_BloodSplatter — se instancia en el punto de impacto cuando el raycast golpea a un enemigo (cuerpo o cabeza). Debe llevar BloodCollisionHandler para que las partículas spawn'een charcos al tocar el suelo.")]
    [SerializeField] private GameObject bloodSplatterPrefab;
    [Tooltip("Distancia de offset desde el punto de impacto hacia la dirección del disparador (entre el collider y la malla, para que el VFX no se incruste dentro del enemigo).")]
    [SerializeField, Range(0f, 0.2f)] private float bloodSplatterSurfaceOffset = 0.04f;

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

    [Header("Audio IDs - Weapon Transitions")]
    [SerializeField] private string drawSfxId   = "gun_draw";
    [SerializeField] private string sheathSfxId = "gun_sheath";
    [SerializeField] private string reloadSfxId = "gun_reload";
    [SerializeField] private string inspectSfxId= "gun_inspect";
    [Tooltip("SFX al pulsar disparo con el cargador a 0 (dryfire).")]
    [SerializeField] private string emptySfxId  = "gun_empty";
    [Tooltip("SFX al pulsar recargar sin munición de reserva (intento fallido).")]
    [SerializeField] private string emptyReloadSfxId = "gun_empty_reload";
    [SerializeField] private string swapSfxId   = "gun_swap";

    [Header("Audio IDs - Shoot (per bullet type)")]
    [SerializeField] private string shootSfxWolfId  = "gun_shoot_wolf";
    [SerializeField] private string shootSfxBullId  = "gun_shoot_bull";
    [SerializeField] private string shootSfxEagleId = "gun_shoot_eagle";

    [Header("Audio IDs - Flashlight")]
    [SerializeField] private string flashlightOnId  = "flashlight_on";
    [SerializeField] private string flashlightOffId = "flashlight_off";

    [Header("Input Buffer")]
    [Tooltip("Ventana (segundos) durante la cual una acción pulsada mientras otra estaba en curso se ejecutará al terminar la actual.")]
    [SerializeField] private float inputBufferWindow = 0.18f;

    [Header("Draw/Sheath Cooldown (anti-bug)")]
    [Tooltip("Tiempo mínimo (segundos) entre dos pulsaciones consecutivas de sacar/guardar arma. Evita desincronización del Animator si se spamea la tecla.")]
    [SerializeField] private float drawSheathLockoutTime = 0.45f;
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
    private bool _masterActionLock = false;

    private float _lastShotTime = -999f;
    private float _lastShotCooldown = 1f;
    private float _reloadStartTime = -999f;
    private float _reloadTotalTime = 1f;

    public float ShootCooldownNormalized =>
        1f - Mathf.Clamp01((Time.time - _lastShotTime) / Mathf.Max(0.001f, _lastShotCooldown));

    public bool IsReloading => _isReloading;
    public bool IsShooting => _isShooting;
    public float ShootCooldownProgress => ShootCooldownNormalized;
    public float ReloadProgress =>
        Mathf.Clamp01((Time.time - _reloadStartTime) / Mathf.Max(0.001f, _reloadTotalTime));

    private Renderer[] _armsRenderers;

    private readonly Queue<int> _scrollBuffer = new Queue<int>();
    private float _nextSwapAllowedTime;
    private float _bufferLastEnqueueTime;

    // Buffer genérico de acción: si pulsas Disparo/Recargar/Inspeccionar/Sacar mientras
    // otra acción aún está en curso, se encola y se ejecuta al terminar la actual
    // (siempre que esté dentro de la ventana inputBufferWindow). Evita que se "trague"
    // los inputs en cadena.
    private enum BufferedAction { None, Shoot, Reload, Inspect, DrawToggle }
    private BufferedAction _bufferedAction = BufferedAction.None;
    private float _bufferedActionTime = -999f;

    // Cooldown estricto del par draw/sheath. Impide que se intercale otra transición
    // mientras la última no haya pasado el lockout. Refuerza _isTransitioning con un
    // candado temporal independiente del coroutine.
    private float _drawSheathLockoutUntil = -999f;

    private bool _isFlashlightOn = false;
    private bool _hasFlashlight = false;
    private Tween _flashlightTween;
    public bool HasFlashlight { get => _hasFlashlight; set => _hasFlashlight = value; }

    public BulletType CurrentBulletType => currentBullet;
    public int GetMag(BulletType type) => GetStatsRef(type).currentMag;
    public int GetReserve(BulletType type) => GetStatsRef(type).reserveAmmo;
    public int GetMagCapacity(BulletType type) => GetStatsRef(type).magCapacity;

    public bool IsCurrentMagEmpty => _activeStats.currentMag <= 0;

    /// <summary>
    /// Se dispara cuando el jugador INTENTA disparar pero el cargador está vacío.
    /// El EnemyAI lo escucha para iniciar el rush.
    /// </summary>
    public event Action OnDryFireAttempt;

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

    private string GetShootSfxId(BulletType type)
    {
        switch (type)
        {
            case BulletType.Wolf: return shootSfxWolfId;
            case BulletType.Bull: return shootSfxBullId;
            case BulletType.Eagle: return shootSfxEagleId;
        }
        return shootSfxWolfId;
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

        // El VFX de disparo arranca DESACTIVADO; se activa puntualmente en cada disparo.
        if (shootVFX != null) shootVFX.SetActive(false);
    }

    private void Start()
    {
        // El GameObject 'arms' se mantiene SIEMPRE activo para que el Animator no se reinicie
        // al sacar/guardar el arma (raíz del antiguo parpadeo). Solo toggleamos los renderers.
        if (arms != null)
        {
            arms.SetActive(true);
            SetArmsRenderersEnabled(false);
        }
        if (anim != null) anim.speed = globalAnimationSpeedMultiplier;
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
    private void Update()
    {
        ProcessScrollInput();
        ProcessScrollBuffer();
        ProcessActionBuffer();
    }

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
        if (IsInputBlocked() || _isTransitioning || _isReloading || _masterActionLock) return;
        int dir = _scrollBuffer.Dequeue();
        BulletType next = (BulletType)(((int)currentBullet + dir + 3) % 3);
        if (next == currentBullet) return;
        _nextSwapAllowedTime = Time.time + weaponSwapCooldown + inspectTime;
        StartCoroutine(SwapViaInspectRoutine(next));
    }

    /// <summary>
    /// Encola una acción para que se ejecute cuando la transición/animación actual
    /// termine. La cola tiene una sola posición — la acción más reciente sobreescribe
    /// la anterior, igual que en los shooter modernos.
    /// </summary>
    private void BufferAction(BufferedAction action)
    {
        _bufferedAction = action;
        _bufferedActionTime = Time.time;
    }

    private void ProcessActionBuffer()
    {
        if (_bufferedAction == BufferedAction.None) return;

        // Caduca si hace demasiado tiempo del input
        if (Time.time - _bufferedActionTime > inputBufferWindow)
        {
            _bufferedAction = BufferedAction.None;
            return;
        }

        // Si la UI está abierta, descartamos el buffer entero (RE4: no podemos actuar
        // sobre el arma con la UI desplegada).
        if (IsInputBlocked())
        {
            _bufferedAction = BufferedAction.None;
            return;
        }

        // Esperamos a que se libere todo
        if (_isTransitioning || _isShooting || _isReloading || _masterActionLock) return;

        BufferedAction toExec = _bufferedAction;
        _bufferedAction = BufferedAction.None;

        switch (toExec)
        {
            case BufferedAction.Shoot:      TryShootImmediate(); break;
            case BufferedAction.Reload:     TryReloadImmediate(); break;
            case BufferedAction.Inspect:    TryInspectImmediate(); break;
            case BufferedAction.DrawToggle: TryDrawToggleImmediate(); break;
        }
    }
    #endregion

    #region Input Methods (PlayerInput)
    public void OnDrawn(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        if (IsInputBlocked()) return;

        if (!CanStartDrawSheathNow()) { BufferAction(BufferedAction.DrawToggle); return; }
        TryDrawToggleImmediate();
    }

    public void OnShoot(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        if (IsInputBlocked()) return;

        if (!CanShootNow()) { BufferAction(BufferedAction.Shoot); return; }
        TryShootImmediate();
    }

    public void OnReload(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        if (IsInputBlocked()) return;

        if (!CanReloadNow()) { BufferAction(BufferedAction.Reload); return; }
        TryReloadImmediate();
    }

    public void OnInspect(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        if (IsInputBlocked()) return;

        if (!CanInspectNow()) { BufferAction(BufferedAction.Inspect); return; }
        TryInspectImmediate();
    }

    // ==== Helpers de validación (también consultados por el buffer) ====
    private bool CanShootNow()
    {
        if (!_isGunDrawn) return false;
        if (!_canShoot || _isReloading || _isTransitioning || _masterActionLock) return false;
        return true;
    }

    private bool CanReloadNow()
    {
        if (!_isGunDrawn) return false;
        if (_isReloading || _isShooting || _isTransitioning || _masterActionLock) return false;
        return true;
    }

    private bool CanInspectNow()
    {
        if (!_isGunDrawn) return false;
        if (_isShooting || _isReloading || _isTransitioning || _masterActionLock) return false;
        return true;
    }

    private bool CanStartDrawSheathNow()
    {
        if (Time.time < _drawSheathLockoutUntil) return false;
        if (_isShooting || _isReloading || _isTransitioning || _masterActionLock) return false;
        return true;
    }

    // ==== Ejecutores reales (también llamados por el buffer) ====
    private void TryShootImmediate()
    {
        if (!CanShootNow()) return;

        if (_activeStats.currentMag > 0)
        {
            StartCoroutine(ShootRoutine());
        }
        else
        {
            // Dryfire: sonido + evento que escuchan los enemigos
            AudioManager.Instance?.PlaySFX(emptySfxId, transform.position);
            OnDryFireAttempt?.Invoke();
        }
    }

    private void TryReloadImmediate()
    {
        if (!CanReloadNow()) return;

        UpdateActiveStats();

        bool needsReload = _activeStats.currentMag < _activeStats.magCapacity;
        bool hasReserveAmmo = _activeStats.reserveAmmo > 0;

        if (!needsReload) return; // ya está completo, ignorado

        if (!hasReserveAmmo)
        {
            // Intento de recarga sin reservas: feedback de audio dedicado.
            AudioManager.Instance?.PlaySFX(emptyReloadSfxId, transform.position);
            return;
        }

        StartCoroutine(ReloadRoutine());
    }

    private void TryInspectImmediate()
    {
        if (!CanInspectNow()) return;
        StartCoroutine(InspectAndSwapRoutine());
    }

    private void TryDrawToggleImmediate()
    {
        if (!CanStartDrawSheathNow()) return;
        // Lockout PRE-coroutine: bloquea cualquier nuevo Draw/Sheath durante el tiempo
        // mínimo configurado, aunque la coroutine en sí termine antes (defensa frente
        // al spam que dejaba desincronizado el Animator).
        _drawSheathLockoutUntil = Time.time + drawSheathLockoutTime;
        if (!_isGunDrawn) StartCoroutine(DrawWeaponRoutine());
        else StartCoroutine(SheathWeaponRoutine());
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
                if (flashlightFlickerAmount > 0f && UnityEngine.Random.value < flashlightFlickerAmount * 0.4f)
                    flashlight.intensity = val * UnityEngine.Random.Range(0f, 0.3f);
                else
                    flashlight.intensity = val;
            }).SetEase(Ease.OutQuad)
              .OnComplete(() => flashlight.intensity = maxFlashlightIntensity);
        }
        else
        {
            _flashlightTween = DOVirtual.Float(flashlight.intensity, 0f, flashlightTransitionTime, val =>
            {
                if (flashlightFlickerAmount > 0f && UnityEngine.Random.value < flashlightFlickerAmount * 0.4f)
                    flashlight.intensity = val + UnityEngine.Random.Range(0f, maxFlashlightIntensity * 0.6f);
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

        if (arms != null && !arms.activeSelf) arms.SetActive(true);
        SetArmsRenderersEnabled(false);

        if (anim != null && anim.isActiveAndEnabled)
        {
            anim.speed = globalAnimationSpeedMultiplier;
            anim.ResetTrigger("Draw");
            anim.ResetTrigger("SheathComplete");
            anim.SetFloat("DrawSpeed", 1f);
            anim.SetTrigger("Draw");
            anim.Update(0f);
        }

        // Esperar a que la transición concluya antes de mostrar la malla, para que no
        // aparezca un fotograma "pose base" enganchado.
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
        // Extiende el lockout post-coroutine: cualquier Draw/Sheath nuevo tiene que
        // esperar drawSheathLockoutTime adicional al término de esta transición.
        _drawSheathLockoutUntil = Mathf.Max(_drawSheathLockoutUntil, Time.time + drawSheathLockoutTime);
    }

    /// <summary>
    /// FIX PARPADEO INVERTIDO: cuando guardamos el arma (DrawSpeed=-1), el clip se reproduce
    /// hacia atrás. El truco es:
    ///   1. NO ocultar los renderers hasta que la animación termine.
    ///   2. Forzar al Animator a la pose final del clip Draw (normalizedTime=1) ANTES
    ///      de cambiar la velocidad a -1, evitando que la primera frame muestre la pose base.
    /// </summary>
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

            // Primero forzamos la pose de Draw al final, ANTES de invertir la velocidad,
            // para que el reverse arranque de un fotograma estable (la mano sostiene el arma).
            anim.SetFloat("DrawSpeed", 1f);
            anim.SetTrigger("Draw");
            anim.Update(0f);

            // Esperar a que termine la transición de entrada al estado Draw
            float guard = 0f;
            while (anim.IsInTransition(0) && guard < 0.2f)
            {
                guard += Time.deltaTime;
                yield return null;
            }

            // Saltar a la pose final (arma sacada) y mostrarla en pantalla
            AnimatorStateInfo currentState = anim.GetCurrentAnimatorStateInfo(0);
            int targetStateHash = currentState.fullPathHash;
            float stateLength = currentState.length;

            anim.Play(targetStateHash, 0, 1f);
            anim.Update(0f);

            // En este punto la malla muestra la pose con el arma fuera. Ahora invertimos
            // el playback para que vuelva a la cintura.
            anim.SetFloat("DrawSpeed", -1f);

            if (stateLength > 0.05f)
                actualSheathWait = stateLength / Mathf.Max(0.01f, globalAnimationSpeedMultiplier);
        }

        // La malla SIGUE visible durante todo el reverse para no parpadear.
        yield return new WaitForSeconds(actualSheathWait);

        if (anim != null && anim.isActiveAndEnabled)
        {
            anim.ResetTrigger("SheathComplete");
            anim.SetTrigger("SheathComplete");
        }

        // Solo ocultamos renderers AL FINAL, una vez la mano "ha guardado" el arma.
        SetArmsRenderersEnabled(false);

        _isGunDrawn = false;
        _isTransitioning = false;
        _masterActionLock = false;
        // Extiende el lockout post-coroutine.
        _drawSheathLockoutUntil = Mathf.Max(_drawSheathLockoutUntil, Time.time + drawSheathLockoutTime);
    }

    private IEnumerator ShootRoutine()
    {
        _isShooting = true;
        _canShoot = false;
        _masterActionLock = true;

        ExecuteRaycast();
        _activeStats.currentMag--;
        SaveActiveStats();

        // Sonido específico por tipo de bala
        AudioManager.Instance?.PlaySFX(GetShootSfxId(currentBullet), transform.position);

        // VFX de boca de fuego: instanciar un clon temporal en la posición del shootVFX original
        // para no depender de re-activación del mismo GameObject (que con ParticleSystems no se
        // reproduce siempre correctamente al re-disparar).
        SpawnShootVFX();

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

    /// <summary>
    /// Activa el VFX de boca de fuego de forma robusta. Si shootVFX es un ParticleSystem
    /// (o lo contiene), lo reinicia. Si no, instancia una copia temporal.
    /// </summary>
    private void SpawnShootVFX()
    {
        if (shootVFX == null) return;

        // Si el VFX está parented al arma, lo activamos y reproducimos sus partículas
        if (shootVFX.transform.IsChildOf(transform) || shootVFX.transform.parent != null)
        {
            shootVFX.SetActive(true);
            ParticleSystem[] all = shootVFX.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                all[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                all[i].Play(true);
            }
            // Asegurar que renderers están enabled
            Renderer[] rends = shootVFX.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
                if (rends[i] != null) rends[i].enabled = true;
        }
        else
        {
            // VFX standalone (asset): instanciar copia temporal
            GameObject clone = Instantiate(shootVFX, shootVFX.transform.position, shootVFX.transform.rotation);
            clone.SetActive(true);
            Destroy(clone, 2f);
        }
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
        direction.x += UnityEngine.Random.Range(-_activeStats.spread, _activeStats.spread);
        direction.y += UnityEngine.Random.Range(-_activeStats.spread, _activeStats.spread);
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

    private void ProcessStoppingHit(RaycastHit[] hits, bool isBullBullet)
    {
        foreach (var hit in hits)
        {
            var hs = hit.collider.GetComponent<HeadshotCollider>();
            if (hs != null && hs.Target != null)
            {
                SpawnEnemyHitVFX(hit);
                hs.ApplyDamage(_activeStats.damage, isBullBullet);
                return;
            }

            if (hit.collider.CompareTag("Enemy"))
            {
                var health = hit.collider.GetComponent<EnemyHealth>() ?? hit.collider.GetComponentInParent<EnemyHealth>();
                if (health != null)
                {
                    SpawnEnemyHitVFX(hit);
                    health.TakeDamage(_activeStats.damage, isBullBullet);
                    return;
                }
            }

            if (_activeStats.impactEffect)
                Instantiate(_activeStats.impactEffect, hit.point, Quaternion.LookRotation(hit.normal));
            return;
        }
    }

    private void ProcessPiercingHits(RaycastHit[] hits, bool isBullBullet)
    {
        var damagedEnemies = new HashSet<EnemyHealth>();

        foreach (var hit in hits)
        {
            var hs = hit.collider.GetComponent<HeadshotCollider>();
            if (hs != null && hs.Target != null && !damagedEnemies.Contains(hs.Target))
            {
                damagedEnemies.Add(hs.Target);
                SpawnEnemyHitVFX(hit);
                hs.ApplyDamage(_activeStats.damage, isBullBullet);
                continue;
            }

            if (hit.collider.CompareTag("Enemy"))
            {
                var health = hit.collider.GetComponent<EnemyHealth>() ?? hit.collider.GetComponentInParent<EnemyHealth>();
                if (health != null && !damagedEnemies.Contains(health))
                {
                    damagedEnemies.Add(health);
                    SpawnEnemyHitVFX(hit);
                    health.TakeDamage(_activeStats.damage, isBullBullet);
                }
                continue;
            }

            if (_activeStats.impactEffect)
                Instantiate(_activeStats.impactEffect, hit.point, Quaternion.LookRotation(hit.normal));
            return;
        }
    }

    /// <summary>
    /// Spawnea el VFX_BloodSplatter en la superficie del impacto (entre collider y malla)
    /// orientado contra la dirección de la bala. Mantiene también el impactEffect del arma
    /// si está asignado, para que el feedback general de impacto siga visible.
    /// </summary>
    private void SpawnEnemyHitVFX(RaycastHit hit)
    {
        Vector3 surfacePos = hit.point + hit.normal * bloodSplatterSurfaceOffset;
        Quaternion surfaceRot = Quaternion.LookRotation(hit.normal);

        if (bloodSplatterPrefab != null)
        {
            Instantiate(bloodSplatterPrefab, surfacePos, surfaceRot);
        }

        if (_activeStats.impactEffect != null)
        {
            Instantiate(_activeStats.impactEffect, hit.point, Quaternion.LookRotation(hit.normal));
        }
    }

    // Swap de bala via inspect: aprovecha la animación para sentir el cambio de cargador.
    private IEnumerator SwapViaInspectRoutine(BulletType targetType)
    {
        _canSwap = false;
        _canShoot = false;
        _canReload = false;
        _masterActionLock = true;

        float waitTime = inspectTime;

        AudioManager.Instance?.PlaySFX(inspectSfxId, transform.position);

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
        AudioManager.Instance?.PlaySFX(inspectSfxId, transform.position);

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
