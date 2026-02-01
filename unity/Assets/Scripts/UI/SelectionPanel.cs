using UnityEngine;
using UnityEngine.UIElements;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Core.Simulation;
using EconSim.Renderer;

namespace EconSim.UI
{
    /// <summary>
    /// UI Toolkit controller for the county selection/inspection panel.
    /// Shows detailed info about the currently selected county.
    /// </summary>
    public class SelectionPanel : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private VisualTreeAsset _panelTemplate;
        [SerializeField] private StyleSheet _styleSheet;
        [SerializeField] private MapView _mapView;

        private VisualElement _root;
        private VisualElement _panel;

        // Cached element references
        private Label _countyName;
        private Label _provinceValue;
        private Label _stateValue;
        private Label _terrainValue;
        private Label _popTotal;
        private Label _popWorkers;
        private Label _popEmployed;
        private VisualElement _resourcesList;
        private VisualElement _stockpileList;
        private Label _facilitiesCount;

        private MapData _mapData;
        private EconomyState _economy;
        private int _selectedCellId = -1;

        private void Start()
        {
            StartCoroutine(Initialize());
        }

        private System.Collections.IEnumerator Initialize()
        {
            yield return null;

            var gameManager = EconSim.Core.GameManager.Instance;
            if (gameManager != null)
            {
                _mapData = gameManager.MapData;
                var sim = gameManager.Simulation;
                if (sim != null)
                {
                    _economy = sim.GetState().Economy;
                }
            }

            // Find MapView if not assigned
            if (_mapView == null)
            {
                _mapView = FindObjectOfType<MapView>();
            }

            // Subscribe to cell clicks
            if (_mapView != null)
            {
                _mapView.OnCellClicked += OnCellClicked;
            }

            SetupUI();
        }

        private void OnDestroy()
        {
            if (_mapView != null)
            {
                _mapView.OnCellClicked -= OnCellClicked;
            }
        }

        private void OnCellClicked(int cellId)
        {
            SelectCounty(cellId);
        }

        private void SetupUI()
        {
            if (_uiDocument == null)
            {
                _uiDocument = GetComponent<UIDocument>();
            }

            if (_uiDocument == null)
            {
                Debug.LogError("SelectionPanel: No UIDocument found");
                return;
            }

            _root = _uiDocument.rootVisualElement;

            if (_styleSheet != null)
            {
                _root.styleSheets.Add(_styleSheet);
            }

            // Load the panel template
            if (_panelTemplate != null)
            {
                var panelInstance = _panelTemplate.Instantiate();
                _root.Add(panelInstance);
            }

            // Query elements
            _panel = _root.Q<VisualElement>("selection-panel");
            _countyName = _root.Q<Label>("county-name");
            _provinceValue = _root.Q<Label>("province-value");
            _stateValue = _root.Q<Label>("state-value");
            _terrainValue = _root.Q<Label>("terrain-value");
            _popTotal = _root.Q<Label>("pop-total");
            _popWorkers = _root.Q<Label>("pop-workers");
            _popEmployed = _root.Q<Label>("pop-employed");
            _resourcesList = _root.Q<VisualElement>("resources-list");
            _stockpileList = _root.Q<VisualElement>("stockpile-list");
            _facilitiesCount = _root.Q<Label>("facilities-count");

            // Start hidden
            if (_panel != null)
            {
                _panel.AddToClassList("hidden");
            }
        }

        /// <summary>
        /// Select a county by cell ID. Pass -1 to deselect.
        /// </summary>
        public void SelectCounty(int cellId)
        {
            _selectedCellId = cellId;

            if (cellId < 0 || _mapData == null || !_mapData.CellById.ContainsKey(cellId))
            {
                Hide();
                return;
            }

            Show();
            UpdateDisplay();
        }

        public void Show()
        {
            _panel?.RemoveFromClassList("hidden");
        }

        public void Hide()
        {
            _panel?.AddToClassList("hidden");
        }

        private void Update()
        {
            // Refresh display periodically if something is selected
            if (_selectedCellId >= 0 && Time.frameCount % 30 == 0)
            {
                UpdateDisplay();
            }
        }

        private void UpdateDisplay()
        {
            if (_selectedCellId < 0 || _mapData == null) return;

            if (!_mapData.CellById.TryGetValue(_selectedCellId, out var cell)) return;

            // County name (use burg name if has settlement, otherwise Cell #)
            string countyName = $"Cell {cell.Id}";
            if (cell.BurgId > 0 && cell.BurgId < _mapData.Burgs.Count)
            {
                var burg = _mapData.Burgs[cell.BurgId];
                if (!string.IsNullOrEmpty(burg.Name))
                {
                    countyName = burg.Name;
                }
            }
            SetLabel(_countyName, countyName);

            // Province
            string provinceName = "-";
            if (cell.ProvinceId > 0 && _mapData.ProvinceById.TryGetValue(cell.ProvinceId, out var province))
            {
                provinceName = province.Name;
            }
            SetLabel(_provinceValue, provinceName);

            // State
            string stateName = "-";
            if (cell.StateId > 0 && _mapData.StateById.TryGetValue(cell.StateId, out var state))
            {
                stateName = state.Name;
            }
            SetLabel(_stateValue, stateName);

            // Terrain/Biome
            string biomeName = "Unknown";
            if (cell.BiomeId >= 0 && cell.BiomeId < _mapData.Biomes.Count)
            {
                biomeName = _mapData.Biomes[cell.BiomeId].Name;
            }
            SetLabel(_terrainValue, biomeName);

            // Population data from economy
            if (_economy != null && _economy.Counties.TryGetValue(_selectedCellId, out var countyEcon))
            {
                var pop = countyEcon.Population;
                SetLabel(_popTotal, pop.Total.ToString("N0"));
                SetLabel(_popWorkers, pop.WorkingAge.ToString("N0"));
                int employed = pop.EmployedUnskilled + pop.EmployedSkilled;
                SetLabel(_popEmployed, employed.ToString("N0"));

                // Resources
                UpdateResourcesList(countyEcon);

                // Stockpile
                UpdateStockpileList(countyEcon);

                // Facilities
                int facilityCount = countyEcon.FacilityIds?.Count ?? 0;
                SetLabel(_facilitiesCount, $"{facilityCount} facilities");
            }
            else
            {
                // No economy data yet
                SetLabel(_popTotal, ((int)cell.Population).ToString("N0"));
                SetLabel(_popWorkers, "-");
                SetLabel(_popEmployed, "-");
                ClearList(_resourcesList);
                ClearList(_stockpileList);
                SetLabel(_facilitiesCount, "0 facilities");
            }
        }

        private void UpdateResourcesList(CountyEconomy county)
        {
            if (_resourcesList == null) return;

            _resourcesList.Clear();

            if (county.Resources == null || county.Resources.Count == 0)
            {
                var noneLabel = new Label("None");
                noneLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                noneLabel.style.fontSize = 11;
                _resourcesList.Add(noneLabel);
                return;
            }

            foreach (var kvp in county.Resources)
            {
                if (kvp.Value <= 0) continue;

                var row = new VisualElement();
                row.AddToClassList("resource-item");

                var nameLabel = new Label(kvp.Key);
                var valueLabel = new Label($"{kvp.Value:P0}");

                row.Add(nameLabel);
                row.Add(valueLabel);
                _resourcesList.Add(row);
            }
        }

        private void UpdateStockpileList(CountyEconomy county)
        {
            if (_stockpileList == null) return;

            _stockpileList.Clear();

            var stockpile = county.Stockpile;
            if (stockpile == null || stockpile.IsEmpty)
            {
                var noneLabel = new Label("Empty");
                noneLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                noneLabel.style.fontSize = 11;
                _stockpileList.Add(noneLabel);
                return;
            }

            foreach (var kvp in stockpile.All)
            {
                if (kvp.Value < 0.1f) continue;

                var row = new VisualElement();
                row.AddToClassList("stockpile-item");

                var nameLabel = new Label(kvp.Key);
                var valueLabel = new Label($"{kvp.Value:F1}");

                row.Add(nameLabel);
                row.Add(valueLabel);
                _stockpileList.Add(row);
            }

            if (_stockpileList.childCount == 0)
            {
                var noneLabel = new Label("Empty");
                noneLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                noneLabel.style.fontSize = 11;
                _stockpileList.Add(noneLabel);
            }
        }

        private void SetLabel(Label label, string text)
        {
            if (label != null) label.text = text;
        }

        private void ClearList(VisualElement list)
        {
            list?.Clear();
        }
    }
}
