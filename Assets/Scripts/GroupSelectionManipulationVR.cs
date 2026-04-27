using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

public class GroupSelectionManipulationVR : MonoBehaviour
{
    public bool IsGroupModeActive => menuOpen || manipulationMode;
    public SpawnMenu spawnMenu;
    public SelectionManipulator singleSelectionMenu;

    private enum GroupManipulationMode
    {
        Move,
        Rotate,
        Scale
    }

    private struct RigidbodyState
    {
        public bool IsKinematic;
        public bool UseGravity;
    }

    [Header("References")]
    public Transform selectionHand;
    public LayerMask selectableMask = ~0;

    [Header("Selection Query")]
    public float selectorDistance = 0.65f;
    public float selectorRadius = 1.1f;

    [Header("VR Menu Canvas")]
    public GameObject vrMenuCanvas;
    public TextMeshProUGUI vrMenuText;
    public Transform headsetCamera;
    public float menuDistance = 2f;
    public Vector3 menuOffset = new Vector3(0f, -0.15f, 0f);

    [Header("Menu Layout")]
    public int maxVisibleRows = 3;

    [Header("Manipulation")]
    public float scaleSensitivity = 1.1f;
    public float duplicateOffset = 0.35f;
    public float minObjectScale = 0.1f;
    public float maxObjectScale = 4f;

    [Header("Menu Colors")]
    public Color selectedColor = new Color(0.2f, 1f, 0.45f, 1f);

    private readonly List<SelectableObject> selectedObjects = new List<SelectableObject>();
    private readonly List<SelectableObject> menuObjects = new List<SelectableObject>();
    private readonly Dictionary<Rigidbody, RigidbodyState> rigidbodyStates = new Dictionary<Rigidbody, RigidbodyState>();

    private InputDevice leftDevice;
    private InputDevice rightDevice;
    private bool prevLeftTriggerPressed;
    private bool prevRightTriggerPressed;
    private bool prevLeftGripPressed;
    private bool prevRightGripPressed;
    private bool leftTriggerPressed;
    private bool rightTriggerPressed;
    private bool leftGripPressed;
    private bool rightGripPressed;

    private bool menuOpen;
    private int menuIndex;
    private bool manipulationMode;
    private GroupManipulationMode currentMode = GroupManipulationMode.Move;
    private Vector3 lastHandPosition;
    private Quaternion lastHandRotation;

    private void Start()
    {
        RefreshDevices();
        ResolveReferences();

        if (headsetCamera == null && Camera.main != null)
            headsetCamera = Camera.main.transform;

        if (spawnMenu == null)
            spawnMenu = FindObjectOfType<SpawnMenu>(true);

        if (singleSelectionMenu == null)
            singleSelectionMenu = FindObjectOfType<SelectionManipulator>(true);

        if (vrMenuCanvas != null)
            vrMenuCanvas.SetActive(true);
    }

    private void Update()
    {
        if (!leftDevice.isValid || !rightDevice.isValid)
            RefreshDevices();

        if (selectionHand == null)
            ResolveReferences();

        CleanupSelection();
        ReadButtons();
        UpdateMenuObjects();

        bool otherModeActive =
            (spawnMenu != null && spawnMenu.IsSpawnModeActive) ||
            (singleSelectionMenu != null && singleSelectionMenu.IsSelectionModeActive);

        if (!IsGroupModeActive && otherModeActive)
            return;

        if (manipulationMode)
        {
            HandleManipulationInput();
            UpdateVRMenu();
            return;
        }

        HandleSelectionInput();
        UpdateVRMenu();
    }

    private void HandleSelectionInput()
    {
        if (!menuOpen)
        {
            if (LeftGripDown() && menuObjects.Count > 0)
                OpenMenu();

            return;
        }

        if (LeftTriggerDown())
            AdvanceMenuIndex(-1);

        if (RightTriggerDown())
            AdvanceMenuIndex(1);

        if (RightGripDown())
            ToggleFocusedSelection();

        if (LeftGripDown())
            ConfirmMenuSelection();
    }

    private void HandleManipulationInput()
    {
        if (selectedObjects.Count == 0)
        {
            EndManipulation();
            return;
        }

        if (LeftGripDown())
        {
            EndManipulation();
            return;
        }

        if (LeftTriggerDown())
        {
            AdvanceMode();
            return;
        }

        ApplyManipulation();
    }

    private void RefreshDevices()
    {
        leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    private void ResolveReferences()
    {
        if (selectionHand != null)
            return;

        if (Camera.main != null)
            selectionHand = FindChildRecursive(Camera.main.transform.root, "Left Controller");

        if (selectionHand == null)
            selectionHand = transform;
    }

    private Transform FindChildRecursive(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        if (parent.name == childName)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform result = FindChildRecursive(parent.GetChild(i), childName);
            if (result != null)
                return result;
        }

        return null;
    }

    private void ReadButtons()
    {
        prevLeftTriggerPressed = leftTriggerPressed;
        prevRightTriggerPressed = rightTriggerPressed;
        prevLeftGripPressed = leftGripPressed;
        prevRightGripPressed = rightGripPressed;
        leftTriggerPressed = false;
        rightTriggerPressed = false;
        leftGripPressed = false;
        rightGripPressed = false;

        if (leftDevice.isValid)
        {
            leftDevice.TryGetFeatureValue(CommonUsages.triggerButton, out leftTriggerPressed);
            leftDevice.TryGetFeatureValue(CommonUsages.gripButton, out leftGripPressed);
        }

        if (rightDevice.isValid)
        {
            rightDevice.TryGetFeatureValue(CommonUsages.triggerButton, out rightTriggerPressed);
            rightDevice.TryGetFeatureValue(CommonUsages.gripButton, out rightGripPressed);
        }
    }

    private bool LeftTriggerDown()
    {
        return leftTriggerPressed && !prevLeftTriggerPressed;
    }

    private bool RightTriggerDown()
    {
        return rightTriggerPressed && !prevRightTriggerPressed;
    }

    private bool LeftGripDown()
    {
        return leftGripPressed && !prevLeftGripPressed;
    }

    private bool RightGripDown()
    {
        return rightGripPressed && !prevRightGripPressed;
    }

    private void UpdateMenuObjects()
    {
        menuObjects.Clear();

        SelectableObject[] allSelectables = FindObjectsOfType<SelectableObject>(true);
        for (int i = 0; i < allSelectables.Length; i++)
        {
            SelectableObject selectable = allSelectables[i];
            if (selectable == null || !selectable.gameObject.activeInHierarchy)
                continue;

            menuObjects.Add(selectable);
        }

        menuObjects.Sort(CompareSelectableObjects);

        if (!menuOpen)
            return;

        if (GetMenuEntryCount() == 0)
        {
            CloseMenu();
            return;
        }

        menuIndex = Mathf.Clamp(menuIndex, 0, GetMenuEntryCount() - 1);
    }

    private int CompareSelectableObjects(SelectableObject a, SelectableObject b)
    {
        int nameCompare = string.Compare(a.name, b.name, System.StringComparison.Ordinal);
        if (nameCompare != 0)
            return nameCompare;

        return a.GetInstanceID().CompareTo(b.GetInstanceID());
    }

    private Vector3 GetSelectionCenter()
    {
        if (selectionHand == null)
            return transform.position;

        return selectionHand.position + selectionHand.forward * selectorDistance;
    }

    private void OpenMenu()
    {
        if (menuObjects.Count == 0)
            return;

        menuOpen = true;
        menuIndex = Mathf.Clamp(menuIndex, 0, GetMenuEntryCount() - 1);
    }

    private void CloseMenu()
    {
        menuOpen = false;
    }

    private void AdvanceMenuIndex(int direction)
    {
        int entryCount = GetMenuEntryCount();
        if (entryCount == 0)
            return;

        menuIndex = (menuIndex + direction + entryCount) % entryCount;
    }

    private void ToggleFocusedSelection()
    {
        int objectCount = menuObjects.Count;
        if (objectCount == 0 || menuIndex >= objectCount)
            return;

        ToggleSelection(menuObjects[menuIndex]);
    }

    private void ConfirmMenuSelection()
    {
        CloseMenu();
        if (selectedObjects.Count > 0)
            BeginManipulation();
    }

    private int GetMenuEntryCount()
    {
        if (menuObjects.Count == 0)
            return 0;

        return menuObjects.Count + 1;
    }

    private void ToggleSelection(SelectableObject selectable)
    {
        if (selectedObjects.Contains(selectable))
            RemoveSelection(selectable);
        else
            AddSelection(selectable);
    }

    private void AddSelection(SelectableObject selectable)
    {
        if (selectable == null || selectedObjects.Contains(selectable))
            return;

        selectedObjects.Add(selectable);
        selectable.SetHighlight(true, SelectableObject.HighlightChannel.GroupSelection);
    }

    private void RemoveSelection(SelectableObject selectable)
    {
        if (selectable == null)
            return;

        if (selectedObjects.Remove(selectable))
            selectable.SetHighlight(false, SelectableObject.HighlightChannel.GroupSelection);
    }

    private void ClearSelection()
    {
        for (int i = 0; i < selectedObjects.Count; i++)
        {
            if (selectedObjects[i] != null)
                selectedObjects[i].SetHighlight(false, SelectableObject.HighlightChannel.GroupSelection);
        }

        selectedObjects.Clear();
    }

    private void CleanupSelection()
    {
        for (int i = selectedObjects.Count - 1; i >= 0; i--)
        {
            if (selectedObjects[i] == null)
                selectedObjects.RemoveAt(i);
        }
    }

    private void BeginManipulation()
    {
        CloseMenu();
        manipulationMode = true;
        currentMode = GroupManipulationMode.Move;
        lastHandPosition = selectionHand.position;
        lastHandRotation = selectionHand.rotation;

        rigidbodyStates.Clear();
        for (int i = 0; i < selectedObjects.Count; i++)
        {
            Rigidbody rb = selectedObjects[i].GetComponent<Rigidbody>();
            if (rb == null || rigidbodyStates.ContainsKey(rb))
                continue;

            rigidbodyStates.Add(rb, new RigidbodyState
            {
                IsKinematic = rb.isKinematic,
                UseGravity = rb.useGravity
            });

            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private void EndManipulation()
    {
        manipulationMode = false;

        foreach (KeyValuePair<Rigidbody, RigidbodyState> pair in rigidbodyStates)
        {
            if (pair.Key == null)
                continue;

            pair.Key.isKinematic = pair.Value.IsKinematic;
            pair.Key.useGravity = pair.Value.UseGravity;
        }

        rigidbodyStates.Clear();
        ClearSelection();
    }

    private void AdvanceMode()
    {
        if (currentMode == GroupManipulationMode.Move)
        {
            currentMode = GroupManipulationMode.Rotate;
        }
        else if (currentMode == GroupManipulationMode.Rotate)
        {
            currentMode = GroupManipulationMode.Scale;
        }
        else
        {
            DuplicateSelection();
            currentMode = GroupManipulationMode.Move;
        }

        lastHandPosition = selectionHand.position;
        lastHandRotation = selectionHand.rotation;
    }

    private void ApplyManipulation()
    {
        if (selectionHand == null || selectedObjects.Count == 0)
            return;

        if (currentMode == GroupManipulationMode.Move)
        {
            ApplyMove();
        }
        else if (currentMode == GroupManipulationMode.Rotate)
        {
            ApplyRotation();
        }
        else
        {
            ApplyScale();
        }
    }

    private void ApplyMove()
    {
        Vector3 delta = selectionHand.position - lastHandPosition;
        if (delta.sqrMagnitude < 0.000001f)
            return;

        for (int i = 0; i < selectedObjects.Count; i++)
            selectedObjects[i].transform.position += delta;

        lastHandPosition = selectionHand.position;
    }

    private void ApplyRotation()
    {
        Vector3 previousForward = Vector3.ProjectOnPlane(lastHandRotation * Vector3.forward, Vector3.up);
        Vector3 currentForward = Vector3.ProjectOnPlane(selectionHand.forward, Vector3.up);

        if (previousForward.sqrMagnitude < 0.0001f || currentForward.sqrMagnitude < 0.0001f)
            return;

        float angle = Vector3.SignedAngle(previousForward, currentForward, Vector3.up);
        if (Mathf.Abs(angle) < 0.05f)
            return;

        Quaternion deltaRotation = Quaternion.AngleAxis(angle, Vector3.up);
        for (int i = 0; i < selectedObjects.Count; i++)
        {
            Transform target = selectedObjects[i].transform;
            target.rotation = deltaRotation * target.rotation;
        }

        lastHandRotation = selectionHand.rotation;
        lastHandPosition = selectionHand.position;
    }

    private void ApplyScale()
    {
        float distanceDelta = Vector3.Dot(selectionHand.position - lastHandPosition, selectionHand.forward);
        float scaleFactor = 1f + distanceDelta * scaleSensitivity;

        if (Mathf.Abs(scaleFactor - 1f) < 0.0001f)
            return;

        scaleFactor = Mathf.Max(0.2f, scaleFactor);

        for (int i = 0; i < selectedObjects.Count; i++)
        {
            Transform target = selectedObjects[i].transform;
            Vector3 scaled = target.localScale * scaleFactor;
            scaled.x = Mathf.Clamp(scaled.x, minObjectScale, maxObjectScale);
            scaled.y = Mathf.Clamp(scaled.y, minObjectScale, maxObjectScale);
            scaled.z = Mathf.Clamp(scaled.z, minObjectScale, maxObjectScale);
            target.localScale = scaled;
        }

        lastHandPosition = selectionHand.position;
    }

    private void DuplicateSelection()
    {
        if (selectedObjects.Count == 0)
            return;

        List<SelectableObject> originals = new List<SelectableObject>(selectedObjects);
        List<SelectableObject> duplicates = new List<SelectableObject>();
        Vector3 offset = selectionHand != null ? selectionHand.right * duplicateOffset : Vector3.right * duplicateOffset;

        for (int i = 0; i < originals.Count; i++)
            originals[i].SetHighlight(false, SelectableObject.HighlightChannel.GroupSelection);

        for (int i = 0; i < originals.Count; i++)
        {
            if (originals[i] == null)
                continue;

            GameObject clone = Instantiate(originals[i].gameObject);
            clone.transform.position = originals[i].transform.position + offset;
            clone.transform.rotation = originals[i].transform.rotation;
            clone.transform.localScale = originals[i].transform.localScale;

            SelectableObject selectable = clone.GetComponent<SelectableObject>();
            if (selectable == null)
                selectable = clone.AddComponent<SelectableObject>();

            duplicates.Add(selectable);
        }

        for (int i = 0; i < originals.Count; i++)
        {
            if (originals[i] != null)
                originals[i].SetHighlight(true, SelectableObject.HighlightChannel.GroupSelection);
        }

        ClearSelection();
        for (int i = 0; i < duplicates.Count; i++)
            AddSelection(duplicates[i]);
    }

    private Vector3 GetGroupPivot()
    {
        if (selectedObjects.Count == 0)
            return GetSelectionCenter();

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < selectedObjects.Count; i++)
            sum += selectedObjects[i].transform.position;

        return sum / selectedObjects.Count;
    }

    private void UpdateVRMenu()
    {
        if (vrMenuCanvas == null || vrMenuText == null)
            return;

        if (!menuOpen && !manipulationMode)
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

        if (menuOpen)
        {
            vrMenuText.text =
                "Group Selection Menu\n\n" +
                BuildMenuText() + "\n" +
                "Left/Right Trigger = Move\n" +
                "Right Grip = Toggle Select\n" +
                "Left Grip = Start Group Edit";
        }
        else if (manipulationMode)
        {
            vrMenuText.text =
                "Group Manipulation Mode\n\n" +
                "Current Mode: " + currentMode + "\n" +
                "Left Trigger = Next Mode\n" +
                "Left Grip = Exit Manipulation";
        }
        else
        {
            vrMenuText.text =
                "Group Selection Controls\n\n" +
                "Left Grip = Open Group Menu\n" +
                "Left/Right Trigger = Move\n" +
                "Right Grip = Toggle Select\n" +
                "Left Grip = Start Group Edit";
        }
    }

    private string BuildMenuText()
    {
        StringBuilder builder = new StringBuilder();
        int objectCount = menuObjects.Count;

        builder.AppendLine("Selectable Objects:");

        if (objectCount == 0)
        {
            builder.AppendLine("  (none)");
            return builder.ToString();
        }

        int visibleRows = Mathf.Min(objectCount, Mathf.Max(1, maxVisibleRows));
        int scrollStart = 0;
        if (objectCount > visibleRows)
            scrollStart = Mathf.Clamp(menuIndex - visibleRows + 1, 0, objectCount - visibleRows);

        int scrollEnd = Mathf.Min(objectCount, scrollStart + visibleRows);
        for (int i = scrollStart; i < scrollEnd; i++)
        {
            SelectableObject selectable = menuObjects[i];
            bool isFocused = menuIndex == i;
            bool isSelected = selectedObjects.Contains(selectable);

            string cursor = isFocused ? "> " : "  ";
            string check = isSelected ? "[X] " : "[ ] ";
            builder.AppendLine(cursor + check + selectable.name);
        }

        bool confirmFocused = menuIndex == objectCount;
        string confirmCursor = confirmFocused ? "> " : "  ";
        builder.AppendLine(confirmCursor + "Start Group Edit");
        builder.AppendLine();
        builder.AppendLine("Showing " + (scrollStart + 1) + "-" + scrollEnd + " of " + objectCount);
        builder.AppendLine("Selected: " + selectedObjects.Count);

        return builder.ToString();
    }

    private void OnGUI()
    {
        if (!menuOpen && !manipulationMode)
            return;

        float width = 460f;
        float height = 150f;
        float x = Screen.width - width - 20f;
        float y = 40f;

        string modeText = manipulationMode ? currentMode.ToString() : (menuOpen ? "Menu" : "Selection");

        GUI.Box(new Rect(x, y, width, height),
            "Group Selection (Left Hand)\n" +
            "Mode: " + modeText + "\n\n" +
            "Left Grip = Open menu / exit manipulation\n" +
            "Left/Right Trigger = Move through list\n" +
            "Right Grip = Toggle item\n" +
            "While manipulating: Left Trigger cycles Move -> Rotate -> Scale -> Duplicate");
    }
}
