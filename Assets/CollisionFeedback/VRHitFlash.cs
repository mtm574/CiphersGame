using UnityEngine;
using UnityEngine.Rendering;

public sealed class VRHitFlash : MonoBehaviour
{
    Material material;
    GameObject screen;
    float remaining;
    public float HoldSeconds = 0.1f;
    public float FadeSeconds = 0.1f;
    public float Alpha { get; private set; }

    public void Initialize(Camera camera, Shader shader)
    {
        material = new Material(shader) { name = "VR hit flash (runtime)" };
        screen = new GameObject("VR Hit Flash", typeof(MeshFilter), typeof(MeshRenderer));
        screen.layer = 2; // Ignore Raycast, already visible to the existing VR camera.
        screen.transform.SetParent(camera.transform, false);
        screen.transform.localPosition = Vector3.forward * (camera.nearClipPlane + 0.05f);
        screen.transform.localScale = Vector3.one * 10f;
        var mesh = new Mesh { name = "VR flash quad" };
        mesh.vertices = new[] { new Vector3(-1,-1,0), new Vector3(-1,1,0), new Vector3(1,1,0), new Vector3(1,-1,0) };
        mesh.triangles = new[] { 0,1,2,0,2,3 };
        mesh.RecalculateBounds();
        screen.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = screen.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.allowOcclusionWhenDynamic = false;
        Clear();
    }
    public void Flash() { remaining = HoldSeconds + FadeSeconds; SetAlpha(1f); }
    void Update() { Tick(Time.unscaledDeltaTime); }
    public void Tick(float seconds)
    {
        remaining = Mathf.Max(0f, remaining - seconds);
        SetAlpha(remaining > FadeSeconds ? 1f : Mathf.Clamp01(remaining / Mathf.Max(0.001f, FadeSeconds)));
    }
    public void Clear() { remaining = 0f; SetAlpha(0f); }
    void SetAlpha(float alpha)
    {
        Alpha = alpha;
        if (material != null) material.SetColor("_Color", new Color(1f, 0f, 0f, alpha));
        if (screen != null) screen.SetActive(alpha > 0f);
    }
    void OnDisable() { Clear(); }
    static void Release(Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    void OnDestroy()
    {
        if (screen != null) { Release(screen.GetComponent<MeshFilter>().sharedMesh); Release(screen); }
        if (material != null) Release(material);
    }
}
