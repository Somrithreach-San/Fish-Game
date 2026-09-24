using UnityEngine;

namespace Rhinotap.UI
{
    /// <summary>
    /// Smoothly rotates the Shiny_Light additive sunburst aura behind active UI elements.
    /// </summary>
    public class ShinyLightRotator : MonoBehaviour
    {
        [Tooltip("Rotation speed in degrees per second (negative for clockwise)")]
        [SerializeField] private float rotationSpeed = -28.0f;

        private void Awake()
        {
            transform.localScale = Vector3.one;
        }

        private void OnEnable()
        {
            transform.localScale = Vector3.one;
        }

        private void Update()
        {
            // Continuous smooth rotation independent of time scale
            transform.Rotate(0f, 0f, rotationSpeed * Time.unscaledDeltaTime);
        }
    }
}
