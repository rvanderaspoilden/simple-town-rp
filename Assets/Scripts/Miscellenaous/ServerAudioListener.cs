using Mirror;
using UnityEngine;

namespace Miscellenaous
{
    public class ServerAudioListener : MonoBehaviour
    {
        private void Awake()
        {
            if (!NetworkServer.activeHost)
            {
                Destroy(this.gameObject);
            }
        }
    }
}