using UnityEngine;
using UnityEngine.UIElements;

namespace EconSim.UI
{
    /// <summary>
    /// UI Toolkit controller for the startup screen.
    /// Allows user to choose between loading an Azgaar map or generating a new one.
    /// </summary>
    public class StartupScreenPanel : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private StyleSheet _styleSheet;

        private VisualElement _overlay;
        private Button _loadMapButton;
        private Button _generateButton;
        private Label _statusLabel;

        private bool _isLoading;

        private void Start()
        {
            SetupUI();
        }

        private void OnEnable()
        {
            EconSim.Core.GameManager.OnMapReady += OnMapReady;
        }

        private void OnDisable()
        {
            EconSim.Core.GameManager.OnMapReady -= OnMapReady;
        }

        private void SetupUI()
        {
            if (_uiDocument == null)
            {
                _uiDocument = GetComponent<UIDocument>();
            }

            if (_uiDocument == null)
            {
                Debug.LogError("StartupScreenPanel: No UIDocument found");
                return;
            }

            var root = _uiDocument.rootVisualElement;

            // Apply stylesheet if assigned
            if (_styleSheet != null)
            {
                root.styleSheets.Add(_styleSheet);
            }

            // Query elements
            _overlay = root.Q<VisualElement>("startup-overlay");
            _loadMapButton = root.Q<Button>("load-map-button");
            _generateButton = root.Q<Button>("generate-button");
            _statusLabel = root.Q<Label>("startup-status");

            // Wire up buttons
            _loadMapButton?.RegisterCallback<ClickEvent>(evt => OnLoadMapClicked());
            _generateButton?.RegisterCallback<ClickEvent>(evt => OnGenerateClicked());
        }

        private void OnLoadMapClicked()
        {
            if (_isLoading) return;

            _isLoading = true;
            SetStatus("Loading map...");
            DisableButtons();

            // Trigger map loading
            var gameManager = EconSim.Core.GameManager.Instance;
            if (gameManager != null)
            {
                gameManager.LoadMapFromStartup();
            }
            else
            {
                SetStatus("Error: GameManager not found");
                _isLoading = false;
                EnableButtons();
            }
        }

        private void OnGenerateClicked()
        {
            if (_isLoading) return;

            SetStatus("Map generation coming soon!");
        }

        private void OnMapReady()
        {
            Hide();
        }

        private void Hide()
        {
            if (_overlay != null)
            {
                _overlay.style.display = DisplayStyle.None;
            }
        }

        private void SetStatus(string message)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = message;
            }
        }

        private void DisableButtons()
        {
            if (_loadMapButton != null)
            {
                _loadMapButton.SetEnabled(false);
                _loadMapButton.AddToClassList("startup-button-disabled");
            }
            if (_generateButton != null)
            {
                _generateButton.SetEnabled(false);
            }
        }

        private void EnableButtons()
        {
            if (_loadMapButton != null)
            {
                _loadMapButton.SetEnabled(true);
                _loadMapButton.RemoveFromClassList("startup-button-disabled");
            }
        }
    }
}
