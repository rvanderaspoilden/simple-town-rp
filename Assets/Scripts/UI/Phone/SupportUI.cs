using Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phone app : remontée de bugs. Le joueur saisit une description, valide,
/// l'API persiste le rapport (lié au userId du JWT + characterId si dispo).
/// Une notification "Rapport envoyé" s'affiche à la complétion. Pas
/// d'assistance temps-réel à ce stade — fire-and-forget côté joueur.
/// </summary>
public class SupportUI : PhoneApplicationUI {
    [Header("Form")]
    [SerializeField] private TMP_InputField messageField;
    [SerializeField] private Button submitButton;
    [SerializeField] private TMP_Text feedbackText;

    private void OnEnable() {
        if (submitButton != null) submitButton.onClick.AddListener(OnSubmitClicked);
        SetFeedback(string.Empty);
        if (submitButton != null) submitButton.interactable = true;
    }

    private void OnDisable() {
        if (submitButton != null) submitButton.onClick.RemoveListener(OnSubmitClicked);
    }

    public override void Back() {
        PhoneControllerUI.Instance?.BackToHome();
    }

    private void OnSubmitClicked() {
        string msg = (messageField != null ? messageField.text : string.Empty)?.Trim();
        if (string.IsNullOrEmpty(msg)) {
            SetFeedback("Décrivez votre problème avant d'envoyer.");
            return;
        }
        if (ApiManager.Instance == null) {
            SetFeedback("Hors-ligne — impossible d'envoyer.");
            return;
        }

        submitButton.interactable = false;
        SetFeedback("Envoi…");

        string characterId = PlayerController.Local != null && PlayerController.Local.CharacterData != null
            ? PlayerController.Local.CharacterData.Id
            : null;

        ApiManager.Instance.CreateBugReport(msg, characterId, ok => {
            submitButton.interactable = true;
            if (ok) {
                if (messageField != null) messageField.text = string.Empty;
                SetFeedback("Rapport envoyé. Merci !");
                NotificationManager.Instance?.AddNotification("Rapport envoyé. Merci !", PhoneAppIds.Support);
            } else {
                SetFeedback("Échec de l'envoi — réessayez.");
            }
        });
    }

    private void SetFeedback(string text) {
        if (feedbackText != null) feedbackText.text = text;
    }
}
