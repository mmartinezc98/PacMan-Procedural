using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MazeGenerator : MonoBehaviour
{

    #region VARIABLES DE CONFIGURACION

    [Header("Dimensiones del laberinto")]
    [SerializeField] private int _width = 10;
    [SerializeField] private int _height = 10;
    [SerializeField] private float _cellSize = 3f; // debe coincidir con la escala del prefab Cell

    #endregion

    #region PREFABS

    [Header("Prefabs")]
    [SerializeField] private GameObject _cellPrefab;
    [SerializeField] private GameObject _coinPrefab;
    [SerializeField] private GameObject _powerUpPrefab;
    [SerializeField] private GameObject _exitPrefab;

    #endregion

    #region PERLIN NOISE

    [Header("Perlin Noise - distribucion de items")]
    [SerializeField] private float _magnification = 4f;

    private int _xOffset = 0;
    private int _yOffset = 0;

    // Por debajo de este valor Perlin -> moneda
    private const float COIN_THRESHOLD = 0.3f;
    // Por encima de este valor Perlin -> power-up
    private const float POWERUP_THRESHOLD = 0.85f;

    #endregion

    #region SEMILLA

    [Header("Semilla (compartida por red)")]
    public int seed = 0;

    #endregion

    // Singleton: acceso rapido desde otros scripts (igual que MapGenerator.gen en buscaminas)
    public static MazeGenerator Instance;

    // Rejilla de celdas
    private MazeCell[,] _grid;

    // Posiciones de spawn publicas para que GameState las use
    [HideInInspector] public Vector3 pacManSpawnPos;
    [HideInInspector] public Vector3 ghostSpawnPos;

    #region PROPIEDADES PUBLICAS

    public int Width => _width;
    public int Height => _height;
    public float CellSize => _cellSize;

    #endregion

    #region UNITY METHODS

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // El host llama a Start() directamente.
        // El cliente llama a GenerateMap() desde GameState cuando recibe la semilla.
        if (NetworkManager.Instance != null && NetworkManager.Instance.isHost)
        {
            GenerateMap();
        }
    }

    #endregion

    #region GENERACION PRINCIPAL

    /// <summary>
    /// Punto de entrada de la generacion. Llamado por el host en Start()
    /// y por el cliente desde GameState.OnSeedReceived().
    /// </summary>
    public void GenerateMap()
    {
        ApplySeed();
        InitGrid();
        GenerateMaze(0, 0);
        PlaceItemsWithPerlin();
        PlaceExit();
        PlaceCamera();
    }

    #endregion

    #region SEMILLA

    /// <summary>
    /// Aplica la semilla a Random para que la generacion sea determinista.
    /// Mismo resultado en el host y en el cliente con la misma semilla.
    /// </summary>
    public void ApplySeed()
    {
        Random.InitState(seed);
        _xOffset = seed % 1000;
        _yOffset = (seed / 1000) % 1000;
    }

    /// <summary>
    /// Genera una semilla nueva. Solo lo llama el HOST.
    /// Despues NetworkManager la envia al cliente.
    /// </summary>
    public int GenerateNewSeed()
    {
        seed = System.Environment.TickCount;
        return seed;
    }

    #endregion

    #region INICIALIZACION DE LA REJILLA

    private void InitGrid()
    {
        _grid = new MazeCell[_width, _height];

        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                Vector3 pos = new Vector3(x * _cellSize, 0f, y * _cellSize);
                GameObject newCell = Instantiate(_cellPrefab, pos, Quaternion.identity, transform);
                _grid[x, y] = new MazeCell(x, y, newCell);
            }
        }
    }

    #endregion

    #region BACKTRACKING RECURSIVO

    /// <summary>
    /// Genera el laberinto con backtracking recursivo.
    /// Identico al de Maze.cs del ejercicio de laberinto.
    /// </summary>
    private void GenerateMaze(int x, int y)
    {
        _grid[x, y].isVisited = true;

        // Direcciones posibles con sus pares de paredes
        List<(int dx, int dy, string wallA, string wallB)> directions =
            new List<(int, int, string, string)>
        {
            ( 0,  1, "Wall_N", "Wall_S"),
            ( 0, -1, "Wall_S", "Wall_N"),
            ( 1,  0, "Wall_E", "Wall_W"),
            (-1,  0, "Wall_W", "Wall_E")
        };

        // Barajamos aleatoriamente (mismo sistema que en Maze.cs)
        for (int i = 0; i < directions.Count; i++)
        {
            var temp = directions[i];
            int r = Random.Range(i, directions.Count);
            directions[i] = directions[r];
            directions[r] = temp;
        }

        foreach (var dir in directions)
        {
            int nx = x + dir.dx;
            int ny = y + dir.dy;

            if (nx >= 0 && ny >= 0 && nx < _width && ny < _height
                && !_grid[nx, ny].isVisited)
            {
                _grid[x, y].RemoveWall(dir.wallA);
                _grid[nx, ny].RemoveWall(dir.wallB);
                GenerateMaze(nx, ny);
            }
        }
    }

    #endregion

    #region PERLIN NOISE - COLOCACION DE ITEMS

    /// <summary>
    /// Usa Perlin Noise para decidir donde colocar monedas y power-ups.
    /// Mismo sistema que GetIdUsingPerlin() de PerlinOrigen.cs.
    /// </summary>
    private void PlaceItemsWithPerlin()
    {
        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                // Protegemos las 4 esquinas (zonas de spawn)
                if (IsSpawnCell(x, y)) continue;

                float raw = Mathf.PerlinNoise(
                    (x - _xOffset) / _magnification,
                    (y - _yOffset) / _magnification
                );
                float value = Mathf.Clamp01(raw);

                Vector3 itemPos = new Vector3(x * _cellSize, 0.5f, y * _cellSize);

                if (value >= POWERUP_THRESHOLD)
                {
                    Instantiate(_powerUpPrefab, itemPos, Quaternion.identity);
                    _grid[x, y].hasPowerUp = true;
                }
                else if (value <= COIN_THRESHOLD)
                {
                    Instantiate(_coinPrefab, itemPos, Quaternion.identity);
                    _grid[x, y].hasCoin = true;
                }
                // Valores intermedios: pasillo libre
            }
        }
    }

    private bool IsSpawnCell(int x, int y)
    {
        return (x == 0 && y == 0) ||
               (x == _width - 1 && y == _height - 1) ||
               (x == 0 && y == _height - 1) ||
               (x == _width - 1 && y == 0);
    }

    #endregion

    #region SALIDA Y CAMARA

    private void PlaceExit()
    {
        // Salida en el centro del mapa
        Vector3 exitPos = new Vector3((_width / 2) * _cellSize, 0.1f, (_height / 2) * _cellSize);
        Instantiate(_exitPrefab, exitPos, Quaternion.identity);

        // Guardamos posiciones de spawn para los jugadores
        pacManSpawnPos = new Vector3(0f, 0.5f, 0f);
        ghostSpawnPos = new Vector3((_width - 1) * _cellSize, 0.5f, (_height - 1) * _cellSize);
    }

    /// <summary>
    /// Posiciona la camara centrada sobre el mapa con vista cenital.
    /// Igual que PlaceCamera() en Maze.cs.
    /// </summary>
    private void PlaceCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float centroX = (_width * _cellSize) / 2f - (_cellSize / 2f);
        float centroZ = (_height * _cellSize) / 2f - (_cellSize / 2f);
        float altura = Mathf.Max(_width, _height) * _cellSize * 1.2f;

        cam.transform.position = new Vector3(centroX, altura, centroZ);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    #endregion
}