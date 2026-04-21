using UnityEngine;
using UnityEngine.XR;

public class SpawnMenu : MonoBehaviour
{
    [Header("Menu")]
    public bool spawnModeEnabled = true;
    public GameObject[] spawnPrefabs;

    [Header("Debug")]
    public bool keyboardDebug = true;
    public bool showMenuOnScreen = true;

    private InputDevice leftDevice;
    private InputDevice rightDevice;

    private bool prevLeftTrigger;
    private bool prevRightTrigger;
    private bool prevLeftGrip;
    private bool prevRightGrip;

    private int selectedIndex = 0;
    private bool menuOpen = false;

    public GameObject SelectedPrefab
    {
        get
        {
            if (spawnPrefabs == null || spawnPrefabs.Length == 0)
                return null;

            return spawnPrefabs[selectedIndex];
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        RefreshDevices();
    }

    
    // Update is called once per frame
    void Update()
    {
        if (!leftDevice.isValid || !rightDevice.isValid)
            RefreshDevices();

        if (!spawnModeEnabled)
        {
            menuOpen = false;
            return;
        }

        ReadButtons(out bool leftTriggerDown, out bool rightTriggerDown,
                    out bool leftGripDown, out bool rightGripDown);

        if (!menuOpen)
        {
            if (rightTriggerDown)
            {
                menuOpen = true;
                Debug.Log("Menu opened");
            }

            return;
        }
            

        if (spawnPrefabs == null || spawnPrefabs.Length == 0)
            return;

        // Left grip = previous item
        if (leftGripDown)
        {
            selectedIndex = (selectedIndex - 1 + spawnPrefabs.Length) % spawnPrefabs.Length;
            Debug.Log("Selected: " + spawnPrefabs[selectedIndex].name);
        }

        // Right grip = next item
        if (rightGripDown)
        {
            selectedIndex = (selectedIndex + 1) % spawnPrefabs.Length;
            Debug.Log("Selected: " + spawnPrefabs[selectedIndex].name);
        }

        // Right trigger = confirm selection
        if (rightTriggerDown)
        {
            Debug.Log("Confirmed spawn object: " + spawnPrefabs[selectedIndex].name);
            // another script can read SelectedPrefab and start placement
            menuOpen = false;
        }

        // Left trigger = cancel / close menu
        if (leftTriggerDown)
        {
            menuOpen = false;
            Debug.Log("Spawn menu closed");
        }
    }

      public void OpenMenu()
    {
        if (spawnPrefabs == null || spawnPrefabs.Length == 0)
            return;

        menuOpen = true;
        Debug.Log("Spawn menu opened. Selected: " + spawnPrefabs[selectedIndex].name);
    }

    public void CloseMenu()
    {
        menuOpen = false;
    }

    public bool IsMenuOpen()
    {
        return menuOpen;
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

        // keyboard fallback for testing
        if (keyboardDebug)
        {
            leftTrigger |= Input.GetKey(KeyCode.Z);
            rightTrigger |= Input.GetKey(KeyCode.C);
            leftGrip |= Input.GetKey(KeyCode.X);
            rightGrip |= Input.GetKey(KeyCode.V);
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

        // --- Center pivot ---
        Vector3 pivot = new Vector3(Screen.width / 2f, Screen.height / 2f, 0);

        GUI.matrix =
            Matrix4x4.TRS(pivot, Quaternion.identity, Vector3.one) *
            Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f)) *
            Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);

        // --- Box size ---
        float boxWidth = 400;
        float boxHeight = 180;

    
        float x = (Screen.width - boxWidth) / 2f;
        float y = (Screen.height - boxHeight) / 2f;

    //Menu display
        if (menuOpen)
        {
            GUI.Box(
                new Rect(x, y, boxWidth, boxHeight),
                "Spawn Menu\n\n" +
                "Selected: " + spawnPrefabs[selectedIndex].name + "\n\n" +
                "X / Left Grip: Previous\n" +
                "V / Right Grip: Next\n" +
                "C / Right Trigger: Confirm\n" +
                "Z / Left Trigger: Close"
            );
        }
    }
}
