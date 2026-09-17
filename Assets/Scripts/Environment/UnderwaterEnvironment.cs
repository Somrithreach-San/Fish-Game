using System.Collections.Generic;
using UnityEngine;

namespace Rhinotap
{
    public class UnderwaterEnvironment : MonoBehaviour
    {
        [Header("Layers")]
        [SerializeField] private Transform distantLayer;
        [SerializeField] private Transform midgroundLayer;
        [SerializeField] private Transform vegetationLayer;
        [SerializeField] private Transform foregroundLayer;

        [Header("Parallax Settings")]
        [SerializeField] private Vector2 distantParallaxFactor = new Vector2(0.2f, 0.08f);
        [SerializeField] private Vector2 foregroundParallaxFactor = new Vector2(-0.15f, -0.06f);

        [Header("Vegetation Sway")]
        [SerializeField] private bool enableVegetationSway = true;
        [SerializeField] private float swayFrequency = 0.8f;
        [SerializeField] private float swayMaxAngle = 2.0f;
        [SerializeField] private List<Transform> swayingVegetation = new List<Transform>();

        private Vector3 initialCameraPos;
        private Camera targetCamera;
        private Vector3 initialDistantPos;
        private Vector3 initialForegroundPos;
        private List<float> initialRotations = new List<float>();

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }

        private void Start()
        {
            if (targetCamera != null)
            {
                initialCameraPos = targetCamera.transform.position;
            }

            if (distantLayer != null) initialDistantPos = distantLayer.localPosition;
            if (foregroundLayer != null) initialForegroundPos = foregroundLayer.localPosition;

            initialRotations.Clear();
            if (swayingVegetation != null)
            {
                for (int i = 0; i < swayingVegetation.Count; i++)
                {
                    if (swayingVegetation[i] != null)
                    {
                        initialRotations.Add(swayingVegetation[i].localEulerAngles.z);
                    }
                    else
                    {
                        initialRotations.Add(0f);
                    }
                }
            }
        }

        public void InitializeLayers()
        {
            distantLayer = transform.Find("Layer1_Distant");
            midgroundLayer = transform.Find("Layer2_Midground");
            vegetationLayer = transform.Find("Layer3_MidVegetation");
            foregroundLayer = transform.Find("Layer4_ForegroundOcclusion");

            swayingVegetation.Clear();
            if (vegetationLayer != null)
            {
                foreach (Transform child in vegetationLayer)
                {
                    swayingVegetation.Add(child);
                }
            }
        }

        private void LateUpdate()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
                if (targetCamera == null) return;
                initialCameraPos = targetCamera.transform.position;
            }

            Vector3 camDelta = targetCamera.transform.position - initialCameraPos;

            if (distantLayer != null)
            {
                distantLayer.localPosition = initialDistantPos + new Vector3(camDelta.x * distantParallaxFactor.x, camDelta.y * distantParallaxFactor.y, 0f);
            }

            if (foregroundLayer != null)
            {
                foregroundLayer.localPosition = initialForegroundPos + new Vector3(camDelta.x * foregroundParallaxFactor.x, camDelta.y * foregroundParallaxFactor.y, 0f);
            }

            if (enableVegetationSway && swayingVegetation != null)
            {
                float time = Time.time * swayFrequency * Mathf.PI * 2f;
                for (int i = 0; i < swayingVegetation.Count; i++)
                {
                    if (swayingVegetation[i] != null)
                    {
                        float baseRot = (i < initialRotations.Count) ? initialRotations[i] : 0f;
                        float offset = i * 0.7f;
                        float angle = Mathf.Sin(time + offset) * swayMaxAngle;
                        swayingVegetation[i].localEulerAngles = new Vector3(0f, 0f, baseRot + angle);
                    }
                }
            }
        }
    }
}
