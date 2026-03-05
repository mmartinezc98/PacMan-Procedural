using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// Gestiona el estado global de la partida y sincroniza variables entre jugadores.
/// Equivalente a PointsController.cs del ejercicio de Photon pero con .NET.
/// </summary>
public class GameState : MonoBehaviour
{
    #region VARIABLES

    [Header("Prefabs de jugadores")]
    [SerializeField] private GameObject _pacManPrefab;
    [SerializeField] private GameObject _ghostPrefab;

    [Header("Camara")]
    [SerializeField] private CameraController _cameraController;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI _infoText;    // "Soy PACMAN" / "Soy FANTASMA"
    [SerializeField] private TextMeshProUGUI _coinsText;   // contador monedas
    [SerializeField] private TextMeshProUGUI _statusText;  // estado partida
    [SerializeField] private GameObject _winPanel;    // panel fin partida
    [SerializeField] private TextMeshProUGUI _winText;     // texto del panel

    [Header("Sincronizacion")]
    [SerializeField] private float _syncInterval = 0.05f; // 20 veces por segundo

    // Jugadores
    [HideInInspector] public PlayerController localPlayer;
    [HideInInspector] public PlayerController rivalPlayer;

    // Estado de la partida
    private bool _gameOver = false;
    private bool _mazeReady = false;

    // Singleton
    public static GameState Instance;

    #endregion

    #region UNITY METHODS

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (NetworkManager.Instance.isHost)
        {
            // El host espera al cliente antes de generar el mapa y los jugadores
            UpdateInfoText("Esperando jugador...");
            StartCoroutine(WaitForConnectionThenStart());
        }
        else
        {
            // El cliente espera la semilla del host (llega via NetworkManager)
            UpdateInfoText("Conectando...");
        }
    }

    private void Update()
    {
        if (!_mazeReady || _gameOver) return;

        // Actualizamos contador de monedas para PacMan
        if (localPlayer != null && localPlayer.isPacMan && _coinsText != null)
            _coinsText.text = "Monedas: " + localPlayer.coinsCollected;

        // Condicion de victoria de PacMan
        if (localPlayer != null && localPlayer.isPacMan && localPlayer.hasFoundExit)
            PacManWins();
    }

    #endregion

    #region SETUP DE JUGADORES

    private void SetupLocalPlayer(bool isPacMan)
    {
        Vector3 spawnPos = isPacMan
            ? MazeGenerator.Instance.pacManSpawnPos
            : MazeGenerator.Instance.ghostSpawnPos;

        GameObject prefab = isPacMan ? _pacManPrefab : _ghostPrefab;
        GameObject obj = Instantiate(prefab, spawnPos, Quaternion.identity);

        localPlayer = obj.GetComponent<PlayerController>();
        localPlayer.isPacMan = isPacMan;
        localPlayer.isLocalPlayer = true;
        obj.tag = isPacMan ? "PacMan" : "Ghost";

        if (_cameraController != null)
            _cameraController.SetPlayer(obj.transform);

        UpdateInfoText(isPacMan ? "Soy PACMAN" : "Soy FANTASMA");
        Debug.Log("Jugador local: " + (isPacMan ? "PACMAN" : "FANTASMA"));
    }

    private void SetupRivalPlayer(bool rivalIsPacMan)
    {
        Vector3 spawnPos = rivalIsPacMan
            ? MazeGenerator.Instance.pacManSpawnPos
            : MazeGenerator.Instance.ghostSpawnPos;

        GameObject prefab = rivalIsPacMan ? _pacManPrefab : _ghostPrefab;
        GameObject obj = Instantiate(prefab, spawnPos, Quaternion.identity);

        rivalPlayer = obj.GetComponent<PlayerController>();
        rivalPlayer.isPacMan = rivalIsPacMan;
        rivalPlayer.isLocalPlayer = false;
        obj.tag = rivalIsPacMan ? "PacMan" : "Ghost";

        // Anadimos una luz tenue al rival para que sea visible en la niebla de guerra
        // Esta luz es independiente de la del CameraController (que es mas tenue y roja)
        GameObject rivalLightObj = new GameObject("RivalSelfLight");
        rivalLightObj.transform.SetParent(obj.transform);
        rivalLightObj.transform.localPosition = Vector3.up * 1f;
        Light rivalSelfLight = rivalLightObj.AddComponent<Light>();
        rivalSelfLight.type = LightType.Point;
        rivalSelfLight.range = 1.5f;
        rivalSelfLight.intensity = 1.5f;
        rivalSelfLight.color = rivalIsPacMan ? Color.yellow : Color.red;
        rivalSelfLight.shadows = LightShadows.None;

        if (_cameraController != null)
            _cameraController.SetRival(obj.transform);

        Debug.Log("Jugador rival creado: " + (rivalIsPacMan ? "PACMAN" : "FANTASMA"));
    }

    #endregion

    #region GENERACION DEL MAPA

    /// <summary>
    /// HOST: espera la conexion del cliente, envia la semilla y LUEGO genera el mapa.
    /// Asi el cliente recibe la semilla antes de que ninguno genere nada.
    /// </summary>
    private IEnumerator WaitForConnectionThenStart()
    {
        UpdateStatusText("Esperando jugador...");

        // Esperamos a que el cliente se conecte
        yield return new WaitUntil(() => NetworkManager.Instance.isConnected);
        Debug.Log("[HOST] Cliente conectado, generando mapa...");

        // Generamos la semilla y la enviamos al cliente ANTES de generar el mapa
        int seed = MazeGenerator.Instance.GenerateNewSeed();
        NetworkManager.Instance.SendSeed(seed);
        Debug.Log("[HOST] Semilla enviada: " + seed);

        // Pequeña pausa para que el cliente reciba la semilla
        yield return new WaitForSeconds(0.3f);

        // Generamos el mapa con esa semilla
        MazeGenerator.Instance.GenerateMap();

        SetupLocalPlayer(isPacMan: true);
        SetupRivalPlayer(rivalIsPacMan: false);
        StartGame();
    }

    /// <summary>
    /// CLIENTE: llamado desde NetworkManager cuando recibe la semilla.
    /// Genera el mapa con la semilla recibida (mismo resultado que el host).
    /// </summary>
    public void OnSeedReceived()
    {
        Debug.Log("[CLIENTE] Generando mapa con semilla: " + MazeGenerator.Instance.seed);
        MazeGenerator.Instance.GenerateMap();

        // CLIENTE es el Fantasma, su rival es PacMan
        SetupLocalPlayer(isPacMan: false);
        SetupRivalPlayer(rivalIsPacMan: true);
        StartGame();
    }

    private void StartGame()
    {
        _mazeReady = true;
        UpdateStatusText("Partida en curso!");
        StartCoroutine(SyncPositionLoop());
    }

    #endregion

    #region SINCRONIZACION DE POSICION

    /// <summary>
    /// Envia posicion y estado del jugador local al rival cada _syncInterval segundos.
    /// Equivalente a OnPhotonSerializeView() de IPunObservable.
    /// </summary>
    private IEnumerator SyncPositionLoop()
    {
        while (!_gameOver)
        {
            if (localPlayer != null && NetworkManager.Instance.isConnected)
            {
                NetworkManager.Instance.SendPosition(localPlayer.transform.position);
                NetworkManager.Instance.SendState(localPlayer.isInvisible, localPlayer.coinsCollected);
            }
            yield return new WaitForSeconds(_syncInterval);
        }
    }

    /// <summary>
    /// Actualiza la posicion del rival con datos recibidos por red.
    /// Equivalente a CambiarValorEnRed() del ejercicio de variables.
    /// </summary>
    public void UpdateRivalPosition(Vector3 newPos)
    {
        if (rivalPlayer != null)
            rivalPlayer.UpdateRemotePosition(newPos);
    }

    public void UpdateRivalState(bool invisible, int coins)
    {
        if (rivalPlayer != null)
            rivalPlayer.UpdateRemoteState(invisible, coins);
    }

    #endregion

    #region CONDICIONES DE VICTORIA

    public void GhostWins()
    {
        if (_gameOver) return;
        _gameOver = true;
        NetworkManager.Instance.SendEvent("ghostWins");
        ShowResult(localPlayer != null && !localPlayer.isPacMan);
    }

    public void PacManWins()
    {
        if (_gameOver) return;
        _gameOver = true;
        NetworkManager.Instance.SendEvent("pacmanWins");
        ShowResult(localPlayer != null && localPlayer.isPacMan);
    }

    private void ShowResult(bool localWon)
    {
        if (_winPanel != null) _winPanel.SetActive(true);
        if (_winText != null) _winText.text = localWon ? "HAS GANADO!" : "HAS PERDIDO!";
        UpdateStatusText(localWon ? "Victoria" : "Derrota");
    }

    #endregion

    #region UTILIDADES UI

    private void UpdateInfoText(string text)
    {
        if (_infoText != null) _infoText.text = text;
    }

    private void UpdateStatusText(string text)
    {
        if (_statusText != null) _statusText.text = text;
    }

    #endregion
}