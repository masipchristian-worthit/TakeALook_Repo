using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using DG.Tweening; // REQUISITO ABSOLUTO: Asegúrate de tener DOTween importado y configurado en tu proyecto.

public class GunSystem : MonoBehaviour
{
    public enum BulletType { Wolf, Bull, Eagle }

    #region Serialized Fields
    [Header("Weapon State")]
    [SerializeField] private BulletType currentBullet = BulletType.Wolf;
    [SerializeField] private float weaponSwapCooldown = 0.5f;

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

    // Parámetros de anulación de rendimiento del Animator
    [Header("Animation Performance Overrides")]
    [Tooltip("Multiplicador global para acelerar forzosamente las animaciones del Animator. 1 = Normal, 2 = Doble de rápido.")]
    [SerializeField] private float globalAnimationSpeedMultiplier = 1.0f;

    // Reversión controlada del disparo rápido
    [Tooltip("Si es verdadero, permite disparar más rápido que la animación, interrumpiéndola. Si es falso, bloquea el disparo hasta que termine la animación visual.")]
    [SerializeField] private bool allowRapidFireOverlap = true;

    // NUEVO SUBSISTEMA: Control Fotométrico (Linterna)
    [Header("Flashlight System")]
    [SerializeField] private Light flashlight;
    [SerializeField] private float maxFlashlightIntensity = 5f;
    [SerializeField] private float flashlightTransitionTime = 0.8f;
    [Range(0f, 1f)]
    [Tooltip("Control paramétrico del ruido visual: 0 = Transición suave, 1 = Parpadeo caótico severo.")]
    [SerializeField] private float flashlightFlickerAmount = 0.5f;
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

    private bool _hardwareBypassPressedThisFrame = false;
    private System.Diagnostics.Stopwatch _latencyStopwatch = new System.Diagnostics.Stopwatch();
    private bool _masterActionLock = false;

    // Variables internas del subsistema de linterna
    private bool _isFlashlightOn = false;
    private Tween _flashlightTween;

    private void Awake()
    {
        _isGunDrawn = false;
        _isTransitioning = false;
        _isShooting = false;
        _isReloading = false;
        _masterActionLock = false;
        _isFlashlightOn = false; // Estado base inerte

        if (wolfStats.magCapacity <= 0) { wolfStats.magCapacity = 30; wolfStats.currentMag = 30; wolfStats.reserveAmmo = 90; wolfStats.damage = 10; wolfStats.range = 100f; }
        if (bullStats.magCapacity <= 0) { bullStats.magCapacity = 30; bullStats.currentMag = 30; bullStats.reserveAmmo = 90; bullStats.damage = 15; bullStats.range = 100f; }
        if (eagleStats.magCapacity <= 0) { eagleStats.magCapacity = 30; eagleStats.currentMag = 30; eagleStats.reserveAmmo = 90; eagleStats.damage = 20; eagleStats.range = 100f; }

        wolfStats.currentMag = wolfStats.magCapacity;
        wolfStats.reserveAmmo = 99;
        bullStats.currentMag = bullStats.magCapacity;
        bullStats.reserveAmmo = 99;
        eagleStats.currentMag = eagleStats.magCapacity;
        eagleStats.reserveAmmo = 99;

        UpdateActiveStats();

        // Seguro para la linterna al inicio
        if (flashlight != null)
        {
            flashlight.intensity = 0f;
            flashlight.gameObject.SetActive(false);
        }

        Debug.Log("[GunSystem - Awake] Estado inicializado correctamente. Munición asegurada.");
    }

    private void Start()
    {
        if (arms != null) arms.SetActive(false);

        if (anim != null)
        {
            anim.speed = globalAnimationSpeedMultiplier;
            Debug.Log($"[GunSystem - Start] Velocidad global del Animator configurada a: {globalAnimationSpeedMultiplier}x");
        }

        Debug.Log("[GunSystem - Start] Brazos desactivados por seguridad.");
    }

    private void Update()
    {
        if (_isGunDrawn && !_isShooting && !_isReloading && _canSwap && !_masterActionLock)
        {
            HandleScrollInput();
        }

        if (Keyboard.current != null)
        {
            if (Keyboard.current.tabKey.wasPressedThisFrame || Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                if (!_hardwareBypassPressedThisFrame)
                {
                    _hardwareBypassPressedThisFrame = true;
                    Debug.LogWarning("[GunSystem - Hardware Bypass] Tecla TAB o 1 pulsada directamente. Evadiendo el sistema de eventos de Unity.");

                    if (!_isTransitioning && !_isShooting && !_isReloading && !_masterActionLock)
                    {
                        if (!_isGunDrawn) StartCoroutine(DrawWeaponRoutine());
                        else StartCoroutine(SheathWeaponRoutine());
                    }
                    else
                    {
                        Debug.LogWarning($"[GunSystem - Hardware Bypass] ACCIÓN DENEGADA. Razones -> Transitioning: {_isTransitioning} | Shooting: {_isShooting} | Reloading: {_isReloading} | MasterLock: {_masterActionLock}");
                    }
                }
            }
            else
            {
                _hardwareBypassPressedThisFrame = false;
            }
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

    #region Input Methods
    public void OnDrawn(InputAction.CallbackContext context)
    {
        _latencyStopwatch.Restart();
        Debug.Log($"[GunSystem - OnDrawn] Señal de Input detectada. Fase actual: {context.phase}");

        if (!context.performed) return;

        if (_isShooting || _isReloading || _isTransitioning || _masterActionLock)
        {
            Debug.LogWarning($"[GunSystem - OnDrawn] Draw/Sheath bloqueado por seguridad. Ocupado en otra acción. (Lock: {_masterActionLock})");
            return;
        }

        Debug.Log($"[GunSystem - OnDrawn] Cláusulas de seguridad superadas en {_latencyStopwatch.ElapsedMilliseconds} ms. Ejecutando corrutina.");

        if (!_isGunDrawn) StartCoroutine(DrawWeaponRoutine());
        else StartCoroutine(SheathWeaponRoutine());
    }

    public void OnShoot(InputAction.CallbackContext context)
    {
        _latencyStopwatch.Restart();
        Debug.Log($"[GunSystem - OnShoot] Señal detectada. Fase: {context.phase}");

        if (!context.performed || !_isGunDrawn || !_canShoot || _isReloading || _isTransitioning || _masterActionLock)
        {
            if (context.performed) Debug.LogWarning($"[GunSystem - OnShoot] Disparo bloqueado. Drawn:{_isGunDrawn}, CanShoot:{_canShoot}, Reloading:{_isReloading}, Transitioning:{_isTransitioning}, Lock:{_masterActionLock}");
            return;
        }

        if (_activeStats.currentMag > 0)
        {
            Debug.Log($"[GunSystem - OnShoot] Latencia de validación de disparo: {_latencyStopwatch.ElapsedMilliseconds} ms.");
            StartCoroutine(ShootRoutine());
        }
        else
        {
            Debug.LogWarning($"[GunSystem - OnShoot] Intento de disparo fallido: Cargador vacío. Tipo actual: {currentBullet}");
        }
    }

    public void OnReload(InputAction.CallbackContext context)
    {
        Debug.Log($"[GunSystem - OnReload] Señal detectada. Fase: {context.phase}");

        if (!context.performed || !_isGunDrawn || _isReloading || _isShooting || _isTransitioning || _masterActionLock) return;

        bool needsReload = _activeStats.currentMag < _activeStats.magCapacity;
        bool hasReserveAmmo = _activeStats.reserveAmmo > 0;

        if (needsReload && hasReserveAmmo)
        {
            StartCoroutine(ReloadRoutine());
        }
    }

    public void OnInspect(InputAction.CallbackContext context)
    {
        Debug.Log($"[GunSystem - OnInspect] Señal detectada. Fase: {context.phase}");

        if (context.performed && _isGunDrawn && !_isShooting && !_isReloading && !_isTransitioning && !_masterActionLock)
        {
            if (anim != null)
            {
                anim.ResetTrigger("Inspect");
                anim.SetTrigger("Inspect");
                Debug.Log("[GunSystem - OnInspect] Enviando Trigger 'Inspect' al Animator.");
            }
        }
    }

    // NUEVO MÉTODO: Receptor del Player Input para la linterna
    public void OnFlashlight(InputAction.CallbackContext context)
    {
        if (!context.performed) return;

        if (flashlight == null)
        {
            Debug.LogWarning("[GunSystem - Flashlight] REFERENCIA NULA: No hay luz asignada en el inspector para ejecutar la acción.");
            return;
        }

        _isFlashlightOn = !_isFlashlightOn;
        Debug.Log($"[GunSystem - Flashlight] Señal detectada. Estado objetivo: {_isFlashlightOn}. Ejecutando interpolación paramétrica.");

        // Anulamos de forma segura cualquier interpolación activa para evitar conflictos de sobre-escritura
        _flashlightTween?.Kill();

        if (_isFlashlightOn)
        {
            flashlight.gameObject.SetActive(true);

            // Interpolación virtual: Calculamos el valor y lo aplicamos manualmente para inyectarle el ruido (flicker)
            _flashlightTween = DOVirtual.Float(flashlight.intensity, maxFlashlightIntensity, flashlightTransitionTime, (val) =>
            {
                if (flashlightFlickerAmount > 0f)
                {
                    // La probabilidad de que haya un micro-corte se basa en el slider
                    bool shouldFlicker = Random.value < (flashlightFlickerAmount * 0.4f);
                    if (shouldFlicker)
                    {
                        // Ruido matemático: cae la intensidad de golpe
                        flashlight.intensity = val * Random.Range(0f, 0.3f);
                    }
                    else
                    {
                        flashlight.intensity = val;
                    }
                }
                else
                {
                    flashlight.intensity = val;
                }
            })
            .SetEase(Ease.OutQuad) // Curva de aceleración inicial
            .OnComplete(() =>
            {
                flashlight.intensity = maxFlashlightIntensity; // Estabilización final garantizada
                Debug.Log("[GunSystem - Flashlight] Ciclo de encendido finalizado.");
            });
        }
        else
        {
            _flashlightTween = DOVirtual.Float(flashlight.intensity, 0f, flashlightTransitionTime, (val) =>
            {
                if (flashlightFlickerAmount > 0f)
                {
                    bool shouldSpike = Random.value < (flashlightFlickerAmount * 0.4f);
                    if (shouldSpike)
                    {
                        // Ruido inverso: pega picos de luz antes de apagarse del todo
                        flashlight.intensity = val + Random.Range(0f, maxFlashlightIntensity * 0.6f);
                    }
                    else
                    {
                        flashlight.intensity = val;
                    }
                }
                else
                {
                    flashlight.intensity = val;
                }
            })
            .SetEase(Ease.InQuad) // Curva de deceleración final
            .OnComplete(() =>
            {
                flashlight.intensity = 0f;
                flashlight.gameObject.SetActive(false); // Apagado físico del GameObject
                Debug.Log("[GunSystem - Flashlight] Ciclo de apagado finalizado.");
            });
        }
    }
    #endregion

    #region Coroutines & Logic
    private IEnumerator DrawWeaponRoutine()
    {
        _latencyStopwatch.Restart();
        Debug.Log("[GunSystem] Iniciando DrawWeaponRoutine...");

        _isTransitioning = true;
        _isGunDrawn = true;
        _masterActionLock = true;

        if (arms != null)
        {
            arms.SetActive(true);
            Debug.Log($"[GunSystem] GameObject 'arms' encendido en {_latencyStopwatch.ElapsedMilliseconds} ms.");
        }
        else Debug.LogError("[GunSystem] REFERENCIA NULA: 'arms' no está asignado en el Inspector.");

        yield return null;

        if (anim != null)
        {
            if (anim.isActiveAndEnabled)
            {
                anim.speed = globalAnimationSpeedMultiplier;

                anim.ResetTrigger("Draw");
                anim.SetFloat("DrawSpeed", 1f);
                anim.SetTrigger("Draw");

                Debug.Log($"[GunSystem] Señal enviada al Animator tras {_latencyStopwatch.ElapsedMilliseconds} ms. Sacando arma de forma fluida.");
            }
            else
            {
                Debug.LogError("[GunSystem] ERROR CRÍTICO: El Animator está asignado, pero está desactivado o no activo en la jerarquía.");
            }
        }
        else Debug.LogError("[GunSystem] REFERENCIA NULA: 'anim' no está asignado en el Inspector.");

        float actualDrawWait = Mathf.Max(drawCooldown, 0.1f);
        yield return new WaitForSeconds(actualDrawWait);

        // NUEVAS LÍNEAS DE RESOLUCIÓN: El Cierre de Ciclo para el Draw Positivo.
        // Liberamos al Animator de la espera para que retorne a Walk/Idle inmediatamente.
        if (anim != null && anim.isActiveAndEnabled)
        {
            anim.ResetTrigger("SheathComplete");
            anim.SetTrigger("SheathComplete");
            Debug.Log("[GunSystem] Trigger 'SheathComplete' inyectado en desenfundado positivo. Animator liberado para transición a Idle.");
        }

        _canShoot = true;
        _canSwap = true;
        _canReload = true;
        _isTransitioning = false;
        _masterActionLock = false;

        Debug.Log("[GunSystem] DrawWeaponRoutine finalizada con éxito.");
    }

    private IEnumerator SheathWeaponRoutine()
    {
        _latencyStopwatch.Restart();
        Debug.Log("[GunSystem] Iniciando SheathWeaponRoutine...");

        _isTransitioning = true;
        _canShoot = false;
        _canSwap = false;
        _canReload = false;
        _masterActionLock = true;

        float actualSheathWait = drawCooldown;

        if (anim != null)
        {
            if (anim.isActiveAndEnabled)
            {
                anim.speed = globalAnimationSpeedMultiplier;
                anim.ResetTrigger("Draw");
                anim.SetFloat("DrawSpeed", -1f);
                anim.SetTrigger("Draw");

                yield return null;

                AnimatorStateInfo currentState = anim.GetCurrentAnimatorStateInfo(0);
                AnimatorStateInfo nextState = anim.GetNextAnimatorStateInfo(0);

                int targetStateHash = nextState.fullPathHash != 0 ? nextState.fullPathHash : currentState.fullPathHash;
                float stateLength = nextState.fullPathHash != 0 ? nextState.length : currentState.length;

                anim.Play(targetStateHash, 0, 1.0f);

                if (stateLength > 0.05f)
                {
                    actualSheathWait = stateLength / (globalAnimationSpeedMultiplier > 0 ? globalAnimationSpeedMultiplier : 1f);
                    Debug.Log($"[GunSystem - SheathLock] Tiempo ajustado dinámicamente a {actualSheathWait}s exactos para cuadrar con el final de la animación visual.");
                }
            }
        }

        yield return new WaitForSeconds(actualSheathWait);

        if (anim != null && anim.isActiveAndEnabled)
        {
            anim.ResetTrigger("SheathComplete");
            anim.SetTrigger("SheathComplete");
            Debug.Log("[GunSystem] Trigger 'SheathComplete' inyectado en ciclo inverso. Autorizando retorno a Idle.");
        }

        if (arms != null)
        {
            arms.SetActive(false);
            Debug.Log("[GunSystem] GameObject 'arms' apagado tras completar el ciclo inverso.");
        }

        _isGunDrawn = false;
        _isTransitioning = false;
        _masterActionLock = false;

        Debug.Log("[GunSystem] SheathWeaponRoutine finalizada con éxito.");
    }

    private IEnumerator ShootRoutine()
    {
        _isShooting = true;
        _canShoot = false;
        _masterActionLock = true;

        ExecuteRaycast();
        _activeStats.currentMag--;
        SaveActiveStats();

        float dynamicCooldown = _activeStats.shootingCooldown;

        if (anim != null)
        {
            if (anim.isActiveAndEnabled)
            {
                anim.speed = globalAnimationSpeedMultiplier;
                anim.ResetTrigger("Fire");
                anim.SetTrigger("Fire");
                anim.Update(0f);
                Debug.Log("[GunSystem] Trigger 'Fire' enviado al Animator.");

                if (!allowRapidFireOverlap)
                {
                    AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
                    if (stateInfo.length > _activeStats.shootingCooldown)
                    {
                        dynamicCooldown = stateInfo.length / (globalAnimationSpeedMultiplier > 0 ? globalAnimationSpeedMultiplier : 1f);
                        Debug.Log($"[GunSystem - ShootLock] Cooldown ajustado a {dynamicCooldown}s. (Rapid Fire desactivado).");
                    }
                }
                else
                {
                    Debug.Log($"[GunSystem - ShootLock] Rapid Fire Activo: Priorizando el cooldown lógico de {_activeStats.shootingCooldown}s sobre la longitud visual.");
                }
            }
        }

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

        if (anim != null)
        {
            if (anim.isActiveAndEnabled)
            {
                anim.speed = globalAnimationSpeedMultiplier;
                anim.ResetTrigger("Reload");
                anim.SetTrigger("Reload");
                anim.Update(0f);
                Debug.Log("[GunSystem] Trigger 'Reload' enviado al Animator.");
            }
        }

        float currentReloadTime = reloadTime;
        if (anim != null && anim.isActiveAndEnabled)
        {
            AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.length > reloadTime) currentReloadTime = stateInfo.length / (globalAnimationSpeedMultiplier > 0 ? globalAnimationSpeedMultiplier : 1f);
        }

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

        Debug.Log($"[GunSystem - Raycast] Iniciando cálculo balístico. Munición: {currentBullet} | Daño: {_activeStats.damage} | Rango: {_activeStats.range}");

        if (Physics.Raycast(fpsCam.transform.position, direction, out _hit, _activeStats.range, impactLayer))
        {
            Debug.Log($"[GunSystem - Raycast] IMPACTO REGISTRADO. Objeto: '{_hit.collider.gameObject.name}' | Capa: {LayerMask.LayerToName(_hit.collider.gameObject.layer)}");

            if (_activeStats.impactEffect)
            {
                Instantiate(_activeStats.impactEffect, _hit.point, Quaternion.LookRotation(_hit.normal));
                Debug.Log("[GunSystem - Raycast] Instanciado VFX de impacto en las coordenadas de la colisión.");
            }

            if (_hit.collider.CompareTag("Enemy"))
            {
                var health = _hit.collider.GetComponent<EnemyHealth>();
                if (health != null)
                {
                    health.TakeDamage(_activeStats.damage);
                    Debug.Log($"[GunSystem - Raycast] ÉXITO OFENSIVO. Daño ({_activeStats.damage}) aplicado al componente EnemyHealth del objetivo.");
                }
                else
                {
                    Debug.LogWarning($"[GunSystem - Raycast] ANOMALÍA: El objeto '{_hit.collider.gameObject.name}' tiene la etiqueta 'Enemy' pero carece del script 'EnemyHealth'.");
                }
            }
            else
            {
                Debug.Log($"[GunSystem - Raycast] OBJETIVO INERTE. El objeto no tiene la etiqueta 'Enemy' (Etiqueta actual: {_hit.collider.tag}). No se aplica daño lógico.");
            }
        }
        else
        {
            Debug.LogWarning($"[GunSystem - Raycast] FALLO DE IMPACTO: El vector de trayectoria no colisionó con ningún objeto dentro del 'impactLayer' o la distancia máxima ({_activeStats.range}) fue excedida.");
        }
    }

    private IEnumerator SwapCooldownRoutine()
    {
        _canSwap = false;
        yield return new WaitForSeconds(weaponSwapCooldown);
        _canSwap = true;
    }
    #endregion

    #region Herramientas de Inyección Manual (Debug)
    [ContextMenu("Debug: Forzar Acción Draw/Sheath")]
    private void DebugForceDraw()
    {
        Debug.Log("[DevTool] Inyectando ejecución de Draw manualmente ignorando el Input.");
        if (!_isGunDrawn) StartCoroutine(DrawWeaponRoutine());
        else StartCoroutine(SheathWeaponRoutine());
    }

    [ContextMenu("Debug: Resetear Estados de Bloqueo")]
    private void DebugResetStates()
    {
        _isShooting = false;
        _isReloading = false;
        _isTransitioning = false;
        _canShoot = true;
        _canSwap = true;
        _canReload = true;
        _masterActionLock = false;
        Debug.Log("[DevTool] Todos los booleanos de estado han sido reiniciados a sus valores por defecto.");
    }
    #endregion
}