using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

[DisallowMultipleComponent]
public class TravelRigController : MonoBehaviour
{
    [Header("Rig References")]
    [SerializeField] private Transform rigRoot;
    [SerializeField] private Transform headTransform;
    [SerializeField] private CharacterController characterController;

    [Header("Desktop Fallback Input")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference turnAction;

    [Header("XR Thumbsticks")]
    [SerializeField] private XRNode moveHand = XRNode.LeftHand;
    [SerializeField] private XRNode turnHand = XRNode.RightHand;
    [SerializeField] private bool preferXRThumbsticks = true;

    [Header("Motion")]
    [SerializeField, Min(0.1f)] private float moveSpeed = 1.75f;
    [SerializeField, Min(10f)] private float turnSpeedDegrees = 90f;
    [SerializeField, Range(0f, 1f)] private float deadZone = 0.2f;
    [SerializeField] private bool rotateAroundHead = true;
    [SerializeField] private bool applyGravityWithCharacterController = true;

    private Vector3 verticalVelocity;
    private bool moveActionWasEnabled;
    private bool turnActionWasEnabled;

    private void Reset()
    {
        rigRoot = transform;
        characterController = GetComponent<CharacterController>();

        if (Camera.main != null)
        {
            headTransform = Camera.main.transform;
        }
    }

    private void OnEnable()
    {
        moveActionWasEnabled = EnableAction(moveAction);
        turnActionWasEnabled = EnableAction(turnAction);
    }

    private void OnDisable()
    {
        DisableAction(moveAction, moveActionWasEnabled);
        DisableAction(turnAction, turnActionWasEnabled);
    }

    private void Update()
    {
        if (rigRoot == null)
        {
            return;
        }

        Vector2 moveInput = GetPreferredInput(moveHand, moveAction);
        Vector2 turnInput = GetPreferredInput(turnHand, turnAction);

        ApplyTurn(turnInput.x);
        ApplyMove(moveInput);
    }

    private void ApplyMove(Vector2 moveInput)
    {
        Vector2 processedInput = ApplyDeadZone(moveInput);
        Vector3 planarDirection = GetPlanarDirection(processedInput);
        Vector3 horizontalVelocity = planarDirection * moveSpeed;

        if (characterController != null)
        {
            if (applyGravityWithCharacterController)
            {
                if (characterController.isGrounded && verticalVelocity.y < 0f)
                {
                    verticalVelocity.y = -0.5f;
                }

                verticalVelocity.y += Physics.gravity.y * Time.deltaTime;
            }
            else
            {
                verticalVelocity = Vector3.zero;
            }

            Vector3 velocity = horizontalVelocity + verticalVelocity;
            characterController.Move(velocity * Time.deltaTime);
            return;
        }

        rigRoot.position += horizontalVelocity * Time.deltaTime;
    }

    private void ApplyTurn(float turnValue)
    {
        if (Mathf.Abs(turnValue) < deadZone)
        {
            return;
        }

        float signedAngle = turnValue * turnSpeedDegrees * Time.deltaTime;
        Vector3 pivot = rigRoot.position;

        if (rotateAroundHead && headTransform != null)
        {
            pivot = headTransform.position;
        }

        rigRoot.RotateAround(pivot, Vector3.up, signedAngle);
    }

    private Vector2 GetPreferredInput(XRNode xrNode, InputActionReference actionReference)
    {
        Vector2 xrInput = ReadXRThumbstick(xrNode);
        Vector2 fallbackInput = ReadAction(actionReference);

        if (preferXRThumbsticks)
        {
            return xrInput.sqrMagnitude > 0.0001f ? xrInput : fallbackInput;
        }

        return fallbackInput.sqrMagnitude > 0.0001f ? fallbackInput : xrInput;
    }

    private Vector2 ReadXRThumbstick(XRNode xrNode)
    {
        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(xrNode);
        Vector2 axis = Vector2.zero;

        if (device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out axis))
        {
            return axis;
        }

        return Vector2.zero;
    }

    private Vector2 ReadAction(InputActionReference actionReference)
    {
        if (actionReference == null || actionReference.action == null)
        {
            return Vector2.zero;
        }

        return actionReference.action.ReadValue<Vector2>();
    }

    private Vector2 ApplyDeadZone(Vector2 input)
    {
        float magnitude = input.magnitude;

        if (magnitude <= deadZone)
        {
            return Vector2.zero;
        }

        float scaledMagnitude = Mathf.InverseLerp(deadZone, 1f, Mathf.Clamp01(magnitude));
        return input.normalized * scaledMagnitude;
    }

    private Vector3 GetPlanarDirection(Vector2 moveInput)
    {
        if (moveInput == Vector2.zero)
        {
            return Vector3.zero;
        }

        Vector3 forward = GetPlanarForward();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 direction = forward * moveInput.y + right * moveInput.x;

        if (direction.sqrMagnitude > 1f)
        {
            direction.Normalize();
        }

        return direction;
    }

    private Vector3 GetPlanarForward()
    {
        Transform reference = headTransform != null ? headTransform : rigRoot;
        Vector3 forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up);

        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }

        return forward.normalized;
    }

    private bool EnableAction(InputActionReference actionReference)
    {
        if (actionReference == null || actionReference.action == null || actionReference.action.enabled)
        {
            return false;
        }

        actionReference.action.Enable();
        return true;
    }

    private void DisableAction(InputActionReference actionReference, bool wasEnabledHere)
    {
        if (!wasEnabledHere || actionReference == null || actionReference.action == null)
        {
            return;
        }

        actionReference.action.Disable();
    }
}
