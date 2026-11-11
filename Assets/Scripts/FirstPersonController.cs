using UnityEngine;

/// <summary>
/// Simple, robust first-person controller built on CharacterController.
/// Attach to the Player root. Assign the camera pivot (a child holding the Camera).
/// Uses the classic Input Manager (Horizontal, Vertical, Mouse X, Mouse Y, Jump).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Child object that holds the Camera. It pitches up/down; the body yaws left/right.")]
    [SerializeField] private Transform cameraPivot;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 4.5f;
    [SerializeField] private float sprintSpeed = 7f;
    [SerializeField] private float jumpHeight = 1.1f;
    [SerializeField] private float gravity = -20f;
    [Tooltip("Higher = snappier acceleration and stopping.")]
    [SerializeField] private float responsiveness = 12f;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float maxLookAngle = 85f;
    [Tooltip("How quickly the camera settles back down after recoil.")]
    [SerializeField] private float recoilRecovery = 10f;

    // Read-only state other systems (footsteps, head bob, weapon sway) can use later.
    public bool IsGrounded { get; private set; }
    public bool IsSprinting { get; private set; }
    public float CurrentSpeed => new Vector3(horizontalVelocity.x, 0f, horizontalVelocity.z).magnitude;

    private CharacterController controller;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private float pitch;
    private float recoilPitch; // visual kick that decays back to zero
    private CameraShake shake; // optional, found on the camera pivot

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (cameraPivot == null && Camera.main != null)
            cameraPivot = Camera.main.transform;

        if (cameraPivot == null)
            Debug.LogError("FirstPersonController: assign a Camera Pivot (or tag a camera MainCamera).", this);
        else
            shake = cameraPivot.GetComponent<CameraShake>();

        LockCursor(true);
    }

    private void Update()
    {
        // Don't process look/move while the cursor is free (pause menu, etc.).
        if (Cursor.lockState != CursorLockMode.Locked) return;

        HandleLook();
        HandleMovement();
    }

    private void HandleLook()
    {
        if (cameraPivot == null) return;

        float mouseX = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;

        transform.Rotate(Vector3.up * mouseX);

        pitch = Mathf.Clamp(pitch - mouseY, -maxLookAngle, maxLookAngle);
        // Recoil kicks the view up, then eases back down.
        recoilPitch = Mathf.Lerp(recoilPitch, 0f, 1f - Mathf.Exp(-recoilRecovery * Time.deltaTime));
        Quaternion look = Quaternion.Euler(pitch - recoilPitch, 0f, 0f);
        cameraPivot.localRotation = shake != null ? look * shake.Rotation : look;
    }

    /// <summary>Called by weapons. Positive pitchKick raises the view.</summary>
    public void AddRecoil(float pitchKick, float yawKick)
    {
        recoilPitch += pitchKick;
        transform.Rotate(Vector3.up * yawKick);
    }

    private void HandleMovement()
    {
        IsGrounded = controller.isGrounded;

        Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        input = Vector3.ClampMagnitude(input, 1f); // no faster diagonal movement

        // Sprint only while moving forward.
        IsSprinting = Input.GetKey(KeyCode.LeftShift) && input.z > 0.1f;
        float targetSpeed = IsSprinting ? sprintSpeed : walkSpeed;

        Vector3 targetVelocity = transform.TransformDirection(input) * targetSpeed;

        // Frame-rate independent smoothing.
        float t = 1f - Mathf.Exp(-responsiveness * Time.deltaTime);
        horizontalVelocity = Vector3.Lerp(horizontalVelocity, targetVelocity, t);

        // Gravity and jumping.
        if (IsGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f; // small push keeps the controller grounded on slopes

        if (IsGrounded && Input.GetButtonDown("Jump"))
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);
    }

    private static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
