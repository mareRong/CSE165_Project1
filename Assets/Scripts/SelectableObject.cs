using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SelectableObject : MonoBehaviour
{
    public enum HighlightChannel
    {
        GroupSelection
    }

    [SerializeField] private Color highlightColor = new Color(1f, 0.92156863f, 0.015686275f, 1f);

    private readonly HashSet<HighlightChannel> activeChannels = new HashSet<HighlightChannel>();
    private Renderer[] cachedRenderers;
    private MaterialPropertyBlock[] originalBlocks;

    private void Awake()
    {
        CacheRenderers();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            CacheRenderers();
        }
    }

    public void SetHighlight(bool enabled, HighlightChannel channel)
    {
        CacheRenderers();

        if (enabled)
        {
            if (activeChannels.Count == 0)
            {
                SaveOriginalBlocks();
            }

            activeChannels.Add(channel);
            ApplyHighlightColor();
            return;
        }

        activeChannels.Remove(channel);

        if (activeChannels.Count == 0)
        {
            RestoreOriginalBlocks();
        }
        else
        {
            ApplyHighlightColor();
        }
    }

    private void CacheRenderers()
    {
        if (cachedRenderers == null || cachedRenderers.Length == 0)
        {
            cachedRenderers = GetComponentsInChildren<Renderer>(true);
        }
    }

    private void SaveOriginalBlocks()
    {
        if (cachedRenderers == null)
        {
            return;
        }

        originalBlocks = new MaterialPropertyBlock[cachedRenderers.Length];

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            originalBlocks[i] = new MaterialPropertyBlock();

            if (cachedRenderers[i] != null)
            {
                cachedRenderers[i].GetPropertyBlock(originalBlocks[i]);
            }
        }
    }

    private void RestoreOriginalBlocks()
    {
        if (cachedRenderers == null)
        {
            return;
        }

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            if (cachedRenderers[i] == null)
            {
                continue;
            }

            if (originalBlocks != null && i < originalBlocks.Length && originalBlocks[i] != null)
            {
                cachedRenderers[i].SetPropertyBlock(originalBlocks[i]);
            }
            else
            {
                cachedRenderers[i].SetPropertyBlock(null);
            }
        }
    }

    private void ApplyHighlightColor()
    {
        if (cachedRenderers == null)
        {
            return;
        }

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            Renderer rend = cachedRenderers[i];
            if (rend == null)
            {
                continue;
            }

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            rend.GetPropertyBlock(block);
            block.SetColor("_Color", highlightColor);
            block.SetColor("_BaseColor", highlightColor);
            rend.SetPropertyBlock(block);
        }
    }
}
