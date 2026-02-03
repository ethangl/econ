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
        [SerializeField] private float focusSmoothTime = 0.3f;  // Smooth pan duration for FocusOn

        [Header("Pitch Settings")]
        [SerializeField] private float pitchAtMaxZoom = 75f;  // More top-down when zoomed out
        [SerializeField] private float pitchAtMinZoom = 50f;  // More angled when zoomed in

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
        private float pitchAngle;  // Camera X rotation in degrees (90 = straight down, 75 = tilted)

        // Smooth pan state
        private Vector2 targetPanPosition;
        private Vector2 currentPanPosition;
        private Vector2 panVelocity;
        private bool isFocusPanning;  // True when animating to a FocusOn target

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
            // Add screen noise overlay if not present
            if (GetComponent<Renderer.ScreenNoiseOverlay>() == null)
            {
                gameObject.AddComponent<Renderer.ScreenNoiseOverlay>();
            }

            // Initialize zoom limits based on default map size
            RecalculateZoomLimits();

            // Initialize pitch based on starting zoom (will be updated each frame)
            float zoomT = Mathf.InverseLerp(minZoom, maxZoom, transform.position.y);
            pitchAngle = Mathf.Lerp(pitchAtMinZoom, pitchAtMaxZoom, zoomT);

            // Initialize zoom
            currentZoom = transform.position.y;
            targetZoom = Mathf.Clamp(currentZoom, minZoom, maxZoom);

            // Initialize pan position (accounting for pitch offset)
            currentPanPosition = new Vector2(transform.position.x, transform.position.z);
            targetPanPosition = currentPanPosition;

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

            // Update pitch based on zoom level (more top-down when far, more angled when close)
            float zoomT = Mathf.InverseLerp(minZoom, maxZoom, currentZoom);
            pitchAngle = Mathf.Lerp(pitchAtMinZoom, pitchAtMaxZoom, zoomT);
            transform.rotation = Quaternion.Euler(pitchAngle, 0f, 0f);

            // Smooth pan (when focus panning)
            if (isFocusPanning)
            {
                currentPanPosition = Vector2.SmoothDamp(currentPanPosition, targetPanPosition, ref panVelocity, focusSmoothTime);

                // Stop panning when close enough
                if (Vector2.Distance(currentPanPosition, targetPanPosition) < 0.001f)
                {
                    currentPanPosition = targetPanPosition;
                    isFocusPanning = false;
                }
            }

            // Apply position
            Vector3 pos = transform.position;
            pos.y = currentZoom;
            if (isFocusPanning)
            {
                pos.x = currentPanPosition.x;
                pos.z = currentPanPosition.y;
            }
            transform.position = pos;

            // Apply constraints after setting position (important for focus panning)
            if (isFocusPanning)
            {
                ApplyConstraints();
                // Sync currentPanPosition with constrained position
                currentPanPosition = new Vector2(transform.position.x, transform.position.z);
            }
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

                // Cancel focus panning when user manually pans
                isFocusPanning = false;
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

                // Cancel focus panning when user drags
                isFocusPanning = false;
            }
        }

        private void ApplyConstraints()
        {
            if (!constrainToBounds || cam == null) return;

            Vector3 pos = transform.position;
            float zOffset = GetLookAtOffset(currentZoom);

            // Calculate where the camera is currently looking
            Vector2 lookAt = new Vector2(pos.x, pos.z + zOffset);

            // Constrain the look-at point to keep visible area within map bounds
            lookAt = ClampLookAtToBounds(lookAt);

            // Derive camera position from constrained look-at point
            pos.x = lookAt.x;
            pos.z = lookAt.y - zOffset;

            transform.position = pos;
        }

        /// <summary>
        /// Clamp a look-at point to keep the visible area within map bounds.
        /// </summary>
        private Vector2 ClampLookAtToBounds(Vector2 lookAt)
        {
            if (!constrainToBounds || cam == null) return lookAt;

            float halfMapWidth = mapSize.x * 0.5f;
            float halfMapHeight = mapSize.y * 0.5f;

            // Calculate visible area at current zoom
            float verticalFOV = cam.fieldOfView * Mathf.Deg2Rad;
            float horizontalFOV = 2f * Mathf.Atan(Mathf.Tan(verticalFOV / 2f) * cam.aspect);

            float visibleHalfWidth = currentZoom * Mathf.Tan(horizontalFOV / 2f);
            float visibleHalfHeight = currentZoom * Mathf.Tan(verticalFOV / 2f);

            // Account for pitch - visible area in Z is stretched
            float pitchRad = pitchAngle * Mathf.Deg2Rad;
            float pitchStretch = 1f / Mathf.Sin(pitchRad);
            visibleHalfHeight *= pitchStretch;

            // Constrain so visible area stays within map bounds
            float maxOffsetX = Mathf.Max(0f, halfMapWidth - visibleHalfWidth);
            float maxOffsetZ = Mathf.Max(0f, halfMapHeight - visibleHalfHeight);

            lookAt.x = Mathf.Clamp(lookAt.x, -maxOffsetX, maxOffsetX);
            lookAt.y = Mathf.Clamp(lookAt.y, -maxOffsetZ, maxOffsetZ);

            return lookAt;
        }

        /// <summary>
        /// Calculate the Z offset from camera position to the point it's looking at on the ground.
        /// For a tilted camera, the look-at point is in front of (positive Z from) the camera.
        /// </summary>
        private float GetLookAtOffset(float height)
        {
            // offset = height / tan(pitchAngle)
            // At 90° (straight down): tan(90°) = infinity, offset = 0
            // At 75°: tan(75°) ≈ 3.73, offset ≈ 0.268 * height
            float pitchRad = pitchAngle * Mathf.Deg2Rad;
            if (pitchAngle >= 89.9f) return 0f;  // Straight down, no offset
            return height / Mathf.Tan(pitchRad);
        }

        /// <summary>
        /// Center the camera on the map (instant, no animation).
        /// Accounts for camera pitch - positions camera so it looks at map center.
        /// </summary>
        public void CenterOnMap()
        {
            // To look at (0, 0, 0), camera needs to be offset backward in Z
            float zOffset = GetLookAtOffset(currentZoom);
            transform.position = new Vector3(0, currentZoom, -zOffset);
            currentPanPosition = new Vector2(0, -zOffset);
            targetPanPosition = currentPanPosition;
            isFocusPanning = false;
        }

        /// <summary>
        /// Smoothly move camera to focus on a specific world position.
        /// Accounts for camera pitch - positions camera so it looks at the target.
        /// Respects map bounds - the target is clamped to keep the view within the map.
        /// </summary>
        public void FocusOn(Vector3 worldPosition)
        {
            // Clamp the look-at point to valid bounds
            Vector2 lookAt = new Vector2(worldPosition.x, worldPosition.z);
            lookAt = ClampLookAtToBounds(lookAt);

            // Derive camera position from the look-at point
            float zOffset = GetLookAtOffset(currentZoom);
            Vector2 cameraTarget = new Vector2(lookAt.x, lookAt.y - zOffset);

            targetPanPosition = cameraTarget;
            currentPanPosition = new Vector2(transform.position.x, transform.position.z);
            panVelocity = Vector2.zero;
            isFocusPanning = true;
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
        /// Accounts for camera pitch when centering.
        /// </summary>
        /// <param name="bounds">World-space bounds to fit</param>
        /// <param name="marginPercent">Extra margin as a percentage (0.1 = 10% margin)</param>
        public void FitToBounds(Bounds bounds, float marginPercent = 0.1f)
        {
            if (cam == null) return;

            // Update map bounds and recalculate zoom limits
            mapSize = new Vector2(bounds.size.x, bounds.size.z);
            RecalculateZoomLimits();

            // Calculate required height to fit bounds with the requested margin
            float boundsWidth = bounds.size.x * (1f + marginPercent);
            float boundsHeight = bounds.size.z * (1f + marginPercent);

            float verticalFOV = cam.fieldOfView * Mathf.Deg2Rad;
            float horizontalFOV = 2f * Mathf.Atan(Mathf.Tan(verticalFOV / 2f) * cam.aspect);

            float heightForVertical = (boundsHeight / 2f) / Mathf.Tan(verticalFOV / 2f);
            float heightForHorizontal = (boundsWidth / 2f) / Mathf.Tan(horizontalFOV / 2f);

            float requiredHeight = Mathf.Max(heightForVertical, heightForHorizontal);
            requiredHeight = Mathf.Clamp(requiredHeight, minZoom, maxZoom);

            // To look at bounds center, offset camera backward for pitch
            float zOffset = GetLookAtOffset(requiredHeight);
            Vector3 pos = new Vector3(bounds.center.x, requiredHeight, bounds.center.z - zOffset);

            transform.position = pos;
            currentZoom = requiredHeight;
            targetZoom = requiredHeight;
            currentPanPosition = new Vector2(pos.x, pos.z);
            targetPanPosition = currentPanPosition;
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
