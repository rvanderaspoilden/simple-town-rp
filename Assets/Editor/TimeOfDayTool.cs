using UnityEditor;
using UnityEngine;
using PolyverseSkiesAsset;
using System;

public class TimeOfDayTool : EditorWindow
{
    private float _hours = 12f;
    private PolyverseSkies _polyverseSkies;
    private GameObject _meteoObject;

    [MenuItem("Tools/Time of Day Tool")]
    public static void ShowWindow()
    {
        GetWindow<TimeOfDayTool>("Time of Day");
    }

    private void OnEnable()
    {
        FindAssets();
    }

    private void FindAssets()
    {
        _polyverseSkies = FindFirstObjectByType<PolyverseSkies>();
        _meteoObject = GameObject.Find("Meteo");
    }

    private void OnGUI()
    {
        GUILayout.Label("Time of Day Controller", EditorStyles.boldLabel);

        if (_polyverseSkies == null || _meteoObject == null)
        {
            EditorGUILayout.HelpBox("Could not find PolyverseSkies or Meteo object in scene.", MessageType.Warning);
            if (GUILayout.Button("Refresh"))
            {
                FindAssets();
            }
            return;
        }

        EditorGUI.BeginChangeCheck();

        _hours = EditorGUILayout.Slider("Hour", _hours, 0f, 23.99f);

        int h = (int)_hours;
        int m = (int)((_hours - h) * 60);
        EditorGUILayout.LabelField($"Time: {h:00}:{m:00}");

        if (EditorGUI.EndChangeCheck())
        {
            UpdateTime();
        }

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Morning (08:00)"))
        {
            _hours = 8f;
            UpdateTime();
        }
        if (GUILayout.Button("Noon (12:00)"))
        {
            _hours = 12f;
            UpdateTime();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Evening (18:00)"))
        {
            _hours = 18f;
            UpdateTime();
        }
        if (GUILayout.Button("Midnight (00:00)"))
        {
            _hours = 0f;
            UpdateTime();
        }
        EditorGUILayout.EndHorizontal();

        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Note: TimeManager will override this in Play Mode unless paused.", MessageType.Info);
        }
    }

    private void UpdateTime()
    {
        if (_polyverseSkies == null || _meteoObject == null) return;

        Undo.RecordObjects(new UnityEngine.Object[] { _polyverseSkies, _meteoObject.transform }, "Change Time of Day");

        int hoursInt = (int)_hours;
        float polyverseValue = 0;

        // Replicating MeteoManager logic
        float polyverseHourConstant = 1f / 12f;
        if (hoursInt < 12)
        {
            polyverseValue = 1f - (hoursInt * polyverseHourConstant);
        }
        else if (hoursInt >= 12)
        {
            polyverseValue = (hoursInt - 12) * polyverseHourConstant;
        }

        _polyverseSkies.timeOfDay = polyverseValue;

        // Rotation logic
        float rotationHourConstant = 360f / 24f;
        _meteoObject.transform.rotation = Quaternion.Euler((_hours * rotationHourConstant) + 180f, 0, 0);

        // Force update of environment lighting if enabled
        if (_polyverseSkies.updateLighting)
        {
            DynamicGI.UpdateEnvironment();
        }

        EditorUtility.SetDirty(_polyverseSkies);
        EditorUtility.SetDirty(_meteoObject.transform);
    }
}
