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
        [SerializeField] private float minZoomFraction = 0.15f;  // Min zoom as fraction of max (15% = moderate detail)
        [SerializeField] private float absoluteMinZoom = 0.5f;   // Never zoom closer than this (world units)
        [SerializeField] private float zoomSpeed = 5f;
        [SerializeField] private float zoomSmoothTime = 0.1f;

        private float minZoom = 0.5f;   // Calculated from map size
        private float maxZoom = 12f;    // Calculated from map size

        [Header("Pan Settings")]
        [SerializeField] private float panSpeed = 12f;
        [SerializeField] private float dragPanSpeed = 0.333333f;

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
        private bool isSpacebarHeld;

        // Cursor textures (created at runtime)
        private Texture2D openHandCursor;
        private Texture2D closedHandCursor;
        private Vector2 cursorHotspot = new Vector2(8, 8);

        /// <summary>
        /// True when the camera is in panning mode (spacebar held or middle mouse).
        /// Other systems should not process clicks during this time.
        /// </summary>
        public bool IsPanningMode => isSpacebarHeld || isDragging || isSpacebarPanning;

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
            // Initialize zoom limits based on default map size
            RecalculateZoomLimits();

            // Initialize zoom
            currentZoom = transform.position.y;
            targetZoom = Mathf.Clamp(currentZoom, minZoom, maxZoom);

            // Position camera at center of map
            CenterOnMap();

            // Create hand cursors
            CreateHandCursors();
        }

        private void OnDestroy()
        {
            // Clean up cursor textures
            if (openHandCursor != null) Destroy(openHandCursor);
            if (closedHandCursor != null) Destroy(closedHandCursor);
        }

        private void CreateHandCursors()
        {
            // Create simple 16x16 hand cursor textures
            openHandCursor = CreateHandTexture(false);
            closedHandCursor = CreateHandTexture(true);
        }

        private Texture2D CreateHandTexture(bool closed)
        {
            var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;

            // Clear to transparent
            var clear = new Color(0, 0, 0, 0);
            var black = Color.black;
            var white = Color.white;

            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    tex.SetPixel(x, y, clear);

            if (closed)
            {
                // Closed hand (grabbing) - simple fist shape
                // Palm
                for (int y = 2; y <= 7; y++)
                    for (int x = 3; x <= 12; x++)
                        tex.SetPixel(x, y, white);
                // Outline
                for (int x = 3; x <= 12; x++) { tex.SetPixel(x, 1, black); tex.SetPixel(x, 8, black); }
                for (int y = 2; y <= 7; y++) { tex.SetPixel(2, y, black); tex.SetPixel(13, y, black); }
                // Finger bumps on top
                for (int i = 0; i < 4; i++)
                {
                    int x = 4 + i * 2;
                    tex.SetPixel(x, 9, white); tex.SetPixel(x + 1, 9, white);
                    tex.SetPixel(x, 10, black); tex.SetPixel(x + 1, 10, black);
                }
            }
            else
            {
                // Open hand - palm with fingers
                // Palm
                for (int y = 1; y <= 5; y++)
                    for (int x = 3; x <= 12; x++)
                        tex.SetPixel(x, y, white);
                // Outline palm
                for (int x = 3; x <= 12; x++) { tex.SetPixel(x, 0, black); tex.SetPixel(x, 6, black); }
                for (int y = 1; y <= 5; y++) { tex.SetPixel(2, y, black); tex.SetPixel(13, y, black); }
                // Fingers
                for (int i = 0; i < 4; i++)
                {
                    int x = 4 + i * 2;
                    for (int y = 7; y <= 12; y++) { tex.SetPixel(x, y, white); tex.SetPixel(x + 1, y, white); }
                    // Outline
                    tex.SetPixel(x - 1, 7, black); tex.SetPixel(x + 2, 7, black);
                    for (int y = 7; y <= 12; y++) { tex.SetPixel(x - 1, y, black); tex.SetPixel(x + 2, y, black); }
                    tex.SetPixel(x, 13, black); tex.SetPixel(x + 1, 13, black);
                }
            }

            tex.Apply();
            return tex;
        }

        private void UpdateCursor()
        {
            if (isSpacebarPanning || isDragging)
            {
                Cursor.SetCursor(closedHandCursor, cursorHotspot, CursorMode.Auto);
            }
            else if (isSpacebarHeld)
            {
                Cursor.SetCursor(openHandCursor, cursorHotspot, CursorMode.Auto);
            }
            else
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            }
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

            // Track cursor state changes
            bool wasInPanMode = IsPanningMode;

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

            // Track spacebar held state (for cursor and click blocking)
            if (Input.GetKeyDown(spacebarPanKey))
            {
                isSpacebarHeld = true;
            }
            if (Input.GetKeyUp(spacebarPanKey))
            {
                isSpacebarHeld = false;
                isSpacebarPanning = false;
            }

            // Spacebar + left click drag pan (laptop-friendly)
            if (isSpacebarHeld && Input.GetMouseButtonDown(0))
            {
                isSpacebarPanning = true;
                lastMousePosition = Input.mousePosition;
            }

            if (Input.GetMouseButtonUp(0))
            {
                isSpacebarPanning = false;
            }

            // Update cursor when pan mode changes
            if (wasInPanMode != IsPanningMode || (isSpacebarHeld && Input.GetMouseButtonDown(0)) || Input.GetMouseButtonUp(0))
            {
                UpdateCursor();
            }

            if (isDragging || isSpacebarPanning)
            {
                Vector3 delta = Input.mousePosition - lastMousePosition;
                lastMousePosition = Input.mousePosition;

                float panMultiplier = invertPan ? 1f : -1f;

                // Scale by currentZoom directly (map-size independent)
                // Higher zoom = seeing more = same pixel drag moves further in world
                const float pixelToWorld = 0.005f;

                Vector3 move = new Vector3(
                    delta.x * dragPanSpeed * currentZoom * panMultiplier * pixelToWorld,
                    0,
                    delta.y * dragPanSpeed * currentZoom * panMultiplier * pixelToWorld
                );

                transform.position += move;
            }
        }

        private void ApplyConstraints()
        {
            if (!constrainToBounds || cam == null) return;

            Vector3 pos = transform.position;

            float halfMapWidth = mapSize.x * 0.5f;
            float halfMapHeight = mapSize.y * 0.5f;

            // Calculate visible area at current zoom
            float verticalFOV = cam.fieldOfView * Mathf.Deg2Rad;
            float horizontalFOV = 2f * Mathf.Atan(Mathf.Tan(verticalFOV / 2f) * cam.aspect);

            float visibleHalfWidth = currentZoom * Mathf.Tan(horizontalFOV / 2f);
            float visibleHalfHeight = currentZoom * Mathf.Tan(verticalFOV / 2f);

            // Constrain so visible area stays within map bounds
            // At max zoom (whole map visible), this keeps camera centered
            // At min zoom (close up), allows panning to edges
            float maxOffsetX = Mathf.Max(0f, halfMapWidth - visibleHalfWidth);
            float maxOffsetZ = Mathf.Max(0f, halfMapHeight - visibleHalfHeight);

            pos.x = Mathf.Clamp(pos.x, -maxOffsetX, maxOffsetX);
            pos.z = Mathf.Clamp(pos.z, -maxOffsetZ, maxOffsetZ);

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
        /// Also recalculates zoom limits based on map size.
        /// </summary>
        public void SetMapBounds(float width, float height)
        {
            mapSize = new Vector2(width, height);
            RecalculateZoomLimits();
        }

        /// <summary>
        /// Recalculates min/max zoom based on current map size and camera FOV.
        /// maxZoom is set so the entire map just fits on screen.
        /// minZoom is a fraction of maxZoom, but never below absoluteMinZoom.
        /// </summary>
        private void RecalculateZoomLimits()
        {
            if (cam == null)
            {
                cam = GetComponent<UnityEngine.Camera>();
                if (cam == null) cam = UnityEngine.Camera.main;
            }
            if (cam == null) return;

            // Calculate height needed to see the full map (tight fit, no margin)
            float verticalFOV = cam.fieldOfView * Mathf.Deg2Rad;
            float horizontalFOV = 2f * Mathf.Atan(Mathf.Tan(verticalFOV / 2f) * cam.aspect);

            // Height needed to fit vertical extent
            float heightForVertical = (mapSize.y / 2f) / Mathf.Tan(verticalFOV / 2f);
            // Height needed to fit horizontal extent
            float heightForHorizontal = (mapSize.x / 2f) / Mathf.Tan(horizontalFOV / 2f);

            // Use the larger of the two to ensure everything fits
            maxZoom = Mathf.Max(heightForVertical, heightForHorizontal);

            // Min zoom is a fraction of max, but never below absolute minimum
            minZoom = Mathf.Max(maxZoom * minZoomFraction, absoluteMinZoom);

            // Clamp current zoom if it exceeds new limits
            if (targetZoom > maxZoom) targetZoom = maxZoom;
            if (targetZoom < minZoom) targetZoom = minZoom;
        }

        /// <summary>
        /// Set zoom level directly (0-1 normalized).
        /// </summary>
        public void SetZoom(float normalizedZoom)
        {
            targetZoom = Mathf.Lerp(minZoom, maxZoom, normalizedZoom);
        }

        /// <summary>
        /// Fit the camera to show the given bounds with some margin.
        /// Centers on the bounds and sets zoom to show the full area.
        /// Also updates map bounds and zoom limits based on the new bounds.
        /// </summary>
        /// <param name="bounds">World-space bounds to fit</param>
        /// <param name="marginPercent">Extra margin as a percentage (0.1 = 10% margin)</param>
        public void FitToBounds(Bounds bounds, float marginPercent = 0.1f)
        {
            if (cam == null) return;

            // Update map bounds and recalculate zoom limits
            mapSize = new Vector2(bounds.size.x, bounds.size.z);
            RecalculateZoomLimits();

            // Center on the bounds
            Vector3 pos = transform.position;
            pos.x = bounds.center.x;
            pos.z = bounds.center.z;

            // Calculate required height to fit bounds with the requested margin
            float boundsWidth = bounds.size.x * (1f + marginPercent);
            float boundsHeight = bounds.size.z * (1f + marginPercent);

            float verticalFOV = cam.fieldOfView * Mathf.Deg2Rad;
            float horizontalFOV = 2f * Mathf.Atan(Mathf.Tan(verticalFOV / 2f) * cam.aspect);

            float heightForVertical = (boundsHeight / 2f) / Mathf.Tan(verticalFOV / 2f);
            float heightForHorizontal = (boundsWidth / 2f) / Mathf.Tan(horizontalFOV / 2f);

            float requiredHeight = Mathf.Max(heightForVertical, heightForHorizontal);
            requiredHeight = Mathf.Clamp(requiredHeight, minZoom, maxZoom);

            pos.y = requiredHeight;
            transform.position = pos;
            currentZoom = requiredHeight;
            targetZoom = requiredHeight;
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
