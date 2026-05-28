using System.Collections;
using UnityEngine;

/// <summary>
/// Anime un pivot (porte de frigo, couvercle de placard, tiroir…) entre deux orientations
/// en réponse à <see cref="StorageContainerBehaviour.SetOpenState"/>. 100% code, pas d'Animator :
/// coût nul au repos, setup minimal sur le prefab (Hinge + euler fermé/ouvert + durée).
///
/// La rotation est appliquée en LOCAL sur <see cref="hinge"/>, donc le pivot doit déjà être
/// l'enfant placé à l'emplacement du gond (côté charnière de la porte) — pas l'origine du prop.
/// </summary>
public class ContainerDoorAnimator : MonoBehaviour
{
    [Tooltip("Transform à faire pivoter (la porte, le couvercle…). Défaut = ce transform.")]
    [SerializeField] private Transform hinge;
    [Tooltip("Euler local en position fermée.")]
    [SerializeField] private Vector3 closedEuler = Vector3.zero;
    [Tooltip("Euler local en position ouverte.")]
    [SerializeField] private Vector3 openEuler = new Vector3(0f, 90f, 0f);
    [Tooltip("Durée de l'interpolation (secondes).")]
    [SerializeField] private float duration = 0.3f;
    [Tooltip("Courbe d'easing (0→1 sur la durée).")]
    [SerializeField] private AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Coroutine _coroutine;
    private bool _isOpen;

    private void Awake() {
        if (hinge == null) hinge = transform;
        hinge.localRotation = Quaternion.Euler(closedEuler);
    }

    public void SetOpen(bool open, bool instant = false) {
        if (_isOpen == open && !instant) return;
        _isOpen = open;

        if (_coroutine != null) {
            StopCoroutine(_coroutine);
            _coroutine = null;
        }

        Quaternion target = Quaternion.Euler(open ? openEuler : closedEuler);
        // GameObject désactivé (prop hors champ) ou durée nulle : snap direct, pas de coroutine.
        if (instant || !isActiveAndEnabled || duration <= 0f) {
            hinge.localRotation = target;
            return;
        }
        _coroutine = StartCoroutine(AnimateTo(target));
    }

    private IEnumerator AnimateTo(Quaternion target) {
        Quaternion start = hinge.localRotation;
        float t = 0f;
        while (t < duration) {
            t += Time.deltaTime;
            float k = curve.Evaluate(Mathf.Clamp01(t / duration));
            hinge.localRotation = Quaternion.SlerpUnclamped(start, target, k);
            yield return null;
        }
        hinge.localRotation = target;
        _coroutine = null;
    }
}
