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
    public float menuDistance = 0.42f;
    public float menuVerticalOffset = 0.08f;
    public float menuWidth = 0.28f;
    public float rowHeight = 0.06f;
    public int maxVisibleRows = 4;
    public float menuDepth = 0.004f;
    public float menuRowDepth = 0.002f;
    public float menuRowForwardOffset = 0.015f;
    public float menuTextForwardOffset = 0.026f;

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

    private readonly List<SelectableObject> selectedObjects = new List<SelectableObject>();
    private readonly List<SelectableObject> nearbyObjects = new List<SelectableObject>();
    private readonly Dictionary<Rigidbody, RigidbodyState> rigidbodyStates = new Dictionary<Rigidbody, RigidbodyState>();
    private readonly List<MenuRowVisual> menuRows = new List<MenuRowVisual>();

    private InputDevice leftDevice;
    private bool prevTriggerPressed;
    private bool prevGripPressed;
    private bool triggerPressed;
    private bool gripPressed;

    private bool menuOpen;
    private int menuIndex;
    private bool manipulationMode;
    private GroupManipulationMode currentMode = GroupManipulationMode.Move;
    private Vector3 lastHandPosition;
    private Quaternion lastHandRotation;

    private Transform menuRoot;
    private Transform menuPanel;
    private Renderer menuPanelRenderer;
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
        UpdateNearbyObjects();
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
                if (nearbyObjects.Count > 0)
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

        if (GripDown())
        {
            AdvanceMenuIndex();
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
    }

    private bool TriggerDown()
    {
        return triggerPressed && !prevTriggerPressed;
    }

    private bool GripDown()
    {
        return gripPressed && !prevGripPressed;
    }

    private void UpdateNearbyObjects()
    {
        nearbyObjects.Clear();

        if (selectionHand == null)
            return;

        Vector3 center = GetSelectionCenter();
        Collider[] hits = Physics.OverlapSphere(center, selectorRadius, selectableMask, QueryTriggerInteraction.Ignore);
        HashSet<SelectableObject> seenObjects = new HashSet<SelectableObject>();

        for (int i = 0; i < hits.Length; i++)
        {
            SelectableObject selectable = hits[i].GetComponentInParent<SelectableObject>();
            if (selectable == null || seenObjects.Contains(selectable))
                continue;

            seenObjects.Add(selectable);
            nearbyObjects.Add(selectable);
        }

        nearbyObjects.Sort(CompareSelectableObjects);

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
        float distanceA = Vector3.Distance(GetSelectionCenter(), a.transform.position);
        float distanceB = Vector3.Distance(GetSelectionCenter(), b.transform.position);

        if (!Mathf.Approximately(distanceA, distanceB))
            return distanceA.CompareTo(distanceB);

        return string.Compare(a.name, b.name, System.StringComparison.Ordinal);
    }

    private Vector3 GetSelectionCenter()
    {
        if (selectionHand == null)
            return transform.position;

        return selectionHand.position + selectionHand.forward * selectorDistance;
    }

    private void OpenMenu()
    {
        if (nearbyObjects.Count == 0)
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

    private void AdvanceMenuIndex()
    {
        int entryCount = GetMenuEntryCount();
        if (entryCount == 0)
            return;

        menuIndex = (menuIndex + 1) % entryCount;
        UpdateMenuVisuals();
    }

    private void ActivateMenuEntry()
    {
        int objectCount = nearbyObjects.Count;
        if (objectCount == 0)
        {
            CloseMenu();
            return;
        }

        if (menuIndex < objectCount)
        {
            ToggleSelection(nearbyObjects[menuIndex]);
            UpdateMenuVisuals();
            return;
        }

        CloseMenu();
        if (selectedObjects.Count > 0)
            BeginManipulation();
    }

    private int GetMenuEntryCount()
    {
        if (nearbyObjects.Count == 0)
            return 0;

        return nearbyObjects.Count + 1;
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

        menuTitleText = CreateMenuText("MenuTitle", menuRoot, 0.032f, TextAnchor.MiddleLeft);
        menuHintText = CreateMenuText("MenuHint", menuRoot, 0.022f, TextAnchor.MiddleLeft);

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

            row.Label = CreateMenuText("Label", row.Root, 0.026f, TextAnchor.MiddleLeft);
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
        textMesh.fontSize = 80;
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
            if (lookDirection.sqrMagnitude > 0.0001f)
                menuRoot.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        }
        else
        {
            menuRoot.rotation = Quaternion.LookRotation(selectionHand.forward, Vector3.up);
        }

        if (menuOpen)
            UpdateMenuVisuals();
    }

    private void UpdateMenuVisuals()
    {
        if (menuRoot == null || menuPanel == null || menuTitleText == null || menuHintText == null)
            return;

        if (!menuOpen || nearbyObjects.Count == 0)
        {
            menuRoot.gameObject.SetActive(false);
            return;
        }

        menuRoot.gameObject.SetActive(true);

        int objectCount = nearbyObjects.Count;
        int visibleObjectRows = Mathf.Min(objectCount, Mathf.Max(1, maxVisibleRows));
        int visibleRows = visibleObjectRows + 1;
        float menuHeight = 0.09f + visibleRows * rowHeight;

        menuPanel.localScale = new Vector3(menuWidth, menuHeight, menuDepth);
        menuPanel.localPosition = new Vector3(0f, -menuHeight * 0.5f, 0f);

        menuTitleText.text = "Selection Menu";
        menuTitleText.transform.localPosition = new Vector3(-menuWidth * 0.44f, -0.035f, -menuTextForwardOffset);

        menuHintText.text = "Grip: scroll   Trigger: toggle / confirm";
        menuHintText.transform.localPosition = new Vector3(-menuWidth * 0.44f, -0.075f, -menuTextForwardOffset);

        int scrollStart = 0;
        if (objectCount > visibleObjectRows)
            scrollStart = Mathf.Clamp(menuIndex - visibleObjectRows + 1, 0, objectCount - visibleObjectRows);

        int rowSlot = 0;
        for (; rowSlot < visibleObjectRows; rowSlot++)
        {
            int objectIndex = scrollStart + rowSlot;
            MenuRowVisual row = menuRows[rowSlot];
            SelectableObject selectable = nearbyObjects[objectIndex];
            bool isFocused = menuIndex == objectIndex;
            bool isSelected = selectedObjects.Contains(selectable);

            string prefix = isSelected ? "[x] " : "[ ] ";
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
            selectedObjects.Count > 0 ? "Confirm Selection" : "Confirm Selection (none selected)",
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

        float y = -0.12f - rowIndex * rowHeight;
        row.Root.localPosition = new Vector3(0f, y, -menuRowForwardOffset);

        Transform rowBackground = row.BackgroundRenderer.transform;
        rowBackground.localPosition = Vector3.zero;
        rowBackground.localScale = new Vector3(menuWidth * 0.88f, rowHeight * 0.72f, menuRowDepth);

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
            "Trigger = Open menu / toggle item / confirm selection\n" +
            "Grip = Scroll menu\n" +
            "While manipulating: Trigger cycles Move -> Rotate -> Scale -> Duplicate");
    }
}
