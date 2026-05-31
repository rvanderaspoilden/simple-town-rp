using System;
using UnityEngine;

namespace Sim.Deployment {
    /// <summary>
    /// One deployment target the user can pick in the Launcher.
    ///   - Name           : label shown in the dropdown
    ///   - ApiUri         : where ApiManager talks to the backend (REST)
    ///   - MirrorAddress  : where SimpleTownNetwork.StartClient connects (host:port form, port read from
    ///                      NetworkManager's Transport asset)
    ///   - MirrorHealthUrl: HTTP URL of the Mirror server's /health endpoint
    ///                      (HealthHttpEndpoint in the server build). Used by the
    ///                      Launcher to show the green/red status indicator. Can be
    ///                      empty in which case the indicator is hidden.
    ///
    /// Edit the values in the EnvironmentRegistry asset, not in code — the whole
    /// point of this struct is to let you add a Staging env later without rebuilding.
    /// </summary>
    [Serializable]
    public class EnvironmentEntry {
        [SerializeField] private string name = "Local";
        [SerializeField] private string apiUri = "http://localhost:3000";
        [SerializeField] private string mirrorAddress = "localhost";
        [SerializeField] private string mirrorHealthUrl = "http://localhost:8080/health";

        public string Name            => name;
        public string ApiUri          => apiUri;
        public string MirrorAddress   => mirrorAddress;
        public string MirrorHealthUrl => mirrorHealthUrl;

        public EnvironmentEntry() {}

        // Used by the fallback path when the registry asset is missing.
        public EnvironmentEntry(string name, string apiUri, string mirrorAddress, string mirrorHealthUrl) {
            this.name = name;
            this.apiUri = apiUri;
            this.mirrorAddress = mirrorAddress;
            this.mirrorHealthUrl = mirrorHealthUrl;
        }
    }
}
