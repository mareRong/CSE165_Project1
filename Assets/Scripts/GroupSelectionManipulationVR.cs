using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class GroupSelectionManipulationVR : MonoBehaviour
{
    private const string PreferredIndicatorShaderName = "Universal Render Pipeline/Unlit";

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

    private sealed class MenuRowVisual
    {
        public Transform Root;
        public Renderer BackgroundRenderer;
        public TextMesh Label;
    }

    [Header("References")]
    public Transform selectionHand;
    public LayerMask selectableMask = ~0;

    [Header("Selection Query")]
    public float selectorDistance = 0.65f;
    public float selectorRadius = 1.1f;

    [Header("Menu Layout")]
    public float menuDistance = 0.36f;
    public float menuVerticalOffset = 0.05f;
    public float menuWidth = 0.24f;
    public float rowHeight = 0.052f;
    public int maxVisibleRows = 3;
    public float menuDepth = 0.01f;
    public float menuRowDepth = 0.003f;
    public float menuRowForwardOffset = 0.02f;
    public float menuTextForwardOffset = 0.03f;

    [Header("Manipulation")]
    public float scaleSensitivity = 1.1f;
    public float duplicateOffset = 0.35f;
    public float minObjectScale = 0.1f;
    public float maxObjectScale = 4f;

    [Header("Menu Colors")]
    public Color selectedColor = new Color(0.2f, 1f, 0.45f, 1f);
    public Color menuBackgroundColor = new Color(0.1f, 0.12f, 0.16f, 0.95f);
    public Color menuRowColor = new Color(0.94f, 0.94f, 0.96f, 0.95f);
    public Color menuFocusedRowColor = new Color(1f, 0.82f, 0.25f, 1f);
    public Color menuSelectedRowColor = new Color(0.2f, 1f, 0.45f, 1f);
    public Color menuBorderColor = new Color(0.78f, 0.82f, 0.9f, 1f);

    private readonly List<SelectableObject> selectedObjects = new List<SelectableObject>();
    private readonly List<SelectableObject> menuObjects = new List<SelectableObject>();
    private readonly Dictionary<Rigidbody, RigidbodyState> rigidbodyStates = new Dictionary<Rigidbody, RigidbodyState>();
    private readonly List<MenuRowVisual> menuRows = new List<MenuRowVisual>();

    private InputDevice leftDevice;
    private bool prevTriggerPressed;
    private bool prevGripPressed;
    private bool triggerPressed;
    private bool gripPressed;
    private Vector2 thumbstickAxis;
    private bool thumbstickScrollReady = true;

    private bool menuOpen;
    private int menuIndex;
    private bool manipulationMode;
    private GroupManipulationMode currentMode = GroupManipulationMode.Move;
    private Vector3 lastHandPosition;
    private Quaternion lastHandRotation;

    private Transform menuRoot;
    private Transform menuPanel;
    private Renderer menuPanelRenderer;
    private Transform menuBorder;
    private Renderer menuBorderRenderer;
    private TextMesh menuTitleText;
    private TextMesh menuHintText;

    private void Start()
    {
        RefreshDevice();
        ResolveReferences();
        CreateMenuVisuals();
    }

    private void Update()
    {
        if (!leftDevice.isValid)
            RefreshDevice();

        if (selectionHand == null)
            ResolveReferences();

        CleanupSelection();
        ReadButtons();
        UpdateMenuObjects();
        UpdateMenuAnchor();

        if (manipulationMode)
        {
            HandleManipulationInput();
            return;
        }

        HandleSelectionInput();
    }

    private void HandleSelectionInput()
    {
        if (!menuOpen)
        {
            if (TriggerDown())
            {
                if (menuObjects.Count > 0)
                {
                    OpenMenu();
                }
                else if (selectedObjects.Count > 0)
                {
                    OpenMenu();
                }
            }

            return;
        }

        int scrollDirection = GetThumbstickScrollDirection();
        if (scrollDirection != 0)
        {
            AdvanceMenuIndex(scrollDirection);
        }

        if (TriggerDown())
        {
            ActivateMenuEntry();
        }
    }

    private void HandleManipulationInput()
    {
        if (selectedObjects.Count == 0)
        {
            EndManipulation();
            return;
        }

        if (GripDown())
        {
            EndManipulation();
            return;
        }

        if (TriggerDown())
        {
            AdvanceMode();
            return;
        }

        ApplyManipulation();
    }

    private void RefreshDevice()
    {
        leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
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
        prevTriggerPressed = triggerPressed;
        prevGripPressed = gripPressed;
        triggerPressed = false;
        gripPressed = false;

        if (!leftDevice.isValid)
            return;

        leftDevice.TryGetFeatureValue(CommonUsages.triggerButton, out triggerPressed);
        leftDevice.TryGetFeatureValue(CommonUsages.gripButton, out gripPressed);
        leftDevice.TryGetFeatureValue(CommonUsages.primary2DAxis, out thumbstickAxis);
    }

    private bool TriggerDown()
    {
        return triggerPressed && !prevTriggerPressed;
    }

    private bool GripDown()
    {
        return gripPressed && !prevGripPressed;
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
        UpdateMenuVisuals();
    }

    private void CloseMenu()
    {
        menuOpen = false;
        if (menuRoot != null)
            menuRoot.gameObject.SetActive(false);
    }

    private int GetThumbstickScrollDirection()
    {
        const float scrollThreshold = 0.65f;
        const float resetThreshold = 0.35f;

        if (!leftDevice.isValid)
            return 0;

        float y = thumbstickAxis.y;

        if (!thumbstickScrollReady)
        {
            if (Mathf.Abs(y) <= resetThreshold)
                thumbstickScrollReady = true;

            return 0;
        }

        if (y >= scrollThreshold)
        {
            thumbstickScrollReady = false;
            return -1;
        }

        if (y <= -scrollThreshold)
        {
            thumbstickScrollReady = false;
            return 1;
        }

        return 0;
    }

    private void AdvanceMenuIndex(int direction)
    {
        int entryCount = GetMenuEntryCount();
        if (entryCount == 0)
            return;

        menuIndex = (menuIndex + direction + entryCount) % entryCount;
        UpdateMenuVisuals();
    }

    private void ActivateMenuEntry()
    {
        int objectCount = menuObjects.Count;
        if (objectCount == 0)
        {
            CloseMenu();
            return;
        }

        if (menuIndex < objectCount)
        {
            ToggleSelection(menuObjects[menuIndex]);
            UpdateMenuVisuals();
            return;
        }

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
        Vector3 pivot = GetGroupPivot();
        for (int i = 0; i < selectedObjects.Count; i++)
        {
            Transform target = selectedObjects[i].transform;
            Vector3 offset = target.position - pivot;
            target.position = pivot + deltaRotation * offset;
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
        Vector3 pivot = GetGroupPivot();

        for (int i = 0; i < selectedObjects.Count; i++)
        {
            Transform target = selectedObjects[i].transform;
            Vector3 offset = target.position - pivot;
            target.position = pivot + offset * scaleFactor;

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

    private void CreateMenuVisuals()
    {
        menuRoot = new GameObject("GroupSelectionMenu").transform;
        menuRoot.SetParent(transform, false);
        menuRoot.gameObject.SetActive(false);

        menuPanel = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
        menuPanel.name = "MenuPanel";
        menuPanel.SetParent(menuRoot, false);
        Destroy(menuPanel.GetComponent<Collider>());
        menuPanelRenderer = menuPanel.GetComponent<Renderer>();
        menuPanelRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        menuPanelRenderer.receiveShadows = false;
        menuPanelRenderer.material = new Material(FindIndicatorShader());
        menuPanelRenderer.material.color = menuBackgroundColor;

        menuBorder = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
        menuBorder.name = "MenuBorder";
        menuBorder.SetParent(menuRoot, false);
        Destroy(menuBorder.GetComponent<Collider>());
        menuBorderRenderer = menuBorder.GetComponent<Renderer>();
        menuBorderRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        menuBorderRenderer.receiveShadows = false;
        menuBorderRenderer.material = new Material(FindIndicatorShader());
        menuBorderRenderer.material.color = menuBorderColor;

        menuTitleText = CreateMenuText("MenuTitle", menuRoot, 0.02f, TextAnchor.MiddleLeft);
        menuHintText = CreateMenuText("MenuHint", menuRoot, 0.013f, TextAnchor.MiddleLeft);

        int totalRows = Mathf.Max(1, maxVisibleRows + 1);
        for (int i = 0; i < totalRows; i++)
        {
            MenuRowVisual row = new MenuRowVisual();
            row.Root = new GameObject("Row" + i).transform;
            row.Root.SetParent(menuRoot, false);

            GameObject rowBackground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rowBackground.name = "Background";
            rowBackground.transform.SetParent(row.Root, false);
            Destroy(rowBackground.GetComponent<Collider>());

            row.BackgroundRenderer = rowBackground.GetComponent<Renderer>();
            row.BackgroundRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            row.BackgroundRenderer.receiveShadows = false;
            row.BackgroundRenderer.material = new Material(FindIndicatorShader());

            row.Label = CreateMenuText("Label", row.Root, 0.016f, TextAnchor.MiddleLeft);
            menuRows.Add(row);
        }
    }

    private TextMesh CreateMenuText(string objectName, Transform parent, float characterSize, TextAnchor anchor)
    {
        GameObject textObject = new GameObject(objectName);
        textObject.transform.SetParent(parent, false);

        TextMesh textMesh = textObject.AddComponent<TextMesh>();
        textMesh.anchor = anchor;
        textMesh.alignment = TextAlignment.Left;
        textMesh.characterSize = characterSize;
        textMesh.fontSize = 64;
        textMesh.color = Color.black;
        textMesh.text = string.Empty;

        MeshRenderer renderer = textObject.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return textMesh;
    }

    private Shader FindIndicatorShader()
    {
        Shader shader = Shader.Find(PreferredIndicatorShaderName);
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        return shader;
    }

    private void UpdateMenuAnchor()
    {
        if (menuRoot == null || selectionHand == null)
            return;

        Vector3 anchorPosition = selectionHand.position +
                                 selectionHand.forward * menuDistance +
                                 Vector3.up * menuVerticalOffset;

        menuRoot.position = anchorPosition;

        if (Camera.main != null)
        {
            Vector3 lookDirection = Camera.main.transform.position - menuRoot.position;
            lookDirection = Vector3.ProjectOnPlane(lookDirection, Vector3.up);
            if (lookDirection.sqrMagnitude > 0.0001f)
                menuRoot.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        }
        else
        {
            Vector3 handForward = Vector3.ProjectOnPlane(selectionHand.forward, Vector3.up);
            if (handForward.sqrMagnitude > 0.0001f)
                menuRoot.rotation = Quaternion.LookRotation(handForward.normalized, Vector3.up);
        }

        if (menuOpen)
            UpdateMenuVisuals();
    }

    private void UpdateMenuVisuals()
    {
        if (menuRoot == null || menuPanel == null || menuTitleText == null || menuHintText == null)
            return;

        if (!menuOpen || menuObjects.Count == 0)
        {
            menuRoot.gameObject.SetActive(false);
            return;
        }

        menuRoot.gameObject.SetActive(true);

        int objectCount = menuObjects.Count;
        int visibleObjectRows = Mathf.Min(objectCount, Mathf.Max(1, maxVisibleRows));
        int visibleRows = visibleObjectRows + 1;
        float headerHeight = 0.09f;
        float footerHeight = 0.03f;
        float menuHeight = headerHeight + visibleRows * rowHeight + footerHeight;
        float borderInset = 0.008f;

        menuPanel.localScale = new Vector3(menuWidth, menuHeight, menuDepth);
        menuPanel.localPosition = new Vector3(0f, -menuHeight * 0.5f, 0.008f);

        if (menuBorder != null)
        {
            menuBorder.localScale = new Vector3(menuWidth + borderInset, menuHeight + borderInset, menuDepth);
            menuBorder.localPosition = new Vector3(0f, -menuHeight * 0.5f, 0f);
        }

        int scrollStart = 0;
        if (objectCount > visibleObjectRows)
            scrollStart = Mathf.Clamp(menuIndex - visibleObjectRows + 1, 0, objectCount - visibleObjectRows);

        menuTitleText.text = "Group Select";
        menuTitleText.transform.localPosition = new Vector3(-menuWidth * 0.42f, -0.03f, -menuTextForwardOffset);

        int windowStart = objectCount == 0 ? 0 : scrollStart + 1;
        int windowEnd = Mathf.Min(objectCount, scrollStart + visibleObjectRows);
        menuHintText.text = selectedObjects.Count + " selected | " + windowStart + "-" + windowEnd + " of " + objectCount;
        menuHintText.transform.localPosition = new Vector3(-menuWidth * 0.42f, -0.064f, -menuTextForwardOffset);

        int rowSlot = 0;
        for (; rowSlot < visibleObjectRows; rowSlot++)
        {
            int objectIndex = scrollStart + rowSlot;
            MenuRowVisual row = menuRows[rowSlot];
            SelectableObject selectable = menuObjects[objectIndex];
            bool isFocused = menuIndex == objectIndex;
            bool isSelected = selectedObjects.Contains(selectable);

            string prefix = isSelected ? "[x]  " : "[ ]  ";
            ConfigureMenuRow(
                row,
                rowSlot,
                prefix + selectable.name,
                isFocused,
                isSelected,
                menuRowColor);
        }

        MenuRowVisual confirmRow = menuRows[rowSlot];
        bool confirmFocused = menuIndex == objectCount;
        ConfigureMenuRow(
            confirmRow,
            rowSlot,
            "Start Group Edit",
            confirmFocused,
            false,
            selectedObjects.Count > 0 ? selectedColor : menuRowColor);
        rowSlot++;

        for (int i = rowSlot; i < menuRows.Count; i++)
            menuRows[i].Root.gameObject.SetActive(false);
    }

    private void ConfigureMenuRow(MenuRowVisual row, int rowIndex, string label, bool isFocused, bool isSelected, Color baseColor)
    {
        row.Root.gameObject.SetActive(true);

        float y = -0.125f - rowIndex * rowHeight;
        row.Root.localPosition = new Vector3(0f, y, -menuRowForwardOffset);

        Transform rowBackground = row.BackgroundRenderer.transform;
        rowBackground.localPosition = Vector3.zero;
        rowBackground.localScale = new Vector3(menuWidth * 0.9f, rowHeight * 0.74f, menuRowDepth);

        Color rowColor = baseColor;
        if (isSelected)
            rowColor = menuSelectedRowColor;
        if (isFocused)
            rowColor = menuFocusedRowColor;

        row.BackgroundRenderer.material.color = rowColor;
        row.Label.text = label;
        row.Label.transform.localPosition = new Vector3(-menuWidth * 0.4f, 0f, -menuTextForwardOffset);
    }

    private void OnGUI()
    {
        float width = 460f;
        float height = 150f;
        float x = Screen.width - width - 20f;
        float y = 40f;

        string modeText = manipulationMode ? currentMode.ToString() : (menuOpen ? "Menu" : "Selection");

        GUI.Box(new Rect(x, y, width, height),
            "Group Selection (Left Hand)\n" +
            "Mode: " + modeText + "\n\n" +
            "Trigger = Open menu / toggle item / confirm\n" +
            "Thumbstick = Scroll menu\n" +
            "Grip = Exit manipulation\n" +
            "While manipulating: Trigger cycles Move -> Rotate -> Scale -> Duplicate");
    }
}
