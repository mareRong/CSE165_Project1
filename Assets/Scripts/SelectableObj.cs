using UnityEngine;

public class SelectableObject : MonoBehaviour
{
    public Color highlightColor = Color.yellow;

    private Renderer[] renderers;
    private Color[][] originalColors;
    private bool isHighlighted = false;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        originalColors = new Color[renderers.Length][];

        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] mats = renderers[i].materials;
            originalColors[i] = new Color[mats.Length];

            for (int j = 0; j < mats.Length; j++)
            {
                if (mats[j].HasProperty("_Color"))
                    originalColors[i][j] = mats[j].color;
            }
        }
    }

    public void SetHighlight(bool on)
    {
        if (isHighlighted == on) return;
        isHighlighted = on;

        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] mats = renderers[i].materials;

            for (int j = 0; j < mats.Length; j++)
            {
                if (!mats[j].HasProperty("_Color")) continue;
                mats[j].color = on ? highlightColor : originalColors[i][j];
            }
        }
    }
}