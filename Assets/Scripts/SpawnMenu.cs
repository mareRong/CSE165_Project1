using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;
using System.Text;

// Main class handling spawning system, menu, and VR interaction
public class SpawnMenu : MonoBehaviour
{
    // ===================== MENU SETTINGS =====================
    [Header("Menu")]
    public bool spawnModeEnabled = true;
    public GameObject[] spawnPrefabs;

    // ===================== RAYCAST SETTINGS =====================
    [Header("Ray")]
    public Transform rayOrigin;
    public float maxRayDistance = 20f;
    public LayerMask placementMask = ~0;

    // ===================== DEBUG GUI SETTINGS =====================
    [Header("Menu Display")]
    public bool showMenuOnScreen = true;
    public float menuScale = 1.2f;
    public float menuScreenWidth = 260f;

    // ===================== MULTI SPAWN SETTINGS =====================
    [Header("Multi Spawn")]
    public float multiSpawnSpacing = 0.8f;

    // ===================== INPUT DEVICES =====================
    private InputDevice leftDevice;
    private InputDevice rightDevice;

    // Previous frame button states (for detecting presses)
    private bool prevLeftTrigger;
    private bool prevRightTrigger;
    private bool prevLeftGrip;
    private bool prevRightGrip;

    // Current trigger hold state
    private bool rightTriggerHeld;

    // ===================== MENU STATE =====================
    private int selectedIndex = 0;

    private bool menuOpen = false;
    private bool placementMode = false;
    private bool orientationMode = false;

    // ===================== SPAWN OBJECTS =====================
    private GameObject selectedPrefab;
    private GameObject previewObject;

    // Multi-selection list
    private List<GameObject> multiSelectedPrefabs = new List<GameObject>();

    // Rotation tracking
    private float currentYRotation = 0f;

    // ===================== PLACEMENT SETTINGS =====================
    [Header("Placement Offset")]
    public float spawnHeightOffset = 0.7f;

    [Header("Spawn Constraints")]
    public float maxSpawnRange = 5f;

    // ===================== INITIALIZATION =====================
    void Start()
    {
        RefreshDevices();

        if (rayOrigin == null)
            rayOrigin = transform;
    }

    // ===================== MAIN UPDATE LOOP =====================
    void Update()
    {
        // Reconnect controllers if needed
        if (!leftDevice.isValid || !rightDevice.isValid)
            RefreshDevices();

        // Disable everything if spawn mode is off
        if (!spawnModeEnabled)
        {
            ResetModes();
            return;
        }

        // No prefabs → do nothing
        if (spawnPrefabs == null || spawnPrefabs.Length == 0)
            return;

        // Read controller inputs
        ReadButtons(
            out bool leftTriggerDown,
            out bool rightTriggerDown,
            out bool leftGripDown,
            out bool rightGripDown
        );

        // Open menu from idle
        if (!menuOpen && !placementMode && !orientationMode)
        {
            if (leftGripDown)
                menuOpen = true;

            return;
        }

        // Handle different modes
        if (menuOpen)
        {
            HandleMenuMode(leftTriggerDown, rightTriggerDown, leftGripDown, rightGripDown);
        }
        else if (placementMode)
        {
            HandlePlacementMode(leftTriggerDown, rightTriggerDown);
        }
        else if (orientationMode)
        {
            HandleOrientationMode(leftTriggerDown, rightGripDown);
        }
    }

    // ===================== MENU MODE =====================
    private void HandleMenuMode(bool leftTriggerDown, bool rightTriggerDown, bool leftGripDown, bool rightGripDown)
    {
        // Cycle through prefabs
        if (leftTriggerDown)
            selectedIndex = (selectedIndex - 1 + spawnPrefabs.Length) % spawnPrefabs.Length;

        if (rightTriggerDown)
            selectedIndex = (selectedIndex + 1) % spawnPrefabs.Length;

        // Toggle multi-selection
        if (rightGripDown)
            ToggleMultiSelect(spawnPrefabs[selectedIndex]);

        // Start placement
        if (leftGripDown)
        {
            if (multiSelectedPrefabs.Count > 0)
                StartMultiPlacement();
            else
                StartSinglePlacement(spawnPrefabs[selectedIndex]);
        }
    }

    // ===================== PLACEMENT MODE =====================
    private void HandlePlacementMode(bool leftTriggerDown, bool rightTriggerDown)
    {
        // Update preview position with raycast
        UpdatePreviewPosition();

        // Move to orientation mode
        if (rightTriggerDown)
        {
            placementMode = false;
            orientationMode = true;
            currentYRotation = GetControllerYaw();
        }

        // Cancel placement
        if (leftTriggerDown)
        {
            CancelPreview();
            menuOpen = true;
        }
    }

    // ===================== ORIENTATION MODE =====================
    private void HandleOrientationMode(bool leftTriggerDown, bool rightGripDown)
    {
        // Rotate object based on controller direction
        if (rightTriggerHeld)
        {
            currentYRotation = GetControllerYaw();

            if (previewObject != null)
                previewObject.transform.rotation = Quaternion.Euler(0f, currentYRotation, 0f);
        }

        // Confirm spawn
        if (rightGripDown)
            ConfirmSpawn();

        // Go back to placement
        if (leftTriggerDown)
        {
            orientationMode = false;
            placementMode = true;
        }
    }

    // ===================== SINGLE OBJECT SPAWN =====================
    private void StartSinglePlacement(GameObject prefab)
    {
        selectedPrefab = prefab;

        if (previewObject != null)
            Destroy(previewObject);

        previewObject = Instantiate(selectedPrefab);
        PreparePreviewObject(previewObject);

        menuOpen = false;
        placementMode = true;
    }

    // ===================== MULTI OBJECT SPAWN =====================
    private void StartMultiPlacement()
    {
        if (previewObject != null)
            Destroy(previewObject);

        previewObject = new GameObject("Multi Spawn Preview");

        for (int i = 0; i < multiSelectedPrefabs.Count; i++)
        {
            GameObject child = Instantiate(multiSelectedPrefabs[i], previewObject.transform);

            float xOffset = (i - (multiSelectedPrefabs.Count - 1) / 2f) * multiSpawnSpacing;
            child.transform.localPosition = new Vector3(xOffset, 0f, 0f);
            child.transform.localRotation = Quaternion.identity;

            PreparePreviewObject(child);
        }

        menuOpen = false;
        placementMode = true;
    }

    // ===================== PREVIEW POSITION =====================
    private void UpdatePreviewPosition()
    {
        if (previewObject == null || rayOrigin == null)
            return;

        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);

        Vector3 targetPoint;

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, placementMask))
        {
            targetPoint = hit.point;
        }
        else
        {
            targetPoint = rayOrigin.position + rayOrigin.forward * maxSpawnRange;
        }

        Vector3 fromPlayer = targetPoint - rayOrigin.position;

        // Clamp distance to stay within range
        if (fromPlayer.magnitude > maxSpawnRange)
        {
            fromPlayer = fromPlayer.normalized * maxSpawnRange;
            targetPoint = rayOrigin.position + fromPlayer;
        }

        previewObject.SetActive(true);
        previewObject.transform.position = targetPoint + Vector3.up * spawnHeightOffset;
        previewObject.transform.rotation = Quaternion.Euler(0f, currentYRotation, 0f);
    }

    // ===================== CONFIRM SPAWN =====================
    private void ConfirmSpawn()
    {
        if (previewObject == null)
            return;

        // Re-enable colliders
        Collider[] colliders = previewObject.GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
            col.enabled = true;

        // Re-enable physics
        Rigidbody[] bodies = previewObject.GetComponentsInChildren<Rigidbody>();
        foreach (Rigidbody rb in bodies)
        {
            rb.useGravity = true;
            rb.isKinematic = false;
        }

        ConfigureSpawnedObject(previewObject);

        previewObject = null;
        selectedPrefab = null;

        multiSelectedPrefabs.Clear();

        orientationMode = false;
        placementMode = false;
        menuOpen = false;
    }

    // ===================== HELPERS =====================
    private void ToggleMultiSelect(GameObject prefab)
    {
        if (multiSelectedPrefabs.Contains(prefab))
            multiSelectedPrefabs.Remove(prefab);
        else
            multiSelectedPrefabs.Add(prefab);
    }

    private void ConfigureSpawnedObject(GameObject obj)
    {
        if (obj == null)
            return;

        // Multi-spawn previews use an empty parent, so configure each spawned child instead.
        if (obj.transform.childCount > 0 && obj.GetComponent<Renderer>() == null && obj.GetComponent<Collider>() == null)
        {
            for (int i = 0; i < obj.transform.childCount; i++)
                RuntimeInteractableSetup.ConfigureInteractableHierarchy(obj.transform.GetChild(i).gameObject);

            return;
        }

        RuntimeInteractableSetup.ConfigureInteractableHierarchy(obj);
    }

    private void PreparePreviewObject(GameObject obj)
    {
        // Disable physics for preview
        Rigidbody[] bodies = obj.GetComponentsInChildren<Rigidbody>();
        foreach (Rigidbody rb in bodies)
        {
            rb.useGravity = false;
            rb.isKinematic = true;
        }

        // Disable collisions for preview
        Collider[] colliders = obj.GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
            col.enabled = false;
    }

    private float GetControllerYaw()
    {
        if (rayOrigin == null)
            return currentYRotation;

        Vector3 flatForward = Vector3.ProjectOnPlane(rayOrigin.forward, Vector3.up);

        if (flatForward.sqrMagnitude < 0.001f)
            return currentYRotation;

        return Quaternion.LookRotation(flatForward.normalized, Vector3.up).eulerAngles.y;
    }

    private void CancelPreview()
    {
        if (previewObject != null)
            Destroy(previewObject);

        previewObject = null;
        selectedPrefab = null;

        placementMode = false;
        orientationMode = false;
    }

    private void ResetModes()
    {
        menuOpen = false;
        placementMode = false;
        orientationMode = false;

        if (previewObject != null)
            Destroy(previewObject);

        previewObject = null;
    }

    // ===================== INPUT =====================
    private void RefreshDevices()
    {
        leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    private void ReadButtons(
        out bool leftTriggerDown,
        out bool rightTriggerDown,
        out bool leftGripDown,
        out bool rightGripDown
    )
    {
        bool leftTrigger = false;
        bool rightTrigger = false;
        bool leftGrip = false;
        bool rightGrip = false;

        if (leftDevice.isValid)
        {
            leftDevice.TryGetFeatureValue(CommonUsages.triggerButton, out leftTrigger);
            leftDevice.TryGetFeatureValue(CommonUsages.gripButton, out leftGrip);
        }

        if (rightDevice.isValid)
        {
            rightDevice.TryGetFeatureValue(CommonUsages.triggerButton, out rightTrigger);
            rightDevice.TryGetFeatureValue(CommonUsages.gripButton, out rightGrip);
        }

        leftTriggerDown = leftTrigger && !prevLeftTrigger;
        rightTriggerDown = rightTrigger && !prevRightTrigger;
        leftGripDown = leftGrip && !prevLeftGrip;
        rightGripDown = rightGrip && !prevRightGrip;

        rightTriggerHeld = rightTrigger;

        prevLeftTrigger = leftTrigger;
        prevRightTrigger = rightTrigger;
        prevLeftGrip = leftGrip;
        prevRightGrip = rightGrip;
    }

    // ===================== DEBUG GUI =====================
    void OnGUI()
    {
        if (!showMenuOnScreen || spawnPrefabs == null || spawnPrefabs.Length == 0)
            return;

        float scale = menuScale;
        Vector3 pivot = new Vector3(Screen.width / 2f, Screen.height / 2f, 0);

        GUI.matrix =
            Matrix4x4.TRS(pivot, Quaternion.identity, Vector3.one) *
            Matrix4x4.Scale(new Vector3(scale, scale, 1)) *
            Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);

        float w = menuScreenWidth;
        float h = menuOpen ? Mathf.Max(220f, 150f + spawnPrefabs.Length * 24f) : 160f;
        float x = (Screen.width - w) / 2f;
        float y = (Screen.height - h) / 2f;

        if (menuOpen)
        {
            GUI.Box(new Rect(x, y, w, h),
                "Spawn Menu\n\n" +
                BuildMenuText() + "\n" +
                "Left/Right Trigger = Change Prefab\n" +
                "Right Grip = Add/Remove Multi-Select\n" +
                "Left Grip = Start Placement");
        }
        else if (placementMode)
        {
            GUI.Box(new Rect(x, y, w, h),
                "Placement Mode\n\n" +
                "Preview follows raycast hit point\n" +
                "Right Trigger = Orientation\n" +
                "Left Trigger = Cancel");
        }
        else if (orientationMode)
        {
            GUI.Box(new Rect(x, y, w, h),
                "Orientation Mode\n\n" +
                "Hold Right Trigger + Tilt Controller = Rotate\n" +
                "Right Grip = Confirm Spawn\n" +
                "Left Trigger = Back");
        }
    }

    // ===================== BUILD MENU TEXT =====================
    private string BuildMenuText()
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine("All Spawnable Prefabs:");

        for (int i = 0; i < spawnPrefabs.Length; i++)
        {
            bool isSelected = i == selectedIndex;
            bool isMultiSelected = multiSelectedPrefabs.Contains(spawnPrefabs[i]);

            string cursor = isSelected ? "> " : "  ";
            string check = isMultiSelected ? "[X] " : "[ ] ";

            builder.AppendLine(cursor + check + spawnPrefabs[i].name);
        }

        return builder.ToString();
    }
}
