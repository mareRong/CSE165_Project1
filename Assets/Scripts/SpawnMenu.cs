using UnityEngine;
using UnityEngine.XR;
using System.Text;

public class SpawnMenu : MonoBehaviour
{
    [Header("Menu")]
    public bool spawnModeEnabled = true;
    public GameObject[] spawnPrefabs;

    [Header("Menu Ray Hover")]
    public Transform menuAnchor;
    public float menuDistance = 0.65f;
    public float menuWidth = 0.34f;
    public float menuRowHeight = 0.05f;
    public float menuHeaderHeight = 0.06f;
    public float menuHorizontalOffset = -0.08f;

    [Header("Spawning")]
    public Transform rayOrigin;
    public float maxRayDistance = 20f;
    public LayerMask placementMask = ~0;

    [Header("Debug")]
    public bool showMenuOnScreen = true;
    public float menuScale = 2.4f;
    public float menuScreenWidth = 340f;
    public float menuScreenOffsetX = -80f;

    private InputDevice leftDevice;
    private InputDevice rightDevice;

    private bool prevLeftTrigger;
    private bool prevRightTrigger;
    private bool prevLeftGrip;
    private bool prevRightGrip;
    private bool rightTriggerPressed;

    private int selectedIndex = 0;

    private bool menuOpen = false;
    private bool placementMode = false;
    private bool orientationMode = false;

    private GameObject previewObject;

    private float currentYRotation = 0f;

    void Start()
    {
        RefreshDevices();

        if (rayOrigin == null)
            rayOrigin = transform;

        if (menuAnchor == null && Camera.main != null)
            menuAnchor = Camera.main.transform;
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

        // Preview follows the raycast hit directly.
        if ((placementMode || orientationMode) && previewObject != null)
        {
            Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, maxRayDistance, placementMask))
            {
                Vector3 pos = hit.point;
                previewObject.transform.position = pos;
                previewObject.transform.rotation = Quaternion.Euler(0, currentYRotation, 0);
            }
        }

        // Open menu from idle with the left grip.
        if (!menuOpen && !placementMode && !orientationMode)
        {
            if (leftGripDown)
            {
                menuOpen = true;
                Debug.Log("Menu opened");
            }
            return;
        }

        // Menu mode
        if (menuOpen)
        {
            UpdateMenuSelectionFromRay();

            if (leftTriggerDown)
                selectedIndex = (selectedIndex - 1 + spawnPrefabs.Length) % spawnPrefabs.Length;

            if (rightTriggerDown)
                selectedIndex = (selectedIndex + 1) % spawnPrefabs.Length;

            if (rightGripDown)
            {
                menuOpen = false;
                placementMode = true;

                currentYRotation = GetControllerYaw();

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

            if (leftGripDown)
                menuOpen = false;
        }

        // Placement mode
        else if (placementMode)
        {
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
            if (rightTriggerPressed)
                UpdateRotationFromController();

            if (rightGripDown)
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

        if (menuAnchor == null && Camera.main != null)
            menuAnchor = Camera.main.transform;
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

        rightTriggerPressed = rightTrigger;
        prevLeftTrigger = leftTrigger;
        prevRightTrigger = rightTrigger;
        prevLeftGrip = leftGrip;
        prevRightGrip = rightGrip;
    }

    private void UpdateRotationFromController()
    {
        currentYRotation = GetControllerYaw();

        if (previewObject != null)
            previewObject.transform.rotation = Quaternion.Euler(0f, currentYRotation, 0f);
    }

    private float GetControllerYaw()
    {
        if (rayOrigin == null)
            return currentYRotation;

        Vector3 planarForward = Vector3.ProjectOnPlane(rayOrigin.forward, Vector3.up);

        if (planarForward.sqrMagnitude < 0.0001f)
            planarForward = Vector3.ProjectOnPlane(rayOrigin.up, Vector3.up);

        if (planarForward.sqrMagnitude < 0.0001f)
            return currentYRotation;

        return Quaternion.LookRotation(planarForward.normalized, Vector3.up).eulerAngles.y;
    }

    private void UpdateMenuSelectionFromRay()
    {
        if (rayOrigin == null || spawnPrefabs == null || spawnPrefabs.Length == 0)
            return;

        Transform anchor = menuAnchor != null ? menuAnchor : Camera.main != null ? Camera.main.transform : null;
        if (anchor == null)
            return;

        Vector3 menuCenter = anchor.position + anchor.forward * menuDistance + anchor.right * menuHorizontalOffset;
        Plane menuPlane = new Plane(anchor.forward, menuCenter);
        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);

        if (!menuPlane.Raycast(ray, out float enter))
            return;

        Vector3 hitPoint = ray.GetPoint(enter);
        Vector3 localPoint = hitPoint - menuCenter;

        float x = Vector3.Dot(localPoint, anchor.right);
        float y = Vector3.Dot(localPoint, anchor.up);

        float halfWidth = menuWidth * 0.5f;
        if (Mathf.Abs(x) > halfWidth)
            return;

        float listHeight = spawnPrefabs.Length * menuRowHeight;
        float top = listHeight * 0.5f;
        float bottom = -top;

        if (y > top + menuHeaderHeight || y < bottom)
            return;

        float listY = Mathf.Clamp(top - y, 0f, Mathf.Max(0f, listHeight - 0.0001f));
        int hoveredIndex = Mathf.FloorToInt(listY / menuRowHeight);
        selectedIndex = Mathf.Clamp(hoveredIndex, 0, spawnPrefabs.Length - 1);
    }

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
        float h = menuOpen ? Mathf.Max(190f, 120f + spawnPrefabs.Length * 24f) : 150f;
        float x = (Screen.width - w) / 2f + menuScreenOffsetX;
        float y = (Screen.height - h) / 2f;

        if (menuOpen)
        {
            GUI.Box(new Rect(x, y, w, h),
                "Spawn Menu\n\n" +
                BuildSpawnListText() + "\n" +
                "Aim ray to highlight\n" +
                "Left Trigger = Previous | Right Trigger = Next\n" +
                "Right Grip = Select\nLeft Grip = Close");
        }
        else if (placementMode)
        {
            GUI.Box(new Rect(x, y, w, h),
                "Placement Mode\n\n" +
                "Preview follows the raycast hit\n" +
                "Right Trigger = Orientation\nLeft Trigger = Cancel");
        }
        else if (orientationMode)
        {
            GUI.Box(new Rect(x, y, w, h),
                "Orientation Mode\n\n" +
                "Hold Right Trigger = Aim rotation\n" +
                "Right Grip = Spawn\nLeft Trigger = Back");
        }
    }

    private string BuildSpawnListText()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Spawnable Objects:");

        for (int i = 0; i < spawnPrefabs.Length; i++)
        {
            string prefix = i == selectedIndex ? "> " : "  ";
            string suffix = i == selectedIndex ? " <" : string.Empty;
            builder.AppendLine(prefix + spawnPrefabs[i].name + suffix);
        }

        return builder.ToString();
    }
}
