using UnityEngine;

namespace EconSim.Camera
{
    /// <summary>
    /// Camera controller for the map view.
    /// Supports pan (drag and keyboard), zoom (scroll wheel).
    /// </summary>
    public class MapCamera : MonoBehaviour
    {
        // Zoom settings
        private float minZoomFraction = 0.15f;  // Min zoom as fraction of max
        private float absoluteMinZoom = 0.5f;   // Never zoom closer than this
        private float zoomSpeed = 5f;
        private float zoomSmoothTime = 0.5f;    // Used for manual scroll zoom
        private float minZoom = 0.5f;   // Calculated from map size
        private float maxZoom = 12f;    // Calculated from map size

        // Pan settings
        private float panSpeed = 12f;
        private float dragPanSpeed = 0.333333f;

        // Focus animation settings (dynamic duration based on movement)
        private float minFocusDuration = 0.25f;
        private float maxFocusDuration = 0.7f;
        private float activeFocusSmoothTime = 0.5f;  // Current animation duration

        // Pitch settings
        private float pitchAtMaxZoom = 75f;  // More top-down when zoomed out
        private float pitchAtMinZoom = 50f;  // More angled when zoomed in

        // Bounds (set by FitToBounds at runtime)
        private bool constrainToBounds = false;
        private Vector2 mapSize = Vector2.zero;
        private Vector2 mapCenter = Vector2.zero;

        // Input
        private float dragStartThresholdPixels = 3f;
        private bool invertPan = false;

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
        private Vector3 primaryMouseDownPosition;
        private bool isMiddleMouseDragging;
        private bool isPrimaryPointerDown;
        private bool isPrimaryDragging;
        private bool suppressSelectionOnPrimaryRelease;

        /// <summary>
        /// True when the camera is in panning mode (mouse button held).
        /// Other systems should not process clicks during this time.
        /// </summary>
        public bool IsPanningMode => isMiddleMouseDragging || isPrimaryPointerDown;

        /// <summary>
        /// Returns whether the last primary-button release ended a drag gesture.
        /// Clears the latched suppression flag after reading.
        /// </summary>
        public bool ConsumeSelectionReleaseSuppression()
        {
            bool suppress = suppressSelectionOnPrimaryRelease
                || (isPrimaryDragging && Input.GetMouseButtonUp(0));
            suppressSelectionOnPrimaryRelease = false;
            return suppress;
        }

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

        }

        private void Update()
        {
            HandleZoom();
            HandlePan();
            ApplyConstraints();
        }

        private void LateUpdate()
        {
            // Smooth zoom - use dynamic duration when focus panning, fixed duration for manual zoom
            float zoomDuration = isFocusPanning ? activeFocusSmoothTime : zoomSmoothTime;
            currentZoom = Mathf.SmoothDamp(currentZoom, targetZoom, ref zoomVelocity, zoomDuration);

            // Update pitch based on zoom level (more top-down when far, more angled when close)
            float zoomT = Mathf.InverseLerp(minZoom, maxZoom, currentZoom);
            pitchAngle = Mathf.Lerp(pitchAtMinZoom, pitchAtMaxZoom, zoomT);
            transform.rotation = Quaternion.Euler(pitchAngle, 0f, 0f);

            // Smooth pan (when focus panning)
            if (isFocusPanning)
            {
                currentPanPosition = Vector2.SmoothDamp(currentPanPosition, targetPanPosition, ref panVelocity, activeFocusSmoothTime);

                // Stop panning when close enough (and zoom close enough)
                bool panDone = Vector2.Distance(currentPanPosition, targetPanPosition) < 0.001f;
                bool zoomDone = Mathf.Abs(currentZoom - targetZoom) < 0.001f;
                if (panDone && zoomDone)
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

            if (Input.GetMouseButtonDown(2))
            {
                isMiddleMouseDragging = true;
                lastMousePosition = Input.mousePosition;
            }
            if (Input.GetMouseButtonUp(2))
            {
                isMiddleMouseDragging = false;
            }

            if (Input.GetMouseButtonDown(0))
            {
                isPrimaryPointerDown = true;
                isPrimaryDragging = false;
                primaryMouseDownPosition = Input.mousePosition;
                lastMousePosition = Input.mousePosition;
            }
            if (Input.GetMouseButtonUp(0))
            {
                suppressSelectionOnPrimaryRelease = isPrimaryDragging;
                isPrimaryPointerDown = false;
                isPrimaryDragging = false;
            }

            if (isPrimaryPointerDown && !isPrimaryDragging)
            {
                Vector3 totalPrimaryDelta = Input.mousePosition - primaryMouseDownPosition;
                if (totalPrimaryDelta.sqrMagnitude >= dragStartThresholdPixels * dragStartThresholdPixels)
                {
                    isPrimaryDragging = true;
                }
            }

            bool isDragging = (isMiddleMouseDragging && Input.GetMouseButton(2))
                || (isPrimaryDragging && Input.GetMouseButton(0));

            if (isDragging)
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
            else if (isPrimaryPointerDown || isMiddleMouseDragging)
            {
                // Keep frame-to-frame drag delta stable while waiting for drag threshold.
                lastMousePosition = Input.mousePosition;
            }
        }

        private void ApplyConstraints()
        {
            // Skip constraints if bounds haven't been set yet (FitToBounds not called)
            if (!constrainToBounds || cam == null || mapSize == Vector2.zero) return;

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
            // Skip if bounds haven't been set yet
            if (!constrainToBounds || cam == null || mapSize == Vector2.zero) return lookAt;

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

            // Constrain so visible area stays within map bounds (centered on mapCenter)
            float maxOffsetX = Mathf.Max(0f, halfMapWidth - visibleHalfWidth);
            float maxOffsetZ = Mathf.Max(0f, halfMapHeight - visibleHalfHeight);

            lookAt.x = Mathf.Clamp(lookAt.x, mapCenter.x - maxOffsetX, mapCenter.x + maxOffsetX);
            lookAt.y = Mathf.Clamp(lookAt.y, mapCenter.y - maxOffsetZ, mapCenter.y + maxOffsetZ);

            return lookAt;
        }

        /// <summary>
        /// Calculate the pitch angle for a given zoom level.
        /// </summary>
        private float GetPitchForZoom(float zoom)
        {
            float zoomT = Mathf.InverseLerp(minZoom, maxZoom, zoom);
            return Mathf.Lerp(pitchAtMinZoom, pitchAtMaxZoom, zoomT);
        }

        /// <summary>
        /// Calculate the Z offset from camera position to the point it's looking at on the ground.
        /// For a tilted camera, the look-at point is in front of (positive Z from) the camera.
        /// </summary>
        private float GetLookAtOffset(float height)
        {
            // Calculate pitch for this zoom level (not current pitch)
            float pitch = GetPitchForZoom(height);
            // offset = height / tan(pitch)
            // At 90° (straight down): tan(90°) = infinity, offset = 0
            // At 75°: tan(75°) ≈ 3.73, offset ≈ 0.268 * height
            float pitchRad = pitch * Mathf.Deg2Rad;
            if (pitch >= 89.9f) return 0f;  // Straight down, no offset
            return height / Mathf.Tan(pitchRad);
        }

        /// <summary>
        /// Center the camera on the map (instant, no animation).
        /// Accounts for camera pitch - positions camera so it looks at map center.
        /// </summary>
        public void CenterOnMap()
        {
            // To look at map center, camera needs to be offset backward in Z
            float zOffset = GetLookAtOffset(currentZoom);
            transform.position = new Vector3(mapCenter.x, currentZoom, mapCenter.y - zOffset);
            currentPanPosition = new Vector2(mapCenter.x, mapCenter.y - zOffset);
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

            currentPanPosition = new Vector2(transform.position.x, transform.position.z);

            // Calculate animation duration based on pan distance
            float panDistance = Vector2.Distance(currentPanPosition, cameraTarget);
            float viewSize = currentZoom * 2f;
            float significance = Mathf.Clamp01(panDistance / viewSize);
            activeFocusSmoothTime = Mathf.Lerp(minFocusDuration, maxFocusDuration, significance);

            targetPanPosition = cameraTarget;
            panVelocity = Vector2.zero;
            isFocusPanning = true;
        }

        /// <summary>
        /// Smoothly move camera to focus on and frame the given bounds.
        /// Zooms to fit the bounds with margin, and pans to center.
        /// Accounts for camera pitch stretch on the Z axis.
        /// </summary>
        /// <param name="bounds">World-space bounds to frame</param>
        /// <param name="marginPercent">Extra margin as a percentage (0.3 = 30% margin)</param>
        public void FocusOnBounds(Bounds bounds, float marginPercent = 0.3f)
        {
            if (cam == null) return;

            float boundsWidth = bounds.size.x * (1f + marginPercent);
            float boundsHeight = bounds.size.z * (1f + marginPercent);

            float verticalFOV = cam.fieldOfView * Mathf.Deg2Rad;
            float horizontalFOV = 2f * Mathf.Atan(Mathf.Tan(verticalFOV / 2f) * cam.aspect);

            // Initial zoom estimate (ignoring pitch)
            float heightForHorizontal = (boundsWidth / 2f) / Mathf.Tan(horizontalFOV / 2f);
            float heightForVertical = (boundsHeight / 2f) / Mathf.Tan(verticalFOV / 2f);
            float requiredZoom = Mathf.Max(heightForVertical, heightForHorizontal);

            // Account for pitch stretch: at shallower angles, Z extent is compressed on screen
            // Iterate once to refine the estimate
            float pitch = GetPitchForZoom(requiredZoom);
            float pitchRad = pitch * Mathf.Deg2Rad;
            float pitchStretch = 1f / Mathf.Sin(pitchRad);  // How much Z is stretched
            float adjustedBoundsHeight = boundsHeight * pitchStretch;

            heightForVertical = (adjustedBoundsHeight / 2f) / Mathf.Tan(verticalFOV / 2f);
            requiredZoom = Mathf.Max(heightForVertical, heightForHorizontal);
            targetZoom = Mathf.Clamp(requiredZoom, minZoom, maxZoom);

            // Pan to center on bounds
            Vector2 lookAt = new Vector2(bounds.center.x, bounds.center.z);
            lookAt = ClampLookAtToBounds(lookAt);

            float zOffset = GetLookAtOffset(targetZoom);
            Vector2 cameraTarget = new Vector2(lookAt.x, lookAt.y - zOffset);

            currentPanPosition = new Vector2(transform.position.x, transform.position.z);

            // Calculate animation duration based on movement significance
            float panDistance = Vector2.Distance(currentPanPosition, cameraTarget);
            float zoomChange = Mathf.Abs(targetZoom - currentZoom);

            // Normalize to 0-1 range: pan relative to current view size, zoom relative to zoom range
            float viewSize = currentZoom * 2f;  // Approximate visible width at current zoom
            float panSignificance = Mathf.Clamp01(panDistance / viewSize);
            float zoomSignificance = Mathf.Clamp01(zoomChange / (maxZoom - minZoom));

            // Use the larger of the two, map to duration range
            float significance = Mathf.Max(panSignificance, zoomSignificance);
            activeFocusSmoothTime = Mathf.Lerp(minFocusDuration, maxFocusDuration, significance);

            targetPanPosition = cameraTarget;
            panVelocity = Vector2.zero;
            zoomVelocity = 0f;
            isFocusPanning = true;
        }

        /// <summary>
        /// Set the map bounds for constraining camera movement.
        /// Also recalculates zoom limits based on map size.
        /// </summary>
        public void SetMapBounds(float width, float height, Vector2? center = null)
        {
            mapSize = new Vector2(width, height);
            mapCenter = center ?? Vector2.zero;
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

            // Skip if bounds haven't been set yet
            if (mapSize == Vector2.zero) return;

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

            // Update map bounds and center, recalculate zoom limits
            mapSize = new Vector2(bounds.size.x, bounds.size.z);
            mapCenter = new Vector2(bounds.center.x, bounds.center.z);
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
            Vector3 center = new Vector3(mapCenter.x, 0, mapCenter.y);
            Vector3 size = new Vector3(mapSize.x, 0.1f, mapSize.y);
            Gizmos.DrawWireCube(center, size);
        }
#endif
    }
}
