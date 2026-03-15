using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using MapGen.Core;
using WorldGen.Core;

namespace EconSim.UI
{
    /// <summary>
    /// UI Toolkit controller for the startup screen.
    /// Two phases: generation fields (initial) and site review (after globe gen).
    /// </summary>
    public class StartupScreenPanel : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private StyleSheet _styleSheet;

        private VisualElement _overlay;
        private Button _generateButton;
        // _loadLastMapButton removed — Generate New auto-detects cache hits
        private Button _generateGlobeButton;
        private Label _statusLabel;
        private DropdownField _templateDropdown;
        private IntegerField _seedField;
        private IntegerField _cellCountField;
        private FloatField _aspectRatioField;
        private FloatField _latitudeField;
        private bool _isLoading;

        // Site review mode elements
        private VisualElement _generationFields;
        private VisualElement _siteReviewFields;
        private Label _siteTypeLabel;
        private Label _siteLatLabel;
        private Label _siteLngLabel;
        private Label _siteTemplateLabel;
        private Label _siteCounterLabel;
        private Button _sitePrevButton;
        private Button _siteNextButton;
        private Button _generateFromSiteButton;
        private Button _regenerateGlobeButton;

        /// <summary>True while the startup overlay is visible (suppresses hotkeys).</summary>
        public static bool IsOpen { get; private set; } = true;

        private static readonly List<string> TemplateNames =
            Enum.GetNames(typeof(HeightmapTemplateType)).ToList();

        private void Start()
        {
            SetupUI();
        }

        private void OnEnable()
        {
            EconSim.Core.GameManager.OnMapReady += OnMapReady;
            EconSim.Core.GameManager.OnGlobeReady += OnGlobeReady;
            EconSim.Core.GameManager.OnSiteChanged += OnSiteChanged;
        }

        private void OnDisable()
        {
            EconSim.Core.GameManager.OnMapReady -= OnMapReady;
            EconSim.Core.GameManager.OnGlobeReady -= OnGlobeReady;
            EconSim.Core.GameManager.OnSiteChanged -= OnSiteChanged;
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
            _latitudeField = root.Q<FloatField>("latitude-field");
            // Setup template dropdown
            if (_templateDropdown != null)
            {
                _templateDropdown.choices = TemplateNames;
                _templateDropdown.index = TemplateNames.IndexOf(nameof(HeightmapTemplateType.Continents));
            }

            _generateGlobeButton = root.Q<Button>("generate-globe-button");

            // Site review mode elements
            _generationFields = root.Q<VisualElement>("generation-fields");
            _siteReviewFields = root.Q<VisualElement>("site-review-fields");
            _siteTypeLabel = root.Q<Label>("site-type-value");
            _siteLatLabel = root.Q<Label>("site-lat-value");
            _siteLngLabel = root.Q<Label>("site-lng-value");
            _siteTemplateLabel = root.Q<Label>("site-template-value");
            _siteCounterLabel = root.Q<Label>("site-counter-label");
            _sitePrevButton = root.Q<Button>("site-prev-button");
            _siteNextButton = root.Q<Button>("site-next-button");
            _generateFromSiteButton = root.Q<Button>("generate-from-site-button");
            _regenerateGlobeButton = root.Q<Button>("regenerate-globe-button");

            // Wire up buttons
            _generateButton?.RegisterCallback<ClickEvent>(evt => OnGenerateClicked());
            // Load Last Map button removed — Generate New auto-detects cache hits
            _generateGlobeButton?.RegisterCallback<ClickEvent>(evt => OnGenerateGlobeClicked());
            _generateFromSiteButton?.RegisterCallback<ClickEvent>(evt => OnGenerateFromSiteClicked());
            _regenerateGlobeButton?.RegisterCallback<ClickEvent>(evt => OnRegenerateGlobeClicked());
            _sitePrevButton?.RegisterCallback<ClickEvent>(evt => OnSitePrevClicked());
            _siteNextButton?.RegisterCallback<ClickEvent>(evt => OnSiteNextClicked());
            // Start in generation mode
            ShowGenerationMode();
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
                float latitude = _latitudeField?.value ?? 50f;
                var config = new MapGenConfig
                {
                    Seed = _seedField?.value ?? 12345,
                    CellCount = _cellCountField?.value ?? 100000,
                    AspectRatio = _aspectRatioField?.value ?? 1.5f,
                    Latitude = latitude,
                };

                if (_templateDropdown != null && _templateDropdown.index >= 0)
                {
                    config.Template = (HeightmapTemplateType)_templateDropdown.index;
                }

                try
                {
                    config.Validate();
                    gameManager.GenerateMap(config);
                }
                catch (Exception ex)
                {
                    SetStatus($"Invalid generation settings: {ex.Message}");
                    _isLoading = false;
                    EnableButtons();
                }
            }
            else
            {
                SetStatus("Error: GameManager not found");
                _isLoading = false;
                EnableButtons();
            }
        }

        private void OnGenerateGlobeClicked()
        {
            if (_isLoading) return;

            _isLoading = true;
            SetStatus("Generating globe...");
            DisableButtons();

            var gameManager = EconSim.Core.GameManager.Instance;
            if (gameManager != null)
            {
                int seed = _seedField?.value ?? 12345;
                float latitude = _latitudeField?.value ?? 50f;
                gameManager.GenerateGlobe(seed, latitude);
            }
            else
            {
                SetStatus("Error: GameManager not found");
                _isLoading = false;
                EnableButtons();
            }
        }

        private void OnGenerateFromSiteClicked()
        {
            if (_isLoading) return;

            _isLoading = true;
            SetStatus("Generating map from site...");
            DisableButtons();

            var gameManager = EconSim.Core.GameManager.Instance;
            if (gameManager != null)
            {
                gameManager.GenerateMapFromSite();
            }
            else
            {
                SetStatus("Error: GameManager not found");
                _isLoading = false;
                EnableButtons();
            }
        }

        private void OnRegenerateGlobeClicked()
        {
            if (_isLoading) return;

            // Increment seed for variety
            if (_seedField != null)
                _seedField.value = _seedField.value + 1;

            ShowGenerationMode();
            OnGenerateGlobeClicked();
        }

        private void OnSitePrevClicked()
        {
            EconSim.Core.GameManager.Instance?.CycleSite(-1);
        }

        private void OnSiteNextClicked()
        {
            EconSim.Core.GameManager.Instance?.CycleSite(1);
        }

        private void OnSiteChanged(SiteContext site, int index, int total)
        {
            if (site == null) return;
            UpdateSiteDisplay(site, index, total);
        }

        private void OnGlobeReady(SiteContext site)
        {
            _isLoading = false;

            if (site == null)
            {
                SetStatus("No suitable site found. Try a different seed.");
                ShowGenerationMode();
                EnableButtons();
                return;
            }

            ShowSiteReviewMode(site);
        }

        private void OnMapReady()
        {
            _isLoading = false;
            Hide();
        }

        private void ShowGenerationMode()
        {
            if (_generationFields != null)
                _generationFields.style.display = DisplayStyle.Flex;
            if (_siteReviewFields != null)
                _siteReviewFields.style.display = DisplayStyle.None;
            if (_overlay != null)
            {
                _overlay.RemoveFromClassList("site-review-mode");
                _overlay.pickingMode = PickingMode.Position;
            }
        }

        private void ShowSiteReviewMode(SiteContext site)
        {
            if (_generationFields != null)
                _generationFields.style.display = DisplayStyle.None;
            if (_siteReviewFields != null)
                _siteReviewFields.style.display = DisplayStyle.Flex;
            if (_overlay != null)
            {
                _overlay.AddToClassList("site-review-mode");
                _overlay.pickingMode = PickingMode.Ignore;
            }

            var gm = EconSim.Core.GameManager.Instance;
            UpdateSiteDisplay(site, gm?.CurrentSiteIndex ?? 0, gm?.SiteCount ?? 1);

            SetStatus("");
            EnableButtons();
        }

        private void UpdateSiteDisplay(SiteContext site, int index, int total)
        {
            var templateName = MapTemplateNameForSiteType(site.SiteType);

            if (_siteTypeLabel != null)
                _siteTypeLabel.text = site.SiteType.ToString();
            if (_siteLatLabel != null)
                _siteLatLabel.text = $"{site.Latitude:F1}\u00b0";
            if (_siteLngLabel != null)
                _siteLngLabel.text = $"{site.Longitude:F1}\u00b0";
            if (_siteTemplateLabel != null)
                _siteTemplateLabel.text = templateName;
            if (_siteCounterLabel != null)
                _siteCounterLabel.text = $"Site {index + 1} of {total}";

            bool canCycle = total > 1;
            _sitePrevButton?.SetEnabled(canCycle);
            _siteNextButton?.SetEnabled(canCycle);
        }

        private static string MapTemplateNameForSiteType(SiteType siteType)
        {
            return siteType switch
            {
                SiteType.Volcanic => "Volcano",
                SiteType.HighIsland => "HighIsland",
                SiteType.LowIsland => "LowIsland",
                SiteType.Archipelago => "HighIsland",
                _ => "LowIsland",
            };
        }

        public void Show()
        {
            if (_overlay != null)
            {
                _overlay.style.display = DisplayStyle.Flex;
            }
            IsOpen = true;
            ShowGenerationMode();
            EnableButtons();
            SetStatus("");
        }

        private void Hide()
        {
            if (_overlay != null)
            {
                _overlay.style.display = DisplayStyle.None;
            }
            IsOpen = false;
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
                _generateButton.SetEnabled(false);
            if (_generateGlobeButton != null)
                _generateGlobeButton.SetEnabled(false);
            if (_generateFromSiteButton != null)
                _generateFromSiteButton.SetEnabled(false);
            if (_regenerateGlobeButton != null)
                _regenerateGlobeButton.SetEnabled(false);
            if (_sitePrevButton != null)
                _sitePrevButton.SetEnabled(false);
            if (_siteNextButton != null)
                _siteNextButton.SetEnabled(false);
        }

        private void EnableButtons()
        {
            if (_generateButton != null)
                _generateButton.SetEnabled(true);
            if (_generateGlobeButton != null)
                _generateGlobeButton.SetEnabled(true);
            if (_generateFromSiteButton != null)
                _generateFromSiteButton.SetEnabled(true);
            if (_regenerateGlobeButton != null)
                _regenerateGlobeButton.SetEnabled(true);
        }
    }
}
