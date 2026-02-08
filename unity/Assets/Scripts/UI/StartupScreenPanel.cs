using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using MapGen.Core;

namespace EconSim.UI
{
    /// <summary>
    /// UI Toolkit controller for the startup screen.
    /// Allows the user to generate a new map with template and cell count options.
    /// </summary>
    public class StartupScreenPanel : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private StyleSheet _styleSheet;

        private VisualElement _overlay;
        private Button _generateButton;
        private Label _statusLabel;
        private DropdownField _templateDropdown;
        private IntegerField _seedField;
        private IntegerField _cellCountField;
        private FloatField _aspectRatioField;
        private bool _isLoading;

        private static readonly List<string> TemplateNames =
            Enum.GetNames(typeof(HeightmapTemplateType)).ToList();

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
            _generateButton = root.Q<Button>("generate-button");
            _statusLabel = root.Q<Label>("startup-status");
            _templateDropdown = root.Q<DropdownField>("template-dropdown");
            _seedField = root.Q<IntegerField>("seed-field");
            _cellCountField = root.Q<IntegerField>("cellcount-field");
            _aspectRatioField = root.Q<FloatField>("aspect-ratio-field");
            // Setup template dropdown
            if (_templateDropdown != null)
            {
                _templateDropdown.choices = TemplateNames;
                _templateDropdown.index = TemplateNames.IndexOf(nameof(HeightmapTemplateType.Continents));
            }

            // Wire up buttons
            _generateButton?.RegisterCallback<ClickEvent>(evt => OnGenerateClicked());
        }

        private void OnGenerateClicked()
        {
            if (_isLoading) return;

            _isLoading = true;
            SetStatus("Generating map...");
            DisableButtons();

            var gameManager = EconSim.Core.GameManager.Instance;
            if (gameManager != null)
            {
                var config = new MapGenConfig
                {
                    Seed = _seedField?.value ?? 12345,
                    CellCount = _cellCountField?.value ?? 100000,
                    AspectRatio = _aspectRatioField?.value ?? 1.5f,
                };

                if (_templateDropdown != null && _templateDropdown.index >= 0)
                {
                    config.Template = (HeightmapTemplateType)_templateDropdown.index;
                }

                gameManager.GenerateMap(config);
            }
            else
            {
                SetStatus("Error: GameManager not found");
                _isLoading = false;
                _generateButton?.SetEnabled(true);
            }
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
            if (_generateButton != null)
            {
                _generateButton.SetEnabled(false);
            }
        }
    }
}
