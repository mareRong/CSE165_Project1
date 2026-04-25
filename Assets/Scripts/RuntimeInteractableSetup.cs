using UnityEngine;
using UnityEngine.SceneManagement;

public static class RuntimeInteractableSetup
{
    private const int SelectableLayer = 3;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ConfigureSceneInteractables()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        GameObject[] roots = activeScene.GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            if (!ShouldAutoConfigureRoot(roots[i]))
                continue;

            ConfigureInteractableHierarchy(roots[i]);
        }
    }

    public static void ConfigureInteractableHierarchy(GameObject root)
    {
        if (root == null)
            return;

        if (root.GetComponent<SelectableObject>() == null)
            root.AddComponent<SelectableObject>();

        SetLayerRecursively(root.transform, SelectableLayer);
        EnsureColliderExists(root);
    }

    private static bool ShouldAutoConfigureRoot(GameObject root)
    {
        if (root == null)
            return false;

        string name = root.name;
        return name.StartsWith("Medical_") || name.StartsWith("Equipment_") || name.StartsWith("Package_");
    }

    private static void SetLayerRecursively(Transform current, int layer)
    {
        current.gameObject.layer = layer;

        for (int i = 0; i < current.childCount; i++)
        {
            SetLayerRecursively(current.GetChild(i), layer);
        }
    }

    private static void EnsureColliderExists(GameObject root)
    {
        if (root.GetComponentInChildren<Collider>(true) != null)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        Bounds combinedBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combinedBounds.Encapsulate(renderers[i].bounds);
        }

        BoxCollider collider = root.GetComponent<BoxCollider>();
        if (collider == null)
            collider = root.AddComponent<BoxCollider>();

        collider.center = root.transform.InverseTransformPoint(combinedBounds.center);

        Vector3 localSize = root.transform.InverseTransformVector(combinedBounds.size);
        collider.size = new Vector3(
            Mathf.Abs(localSize.x),
            Mathf.Abs(localSize.y),
            Mathf.Abs(localSize.z));
    }
}
