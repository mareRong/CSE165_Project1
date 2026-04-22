using UnityEngine;
using UnityEngine.XR;

public class SpawnMenu : MonoBehaviour
{
    [Header("Menu")]
    public bool spawnModeEnabled = true;
    public GameObject[] spawnPrefabs;

    [Header("Spawning")]
    public Transform rayOrigin;
    public float maxRayDistance = 20f;
    public LayerMask placementMask = ~0;

    [Header("Adjustment")]
    public float placementStep = 0.2f;
    public float rotationStep = 15f;

    [Header("Debug")]
    public bool showMenuOnScreen = true;

    private InputDevice leftDevice;
    private InputDevice rightDevice;

    private bool prevLeftTrigger;
    private bool prevRightTrigger;
    private bool prevLeftGrip;
    private bool prevRightGrip;

    private int selectedIndex = 0;

    private bool menuOpen = false;
    private bool placementMode = false;
    private bool orientationMode = false;

    private GameObject previewObject;

    private float placementOffset = 0f;
    private float currentYRotation = 0f;

    void Start()
    {
        RefreshDevices();

        if (rayOrigin == null)
            rayOrigin = transform;
    }

    void Update()
    {
        if (!leftDevice.isValid || !rightDevice.isValid)
            RefreshDevices();

        if (!spawnModeEnabled)
        {
            menuOpen = false;
            placementMode = false;
            orientationMode = false;

            if (previewObject != null)
                Destroy(previewObject);

            return;
        }

        ReadButtons(out bool leftTriggerDown, out bool rightTriggerDown,
                    out bool leftGripDown, out bool rightGripDown);

        if (spawnPrefabs == null || spawnPrefabs.Length == 0)
            return;

        // Preview follows raycast
        if ((placementMode || orientationMode) && previewObject != null)
        {
            Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, maxRayDistance, placementMask))
            {
                Vector3 pos = hit.point + rayOrigin.forward * placementOffset;
                previewObject.transform.position = pos;
                previewObject.transform.rotation = Quaternion.Euler(0, currentYRotation, 0);
            }
        }

        // Open menu
        if (!menuOpen && !placementMode && !orientationMode)
        {
            if (rightTriggerDown)
            {
                menuOpen = true;
                Debug.Log("Menu opened");
            }
            return;
        }

        // Menu mode
        if (menuOpen)
        {
            if (leftGripDown)
                selectedIndex = (selectedIndex - 1 + spawnPrefabs.Length) % spawnPrefabs.Length;

            if (rightGripDown)
                selectedIndex = (selectedIndex + 1) % spawnPrefabs.Length;

            if (rightTriggerDown)
            {
                menuOpen = false;
                placementMode = true;

                placementOffset = 0f;
                currentYRotation = 0f;

                if (previewObject != null)
                    Destroy(previewObject);

                previewObject = Instantiate(spawnPrefabs[selectedIndex]);

                Rigidbody rb = previewObject.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.useGravity = false;
                    rb.isKinematic = true;
                }
            }

            if (leftTriggerDown)
                menuOpen = false;
        }

        // Placement mode
        else if (placementMode)
        {
            if (leftGripDown)
                placementOffset -= placementStep;

            if (rightGripDown)
                placementOffset += placementStep;

            if (rightTriggerDown)
            {
                placementMode = false;
                orientationMode = true;
            }

            if (leftTriggerDown)
            {
                Destroy(previewObject);
                placementMode = false;
                menuOpen = true;
            }
        }

        // Orientation mode
        else if (orientationMode)
        {
            if (leftGripDown)
                currentYRotation -= rotationStep;

            if (rightGripDown)
                currentYRotation += rotationStep;

            if (rightTriggerDown)
            {
                Rigidbody rb = previewObject.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.useGravity = true;
                    rb.isKinematic = false;
                }

                previewObject = null;
                orientationMode = false;
            }

            if (leftTriggerDown)
            {
                orientationMode = false;
                placementMode = true;
            }
        }
    }

    private void RefreshDevices()
    {
        leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    private void ReadButtons(out bool leftTriggerDown, out bool rightTriggerDown,
                             out bool leftGripDown, out bool rightGripDown)
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

        prevLeftTrigger = leftTrigger;
        prevRightTrigger = rightTrigger;
        prevLeftGrip = leftGrip;
        prevRightGrip = rightGrip;
    }

    void OnGUI()
    {
        if (!showMenuOnScreen || spawnPrefabs == null || spawnPrefabs.Length == 0)
            return;

        float scale = 3f;
        Vector3 pivot = new Vector3(Screen.width / 2f, Screen.height / 2f, 0);

        GUI.matrix =
            Matrix4x4.TRS(pivot, Quaternion.identity, Vector3.one) *
            Matrix4x4.Scale(new Vector3(scale, scale, 1)) *
            Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);

        float w = 400;
        float h = 180;
        float x = (Screen.width - w) / 2f;
        float y = (Screen.height - h) / 2f;

        if (menuOpen)
        {
            GUI.Box(new Rect(x, y, w, h),
                "Spawn Menu\n\n" +
                "Selected: " + spawnPrefabs[selectedIndex].name + "\n\n" +
                "Left Grip = Previous | Right Grip = Next\n" +
                "Right Trigger = Select\nLeft Trigger = Close");
        }
        else if (placementMode)
        {
            GUI.Box(new Rect(x, y, w, h),
                "Placement Mode\n\n" +
                "Left/Right Grip = Move\n" +
                "Right Trigger = Orientation\nLeft Trigger = Cancel");
        }
        else if (orientationMode)
        {
            GUI.Box(new Rect(x, y, w, h),
                "Orientation Mode\n\n" +
                "Left/Right Grip = Rotate\n" +
                "Right Trigger = Spawn\nLeft Trigger = Back");
        }
    }
}