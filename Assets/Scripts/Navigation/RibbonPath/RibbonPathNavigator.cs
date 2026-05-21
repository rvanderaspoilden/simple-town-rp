using UnityEngine;
using UnityEngine.AI;

namespace TheBroz.Navigation
{
    [ExecuteAlways]
    [RequireComponent(typeof(RibbonPathRenderer))]
    public class RibbonPathNavigator : MonoBehaviour
    {
        [Header("References")]
        public Transform target;
        public Transform player;

        [Header("Settings")]
        public float updateInterval = 0.2f;
        public float minDistanceToDisplay = 1.0f;
        public bool hideIfNoPath = true;

        [Header("Debug")]
        public bool showDebugLogs = true;

        private RibbonPathRenderer _renderer;
        private NavMeshPath _path;
        private float _lastUpdateTime;

        private void OnEnable()
        {
            _renderer = GetComponent<RibbonPathRenderer>();
            _path = new NavMeshPath();
        }

        private void Update()
        {
            if (target == null || player == null) return;

            if (Time.time - _lastUpdateTime > updateInterval || !Application.isPlaying)
            {
                UpdatePath();
                _lastUpdateTime = Time.time;
            }
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            UpdatePath();
        }

        private void UpdatePath()
        {
            if (target == null || player == null) return;

            float dist = Vector3.Distance(player.position, target.position);
            if (dist < minDistanceToDisplay)
            {
                _renderer.SetPath(null);
                return;
            }

            if (NavMesh.CalculatePath(player.position, target.position, NavMesh.AllAreas, _path))
            {
                if (_path.status == NavMeshPathStatus.PathComplete || _path.status == NavMeshPathStatus.PathPartial)
                {
                    _renderer.SetPath(_path.corners);
                }
                else
                {
                    if (showDebugLogs) Debug.LogWarning($"[RibbonPath] Path status: {_path.status} on {gameObject.name}");
                    _renderer.SetPath(null);
                }
            }
            else
            {
                if (showDebugLogs && Application.isPlaying) 
                {
                    Debug.LogWarning($"[RibbonPath] Failed to calculate path between {player.name} and {target.name}. Is NavMesh baked?");
                }
                
                if (!hideIfNoPath)
                {
                    // Fallback to direct line if user wants it
                    _renderer.SetPath(new Vector3[] { player.position, target.position });
                }
                else
                {
                    _renderer.SetPath(null);
                }
            }
        }
    }
}
