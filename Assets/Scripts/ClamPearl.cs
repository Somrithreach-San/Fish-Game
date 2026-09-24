using UnityEngine;

/// <summary>
/// Trigger component attached to the pearl inside the clam.
/// Detects when the player fish swims close to the pearl while the clam is open.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ClamPearl : MonoBehaviour
{
    [Tooltip("Reference to the parent Clam controller")]
    [SerializeField] private Clam clam;

    private Collider2D triggerCollider;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider2D>();
        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
        }

        if (clam == null)
        {
            clam = GetComponentInParent<Clam>();
        }
    }

    public void SetTriggerActive(bool active)
    {
        if (triggerCollider == null) triggerCollider = GetComponent<Collider2D>();
        if (triggerCollider != null)
        {
            triggerCollider.enabled = active;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryEatPearl(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryEatPearl(other);
    }

    private void TryEatPearl(Collider2D other)
    {
        if (clam == null || !clam.CanEatPearl) return;

        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null) player = other.GetComponentInParent<PlayerController>();

        if (player != null)
        {
            clam.OnPearlEaten(player);
        }
    }
}
