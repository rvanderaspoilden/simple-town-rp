using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stylized cozy construction VFX played at a prop's location while it is being built.
/// Art direction: Animal Crossing / Sims 4 / Dreamlight Valley — soft, warm, readable.
/// Low particle count (&lt;120), additive/alpha-blended, URP + bloom friendly.
///
/// The build phase LOOPS for an arbitrary duration (build times vary a lot), then the
/// caller triggers the one-shot finale via <see cref="PlayFinale"/> when construction
/// completes (or just destroys the object on cancel). Driven over the network per prop
/// (see PropBehaviourBase.ApplyConstructionVfx) so every client sees it.
///
/// Built entirely procedurally in Awake (textures, materials, particle systems) so the
/// prefab is a single self-contained GameObject with no asset dependencies — same approach
/// as <see cref="TrashEcoVfx"/>.
///
/// Phases:
///  Loop : ground decal ring + soft green glow, blueprint motes rising, orbiting debris
///         (wood chips / planks / paper) — repeat until the build ends.
///  (mesh reveal / vertical dissolve is a shader on the PROP material — see notes)
///  Finale: golden radial burst + sparkles, then a few leaves floating upward.
/// </summary>
[DisallowMultipleComponent]
public class ConstructionVfx : MonoBehaviour
{
    /// <summary>Resources path of the prefab carrying this component.</summary>
    public const string ResourcePath = "VFX/VFX_Construction";

    private const float SafetyLifeTime = 30f; // hard cap so a stuck loop never leaks
    private const float FinaleLifeTime = 2.0f; // time to let the finale + loop tails fade

    // ── Cozy construction palette ────────────────────────────────────────────────
    private static readonly Color GroundGreen    = new Color(0.55f, 0.92f, 0.62f, 1f);
    private static readonly Color BlueprintCyan   = new Color(0.58f, 0.86f, 1.00f, 1f);
    private static readonly Color BlueprintWhite  = new Color(0.86f, 0.95f, 1.00f, 1f);
    private static readonly Color Wood            = new Color(0.62f, 0.43f, 0.24f, 1f);
    private static readonly Color WoodLight       = new Color(0.80f, 0.58f, 0.34f, 1f);
    private static readonly Color Paper           = new Color(0.96f, 0.93f, 0.84f, 1f);
    private static readonly Color Gold            = new Color(1.00f, 0.82f, 0.32f, 1f);
    private static readonly Color GoldBright      = new Color(1.00f, 0.96f, 0.62f, 1f);
    private static readonly Color LeafGreen       = new Color(0.56f, 0.85f, 0.45f, 1f);

    private static Texture2D _softCircle;
    private static Texture2D _ring;
    private static Texture2D _chip;
    private static Texture2D _leaf;

    private Material _glow;    // additive soft dot (glow / blueprint / sparkles)
    private Material _ringMat; // additive ring (ground decal)
    private Material _chipMat; // alpha rounded square (wood / paper debris)
    private Material _leafMat; // alpha leaf

    private readonly List<ParticleSystem> _loops = new();   // play while building
    private readonly List<ParticleSystem> _finale = new();  // played once on completion
    private bool _finishing;

    // ── Spawning ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Instantiate the looping construction VFX at a world position. The caller must call
    /// <see cref="PlayFinale"/> on completion, or Destroy the object to cancel.
    /// </summary>
    public static ConstructionVfx SpawnAt(Vector3 position)
    {
        var prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"[ConstructionVfx] Prefab not found at Resources/{ResourcePath}");
            return null;
        }
        return Instantiate(prefab, position, Quaternion.identity).GetComponent<ConstructionVfx>();
    }

    private void Awake()
    {
        BuildTextures();
        BuildMaterials();

        // Looping build phase.
        BuildGroundDecal();
        BuildGroundGlow();
        BuildBlueprint();
        BuildDebris();
        foreach (var ps in _loops) ps.Play();

        // Finale systems are configured now but only played by PlayFinale().
        BuildGoldGlow();
        BuildSparkles();
        BuildLeaves();

        Destroy(gameObject, SafetyLifeTime);
    }

    /// <summary>Stop the looping build phase (let it fade) and play the golden finale.</summary>
    public void PlayFinale()
    {
        if (_finishing) return;
        _finishing = true;

        foreach (var ps in _loops)
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); // existing particles fade

        foreach (var ps in _finale)
            if (ps != null) ps.Play();

        Destroy(gameObject, FinaleLifeTime);
    }

    // ── Textures (procedural, soft & rounded) ────────────────────────────────────

    private static void BuildTextures()
    {
        if (_softCircle == null) _softCircle = MakeRadial(64, 1.0f);
        if (_ring == null)       _ring       = MakeRing(96, 0.74f, 0.96f);
        if (_chip == null)       _chip       = MakeChip(48);
        if (_leaf == null)       _leaf       = MakeLeaf(64);
    }

    private static Texture2D MakeRadial(int size, float power)
    {
        Texture2D tex = NewTex(size);
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f) * power;
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

    /// <summary>Rounded square (wood chip / plank / paper fragment).</summary>
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
            float dist = (outside + inside) - r;
            float a = Mathf.Clamp01(-dist / 2f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }

    private static Texture2D MakeLeaf(int size)
    {
        Texture2D tex = NewTex(size);
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float nx = (x - cx) / (size * 0.30f);
            float ny = (y - cy) / (size * 0.46f);
            float taper = Mathf.Lerp(1.4f, 0.5f, Mathf.Clamp01(y / (float)size));
            float d = (nx * nx) / (taper * taper) + ny * ny;
            float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1f - d));
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

        _glow    = new Material(add)   { mainTexture = _softCircle };
        _ringMat = new Material(add)   { mainTexture = _ring };
        _chipMat = new Material(alpha) { mainTexture = _chip };
        _leafMat = new Material(alpha) { mainTexture = _leaf };
    }

    // ── Looping build systems ────────────────────────────────────────────────────

    private void BuildGroundDecal()
    {
        ParticleSystem ps = NewChild("GroundDecal", 1.6f, true, _loops);
        var main = ps.main;
        main.startLifetime = 1.6f;
        main.startSpeed = 0f;
        main.startSize = 0.5f;
        main.startColor = new Color(GroundGreen.r, GroundGreen.g, GroundGreen.b, 0.8f);
        main.maxParticles = 4;

        ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, GrowCurve(0.5f, 2.2f));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeGradient(0.2f, 0.75f);

        ApplyRenderer(ps, _ringMat, ParticleSystemRenderMode.HorizontalBillboard);
    }

    private void BuildGroundGlow()
    {
        ParticleSystem ps = NewChild("GroundGlow", 1.6f, true, _loops);
        var main = ps.main;
        main.startLifetime = 1.6f;
        main.startSpeed = 0f;
        main.startSize = 1.2f;
        main.startColor = new Color(GroundGreen.r, GroundGreen.g, GroundGreen.b, 0.4f);
        main.maxParticles = 4;

        ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, PulseCurve());

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeGradient(0.25f, 0.7f);

        ApplyRenderer(ps, _glow, ParticleSystemRenderMode.HorizontalBillboard);
    }

    private void BuildBlueprint()
    {
        ParticleSystem ps = NewChild("Blueprint", 1.5f, true, _loops);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
        main.startColor = BlueprintGradient();
        main.maxParticles = 40;

        var emission = ps.emission;
        emission.rateOverTime = 22f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.45f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.y = new ParticleSystem.MinMaxCurve(0.7f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, SizePopCurve());

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeGradient(0.2f, 0.7f);

        ApplyRenderer(ps, _glow, ParticleSystemRenderMode.Billboard);
    }

    private void BuildDebris()
    {
        ParticleSystem ps = NewChild("Debris", 1.5f, true, _loops);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.3f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = DebrisGradient();
        main.maxParticles = 28;

        var emission = ps.emission;
        emission.rateOverTime = 16f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Donut;
        shape.radius = 0.5f;
        shape.donutRadius = 0.12f;
        shape.position = new Vector3(0f, 0.45f, 0f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.orbitalY = new ParticleSystem.MinMaxCurve(1.2f);
        vel.radial = new ParticleSystem.MinMaxCurve(0.05f);

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-2f, 2f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, SizePopCurve());

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeGradient(0.15f, 0.7f);

        ApplyRenderer(ps, _chipMat, ParticleSystemRenderMode.Billboard);
    }

    // ── Finale systems (played by PlayFinale) ────────────────────────────────────

    private void BuildGoldGlow()
    {
        ParticleSystem ps = NewChild("GoldGlow", 0.6f, false, _finale);
        var main = ps.main;
        main.startLifetime = 0.5f;
        main.startSpeed = 0f;
        main.startSize = 0.7f;
        main.startColor = new Color(Gold.r, Gold.g, Gold.b, 0.8f);
        main.maxParticles = 2;

        ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, GrowCurve(0.6f, 3.4f));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeGradient(0.05f, 0.6f);

        ps.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        ApplyRenderer(ps, _glow, ParticleSystemRenderMode.Billboard);
    }

    private void BuildSparkles()
    {
        ParticleSystem ps = NewChild("Sparkles", 0.7f, false, _finale);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
        main.gravityModifier = -0.1f;
        main.startColor = SparkleGradient();
        main.maxParticles = 30;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 26) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.25f;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, TwinkleCurve());

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeGradient(0.1f, 0.6f);

        ps.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        ApplyRenderer(ps, _glow, ParticleSystemRenderMode.Billboard);
    }

    private void BuildLeaves()
    {
        ParticleSystem ps = NewChild("Leaves", 0.6f, false, _finale);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.18f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = -0.12f;
        main.startColor = LeafGreen;
        main.maxParticles = 10;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 7) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 20f;
        shape.radius = 0.2f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        shape.position = new Vector3(0f, 0.3f, 0f);

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-1.3f, 1.3f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.y = new ParticleSystem.MinMaxCurve(0.4f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, SizePopCurve());

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeGradient(0.2f, 0.6f);

        ApplyRenderer(ps, _leafMat, ParticleSystemRenderMode.Billboard);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private ParticleSystem NewChild(string childName, float duration, bool loop, List<ParticleSystem> bucket)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();

        // A ParticleSystem auto-plays on add; editing main.duration while playing throws.
        // Stop & clear, configure, then Play() is called explicitly (loops in Awake,
        // finale systems in PlayFinale).
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = loop;
        main.duration = Mathf.Max(0.05f, duration);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        bucket.Add(ps);
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

    private static ParticleSystem.MinMaxGradient BlueprintGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(BlueprintCyan, 0f), new GradientColorKey(BlueprintWhite, 1f) },
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
                new GradientColorKey(Paper, 1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
    }

    private static ParticleSystem.MinMaxGradient SparkleGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(GoldBright, 0f), new GradientColorKey(Gold, 1f) },
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

    private static AnimationCurve SizePopCurve()
    {
        var c = new AnimationCurve();
        c.AddKey(0f, 0.2f);
        c.AddKey(0.2f, 1f);
        c.AddKey(0.7f, 0.95f);
        c.AddKey(1f, 0f);
        return c;
    }

    /// <summary>Grow in, gently breathe, used by the persistent ground glow loop.</summary>
    private static AnimationCurve PulseCurve()
    {
        var c = new AnimationCurve();
        c.AddKey(0f, 0.7f);
        c.AddKey(0.5f, 1.15f);
        c.AddKey(1f, 0.7f);
        return c;
    }

    private static AnimationCurve TwinkleCurve()
    {
        var c = new AnimationCurve();
        c.AddKey(0f, 0f);
        c.AddKey(0.3f, 1f);
        c.AddKey(0.6f, 0.3f);
        c.AddKey(0.85f, 0.9f);
        c.AddKey(1f, 0f);
        return c;
    }

    private static AnimationCurve GrowCurve(float from, float to)
    {
        var c = new AnimationCurve();
        c.AddKey(0f, from);
        c.AddKey(1f, to);
        return c;
    }
}
