using UnityEngine;

public class PacmanController : MonoBehaviour
{
    public float speed = 5f;
    public float tiltX = -35f;      // la rotacion en X que le diste al prefab
    public float angleOffset = 0f;  // ajusta esto si mira en direccion incorrecta
    private Animator animator;
    private Rigidbody rb;
    private Vector3 direction;

    void Start()
    {
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        direction = new Vector3(h, 0, v);

        if (direction != Vector3.zero)
            animator.speed = 1f;
        else
            animator.speed = 0f;
    }

    void FixedUpdate()
    {
        rb.MovePosition(rb.position + direction * speed * Time.fixedDeltaTime);

        if (direction != Vector3.zero)
        {
            float angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            rb.MoveRotation(Quaternion.Euler(tiltX, angle + angleOffset, 0));
        }
    }
}