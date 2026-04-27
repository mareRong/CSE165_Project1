using UnityEngine;
using UnityEngine.XR;
using TMPro;

public class SelectionManipulator : MonoBehaviour
{
    public bool IsSelectionModeActive => selectionMode || manipulationMode;

    public SpawnMenu spawnMenu;

    [Header("Ray Settings")]
    public Transform rayOrigin;
    public float maxRayDistance = 20f;
    public LayerMask selectableMask = ~0;

    [Header("Indicator")]
    public LineRenderer lineRenderer;
    public Transform hitPointVisual;

    [Header("VR Menu Canvas")]
    public GameObject vrMenuCanvas;
    public TextMeshProUGUI vrMenuText;
    public Transform headsetCamera;
    public float menuDistance = 2f;
    public Vector3 menuOffset = new Vector3(0f, -0.15f, 0f);

    [Header("Manipulation Settings")]
    public float moveHeightOffset = 0.7f;
    public float defaultMoveDistance = 3f;
    public float scaleSpeed = 1.5f;
    public float minScale = 0.2f;
    public float maxScale = 3f;

    [Header("Highlight")]
    public Color hoverColor = Color.cyan;
    public Color selectedColor = Color.yellow;

    private InputDevice leftDevice;
    private InputDevice rightDevice;

    private bool prevRightTrigger;
    private bool prevRightGrip;
    private bool prevLeftTrigger;

    private bool selectionMode = false;
    private bool manipulationMode = false;
    private bool wasManipulating = false;

    private GameObject hoveredObject;
    private Renderer[] hoveredRenderers;

    private GameObject selectedObject;
    private Renderer[] selectedRenderers;
    private MaterialPropertyBlock[] selectedOriginalBlocks;

    private enum ManipulationMode
    {
        Move,
        Rotate,
        Scale
    }

    private ManipulationMode currentMode = ManipulationMode.Move;

    void Start()
    {
        RefreshDevices();

        if (spawnMenu == null)
            spawnMenu = FindObjectOfType<SpawnMenu>(true);

        if (rayOrigin == null)
            rayOrigin = transform;

        if (headsetCamera == null && Camera.main != null)
            headsetCamera = Camera.main.transform;

        EnsureIndicatorReferences();
        HideIndicator();

        if (vrMenuCanvas != null)
            vrMenuCanvas.SetActive(true);
    }

    void Update()
    {
        if (!leftDevice.isValid || !rightDevice.isValid)
            RefreshDevices();

        ReadButtons(
            out bool rightTriggerDown,
            out bool rightTriggerHeld,
            out bool rightTriggerReleased,
            out bool rightGripDown,
            out bool leftTriggerDown
        );

        if (!IsSelectionModeActive && spawnMenu != null && spawnMenu.IsSpawnModeActive)
        {
            UpdateVRMenu();
            return;
        }

        if (!selectionMode && !manipulationMode)
        {
            if (rightGripDown)
                StartSelectionMode();

            UpdateVRMenu();
            return;
        }

        if (leftTriggerDown)
        {
            ExitRaySelection();
            UpdateVRMenu();
            return;
        }

        if (selectionMode && !manipulationMode)
        {
            UpdateIndicator();
            UpdateHoverHighlight();

            if (rightTriggerDown && hoveredObject != null)
            {
                SelectObject(hoveredObject);
                StartManipulationMode();
            }

            UpdateVRMenu();
            return;
        }

        if (manipulationMode && selectedObject != null)
        {
            UpdateIndicator();

            if (rightGripDown)
            {
                CycleManipulationMode();
                UpdateVRMenu();
                return;
            }

            // Move happens once per trigger press
            if (rightTriggerDown && currentMode == ManipulationMode.Move)
            {
                ManipulateSelectedObject();
                wasManipulating = true;
            }

            // Rotate and scale happen while holding trigger
            if (rightTriggerHeld && currentMode != ManipulationMode.Move)
            {
                ManipulateSelectedObject();
                wasManipulating = true;
            }

            if (rightTriggerReleased && wasManipulating)
            {
                FinishManipulation();
                UpdateVRMenu();
                return;
            }

            UpdateVRMenu();
        }
    }

    private void StartSelectionMode()
    {
        selectionMode = true;
        manipulationMode = false;
        wasManipulating = false;
        currentMode = ManipulationMode.Move;
        ShowIndicator();
    }

    private void StartManipulationMode()
    {
        if (selectedObject == null)
            return;

        selectionMode = false;
        manipulationMode = true;
        wasManipulating = false;
        currentMode = ManipulationMode.Move;

        ClearHoverHighlight();
        ShowIndicator();
    }

    private void FinishManipulation()
    {
        DeselectObject();

        selectionMode = false;
        manipulationMode = false;
        wasManipulating = false;

        ClearHoverHighlight();
        HideIndicator();
    }

    private void ExitRaySelection()
    {
        if (selectedObject != null)
            DeselectObject();

        ClearHoverHighlight();

        selectionMode = false;
        manipulationMode = false;
        wasManipulating = false;

        HideIndicator();
    }

    private void UpdateVRMenu()
    {
        if (vrMenuCanvas == null || vrMenuText == null)
            return;

        vrMenuCanvas.SetActive(true);

        if (headsetCamera != null)
        {
            vrMenuCanvas.transform.position =
                headsetCamera.position +
                headsetCamera.forward * menuDistance +
                menuOffset;

            vrMenuCanvas.transform.rotation =
                Quaternion.LookRotation(vrMenuCanvas.transform.position - headsetCamera.position);
        }

        string hoverName = hoveredObject == null ? "None" : hoveredObject.name;
        string selectedName = selectedObject == null ? "None" : selectedObject.name;

        if (selectionMode)
        {
            vrMenuText.text =
                "Ray Selection Mode\n\n" +
                "Hovering: " + hoverName + "\n\n" +
                "Right Trigger = Select Object\n" +
                "Left Trigger = Cancel / Exit";
        }
        else if (manipulationMode)
        {
            vrMenuText.text =
                "Manipulation Mode\n\n" +
                "Selected: " + selectedName + "\n" +
                "Current Mode: " + currentMode + "\n\n" +
                "Move Mode: Press Right Trigger Once\n" +
                "Rotate/Scale: Hold Right Trigger\n" +
                "Right Grip = Switch Move / Rotate / Scale\n" +
                "Left Trigger = Cancel / Exit";
        }
        else
        {
            vrMenuText.text =
                "Controls\n\n" +
                "Left Trigger = Spawn\n" +
                "Left Grip = Group Selection\n" +
                "Right Grip = Ray Selection";
        }
    }

    private void EnsureIndicatorReferences()
    {
        if (lineRenderer == null)
            lineRenderer = CreateLineIndicator();

        if (hitPointVisual == null)
            hitPointVisual = CreateHitPointIndicator();
    }

    private LineRenderer CreateLineIndicator()
    {
        GameObject lineObject = new GameObject("SelectionManipulatorLine");
        lineObject.transform.SetParent(transform, false);

        LineRenderer createdLine = lineObject.AddComponent<LineRenderer>();
        createdLine.useWorldSpace = true;
        createdLine.positionCount = 2;
        createdLine.widthMultiplier = 0.01f;
        createdLine.numCapVertices = 4;
        createdLine.numCornerVertices = 4;
        createdLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        createdLine.receiveShadows = false;
        createdLine.startColor = hoverColor;
        createdLine.endColor = hoverColor;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
            createdLine.sharedMaterial = new Material(shader);

        return createdLine;
    }

    private Transform CreateHitPointIndicator()
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "SelectionManipulatorHitPoint";
        marker.transform.SetParent(transform, false);
        marker.transform.localScale = Vector3.one * 0.06f;

        Collider col = marker.GetComponent<Collider>();
        if (col != null)
            Destroy(col);

        Renderer rend = marker.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.material.color = hoverColor;
        }

        return marker.transform;
    }

    private void ShowIndicator()
    {
        if (lineRenderer != null)
            lineRenderer.enabled = true;

        if (hitPointVisual != null)
            hitPointVisual.gameObject.SetActive(false);
    }

    private void HideIndicator()
    {
        if (lineRenderer != null)
            lineRenderer.enabled = false;

        if (hitPointVisual != null)
            hitPointVisual.gameObject.SetActive(false);
    }

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

    private void UpdateHoverHighlight()
    {
        if (rayOrigin == null)
            return;

        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
        GameObject newHover = null;

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, selectableMask))
            newHover = GetMainSelectableObject(hit.collider);

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
        if (selectedObject != null)
            DeselectObject();

        selectedObject = obj;
        selectedRenderers = selectedObject.GetComponentsInChildren<Renderer>();

        SaveSelectedOriginalBlocks();

        ClearHoverHighlight();
        SetRendererColor(selectedRenderers, selectedColor);
    }

    private void DeselectObject()
    {
        RestoreSelectedOriginalBlocks();

        selectedObject = null;
        selectedRenderers = null;
        selectedOriginalBlocks = null;
    }

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

            block.SetColor("_Color", color);
            block.SetColor("_BaseColor", color);

            rend.SetPropertyBlock(block);
        }
    }

    private void CycleManipulationMode()
    {
        // Stop current manipulation when switching modes
        wasManipulating = false;

        if (currentMode == ManipulationMode.Move)
            currentMode = ManipulationMode.Rotate;
        else if (currentMode == ManipulationMode.Rotate)
            currentMode = ManipulationMode.Scale;
        else
            currentMode = ManipulationMode.Move;
    }

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
        Vector3 targetPosition;

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance))
        {
            targetPosition = hit.point;
        }
        else
        {
            targetPosition = rayOrigin.position + rayOrigin.forward * defaultMoveDistance;
        }

        selectedObject.transform.position = targetPosition + Vector3.up * moveHeightOffset;
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

        if (rayOrigin.forward.y > 0.15f)
            currentScale += Vector3.one * scaleSpeed * Time.deltaTime;
        else if (rayOrigin.forward.y < -0.15f)
            currentScale -= Vector3.one * scaleSpeed * Time.deltaTime;

        currentScale.x = Mathf.Clamp(currentScale.x, minScale, maxScale);
        currentScale.y = Mathf.Clamp(currentScale.y, minScale, maxScale);
        currentScale.z = Mathf.Clamp(currentScale.z, minScale, maxScale);

        selectedObject.transform.localScale = currentScale;
    }

    private void RefreshDevices()
    {
        leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    private void ReadButtons(
        out bool rightTriggerDown,
        out bool rightTriggerHeld,
        out bool rightTriggerReleased,
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
        rightTriggerReleased = !rightTrigger && prevRightTrigger;

        rightGripDown = rightGrip && !prevRightGrip;
        leftTriggerDown = leftTrigger && !prevLeftTrigger;

        prevRightTrigger = rightTrigger;
        prevRightGrip = rightGrip;
        prevLeftTrigger = leftTrigger;
    }
}