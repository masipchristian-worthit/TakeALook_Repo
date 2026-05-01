using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using DG.Tweening;

public class FPS_Controller : MonoBehaviour
{
    #region General Variables
    [Header("Movement & Look")]
    [SerializeField] GameObject camHolder;
    [SerializeField] float speed = 5f;
    [SerializeField] float sprintSpeed = 8f;
    [SerializeField] float crouchSpeed = 3f;
    [SerializeField] float maxForce = 1f;
    [SerializeField] float sentivity = 0.1f;

    [Header("Jump & Groundcheck")]
    [SerializeField] float jumpForce = 5f;
    [SerializeField] bool isGrounded;
    [SerializeField] Transform groundCheck;
    [SerializeField] float groundCheckRadius = 0.3f;
    [SerializeField] LayerMask groundLayer;

    [Header("Player State Bools")]
    [SerializeField] bool isSprinting;
    [SerializeField] bool IsCrouching;

    [Header("Sprint Stamina")]
    [SerializeField] float maxSprintStamina = 5f;
    [SerializeField] float staminaDepletionRate = 1f;
    [SerializeField] float staminaRechargeRate = 0.8f;
    [SerializeField] float staminaCooldownAfterEmpty = 2f;

    [Header("Crouch (físico)")]
    [SerializeField] CapsuleCollider playerCollider;
    [SerializeField] float standingColliderHeight = 2f;
    [SerializeField] float crouchingColliderHeight = 1f;
    [SerializeField] float standingCameraHeight = 1.7f;
    [SerializeField] float crouchingCameraHeight = 0.9f;
    [SerializeField] float crouchTransitionSpeed = 10f;
    [SerializeField] LayerMask ceilingLayer = ~0;
    [SerializeField] float ceilingClearance = 0.15f;

    [Header("Crouch Vignette")]
    [SerializeField] Image crouchVignette;
    [SerializeField, Range(0f, 1f)] float crouchVignetteAlpha = 0.35f;
    [SerializeField] Color crouchVignetteColor = new Color(0f, 0f, 0f, 1f);
    [SerializeField] float crouchVignetteFadeTime = 0.35f;

    [Header("Interaction")]
    [SerializeField] float interactDistance = 3f;
    [SerializeField] LayerMask interactLayer;

    [Header("Head Bob")]
    [SerializeField] float bobSpeed = 8f;
    [SerializeField] float bobSideAmount = 0.05f;
    [SerializeField] float tiltAmount = 3f;
    [SerializeField] float sprintMultiplier = 1.5f;
    [SerializeField] float crouchMultiplier = 0.5f;

    [Header("Footsteps Audio")]
    [SerializeField] string footstepSfxId = "player_step";
    [SerializeField] float footstepWalkInterval = 0.5f;
    [SerializeField] float footstepSprintInterval = 0.32f;
    [SerializeField] float footstepCrouchInterval = 0.7f;

    [Header("Component References")]
    [SerializeField] Animator anim;
    [SerializeField] PlayerInventory playerInventory;
    #endregion

    Rigidbody rb;
    Vector2 moveInput;
    Vector2 lookInput;
    float lookRotation;
    float defaultYPos;
    float defaultXPos;
    float timer;
    CodeDoor[] codeDoors;
    bool isUsingCodePanel;

    float _sprintStamina;
    bool _staminaOnCooldown;
    float _staminaCooldownTimer;
    bool _sprintHeld;

    Tween _crouchVignetteTween;
    float _colliderTargetHeight;
    float _cameraTargetHeight;
    Vector3 _colliderBaseCenter;
    float _footstepTimer;
    bool _interactRequested;

    public float SprintStaminaNormalized => _sprintStamina / Mathf.Max(0.001f, maxSprintStamina);
    public bool IsStaminaOnCooldown => _staminaOnCooldown;
    public float CurrentHeadHeight => camHolder != null ? camHolder.transform.localPosition.y : (IsCrouching ? crouchingCameraHeight : standingCameraHeight);

    private bool IsUIOpen() => UIManager.Instance != null && UIManager.Instance.IsUIPanelOpen();
    private bool IsAnyBlocker() => isUsingCodePanel || IsUIOpen();
    private bool CanSprint => !_staminaOnCooldown && _sprintStamina > 0f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        _sprintStamina = maxSprintStamina;

        if (playerCollider == null) playerCollider = GetComponent<CapsuleCollider>();
        if (playerCollider != null)
        {
            playerCollider.height = standingColliderHeight;
            _colliderBaseCenter = playerCollider.center;
        }
        _colliderTargetHeight = standingColliderHeight;
        _cameraTargetHeight = standingCameraHeight;

        if (crouchVignette != null)
        {
            Color c = crouchVignetteColor; c.a = 0f;
            crouchVignette.color = c;
            crouchVignette.raycastTarget = false;
            crouchVignette.gameObject.SetActive(false);
        }
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (camHolder != null)
        {
            Vector3 lp = camHolder.transform.localPosition;
            lp.y = standingCameraHeight;
            camHolder.transform.localPosition = lp;
        }

        defaultYPos = standingCameraHeight;
        defaultXPos = camHolder != null ? camHolder.transform.localPosition.x : 0f;
        codeDoors = FindObjectsByType<CodeDoor>(FindObjectsSortMode.None);
    }

    void Update()
    {
        isGrounded = Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundLayer);

        CheckIfUsingCodePanel();
        UpdateCrouchPhysical();
        UpdateSprintIntent();
        UpdateSprintStamina();
        UpdateFootsteps();

        if (IsAnyBlocker())
        {
            ResetHeadBob();
            _interactRequested = false;
            return;
        }

        ProcessInteractRequest();
        HeadBob();
        AnimationHandle();
    }

    private void FixedUpdate()
    {
        if (IsAnyBlocker())
        {
            if (rb != null)
            {
                Vector3 v = rb.linearVelocity;
                v.x = 0f; v.z = 0f;
                rb.linearVelocity = v;
            }
            return;
        }
        Movement();
    }

    private void LateUpdate()
    {
        if (IsAnyBlocker()) return;
        CameraLook();
    }

    void CheckIfUsingCodePanel()
    {
        isUsingCodePanel = false;
        if (codeDoors == null) return;
        for (int i = 0; i < codeDoors.Length; i++)
        {
            if (codeDoors[i] != null && codeDoors[i].IsUsingPanel())
            {
                isUsingCodePanel = true;
                break;
            }
        }
        if (isUsingCodePanel)
        {
            moveInput = Vector2.zero;
            lookInput = Vector2.zero;
            isSprinting = false;
        }
    }

    void UpdateSprintIntent()
    {
        if (IsAnyBlocker()) { isSprinting = false; return; }
        bool wantsSprint = _sprintHeld && !IsCrouching && moveInput.sqrMagnitude > 0.01f;
        isSprinting = wantsSprint && CanSprint;
    }

    void UpdateSprintStamina()
    {
        bool activelySprinting = isSprinting && moveInput.magnitude > 0.1f && isGrounded;
        if (_staminaOnCooldown)
        {
            _staminaCooldownTimer -= Time.deltaTime;
            if (_staminaCooldownTimer <= 0f) _staminaOnCooldown = false;
            isSprinting = false;
            return;
        }
        if (activelySprinting)
        {
            _sprintStamina -= staminaDepletionRate * Time.deltaTime;
            if (_sprintStamina <= 0f)
            {
                _sprintStamina = 0f;
                isSprinting = false;
                _staminaOnCooldown = true;
                _staminaCooldownTimer = staminaCooldownAfterEmpty;
            }
        }
        else
        {
            _sprintStamina = Mathf.Min(maxSprintStamina, _sprintStamina + staminaRechargeRate * Time.deltaTime);
        }
    }

    void UpdateFootsteps()
    {
        bool moving = moveInput.sqrMagnitude > 0.04f && isGrounded && !IsAnyBlocker();
        if (!moving) { _footstepTimer = 0f; return; }

        float interval = footstepWalkInterval;
        if (isSprinting) interval = footstepSprintInterval;
        else if (IsCrouching) interval = footstepCrouchInterval;

        _footstepTimer += Time.deltaTime;
        if (_footstepTimer >= interval)
        {
            _footstepTimer = 0f;
            AudioManager.Instance?.PlaySFX(footstepSfxId, transform.position);
        }
    }

    void UpdateCrouchPhysical()
    {
        if (playerCollider != null && Mathf.Abs(playerCollider.height - _colliderTargetHeight) > 0.001f)
        {
            float newH = Mathf.MoveTowards(playerCollider.height, _colliderTargetHeight, crouchTransitionSpeed * Time.deltaTime);
            playerCollider.height = newH;
            Vector3 c = _colliderBaseCenter;
            c.y -= (standingColliderHeight - newH) * 0.5f;
            playerCollider.center = c;
        }
        if (camHolder != null && Mathf.Abs(camHolder.transform.localPosition.y - _cameraTargetHeight) > 0.001f)
        {
            Vector3 lp = camHolder.transform.localPosition;
            lp.y = Mathf.MoveTowards(lp.y, _cameraTargetHeight, crouchTransitionSpeed * Time.deltaTime);
            camHolder.transform.localPosition = lp;
            defaultYPos = lp.y;
        }
    }

    bool CanStandUp()
    {
        if (playerCollider == null) return true;
        float radius = Mathf.Max(0.05f, playerCollider.radius * 0.95f);
        Vector3 origin = transform.position + Vector3.up * (crouchingColliderHeight - radius + 0.02f);
        float castDist = (standingColliderHeight - crouchingColliderHeight) + ceilingClearance;
        return !Physics.SphereCast(origin, radius, Vector3.up, out _, castDist, ceilingLayer, QueryTriggerInteraction.Ignore);
    }

    void CameraLook()
    {
        transform.Rotate(Vector3.up * lookInput.x * sentivity);
        lookRotation += (-lookInput.y * sentivity);
        lookRotation = Mathf.Clamp(lookRotation, -90, 90);
        camHolder.transform.localEulerAngles = new Vector3(lookRotation, 0f, camHolder.transform.localEulerAngles.z);
    }

    void Movement()
    {
        Vector3 currentVelocity = rb.linearVelocity;
        Vector3 targetVelocity = new Vector3(moveInput.x, 0, moveInput.y);
        targetVelocity *= IsCrouching ? crouchSpeed : (isSprinting ? sprintSpeed : speed);
        targetVelocity = transform.TransformDirection(targetVelocity);

        Vector3 velocityChange = targetVelocity - currentVelocity;
        velocityChange.y = 0f;
        velocityChange = Vector3.ClampMagnitude(velocityChange, maxForce);

        rb.AddForce(velocityChange, ForceMode.VelocityChange);
    }

    void HeadBob()
    {
        if (moveInput.magnitude < 0.1f || !isGrounded)
        {
            ResetHeadBob();
            return;
        }

        float speedMultiplier = 1f;
        if (isSprinting) speedMultiplier = sprintMultiplier;
        if (IsCrouching) speedMultiplier = crouchMultiplier;

        timer += Time.deltaTime * bobSpeed * speedMultiplier;

        float bobY = 0f;
        float bobX = Mathf.Cos(timer * 0.5f) * bobSideAmount;

        Vector3 newPos = camHolder.transform.localPosition;
        newPos.y = defaultYPos + bobY;
        newPos.x = defaultXPos + bobX;
        camHolder.transform.localPosition = newPos;

        float tilt = Mathf.Sin(timer * 0.5f) * tiltAmount;
        camHolder.transform.localEulerAngles = new Vector3(lookRotation, 0f, tilt);
    }

    void ResetHeadBob()
    {
        timer = 0;
        Vector3 pos = camHolder.transform.localPosition;
        pos.y = Mathf.Lerp(pos.y, defaultYPos, Time.deltaTime * 5f);
        pos.x = Mathf.Lerp(pos.x, defaultXPos, Time.deltaTime * 5f);
        camHolder.transform.localPosition = pos;

        Vector3 rot = camHolder.transform.localEulerAngles;
        rot.z = Mathf.LerpAngle(rot.z, 0f, Time.deltaTime * 5f);
        camHolder.transform.localEulerAngles = new Vector3(lookRotation, 0f, rot.z);
    }

    void ProcessInteractRequest()
    {
        if (!_interactRequested) return;
        _interactRequested = false;

        if (camHolder == null) return;

        Ray ray = new Ray(camHolder.transform.position, camHolder.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactLayer)) return;

        var door = hit.collider.GetComponent<DoorOpen>();
        if (door != null) { door.OpenDoor(); return; }

        var pickable = hit.collider.GetComponent<PickableItem>();
        if (pickable == null) pickable = hit.collider.GetComponentInParent<PickableItem>();
        if (pickable != null && playerInventory != null)
        {
            pickable.TryPickup(playerInventory);
            return;
        }
    }

    void AnimationHandle()
    {
        if (anim == null || !anim.gameObject.activeInHierarchy) return;
        anim.SetBool("isWalking", moveInput.magnitude > 0.01f);
    }

    void ApplyCrouchTargets()
    {
        _colliderTargetHeight = IsCrouching ? crouchingColliderHeight : standingColliderHeight;
        _cameraTargetHeight = IsCrouching ? crouchingCameraHeight : standingCameraHeight;

        if (crouchVignette != null)
        {
            crouchVignette.gameObject.SetActive(true);
            _crouchVignetteTween?.Kill();

            float targetA = IsCrouching ? crouchVignetteAlpha : 0f;
            Color c = crouchVignetteColor;
            c.a = crouchVignette.color.a;
            crouchVignette.color = c;

            _crouchVignetteTween = crouchVignette.DOFade(targetA, crouchVignetteFadeTime)
                .SetEase(Ease.OutQuad)
                .OnComplete(() => { if (!IsCrouching) crouchVignette.gameObject.SetActive(false); })
                .SetLink(gameObject);
        }
    }

    #region Input Methods
    public void OnMove(InputAction.CallbackContext context)
    {
        if (IsAnyBlocker()) { moveInput = Vector2.zero; return; }
        moveInput = context.ReadValue<Vector2>();
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        if (IsAnyBlocker()) { lookInput = Vector2.zero; return; }
        lookInput = context.ReadValue<Vector2>();
    }

    public void OnCrouch(InputAction.CallbackContext context)
    {
        if (IsAnyBlocker()) return;
        if (!context.performed) return;

        if (IsCrouching)
        {
            if (CanStandUp()) { IsCrouching = false; ApplyCrouchTargets(); }
        }
        else
        {
            IsCrouching = true; ApplyCrouchTargets();
        }
    }

    public void OnSprint(InputAction.CallbackContext context)
    {
        if (context.performed) _sprintHeld = true;
        else if (context.canceled) _sprintHeld = false;
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (IsAnyBlocker()) return;
        if (context.performed && isGrounded && rb != null)
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
    }

    public void OnInteract(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        if (IsAnyBlocker()) return;
        _interactRequested = true;
    }

    public void OnToggleUI(InputAction.CallbackContext context)
    {
        if (context.performed) { /* Lógica de ToggleUI */ }
    }

    public void OnUISliderLeft(InputAction.CallbackContext context)
    {
        if (context.performed) { /* Lógica UISliderLeft */ }
    }

    public void OnUISliderRight(InputAction.CallbackContext context)
    {
        if (context.performed) { /* Lógica UISliderRight */ }
    }

    public void OnUIInteract(InputAction.CallbackContext context)
    {
        if (context.performed) { /* Lógica UIInteract */ }
    }
    #endregion
}