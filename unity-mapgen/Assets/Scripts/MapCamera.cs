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

        private UnityEngine.Camera _camera;

        void Awake()
        {
            _camera = GetComponent<UnityEngine.Camera>();
            _camera.orthographic = true;
        }

        void Start()
        {
            // Center on default map size
            transform.position = new Vector3(960, 540, -10);
            _camera.orthographicSize = 540;
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
