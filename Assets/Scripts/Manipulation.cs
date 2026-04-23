using UnityEngine;
using UnityEngine.XR;

public class DirectManipulationVR : MonoBehaviour
{
    [Header("References")]
    public Transform rayOrigin;
    public LayerMask selectableMask = ~0;

    [Header("Raycast")]
    public float maxRayDistance = 20f;

    [Header("Move")]
    public float moveOffsetStep = 0.2f;

    [Header("Rotate")]
    public float rotationStep = 15f;

    [Header("Scale")]
    public float scaleStep = 0.1f;
    public float minScale = 0.2f;
    public float maxScale = 3f;

    [Header("Optional")]
    public bool showDebugRay = true;

    private InputDevice rightDevice;

    private bool prevTrigger;
    private bool prevGrip;
    private bool triggerPressed;
    private bool gripPressed;

    private SelectableObject selectedObject;
    private bool manipulationMode = false;

    private float moveOffset = 0f;
    private float currentYRotation = 0f;
    private Vector3 currentScale = Vector3.one;

    void Start()
    {
        RefreshDevice();

        if (rayOrigin == null)
            rayOrigin = transform;
    }

    void Update()
    {
        if (!rightDevice.isValid)
            RefreshDevice();

        ReadButtons();

        HandleSelection();
        HandleManipulationModeToggle();

        if (manipulationMode && selectedObject != null)
        {
            HandleManipulation();
        }
    }

    private void HandleSelection()
    {
        // Only allow new selection when not manipulating
        if (manipulationMode) return;

        if (TriggerDown())
        {
            Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, maxRayDistance, selectableMask))
            {
                SelectableObject obj = hit.collider.GetComponentInParent<SelectableObject>();
                if (obj != null)
                {
                    SetSelected(obj);
                }
            }
        }
    }

    private void HandleManipulationModeToggle()
    {
        if (selectedObject == null) return;

        // Grip press toggles manipulation mode on/off
        if (GripDown())
        {
            manipulationMode = !manipulationMode;

            if (manipulationMode)
            {
                moveOffset = 0f;
                currentYRotation = selectedObject.transform.eulerAngles.y;
                currentScale = selectedObject.transform.localScale;
            }
        }
    }

    private void HandleManipulation()
    {
        // Trigger only = move
        if (triggerPressed && !gripPressed)
        {
            HandleMove();
        }
        // Grip only does nothing here because grip press toggles mode
        // so rotation is done by holding grip after already in mode? not possible with same logic
        // better: use trigger click cycles submodes
    }

    private enum ManipulationSubMode
    {
        Move,
        Rotate,
        Scale
    }

    private ManipulationSubMode subMode = ManipulationSubMode.Move;

    private void LateUpdate()
    {
        if (!manipulationMode || selectedObject == null) return;

        // While in manipulation mode:
        // trigger press cycles Move -> Rotate -> Scale
        if (TriggerDown())
        {
            if (subMode == ManipulationSubMode.Move)
                subMode = ManipulationSubMode.Rotate;
            else if (subMode == ManipulationSubMode.Rotate)
                subMode = ManipulationSubMode.Scale;
            else
                subMode = ManipulationSubMode.Move;
        }

        if (subMode == ManipulationSubMode.Move)
        {
            HandleMoveByGripStep();
        }
        else if (subMode == ManipulationSubMode.Rotate)
        {
            HandleRotateByGripStep();
        }
        else if (subMode == ManipulationSubMode.Scale)
        {
            HandleScaleByGripStep();
        }
    }

    private void HandleMove()
    {
        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, maxRayDistance))
        {
            Vector3 pos = hit.point + rayOrigin.forward * moveOffset;
            selectedObject.transform.position = pos;
        }
        else
        {
            selectedObject.transform.position =
                rayOrigin.position + rayOrigin.forward * (2f + moveOffset);
        }
    }

    private void HandleMoveByGripStep()
    {
        HandleMove();

        // use analog-ish behavior by checking grip amount if available
        float gripValue;
        if (rightDevice.TryGetFeatureValue(CommonUsages.grip, out gripValue))
        {
            if (gripValue > 0.8f)
            {
                moveOffset += moveOffsetStep * Time.deltaTime * 4f;
            }
        }
    }

    private void HandleRotateByGripStep()
    {
        float triggerValue;
        float gripValue;

        rightDevice.TryGetFeatureValue(CommonUsages.trigger, out triggerValue);
        rightDevice.TryGetFeatureValue(CommonUsages.grip, out gripValue);

        if (triggerValue > 0.8f)
            currentYRotation -= rotationStep * Time.deltaTime * 4f;

        if (gripValue > 0.8f)
            currentYRotation += rotationStep * Time.deltaTime * 4f;

        Vector3 euler = selectedObject.transform.eulerAngles;
        selectedObject.transform.rotation = Quaternion.Euler(euler.x, currentYRotation, euler.z);
    }

    private void HandleScaleByGripStep()
    {
        float triggerValue;
        float gripValue;

        rightDevice.TryGetFeatureValue(CommonUsages.trigger, out triggerValue);
        rightDevice.TryGetFeatureValue(CommonUsages.grip, out gripValue);

        if (triggerValue > 0.8f)
            currentScale -= Vector3.one * scaleStep * Time.deltaTime * 2f;

        if (gripValue > 0.8f)
            currentScale += Vector3.one * scaleStep * Time.deltaTime * 2f;

        currentScale.x = Mathf.Clamp(currentScale.x, minScale, maxScale);
        currentScale.y = Mathf.Clamp(currentScale.y, minScale, maxScale);
        currentScale.z = Mathf.Clamp(currentScale.z, minScale, maxScale);

        selectedObject.transform.localScale = currentScale;
    }

    private void SetSelected(SelectableObject obj)
    {
        if (selectedObject != null)
            selectedObject.SetHighlight(false);

        selectedObject = obj;
        selectedObject.SetHighlight(true);

        currentYRotation = selectedObject.transform.eulerAngles.y;
        currentScale = selectedObject.transform.localScale;
    }

    private void RefreshDevice()
    {
        rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    private void ReadButtons()
    {
        prevTrigger = triggerPressed;
        prevGrip = gripPressed;

        triggerPressed = false;
        gripPressed = false;

        if (rightDevice.isValid)
        {
            rightDevice.TryGetFeatureValue(CommonUsages.triggerButton, out triggerPressed);
            rightDevice.TryGetFeatureValue(CommonUsages.gripButton, out gripPressed);
        }
    }

    private bool TriggerDown()
    {
        return triggerPressed && !prevTrigger;
    }

    private bool GripDown()
    {
        return gripPressed && !prevGrip;
    }

    private void OnDrawGizmos()
    {
        if (!showDebugRay || rayOrigin == null) return;

        Gizmos.color = Color.red;
        Gizmos.DrawRay(rayOrigin.position, rayOrigin.forward * maxRayDistance);
    }

    private void OnGUI()
    {
        float w = 420;
        float h = 120;
        float x = (Screen.width - w) / 2f;
        float y = 40f;

        string selectedName = selectedObject == null ? "None" : selectedObject.name;
        string modeText = manipulationMode ? subMode.ToString() : "Selection";

        GUI.Box(new Rect(x, y, w, h),
            "Selected: " + selectedName + "\n" +
            "Mode: " + modeText + "\n\n" +
            "Trigger = Select / Cycle Mode\n" +
            "Grip = Enter/Exit Manipulation");
    }
}