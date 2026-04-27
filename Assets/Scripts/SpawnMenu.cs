using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;
using System.Text;
using TMPro;

// Main class handling spawning system, menu, and VR interaction
public class SpawnMenu : MonoBehaviour
{
    public bool IsSpawnModeActive => menuOpen || placementMode || orientationMode;
    public bool IsSuppressingOtherModeEntry => Time.time < suppressOtherModeEntryUntil;

    public GroupSelectionManipulationVR groupSelectionMenu;
    public SelectionManipulator singleSelectionMenu;

    [Header("Menu")]
    public bool spawnModeEnabled = true;
    public GameObject[] spawnPrefabs;

    [Header("Ray")]
    public Transform rayOrigin;
    public float maxRayDistance = 20f;
    public LayerMask placementMask = ~0;

    [Header("VR Menu Canvas")]
    public GameObject vrMenuCanvas;
    public TextMeshProUGUI vrMenuText;
    public Transform headsetCamera;
    public float menuDistance = 2f;
    public Vector3 menuOffset = new Vector3(0f, -0.15f, 0f);

    [Header("Multi Spawn")]
    public float multiSpawnSpacing = 1.0f;

    private InputDevice leftDevice;
    private InputDevice rightDevice;

    private bool prevLeftTrigger;
    private bool prevRightTrigger;
    private bool prevLeftGrip;
    private bool prevRightGrip;

    private bool rightTriggerHeld;

    private int selectedIndex = 0;

    private bool menuOpen = false;
    private bool placementMode = false;
    private bool orientationMode = false;

    private GameObject selectedPrefab;
    private GameObject previewObject;

    private List<GameObject> multiSelectedPrefabs = new List<GameObject>();

    private float currentYRotation = 0f;
    private float suppressOtherModeEntryUntil = 0f;

    [Header("Placement Offset")]
    public float spawnHeightOffset = 0.7f;

    [Header("Spawn Constraints")]
    public float maxSpawnRange = 5f;

    void Start()
    {
        RefreshDevices();

        if (rayOrigin == null)
            rayOrigin = transform;

        if (headsetCamera == null && Camera.main != null)
            headsetCamera = Camera.main.transform;

        if (groupSelectionMenu == null)
            groupSelectionMenu = FindObjectOfType<GroupSelectionManipulationVR>(true);

        if (singleSelectionMenu == null)
            singleSelectionMenu = FindObjectOfType<SelectionManipulator>(true);

        if (vrMenuCanvas != null)
            vrMenuCanvas.SetActive(true);
    }

    public void OpenSpawnMenuFromIdle()
    {
        if (!spawnModeEnabled || IsSpawnModeActive)
            return;

        if (spawnPrefabs == null || spawnPrefabs.Length == 0)
            return;

        menuOpen = true;
    }

    void Update()
    {
        if (!leftDevice.isValid || !rightDevice.isValid)
            RefreshDevices();

        if (groupSelectionMenu == null)
            groupSelectionMenu = FindObjectOfType<GroupSelectionManipulationVR>(true);

        if (singleSelectionMenu == null)
            singleSelectionMenu = FindObjectOfType<SelectionManipulator>(true);

        if (!spawnModeEnabled)
        {
            ResetModes();
            UpdateVRMenu();
            return;
        }

        if (spawnPrefabs == null || spawnPrefabs.Length == 0)
        {
            UpdateVRMenu();
            return;
        }

        ReadButtons(
            out bool leftTriggerDown,
            out bool rightTriggerDown,
            out bool leftGripDown,
            out bool rightGripDown
        );

        bool otherModeActive =
            (groupSelectionMenu != null && groupSelectionMenu.IsGroupModeActive) ||
            (singleSelectionMenu != null && singleSelectionMenu.IsSelectionModeActive);

        if (!IsSpawnModeActive && otherModeActive)
        {
            UpdateVRMenu();
            return;
        }

        if (!menuOpen && !placementMode && !orientationMode)
        {
            if (leftTriggerDown)
                menuOpen = true;

            UpdateVRMenu();
            return;
        }

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

        UpdateVRMenu();
    }

    private void HandleMenuMode(bool leftTriggerDown, bool rightTriggerDown, bool leftGripDown, bool rightGripDown)
    {
        if (leftTriggerDown)
        {
            ExitSpawnMode();
            return;
        }

        if (rightTriggerDown)
            selectedIndex = (selectedIndex + 1) % spawnPrefabs.Length;

        if (rightGripDown)
            ToggleMultiSelect(spawnPrefabs[selectedIndex]);

        if (leftGripDown)
        {
            if (multiSelectedPrefabs.Count > 0)
                StartMultiPlacement();
            else
                StartSinglePlacement(spawnPrefabs[selectedIndex]);
        }
    }

    private void HandlePlacementMode(bool leftTriggerDown, bool rightTriggerDown)
    {
        UpdatePreviewPosition();

        if (leftTriggerDown)
        {
            CancelPreview();
            menuOpen = true;
            return;
        }

        if (rightTriggerDown)
        {
            placementMode = false;
            orientationMode = true;
            currentYRotation = GetControllerYaw();
        }
    }

    private void HandleOrientationMode(bool leftTriggerDown, bool rightGripDown)
{
    if (leftTriggerDown)
    {
        orientationMode = false;
        placementMode = true;
        return;
    }

    if (rightTriggerHeld)
    {
        currentYRotation = GetControllerYaw();
        ApplyPreviewRotation();
    }

    if (rightGripDown)
        ConfirmSpawn();
}

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

    private void UpdatePreviewPosition()
{
    if (previewObject == null || rayOrigin == null)
        return;

    Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
    Vector3 targetPoint;

    if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, placementMask))
        targetPoint = hit.point;
    else
        targetPoint = rayOrigin.position + rayOrigin.forward * maxSpawnRange;

    Vector3 fromPlayer = targetPoint - rayOrigin.position;

    if (fromPlayer.magnitude > maxSpawnRange)
    {
        fromPlayer = fromPlayer.normalized * maxSpawnRange;
        targetPoint = rayOrigin.position + fromPlayer;
    }

    previewObject.SetActive(true);
    previewObject.transform.position = targetPoint + Vector3.up * spawnHeightOffset;

    ApplyPreviewRotation();
}
    private void ConfirmSpawn()
    {
        if (previewObject == null)
            return;

        Collider[] colliders = previewObject.GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
            col.enabled = true;

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
        suppressOtherModeEntryUntil = Time.time + 0.2f;
    }
    private void ApplyPreviewRotation()
{
    if (previewObject == null)
        return;

    Quaternion targetRotation = Quaternion.Euler(0f, currentYRotation, 0f);

    // Multi-spawn: rotate each child in place, not the parent midpoint
    if (previewObject.name == "Multi Spawn Preview")
    {
        previewObject.transform.rotation = Quaternion.identity;

        for (int i = 0; i < previewObject.transform.childCount; i++)
        {
            Transform child = previewObject.transform.GetChild(i);
            child.rotation = targetRotation;
        }
    }
    // Single spawn: rotate the object normally
    else
    {
        previewObject.transform.rotation = targetRotation;
    }
}

    private void UpdateVRMenu()
    {
        if (vrMenuCanvas == null || vrMenuText == null)
            return;

        vrMenuCanvas.SetActive(true);

        bool spawnModeActive = menuOpen || placementMode || orientationMode;
        bool groupModeActive = groupSelectionMenu != null && groupSelectionMenu.IsGroupModeActive;
        bool singleModeActive = singleSelectionMenu != null && singleSelectionMenu.IsSelectionModeActive;

        if (!spawnModeActive && (groupModeActive || singleModeActive))
            return;

        if (headsetCamera != null)
        {
            vrMenuCanvas.transform.position =
                headsetCamera.position +
                headsetCamera.forward * menuDistance +
                menuOffset;

            vrMenuCanvas.transform.rotation =
                Quaternion.LookRotation(vrMenuCanvas.transform.position - headsetCamera.position);
        }

        if (menuOpen)
        {
            vrMenuText.text =
                "Spawn Menu\n\n" +
                BuildMenuText() + "\n" +
                "Left Trigger = Cancel / Exit\n" +
                "Right Trigger = Change Prefab\n" +
                "Right Grip = Multi-Select\n" +
                "Left Grip = Start Placement";
        }
        else if (placementMode)
        {
            vrMenuText.text =
            "Placement Mode\n\n" +
            "Preview follows ray\n" +
            "Right Trigger = Rotate Mode\n" +
            "Left Trigger = Cancel Placement";
        }
        else if (orientationMode)
        {
            vrMenuText.text =
                "Orientation Mode\n\n" +
                "Hold Right Trigger = Rotate\n" +
                "Right Grip = Confirm Spawn\n" +
                "Left Trigger = Cancel Rotation";
        }
        else
        {
            vrMenuText.text =
                "Controls\n\n" +
                "Left Trigger = Spawn\n" +
                "Left Grip = Group Selection\n" +
                "Right Grip = Single Selection";
        }
    }

    private void ExitSpawnMode()
    {
        CancelPreview();

        menuOpen = false;
        placementMode = false;
        orientationMode = false;

        multiSelectedPrefabs.Clear();
        selectedPrefab = null;
    }

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

        if (obj.transform.childCount > 0 &&
            obj.GetComponent<Renderer>() == null &&
            obj.GetComponent<Collider>() == null)
        {
            for (int i = 0; i < obj.transform.childCount; i++)
                RuntimeInteractableSetup.ConfigureInteractableHierarchy(obj.transform.GetChild(i).gameObject);

            return;
        }

        RuntimeInteractableSetup.ConfigureInteractableHierarchy(obj);
    }

    private void PreparePreviewObject(GameObject obj)
    {
        Rigidbody[] bodies = obj.GetComponentsInChildren<Rigidbody>();
        foreach (Rigidbody rb in bodies)
        {
            rb.useGravity = false;
            rb.isKinematic = true;
        }

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

        if (vrMenuCanvas != null)
            vrMenuCanvas.SetActive(false);
    }

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
