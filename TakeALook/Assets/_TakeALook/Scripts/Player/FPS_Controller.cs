using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class FPS_Controller : MonoBehaviour
{
    #region Inspector
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

    [Header("Crouch Settings")]
    [SerializeField] float crouchCamOffset = -0.4f;
    [SerializeField] float crouchTransitionTime = 0.22f;
    [SerializeField] float crouchColliderHeight = 1.2f;
    [SerializeField] float crouchColliderCenterY = 0.6f;

    [Header("Player State (Read-Only in Play)")]
    [SerializeField] bool isSprinting;
    [SerializeField] bool IsCrouching;

    [Header("Interaction")]
    [SerializeField] float interactDistance = 3f;
    [SerializeField] LayerMask interactLayer;

    [Header("Head Bob")]
    [SerializeField] float bobSpeed = 8f;
    [SerializeField] float bobAmount = 0.08f;
    [SerializeField] float bobSideAmount = 0.05f;
    [SerializeField] float tiltAmount = 3f;
    [SerializeField] float sprintMultiplier = 1.5f;
    [SerializeField] float crouchMultiplier = 0.5f;

    [Header("Component References")]
    [SerializeField] Animator anim;
    [SerializeField] PlayerInventory playerInventory;
    #endregion

    // Public state (leído por EnemyAIBase)
    public bool IsCrouchingPublic => IsCrouching;
    public bool IsSprintingPublic => isSprinting;

    Rigidbody rb;
    CapsuleCollider capsule;
    float _standColliderHeight;
    float _standColliderCenterY;

    Vector2 moveInput;
    Vector2 lookInput;
    float lookRotation;

    float defaultYPos;
    float defaultXPos;
    float timer;

    // Crouch camera smooth transition
    float _crouchYOffset;
    float _targetCrouchYOffset;

    // Sprint-from-crouch state
    bool _isStandingUp;
    bool _pendingSprint;

    CodeDoor[] codeDoors;
    bool isUsingCodePanel;

    private bool IsUIOpen() => UIManager.Instance != null && UIManager.Instance.IsUIPanelOpen();
    private bool IsAnyBlocker() => isUsingCodePanel || IsUIOpen();

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        defaultYPos = camHolder.transform.localPosition.y;
        defaultXPos = camHolder.transform.localPosition.x;

        if (capsule != null)
        {
            _standColliderHeight = capsule.height;
            _standColliderCenterY = capsule.center.y;
        }

        codeDoors = FindObjectsByType<CodeDoor>(FindObjectsSortMode.None);
    }

    void Update()
    {
        isGrounded = Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundLayer);

        Debug.DrawRay(camHolder.transform.position, camHolder.transform.forward * 3f, Color.red);

        // Crouch camera smooth lerp
        _crouchYOffset = Mathf.Lerp(_crouchYOffset, _targetCrouchYOffset,
            Time.deltaTime / Mathf.Max(0.01f, crouchTransitionTime));

        CheckIfUsingCodePanel();

        if (IsAnyBlocker())
        {
            ResetHeadBob();
            return;
        }

        Interact();
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

        float bobY = Mathf.Sin(timer) * bobAmount;
        float bobX = Mathf.Cos(timer * 0.5f) * bobSideAmount;

        Vector3 newPos = camHolder.transform.localPosition;
        newPos.y = defaultYPos + _crouchYOffset + bobY;
        newPos.x = defaultXPos + bobX;
        camHolder.transform.localPosition = newPos;

        float tilt = Mathf.Sin(timer * 0.5f) * tiltAmount;
        camHolder.transform.localEulerAngles = new Vector3(lookRotation, 0f, tilt);
    }

    void ResetHeadBob()
    {
        timer = 0;

        Vector3 pos = camHolder.transform.localPosition;
        pos.y = Mathf.Lerp(pos.y, defaultYPos + _crouchYOffset, Time.deltaTime * 5f);
        pos.x = Mathf.Lerp(pos.x, defaultXPos, Time.deltaTime * 5f);
        camHolder.transform.localPosition = pos;

        Vector3 rot = camHolder.transform.localEulerAngles;
        rot.z = Mathf.LerpAngle(rot.z, 0f, Time.deltaTime * 5f);
        camHolder.transform.localEulerAngles = new Vector3(lookRotation, 0f, rot.z);
    }

    void Interact()
    {
        Ray ray = new Ray(camHolder.transform.position, camHolder.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactLayer))
        {
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                var door = hit.collider.GetComponent<DoorOpen>();
                if (door != null) { door.OpenDoor(); return; }

                var pickable = hit.collider.GetComponentInParent<PickableItem>();
                if (pickable != null && playerInventory != null)
                {
                    pickable.TryPickup(playerInventory);
                    return;
                }
            }
        }
    }

    void AnimationHandle()
    {
        if (anim == null || !anim.gameObject.activeInHierarchy) return;
        anim.SetBool("isWalking", moveInput.magnitude > 0.01f);
        anim.SetBool("isCrouching", IsCrouching);
        anim.SetBool("isSprinting", isSprinting);
    }

    void ApplyCrouchCollider(bool crouching)
    {
        if (capsule == null) return;
        capsule.height = crouching ? crouchColliderHeight : _standColliderHeight;
        capsule.center = new Vector3(0, crouching ? crouchColliderCenterY : _standColliderCenterY, 0);
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
            IsCrouching = false;
            _targetCrouchYOffset = 0f;
            ApplyCrouchCollider(false);
        }
        else
        {
            IsCrouching = true;
            isSprinting = false;
            _pendingSprint = false;
            _targetCrouchYOffset = crouchCamOffset;
            ApplyCrouchCollider(true);
        }
    }

    public void OnSprint(InputAction.CallbackContext context)
    {
        if (IsAnyBlocker()) { isSprinting = false; return; }

        if (context.performed)
        {
            if (!IsCrouching)
            {
                isSprinting = true;
            }
            else if (!_isStandingUp)
            {
                // Agachado intentando correr: primero se levanta, luego corre
                StartCoroutine(StandUpThenSprint());
            }
        }

        if (context.canceled)
        {
            isSprinting = false;
            _pendingSprint = false;
        }
    }

    IEnumerator StandUpThenSprint()
    {
        _isStandingUp = true;
        _pendingSprint = true;

        // Iniciar transición de pie
        IsCrouching = false;
        _targetCrouchYOffset = 0f;
        ApplyCrouchCollider(false);

        // Esperar la duración de la transición de agacharse
        yield return new WaitForSeconds(crouchTransitionTime);

        _isStandingUp = false;

        // Solo correr si el jugador aún mantiene pulsado el botón de sprint
        if (_pendingSprint)
            isSprinting = true;
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (IsAnyBlocker()) return;
        if (context.performed && isGrounded && rb != null)
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
    }
    #endregion
}
