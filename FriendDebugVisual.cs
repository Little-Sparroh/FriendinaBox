using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Placeholder visuals for Friend field entities (mine / turret / drone / mortar).
/// Colored box so deployables are visible without custom art.
/// </summary>
public static class FriendDebugVisual
{
    public static readonly Color DefaultColor = Color.magenta;

    private static readonly Dictionary<int, Material> MaterialsByColor = new Dictionary<int, Material>();

    /// <summary>
    /// Attaches a solid colored cube under <paramref name="parent"/>.
    /// Safe to call multiple times — replaces any existing debug visual child.
    /// </summary>
    public static GameObject Attach(Transform parent, Color? color = null, float scale = 0.75f)
    {
        if (parent == null)
            return null;

        Transform existing = parent.Find("FriendDebugVisual");
        if (existing != null)
            Object.Destroy(existing.gameObject);

        Color c = color ?? DefaultColor;

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "FriendDebugVisual";
        cube.transform.SetParent(parent, worldPositionStays: false);
        cube.transform.localPosition = Vector3.up * (scale * 0.5f);
        cube.transform.localRotation = Quaternion.identity;
        cube.transform.localScale = Vector3.one * Mathf.Max(0.1f, scale);

        Collider col = cube.GetComponent<Collider>();
        if (col != null)
            Object.Destroy(col);

        Renderer renderer = cube.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = GetOrCreateMaterial(c);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        return cube;
    }

    private static Material GetOrCreateMaterial(Color color)
    {
        int key = color.GetHashCode();
        if (MaterialsByColor.TryGetValue(key, out Material existing) && existing != null)
            return existing;

        Shader shader = Shader.Find("Unlit/Color")
            ?? Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Standard");

        Material mat = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"));
        mat.name = $"FriendinaBox_Debug_{key:X8}";
        mat.hideFlags = HideFlags.HideAndDontSave;

        if (mat.HasProperty("_Color"))
            mat.color = color;
        else if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);

        MaterialsByColor[key] = mat;
        return mat;
    }
}
