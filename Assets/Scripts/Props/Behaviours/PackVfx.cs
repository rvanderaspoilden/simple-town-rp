using UnityEngine;

/// <summary>
/// Cozy "packing" VFX played once when a prop is packed into a colis: a warm gold sparkle
/// puff that rises and converges, a soft ground ring, and a brief flash — plus a short
/// "thud" sound. ~1.1s, low particle count, URP + bloom friendly.
///
/// Built procedurally in Awake (no asset dependency) — same approach as
/// <see cref="DestructionVfx"/>. Spawn with <see cref="SpawnAt"/> (creates its own GameObject,
/// no prefab needed). Networked per pack by ClientPropManager (S2C_PropPacked).
/// </summary>
[DisallowMultipleComponent]
public class PackVfx : MonoBehaviour
{
    private const float  LifeTime  = 1.6f;

    private static readonly Color Gold      = new Color(1.00f, 0.84f, 0.42f, 1f);
    private static readonly Color GoldWarm  = new Color(0.98f, 0.70f, 0.30f, 1f);
    private static readonly Color Flash     = new Color(1.00f, 0.94f, 0.72f, 1f);

    private static Texture2D _softCircle;
    private static Texture2D _ring;

    private Material _dustMat;
    private Material _ringMat;
    private Material _glow;

    public static GameObject SpawnAt(Vector3 position)
    {
        var go = new GameObject("PackVfx");
        go.transform.position = position;
        go.AddComponent<PackVfx>();
        return go;
    }

    private void Awake()
    {
        BuildTextures();
        BuildMaterials();

        BuildFlash();
        BuildSparkles();
        BuildGroundRing();

        Sim.Audio.AudioManager.Instance.Play(Sim.Audio.SfxId.PropPack, transform.position);

        Destroy(gameObject, LifeTime);
    }

    // ── Textures ─────────────────────────────────────────────────────────────────

    private static void BuildTextures()
    {
        if (_softCircle == null) _softCircle = MakeRadial(64);
        if (_ring == null)       _ring       = MakeRing(96, 0.70f, 0.96f);
    }

    private static Texture2D MakeRadial(int size)
    {
        Texture2D tex = NewTex(size);
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.0f);
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

        _dustMat = new Material(add)   { mainTexture = _softCircle };
        _ringMat = new Material(alpha) { mainTexture = _ring };
        _glow    = new Material(add)   { mainTexture = _softCircle };
    }

    // ── Systems ──────────────────────────────────────────────────────────────────

    private void BuildFlash()
    {
        ParticleSystem ps = NewChild("Flash", 0.3f);
        var main = ps.main;
        main.startLifetime = 0.22f;
        main.startSpeed = 0f;
        main.startSize = 0.7f;
        main.startColor = new Color(Flash.r, Flash.g, Flash.b, 0.9f);
        main.maxParticles = 2;

        ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, GrowCurve(0.6f, 2.0f));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeGradient(0.05f, 0.4f);

        ps.transform.localPosition = new Vector3(0f, 0.4f, 0f);
        ApplyRenderer(ps, _glow, ParticleSystemRenderMode.Billboard);
        ps.Play();
    }

    private void BuildSparkles()
    {
        ParticleSystem ps = NewChild("Sparkles", 0.9f);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
        main.gravityModifier = -0.06f; // rise gently as it fades
        main.startColor = SparkleGradient();
        main.maxParticles = 24;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 20) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.3f;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, ShrinkCurve());

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeGradient(0.1f, 0.5f);

        ps.transform.localPosition = new Vector3(0f, 0.35f, 0f);
        ApplyRenderer(ps, _dustMat, ParticleSystemRenderMode.Billboard);
        ps.Play();
    }

    private void BuildGroundRing()
    {
        ParticleSystem ps = NewChild("GroundRing", 0.7f);
        var main = ps.main;
        main.startLifetime = 0.6f;
        main.startSpeed = 0f;
        main.startSize = 0.5f;
        main.startColor = new Color(GoldWarm.r, GoldWarm.g, GoldWarm.b, 0.6f);
        main.maxParticles = 2;

        ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, GrowCurve(0.5f, 2.6f));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeGradient(0.1f, 0.5f);

        ApplyRenderer(ps, _ringMat, ParticleSystemRenderMode.HorizontalBillboard);
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

    private static ParticleSystem.MinMaxGradient SparkleGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Gold, 0f), new GradientColorKey(GoldWarm, 1f) },
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

    private static AnimationCurve ShrinkCurve()
    {
        var c = new AnimationCurve();
        c.AddKey(0f, 1f);
        c.AddKey(1f, 0.2f);
        return c;
    }
}
