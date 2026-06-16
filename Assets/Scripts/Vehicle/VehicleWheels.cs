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

    [Header("Suspension (visuelle, par roue)")]
    [Tooltip("Active le débattement vertical visuel : chaque roue suit le sol (raycast) dans son puits.")]
    [SerializeField] private bool enableSuspension = true;
    [Tooltip("Layers du sol sondés par roue. Si vide, le layer « Ground » est utilisé.")]
    [SerializeField] private LayerMask groundMask;
    [Tooltip("Débattement max en COMPRESSION (roue qui remonte dans le puits, m).")]
    [SerializeField] private float suspensionTravel = 0.15f;
    [Tooltip("Débattement max en DÉTENTE (roue qui descend / pend, m).")]
    [SerializeField] private float suspensionDroop = 0.12f;
    [Tooltip("Lissage du débattement (amortisseur). Plus grand = plus réactif.")]
    [SerializeField] private float suspensionLerp = 12f;
    [Tooltip("Hauteur de départ du raycast roue au-dessus de la position de repos (m).")]
    [SerializeField] private float castUp = 0.4f;

    private Vector3 _lastPos;
    private float   _lastYaw;
    private float[] _roll;
    private float   _steer;
    private Vector3[] _restLocalPos;    // position locale de repos de chaque roue (puits)
    private float[]   _suspOffset;      // débattement lissé courant par roue (le long de l'axe vertical local)
    private float   _inputSteer;          // input de braquage [-1..1] fourni par le conducteur local
    private float   _inputSteerTime = -10f;

    /// <summary>Le conducteur local pousse son input de braquage chaque frame → angle visuel précis
    /// et instantané. Les clients distants (qui n'appellent pas ceci) retombent sur le lacet mesuré.</summary>
    public void SetSteerInput(float normalized) {
        _inputSteer = Mathf.Clamp(normalized, -1f, 1f);
        _inputSteerTime = Time.time;
    }

    private void Awake() {
        _lastPos = transform.position;
        _lastYaw = transform.eulerAngles.y;
        int n = allWheels != null ? allWheels.Length : 0;
        _roll = new float[n];
        // Mémorise la position de repos (puits) AVANT toute modification du débattement.
        _restLocalPos = new Vector3[n];
        _suspOffset   = new float[n];
        for (int i = 0; i < n; i++)
            _restLocalPos[i] = allWheels[i] != null ? allWheels[i].localPosition : Vector3.zero;

        if (groundMask.value == 0) {
            int g = LayerMask.NameToLayer("Ground");
            if (g >= 0) groundMask = 1 << g;
        }
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

        // Braquage visuel.
        float targetSteer;
        if (Time.time - _inputSteerTime < 0.2f) {
            // Conducteur local : piloté DIRECTEMENT par l'input → précis, sans latence ni dépendance
            // à la mesure du lacet. Tourne même à l'arrêt (comme de vraies roues).
            targetSteer = _inputSteer * maxSteerAngle;
        } else {
            // Clients distants : déduit du taux de lacet mesuré (sans réseau). Nul à l'arrêt.
            targetSteer = 0f;
            if (Mathf.Abs(speed) > 0.2f) {
                float steerRad = Mathf.Atan2(wheelBase * yawRate * Mathf.Deg2Rad, Mathf.Abs(speed));
                targetSteer = Mathf.Clamp(steerRad * Mathf.Rad2Deg * Mathf.Sign(speed), -maxSteerAngle, maxSteerAngle);
            }
        }
        _steer = Mathf.Lerp(_steer, targetSteer, steerLerp * dt);

        // Application : steer (Y) + roll (X) sur la rotation ; débattement vertical sur la position.
        Vector3 up = transform.up;
        for (int i = 0; i < allWheels.Length; i++) {
            Transform w = allWheels[i];
            if (w == null) continue;
            bool steer = IsSteering(w);
            Quaternion yawQ = steer ? Quaternion.AngleAxis(_steer, Vector3.up) : Quaternion.identity;
            w.localRotation = yawQ * Quaternion.AngleAxis(_roll[i], Vector3.right);

            if (enableSuspension) {
                float target = ComputeSuspensionOffset(i, up);
                _suspOffset[i] = Mathf.Lerp(_suspOffset[i], target, suspensionLerp * dt);
                w.localPosition = _restLocalPos[i] + Vector3.up * _suspOffset[i];
            }
        }
    }

    /// <summary>
    /// Débattement vertical cible (le long de l'axe vertical local) pour la roue i : raycast sous sa
    /// position de repos, place le CENTRE de roue à <c>contact + rayon</c>. Borné à
    /// [-détente, +compression]. Sans sol détecté, la roue pend (détente max).
    /// </summary>
    private float ComputeSuspensionOffset(int i, Vector3 up) {
        Vector3 restWorld = transform.TransformPoint(_restLocalPos[i]);
        Vector3 origin = restWorld + up * castUp;
        float maxDist = castUp + wheelRadius + suspensionDroop + 0.05f;

        if (Physics.Raycast(origin, -up, out RaycastHit hit, maxDist, groundMask, QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(transform)) {
            Vector3 desiredCenter = hit.point + up * wheelRadius;
            float offset = Vector3.Dot(desiredCenter - restWorld, up);
            return Mathf.Clamp(offset, -suspensionDroop, suspensionTravel);
        }
        return -suspensionDroop; // roue en l'air → détente max
    }

    private bool IsSteering(Transform w) {
        if (steeringWheels == null) return false;
        for (int i = 0; i < steeringWheels.Length; i++)
            if (steeringWheels[i] == w) return true;
        return false;
    }
}
