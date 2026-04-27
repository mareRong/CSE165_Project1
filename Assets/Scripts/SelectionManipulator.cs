using UnityEngine;
using UnityEngine.XR;
using TMPro;

public class SelectionManipulator : MonoBehaviour
{
    public bool IsSelectionModeActive => selectionMode || manipulationMode;
    private float hoverBlockTimer = 0f;

    public SpawnMenu spawnMenu;
    public GroupSelectionManipulationVR groupSelectionMenu;

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
    private bool prevLeftGrip;

    private bool selectionMode = false;
    private bool manipulationMode = false;

    private GameObject hoveredObject;
    private Renderer[] hoveredRenderers;

    private GameObject selectedObject;
    private Renderer[] selectedRenderers;
    private MaterialPropertyBlock[] selectedOriginalBlocks;

    private float grabDistance = 3f;

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

        if (groupSelectionMenu == null)
            groupSelectionMenu = FindObjectOfType<GroupSelectionManipulationVR>(true);

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
            out bool rightGripDown,
            out bool leftTriggerDown,
            out bool leftGripDown
        );

        bool groupModeActive = groupSelectionMenu != null && groupSelectionMenu.IsGroupModeActive;

        if (!IsSelectionModeActive &&
            ((spawnMenu != null && spawnMenu.IsSpawnModeActive) || groupModeActive))
        {
            HideIndicator();
            UpdateVRMenu();
            return;
        }

        // Idle: Right Grip starts ray selection
        if (!selectionMode && !manipulationMode)
        {
            HideIndicator();

            if (spawnMenu != null && spawnMenu.IsSuppressingOtherModeEntry)
            {
                UpdateVRMenu();
                return;
            }

            if (rightGripDown)
                StartSelectionMode();

            UpdateVRMenu();
            return;
        }

        // Left Trigger exits ray selection / manipulation
        if (leftTriggerDown)
        {
            ExitRaySelection();
            UpdateVRMenu();
            return;
        }

        // Ray selection mode
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

        // Manipulation mode
        if (manipulationMode && selectedObject != null)
        {
            UpdateIndicator();

            // Right Grip cycles Move -> Rotate -> Scale
            if (rightGripDown)
            {
                CycleManipulationMode();
                UpdateVRMenu();
                return;
            }

            // Hold Right Trigger to manipulate.
            // Releasing trigger does NOT exit manipulation mode.
            if (rightTriggerHeld)
            {
                ManipulateSelectedObject();
            }

            UpdateVRMenu();
        }
    }

    private void StartSelectionMode()
    {
        selectionMode = true;
        manipulationMode = false;
        currentMode = ManipulationMode.Move;
        ShowIndicator();
        ShowRayMenuCanvas();
        UpdateVRMenu();
    }

    private void StartManipulationMode()
    {
        if (selectedObject == null)
            return;

        selectionMode = false;
        manipulationMode = true;
        currentMode = ManipulationMode.Move;

        grabDistance = Vector3.Distance(rayOrigin.position, selectedObject.transform.position);
        grabDistance = Mathf.Clamp(grabDistance, 0.5f, maxRayDistance);

        ClearHoverHighlight();
        ShowIndicator();
        ShowRayMenuCanvas();
        UpdateVRMenu();
    }

    private void ExitRaySelection()
    {
        if (selectedObject != null)
            DeselectObject();

        ClearHoverHighlight();

        selectionMode = false;
        manipulationMode = false;

        hoverBlockTimer = 0.2f;

        HideIndicator();
    }

    private void ShowRayMenuCanvas()
    {
        if (vrMenuCanvas != null)
            vrMenuCanvas.SetActive(true);
    }

    private void HideRayMenuCanvas()
    {
        // Shared with other menus; don't hide the canvas globally here.
    }

    private void UpdateVRMenu()
    {
        if (vrMenuCanvas == null || vrMenuText == null)
            return;

        if (!selectionMode && !manipulationMode)
            return;

        ShowRayMenuCanvas();

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
                "Left Trigger = Back";
        }
        else if (manipulationMode)
        {
            vrMenuText.text =
                "Manipulation Mode\n\n" +
                "Selected: " + selectedName + "\n" +
                "Current Mode: " + currentMode + "\n\n" +
                "Hold Right Trigger = Manipulate\n" +
                "Right Grip = Switch Move / Rotate / Scale\n" +
                "Left Trigger = Back";
        }
        else
        {
            return;
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
        if (hoverBlockTimer > 0f)
        {
            hoverBlockTimer -= Time.deltaTime;
            return;
        }
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
            MoveObjectWithRay();
        else if (currentMode == ManipulationMode.Rotate)
            RotateObjectWithRay();
        else if (currentMode == ManipulationMode.Scale)
            ScaleObjectUpward();
    }

    private void MoveObjectWithRay()
    {
        if (rayOrigin == null || selectedObject == null)
            return;

        Vector3 targetPosition =
            rayOrigin.position + rayOrigin.forward * grabDistance;

        selectedObject.transform.position = targetPosition;
    }

    private void RotateObjectWithRay()
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

    private void ScaleObjectUpward()
    {
        if (rayOrigin == null || selectedObject == null)
            return;

        Bounds beforeBounds = GetObjectBounds(selectedObject);
        float bottomYBefore = beforeBounds.min.y;

        Vector3 currentScale = selectedObject.transform.localScale;

        if (rayOrigin.forward.y > 0.15f)
            currentScale += Vector3.one * scaleSpeed * Time.deltaTime;
        else if (rayOrigin.forward.y < -0.15f)
            currentScale -= Vector3.one * scaleSpeed * Time.deltaTime;

        currentScale.x = Mathf.Clamp(currentScale.x, minScale, maxScale);
        currentScale.y = Mathf.Clamp(currentScale.y, minScale, maxScale);
        currentScale.z = Mathf.Clamp(currentScale.z, minScale, maxScale);

        selectedObject.transform.localScale = currentScale;

        Bounds afterBounds = GetObjectBounds(selectedObject);
        float bottomYAfter = afterBounds.min.y;

        float yCorrection = bottomYBefore - bottomYAfter;
        selectedObject.transform.position += new Vector3(0f, yCorrection, 0f);
    }

    private Bounds GetObjectBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
            return new Bounds(obj.transform.position, Vector3.zero);

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    private void RefreshDevices()
    {
        leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    private void ReadButtons(
        out bool rightTriggerDown,
        out bool rightTriggerHeld,
        out bool rightGripDown,
        out bool leftTriggerDown,
        out bool leftGripDown
    )
    {
        bool rightTrigger = false;
        bool rightGrip = false;
        bool leftTrigger = false;
        bool leftGrip = false;

        if (rightDevice.isValid)
        {
            rightDevice.TryGetFeatureValue(CommonUsages.triggerButton, out rightTrigger);
            rightDevice.TryGetFeatureValue(CommonUsages.gripButton, out rightGrip);
        }

        if (leftDevice.isValid)
        {
            leftDevice.TryGetFeatureValue(CommonUsages.triggerButton, out leftTrigger);
            leftDevice.TryGetFeatureValue(CommonUsages.gripButton, out leftGrip);
        }

        rightTriggerDown = rightTrigger && !prevRightTrigger;
        rightTriggerHeld = rightTrigger;
        rightGripDown = rightGrip && !prevRightGrip;
        leftTriggerDown = leftTrigger && !prevLeftTrigger;
        leftGripDown = leftGrip && !prevLeftGrip;

        prevRightTrigger = rightTrigger;
        prevRightGrip = rightGrip;
        prevLeftTrigger = leftTrigger;
        prevLeftGrip = leftGrip;
    }
}
