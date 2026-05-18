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

    [Header("Graphics")]
    [Tooltip("Dropdown filled at runtime with QualitySettings.names.")]
    [SerializeField] private TMP_Dropdown graphicsQualityDropdown;

    [Header("Save")]
    [SerializeField] private Button saveButton;
    [SerializeField] private TMP_Text saveFeedbackText;

    private UserSettingsData _working;

    private void OnEnable() {
        ApiManager.OnUserSettingsLoaded += OnSettingsLoaded;
        PopulateGraphicsDropdown();
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
        if (saveFeedbackText != null) saveFeedbackText.text = string.Empty;
    }

    private void PopulateGraphicsDropdown() {
        if (graphicsQualityDropdown == null) return;
        graphicsQualityDropdown.ClearOptions();
        var names = QualitySettings.names;
        var options = new System.Collections.Generic.List<string>(names);
        graphicsQualityDropdown.AddOptions(options);
    }

    private void WireButtons() {
        if (notificationsNewMissionToggle != null)
            notificationsNewMissionToggle.onValueChanged.AddListener(OnToggleNotif);
        if (audioMasterSlider != null) audioMasterSlider.onValueChanged.AddListener(v => _working.AudioMaster = v);
        if (audioMusicSlider != null) audioMusicSlider.onValueChanged.AddListener(v => _working.AudioMusic = v);
        if (audioSfxSlider != null) audioSfxSlider.onValueChanged.AddListener(v => _working.AudioSfx = v);
        if (graphicsQualityDropdown != null)
            graphicsQualityDropdown.onValueChanged.AddListener(v => _working.GraphicsQuality = v);
        if (saveButton != null) saveButton.onClick.AddListener(OnSaveClicked);
    }

    private void UnwireButtons() {
        if (notificationsNewMissionToggle != null) notificationsNewMissionToggle.onValueChanged.RemoveAllListeners();
        if (audioMasterSlider != null) audioMasterSlider.onValueChanged.RemoveAllListeners();
        if (audioMusicSlider != null) audioMusicSlider.onValueChanged.RemoveAllListeners();
        if (audioSfxSlider != null) audioSfxSlider.onValueChanged.RemoveAllListeners();
        if (graphicsQualityDropdown != null) graphicsQualityDropdown.onValueChanged.RemoveAllListeners();
        if (saveButton != null) saveButton.onClick.RemoveAllListeners();
    }

    private void OnToggleNotif(bool value) {
        _working.NotificationsNewMission = value;
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
        AudioListener.volume = Mathf.Clamp01(data.AudioMaster);
        int qualityIdx = Mathf.Clamp(data.GraphicsQuality, 0, QualitySettings.names.Length - 1);
        if (QualitySettings.GetQualityLevel() != qualityIdx) {
            QualitySettings.SetQualityLevel(qualityIdx, applyExpensiveChanges: true);
        }
    }

    private static UserSettingsData CloneData(UserSettingsData src) {
        if (src == null) return new UserSettingsData();
        return new UserSettingsData {
            NotificationsNewMission = src.NotificationsNewMission,
            AudioMaster = src.AudioMaster,
            AudioMusic = src.AudioMusic,
            AudioSfx = src.AudioSfx,
            GraphicsQuality = src.GraphicsQuality,
        };
    }
}
