using System;
using System.Globalization;
using Mirror;
using Sim;
using Sim.Entities;
using Sim.Professions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phone app: career dashboard. Two modes —
///   noJobView   → "Pas de métier" + Apply buttons (one per ProfessionConfig).
///   careerView  → current job name + xp + start date + Resign button.
/// Driven by PlayerController.Local + OnCharacterDataChanged. Sends
/// MissionChangeCareerMessage to the server on Apply / Resign.
/// </summary>
public class CareerUI : PhoneApplicationUI {
    [Serializable]
    private struct CareerChoice {
        public ProfessionConfig profession;
        public Button applyButton;
    }

    [Header("Mode roots")]
    [SerializeField] private GameObject noJobView;
    [SerializeField] private GameObject careerView;

    [Header("No-job view")]
    [Tooltip("One entry per job the player can apply to. Drag the Button refs here.")]
    [SerializeField] private CareerChoice[] choices;

    [Tooltip("Optional label that displays the city's unemployment income (e.g. 'Allocation : 50 € toutes les 10 min').")]
    [SerializeField] private TextMeshProUGUI unemploymentIncomeLabel;

    [Header("Career view")]
    [SerializeField] private TextMeshProUGUI jobNameLabel;
    [SerializeField] private TextMeshProUGUI startedAtLabel;
    [SerializeField] private TextMeshProUGUI salaryLabel;
    [SerializeField] private Button resignButton;

    private PlayerController _player;

    private void OnEnable() {
        _player = PlayerController.Local;
        PlayerController.OnCharacterDataChanged += OnCharacterDataChanged;
        WireButtons();
        Refresh();
    }

    private void OnDisable() {
        PlayerController.OnCharacterDataChanged -= OnCharacterDataChanged;
        UnwireButtons();
    }

    public override void Back() {
        PhoneControllerUI.Instance?.BackToHome();
    }

    private void OnCharacterDataChanged(CharacterData data) {
        Refresh();
    }

    private void WireButtons() {
        if (choices != null) {
            foreach (var choice in choices) {
                if (choice.applyButton == null) continue;
                var captured = choice.profession != null ? choice.profession.id : "";
                choice.applyButton.onClick.RemoveAllListeners();
                choice.applyButton.onClick.AddListener(() => SendChange(captured));
            }
        }
        if (resignButton != null) {
            resignButton.onClick.RemoveAllListeners();
            resignButton.onClick.AddListener(() => SendChange(""));
        }
    }

    private void UnwireButtons() {
        if (choices != null) {
            foreach (var choice in choices) {
                if (choice.applyButton != null) choice.applyButton.onClick.RemoveAllListeners();
            }
        }
        if (resignButton != null) resignButton.onClick.RemoveAllListeners();
    }

    private void Refresh() {
        if (_player == null) _player = PlayerController.Local;
        var character = _player != null ? _player.CharacterData : null;
        var current = character?.CurrentProfession;

        bool hasJob = current != null;
        if (noJobView != null) noJobView.SetActive(!hasJob);
        if (careerView != null) careerView.SetActive(hasJob);

        if (!hasJob) {
            if (unemploymentIncomeLabel != null)
                unemploymentIncomeLabel.text = FormatUnemploymentIncome();
            return;
        }

        var row = character.GetJob(current.id);
        if (jobNameLabel != null) jobNameLabel.text = current.displayName;
        if (startedAtLabel != null) startedAtLabel.text = FormatStartedAt(row?.StartedAt);
        if (salaryLabel != null) salaryLabel.text = FormatSalary(current);
    }

    private static string FormatSalary(ProfessionConfig profession) {
        int amount = profession != null ? profession.baseSalary : 0;
        if (amount <= 0) return string.Empty;

        var net = NetworkManager.singleton as SimpleTownNetwork;
        int periodSeconds = net != null ? net.CityData.SalaryPeriodSeconds : 0;
        if (periodSeconds <= 0) return $"Salaire : {amount} €";

        return $"Salaire : {amount} € toutes les {FormatPeriod(periodSeconds)}";
    }

    private static string FormatUnemploymentIncome() {
        var net = NetworkManager.singleton as SimpleTownNetwork;
        if (net == null) return string.Empty;
        int amount = net.CityData.UnemployedIncome;
        if (amount <= 0) return string.Empty;
        int periodSeconds = net.CityData.SalaryPeriodSeconds;
        if (periodSeconds <= 0) return $"Allocation chômage : {amount} €";
        return $"Allocation chômage : {amount} € toutes les {FormatPeriod(periodSeconds)}";
    }

    private static string FormatPeriod(int totalSeconds) {
        if (totalSeconds < 60) return $"{totalSeconds} s";
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return seconds == 0 ? $"{minutes} min" : $"{minutes} min {seconds} s";
    }

    private static string FormatStartedAt(string iso) {
        if (string.IsNullOrEmpty(iso)) return string.Empty;
        if (DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                              DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                              out var dt)) {
            return $"Depuis le {dt.ToLocalTime():d MMM yyyy}";
        }
        return $"Depuis {iso}";
    }

    private void SendChange(string newProfessionId) {
        if (!NetworkClient.active || !NetworkClient.isConnected) return;
        NetworkClient.Send(new MissionChangeCareerMessage { newProfessionId = newProfessionId ?? "" });
    }
}
