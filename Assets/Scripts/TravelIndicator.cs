using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]

    /**
        The TravelIndicator is triggered when a user moves a thumbstick. It reads the user input (via thumstick)
        in every frame, then shows a line or arc that tells you which way you should move or turn. 
    **/
public class TravelIndicator : MonoBehaviour {
    /**
        We group fields by headers (i.e. "Scene References", "Desktop Fallback Input", "XR Thumbsticks", "Pointer Tuning").
        The script renders these fields in the inspector and ensure that only this class alone can access it.
    **/
    [Header("Scene References")]
    [SerializeField] private Transform indicatorOrigin; // where LineRenderer Starts
    [SerializeField] private Transform movementReference; // decides forward direction 
    [SerializeField] private LineRenderer lineRenderer;  // draws line/arc to indicate potential user travel

    [Header("Desktop Fallback Input")]
    [SerializeField] private InputActionReference moveAction; // Decides movement
    [SerializeField] private InputActionReference turnAction; // Decides turning/orientation

    [Header("XR Thumbsticks")]
    [SerializeField] private XRNode moveHand = XRNode.LeftHand; // Wires left XR controller for movement input
    [SerializeField] private XRNode turnHand = XRNode.RightHand; // Wires right XR controller for turning input
    [SerializeField] private bool preferXRThumbsticks = true; //

    [Header("Pointer Tuning")]
    [SerializeField, Range(0f, 1f)] private float deadZone = 0.2f; // Ignores small stick movements <0.2f
    [SerializeField, Min(0.2f)] private float movePointerLength = 1.25f; //
    [SerializeField, Min(0.2f)] private float turnArcRadius = 0.75f; // Default turning radius value, must be >0.2f
    [SerializeField, Range(15f, 180f)] private float turnArcDegrees = 65f; // Default turning arc degress, must be between 15f & 180f
    [SerializeField, Range(3, 24)] private int turnArcSegments = 10; //Default # of line segments to set up turnArc, must be between 3 & 24
    [SerializeField] private float verticalOffset = -0.03f; //
    [SerializeField] private Color moveColor = new Color(0.17f, 0.9f, 0.78f, 1f); // Color of movement arrow
    [SerializeField] private Color turnColor = new Color(1f, 0.73f, 0.21f, 1f); // Color of turning arc

    private bool moveActionWasEnabled;
    private bool turnActionWasEnabled;

    /** 
        When Unity Component is added or reset, this sets defaults: Hooks up indicatorOrigin, Grabs LineRenderer 
    */
    private void Reset() {
        indicatorOrigin = transform;
        lineRenderer = GetComponent<LineRenderer>();

        if (Camera.main != null) {
            movementReference = Camera.main.transform;
        }

        ConfigureLineRenderer();
    }

    /** 
        Before gameplay starts, we make sure the LineRenderer reference exists and is configured
    */
    private void Awake() {
        if (lineRenderer == null) {
            lineRenderer = GetComponent<LineRenderer>();
        }

        ConfigureLineRenderer();
    }

    /** 
        Activate moveAction and turnAction for specified component
    */
    private void OnEnable() {
        moveActionWasEnabled = EnableAction(moveAction);
        turnActionWasEnabled = EnableAction(turnAction);
    }

    /** 
        Deactivate moveAction and turnAction for specified component
    */
    private void OnDisable() {
        DisableAction(moveAction, moveActionWasEnabled);
        DisableAction(turnAction, turnActionWasEnabled);
    }


    /** 
        Runs in every frame during active gameplay
        Each frame, show a move pointer, a turn arc, or hide the indicator if there is no input.
    **/
    private void Update() {
        if (indicatorOrigin == null || lineRenderer == null) {
            return;
        }

        Vector2 moveInput = ApplyDeadZone(GetPreferredInput(moveHand, moveAction));
        Vector2 turnInput = ApplyDeadZone(GetPreferredInput(turnHand, turnAction));

        if (moveInput != Vector2.zero) {
            RenderMovePointer(moveInput);
            return;
        }

        if (Mathf.Abs(turnInput.x) > 0f) {
            RenderTurnArc(turnInput.x);
            return;
        }

        lineRenderer.enabled = false;
    }

    /**
        We convert moveInput into a 3D direction. Then, we set up 6 points in the LineRenderer: 
        1.) start, 2.) shaft end, 3.) tip, 4.) one side of arrowhead, 
        5.) tip again, and 5.) other side of arrowhead,
    **/
    private void RenderMovePointer(Vector2 moveInput) {

        Vector3 direction = GetPlanarDirection(moveInput);

        if (direction == Vector3.zero)
        {
            lineRenderer.enabled = false;
            return;
        }

        Vector3 origin = GetOriginPoint();
        float length = movePointerLength * Mathf.Clamp01(moveInput.magnitude);
        float headSize = Mathf.Min(0.15f, length * 0.2f);
        Vector3 tip = origin + direction * length;
        Vector3 shaftEnd = tip - direction * headSize;
        Vector3 side = Vector3.Cross(Vector3.up, direction).normalized * headSize * 0.7f;

        lineRenderer.positionCount = 6;
        lineRenderer.SetPosition(0, origin);
        lineRenderer.SetPosition(1, shaftEnd);
        lineRenderer.SetPosition(2, tip);
        lineRenderer.SetPosition(3, shaftEnd + side);
        lineRenderer.SetPosition(4, tip);
        lineRenderer.SetPosition(5, shaftEnd - side);
        lineRenderer.startColor = moveColor;
        lineRenderer.endColor = moveColor;
        lineRenderer.enabled = true;
    }

    /**

    **/
    private void RenderTurnArc(float turnValue)
    {
        Vector3 origin = GetOriginPoint();
        Vector3 forward = GetPlanarForward();
        float signedAngle = Mathf.Sign(turnValue) * turnArcDegrees * Mathf.Clamp01(Mathf.Abs(turnValue));
        int segmentCount = Mathf.Max(3, turnArcSegments);

        lineRenderer.positionCount = segmentCount + 2;
        lineRenderer.SetPosition(0, origin);

        for (int i = 0; i <= segmentCount; i++) {
            float t = i / (float)segmentCount;
            float angle = Mathf.Lerp(0f, signedAngle, t);
            Vector3 point = origin + Quaternion.AngleAxis(angle, Vector3.up) * forward * turnArcRadius;
            lineRenderer.SetPosition(i + 1, point);
        }

        lineRenderer.startColor = turnColor;
        lineRenderer.endColor = turnColor;
        lineRenderer.enabled = true;
    }


    /**

    **/
    private Vector3 GetOriginPoint() {
        return indicatorOrigin.position + Vector3.up * verticalOffset;
    }

    private Vector2 GetPreferredInput(XRNode xrNode, InputActionReference actionReference) {
        Vector2 xrInput = ReadXRThumbstick(xrNode);
        Vector2 fallbackInput = ReadAction(actionReference);

        if (preferXRThumbsticks)
        {
            return xrInput.sqrMagnitude > 0.0001f ? xrInput : fallbackInput;
        }

        return fallbackInput.sqrMagnitude > 0.0001f ? fallbackInput : xrInput;
    }


    /**

    **/
    private Vector2 ReadXRThumbstick(XRNode xrNode) {
        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(xrNode);
        Vector2 axis = Vector2.zero;

        if (device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out axis)) {
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

    private Vector2 ApplyDeadZone(Vector2 input) {
        float magnitude = input.magnitude;

        if (magnitude <= deadZone) {
            return Vector2.zero;
        }

        float scaledMagnitude = Mathf.InverseLerp(deadZone, 1f, Mathf.Clamp01(magnitude));
        return input.normalized * scaledMagnitude;
    }

    private Vector3 GetPlanarDirection(Vector2 moveInput) {
        if (moveInput == Vector2.zero) {
            return Vector3.zero;
        }

        Vector3 forward = GetPlanarForward();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 direction = forward * moveInput.y + right * moveInput.x;

        if (direction.sqrMagnitude > 1f) {
            direction.Normalize();
        }

        return direction;
    }

    private Vector3 GetPlanarForward()
    {
        Transform reference = movementReference != null ? movementReference : indicatorOrigin;
        Vector3 forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up);

        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }

        return forward.normalized;
    }

    private void ConfigureLineRenderer() {
        if (lineRenderer == null) {
            return;
        }

        lineRenderer.useWorldSpace = true;
        lineRenderer.loop = false;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.widthMultiplier = 0.01f;
        lineRenderer.numCapVertices = 4;
        lineRenderer.numCornerVertices = 4;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.enabled = false;
    }



    private bool EnableAction(InputActionReference actionReference) {
        if (actionReference == null || actionReference.action == null || actionReference.action.enabled)
        {
            return false;
        }

        actionReference.action.Enable();
        return true;
    }

    private void DisableAction(InputActionReference actionReference, bool wasEnabledHere) {
        if (!wasEnabledHere || actionReference == null || actionReference.action == null) {
            return;
        }

        actionReference.action.Disable();
    }
}
