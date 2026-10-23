using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace GoingBatty
{
    /// <summary>
    /// Entry point. Everything (camera, networking, map, systems, UI) is created here in code,
    /// so the project has no hand-wired scenes or prefabs — easy to change from a text prompt.
    /// </summary>
    public class Bootstrap : MonoBehaviour
    {
        public static ILobbyService Lobby { get; private set; }
        public static Camera MainCamera { get; private set; }
        public static AudioSource UiAudio { get; private set; }

        static Bootstrap _i;
        float _menuYaw, _nextDemoPulse;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_i != null) return;
            var go = new GameObject("GoingBatty");
            DontDestroyOnLoad(go);
            go.AddComponent<Bootstrap>();
        }

        void Awake()
        {
            _i = this;
            Application.runInBackground = true; // keep networking alive when alt-tabbed
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 240;
            AudioListener.volume = Settings.MasterVolume;
            Sfx.Build();

            // Remove any camera/listener from the (empty) scene; we make our own.
            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) Destroy(l);
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) Destroy(c.gameObject);

            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(transform, false);
            MainCamera = camGo.AddComponent<Camera>();
            MainCamera.clearFlags = CameraClearFlags.SolidColor;
            MainCamera.backgroundColor = Color.black;
            MainCamera.nearClipPlane = 0.03f;
            MainCamera.farClipPlane = 200f;
            MainCamera.fieldOfView = 75f;
            camGo.AddComponent<AudioListener>();
            camGo.tag = "MainCamera";

            UiAudio = gameObject.AddComponent<AudioSource>();
            UiAudio.spatialBlend = 0f;
            UiAudio.playOnAwake = false;

            CreateNetworking();

            gameObject.AddComponent<MicInput>();
            var sonar = new GameObject("Sonar");
            sonar.transform.SetParent(transform, false);
            sonar.AddComponent<SonarSystem>();

            var map = MapBuilder.Build(transform);
            var session = gameObject.AddComponent<GameSession>();
            session.Map = map;
            session.CreateBoxViews();
            gameObject.AddComponent<BotDirector>();

            gameObject.AddComponent<UIRoot>();
            gameObject.AddComponent<UpdateChecker>();

            ReturnCameraToMenu();
        }

        static void CreateNetworking()
        {
            // Built inactive so the components are configured before their Awake runs.
            var netGo = new GameObject("Network");
            netGo.SetActive(false);
            var nm = netGo.AddComponent<NetworkManager>();
            var transport = netGo.AddComponent<UnityTransport>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
                ConnectionApproval = false,
                TickRate = 30,
            };
            netGo.SetActive(true);
            DontDestroyOnLoad(netGo);
            Net.Init(nm);

            // Swap this line to change online backend (e.g. a future SteamLobbyService).
            Lobby = new UgsLobbyService();
            _ = Lobby.InitializeAsync();
        }

        public static void ReturnCameraToMenu()
        {
            if (MainCamera == null || _i == null) return;
            MainCamera.transform.SetParent(_i.transform, false);
            MainCamera.fieldOfView = 75f;
        }

        void Update()
        {
            // Menu / lobby backdrop: slowly look around the centre of the arena, pinging sonar.
            if (MainCamera != null && MainCamera.transform.parent == transform)
            {
                _menuYaw += Time.deltaTime * 9f;
                var pos = new Vector3(0, MapBuilder.Raised + 1.6f, 0);
                MainCamera.transform.SetPositionAndRotation(pos, Quaternion.Euler(8f * Mathf.Sin(Time.time * 0.3f), _menuYaw, 0));
                if (Time.time >= _nextDemoPulse && SonarSystem.I != null)
                {
                    _nextDemoPulse = Time.time + Random.Range(1.4f, 2.6f);
                    float s = Random.value < 0.25f ? Random.Range(0.8f, 1f) : Random.Range(0.3f, 0.6f);
                    SonarSystem.I.Pulse(pos, MainCamera.transform.forward, s, Look.SelfSonar, 255, isLocal: false);
                }
            }
        }
    }
}
