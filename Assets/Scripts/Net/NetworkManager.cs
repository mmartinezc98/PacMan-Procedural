using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Gestiona la conexion de red con .NET puro (System.Net.Sockets).
/// Equivalente a RedController.cs del ejercicio de Photon pero con TCP.
///
/// HOST  -> crea TcpListener, espera al cliente, genera la semilla y la envia.
/// CLIENTE -> se conecta con TcpClient, recibe la semilla y genera el mismo mapa.
///
/// Mensajes enviados (JSON simple):
///   {"type":"seed","value":12345}
///   {"type":"pos","x":1.5,"y":0.5,"z":3.2}
///   {"type":"state","invisible":false,"coins":3}
///   {"type":"event","name":"ghostWins"}
///   {"type":"event","name":"pacmanWins"}
/// </summary>
public class NetworkManager : MonoBehaviour
{
    #region VARIABLES

    [Header("Configuracion de red")]
    public bool isHost = false;         // marcar true en el HOST
    public string serverIP = "127.0.0.1";   // IP del host (el cliente la introduce)
    public int port = 7777;

    [Header("Estado (solo lectura)")]
    public bool isConnected = false;

    // Componentes TCP de .NET
    private TcpListener _server;   // solo el host
    private TcpClient _client;   // ambos jugadores
    private NetworkStream _stream;

    // Hilo secundario para escuchar mensajes sin bloquear Unity
    private Thread _receiveThread;
    private bool _isRunning = false;

    // Cola de mensajes: el hilo de red encola, Update() de Unity desencola
    // Necesario porque Unity no permite modificar GameObjects desde hilos secundarios
    private readonly Queue<string> _messageQueue = new Queue<string>();
    private readonly object _queueLock = new object();

    // Singleton
    public static NetworkManager Instance;

    #endregion

    #region UNITY METHODS

    private void Awake()
    {
        // Si ya existe un NetworkManager (viene del menu con DontDestroyOnLoad),
        // destruimos este y usamos el que ya existe
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // Si este NO es el singleton activo nos destruimos y no hacemos nada
        if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        // No llamamos a InitConnection() aqui.
        // El MenuManager lo llama via DelayedInitConnection() con un pequeño retraso.
        // En pruebas directas sin menu, llamar a InitConnection() manualmente desde el Inspector
        // o añadir un boton de debug.
    }

    /// <summary>
    /// Inicia la conexion. Llamado desde Start() o manualmente desde MenuManager.
    /// </summary>
    public void InitConnection()
    {
        if (isHost)
            StartHost();
        else
            StartClient();
    }

    private void Update()
    {
        // Procesamos los mensajes encolados en el hilo principal de Unity
        lock (_queueLock)
        {
            while (_messageQueue.Count > 0)
            {
                string msg = _messageQueue.Dequeue();
                ProcessMessage(msg);
            }
        }
    }

    private void OnDestroy()
    {
        // Solo desconectamos si somos la instancia activa
        // Evita que el duplicado destruido corte la conexion del original
        if (Instance == this)
            Disconnect();
    }
    private void OnApplicationQuit() { Disconnect(); }

    #endregion

    #region CONEXION HOST

    private void StartHost()
    {
        try
        {
            // Cerramos cualquier servidor previo
            if (_server != null)
            {
                _server.Stop();
                _server = null;
            }

            _server = new TcpListener(IPAddress.Any, port);

            // SO_REUSEADDR: permite reusar el puerto inmediatamente
            // sin esperar al timeout del SO (TIME_WAIT)
            _server.Server.SetSocketOption(
                System.Net.Sockets.SocketOptionLevel.Socket,
                System.Net.Sockets.SocketOptionName.ReuseAddress,
                true
            );

            _server.Start();
            Debug.Log("[HOST] Servidor iniciado en puerto " + port + ". Esperando cliente...");

            // Esperamos al cliente en hilo secundario para no bloquear Unity
            Thread t = new Thread(WaitForClient);
            t.IsBackground = true;
            t.Start();
        }
        catch (Exception e)
        {
            Debug.LogError("[HOST] Error al iniciar servidor: " + e.Message);
        }
    }

    private void WaitForClient()
    {
        try
        {
            _client = _server.AcceptTcpClient();
            _stream = _client.GetStream();
            isConnected = true;
            Debug.Log("[HOST] Cliente conectado!");

            // La semilla se envia desde GameState cuando MazeGenerator este listo
            StartReceiving();
        }
        catch (Exception e)
        {
            // WSACancelBlockingCall ocurre al cerrar el servidor mientras espera cliente
            // Es normal al parar el juego o volver al menu, no es un error real
            if (_isRunning)
                Debug.LogError("[HOST] Error esperando cliente: " + e.Message);
        }
    }

    /// <summary>
    /// Envia la semilla al cliente. Llamado desde GameState cuando el mapa esta listo.
    /// </summary>
    public void SendSeed(int seed)
    {
        SendRaw("{\"type\":\"seed\",\"value\":" + seed + "}");
        Debug.Log("[HOST] Semilla enviada: " + seed);
    }

    #endregion

    #region CONEXION CLIENTE

    private void StartClient()
    {
        try
        {
            Debug.Log("[CLIENTE] Conectando a " + serverIP + ":" + port + "...");
            _client = new TcpClient();
            _client.Connect(serverIP, port);
            _stream = _client.GetStream();
            isConnected = true;
            Debug.Log("[CLIENTE] Conectado al host!");

            StartReceiving();
        }
        catch (Exception e)
        {
            Debug.LogError("[CLIENTE] Error al conectar: " + e.Message);
        }
    }

    #endregion

    #region RECEPCION DE MENSAJES

    private void StartReceiving()
    {
        _isRunning = true;
        _receiveThread = new Thread(ReceiveLoop);
        _receiveThread.IsBackground = true;
        _receiveThread.Start();
    }

    private void ReceiveLoop()
    {
        byte[] buffer = new byte[1024];

        while (_isRunning && _client != null && _client.Connected)
        {
            try
            {
                int bytes = _stream.Read(buffer, 0, buffer.Length);
                if (bytes > 0)
                {
                    string msg = Encoding.UTF8.GetString(buffer, 0, bytes);
                    lock (_queueLock)
                    {
                        _messageQueue.Enqueue(msg);
                    }
                }
            }
            catch (Exception e)
            {
                if (_isRunning)
                    Debug.LogError("[RED] Error recibiendo: " + e.Message);
                break;
            }
        }
    }

    #endregion

    #region ENVIO DE MENSAJES

    /// <summary>
    /// Envio interno desde cualquier hilo.
    /// </summary>
    private void SendRaw(string message)
    {
        if (_stream == null || !isConnected) return;
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            _stream.Write(data, 0, data.Length);
            _stream.Flush();
        }
        catch (Exception e)
        {
            Debug.LogError("[RED] Error enviando: " + e.Message);
        }
    }

    /// <summary>
    /// Envia la posicion del jugador local al rival.
    /// Equivalente a photonView.RPC() del ejercicio de Photon.
    /// </summary>
    public void SendPosition(Vector3 pos)
    {
        string msg = "{\"type\":\"pos\",\"x\":" + pos.x.ToString("F2") +
                     ",\"y\":" + pos.y.ToString("F2") +
                     ",\"z\":" + pos.z.ToString("F2") + "}";
        SendRaw(msg);
    }

    /// <summary>
    /// Envia el estado del jugador local (invisibilidad y monedas).
    /// </summary>
    public void SendState(bool invisible, int coins)
    {
        string inv = invisible ? "true" : "false";
        string msg = "{\"type\":\"state\",\"invisible\":" + inv +
                     ",\"coins\":" + coins + "}";
        SendRaw(msg);
    }

    /// <summary>
    /// Envia un evento de fin de partida.
    /// </summary>
    public void SendEvent(string eventName)
    {
        string msg = "{\"type\":\"event\",\"name\":\"" + eventName + "\"}";
        SendRaw(msg);
    }

    #endregion

    #region PROCESADO DE MENSAJES

    /// <summary>
    /// Procesa un mensaje en el hilo principal de Unity (llamado desde Update).
    /// Equivalente a CambiarValorEnRed() del ejercicio de variables con Photon.
    /// </summary>
    private void ProcessMessage(string msg)
    {
        // Comprobamos que los singletons esten listos antes de usarlos
        // Si no lo estan, volvemos a encolar el mensaje para el siguiente frame
        if (GameState.Instance == null || MazeGenerator.Instance == null)
        {
            lock (_queueLock) { _messageQueue.Enqueue(msg); }
            return;
        }

        try
        {
            if (msg.Contains("\"type\":\"seed\""))
            {
                // El host no procesa su propia semilla, solo el cliente la necesita
                if (!isHost)
                {
                    int seed = ExtractInt(msg, "value");
                    Debug.Log("[CLIENTE] Semilla recibida: " + seed);
                    MazeGenerator.Instance.seed = seed;
                    GameState.Instance.OnSeedReceived();
                }
            }
            else if (msg.Contains("\"type\":\"pos\""))
            {
                float x = ExtractFloat(msg, "x");
                float y = ExtractFloat(msg, "y");
                float z = ExtractFloat(msg, "z");
                GameState.Instance.UpdateRivalPosition(new Vector3(x, y, z));
            }
            else if (msg.Contains("\"type\":\"state\""))
            {
                bool invisible = msg.Contains("\"invisible\":true");
                int coins = ExtractInt(msg, "coins");
                GameState.Instance.UpdateRivalState(invisible, coins);
            }
            else if (msg.Contains("\"type\":\"event\""))
            {
                if (msg.Contains("\"name\":\"ghostWins\""))
                    GameState.Instance.GhostWins();
                else if (msg.Contains("\"name\":\"pacmanWins\""))
                    GameState.Instance.PacManWins();
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[RED] Error procesando mensaje: " + e.Message + " | " + msg);
        }
    }

    #endregion

    #region DESCONEXION

    public void Disconnect()
    {
        _isRunning = false;
        try { _receiveThread?.Interrupt(); } catch { }
        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }
        try { _server?.Stop(); } catch { }
        isConnected = false;
        Debug.Log("[RED] Desconectado.");
    }

    #endregion

    #region PARSEO JSON MANUAL

    private int ExtractInt(string json, string key)
    {
        string search = "\"" + key + "\":";
        int start = json.IndexOf(search, StringComparison.Ordinal) + search.Length;
        int end = json.IndexOfAny(new char[] { ',', '}' }, start);
        return int.Parse(json.Substring(start, end - start).Trim());
    }

    private float ExtractFloat(string json, string key)
    {
        string search = "\"" + key + "\":";
        int start = json.IndexOf(search, StringComparison.Ordinal) + search.Length;
        int end = json.IndexOfAny(new char[] { ',', '}' }, start);
        return float.Parse(
            json.Substring(start, end - start).Trim(),
            System.Globalization.CultureInfo.InvariantCulture
        );
    }

    #endregion
}
