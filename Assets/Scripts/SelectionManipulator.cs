using UnityEngine;
using UnityEngine.XR;

// Handles selection mode, ray indicator, hover highlight,
// object selection, and move/rotate/scale manipulation.
public class SelectionManipulator : MonoBehaviour
{
    // ===================== RAY SETTINGS =====================
    [Header("Ray Settings")]
    public Transform rayOrigin;
    public float maxRayDistance = 20f;
    public LayerMask selectableMask = ~0;

    // ===================== INDICATOR SETTINGS =====================
    [Header("Indicator")]
    public LineRenderer lineRenderer;
    public Transform hitPointVisual;

    // ===================== MANIPULATION SETTINGS =====================
    [Header("Manipulation Settings")]
    public float moveHeightOffset = 0.7f;
    public float defaultMoveDistance = 3f;
    public float scaleSpeed = 1.5f;
    public float minScale = 0.2f;
    public float maxScale = 3f;

    // ===================== HIGHLIGHT SETTINGS =====================
    [Header("Highlight")]
    public Color hoverColor = Color.cyan;
    public Color selectedColor = Color.yellow;

    // ===================== INPUT DEVICES =====================
    private InputDevice leftDevice;
    private InputDevice rightDevice;

    private bool prevRightTrigger;
    private bool prevRightGrip;
    private bool prevLeftTrigger;

    // ===================== SELECTION STATE =====================
    private bool selectionMode = false;

    private GameObject hoveredObject;
    private Renderer[] hoveredRenderers;

    private GameObject selectedObject;
    private Renderer[] selectedRenderers;
    private MaterialPropertyBlock[] selectedOriginalBlocks;

    // ===================== MANIPULATION MODE =====================
    private enum ManipulationMode
    {
        Move,
        Rotate,
        Scale
    }

    private ManipulationMode currentMode = ManipulationMode.Move;

    // ===================== INITIALIZATION =====================
    void Start()
    {
        RefreshDevices();

        if (rayOrigin == null)
            rayOrigin = transform;

        if (lineRenderer != null)
            lineRenderer.enabled = false;

        if (hitPointVisual != null)
            hitPointVisual.gameObject.SetActive(false);
    }

    // ===================== MAIN UPDATE LOOP =====================
    void Update()
    {
        if (!leftDevice.isValid || !rightDevice.isValid)
            RefreshDevices();

        ReadButtons(
            out bool rightTriggerDown,
            out bool rightTriggerHeld,
            out bool rightGripDown,
            out bool leftTriggerDown
        );

        // Idle state: Right Grip enters selection mode
        if (!selectionMode)
        {
            if (rightGripDown)
            {
                selectionMode = true;
                currentMode = ManipulationMode.Move;

                if (lineRenderer != null)
                    lineRenderer.enabled = true;

                Debug.Log("Selection Mode Started");
            }

            return;
        }

        // Selection mode indicator and hover highlight
        UpdateIndicator();
        UpdateHoverHighlight();

        // Left Trigger exits selection mode
        if (leftTriggerDown)
        {
            if (selectedObject != null)
                DeselectObject();

            ClearHoverHighlight();

            selectionMode = false;

            if (lineRenderer != null)
                lineRenderer.enabled = false;

            if (hitPointVisual != null)
                hitPointVisual.gameObject.SetActive(false);

            Debug.Log("Selection Mode Ended");
            return;
        }

        // Right Trigger selects the hovered object if nothing is selected
        if (rightTriggerDown && selectedObject == null)
        {
            if (hoveredObject != null)
                SelectObject(hoveredObject);

            return;
        }

        // Right Grip cycles manipulation mode after an object is selected
        if (rightGripDown && selectedObject != null)
        {
            CycleManipulationMode();
        }

        // Hold Right Trigger to manipulate selected object
        if (rightTriggerHeld && selectedObject != null)
        {
            ManipulateSelectedObject();
        }
    }

    // ===================== INDICATOR =====================
    private void UpdateIndicator()
    {
        if (rayOrigin == null || lineRenderer == null)
            return;

        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
        Vector3 endPoint;

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, selectableMask))
        {
            endPoint = hit.point;

            if (hitPointVisual != null)
            {
                hitPointVisual.position = hit.point;
                hitPointVisual.gameObject.SetActive(true);
            }
        }
        else
        {
            endPoint = rayOrigin.position + rayOrigin.forward * maxRayDistance;

            if (hitPointVisual != null)
                hitPointVisual.gameObject.SetActive(false);
        }

        lineRenderer.SetPosition(0, rayOrigin.position);
        lineRenderer.SetPosition(1, endPoint);
    }

    // ===================== HOVER HIGHLIGHT =====================
    private void UpdateHoverHighlight()
    {
        if (rayOrigin == null)
            return;

        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
        GameObject newHover = null;

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, selectableMask))
        {
            newHover = GetMainSelectableObject(hit.collider);
        }

        if (hoveredObject == newHover)
            return;

        ClearHoverHighlight();

        hoveredObject = newHover;

        if (hoveredObject != null && hoveredObject != selectedObject)
        {
            hoveredRenderers = hoveredObject.GetComponentsInChildren<Renderer>();
            SetRendererColor(hoveredRenderers, hoverColor);
        }
    }

    private void ClearHoverHighlight()
    {
        if (hoveredRenderers != null)
        {
            foreach (Renderer rend in hoveredRenderers)
            {
                if (rend != null)
                    rend.SetPropertyBlock(null);
            }
        }

        hoveredObject = null;
        hoveredRenderers = null;
    }

    // ===================== SELECTION =====================
    private GameObject GetMainSelectableObject(Collider col)
    {
        SelectableObject selectable = col.GetComponentInParent<SelectableObject>();
        if (selectable != null)
            return selectable.gameObject;

        if (col.attachedRigidbody != null)
            return col.attachedRigidbody.gameObject;

        return col.gameObject;
    }

    private void SelectObject(GameObject obj)
    {
        selectedObject = obj;
        selectedRenderers = selectedObject.GetComponentsInChildren<Renderer>();

        SaveSelectedOriginalBlocks();

        ClearHoverHighlight();
        SetRendererColor(selectedRenderers, selectedColor);

        currentMode = ManipulationMode.Move;

        Debug.Log("Selected: " + selectedObject.name);
    }

    private void DeselectObject()
    {
        RestoreSelectedOriginalBlocks();

        selectedObject = null;
        selectedRenderers = null;
        selectedOriginalBlocks = null;

        Debug.Log("Deselected");
    }

    // ===================== HIGHLIGHT HELPERS =====================
    private void SaveSelectedOriginalBlocks()
    {
        if (selectedRenderers == null)
            return;

        selectedOriginalBlocks = new MaterialPropertyBlock[selectedRenderers.Length];

        for (int i = 0; i < selectedRenderers.Length; i++)
        {
            selectedOriginalBlocks[i] = new MaterialPropertyBlock();

            if (selectedRenderers[i] != null)
                selectedRenderers[i].GetPropertyBlock(selectedOriginalBlocks[i]);
        }
    }

    private void RestoreSelectedOriginalBlocks()
    {
        if (selectedRenderers == null)
            return;

        for (int i = 0; i < selectedRenderers.Length; i++)
        {
            if (selectedRenderers[i] == null)
                continue;

            if (selectedOriginalBlocks != null && i < selectedOriginalBlocks.Length)
                selectedRenderers[i].SetPropertyBlock(selectedOriginalBlocks[i]);
            else
                selectedRenderers[i].SetPropertyBlock(null);
        }
    }

    private void SetRendererColor(Renderer[] renderers, Color color)
    {
        if (renderers == null)
            return;

        foreach (Renderer rend in renderers)
        {
            if (rend == null)
                continue;

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            rend.GetPropertyBlock(block);

            // _Color works for Standard shader.
            // _BaseColor works for URP Lit shader.
            block.SetColor("_Color", color);
            block.SetColor("_BaseColor", color);

            rend.SetPropertyBlock(block);
        }
    }

    // ===================== MODE SWITCHING =====================
    private void CycleManipulationMode()
    {
        if (currentMode == ManipulationMode.Move)
            currentMode = ManipulationMode.Rotate;
        else if (currentMode == ManipulationMode.Rotate)
            currentMode = ManipulationMode.Scale;
        else
            currentMode = ManipulationMode.Move;

        Debug.Log("Manipulation Mode: " + currentMode);
    }

    // ===================== MANIPULATION =====================
    private void ManipulateSelectedObject()
    {
        if (currentMode == ManipulationMode.Move)
            MoveObject();
        else if (currentMode == ManipulationMode.Rotate)
            RotateObject();
        else if (currentMode == ManipulationMode.Scale)
            ScaleObject();
    }

    private void MoveObject()
    {
        if (rayOrigin == null || selectedObject == null)
            return;

        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance))
        {
            selectedObject.transform.position = hit.point + Vector3.up * moveHeightOffset;
        }
        else
        {
            selectedObject.transform.position =
                rayOrigin.position + rayOrigin.forward * defaultMoveDistance;
        }
    }

    private void RotateObject()
    {
        if (rayOrigin == null || selectedObject == null)
            return;

        Vector3 flatForward = Vector3.ProjectOnPlane(rayOrigin.forward, Vector3.up);

        if (flatForward.sqrMagnitude < 0.001f)
            return;

        float yRotation =
            Quaternion.LookRotation(flatForward.normalized, Vector3.up).eulerAngles.y;

        selectedObject.transform.rotation = Quaternion.Euler(0f, yRotation, 0f);
    }

    private void ScaleObject()
    {
        if (rayOrigin == null || selectedObject == null)
            return;

        Vector3 currentScale = selectedObject.transform.localScale;

        // Point controller upward to scale up
        if (rayOrigin.forward.y > 0.15f)
        {
            currentScale += Vector3.one * scaleSpeed * Time.deltaTime;
        }
        // Point controller downward to scale down
        else if (rayOrigin.forward.y < -0.15f)
        {
            currentScale -= Vector3.one * scaleSpeed * Time.deltaTime;
        }

        currentScale.x = Mathf.Clamp(currentScale.x, minScale, maxScale);
        currentScale.y = Mathf.Clamp(currentScale.y, minScale, maxScale);
        currentScale.z = Mathf.Clamp(currentScale.z, minScale, maxScale);

        selectedObject.transform.localScale = currentScale;
    }

    // ===================== INPUT =====================
    private void RefreshDevices()
    {
        leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    private void ReadButtons(
        out bool rightTriggerDown,
        out bool rightTriggerHeld,
        out bool rightGripDown,
        out bool leftTriggerDown
    )
    {
        bool rightTrigger = false;
        bool rightGrip = false;
        bool leftTrigger = false;

        if (rightDevice.isValid)
        {
            rightDevice.TryGetFeatureValue(CommonUsages.triggerButton, out rightTrigger);
            rightDevice.TryGetFeatureValue(CommonUsages.gripButton, out rightGrip);
        }

        if (leftDevice.isValid)
        {
            leftDevice.TryGetFeatureValue(CommonUsages.triggerButton, out leftTrigger);
        }

        rightTriggerDown = rightTrigger && !prevRightTrigger;
        rightTriggerHeld = rightTrigger;
        rightGripDown = rightGrip && !prevRightGrip;
        leftTriggerDown = leftTrigger && !prevLeftTrigger;

        prevRightTrigger = rightTrigger;
        prevRightGrip = rightGrip;
        prevLeftTrigger = leftTrigger;
    }

    // ===================== DEBUG GUI =====================
    void OnGUI()
    {
        if (!selectionMode)
            return;

        string selectedName = selectedObject == null ? "None" : selectedObject.name;

        GUI.Box(
            new Rect(20, 20, 320, 130),
            "Selection Mode\n\n" +
            "Selected: " + selectedName + "\n" +
            "Mode: " + currentMode + "\n\n" +
            "Right Trigger = Select / Manipulate\n" +
            "Right Grip = Change Mode\n" +
            "Left Trigger = Exit"
        );
    }
}
