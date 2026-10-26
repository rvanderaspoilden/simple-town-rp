using Sim.Audio;
using UnityEngine;

/// <summary>
/// Item « arme à feu » (ex. Glock). Étend ItemBehaviour avec la résolution de la bouche du
/// canon (enfant "Muzzle") et les effets de tir purement visuels/sonores : flash lumineux,
/// tracer, son, recul du modèle dans la main. La logique de tir (input, validation serveur,
/// knockdown) vit dans <see cref="Sim.PlayerShooter"/> — ce composant tourne sur toutes les
/// copies et ne fait que du feedback.
/// </summary>
public class WeaponBehaviour : ItemBehaviour {
    private const float TracerLifetime = 0.05f;
    private const float FlashLifetime = 0.05f;
    private const float RecoilDistance = 0.06f;
    private const float RecoilRecoverSpeed = 8f;

    private Transform _muzzle;
    private Transform _foregrip;

    // Recul du modèle : offset local décroissant appliqué par-dessus la position de grip.
    private float _recoil;
    private Vector3 _restLocalPosition;
    private bool _restCaptured;

    private static Material _tracerMaterial;

    public WeaponConfig WeaponConfiguration => Configuration as WeaponConfig;

    /// <summary>Bouche du canon (enfant "Muzzle" du prefab), origine du tracer/flash/son.
    /// Retombe sur le transform de l'arme si le prefab n'a pas de Muzzle.</summary>
    public Vector3 MuzzlePosition {
        get {
            if (_muzzle == null) _muzzle = transform.Find("Muzzle");
            return _muzzle != null ? _muzzle.position : transform.position;
        }
    }

    /// <summary>Garde-main (enfant "Foregrip" du prefab) : cible IK de la main de SOUTIEN
    /// pour les armes TWO_HAND (ex. AK-47). Null si le prefab n'en a pas (arme à une main).</summary>
    public Transform Foregrip {
        get {
            if (_foregrip == null) _foregrip = transform.Find("Foregrip");
            return _foregrip;
        }
    }

    /// <summary>Action « Recharger » du menu d'inventaire (clic droit sur l'arme en main) —
    /// route vers le PlayerShooter local, mêmes gardes et flux serveur que la touche R.</summary>
    protected override void HandleSpecialAction(Sim.Interactables.Action action) {
        if (action.Type != Sim.Enums.ActionTypeEnum.RELOAD) return;
        if (Sim.PlayerController.Local == null) return;
        Sim.PlayerShooter shooter = Sim.PlayerController.Local.GetComponent<Sim.PlayerShooter>();
        if (shooter != null) shooter.RequestReload();
    }

    public override void OnDetachedFromHand() {
        base.OnDetachedFromHand();
        // Au sol, plus de recul en cours ni de position de repos « main » valable. NE PAS
        // restaurer _restLocalPosition ici : le manager vient de poser l'arme au sol
        // (position MONDE, déparentée) — réécrire l'ancienne position locale de main la
        // téléporterait près de l'origine du monde (bug « l'arme disparaît au drop »).
        _recoil = 0f;
        _restCaptured = false;
    }

    private void Update() {
        if (_recoil <= 0f) return;

        _recoil = Mathf.MoveTowards(_recoil, 0f, Time.deltaTime * RecoilRecoverSpeed);
        if (_restCaptured) {
            // Kick le long du -Z local de l'arme (canon vers l'arrière), dans l'espace de la main.
            Vector3 kick = transform.localRotation * new Vector3(0f, 0f, -RecoilDistance * _recoil);
            transform.localPosition = _restLocalPosition + kick;
        }
    }

    /// <summary>Joue les effets d'un tir sur CE client : tracer bouche→impact, flash lumineux,
    /// son 3D et recul du modèle. Appelé par PlayerShooter.RpcFired sur toutes les copies.</summary>
    public void PlayFireEffects(Vector3 hitPoint) {
        Vector3 muzzle = MuzzlePosition;

        if (IsHeld && !_restCaptured) {
            _restLocalPosition = transform.localPosition;
            _restCaptured = true;
        }
        _recoil = 1f;

        SpawnTracer(muzzle, hitPoint);
        SpawnFlash(muzzle);

        WeaponConfig cfg = WeaponConfiguration;
        if (cfg != null && cfg.FireSound != null && AudioManager.Instance != null) {
            AudioManager.Instance.PlayClip3D(cfg.FireSound, muzzle, cfg.FireSoundVolume);
        }
    }

    private static void SpawnTracer(Vector3 from, Vector3 to) {
        if (_tracerMaterial == null) {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return;
            _tracerMaterial = new Material(shader);
        }

        var go = new GameObject("BulletTracer");
        var line = go.AddComponent<LineRenderer>();
        line.material = _tracerMaterial;
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        line.startWidth = 0.02f;
        line.endWidth = 0.005f;
        line.startColor = new Color(1f, 0.9f, 0.55f, 0.9f);
        line.endColor = new Color(1f, 0.85f, 0.4f, 0.15f);
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        Destroy(go, TracerLifetime);
    }

    private static void SpawnFlash(Vector3 at) {
        var go = new GameObject("MuzzleFlash");
        go.transform.position = at;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.8f, 0.45f);
        light.intensity = 4f;
        light.range = 6f;
        light.shadows = LightShadows.None;
        Destroy(go, FlashLifetime);
    }
}
