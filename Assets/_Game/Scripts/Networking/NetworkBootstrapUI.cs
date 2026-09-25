using Unity.Netcode;
using UnityEngine;

namespace SIHKH.Networking
{
    /// <summary>
    /// Whitebox-only overlay with Host / Client buttons and a status line.
    /// Replaced by a real menu once Relay/Lobby exists; kept deliberately dumb until then.
    /// </summary>
    public class NetworkBootstrapUI : MonoBehaviour
    {
        private void OnGUI()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            GUILayout.BeginArea(new Rect(10, 10, 220, 110), GUI.skin.box);

            if (!nm.IsClient && !nm.IsServer)
            {
                if (GUILayout.Button("Host")) nm.StartHost();
                if (GUILayout.Button("Client")) nm.StartClient();
            }
            else
            {
                string role = nm.IsHost ? "Host" : nm.IsServer ? "Server" : "Client";
                // ConnectedClientsIds is server-only; clients don't know the roster.
                string clients = nm.IsServer ? nm.ConnectedClientsIds.Count.ToString() : "?";
                GUILayout.Label($"{role} — connected: {clients}");
                if (GUILayout.Button("Disconnect")) nm.Shutdown();
            }

            GUILayout.EndArea();
        }
    }
}
