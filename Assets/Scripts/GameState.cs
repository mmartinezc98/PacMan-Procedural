using System.Collections;
using UnityEngine;
using TMPro;

public class GameState : MonoBehaviour
{
    #region VARIABLES

    [Header("Prefabs de jugadores")]
    [SerializeField] private GameObject _pacManPrefab;  // Prefab del PacMan
    [SerializeField] private GameObject _ghostPrefab;   // Prefab del Fantasma

    [Header("Camara")]
    [SerializeField] private CameraController _cameraController; // Controlador de camara

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI _infoText;   // Texto informativo  
    [SerializeField] private TextMeshProUGUI _coinsText;  // Texto del contador de monedas
    [SerializeField] private TextMeshProUGUI _statusText; // Texto de estado de la partida
    [SerializeField] private GameObject _winPanel;        // Panel de victoria/derrota
    [SerializeField] private TextMeshProUGUI _winText;    // Texto dentro del panel de resultado

    [Header("Salida")]
    [SerializeField] private GameObject _exitObject;          // Objeto Exit en la escena
    [SerializeField] private Material _exitActiveMaterial;    // Material cuando la salida esta abierta
    [SerializeField] private Material _exitInactiveMaterial;  // Material cuando la salida esta cerrada

    [Header("Sincronizacion")]
    [SerializeField] private float _syncInterval = 0.05f; // Frecuencia de sincronizacion de posicion por red (segundos)

    // Referencias a los jugadores instanciados
    [HideInInspector] public PlayerController localPlayer;  // El jugador que corre en esta maquina
    [HideInInspector] public PlayerController rivalPlayer;  // El jugador remoto (recibido por red)

    // Estado general de la partida
    private bool _gameOver = false;   // True cuando la partida ha terminado
    private bool _mazeReady = false;  // True cuando el mapa ya esta generado y el juego ha empezado

    // Contador de monedas
    private int _totalCoins = 0;  // Total de monedas generadas en el mapa al inicio
    private int _coinsFound = 0;  // Monedas recogidas hasta ahora

    // Estado de la salida
    public bool exitActive = false; // True cuando todas las monedas han sido recogidas

    // Singleton para acceder a GameState desde cualquier script con GameState.Instance
    public static GameState Instance;

    #endregion

    #region UNITY METHODS

    private void Awake()
    {
        // Registramos esta instancia como el singleton global
        Instance = this;
    }

    private void Start()
    {
        // El host espera al cliente antes de generar el mapa
        // El cliente espera recibir la semilla del host para generar el mismo mapa
        if (NetworkManager.Instance.isHost)
        {
            UpdateInfoText("Esperando jugador...");
            StartCoroutine(WaitForConnectionThenStart());
        }
        else
        {
            UpdateInfoText("Conectando...");
        }
    }

    private void Update()
    {
        if (!_mazeReady || _gameOver) return;

        // Actualizamos el texto de monedas en pantalla cada frame
        // FIX: antes solo se mostraba si el jugador local era PacMan,
        // ahora se muestra siempre (tanto PacMan como Fantasma ven el contador)
        if (localPlayer != null && _coinsText != null)
            _coinsText.text = "Monedas: " + _coinsFound + " / " + _totalCoins;
    }

    #endregion

    #region SETUP DE JUGADORES

    /// <summary>
    /// Instancia y configura el jugador local en su posicion de spawn.
    /// Asigna el tag correcto y enlaza la camara.
    /// </summary>
    private void SetupLocalPlayer(bool isPacMan)
    {
        // Elegimos la posicion de spawn segun el rol
        Vector3 spawnPos = isPacMan
            ? MazeGenerator.Instance.pacManSpawnPos
            : MazeGenerator.Instance.ghostSpawnPos;

        GameObject prefab = isPacMan ? _pacManPrefab : _ghostPrefab;
        GameObject obj = Instantiate(prefab, spawnPos, Quaternion.identity);

        // Configuramos el PlayerController
        localPlayer = obj.GetComponent<PlayerController>();
        localPlayer.isPacMan = isPacMan;
        localPlayer.isLocalPlayer = true; // Este jugador es controlado por esta maquina
        obj.tag = isPacMan ? "PacMan" : "Ghost";

        // Enlazamos la camara al jugador local y le pasamos la luz de visibilidad
        if (_cameraController != null)
        {
            _cameraController.SetPlayer(obj.transform);
            localPlayer.playerLight = _cameraController.GetPlayerLight();
        }

        UpdateInfoText(isPacMan ? "Soy PACMAN" : "Soy FANTASMA");
        Debug.Log("Jugador local: " + (isPacMan ? "PACMAN" : "FANTASMA"));
    }

    /// <summary>
    /// Instancia y configura el jugador rival (remoto).
    /// Este jugador no recibe input del teclado, solo actualizaciones de red.
    /// </summary>
    private void SetupRivalPlayer(bool rivalIsPacMan)
    {
        Vector3 spawnPos = rivalIsPacMan
            ? MazeGenerator.Instance.pacManSpawnPos
            : MazeGenerator.Instance.ghostSpawnPos;

        GameObject prefab = rivalIsPacMan ? _pacManPrefab : _ghostPrefab;
        GameObject obj = Instantiate(prefab, spawnPos, Quaternion.identity);

        rivalPlayer = obj.GetComponent<PlayerController>();
        rivalPlayer.isPacMan = rivalIsPacMan;
        rivalPlayer.isLocalPlayer = false; // Este jugador es remoto, no recibe input local
        obj.tag = rivalIsPacMan ? "PacMan" : "Ghost";

        // Creamos una luz pequena sobre el rival para que sea visible en la niebla
        GameObject rivalLightObj = new GameObject("RivalSelfLight");
        rivalLightObj.transform.SetParent(obj.transform);
        rivalLightObj.transform.localPosition = Vector3.up * 1f;
        Light rivalSelfLight = rivalLightObj.AddComponent<Light>();
        rivalSelfLight.type = LightType.Point;
        rivalSelfLight.range = 1.5f;
        rivalSelfLight.intensity = 1.5f;
        rivalSelfLight.color = rivalIsPacMan ? Color.yellow : Color.red; // Amarillo = PacMan, Rojo = Fantasma
        rivalSelfLight.shadows = LightShadows.None;

        if (_cameraController != null)
            _cameraController.SetRival(obj.transform);

        Debug.Log("Jugador rival creado: " + (rivalIsPacMan ? "PACMAN" : "FANTASMA"));
    }

    #endregion

    #region GENERACION DEL MAPA

    /// <summary>
    /// Corrutina del HOST: espera a que el cliente se conecte,
    /// genera una semilla aleatoria, la envia por red y genera el mapa.
    /// </summary>
    private IEnumerator WaitForConnectionThenStart()
    {
        UpdateStatusText("Esperando jugador...");

        // Esperamos hasta que el cliente se conecte
        yield return new WaitUntil(() => NetworkManager.Instance.isConnected);
        Debug.Log("[HOST] Cliente conectado, generando mapa...");

        // Generamos y enviamos la semilla para que ambos generen el mismo mapa
        int seed = MazeGenerator.Instance.GenerateNewSeed();
        NetworkManager.Instance.SendSeed(seed);
        Debug.Log("[HOST] Semilla enviada: " + seed);

        // Pequena espera para asegurar que el cliente recibe la semilla antes de continuar
        yield return new WaitForSeconds(0.3f);

        // Generamos el mapa, contamos monedas y configuramos la salida
        MazeGenerator.Instance.GenerateMap();
        CountTotalCoins();
        SetupExitObject();

        // El host siempre es PacMan, el rival es el Fantasma
        SetupLocalPlayer(isPacMan: true);
        SetupRivalPlayer(rivalIsPacMan: false);
        StartGame();
    }

    /// <summary>
    /// Llamado en el CLIENTE cuando recibe la semilla del host.
    /// Genera el mapa con la misma semilla para que sea identico al del host.
    /// </summary>
    public void OnSeedReceived()
    {
        Debug.Log("[CLIENTE] Generando mapa con semilla: " + MazeGenerator.Instance.seed);
        MazeGenerator.Instance.GenerateMap();
        CountTotalCoins();
        SetupExitObject();

        // El cliente siempre es el Fantasma, el rival es PacMan
        SetupLocalPlayer(isPacMan: false);
        SetupRivalPlayer(rivalIsPacMan: true);
        StartGame();
    }

    /// <summary>
    /// Marca la partida como iniciada y arranca el bucle de sincronizacion de red.
    /// </summary>
    private void StartGame()
    {
        _mazeReady = true;
        UpdateStatusText("Partida en curso!");
        StartCoroutine(SyncPositionLoop());
    }

    #endregion

    #region MONEDAS Y SALIDA

    /// <summary>
    /// Cuenta todas las monedas en el mapa al inicio de la partida.
    /// Se llama justo despues de GenerateMap().
    /// </summary>
    private void CountTotalCoins()
    {
        _totalCoins = GameObject.FindGameObjectsWithTag("Coin").Length;
        _coinsFound = 0;
        exitActive = false;
        Debug.Log("Total de monedas en el mapa: " + _totalCoins);
    }

    /// <summary>
    /// Busca el objeto Exit en la escena y lo desactiva hasta que se recojan todas las monedas.
    /// </summary>
    private void SetupExitObject()
    {
        GameObject exit = GameObject.FindGameObjectWithTag("Exit");
        if (exit == null) return;

        _exitObject = exit;

        // Desactivamos el collider para que PacMan no pueda usarla todavia
        Collider col = _exitObject.GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // Ponemos el material de salida inactiva (apagada)
        Renderer rend = _exitObject.GetComponent<Renderer>();
        if (rend != null && _exitInactiveMaterial != null)
            rend.material = _exitInactiveMaterial;
    }

    /// <summary>
    /// Llamado desde PlayerController cada vez que PacMan recoge una moneda.
    /// Si se recogen todas, activa la salida.
    /// </summary>
    public void OnCoinCollected()
    {
        _coinsFound++;

        // Comprobamos si ya se recogieron todas las monedas
        if (_coinsFound >= _totalCoins)
        {
            ActivateExit();
        }
    }

    /// <summary>
    /// Activa la salida: habilita su collider y cambia su material al de activa.
    /// </summary>
    private void ActivateExit()
    {
        exitActive = true;
        Debug.Log("Todas las monedas recogidas! Salida activada.");

        if (_exitObject == null) return;

        // Activamos el collider para que PacMan pueda entrar
        Collider col = _exitObject.GetComponent<Collider>();
        if (col != null) col.enabled = true;

        // Cambiamos el material a activo (brillante/visible)
        Renderer rend = _exitObject.GetComponent<Renderer>();
        if (rend != null && _exitActiveMaterial != null)
            rend.material = _exitActiveMaterial;

        UpdateStatusText("Salida abierta! Ve a la salida!");
    }

    #endregion

    #region SINCRONIZACION

    /// <summary>
    /// Bucle que envia la posicion y estado del jugador local por red cada _syncInterval segundos.
    /// Se ejecuta durante toda la partida hasta que termina.
    /// </summary>
    private IEnumerator SyncPositionLoop()
    {
        while (!_gameOver)
        {
            if (localPlayer != null && NetworkManager.Instance.isConnected)
            {
                // Enviamos posicion
                NetworkManager.Instance.SendPosition(localPlayer.transform.position);
                // Enviamos estado: si esta invisible y cuantas monedas ha recogido
                NetworkManager.Instance.SendState(localPlayer.isInvisible, localPlayer.coinsCollected);
            }
            yield return new WaitForSeconds(_syncInterval);
        }
    }

    /// <summary>
    /// Llamado desde NetworkManager cuando llega una actualizacion de posicion del rival.
    /// </summary>
    public void UpdateRivalPosition(Vector3 newPos)
    {
        if (rivalPlayer != null)
            rivalPlayer.UpdateRemotePosition(newPos);
    }

    /// <summary>
    /// Llamado desde NetworkManager cuando llega una actualizacion de estado del rival.
    /// FIX: si el rival es PacMan, sincronizamos _coinsFound para que el Fantasma
    /// vea el contador actualizado en su pantalla.
    /// </summary>
    public void UpdateRivalState(bool invisible, int coins)
    {
        if (rivalPlayer != null)
            rivalPlayer.UpdateRemoteState(invisible, coins);

        // FIX: el cliente Fantasma recibe aqui las monedas recogidas por PacMan
        // y actualiza su propio contador para que la UI sea correcta
        if (rivalPlayer != null && rivalPlayer.isPacMan)
        {
            _coinsFound = coins;
        }
    }

    #endregion

    #region CONDICIONES DE VICTORIA

    /// <summary>
    /// El Fantasma gana
    /// Envia el evento por red y muestra el resultado en ambas pantallas.
    /// </summary>
    public void GhostWins()
    {
        if (_gameOver) return;
        _gameOver = true;
        NetworkManager.Instance.SendEvent("ghostWins");
        ShowResult(localPlayer != null && !localPlayer.isPacMan); // True si el local es el Fantasma
    }

    /// <summary>
    /// PacMan gana (llego a la salida con todas las monedas).
    /// Envia el evento por red y muestra el resultado en ambas pantallas.
    /// </summary>
    public void PacManWins()
    {
        if (_gameOver) return;
        _gameOver = true;
        NetworkManager.Instance.SendEvent("pacmanWins");
        ShowResult(localPlayer != null && localPlayer.isPacMan); // True si el local es PacMan
    }

    /// <summary>
    /// Muestra el panel de resultado con el mensaje correcto segun si gano o perdio el jugador local.
    /// </summary>
    private void ShowResult(bool localWon)
    {
        if (_winPanel != null) _winPanel.SetActive(true);
        if (_winText != null) _winText.text = localWon ? "HAS GANADO!" : "HAS PERDIDO!";
        UpdateStatusText(localWon ? "Victoria" : "Derrota");
    }

    #endregion

    #region UI

    /// <summary>
    /// Actualiza el texto informativo superior
    /// </summary>
    private void UpdateInfoText(string text)
    {
        if (_infoText != null) _infoText.text = text;
    }

    /// <summary>
    /// Actualiza el texto de estado de la partida
    /// </summary>
    private void UpdateStatusText(string text)
    {
        if (_statusText != null) _statusText.text = text;
    }

    #endregion
}