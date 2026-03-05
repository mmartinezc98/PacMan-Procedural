using System.Collections;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    #region VARIABLES

    [Header("Configuracion del jugador")]
    public bool isPacMan = true;
    public bool isLocalPlayer = false;

    [Header("Movimiento")]
    public float speed = 5f;

    [Header("Estado del juego")]
    public bool hasFoundExit = false;
    public int coinsCollected = 0;
    public bool isInvisible = false;

    [Header("Power-Up invisibilidad")]
    [SerializeField] private float _invisibilityDuration = 5f;

    // Referencia a la luz de la camara (se asigna desde CameraController)
    [HideInInspector] public Light playerLight;

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

            // Avisamos al GameState para que compruebe si ya se recogieron todas
            GameState.Instance.OnCoinCollected();
        }

        // PacMan recoge power-up -> invisibilidad
        if (isPacMan && other.CompareTag("PowerUp"))
        {
            Destroy(other.gameObject);
            StartCoroutine(ActivateInvisibility());
        }

        // PacMan llega a la salida (solo si esta activa)
        if (isPacMan && other.CompareTag("Exit"))
        {
            if (GameState.Instance.exitActive)
            {
                hasFoundExit = true;
                Debug.Log("PacMan llego a la salida!");
                GameState.Instance.PacManWins();
            }
        }

        // Fantasma atrapa a PacMan
        if (!isPacMan && other.CompareTag("PacMan"))
        {
            PlayerController rival = other.GetComponent<PlayerController>();
            if (rival != null && !rival.isInvisible)
            {
                Debug.Log("Fantasma atrapo a PacMan!");
                GameState.Instance.GhostWins();
            }
        }
    }

    #endregion

    #region POWER-UP INVISIBILIDAD

    private IEnumerator ActivateInvisibility()
    {
        isInvisible = true;

        // Apagamos la luz del jugador para que el fantasma no le vea
        if (playerLight != null)
            playerLight.enabled = false;

        // Hacemos al jugador semitransparente visualmente
        SetTransparency(0.2f);

        Debug.Log("Invisibilidad activada por " + _invisibilityDuration + "s");

        yield return new WaitForSeconds(_invisibilityDuration);

        isInvisible = false;

        // Volvemos a encender la luz
        if (playerLight != null)
            playerLight.enabled = true;

        // Volvemos a la opacidad normal
        SetTransparency(1f);

        Debug.Log("Invisibilidad desactivada");
    }

    /// <summary>
    /// Cambia la transparencia del jugador para indicar visualmente la invisibilidad.
    /// </summary>
    private void SetTransparency(float alpha)
    {
        Renderer rend = GetComponent<Renderer>();
        if (rend == null) return;

        // Necesitamos el modo transparente del shader
        Material mat = rend.material;
        Color color = mat.color;
        color.a = alpha;

        if (alpha < 1f)
        {
            mat.SetFloat("_Mode", 3); // Transparent
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }
        else
        {
            mat.SetFloat("_Mode", 0); // Opaque
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            mat.SetInt("_ZWrite", 1);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = -1;
        }

        mat.color = color;
    }

    #endregion

    #region SINCRONIZACION DE RED

    public void UpdateRemotePosition(Vector3 newPosition)
    {
        if (!isLocalPlayer)
            transform.position = newPosition;
    }

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