using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class GroupSelectionManipulationVR : MonoBehaviour
{
    private const string DefaultLineShaderName = "Sprites/Default";
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

    [Header("References")]
    public Transform selectionHand;
    public LayerMask selectableMask = ~0;

    [Header("Selection Bubble")]
    public float selectorDistance = 0.45f;
    public float selectorRadius = 0.28f;
    public float indicatorLineWidth = 0.008f;

    [Header("Manipulation")]
    public float scaleSensitivity = 1.1f;
    public float duplicateOffset = 0.35f;
    public float minObjectScale = 0.1f;
    public float maxObjectScale = 4f;

    [Header("Indicator Colors")]
    public Color idleColor = new Color(0.3f, 0.7f, 1f, 1f);
    public Color candidateColor = new Color(1f, 0.82f, 0.25f, 1f);
    public Color selectedColor = new Color(0.2f, 1f, 0.45f, 1f);

    private readonly List<SelectableObject> selectedObjects = new List<SelectableObject>();
    private readonly Dictionary<Rigidbody, RigidbodyState> rigidbodyStates = new Dictionary<Rigidbody, RigidbodyState>();

    private InputDevice leftDevice;
    private bool prevTriggerPressed;
    private bool prevGripPressed;
    private bool triggerPressed;
    private bool gripPressed;

    private SelectableObject candidateObject;
    private bool manipulationMode;
    private GroupManipulationMode currentMode = GroupManipulationMode.Move;
    private Vector3 lastHandPosition;
    private Quaternion lastHandRotation;

    private LineRenderer selectorLine;
    private LineRenderer candidateLine;
    private Transform selectorSphere;
    private Renderer selectorRenderer;
    private Transform candidateSphere;
    private Renderer candidateRenderer;
    private Transform pivotSphere;
    private Renderer pivotRenderer;

    private void Start()
    {
        RefreshDevice();
        ResolveReferences();
        CreateIndicators();
    }

    private void Update()
    {
        if (!leftDevice.isValid)
            RefreshDevice();

        if (selectionHand == null)
            ResolveReferences();

        CleanupSelection();
        ReadButtons();
        UpdateCandidate();
        UpdateIndicators();

        if (!manipulationMode)
        {
            HandleSelectionInput();
            return;
        }

        HandleManipulationInput();
    }

    private void HandleSelectionInput()
    {
        if (TriggerDown())
        {
            if (candidateObject != null)
                ToggleSelection(candidateObject);
            else if (selectedObjects.Count > 0)
                ClearSelection();
        }

        if (GripDown() && selectedObjects.Count > 0)
            BeginManipulation();
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

    private void UpdateCandidate()
    {
        candidateObject = null;

        if (selectionHand == null)
            return;

        Vector3 center = GetSelectionCenter();
        Collider[] hits = Physics.OverlapSphere(center, selectorRadius, selectableMask, QueryTriggerInteraction.Ignore);
        float closestDistance = float.MaxValue;
        HashSet<SelectableObject> seenObjects = new HashSet<SelectableObject>();

        for (int i = 0; i < hits.Length; i++)
        {
            SelectableObject selectable = hits[i].GetComponentInParent<SelectableObject>();
            if (selectable == null || seenObjects.Contains(selectable))
                continue;

            seenObjects.Add(selectable);

            float distance = Vector3.Distance(center, selectable.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                candidateObject = selectable;
            }
        }
    }

    private Vector3 GetSelectionCenter()
    {
        if (selectionHand == null)
            return transform.position;

        return selectionHand.position + selectionHand.forward * selectorDistance;
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

    private void CreateIndicators()
    {
        GameObject lineObject = new GameObject("GroupSelectionLine");
        lineObject.transform.SetParent(transform, false);

        selectorLine = lineObject.AddComponent<LineRenderer>();
        ConfigureLineRenderer(selectorLine, indicatorLineWidth);

        GameObject candidateLineObject = new GameObject("GroupCandidateLine");
        candidateLineObject.transform.SetParent(transform, false);

        candidateLine = candidateLineObject.AddComponent<LineRenderer>();
        ConfigureLineRenderer(candidateLine, indicatorLineWidth * 0.75f);

        selectorSphere = CreateIndicatorPrimitive("GroupSelectionBubble", selectorRadius * 2f);
        selectorRenderer = selectorSphere.GetComponent<Renderer>();
        selectorSphere.gameObject.SetActive(false);

        candidateSphere = CreateIndicatorPrimitive("GroupCandidateIndicator", 0.12f);
        candidateRenderer = candidateSphere.GetComponent<Renderer>();
        candidateSphere.gameObject.SetActive(false);

        pivotSphere = CreateIndicatorPrimitive("GroupPivotIndicator", 0.08f);
        pivotRenderer = pivotSphere.GetComponent<Renderer>();
        pivotSphere.gameObject.SetActive(false);
    }

    private Transform CreateIndicatorPrimitive(string objectName, float uniformScale)
    {
        GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        primitive.name = objectName;
        primitive.transform.SetParent(transform, false);
        primitive.transform.localScale = Vector3.one * uniformScale;

        Collider collider = primitive.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        Renderer renderer = primitive.GetComponent<Renderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.material = new Material(FindIndicatorShader());

        return primitive.transform;
    }

    private void ConfigureLineRenderer(LineRenderer target, float width)
    {
        target.useWorldSpace = true;
        target.positionCount = 2;
        target.loop = false;
        target.alignment = LineAlignment.View;
        target.widthMultiplier = width;
        target.numCapVertices = 4;
        target.numCornerVertices = 4;
        target.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        target.receiveShadows = false;
        target.textureMode = LineTextureMode.Stretch;

        if (target.sharedMaterial == null)
        {
            Shader lineShader = Shader.Find(DefaultLineShaderName);
            if (lineShader != null)
                target.sharedMaterial = new Material(lineShader);
        }

        target.enabled = false;
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

    private void ApplyIndicatorColor(Renderer renderer, Color color, float alpha)
    {
        if (renderer == null)
            return;

        Color tinted = new Color(color.r, color.g, color.b, alpha);
        renderer.material.color = tinted;
    }

    private void UpdateIndicators()
    {
        if (selectionHand == null || selectorLine == null || selectorSphere == null || pivotSphere == null)
            return;

        bool showPrimaryIndicator = manipulationMode || selectedObjects.Count > 0 || candidateObject != null;
        selectorLine.enabled = showPrimaryIndicator;
        selectorSphere.gameObject.SetActive(showPrimaryIndicator);

        if (!showPrimaryIndicator)
        {
            candidateLine.enabled = false;
            candidateSphere.gameObject.SetActive(false);
            pivotSphere.gameObject.SetActive(false);
            return;
        }

        Vector3 center = GetSelectionCenter();
        Color lineColor = selectedObjects.Count > 0 ? selectedColor : (candidateObject != null ? candidateColor : idleColor);

        selectorLine.startColor = lineColor;
        selectorLine.endColor = lineColor;
        selectorLine.SetPosition(0, selectionHand.position);
        selectorLine.SetPosition(1, center);

        selectorSphere.position = center;
        selectorSphere.localScale = Vector3.one * selectorRadius * 2f;
        ApplyIndicatorColor(selectorRenderer, lineColor, 0.35f);

        bool showCandidate = candidateObject != null && !selectedObjects.Contains(candidateObject);
        candidateSphere.gameObject.SetActive(showCandidate);
        candidateLine.enabled = showCandidate;
        if (showCandidate)
        {
            Vector3 candidatePosition = candidateObject.transform.position;
            candidateSphere.position = candidatePosition;
            ApplyIndicatorColor(candidateRenderer, candidateColor, 0.9f);
            candidateLine.startColor = candidateColor;
            candidateLine.endColor = candidateColor;
            candidateLine.SetPosition(0, center);
            candidateLine.SetPosition(1, candidatePosition);
        }

        bool showPivot = selectedObjects.Count > 0;
        pivotSphere.gameObject.SetActive(showPivot);
        if (showPivot)
        {
            pivotSphere.position = GetGroupPivot();
            ApplyIndicatorColor(pivotRenderer, selectedColor, 0.95f);
        }
    }

    private void OnGUI()
    {
        float width = 450f;
        float height = 130f;
        float x = Screen.width - width - 20f;
        float y = 40f;

        string modeText = manipulationMode ? currentMode.ToString() : "Selection";

        GUI.Box(new Rect(x, y, width, height),
            "Group Selection (Left Hand)\n" +
            "Mode: " + modeText + "\n\n" +
            "Trigger = Add/Remove object in bubble\n" +
            "Grip = Enter/Exit group manipulation\n" +
            "While manipulating: Trigger cycles Move -> Rotate -> Scale -> Duplicate");
    }
}
