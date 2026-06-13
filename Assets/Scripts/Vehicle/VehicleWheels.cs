using UnityEngine;

/// <summary>
/// Animation visuelle des roues d'un véhicule, entièrement DÉRIVÉE du mouvement du transform —
/// donc identique sur tous les clients sans aucune synchro réseau (le NetworkTransform déplace
/// déjà le véhicule partout).
///
///   - Roulement : chaque roue tourne autour de son axe (X local) proportionnellement à la
///     distance parcourue vers l'avant (distance / circonférence).
///   - Braquage : les roues avant pivotent (Y local) d'un angle déduit du taux de lacet et de la
///     vitesse (modèle d'Ackermann inversé : angle = atan(empattement · ωlacet / vitesse)).
///
/// Structure attendue par roue : un transform "pivot" (référencé ici) dont l'axe X local est
/// l'essieu ; le mesh est un enfant. Les roues avant doivent figurer AUSSI dans
/// <see cref="steeringWheels"/>.
/// </summary>
public class VehicleWheels : MonoBehaviour {
    [Header("Wheels")]
    [Tooltip("Toutes les roues (avant + arrière) — reçoivent le roulement.")]
    [SerializeField] private Transform[] allWheels;
    [Tooltip("Roues directrices (avant) — reçoivent en plus le braquage. Sous-ensemble de allWheels.")]
    [SerializeField] private Transform[] steeringWheels;

    [Header("Tuning")]
    [Tooltip("Rayon de roue (m), pour convertir distance → angle de roulement.")]
    [SerializeField] private float wheelRadius = 0.35f;
    [Tooltip("Empattement (m) : distance essieu avant ↔ arrière. Sert au calcul du braquage.")]
    [SerializeField] private float wheelBase = 2.6f;
    [Tooltip("Angle de braquage visuel maximal (deg).")]
    [SerializeField] private float maxSteerAngle = 30f;
    [Tooltip("Lissage du braquage visuel (plus grand = plus réactif).")]
    [SerializeField] private float steerLerp = 10f;

    private Vector3 _lastPos;
    private float   _lastYaw;
    private float[] _roll;
    private float   _steer;

    private void Awake() {
        _lastPos = transform.position;
        _lastYaw = transform.eulerAngles.y;
        _roll = new float[allWheels != null ? allWheels.Length : 0];
    }

    private void OnEnable() {
        // Reset des repères après une (ré)activation pour éviter un saut.
        _lastPos = transform.position;
        _lastYaw = transform.eulerAngles.y;
    }

    private void LateUpdate() {
        float dt = Time.deltaTime;
        if (dt <= 0f || allWheels == null) return;

        // Distance avant signée parcourue cette frame.
        Vector3 delta = transform.position - _lastPos;
        _lastPos = transform.position;
        float fwd = Vector3.Dot(delta, transform.forward);
        float speed = fwd / dt;

        // Taux de lacet (deg/s).
        float yaw = transform.eulerAngles.y;
        float yawRate = Mathf.DeltaAngle(_lastYaw, yaw) / dt;
        _lastYaw = yaw;

        // Roulement.
        float rollDelta = wheelRadius > 0f ? (fwd / (2f * Mathf.PI * wheelRadius)) * 360f : 0f;
        for (int i = 0; i < allWheels.Length; i++) {
            if (allWheels[i] == null) continue;
            _roll[i] += rollDelta;
        }

        // Braquage visuel : angle = atan(empattement · ωlacet / vitesse). Nul à l'arrêt.
        float targetSteer = 0f;
        if (Mathf.Abs(speed) > 0.2f) {
            float steerRad = Mathf.Atan2(wheelBase * yawRate * Mathf.Deg2Rad, Mathf.Abs(speed));
            targetSteer = Mathf.Clamp(steerRad * Mathf.Rad2Deg * Mathf.Sign(speed), -maxSteerAngle, maxSteerAngle);
        }
        _steer = Mathf.Lerp(_steer, targetSteer, steerLerp * dt);

        // Application : steer (Y) en externe, roll (X) en interne.
        for (int i = 0; i < allWheels.Length; i++) {
            Transform w = allWheels[i];
            if (w == null) continue;
            bool steer = IsSteering(w);
            Quaternion yawQ = steer ? Quaternion.AngleAxis(_steer, Vector3.up) : Quaternion.identity;
            w.localRotation = yawQ * Quaternion.AngleAxis(_roll[i], Vector3.right);
        }
    }

    private bool IsSteering(Transform w) {
        if (steeringWheels == null) return false;
        for (int i = 0; i < steeringWheels.Length; i++)
            if (steeringWheels[i] == w) return true;
        return false;
    }
}
