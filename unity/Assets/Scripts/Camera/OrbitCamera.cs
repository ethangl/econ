using UnityEngine;

namespace EconSim.Camera
{
    /// <summary>
    /// Orbits around a target point. Left-drag to rotate, scroll to zoom.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        private Vector3 target = Vector3.zero;
        private float distance = 20000f;
        private float azimuth = 0f;
        private float elevation = 20f;
        private float minDistance = 1f;
        private float maxDistance = 50000f;
        private float rotationSensitivity = 0.3f;
        private float zoomFactor = 0.1f;

        private bool isDragging;

        public void Configure(Vector3 target, float radius)
        {
            this.target = target;
            minDistance = radius * 1.2f;
            maxDistance = radius * 5f;
            distance = radius * 2.5f;

            // Set clip planes for the sphere scale
            var cam = GetComponent<UnityEngine.Camera>();
            if (cam != null)
            {
                cam.nearClipPlane = radius * 0.01f;
                cam.farClipPlane = radius * 10f;
            }

            ApplyTransform();
        }

        private void LateUpdate()
        {
            HandleInput();
            ApplyTransform();
        }

        private void HandleInput()
        {
            // Left-click drag: rotate
            if (Input.GetMouseButtonDown(0))
                isDragging = true;
            if (Input.GetMouseButtonUp(0))
                isDragging = false;

            if (isDragging)
            {
                azimuth += Input.GetAxis("Mouse X") * rotationSensitivity * 3f;
                elevation -= Input.GetAxis("Mouse Y") * rotationSensitivity * 3f;
                elevation = Mathf.Clamp(elevation, -89f, 89f);
            }

            // Scroll: zoom
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                distance *= 1f - scroll * zoomFactor * 10f;
                distance = Mathf.Clamp(distance, minDistance, maxDistance);
            }
        }

        private void ApplyTransform()
        {
            float azRad = azimuth * Mathf.Deg2Rad;
            float elRad = elevation * Mathf.Deg2Rad;

            float cosEl = Mathf.Cos(elRad);
            Vector3 offset = new Vector3(
                cosEl * Mathf.Sin(azRad),
                Mathf.Sin(elRad),
                cosEl * Mathf.Cos(azRad)
            ) * distance;

            transform.position = target + offset;
            transform.LookAt(target);
        }
    }
}
