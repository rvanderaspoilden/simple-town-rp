using UnityEngine;

/// <summary>
/// Stylized cozy destruction VFX played once when a prop is destroyed: a soft dust puff,
/// a flat ground dust ring, tumbling debris shards (wood / stone) flying out and falling,
/// and a brief warm impact flash. ~1.2s, low particle count (&lt;60), URP + bloom friendly.
///
/// Built procedurally in Awake (textures, materials, particle systems) so the prefab is a
/// single self-contained GameObject with no asset dependencies — same approach as
/// <see cref="TrashEcoVfx"/> / <see cref="ConstructionVfx"/>. Spawn with <see cref="SpawnAt"/>.
/// Networked per destruction by ClientPropManager (S2C_PropDestroyed).
/// </summary>
[DisallowMultipleComponent]
public class DestructionVfx : MonoBehaviour
{
    public const string ResourcePath = "VFX/VFX_Destruction";
    private const float LifeTime = 2.2f;

    // ── Palette ──────────────────────────────────────────────────────────────────
    private static readonly Color Dust       = new Color(0.82f, 0.76f, 0.64f, 1f);
    private static readonly Color DustWarm    = new Color(0.74f, 0.66f, 0.55f, 1f);
    private static readonly Color Wood        = new Color(0.60f, 0.42f, 0.24f, 1f);
    private static readonly Color WoodLight   = new Color(0.78f, 0.56f, 0.33f, 1f);
    private static readonly Color Stone       = new Color(0.62f, 0.60f, 0.57f, 1f);
    private static readonly Color Flash       = new Color(1.00f, 0.92f, 0.72f, 1f);

    private static Texture2D _softCircle;
    private static Texture2D _ring;
    private static Texture2D _chip;

    private Material _dustMat;  // alpha soft (dust puff / ground ring)
    private Material _ringMat;  // alpha ring
    private Material _chipMat;  // alpha rounded square (debris)
    private Material _glow;     // additive (impact flash)

    public static GameObject SpawnAt(Vector3 position)
    {
        var prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"[DestructionVfx] Prefab not found at Resources/{ResourcePath}");
            return null;
        }
        return Instantiate(prefab, position, Quaternion.identity);
    }

    private void Awake()
    {
        BuildTextures();
        BuildMaterials();

        BuildImpactFlash();
        BuildDustPuff();
        BuildGroundDust();
        BuildDebris();

        Destroy(gameObject, LifeTime);
    }

    // ── Textures ─────────────────────────────────────────────────────────────────

    private static void BuildTextures()
    {
        if (_softCircle == null) _softCircle = MakeRadial(64, 1.0f);
        if (_ring == null)       _ring       = MakeRing(96, 0.70f, 0.96f);
        if (_chip == null)       _chip       = MakeChip(48);
    }

    private static Texture2D MakeRadial(int size, float power)
    {
        Texture2D tex = NewTex(size);
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.0f) * power;
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }

    private static Texture2D MakeRing(int size, float inner, float outer)
    {
        Texture2D tex = NewTex(size);
        float c = (size - 1) * 0.5f;
        float mid = (inner + outer) * 0.5f;
        float half = (outer - inner) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(d - mid) / half), 1.5f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }

    private static Texture2D MakeChip(int size)
    {
        Texture2D tex = NewTex(size);
        float c = (size - 1) * 0.5f;
        float r = size * 0.16f;
        float half = size * 0.40f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = Mathf.Abs(x - c) - (half - r);
            float dy = Mathf.Abs(y - c) - (half - r);
            float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) + Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
            float inside = Mathf.Min(Mathf.Max(dx, dy), 0f);
            float a = Mathf.Clamp01(-(outside + inside - r) / 2f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }

    private static Texture2D NewTex(int size)
    {
        return new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
    }

    // ── Materials ─────────────────────────────────────────────────────────────────

    private void BuildMaterials()
    {
        Shader add = Shader.Find("Mobile/Particles/Additive");
        Shader alpha = Shader.Find("Mobile/Particles/Alpha Blended");
        if (add == null) add = Shader.Find("Legacy Shaders/Particles/Additive");
        if (alpha == null) alpha = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (alpha == null) alpha = add;

        _dustMat = new Material(alpha) { mainTexture = _softCircle };
        _ringMat = new Material(alpha) { mainTexture = _ring };
        _chipMat = new Material(alpha) { mainTexture = _chip };
        _glow    = new Material(add)   { mainTexture = _softCircle };
    }

    // ── Systems ──────────────────────────────────────────────────────────────────

    private void BuildImpactFlash()
    {
        ParticleSystem ps = NewChild("ImpactFlash", 0.3f);
        var main = ps.main;
        main.startLifetime = 0.22f;
        main.startSpeed = 0f;
        main.startSize = 0.8f;
        main.startColor = new Color(Flash.r, Flash.g, Flash.b, 0.9f);
        main.maxParticles = 2;

        ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, GrowCurve(0.6f, 2.2f));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeGradient(0.05f, 0.4f);

        ps.transform.localPosition = new Vector3(0f, 0.4f, 0f);
        ApplyRenderer(ps, _glow, ParticleSystemRenderMode.Billboard);
        ps.Play();
    }

    private void BuildDustPuff()
    {
        ParticleSystem ps = NewChild("DustPuff", 0.9f);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = -0.04f; // gently rise as it expands
        main.startColor = DustGradient();
        main.maxParticles = 18;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.25f;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, GrowCurve(0.6f, 1.6f));

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeGradient(0.15f, 0.55f);

        ps.transform.localPosition = new Vector3(0f, 0.35f, 0f);
        ApplyRenderer(ps, _dustMat, ParticleSystemRenderMode.Billboard);
        ps.Play();
    }

    private void BuildGroundDust()
    {
        ParticleSystem ps = NewChild("GroundDust", 0.8f);
        var main = ps.main;
        main.startLifetime = 0.7f;
        main.startSpeed = 0f;
        main.startSize = 0.5f;
        main.startColor = new Color(DustWarm.r, DustWarm.g, DustWarm.b, 0.7f);
        main.maxParticles = 2;

        ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, GrowCurve(0.5f, 2.8f));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeGradient(0.1f, 0.5f);

        ApplyRenderer(ps, _ringMat, ParticleSystemRenderMode.HorizontalBillboard);
        ps.Play();
    }

    private void BuildDebris()
    {
        ParticleSystem ps = NewChild("Debris", 1.0f);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.15f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 1.4f; // shards fly out then fall
        main.startColor = DebrisGradient();
        main.maxParticles = 22;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.15f;

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeGradient(0.05f, 0.7f);

        ps.transform.localPosition = new Vector3(0f, 0.3f, 0f);
        ApplyRenderer(ps, _chipMat, ParticleSystemRenderMode.Billboard);
        ps.Play();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private ParticleSystem NewChild(string childName, float duration)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = Mathf.Max(0.05f, duration);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        return ps;
    }

    private static void ApplyRenderer(ParticleSystem ps, Material mat, ParticleSystemRenderMode mode)
    {
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = mode;
        r.material = mat;
        r.sortMode = ParticleSystemSortMode.None;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    private static ParticleSystem.MinMaxGradient DustGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Dust, 0f), new GradientColorKey(DustWarm, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
    }

    private static ParticleSystem.MinMaxGradient DebrisGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(Wood, 0f),
                new GradientColorKey(WoodLight, 0.5f),
                new GradientColorKey(Stone, 1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
    }

    private static ParticleSystem.MinMaxGradient FadeGradient(float fadeIn, float holdUntil)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, Mathf.Clamp01(fadeIn)),
                new GradientAlphaKey(1f, Mathf.Clamp01(holdUntil)),
                new GradientAlphaKey(0f, 1f),
            });
        return new ParticleSystem.MinMaxGradient(g);
    }

    private static AnimationCurve GrowCurve(float from, float to)
    {
        var c = new AnimationCurve();
        c.AddKey(0f, from);
        c.AddKey(1f, to);
        return c;
    }
}
