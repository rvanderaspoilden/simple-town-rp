using System;
using System.Collections;
using Mirror;
using Sim.Deployment;
#if STRESS_TEST_BOTS
using Sim.StressTest;
#endif
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

        [Header("Environment")]
        [SerializeField]
        [Tooltip("Optional. If wired, the dropdown lets the player switch between the deployments declared in EnvironmentRegistry. " +
                 "If absent, the saved selection from PlayerPrefs still applies but the UI is hidden.")]
        private TMP_Dropdown environmentDropdown;

        [SerializeField]
        [Tooltip("Optional label displayed next to the status indicator (e.g. 'Production'). Hidden if null.")]
        private TextMeshProUGUI environmentLabel;

        [SerializeField]
        [Tooltip("Seconds between two automatic Mirror server health re-polls. 0 disables the periodic refresh.")]
        private float statusRefreshInterval = 0f;

        private Coroutine _statusCoroutine;

        private void Awake() {
#if STRESS_TEST_BOTS
            // Stress-test build: BotRunner owns the boot sequence (auth, connect,
            // wander). Skip the interactive Launcher entirely so it doesn't try
            // to wire its UI inputs or react to ApiManager events.
            if (CommandLineArgs.BotMode) {
                this.gameObject.SetActive(false);
                return;
            }
#endif

            ApiManager.OnAuthenticationSucceeded += OnAuthenticationSucceeded;
            ApiManager.OnAuthenticationFailed += this.OnAuthenticationFailed;

            this.PopulateEnvironmentDropdown();
            this.DisplaySignInPanel();
        }

        private void Start() {
#if STRESS_TEST_BOTS
            if (CommandLineArgs.BotMode) return;
#endif
            this.ApplyEnvironment(EnvironmentSelector.Current);
            this.RefreshStatusOnce();
            if (this.statusRefreshInterval > 0f) {
                _statusCoroutine = StartCoroutine(this.StatusRefreshLoop());
            }
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
            if (_statusCoroutine != null) StopCoroutine(_statusCoroutine);
        }

        // ── Environment selector ────────────────────────────────────────────

        private void PopulateEnvironmentDropdown() {
            if (this.environmentDropdown == null) return;
            var registry = EnvironmentSelector.Registry;
            if (registry == null || registry.Environments == null || registry.Environments.Count == 0) {
                this.environmentDropdown.gameObject.SetActive(false);
                return;
            }
            this.environmentDropdown.ClearOptions();
            int selectedIdx = 0;
            var options = new System.Collections.Generic.List<string>(registry.Environments.Count);
            for (int i = 0; i < registry.Environments.Count; i++) {
                var entry = registry.Environments[i];
                if (entry == null) continue;
                options.Add(entry.Name);
                if (entry.Name == EnvironmentSelector.Current.Name) selectedIdx = options.Count - 1;
            }
            this.environmentDropdown.AddOptions(options);
            this.environmentDropdown.SetValueWithoutNotify(selectedIdx);
            this.environmentDropdown.onValueChanged.AddListener(this.OnEnvironmentDropdownChanged);
        }

        private void OnEnvironmentDropdownChanged(int index) {
            string name = this.environmentDropdown.options[index].text;
            EnvironmentSelector.Select(name);
            this.ApplyEnvironment(EnvironmentSelector.Current);
            this.RefreshStatusOnce();
        }

        private void ApplyEnvironment(EnvironmentEntry entry) {
            if (entry == null) return;
            if (ApiManager.Instance != null) ApiManager.Instance.SetUri(entry.ApiUri);
            if (NetworkManager.singleton != null) NetworkManager.singleton.networkAddress = entry.MirrorAddress;
            if (this.environmentLabel != null) this.environmentLabel.text = entry.Name;
        }

        // ── Mirror health check ─────────────────────────────────────────────

        private IEnumerator StatusRefreshLoop() {
            var wait = new WaitForSeconds(this.statusRefreshInterval);
            while (true) {
                yield return wait;
                this.RefreshStatusOnce();
            }
        }

        private void RefreshStatusOnce() {
            string url = EnvironmentSelector.Current?.MirrorHealthUrl;
            if (string.IsNullOrEmpty(url)) {
                this.statusImg.color = Color.gray;   // health endpoint not configured
                return;
            }
            StartCoroutine(this.CheckMirrorHealth(url));
        }

        private IEnumerator CheckMirrorHealth(string url) {
            using var req = UnityWebRequest.Get(url);
            req.timeout = 5;
            yield return req.SendWebRequest();
            bool ok = req.result == UnityWebRequest.Result.Success && req.responseCode == 200;
            this.statusImg.color = ok ? Color.green : Color.red;
        }

        // ── UI ──────────────────────────────────────────────────────────────

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

        #endregion
    }
}
