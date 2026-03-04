using System.Collections;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    #region VARIABLES

    [Header("Configuracion del jugador")]
    public bool isPacMan = true;   // true = PacMan | false = Fantasma
    public bool isLocalPlayer = false;  // true = lo controla esta maquina

    [Header("Movimiento")]
    public float speed = 5f;

    [Header("Estado del juego")]
    public bool hasFoundExit = false;
    public int coinsCollected = 0;
    public bool isInvisible = false;

    [Header("Power-Up invisibilidad")]
    [SerializeField] private float _invisibilityDuration = 5f;
    [SerializeField] private GameObject _invisibilityIndicator; // efecto visual (opcional)

    private Rigidbody _rb;

    #endregion

    #region UNITY METHODS

    private void Start()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb != null)
        {
            _rb.freezeRotation = true;
            _rb.constraints = RigidbodyConstraints.FreezePositionY
                               | RigidbodyConstraints.FreezeRotation;
        }

        Debug.Log("Jugador iniciado como: " + (isPacMan ? "PACMAN" : "FANTASMA"));
    }

    private void FixedUpdate()
    {
        // Solo movemos si es el jugador local (el remoto se mueve por red)
        if (!isLocalPlayer) return;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 movement = new Vector3(h, 0f, v).normalized;
        _rb.MovePosition(_rb.position + movement * speed * Time.fixedDeltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isLocalPlayer) return;

        // PacMan recoge moneda
        if (isPacMan && other.CompareTag("Coin"))
        {
            coinsCollected++;
            Destroy(other.gameObject);
            Debug.Log("Moneda recogida: " + coinsCollected);
        }

        // PacMan recoge power-up de invisibilidad
        if (isPacMan && other.CompareTag("PowerUp"))
        {
            Destroy(other.gameObject);
            StartCoroutine(ActivateInvisibility());
        }

        // PacMan llega a la salida
        if (isPacMan && other.CompareTag("Exit"))
        {
            hasFoundExit = true;
            Debug.Log("PacMan llego a la salida!");
        }

        // Fantasma atrapa a PacMan
        if (!isPacMan && other.CompareTag("PacMan"))
        {
            PlayerController rival = other.GetComponent<PlayerController>();
            if (rival != null && !rival.isInvisible)
            {
                Debug.Log("Fantasma atrapo a PacMan!");
                if (GameState.Instance != null)
                    GameState.Instance.GhostWins();
            }
        }
    }

    #endregion

    #region POWER-UP

    private IEnumerator ActivateInvisibility()
    {
        isInvisible = true;

        if (_invisibilityIndicator != null)
            _invisibilityIndicator.SetActive(true);

        Debug.Log("Power-Up activado: invisibilidad por " + _invisibilityDuration + "s");

        yield return new WaitForSeconds(_invisibilityDuration);

        isInvisible = false;

        if (_invisibilityIndicator != null)
            _invisibilityIndicator.SetActive(false);
    }

    #endregion

    #region SINCRONIZACION DE RED

    /// <summary>
    /// Actualiza la posicion del jugador remoto con datos recibidos por red.
    /// Equivalente a CambiarValorEnRed() del ejercicio de variables con Photon.
    /// </summary>
    public void UpdateRemotePosition(Vector3 newPosition)
    {
        if (!isLocalPlayer)
            transform.position = newPosition;
    }

    /// <summary>
    /// Actualiza el estado del jugador remoto (invisibilidad, monedas).
    /// </summary>
    public void UpdateRemoteState(bool invisible, int coins)
    {
        if (!isLocalPlayer)
        {
            isInvisible = invisible;
            coinsCollected = coins;
        }
    }

    #endregion
}
