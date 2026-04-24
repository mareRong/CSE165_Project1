using UnityEngine;

public class SelectableObject : MonoBehaviour
{
    public enum HighlightChannel
    {
        SingleSelection,
        GroupSelection
    }

    public Color highlightColor = Color.yellow;

    private Renderer[] renderers;
    private Color[][] originalColors;
    private bool isHighlighted = false;
    private bool singleSelected;
    private bool groupSelected;

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
        SetHighlight(on, HighlightChannel.SingleSelection);
    }

    public void SetHighlight(bool on, HighlightChannel channel)
    {
        if (channel == HighlightChannel.SingleSelection)
            singleSelected = on;
        else
            groupSelected = on;

        bool shouldHighlight = singleSelected || groupSelected;

        if (isHighlighted == shouldHighlight)
            return;

        isHighlighted = shouldHighlight;

        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] mats = renderers[i].materials;

            for (int j = 0; j < mats.Length; j++)
            {
                if (!mats[j].HasProperty("_Color")) continue;
                mats[j].color = shouldHighlight ? highlightColor : originalColors[i][j];
            }
        }
    }
}
