using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Gestiona la pantalla inicial del juego con autodescubrimiento de IP.
/// El HOST emite un broadcast UDP en la red local anunciando su presencia.
/// El CLIENTE escucha ese broadcast, obtiene la IP automaticamente y se conecta.
/// Cuando ambos estan conectados se carga la escena del juego.
/// </summary>
public class MenuManager : MonoBehaviour
{
    #region VARIABLES

    [Header("Paneles UI")]
    [SerializeField] private GameObject _rolePanel;      // botones HOST / CLIENTE
    [SerializeField] private GameObject _waitingPanel;   // "Buscando / Esperando..."
    [SerializeField] private TextMeshProUGUI _waitingText;    // texto de estado
    [SerializeField] private TextMeshProUGUI _connectedText;  // "Conectado! Cargando..."

    [Header("Configuracion")]
    [SerializeField] private string _gameSceneName = "Game";
    [SerializeField] private int _tcpPort = 7777;
    [SerializeField] private int _udpPort = 7778;   // puerto broadcast

    // Mensaje de broadcast que identifica este juego
    private const string BROADCAST_MSG = "PROCPAC_HOST";

    // UDP para autodescubrimiento
    private UdpClient _udpClient;
    private Thread _udpThread;
    private bool _udpRunning = false;
    private string _foundHostIP = null; // IP del host encontrada por el cliente

    private NetworkManager _networkManager;
    private bool _sceneLoading = false;

    #endregion

    #region UNITY METHODS

    private void Start()
    {
        ShowRolePanel();
    }

    private void Update()
    {
        // Cliente: cuando encontramos la IP del host, conectamos
        if (_foundHostIP != null)
        {
            string ip = _foundHostIP;
            _foundHostIP = null;
            ConnectAsClient(ip);
        }

        // Cuando ambos esten conectados, cargamos la escena
        if (!_sceneLoading && _networkManager != null && _networkManager.isConnected)
        {
            StartCoroutine(LoadGameScene());
        }
    }

    private void OnDestroy()
    {
        StopUDP();
    }

    #endregion

    #region BOTONES UI

    /// <summary>
    /// Boton HOST: arranca el servidor TCP y emite broadcast UDP
    /// para que el cliente encuentre la IP automaticamente.
    /// </summary>
    public void OnHostButtonPressed()
    {
        _rolePanel.SetActive(false);
        _waitingPanel.SetActive(true);
        _waitingText.text = "Esperando jugador...";

        // Creamos el NetworkManager como host
        // InitConnection() se llama desde DelayedInitConnection(), no aqui
        CreateNetworkManager(isHost: true, ip: "");

        // Emitimos broadcast UDP para que el cliente nos encuentre
        StartUDPBroadcast();
    }

    /// <summary>
    /// Boton CLIENTE: escucha el broadcast UDP del host
    /// y se conecta automaticamente cuando lo encuentra.
    /// </summary>
    public void OnClientButtonPressed()
    {
        _rolePanel.SetActive(false);
        _waitingPanel.SetActive(true);
        _waitingText.text = "Buscando partida...";

        // Escuchamos el broadcast del host en hilo secundario
        StartUDPListener();
    }

    #endregion

    #region AUTODESCUBRIMIENTO UDP

    /// <summary>
    /// HOST: emite un broadcast UDP cada segundo con su IP.
    /// El cliente escucha este broadcast para encontrar al host.
    /// </summary>
    private void StartUDPBroadcast()
    {
        _udpRunning = true;
        _udpThread = new Thread(() =>
        {
            using (UdpClient udp = new UdpClient())
            {
                udp.EnableBroadcast = true;
                IPEndPoint endpoint = new IPEndPoint(IPAddress.Broadcast, _udpPort);
                byte[] data = Encoding.UTF8.GetBytes(BROADCAST_MSG);

                while (_udpRunning)
                {
                    try
                    {
                        udp.Send(data, data.Length, endpoint);
                        Thread.Sleep(1000); // emitimos cada segundo
                    }
                    catch { break; }
                }
            }
        });
        _udpThread.IsBackground = true;
        _udpThread.Start();
    }

    /// <summary>
    /// CLIENTE: escucha el broadcast UDP del host.
    /// Cuando lo recibe, guarda la IP para conectarse en el hilo de Unity.
    /// </summary>
    private void StartUDPListener()
    {
        _udpRunning = true;
        _udpThread = new Thread(() =>
        {
            try
            {
                using (UdpClient udp = new UdpClient(_udpPort))
                {
                    IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);

                    while (_udpRunning)
                    {
                        byte[] data = udp.Receive(ref sender);
                        string msg = Encoding.UTF8.GetString(data);

                        if (msg == BROADCAST_MSG)
                        {
                            // Encontramos al host: guardamos su IP
                            // Update() la procesara en el hilo de Unity
                            _foundHostIP = sender.Address.ToString();
                            _udpRunning = false;
                            break;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                if (_udpRunning)
                    Debug.LogError("[UDP] Error: " + e.Message);
            }
        });
        _udpThread.IsBackground = true;
        _udpThread.Start();
    }

    private void StopUDP()
    {
        _udpRunning = false;
        try { _udpThread?.Interrupt(); } catch { }
        try { _udpClient?.Close(); } catch { }
    }

    #endregion

    #region CONEXION

    /// <summary>
    /// Conecta como cliente a la IP encontrada por UDP.
    /// Llamado desde Update() cuando _foundHostIP tiene valor.
    /// </summary>
    private void ConnectAsClient(string ip)
    {
        StopUDP();
        _waitingText.text = "Host encontrado: " + ip + "\nConectando...";

        // InitConnection() se llama desde DelayedInitConnection(), no aqui
        CreateNetworkManager(isHost: false, ip: ip);
    }

    /// <summary>
    /// Crea el GameObject NetworkManager con DontDestroyOnLoad
    /// para que persista cuando se cargue la escena del juego.
    /// </summary>
    private void CreateNetworkManager(bool isHost, string ip)
    {
        // Desconectamos y destruimos el NetworkManager anterior para liberar el puerto
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.Disconnect();
            Destroy(NetworkManager.Instance.gameObject);
        }

        GameObject obj = new GameObject("NetworkManager");
        _networkManager = obj.AddComponent<NetworkManager>();
        _networkManager.isHost = isHost;
        _networkManager.serverIP = ip;
        _networkManager.port = _tcpPort;

        DontDestroyOnLoad(obj);

        // Reseteamos la flag y esperamos un momento antes de abrir el puerto
        _connectionStarted = false;
        StartCoroutine(DelayedInitConnection());
    }

    private bool _connectionStarted = false;

    private IEnumerator DelayedInitConnection()
    {
        if (_connectionStarted) yield break;
        _connectionStarted = true;

        yield return new WaitForSeconds(0.5f);
        if (_networkManager != null)
            _networkManager.InitConnection();
    }

    #endregion

    #region CARGA DE ESCENA

    private IEnumerator LoadGameScene()
    {
        _sceneLoading = true;
        StopUDP();

        if (_connectedText != null)
        {
            _connectedText.gameObject.SetActive(true);
            _connectedText.text = "Conectado! Cargando partida...";
        }

        if (_waitingPanel != null)
            _waitingPanel.SetActive(false);

        yield return new WaitForSeconds(1.5f);

        SceneManager.LoadScene(_gameSceneName);
    }

    #endregion

    #region UTILIDADES

    private void ShowRolePanel()
    {
        if (_rolePanel != null) _rolePanel.SetActive(true);
        if (_waitingPanel != null) _waitingPanel.SetActive(false);
        if (_connectedText != null) _connectedText.gameObject.SetActive(false);
    }

    #endregion
}