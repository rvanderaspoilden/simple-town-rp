using System.Collections.Generic;
using Sim;
using Sim.Entities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phone app: user preferences. Reads the current state from
/// <see cref="ApiManager.UserSettings"/> on enable, lets the player toggle
/// notification opt-ins, audio sliders and graphics quality, then PUTs the
/// whole blob on Save. Apply runs the audio/graphics changes locally so the
/// player feels them immediately.
/// </summary>
public class SettingsUI : PhoneApplicationUI {
    [Header("Notifications")]
    [SerializeField] private Toggle notificationsNewMissionToggle;

    [Header("Audio")]
    [SerializeField] private Slider audioMasterSlider;
    [SerializeField] private Slider audioMusicSlider;
    [SerializeField] private Slider audioSfxSlider;
    [Tooltip("Voice capture device. Filled at runtime with Microphone.devices; index 0 is the system default.")]
    [SerializeField] private TMP_Dropdown audioInputDropdown;

    [Header("Graphics")]
    [Tooltip("Dropdown filled at runtime with QualitySettings.names.")]
    [SerializeField] private TMP_Dropdown graphicsQualityDropdown;

    [Header("Save")]
    [SerializeField] private Button saveButton;
    [SerializeField] private TMP_Text saveFeedbackText;

    private UserSettingsData _working;

    // Parallel to audioInputDropdown options. Index 0 is the system default
    // (empty string); the rest mirror Microphone.devices by name.
    private readonly List<string> _inputDeviceValues = new List<string>();

    private const string DefaultInputLabel = "Défaut (système)";

    private void OnEnable() {
        ApiManager.OnUserSettingsLoaded += OnSettingsLoaded;
        PopulateGraphicsDropdown();
        PopulateInputDropdown();
        WireButtons();
        HydrateFromCache();
        EnsureLoaded();
    }

    private void EnsureLoaded() {
        if (ApiManager.Instance == null || ApiManager.Instance.UserSettings != null) return;
        string userId = ApiManager.Instance.ResolveLocalUserId();
        if (!string.IsNullOrEmpty(userId)) ApiManager.Instance.LoadUserSettings(userId);
    }

    private void OnDisable() {
        ApiManager.OnUserSettingsLoaded -= OnSettingsLoaded;
        UnwireButtons();
    }

    public override void Back() {
        PhoneControllerUI.Instance?.BackToHome();
    }

    private void OnSettingsLoaded(UserSettings settings) {
        HydrateFromCache();
    }

    private void HydrateFromCache() {
        var settings = ApiManager.Instance != null ? ApiManager.Instance.UserSettings : null;
        _working = settings != null
            ? CloneData(settings.Data)
            : new UserSettingsData();

        if (notificationsNewMissionToggle != null) notificationsNewMissionToggle.isOn = _working.NotificationsNewMission;
        if (audioMasterSlider != null) audioMasterSlider.value = _working.AudioMaster;
        if (audioMusicSlider != null) audioMusicSlider.value = _working.AudioMusic;
        if (audioSfxSlider != null) audioSfxSlider.value = _working.AudioSfx;
        if (graphicsQualityDropdown != null) {
            graphicsQualityDropdown.value = Mathf.Clamp(_working.GraphicsQuality, 0, QualitySettings.names.Length - 1);
        }
        if (audioInputDropdown != null) {
            // Fall back to "system default" (index 0) when the saved device is
            // no longer present (e.g. mic unplugged since last session).
            int idx = _inputDeviceValues.IndexOf(_working.MicrophoneDevice);
            audioInputDropdown.SetValueWithoutNotify(idx >= 0 ? idx : 0);
            audioInputDropdown.RefreshShownValue();
        }
        if (saveFeedbackText != null) saveFeedbackText.text = string.Empty;
    }

    private void PopulateGraphicsDropdown() {
        if (graphicsQualityDropdown == null) return;
        graphicsQualityDropdown.ClearOptions();
        var names = QualitySettings.names;
        var options = new List<string>(names);
        graphicsQualityDropdown.AddOptions(options);
    }

    private void PopulateInputDropdown() {
        if (audioInputDropdown == null) return;
        audioInputDropdown.ClearOptions();
        _inputDeviceValues.Clear();

        // Index 0: system default (stored as an empty device name).
        var labels = new List<string> { DefaultInputLabel };
        _inputDeviceValues.Add(string.Empty);

        foreach (var device in Microphone.devices) {
            labels.Add(device);
            _inputDeviceValues.Add(device);
        }

        audioInputDropdown.AddOptions(labels);
    }

    private void WireButtons() {
        if (notificationsNewMissionToggle != null)
            notificationsNewMissionToggle.onValueChanged.AddListener(OnToggleNotif);
        // Application LIVE au glissement (preview immédiat) ; la persistance se fait au Save.
        if (audioMasterSlider != null) audioMasterSlider.onValueChanged.AddListener(v => { _working.AudioMaster = v; ApplyAudioLive(); });
        if (audioMusicSlider != null)  audioMusicSlider.onValueChanged.AddListener(v => { _working.AudioMusic = v; ApplyAudioLive(); });
        if (audioSfxSlider != null)    audioSfxSlider.onValueChanged.AddListener(v => { _working.AudioSfx = v; ApplyAudioLive(); });
        if (graphicsQualityDropdown != null)
            graphicsQualityDropdown.onValueChanged.AddListener(v => _working.GraphicsQuality = v);
        if (audioInputDropdown != null)
            audioInputDropdown.onValueChanged.AddListener(OnInputDeviceChanged);
        if (saveButton != null) saveButton.onClick.AddListener(OnSaveClicked);
    }

    private void UnwireButtons() {
        if (notificationsNewMissionToggle != null) notificationsNewMissionToggle.onValueChanged.RemoveAllListeners();
        if (audioMasterSlider != null) audioMasterSlider.onValueChanged.RemoveAllListeners();
        if (audioMusicSlider != null) audioMusicSlider.onValueChanged.RemoveAllListeners();
        if (audioSfxSlider != null) audioSfxSlider.onValueChanged.RemoveAllListeners();
        if (graphicsQualityDropdown != null) graphicsQualityDropdown.onValueChanged.RemoveAllListeners();
        if (audioInputDropdown != null) audioInputDropdown.onValueChanged.RemoveAllListeners();
        if (saveButton != null) saveButton.onClick.RemoveAllListeners();
    }

    private void ApplyAudioLive() {
        if (_working != null) Sim.Audio.AudioVolume.ApplyFrom(_working);
    }

    private void OnToggleNotif(bool value) {
        _working.NotificationsNewMission = value;
    }

    private void OnInputDeviceChanged(int index) {
        _working.MicrophoneDevice = index >= 0 && index < _inputDeviceValues.Count
            ? _inputDeviceValues[index]
            : string.Empty;
    }

    private void OnSaveClicked() {
        if (_working == null || ApiManager.Instance == null) return;
        // Apply locally first so the player feels the change even before the
        // backend round-trip completes.
        ApplyLocal(_working);
        if (saveFeedbackText != null) saveFeedbackText.text = "Enregistrement…";
        ApiManager.Instance.SaveUserSettings(_working, ok => {
            if (saveFeedbackText != null) {
                saveFeedbackText.text = ok ? "Enregistré." : "Échec de l'enregistrement.";
            }
            if (ok) SettingsSyncBridge.NotifyServer(_working);
        });
    }

    private static void ApplyLocal(UserSettingsData data) {
        Sim.Audio.AudioVolume.ApplyFrom(data); // Master (AudioListener) + Musique/SFX (mixer)
        int qualityIdx = Mathf.Clamp(data.GraphicsQuality, 0, QualitySettings.names.Length - 1);
        if (QualitySettings.GetQualityLevel() != qualityIdx) {
            QualitySettings.SetQualityLevel(qualityIdx, applyExpensiveChanges: true);
        }
        AudioDeviceSettings.ApplyMicrophone(data.MicrophoneDevice);
    }

    private static UserSettingsData CloneData(UserSettingsData src) {
        if (src == null) return new UserSettingsData();
        return new UserSettingsData {
            NotificationsNewMission = src.NotificationsNewMission,
            AudioMaster = src.AudioMaster,
            AudioMusic = src.AudioMusic,
            AudioSfx = src.AudioSfx,
            GraphicsQuality = src.GraphicsQuality,
            MicrophoneDevice = src.MicrophoneDevice,
        };
    }
}
