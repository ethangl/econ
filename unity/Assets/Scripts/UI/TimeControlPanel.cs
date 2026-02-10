using UnityEngine;
using UnityEngine.UIElements;
using EconSim.Core.Simulation;

namespace EconSim.UI
{
    /// <summary>
    /// UI Toolkit controller for time controls.
    /// Shows date, pause/play, and speed controls.
    /// </summary>
    public class TimeControlPanel : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private StyleSheet _styleSheet;

        private ISimulation _simulation;
        private int _currentSpeedIndex = 1;

        private Label _dateLabel;
        private Label _speedLabel;
        private Button _slowerButton;
        private Button _pauseButton;
        private Button _fasterButton;
        private VisualElement _panel;

        private static readonly float[] SpeedPresets =
        {
            SimulationConfig.Speed.Slow,
            SimulationConfig.Speed.Normal,
            SimulationConfig.Speed.Fast,
            SimulationConfig.Speed.Ultra,
            SimulationConfig.Speed.Hyper
        };

        private static readonly string[] SpeedNames =
        {
            "Slow",
            "Normal",
            "Fast",
            "Ultra",
            "Hyper"
        };

        private void Start()
        {
            if (EconSim.Core.GameManager.IsMapReady)
                StartCoroutine(Initialize());
            else
                EconSim.Core.GameManager.OnMapReady += OnMapReady;
        }

        private void OnDestroy()
        {
            EconSim.Core.GameManager.OnMapReady -= OnMapReady;
        }

        private void OnMapReady()
        {
            EconSim.Core.GameManager.OnMapReady -= OnMapReady;
            StartCoroutine(Initialize());
        }

        private System.Collections.IEnumerator Initialize()
        {
            // Wait a frame to ensure everything is ready
            yield return null;

            var gameManager = EconSim.Core.GameManager.Instance;
            if (gameManager != null)
            {
                _simulation = gameManager.Simulation;

                if (_simulation != null)
                {
                    // Match current speed
                    for (int i = 0; i < SpeedPresets.Length; i++)
                    {
                        if (Mathf.Approximately(_simulation.TimeScale, SpeedPresets[i]))
                        {
                            _currentSpeedIndex = i;
                            break;
                        }
                    }
                }
            }

            SetupUI();
        }

        private void SetupUI()
        {
            if (_uiDocument == null)
            {
                _uiDocument = GetComponent<UIDocument>();
            }

            if (_uiDocument == null)
            {
                Debug.LogError("TimeControlPanel: No UIDocument found");
                return;
            }

            var root = _uiDocument.rootVisualElement;

            // Apply stylesheet if assigned
            if (_styleSheet != null)
            {
                root.styleSheets.Add(_styleSheet);
            }

            // Query elements
            _panel = root.Q<VisualElement>("time-control-panel");
            _dateLabel = root.Q<Label>("date-label");
            _speedLabel = root.Q<Label>("speed-label");
            _slowerButton = root.Q<Button>("slower-button");
            _pauseButton = root.Q<Button>("pause-button");
            _fasterButton = root.Q<Button>("faster-button");

            // Wire up buttons
            _slowerButton?.RegisterCallback<ClickEvent>(evt => DecreaseSpeed());
            _pauseButton?.RegisterCallback<ClickEvent>(evt => TogglePause());
            _fasterButton?.RegisterCallback<ClickEvent>(evt => IncreaseSpeed());

            UpdateSpeedLabel();
        }

        private void Update()
        {
            if (_simulation == null || _dateLabel == null) return;

            // Update date display
            var state = _simulation.GetState();
            int day = state.CurrentDay;
            int year = day / 360 + 1;
            int month = (day % 360) / 30 + 1;
            int dayOfMonth = day % 30 + 1;

            _dateLabel.text = $"Y{year} M{month} D{dayOfMonth}";

            // Update pause button text and panel class
            if (_pauseButton != null)
            {
                _pauseButton.text = _simulation.IsPaused ? ">" : "||";
            }

            if (_panel != null)
            {
                _panel.EnableInClassList("paused", _simulation.IsPaused);
            }
        }

        private void TogglePause()
        {
            if (_simulation != null)
            {
                _simulation.IsPaused = !_simulation.IsPaused;
            }
        }

        private void IncreaseSpeed()
        {
            if (_simulation != null && _currentSpeedIndex < SpeedPresets.Length - 1)
            {
                _currentSpeedIndex++;
                _simulation.TimeScale = SpeedPresets[_currentSpeedIndex];
                UpdateSpeedLabel();
            }
        }

        private void DecreaseSpeed()
        {
            if (_simulation != null && _currentSpeedIndex > 0)
            {
                _currentSpeedIndex--;
                _simulation.TimeScale = SpeedPresets[_currentSpeedIndex];
                UpdateSpeedLabel();
            }
        }

        private void UpdateSpeedLabel()
        {
            if (_speedLabel != null)
            {
                _speedLabel.text = $"{SpeedNames[_currentSpeedIndex]} ({SpeedPresets[_currentSpeedIndex]}x)";
            }
        }
    }
}
