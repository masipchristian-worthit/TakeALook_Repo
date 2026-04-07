using UnityEngine;
using UnityEngine.InputSystem;

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

    [Header("Weapon State")]
    [SerializeField] bool isPistolEquipped;
    [SerializeField] bool isPistolUnequipped = true;

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
    #endregion

    Rigidbody rb;
    Animator anim;

    Vector2 moveInput;
    Vector2 lookInput;
    float lookRotation;

    float defaultYPos;
    float defaultXPos;
    float timer;

    CodeDoor[] codeDoors;
    bool isUsingCodePanel;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        anim = GetComponent<Animator>();
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        defaultYPos = camHolder.transform.localPosition.y;
        defaultXPos = camHolder.transform.localPosition.x;

        UpdateWeaponBools();

        codeDoors = FindObjectsByType<CodeDoor>(FindObjectsSortMode.None);
    }

    void Update()
    {
        isGrounded = Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundLayer);

        Debug.DrawRay(camHolder.transform.position, camHolder.transform.forward * 3f, Color.red);

        CheckIfUsingCodePanel();

        if (isUsingCodePanel)
            return;

        Interact();
        HeadBob();
    }

    private void FixedUpdate()
    {
        if (isUsingCodePanel) return;

        Movement();
    }

    private void LateUpdate()
    {
        if (isUsingCodePanel) return;

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

            if (rb != null)
            {
                Vector3 velocity = rb.linearVelocity;
                velocity.x = 0f;
                velocity.z = 0f;
                rb.linearVelocity = velocity;
            }
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

        Vector3 velocityChange = (targetVelocity - currentVelocity);
        velocityChange = new Vector3(velocityChange.x, 0f, velocityChange.z);
        velocityChange = Vector3.ClampMagnitude(velocityChange, maxForce);

        rb.AddForce(velocityChange, ForceMode.VelocityChange);
    }

    void Jump()
    {
        if (isGrounded) rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
    }

    void HeadBob()
    {
        if (moveInput.magnitude < 0.1f || !isGrounded)
        {
            timer = 0;

            Vector3 pos = camHolder.transform.localPosition;
            pos.y = Mathf.Lerp(pos.y, defaultYPos, Time.deltaTime * 5f);
            pos.x = Mathf.Lerp(pos.x, defaultXPos, Time.deltaTime * 5f);
            camHolder.transform.localPosition = pos;

            Vector3 rot = camHolder.transform.localEulerAngles;
            rot.z = Mathf.LerpAngle(rot.z, 0f, Time.deltaTime * 5f);
            camHolder.transform.localEulerAngles = new Vector3(lookRotation, 0f, rot.z);

            return;
        }

        float speedMultiplier = 1f;

        if (isSprinting) speedMultiplier = sprintMultiplier;
        if (IsCrouching) speedMultiplier = crouchMultiplier;

        timer += Time.deltaTime * bobSpeed * speedMultiplier;

        float bobY = Mathf.Sin(timer) * bobAmount;
        float bobX = Mathf.Cos(timer * 0.5f) * bobSideAmount;

        Vector3 newPos = camHolder.transform.localPosition;
        newPos.y = defaultYPos + bobY;
        newPos.x = defaultXPos + bobX;

        camHolder.transform.localPosition = newPos;

        float tilt = Mathf.Sin(timer * 0.5f) * tiltAmount;
        camHolder.transform.localEulerAngles = new Vector3(lookRotation, 0f, tilt);
    }

    void Interact()
    {
        Ray ray = new Ray(camHolder.transform.position, camHolder.transform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, interactDistance, interactLayer))
        {
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                DoorOpen door = hit.collider.GetComponent<DoorOpen>();

                if (door != null)
                {
                    door.OpenDoor();
                }
            }
        }
    }

    void UpdateWeaponBools()
    {
        isPistolUnequipped = !isPistolEquipped;
    }

    public void EquipPistol()
    {
        isPistolEquipped = true;
        UpdateWeaponBools();
    }

    public void UnequipPistol()
    {
        isPistolEquipped = false;
        UpdateWeaponBools();
    }

    public bool IsPistolEquipped()
    {
        return isPistolEquipped;
    }

    public bool IsPistolUnequipped()
    {
        return isPistolUnequipped;
    }

    #region Input Methods
    public void OnMove(InputAction.CallbackContext context)
    {
        if (isUsingCodePanel)
        {
            moveInput = Vector2.zero;
            return;
        }

        moveInput = context.ReadValue<Vector2>();
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        if (isUsingCodePanel)
        {
            lookInput = Vector2.zero;
            return;
        }

        lookInput = context.ReadValue<Vector2>();
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (isUsingCodePanel) return;

        if (context.performed) Jump();
    }

    public void OnCrouch(InputAction.CallbackContext context)
    {
        if (isUsingCodePanel) return;

        if (context.performed)
        {
            IsCrouching = !IsCrouching;
            anim.SetBool("IsCrouching", IsCrouching);
        }
    }

    public void OnSprint(InputAction.CallbackContext context)
    {
        if (isUsingCodePanel)
        {
            isSprinting = false;
            return;
        }

        if (context.performed && !IsCrouching) isSprinting = true;
        if (context.canceled) isSprinting = false;
    }
    #endregion
}