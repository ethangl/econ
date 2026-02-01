using UnityEngine;
using EconSim.Core.Simulation;

namespace EconSim.UI
{
    /// <summary>
    /// Simple IMGUI-based HUD for time controls.
    /// Shows day, pause/play, and speed controls.
    /// </summary>
    public class TimeControlHUD : MonoBehaviour
    {
        private ISimulation _simulation;
        private int _currentSpeedIndex = 1; // Start at Normal
        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _dayLabelStyle;
        private GUIStyle _buttonStyle;
        private bool _stylesInitialized;

        private static readonly float[] SpeedPresets = { 0.5f, 1f, 5f };
        private static readonly string[] SpeedNames = { "Slow", "Normal", "Fast" };

        private void Start()
        {
            StartCoroutine(WaitForSimulation());
        }

        private System.Collections.IEnumerator WaitForSimulation()
        {
            yield return null;

            var gameManager = EconSim.Core.GameManager.Instance;
            if (gameManager != null)
            {
                _simulation = gameManager.Simulation;

                if (_simulation != null)
                {
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
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(10, 10, 10, 10)
            };

            _dayLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            if (_simulation == null)
            {
                // Fallback: try to get simulation directly
                var gm = EconSim.Core.GameManager.Instance;
                if (gm != null) _simulation = gm.Simulation;
                if (_simulation == null) return;
            }

            InitStyles();

            float width = 170;
            float height = 90;
            float margin = 10;

            Rect panelRect = new Rect(Screen.width - width - margin, margin, width, height);

            GUI.Box(panelRect, "", _boxStyle);

            GUILayout.BeginArea(new Rect(panelRect.x + 10, panelRect.y + 8, width - 20, height - 16));

            // Day label
            var state = _simulation.GetState();
            int day = state.CurrentDay;
            int year = day / 360 + 1;
            int month = (day % 360) / 30 + 1;
            int dayOfMonth = day % 30 + 1;

            GUILayout.Label($"Y{year} M{month} D{dayOfMonth}", _dayLabelStyle);

            GUILayout.Space(5);

            // Speed controls
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("-", _buttonStyle, GUILayout.Width(35), GUILayout.Height(30)))
            {
                DecreaseSpeed();
            }

            string pauseText = _simulation.IsPaused ? ">" : "||";
            if (GUILayout.Button(pauseText, _buttonStyle, GUILayout.Height(30)))
            {
                _simulation.IsPaused = !_simulation.IsPaused;
            }

            if (GUILayout.Button("+", _buttonStyle, GUILayout.Width(35), GUILayout.Height(30)))
            {
                IncreaseSpeed();
            }

            GUILayout.EndHorizontal();

            // Speed label
            GUILayout.Label($"{SpeedNames[_currentSpeedIndex]} ({SpeedPresets[_currentSpeedIndex]}x)", _labelStyle);

            GUILayout.EndArea();
        }

        private void IncreaseSpeed()
        {
            if (_currentSpeedIndex < SpeedPresets.Length - 1)
            {
                _currentSpeedIndex++;
                _simulation.TimeScale = SpeedPresets[_currentSpeedIndex];
            }
        }

        private void DecreaseSpeed()
        {
            if (_currentSpeedIndex > 0)
            {
                _currentSpeedIndex--;
                _simulation.TimeScale = SpeedPresets[_currentSpeedIndex];
            }
        }
    }
}
