using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

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
        public float updateInterval = 0.1f;
        public float minDistanceToDisplay = 1.0f;
        public float moveThreshold = 0.1f; 
        public float maxPathLength = 3.0f; // Limit path length to 3 meters
        public bool hideIfNoPath = true;

        [Header("Debug")]
        public bool showDebugLogs = true;

        private RibbonPathRenderer _renderer;
        private NavMeshPath _path;
        private float _lastUpdateTime;
        private Vector3 _lastPlayerPos;
        private Vector3 _lastTargetPos;
        private readonly List<Vector3> _trimmedCorners = new List<Vector3>();

        private void OnEnable()
        {
            _renderer = GetComponent<RibbonPathRenderer>();
            _path = new NavMeshPath();
        }

        private void Update()
        {
            if (target == null || player == null) return;

            bool shouldUpdate = false;
            if (Application.isPlaying)
            {
                if (Time.time - _lastUpdateTime > updateInterval)
                {
                    float dPlayer = (player.position - _lastPlayerPos).sqrMagnitude;
                    float dTarget = (target.position - _lastTargetPos).sqrMagnitude;
                    
                    if (dPlayer > moveThreshold * moveThreshold || dTarget > moveThreshold * moveThreshold)
                    {
                        shouldUpdate = true;
                    }
                }
            }
            else
            {
                shouldUpdate = true;
            }

            if (shouldUpdate)
            {
                UpdatePath();
                _lastUpdateTime = Time.time;
                _lastPlayerPos = player.position;
                _lastTargetPos = target.position;
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

            float distToTarget = Vector3.Distance(player.position, target.position);
            if (distToTarget < minDistanceToDisplay)
            {
                _renderer.SetPath(null);
                return;
            }

            if (NavMesh.CalculatePath(player.position, target.position, NavMesh.AllAreas, _path))
            {
                if (_path.status == NavMeshPathStatus.PathComplete || _path.status == NavMeshPathStatus.PathPartial)
                {
                    TrimPath(_path.corners, maxPathLength);
                    _renderer.SetPath(_trimmedCorners.ToArray());
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
                    Vector3 direction = (target.position - player.position).normalized;
                    Vector3 directEnd = player.position + direction * Mathf.Min(distToTarget, maxPathLength);
                    _renderer.SetPath(new Vector3[] { player.position, directEnd });
                }
                else
                {
                    _renderer.SetPath(null);
                }
            }
        }

        private void TrimPath(Vector3[] corners, float maxLength)
        {
            _trimmedCorners.Clear();
            if (corners == null || corners.Length == 0) return;

            float currentLength = 0;
            _trimmedCorners.Add(corners[0]);

            for (int i = 0; i < corners.Length - 1; i++)
            {
                float segDist = Vector3.Distance(corners[i], corners[i + 1]);
                if (currentLength + segDist > maxLength)
                {
                    float remaining = maxLength - currentLength;
                    float t = remaining / Mathf.Max(0.001f, segDist);
                    _trimmedCorners.Add(Vector3.Lerp(corners[i], corners[i + 1], t));
                    return;
                }

                currentLength += segDist;
                _trimmedCorners.Add(corners[i + 1]);
            }
        }
    }
}

