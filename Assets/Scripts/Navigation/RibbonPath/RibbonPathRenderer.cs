using UnityEngine;
using System.Collections.Generic;

namespace TheBroz.Navigation
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class RibbonPathRenderer : MonoBehaviour
    {
        [Header("Appearance")]
        public float width = 0.5f;
        public float heightOffset = 0.1f;
        public int smoothingSubdivisions = 8;

        [Header("Visual Options")]
        [Tooltip("Number of texture repeats per world unit. Increase for more/smaller arrows.")]
        public float arrowsPerMeter = 2.0f;

        private Mesh _mesh;
        private MeshFilter _meshFilter;
        private readonly List<Vector3> _pathPoints = new List<Vector3>(256);

        private readonly List<Vector3> _vertices = new List<Vector3>(512);
        private readonly List<int> _triangles = new List<int>(1536);
        private readonly List<Vector2> _uvs = new List<Vector2>(512);
        private readonly List<Color> _colors = new List<Color>(512);

        private void OnEnable()
        {
            _meshFilter = GetComponent<MeshFilter>();
            InitMesh();
        }

        private void InitMesh()
        {
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "RibbonPathMesh" };
                _mesh.MarkDynamic();
            }
            if (_meshFilter != null)
            {
                _meshFilter.sharedMesh = _mesh;
            }
        }

        public void SetPath(Vector3[] rawPoints)
        {
            if (rawPoints == null || rawPoints.Length < 2)
            {
                if (_mesh != null) _mesh.Clear();
                _pathPoints.Clear();
                return;
            }

            InitMesh();
            _pathPoints.Clear();
            
            // Dynamic smoothing: less subdivisions for longer paths
            int actualSubdivisions = smoothingSubdivisions;
            if (rawPoints.Length > 20) actualSubdivisions = Mathf.Max(1, smoothingSubdivisions / 2);
            if (rawPoints.Length > 50) actualSubdivisions = 1;

            SmoothPath(rawPoints, _pathPoints, actualSubdivisions);
            GenerateMesh();
        }

        private void SmoothPath(Vector3[] input, List<Vector3> output, int subdivisions)
        {
            if (input.Length < 2) return;
            if (subdivisions <= 1)
            {
                output.AddRange(input);
                return;
            }

            // Catmull-Rom smoothing
            for (int i = 0; i < input.Length - 1; i++)
            {
                Vector3 p0 = i == 0 ? input[0] : input[i - 1];
                Vector3 p1 = input[i];
                Vector3 p2 = input[i + 1];
                Vector3 p3 = i + 2 >= input.Length ? input[input.Length - 1] : input[i + 2];

                for (int j = 0; j < subdivisions; j++)
                {
                    float t = j / (float)subdivisions;
                    output.Add(GetCatmullRomPoint(p0, p1, p2, p3, t));
                }
            }
            output.Add(input[input.Length - 1]);
        }

        private Vector3 GetCatmullRomPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        private void GenerateMesh()
        {
            _vertices.Clear();
            _triangles.Clear();
            _uvs.Clear();
            _colors.Clear();

            int pointCount = _pathPoints.Count;
            if (pointCount < 2) return;

            float totalDist = 0;
            Vector3 up = Vector3.up;
            float halfWidth = width * 0.5f;

            for (int i = 0; i < pointCount; i++)
            {
                Vector3 current = _pathPoints[i];
                Vector3 forward;

                if (i < pointCount - 1)
                {
                    forward = (_pathPoints[i + 1] - current).normalized;
                    if (forward.sqrMagnitude < 0.001f && i > 0) forward = (current - _pathPoints[i - 1]).normalized;
                }
                else
                {
                    forward = (current - _pathPoints[i - 1]).normalized;
                }
                
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;

                Vector3 right = Vector3.Cross(up, forward).normalized;
                if (right.sqrMagnitude < 0.001f) right = Vector3.right;
                
                Vector3 scaledRight = right * halfWidth;
                Vector3 heightVec = up * heightOffset;
                
                Vector3 leftPos = current - scaledRight + heightVec;
                Vector3 rightPos = current + scaledRight + heightVec;

                _vertices.Add(transform.InverseTransformPoint(leftPos));
                _vertices.Add(transform.InverseTransformPoint(rightPos));

                if (i > 0)
                {
                    totalDist += Vector3.Distance(_pathPoints[i - 1], current);
                }

                float v = totalDist * arrowsPerMeter;
                _uvs.Add(new Vector2(0, v));
                _uvs.Add(new Vector2(1, v));

                float alpha = 1.0f;
                // Fade at start and end only if path is long enough
                if (pointCount > 10)
                {
                    if (i < 5) alpha = i / 5.0f;
                    else if (i > pointCount - 6) alpha = Mathf.Clamp01((pointCount - 1 - i) / 5.0f);
                }
                else if (pointCount > 2)
                {
                    if (i == 0 || i == pointCount - 1) alpha = 0f;
                    else if (i == 1 || i == pointCount - 2) alpha = 0.5f;
                }
                
                Color col = new Color(1, 1, 1, alpha);
                _colors.Add(col);
                _colors.Add(col);

                if (i < pointCount - 1)
                {
                    int baseIdx = i * 2;
                    _triangles.Add(baseIdx);
                    _triangles.Add(baseIdx + 2);
                    _triangles.Add(baseIdx + 1);

                    _triangles.Add(baseIdx + 1);
                    _triangles.Add(baseIdx + 2);
                    _triangles.Add(baseIdx + 3);
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_colors);
            // No need for normals for unlit stylized ribbon
            _mesh.RecalculateBounds();
        }

        private void OnDrawGizmos()
        {
            if (_pathPoints == null || _pathPoints.Count < 2) return;

            Gizmos.color = Color.cyan;
            for (int i = 0; i < _pathPoints.Count - 1; i++)
            {
                Gizmos.DrawLine(_pathPoints[i], _pathPoints[i + 1]);
            }
        }
    }
}


