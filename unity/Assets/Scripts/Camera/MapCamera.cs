using UnityEngine;

namespace EconSim.Camera
{
    /// <summary>
    /// Camera controller for the map view.
    /// Supports pan (drag and keyboard), zoom (scroll wheel).
    /// </summary>
    public class MapCamera : MonoBehaviour
    {
        [Header("Zoom Settings")]
        [SerializeField] private float minZoom = 2f;
        [SerializeField] private float maxZoom = 50f;
        [SerializeField] private float zoomSpeed = 5f;
        [SerializeField] private float zoomSmoothTime = 0.1f;

        [Header("Pan Settings")]
        [SerializeField] private float panSpeed = 20f;
        [SerializeField] private float dragPanSpeed = 1f;

        [Header("Bounds")]
        [SerializeField] private bool constrainToBounds = true;
        [SerializeField] private Vector2 mapSize = new Vector2(14.4f, 8.1f);  // Default Azgaar map size in world units

        [Header("Input")]
        [SerializeField] private KeyCode panModifierKey = KeyCode.Mouse2;  // Middle mouse button
        [SerializeField] private KeyCode spacebarPanKey = KeyCode.Space;   // Hold to enable drag pan
        [SerializeField] private bool invertPan = false;

        private UnityEngine.Camera cam;
        private float targetZoom;
        private float currentZoom;
        private float zoomVelocity;

        private Vector3 lastMousePosition;
        private bool isDragging;
        private bool isSpacebarPanning;

        private void Awake()
        {
            cam = GetComponent<UnityEngine.Camera>();
            if (cam == null)
            {
                cam = UnityEngine.Camera.main;
            }
        }

        private void Start()
        {
            // Initialize zoom
            currentZoom = transform.position.y;
            targetZoom = currentZoom;

            // Position camera at center of map
            CenterOnMap();
        }

        private void Update()
        {
            HandleZoom();
            HandlePan();
            ApplyConstraints();
        }

        private void LateUpdate()
        {
            // Smooth zoom
            currentZoom = Mathf.SmoothDamp(currentZoom, targetZoom, ref zoomVelocity, zoomSmoothTime);

            // Apply zoom (camera height)
            Vector3 pos = transform.position;
            pos.y = currentZoom;
            transform.position = pos;
        }

        private void HandleZoom()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
            {
                // Zoom toward/away from mouse position
                targetZoom -= scroll * zoomSpeed * (targetZoom / maxZoom + 0.2f);
                targetZoom = Mathf.Clamp(targetZoom, minZoom, maxZoom);
            }
        }

        private void HandlePan()
        {
            // Keyboard pan (WASD or arrow keys)
            Vector3 panDirection = Vector3.zero;

            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
                panDirection.z += 1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
                panDirection.z -= 1;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
                panDirection.x -= 1;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
                panDirection.x += 1;

            if (panDirection.sqrMagnitude > 0.01f)
            {
                panDirection.Normalize();
                float zoomFactor = currentZoom / maxZoom;
                transform.position += panDirection * panSpeed * zoomFactor * Time.deltaTime;
            }

            // Mouse drag pan (middle mouse button)
            if (Input.GetKeyDown(panModifierKey) || Input.GetMouseButtonDown(2))
            {
                isDragging = true;
                lastMousePosition = Input.mousePosition;
            }

            if (Input.GetKeyUp(panModifierKey) || Input.GetMouseButtonUp(2))
            {
                isDragging = false;
            }

            // Spacebar + left click drag pan (laptop-friendly)
            if (Input.GetKey(spacebarPanKey))
            {
                if (Input.GetMouseButtonDown(0))
                {
                    isSpacebarPanning = true;
                    lastMousePosition = Input.mousePosition;
                }
            }

            if (Input.GetMouseButtonUp(0) || Input.GetKeyUp(spacebarPanKey))
            {
                isSpacebarPanning = false;
            }

            if (isDragging || isSpacebarPanning)
            {
                Vector3 delta = Input.mousePosition - lastMousePosition;
                lastMousePosition = Input.mousePosition;

                float zoomFactor = currentZoom / maxZoom;
                float panMultiplier = invertPan ? 1f : -1f;

                Vector3 move = new Vector3(
                    delta.x * dragPanSpeed * zoomFactor * panMultiplier,
                    0,
                    delta.y * dragPanSpeed * zoomFactor * panMultiplier
                );

                transform.position += move * Time.deltaTime * 60f;  // Normalize for frame rate
            }
        }

        private void ApplyConstraints()
        {
            if (!constrainToBounds) return;

            Vector3 pos = transform.position;

            float halfWidth = mapSize.x * 0.5f;
            float halfHeight = mapSize.y * 0.5f;

            // Allow some margin based on zoom level
            float margin = currentZoom * 0.5f;

            pos.x = Mathf.Clamp(pos.x, -halfWidth - margin, halfWidth + margin);
            pos.z = Mathf.Clamp(pos.z, -halfHeight - margin, halfHeight + margin);

            transform.position = pos;
        }

        /// <summary>
        /// Center the camera on the map.
        /// </summary>
        public void CenterOnMap()
        {
            transform.position = new Vector3(0, currentZoom, 0);
        }

        /// <summary>
        /// Move camera to focus on a specific world position.
        /// </summary>
        public void FocusOn(Vector3 worldPosition)
        {
            Vector3 pos = transform.position;
            pos.x = worldPosition.x;
            pos.z = worldPosition.z;
            transform.position = pos;
        }

        /// <summary>
        /// Set the map bounds for constraining camera movement.
        /// </summary>
        public void SetMapBounds(float width, float height)
        {
            mapSize = new Vector2(width, height);
        }

        /// <summary>
        /// Set zoom level directly (0-1 normalized).
        /// </summary>
        public void SetZoom(float normalizedZoom)
        {
            targetZoom = Mathf.Lerp(minZoom, maxZoom, normalizedZoom);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Draw map bounds
            Gizmos.color = Color.yellow;
            Vector3 center = new Vector3(0, 0, 0);
            Vector3 size = new Vector3(mapSize.x, 0.1f, mapSize.y);
            Gizmos.DrawWireCube(center, size);
        }
#endif
    }
}
