using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Sim {
    public class Launcher : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private TextMeshProUGUI errorText;

        [SerializeField]
        private TMP_InputField signInPseudoInputField;

        [SerializeField]
        private TMP_InputField signInPasswordInputField;

        [SerializeField]
        private TMP_InputField signUpPseudoInputField;

        [SerializeField]
        private TMP_InputField signUpPasswordInputField;

        [SerializeField]
        private GameObject signInPanel;

        [SerializeField]
        private GameObject signupPanel;

        [SerializeField]
        private Image statusImg;

        private void Awake() {
            ApiManager.OnAuthenticationSucceeded += OnAuthenticationSucceeded;
            ApiManager.OnAuthenticationFailed += this.OnAuthenticationFailed;
            ApiManager.OnServerStatusChanged += this.OnServerStatusChanged;

            this.DisplaySignInPanel();
        }

        private void Start() {
            ApiManager.Instance.CheckServerStatus();
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.Tab)) {
                Selectable next = EventSystem.current.currentSelectedGameObject
                    .GetComponent<Selectable>()
                    .FindSelectableOnDown();

                if (next) next.Select();
            }

            if (Input.GetKeyDown(KeyCode.Return)) {
                if (this.signInPanel.activeSelf) {
                    this.SignIn();
                } else {
                    this.SignUp();
                }
            }
        }

        private void OnDestroy() {
            ApiManager.OnAuthenticationSucceeded -= this.OnAuthenticationSucceeded;
            ApiManager.OnAuthenticationFailed -= this.OnAuthenticationFailed;
            ApiManager.OnServerStatusChanged -= this.OnServerStatusChanged;
        }

        public void DisplaySignInPanel() {
            this.signInPanel.SetActive(true);
            this.signupPanel.SetActive(false);

            this.ClearForm();

            this.signInPseudoInputField.Select();
        }

        public void DisplaySignUpPanel() {
            this.signupPanel.SetActive(true);
            this.signInPanel.SetActive(false);

            this.ClearForm();

            this.signUpPseudoInputField.Select();
        }

        private void ClearForm() {
            this.signUpPseudoInputField.text = string.Empty;
            this.signUpPasswordInputField.text = string.Empty;
            this.signInPseudoInputField.text = string.Empty;
            this.signInPasswordInputField.text = string.Empty;
        }

        public void SignIn() {
            if (this.signInPseudoInputField.text == string.Empty || this.signInPasswordInputField.text == string.Empty) return;

            this.ResetErrorText();

            ApiManager.Instance.Authenticate(this.signInPseudoInputField.text, this.signInPasswordInputField.text);
        }

        public void SignUp() {
            if (this.signUpPseudoInputField.text == string.Empty || this.signUpPasswordInputField.text == string.Empty) return;

            this.ResetErrorText();

            StartCoroutine(this.SignUpCoroutine());
        }

        private IEnumerator SignUpCoroutine() {
            UnityWebRequest request = ApiManager.Instance.CreateUserRequest(new CreateUserRequest() {
                username = this.signUpPseudoInputField.text,
                password = this.signUpPasswordInputField.text
            });

            yield return request.SendWebRequest();

            if (request.responseCode == 201) {
                Debug.Log("Account creation succeeded !");
                this.DisplaySignInPanel();
            } else {
                this.errorText.text = ApiManager.ExtractErrorMessage(request);
            }
        }

        private void ResetErrorText() => this.errorText.text = String.Empty;

        #region Callbacks

        private void OnAuthenticationSucceeded() {
            SceneManager.LoadScene("Main Menu");
        }

        private void OnAuthenticationFailed(String msg) => this.errorText.text = msg;

        private void OnServerStatusChanged(bool isActive) => this.statusImg.color = isActive ? Color.green : Color.red;

        #endregion
    }
}