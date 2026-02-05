using UnityEngine;

namespace MapGen
{
    /// <summary>
    /// Simple orthographic camera for viewing the map.
    /// WASD to pan, scroll to zoom.
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class MapCamera : MonoBehaviour
    {
        public float PanSpeed = 500f;
        public float ZoomSpeed = 100f;
        public float MinZoom = 100f;
        public float MaxZoom = 2000f;

        [Tooltip("Padding around map edges (fraction of map size)")]
        public float Padding = 0.02f;

        private UnityEngine.Camera _camera;

        void Awake()
        {
            _camera = GetComponent<UnityEngine.Camera>();
            _camera.orthographic = true;
        }

        void Start()
        {
            FitToMap();
        }

        /// <summary>
        /// Center camera and zoom to fit the entire map.
        /// </summary>
        public void FitToMap()
        {
            var generator = FindObjectOfType<CellMeshGenerator>();
            float width, height;

            if (generator != null)
            {
                width = generator.MapWidth;
                height = generator.MapHeight;
            }
            else
            {
                // Fallback to default
                width = 1920f;
                height = 1080f;
            }

            // Center camera on map
            transform.position = new Vector3(width / 2f, height / 2f, -10f);

            // Size to fit map with padding
            float aspect = _camera.aspect;
            float mapAspect = width / height;

            if (mapAspect > aspect)
            {
                // Map is wider than screen - fit to width
                _camera.orthographicSize = (width / aspect / 2f) * (1f + Padding);
            }
            else
            {
                // Map is taller than screen - fit to height
                _camera.orthographicSize = (height / 2f) * (1f + Padding);
            }

            MaxZoom = _camera.orthographicSize * 2f;
        }

        void Update()
        {
            // Pan with WASD or arrow keys
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");

            if (h != 0 || v != 0)
            {
                float speed = PanSpeed * _camera.orthographicSize / 500f;
                transform.position += new Vector3(h, v, 0) * speed * Time.deltaTime;
            }

            // Zoom with scroll wheel
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0)
            {
                float newSize = _camera.orthographicSize - scroll * ZoomSpeed;
                _camera.orthographicSize = Mathf.Clamp(newSize, MinZoom, MaxZoom);
            }
        }
    }
}
