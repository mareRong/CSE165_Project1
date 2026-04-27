using UnityEngine;

public class VRMenuManager : MonoBehaviour
{
    public enum VRMenuMode
    {
        None,
        Spawn,
        GroupSelection,
        SingleSelection
    }

    [Header("Mode Owners")]
    public SpawnMenu spawnMenu;
    public GroupSelectionManipulationVR groupSelectionMenu;
    public SelectionManipulator singleSelectionMenu;

    [Header("Idle Display")]
    public VRMenuMode idleDisplayMode = VRMenuMode.SingleSelection;

    public VRMenuMode ActiveMode => activeMode;

    [SerializeField]
    private VRMenuMode activeMode = VRMenuMode.None;

    private void Awake()
    {
        ResolveReferences();
    }

    public bool RequestMode(VRMenuMode mode)
    {
        ResolveReferences();

        if (mode == VRMenuMode.None)
        {
            ClearAllModes();
            return true;
        }

        if (activeMode == mode)
            return true;

        ForceCloseAllExcept(mode);
        activeMode = mode;
        return true;
    }

    public void ReleaseMode(VRMenuMode mode)
    {
        if (activeMode == mode)
            activeMode = VRMenuMode.None;
    }

    public bool IsModeActive(VRMenuMode mode)
    {
        return activeMode == mode;
    }

    public bool CanDisplay(VRMenuMode mode, bool allowIdleDisplay = false)
    {
        if (activeMode == mode)
            return true;

        return allowIdleDisplay && activeMode == VRMenuMode.None && idleDisplayMode == mode;
    }

    private void ClearAllModes()
    {
        ForceCloseAllExcept(VRMenuMode.None);
        activeMode = VRMenuMode.None;
    }

    private void ForceCloseAllExcept(VRMenuMode modeToKeep)
    {
        if (modeToKeep != VRMenuMode.Spawn && spawnMenu != null)
            spawnMenu.ForceCloseFromManager();

        if (modeToKeep != VRMenuMode.GroupSelection && groupSelectionMenu != null)
            groupSelectionMenu.ForceCloseFromManager();

        if (modeToKeep != VRMenuMode.SingleSelection && singleSelectionMenu != null)
            singleSelectionMenu.ForceCloseFromManager();
    }

    private void ResolveReferences()
    {
        if (spawnMenu == null)
            spawnMenu = FindObjectOfType<SpawnMenu>(true);

        if (groupSelectionMenu == null)
            groupSelectionMenu = FindObjectOfType<GroupSelectionManipulationVR>(true);

        if (singleSelectionMenu == null)
            singleSelectionMenu = FindObjectOfType<SelectionManipulator>(true);
    }
}
