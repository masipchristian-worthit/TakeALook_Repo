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

    // NUEVO BLOQUE: Parámetros de anulación de rendimiento del Animator
    [Header("Animation Performance Overrides")]
    [Tooltip("Multiplicador global para acelerar forzosamente las animaciones del Animator. 1 = Normal, 2 = Doble de rápido.")]
    [SerializeField] private float globalAnimationSpeedMultiplier = 1.0f;
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

    // Control para evitar spam en el bypass de hardware
    private bool _hardwareBypassPressedThisFrame = false;

    // NUEVA VARIABLE: Herramienta de diagnóstico de precisión
    private System.Diagnostics.Stopwatch _latencyStopwatch = new System.Diagnostics.Stopwatch();

    private void Awake()
    {
        _isGunDrawn = false;
        _isTransitioning = false;
        _isShooting = false;
        _isReloading = false;

        // Failsafe para evitar el bloqueo por cargador vacío
        if (wolfStats.magCapacity <= 0) { wolfStats.magCapacity = 30; wolfStats.currentMag = 30; wolfStats.reserveAmmo = 90; wolfStats.damage = 10; wolfStats.range = 100f; }
        if (bullStats.magCapacity <= 0) { bullStats.magCapacity = 30; bullStats.currentMag = 30; bullStats.reserveAmmo = 90; bullStats.damage = 15; bullStats.range = 100f; }
        if (eagleStats.magCapacity <= 0) { eagleStats.magCapacity = 30; eagleStats.currentMag = 30; eagleStats.reserveAmmo = 90; eagleStats.damage = 20; eagleStats.range = 100f; }

        // DEBUG: Balas infinitas temporales para testear
        wolfStats.currentMag = wolfStats.magCapacity;
        wolfStats.reserveAmmo = 99;

        bullStats.currentMag = bullStats.magCapacity;
        bullStats.reserveAmmo = 99;

        eagleStats.currentMag = eagleStats.magCapacity;
        eagleStats.reserveAmmo = 99;

        UpdateActiveStats();

        // Telemetría de inicialización
        Debug.Log("[GunSystem - Awake] Estado inicializado correctamente. Munición asegurada.");
    }

    private void Start()
    {
        // Obligamos a empezar con los brazos apagados de forma segura
        if (arms != null) arms.SetActive(false);

        // Inyectamos el multiplicador de velocidad si hay un animator
        if (anim != null)
        {
            anim.speed = globalAnimationSpeedMultiplier;
            Debug.Log($"[GunSystem - Start] Velocidad global del Animator configurada a: {globalAnimationSpeedMultiplier}x");
        }

        // Telemetría de arranque
        Debug.Log("[GunSystem - Start] Brazos desactivados por seguridad.");
    }

    private void Update()
    {
        if (_isGunDrawn && !_isShooting && !_isReloading && _canSwap)
        {
            HandleScrollInput();
        }

        // Bypass de Hardware Crudo (Derivación del Input System)
        if (Keyboard.current != null)
        {
            if (Keyboard.current.tabKey.wasPressedThisFrame || Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                if (!_hardwareBypassPressedThisFrame)
                {
                    _hardwareBypassPressedThisFrame = true;
                    Debug.LogWarning("[GunSystem - Hardware Bypass] Tecla TAB o 1 pulsada directamente. Evadiendo el sistema de eventos de Unity.");

                    if (!_isTransitioning)
                    {
                        if (!_isGunDrawn) StartCoroutine(DrawWeaponRoutine());
                        else StartCoroutine(SheathWeaponRoutine());
                    }
                    else
                    {
                        Debug.LogWarning("[GunSystem - Hardware Bypass] Ignorado: El arma ya está en plena transición.");
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
        _latencyStopwatch.Restart(); // Iniciamos medición de latencia
        Debug.Log($"[GunSystem - OnDrawn] Señal de Input detectada. Fase actual: {context.phase}");

        if (!context.performed) return;

        if (_isShooting || _isReloading || _isTransitioning)
        {
            Debug.LogWarning("Draw bloqueado. Ocupado en otra acción.");
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

        if (!context.performed || !_isGunDrawn || !_canShoot || _isReloading || _isTransitioning)
        {
            if (context.performed) Debug.LogWarning($"[GunSystem - OnShoot] Disparo bloqueado. Drawn:{_isGunDrawn}, CanShoot:{_canShoot}, Reloading:{_isReloading}, Transitioning:{_isTransitioning}");
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

        if (!context.performed || !_isGunDrawn || _isReloading || _isShooting || _isTransitioning) return;

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

        if (context.performed && _isGunDrawn && !_isShooting && !_isReloading && !_isTransitioning)
        {
            if (anim != null)
            {
                anim.ResetTrigger("Inspect");
                anim.SetTrigger("Inspect");
                Debug.Log("[GunSystem - OnInspect] Enviando Trigger 'Inspect' al Animator.");
            }
        }
    }
    #endregion

    #region Coroutines & Logic
    private IEnumerator DrawWeaponRoutine()
    {
        _latencyStopwatch.Restart(); // Reiniciamos para medir tiempo de la malla
        Debug.Log("[GunSystem] Iniciando DrawWeaponRoutine...");

        _isTransitioning = true;
        _isGunDrawn = true;

        if (arms != null)
        {
            arms.SetActive(true);
            Debug.Log($"[GunSystem] GameObject 'arms' encendido en {_latencyStopwatch.ElapsedMilliseconds} ms.");
        }
        else Debug.LogError("[GunSystem] REFERENCIA NULA: 'arms' no está asignado en el Inspector.");

        // Pausa obligatoria de 1 frame para que el Animator complete su secuencia de inicialización
        yield return null;

        if (anim != null)
        {
            if (anim.isActiveAndEnabled)
            {
                // Aseguramos que la velocidad es la correcta por si otro script la modificó
                anim.speed = globalAnimationSpeedMultiplier;

                anim.ResetTrigger("Draw");
                anim.SetFloat("DrawSpeed", 1f);
                anim.SetTrigger("Draw");

                // Fuerza al Animator a actualizar su estado en este exacto milisegundo.
                anim.Update(0f);

                Debug.Log($"[GunSystem] Señal enviada al Animator tras {_latencyStopwatch.ElapsedMilliseconds} ms desde inicio de corrutina. Si hay lag, es culpa del Transition Duration en el Animator.");
            }
            else
            {
                Debug.LogError("[GunSystem] ERROR CRÍTICO: El Animator está asignado, pero está desactivado o no activo en la jerarquía.");
            }
        }
        else Debug.LogError("[GunSystem] REFERENCIA NULA: 'anim' no está asignado en el Inspector.");

        yield return new WaitForSeconds(drawCooldown);

        _canShoot = true;
        _canSwap = true;
        _canReload = true;
        _isTransitioning = false;

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

        if (anim != null)
        {
            if (anim.isActiveAndEnabled)
            {
                anim.speed = globalAnimationSpeedMultiplier; // Reforzar velocidad
                anim.ResetTrigger("Draw");
                anim.SetFloat("DrawSpeed", -1f);
                anim.SetTrigger("Draw");
                Debug.Log($"[GunSystem] Parámetros de guardado enviados al Animator en {_latencyStopwatch.ElapsedMilliseconds} ms.");
            }
        }

        yield return new WaitForSeconds(drawCooldown);

        if (arms != null)
        {
            arms.SetActive(false);
            Debug.Log("[GunSystem] GameObject 'arms' apagado.");
        }

        _isGunDrawn = false;
        _isTransitioning = false;

        Debug.Log("[GunSystem] SheathWeaponRoutine finalizada con éxito.");
    }

    private IEnumerator ShootRoutine()
    {
        _isShooting = true;
        _canShoot = false;

        ExecuteRaycast();
        _activeStats.currentMag--;
        SaveActiveStats();

        if (anim != null)
        {
            if (anim.isActiveAndEnabled)
            {
                anim.speed = globalAnimationSpeedMultiplier; // Reforzar velocidad
                anim.ResetTrigger("Fire");
                anim.SetTrigger("Fire");
                Debug.Log("[GunSystem] Trigger 'Fire' enviado al Animator.");
            }
        }

        yield return new WaitForSeconds(_activeStats.shootingCooldown);

        _isShooting = false;
        _canShoot = true;
    }

    private IEnumerator ReloadRoutine()
    {
        _isReloading = true;
        _canShoot = false;

        if (anim != null)
        {
            if (anim.isActiveAndEnabled)
            {
                anim.speed = globalAnimationSpeedMultiplier; // Reforzar velocidad
                anim.ResetTrigger("Reload");
                anim.SetTrigger("Reload");
                Debug.Log("[GunSystem] Trigger 'Reload' enviado al Animator.");
            }
        }

        yield return new WaitForSeconds(reloadTime);

        int bulletsNeeded = _activeStats.magCapacity - _activeStats.currentMag;
        int bulletsToReload = Mathf.Min(bulletsNeeded, _activeStats.reserveAmmo);

        _activeStats.currentMag += bulletsToReload;
        _activeStats.reserveAmmo -= bulletsToReload;
        SaveActiveStats();

        _isReloading = false;
        _canShoot = true;
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
        Debug.Log("[DevTool] Todos los booleanos de estado han sido reiniciados a sus valores por defecto.");
    }
    #endregion
}