using UnityEngine;

/// <summary>
/// Stylized cozy eco-validation VFX played when a trash bag is thrown into a bin.
/// Art direction: Animal Crossing / Sims 4 / Dreamlight Valley — soft pastel green burst,
/// gentle swirl, radial glow pulse and a flat ground ring. ~0.8s, low particle count,
/// additive/alpha-blended, URP + bloom friendly.
///
/// Everything (textures, materials, particle systems) is built procedurally in Awake so
/// the prefab is a single self-contained GameObject with no asset dependencies. Designers
/// can later replace this with an authored ParticleSystem prefab — TrashBehaviour just
/// instantiates Resources/VFX/VFX_TrashEco.
/// </summary>
[DisallowMultipleComponent]
public class TrashEcoVfx : MonoBehaviour
{
    // ── Pastel eco palette ──────────────────────────────────────────────────────
    private static readonly Color MintGreen       = new Color(0.60f, 0.95f, 0.75f, 1f);
    private static readonly Color SoftLime         = new Color(0.74f, 0.93f, 0.46f, 1f);
    private static readonly Color WarmYellowGreen  = new Color(0.86f, 0.95f, 0.55f, 1f);
    private static readonly Color PastelTurquoise  = new Color(0.62f, 0.93f, 0.88f, 1f);
    private static readonly Color SoftWhite        = new Color(0.95f, 1.00f, 0.92f, 1f);

    private static Texture2D _softCircle;
    private static Texture2D _leaf;
    private static Texture2D _ring;

    private Material _additiveSoft; // glowing motes / sparkles / glow pulse
    private Material _alphaLeaf;     // stylized leaves
    private Material _additiveRing;  // ground ring

    private void Awake()
    {
        BuildTextures();
        BuildMaterials();

        BuildBurst();
        BuildLeaves();
        BuildSparkles();
        BuildGlowPulse();
        BuildGroundRing();
    }

    // ── Textures (procedural, soft & rounded) ────────────────────────────────────

    private static void BuildTextures()
    {
        if (_softCircle == null) _softCircle = MakeRadial(64, 1.0f);
        if (_leaf == null)       _leaf       = MakeLeaf(64);
        if (_ring == null)       _ring       = MakeRing(96, 0.78f, 0.96f);
    }

    /// <summary>White texture with a soft radial alpha falloff (bokeh / dust mote).</summary>
    private static Texture2D MakeRadial(int size, float power)
    {
        Texture2D tex = NewTex(size);
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a = Mathf.Clamp01(1f - d);
            a = Mathf.Pow(a, 2.2f) * power;     // soft, no hard edge
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }

    /// <summary>Rounded stylized leaf (teardrop ellipse) with soft edges.</summary>
    private static Texture2D MakeLeaf(int size)
    {
        Texture2D tex = NewTex(size);
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float nx = (x - cx) / (size * 0.30f);            // narrow axis
            float ny = (y - cy) / (size * 0.46f);            // long axis
            float taper = Mathf.Lerp(1.4f, 0.5f, Mathf.Clamp01((y) / (float)size)); // teardrop
            float d = (nx * nx) / (taper * taper) + ny * ny;
            float a = Mathf.Clamp01(1f - d);
            a = Mathf.SmoothStep(0f, 1f, a);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }

    /// <summary>Soft annulus (ground ring), inner..outer radius as fractions.</summary>
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
            float a = Mathf.Clamp01(1f - Mathf.Abs(d - mid) / half);
            a = Mathf.Pow(a, 1.5f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }

    private static Texture2D NewTex(int size)
    {
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        return t;
    }

    // ── Materials ─────────────────────────────────────────────────────────────────

    private void BuildMaterials()
    {
        // Mobile particle shaders are always available and behave well under URP — additive
        // for glowing sparkles/motes (bloom-friendly), alpha-blended for leaves.
        Shader add = Shader.Find("Mobile/Particles/Additive");
        Shader alpha = Shader.Find("Mobile/Particles/Alpha Blended");
        if (add == null) add = Shader.Find("Legacy Shaders/Particles/Additive");
        if (alpha == null) alpha = Shader.Find("Legacy Shaders/Particles/Alpha Blended");

        _additiveSoft = new Material(add) { mainTexture = _softCircle };
        _additiveRing = new Material(add) { mainTexture = _ring };
        _alphaLeaf    = new Material(alpha != null ? alpha : add) { mainTexture = _leaf };
    }

    // ── Particle systems ───────────────────────────────────────────────────────────

    /// <summary>Main soft burst: pastel green/mint motes rising with a gentle swirl.</summary>
    private void BuildBurst()
    {
        ParticleSystem ps = NewChild("Burst");
        var main = ps.main;
        main.duration = 0.8f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
        main.gravityModifier = -0.25f;             // gentle float-up
        main.startColor = PaletteGradient();
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 40;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.12f;                      // from inside the bin
        shape.rotation = new Vector3(-90f, 0f, 0f); // open upward

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        // All velocity curves must share the same mode → keep them constant.
        vel.radial = new ParticleSystem.MinMaxCurve(0.25f);  // gentle outward expansion
        vel.y = new ParticleSystem.MinMaxCurve(0.6f);

        var noise = ps.noise;                       // slight swirl
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(0.35f);
        noise.frequency = 0.5f;
        noise.scrollSpeed = 0.4f;

        var sol = ps.sizeOverLifetime;              // soft scale-up then scale-down
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, SizePopCurve());

        var col = ps.colorOverLifetime;             // smooth fade out
        col.enabled = true;
        col.color = FadeGradient();

        ApplyRenderer(ps, _additiveSoft, ParticleSystemRenderMode.Billboard);
        ps.Play();
    }

    /// <summary>A few stylized leaves drifting up, alpha-blended, slowly rotating.</summary>
    private void BuildLeaves()
    {
        ParticleSystem ps = NewChild("Leaves");
        var main = ps.main;
        main.duration = 0.8f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.18f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = -0.15f;
        main.startColor = LeafGradient();
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 16;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 22f;
        shape.radius = 0.12f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.radial = new ParticleSystem.MinMaxCurve(0.2f);
        vel.y = new ParticleSystem.MinMaxCurve(0.45f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, SizePopCurve());

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeGradient();

        ApplyRenderer(ps, _alphaLeaf, ParticleSystemRenderMode.Billboard);
        ps.Play();
    }

    /// <summary>Tiny bright sparkles with a quick twinkle.</summary>
    private void BuildSparkles()
    {
        ParticleSystem ps = NewChild("Sparkles");
        var main = ps.main;
        main.duration = 0.8f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.10f);
        main.gravityModifier = -0.2f;
        main.startColor = SoftWhite;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 16;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.05f, 10) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.2f;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, TwinkleCurve());

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeGradient();

        ApplyRenderer(ps, _additiveSoft, ParticleSystemRenderMode.Billboard);
        ps.Play();
    }

    /// <summary>Single soft radial glow pulse from the bin.</summary>
    private void BuildGlowPulse()
    {
        ParticleSystem ps = NewChild("GlowPulse");
        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.startLifetime = 0.45f;
        main.startSpeed = 0f;
        main.startSize = 0.6f;
        main.startColor = new Color(MintGreen.r, MintGreen.g, MintGreen.b, 0.55f);
        main.maxParticles = 2;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        var shape = ps.shape;
        shape.enabled = false;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, GrowCurve(1f, 3.2f));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeGradient();

        ApplyRenderer(ps, _additiveSoft, ParticleSystemRenderMode.Billboard);
        ps.Play();
    }

    /// <summary>Flat expanding soft ring at ground level.</summary>
    private void BuildGroundRing()
    {
        ParticleSystem ps = NewChild("GroundRing");
        ps.transform.localPosition = new Vector3(0f, -0.65f, 0f); // bring back to ground from bin opening

        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.startLifetime = 0.5f;
        main.startSpeed = 0f;
        main.startSize = 0.4f;
        main.startColor = new Color(SoftLime.r, SoftLime.g, SoftLime.b, 0.7f);
        main.maxParticles = 2;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        var shape = ps.shape;
        shape.enabled = false;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, GrowCurve(0.4f, 2.6f));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeGradient();

        // Horizontal billboard → lies flat on the ground.
        ApplyRenderer(ps, _additiveRing, ParticleSystemRenderMode.HorizontalBillboard);
        ps.Play();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private ParticleSystem NewChild(string childName)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = false;                 // we Play() explicitly after config
        var emission = ps.emission;
        emission.rateOverTime = 0f;               // burst-only by default
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

    private static ParticleSystem.MinMaxGradient PaletteGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(MintGreen, 0f),
                new GradientColorKey(SoftLime, 0.4f),
                new GradientColorKey(WarmYellowGreen, 0.7f),
                new GradientColorKey(PastelTurquoise, 1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
    }

    private static ParticleSystem.MinMaxGradient LeafGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(SoftLime, 0f),
                new GradientColorKey(MintGreen, 0.5f),
                new GradientColorKey(WarmYellowGreen, 1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
    }

    /// <summary>Alpha curve over lifetime: fade in fast, hold, smooth fade out.</summary>
    private static ParticleSystem.MinMaxGradient FadeGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(1f, 0.65f),
                new GradientAlphaKey(0f, 1f),
            });
        return new ParticleSystem.MinMaxGradient(g);
    }

    /// <summary>Scale up quickly, hold, then soft scale-down before disappearing.</summary>
    private static AnimationCurve SizePopCurve()
    {
        var c = new AnimationCurve();
        c.AddKey(0f, 0.2f);
        c.AddKey(0.2f, 1f);
        c.AddKey(0.7f, 0.95f);
        c.AddKey(1f, 0f);
        return c;
    }

    private static AnimationCurve TwinkleCurve()
    {
        var c = new AnimationCurve();
        c.AddKey(0f, 0f);
        c.AddKey(0.3f, 1f);
        c.AddKey(0.6f, 0.3f);
        c.AddKey(0.85f, 0.8f);
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
