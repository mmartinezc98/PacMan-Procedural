using UnityEngine;

public class CameraController : MonoBehaviour
{
    /*
     * CAMARA CON NIEBLA DE GUERRA
     * La escena se oscurece al minimo y cada jugador lleva una luz puntual
     * que revela su zona. Las paredes del laberinto bloquean la luz
     * automaticamente gracias a las sombras de Unity.
     * Una segunda luz tenue (roja) muestra al rival de forma aproximada.
     * Si PacMan activa la invisibilidad, su luz tenue desaparece.
     */

    #region VARIABLES

    [Header("Camara")]
    [SerializeField] private float _cameraHeight = 15f;  // altura sobre el jugador
    [SerializeField] private float _followSpeed = 8f;   // suavidad del seguimiento

    [Header("Luz del jugador (niebla de guerra)")]
    [SerializeField] private Light _playerLight;
    [SerializeField] private float _lightRadius = 8f;
    [SerializeField] private float _lightIntensity = 2f;

    [Header("Luz del rival (muy tenue)")]
    [SerializeField] private Light _rivalLight;
    [SerializeField] private float _rivalLightRadius = 2f;
    [SerializeField] private float _rivalLightIntensity = 0.3f;

    // Se asignan desde GameState cuando se crean los jugadores
    [HideInInspector] public Transform playerTransform;
    [HideInInspector] public Transform rivalTransform;

    private Camera _cam;

    #endregion

    #region UNITY METHODS

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null)
            _cam = Camera.main;
    }

    private void Start()
    {
        SetupFogOfWar();
    }

    private void LateUpdate()
    {
        if (playerTransform == null) return;
        FollowPlayer();
        UpdateLights();
    }

    #endregion

    #region NIEBLA DE GUERRA

    private void SetupFogOfWar()
    {
        // Oscurecemos la escena al minimo para simular niebla de guerra
        RenderSettings.ambientLight = new Color(0.05f, 0.05f, 0.05f);
        RenderSettings.ambientIntensity = 0.05f;

        // Luz del jugador: ilumina su zona con sombras suaves
        if (_playerLight != null)
        {
            _playerLight.type = LightType.Point;
            _playerLight.range = _lightRadius;
            _playerLight.intensity = _lightIntensity;
            _playerLight.color = Color.white;
            _playerLight.shadows = LightShadows.Soft;
        }

        // Luz del rival: muy tenue, solo indica que existe
        if (_rivalLight != null)
        {
            _rivalLight.type = LightType.Point;
            _rivalLight.range = _rivalLightRadius;
            _rivalLight.intensity = _rivalLightIntensity;
            _rivalLight.color = Color.red;
            _rivalLight.shadows = LightShadows.None;
        }
    }

    private void UpdateLights()
    {
        // Luz del jugador: siempre encima de su personaje
        if (_playerLight != null && playerTransform != null)
        {
            _playerLight.transform.position = playerTransform.position + Vector3.up * 3f;
        }

        // Luz del rival: la apagamos si el rival (PacMan) esta invisible
        if (_rivalLight != null && rivalTransform != null)
        {
            PlayerController rival = rivalTransform.GetComponent<PlayerController>();
            bool rivalInvisible = rival != null && rival.isInvisible;

            _rivalLight.enabled = !rivalInvisible;

            if (!rivalInvisible)
                _rivalLight.transform.position = rivalTransform.position + Vector3.up * 3f;
        }
    }

    #endregion

    #region SEGUIMIENTO DE CAMARA

    private void FollowPlayer()
    {
        Vector3 target = new Vector3(
            playerTransform.position.x,
            playerTransform.position.y + _cameraHeight,
            playerTransform.position.z
        );

        _cam.transform.position = Vector3.Lerp(
            _cam.transform.position,
            target,
            _followSpeed * Time.deltaTime
        );

        _cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    #endregion

    #region METODOS PUBLICOS

    public void SetPlayer(Transform player)
    {
        playerTransform = player;
        // Hacemos la luz hija del jugador para que se mueva con el
        if (_playerLight != null)
            _playerLight.transform.SetParent(player);
    }

    public void SetRival(Transform rival)
    {
        rivalTransform = rival;
    }

    /// <summary>
    /// Cambia el radio de vision (por ejemplo al coger un power-up especial)
    /// </summary>
    public void SetVisionRadius(float radius)
    {
        _lightRadius = radius;
        if (_playerLight != null)
            _playerLight.range = radius;
    }

    #endregion
}