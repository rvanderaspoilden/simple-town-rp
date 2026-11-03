using DG.Tweening;
using Sim;
using TMPro;
using UnityEngine;

/// <summary>
/// Chiffre de dégâts flottant (« −20 ») affiché au point d'impact pour l'ATTAQUANT seul —
/// déclenché par les TargetRpc de confirmation de coup (PlayerShooter.TargetDamageDealt,
/// PlayerMelee.TargetDamageDealt), donc toujours la vérité serveur, jamais une estimation
/// client. Même famille que <see cref="WorldToast"/> : canvas world-space construit en code
/// (aucun prefab), billboard caméra, montée + fondu (~0.7 s) puis autodestruction.
///
/// Blanc par défaut, orange quand le coup met la cible KO ; la taille grossit légèrement
/// avec les dégâts (un tir de sniper se voit de loin). Léger décalage latéral aléatoire
/// pour que les coups enchaînés (poings à 0.45 s) ne se superposent pas.
/// </summary>
public class DamageNumber : MonoBehaviour
{
    private static readonly Color NormalColor   = new Color(1f, 1f, 1f, 1f);
    private static readonly Color KnockColor    = new Color(1f, 0.62f, 0.25f, 1f);
    private static readonly Color HeadshotColor = new Color(1f, 0.15f, 0.12f, 1f);
    private static readonly Color ShadowColor   = new Color(0f, 0f, 0f, 0.6f);

    private const float WorldScale = 0.006f;
    private const float Lifetime   = 0.7f;   // s, apparition → disparition complète
    private const float RiseHeight = 0.6f;   // m de montée sur la durée de vie

    private RectTransform _root;
    private CanvasGroup   _group;

    /// <summary>Fait apparaître un chiffre de dégâts au point d'impact (coordonnées monde).</summary>
    public static void Spawn(Vector3 worldPoint, float damage, bool knocked, bool headshot = false)
    {
        var go = new GameObject("DamageNumber");
        go.AddComponent<DamageNumber>().Build(worldPoint, damage, knocked, headshot);
    }

    private void Build(Vector3 worldPoint, float damage, bool knocked, bool headshot)
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 1090; // juste sous les WorldToasts (1100)

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable = false;
        _group.alpha = 0f;

        _root = (RectTransform)transform;
        _root.sizeDelta = new Vector2(160f, 70f);
        _root.position = worldPoint + new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0f, 0.1f), 0f);
        _root.localScale = Vector3.one * WorldScale * 0.6f;

        string text = "−" + Mathf.Max(1, Mathf.RoundToInt(damage));
        // Headshot : préfixe « ! » pour signaler le tir à la tête.
        if (headshot) text = "!" + text;
        // 30 pt pour les petits coups (poing), jusqu'à ~44 pt vers 50 dégâts (sniper).
        float fontSize = 30f + 14f * Mathf.Clamp01((damage - 5f) / 45f);
        // Headshot = rouge vif (prime sur le KO) + plus gros pour qu'il se remarque.
        if (headshot) fontSize += 10f;
        Color color = headshot ? HeadshotColor : (knocked ? KnockColor : NormalColor);

        // Ombre portée (duplicata noir décalé) : lisible sur fond clair comme sombre.
        MakeLabel("Shadow", text, fontSize, ShadowColor, new Vector2(2.5f, -2.5f));
        MakeLabel("Label", text, fontSize, color, Vector2.zero);

        // Fade-in + pop d'échelle, montée douce sur toute la vie, fondu final.
        Vector3 fullScale = Vector3.one * WorldScale;
        DOTween.Sequence().SetTarget(this)
            .Append(_group.DOFade(1f, 0.08f))
            .Join(_root.DOScale(fullScale, 0.14f).SetEase(Ease.OutBack))
            .AppendInterval(Lifetime - 0.14f - 0.25f)
            .Append(_group.DOFade(0f, 0.25f).SetEase(Ease.InCubic))
            .OnComplete(() => Destroy(gameObject));
        _root.DOMoveY(_root.position.y + RiseHeight, Lifetime).SetEase(Ease.OutCubic).SetTarget(this);
    }

    private void MakeLabel(string labelName, string text, float fontSize, Color color, Vector2 offset)
    {
        var go = new GameObject(labelName, typeof(RectTransform));
        go.transform.SetParent(_root, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = offset; rt.offsetMax = offset;

        var label = go.AddComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.fontStyle = FontStyles.Bold;
        label.fontSize = fontSize;
        label.color = color;
        label.raycastTarget = false;
        label.text = text;
    }

    private void LateUpdate()
    {
        Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : Camera.main;
        if (cam != null)
            _root.rotation = Quaternion.LookRotation(_root.position - cam.transform.position, Vector3.up);
    }

    private void OnDestroy() => DOTween.Kill(this);
}
