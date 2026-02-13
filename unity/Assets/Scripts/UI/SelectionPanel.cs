using UnityEngine;
using UnityEngine.UIElements;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Core.Simulation;
using EconSim.Renderer;
using System.Linq;

namespace EconSim.UI
{
    /// <summary>
    /// UI Toolkit controller for the political selection/inspection panel.
    /// Shows different info based on map mode: realm, province, or county.
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
        private Button _closeButton;
        private Label _entityName;
        private VisualElement _locationSection;
        private Label _provinceValue;
        private Label _stateValue;
        private Label _terrainValue;
        private Label _cultureValue;
        private Label _religionValue;
        private Label _popTotal;
        private Label _popWorkers;
        private Label _popEmployed;
        private VisualElement _resourcesSection;
        private VisualElement _resourcesList;
        private VisualElement _stockpileSection;
        private VisualElement _stockpileList;
        private VisualElement _facilitiesSection;
        private Label _facilitiesCount;

        private MapData _mapData;
        private EconomyState _economy;

        // Selection state - what's currently selected based on mode
        private int _selectedCountyId = -1;
        private int _selectedProvinceId = -1;
        private int _selectedRealmId = -1;

        private void Start()
        {
            if (EconSim.Core.GameManager.IsMapReady)
                StartCoroutine(Initialize());
            else
                EconSim.Core.GameManager.OnMapReady += OnMapReadyHandler;
        }

        private void OnMapReadyHandler()
        {
            EconSim.Core.GameManager.OnMapReady -= OnMapReadyHandler;
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

            // Subscribe to selection changes (fires after MapView updates its selection state)
            if (_mapView != null)
            {
                _mapView.OnSelectionChanged += OnSelectionChanged;
            }

            SetupUI();
        }

        private void OnDestroy()
        {
            EconSim.Core.GameManager.OnMapReady -= OnMapReadyHandler;
            if (_mapView != null)
            {
                _mapView.OnSelectionChanged -= OnSelectionChanged;
            }
        }

        private void OnSelectionChanged(MapView.SelectionDepth depth)
        {
            if (_mapView == null) return;

            // Don't show in non-political modes (Market/Biomes have their own panels or no panel)
            var mode = _mapView.CurrentMode;
            if (mode != MapView.MapMode.Political &&
                mode != MapView.MapMode.Province &&
                mode != MapView.MapMode.County)
            {
                Hide();
                return;
            }

            // Use the drill-down selection depth from MapView
            switch (depth)
            {
                case MapView.SelectionDepth.Realm:
                    SelectRealm(_mapView.SelectedRealmId);
                    break;
                case MapView.SelectionDepth.Province:
                    SelectProvince(_mapView.SelectedProvinceId);
                    break;
                case MapView.SelectionDepth.County:
                    SelectCounty(_mapView.SelectedCountyId);
                    break;
                default:
                    Hide();
                    break;
            }
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
            _closeButton = _root.Q<Button>("close-button");
            _entityName = _root.Q<Label>("county-name");  // Reusing as entity name
            _locationSection = _root.Q<VisualElement>("selection-panel")?.Q<VisualElement>(className: "section");
            _provinceValue = _root.Q<Label>("province-value");
            _stateValue = _root.Q<Label>("state-value");
            _terrainValue = _root.Q<Label>("terrain-value");
            _cultureValue = _root.Q<Label>("culture-value");
            _religionValue = _root.Q<Label>("religion-value");
            _popTotal = _root.Q<Label>("pop-total");
            _popWorkers = _root.Q<Label>("pop-workers");
            _popEmployed = _root.Q<Label>("pop-employed");
            _resourcesSection = _root.Q<VisualElement>("resources-section");
            _resourcesList = _root.Q<VisualElement>("resources-list");
            _stockpileSection = _root.Q<VisualElement>("stockpile-section");
            _stockpileList = _root.Q<VisualElement>("stockpile-list");
            _facilitiesSection = _root.Q<VisualElement>("facilities-section");
            _facilitiesCount = _root.Q<Label>("facilities-count");

            // Wire up close button
            _closeButton?.RegisterCallback<ClickEvent>(evt => Hide());

            // Start hidden
            if (_panel != null)
            {
                _panel.AddToClassList("hidden");
            }
        }

        public void SelectRealm(int realmId)
        {
            ClearSelection();
            _selectedRealmId = realmId;

            if (realmId <= 0 || _mapData == null || !_mapData.RealmById.ContainsKey(realmId))
            {
                Hide();
                return;
            }

            Show();
            UpdateRealmDisplay();
        }

        public void SelectProvince(int provinceId)
        {
            ClearSelection();
            _selectedProvinceId = provinceId;

            if (provinceId <= 0 || _mapData == null || !_mapData.ProvinceById.ContainsKey(provinceId))
            {
                Hide();
                return;
            }

            Show();
            UpdateProvinceDisplay();
        }

        public void SelectCounty(int countyId)
        {
            ClearSelection();
            _selectedCountyId = countyId;

            if (countyId <= 0 || _mapData == null || !_mapData.CountyById.ContainsKey(countyId))
            {
                Hide();
                return;
            }

            Show();
            UpdateCountyDisplay();
        }

        private void ClearSelection()
        {
            _selectedCountyId = -1;
            _selectedProvinceId = -1;
            _selectedRealmId = -1;
        }

        private bool HasSelection => _selectedCountyId > 0 || _selectedProvinceId > 0 || _selectedRealmId > 0;

        public void Show()
        {
            _panel?.RemoveFromClassList("hidden");
        }

        public void Hide()
        {
            _panel?.AddToClassList("hidden");
            ClearSelection();
        }

        private void Update()
        {
            // Escape key closes the panel
            if (Input.GetKeyDown(KeyCode.Escape) && HasSelection)
            {
                Hide();
                return;
            }

            // Refresh display periodically if something is selected
            if (HasSelection && Time.frameCount % 30 == 0)
            {
                if (_selectedRealmId > 0)
                    UpdateRealmDisplay();
                else if (_selectedProvinceId > 0)
                    UpdateProvinceDisplay();
                else if (_selectedCountyId > 0)
                    UpdateCountyDisplay();
            }
        }

        private void UpdateRealmDisplay()
        {
            if (_selectedRealmId <= 0 || _mapData == null) return;
            if (!_mapData.RealmById.TryGetValue(_selectedRealmId, out var realm)) return;

            // Title - use full name (includes government form, e.g. "Kuningaskunta of Härvälä")
            SetLabel(_entityName, realm.FullName ?? realm.Name ?? $"Realm {realm.Id}");

            // Location section - show capital and culture
            SetLabel(_provinceValue, "-");
            SetLabel(_stateValue, "-");

            string capitalName = "-";
            capitalName = GetBurgNameById(realm.CapitalBurgId);
            SetLabel(_terrainValue, capitalName);
            SetLabel(_cultureValue, GetCultureName(_selectedRealmId));
            SetLabel(_religionValue, GetReligionName(_selectedRealmId));

            // Find the terrain label and change its text to "Capital"
            var terrainRow = _terrainValue?.parent;
            if (terrainRow != null)
            {
                var label = terrainRow.Q<Label>();
                if (label != null && label != _terrainValue)
                    label.text = "Capital";
            }

            // Population - aggregate from all counties in this realm
            long totalPop = 0;
            long totalWorkers = 0;
            long totalEmployed = 0;
            int countyCount = 0;

            foreach (var cell in _mapData.Cells)
            {
                if (cell.RealmId != _selectedRealmId || !cell.IsLand) continue;
                countyCount++;

                if (_economy != null && _economy.Counties.TryGetValue(cell.Id, out var countyEcon))
                {
                    totalPop += countyEcon.Population.Total;
                    totalWorkers += countyEcon.Population.WorkingAge;
                    totalEmployed += countyEcon.Population.EmployedUnskilled + countyEcon.Population.EmployedSkilled;
                }
                else
                {
                    totalPop += (long)cell.Population;
                }
            }

            SetLabel(_popTotal, totalPop.ToString("N0"));
            SetLabel(_popWorkers, totalWorkers.ToString("N0"));
            SetLabel(_popEmployed, totalEmployed.ToString("N0"));

            // Show provinces list in resources section
            ShowSectionAsProvincesList(realm);

            // Hide stockpile and facilities sections
            SetSectionVisible(_stockpileSection, false);
            SetSectionVisible(_facilitiesSection, false);
        }

        private void UpdateProvinceDisplay()
        {
            if (_selectedProvinceId <= 0 || _mapData == null) return;
            if (!_mapData.ProvinceById.TryGetValue(_selectedProvinceId, out var province)) return;

            // Title - use full name (e.g. "Province of Koskiniemi")
            SetLabel(_entityName, province.FullName ?? province.Name ?? $"Province {province.Id}");

            // Location section
            SetLabel(_provinceValue, "-");

            string realmName = "-";
            if (province.RealmId > 0 && _mapData.RealmById.TryGetValue(province.RealmId, out var realm))
            {
                realmName = realm.Name;
            }
            SetLabel(_stateValue, realmName);

            string capitalName = "-";
            capitalName = GetBurgNameById(province.CapitalBurgId);
            SetLabel(_terrainValue, capitalName);
            SetLabel(_cultureValue, GetCultureName(province.RealmId));
            SetLabel(_religionValue, GetReligionName(province.RealmId));

            // Change terrain label to "Capital"
            var terrainRow = _terrainValue?.parent;
            if (terrainRow != null)
            {
                var label = terrainRow.Q<Label>();
                if (label != null && label != _terrainValue)
                    label.text = "Capital";
            }

            // Population - aggregate from all counties in this province
            long totalPop = 0;
            long totalWorkers = 0;
            long totalEmployed = 0;

            foreach (var cell in _mapData.Cells)
            {
                if (cell.ProvinceId != _selectedProvinceId || !cell.IsLand) continue;

                if (_economy != null && _economy.Counties.TryGetValue(cell.Id, out var countyEcon))
                {
                    totalPop += countyEcon.Population.Total;
                    totalWorkers += countyEcon.Population.WorkingAge;
                    totalEmployed += countyEcon.Population.EmployedUnskilled + countyEcon.Population.EmployedSkilled;
                }
                else
                {
                    totalPop += (long)cell.Population;
                }
            }

            SetLabel(_popTotal, totalPop.ToString("N0"));
            SetLabel(_popWorkers, totalWorkers.ToString("N0"));
            SetLabel(_popEmployed, totalEmployed.ToString("N0"));

            // Show counties list in resources section
            ShowSectionAsCountiesList(province);

            // Hide stockpile and facilities sections
            SetSectionVisible(_stockpileSection, false);
            SetSectionVisible(_facilitiesSection, false);
        }

        private void UpdateCountyDisplay()
        {
            if (_selectedCountyId <= 0 || _mapData == null || _mapData.CountyById == null) return;

            if (!_mapData.CountyById.TryGetValue(_selectedCountyId, out var county)) return;

            // Restore terrain label text to "Cells" for county mode
            var terrainRow = _terrainValue?.parent;
            if (terrainRow != null)
            {
                var label = terrainRow.Q<Label>();
                if (label != null && label != _terrainValue)
                    label.text = "Cells";
            }

            // County name
            string countyName = county.Name ?? $"County {county.Id}";
            SetLabel(_entityName, countyName);

            // Province
            string provinceName = "-";
            if (county.ProvinceId > 0 && _mapData.ProvinceById.TryGetValue(county.ProvinceId, out var province))
            {
                provinceName = province.Name;
            }
            SetLabel(_provinceValue, provinceName);

            // Realm
            string realmName = "-";
            if (county.RealmId > 0 && _mapData.RealmById.TryGetValue(county.RealmId, out var realm))
            {
                realmName = realm.Name;
            }
            SetLabel(_stateValue, realmName);

            // Cell count
            SetLabel(_terrainValue, $"{county.CellCount}");
            SetLabel(_cultureValue, GetCultureName(county.RealmId));
            SetLabel(_religionValue, GetReligionName(county.RealmId));

            // Show all sections for county mode
            SetSectionVisible(_resourcesSection, true);
            SetSectionVisible(_stockpileSection, true);
            SetSectionVisible(_facilitiesSection, true);

            // Restore resources section header
            var resourcesHeader = _resourcesSection?.Q<Label>(className: "section-header");
            if (resourcesHeader != null)
                resourcesHeader.text = "Resources";

            // Population data from economy
            if (_economy != null && _economy.Counties.TryGetValue(_selectedCountyId, out var countyEcon))
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
                // No economy data yet - use total population from county data
                SetLabel(_popTotal, ((int)county.TotalPopulation).ToString("N0"));
                SetLabel(_popWorkers, "-");
                SetLabel(_popEmployed, "-");
                ClearList(_resourcesList);
                ClearList(_stockpileList);
                SetLabel(_facilitiesCount, "0 facilities");
            }
        }

        private void ShowSectionAsProvincesList(Realm realm)
        {
            if (_resourcesSection == null || _resourcesList == null) return;

            SetSectionVisible(_resourcesSection, true);

            // Change header to "Provinces"
            var header = _resourcesSection.Q<Label>(className: "section-header");
            if (header != null)
                header.text = "Provinces";

            _resourcesList.Clear();

            if (realm.ProvinceIds == null || realm.ProvinceIds.Count == 0)
            {
                var noneLabel = new Label("None");
                noneLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                noneLabel.style.fontSize = 11;
                _resourcesList.Add(noneLabel);
                return;
            }

            foreach (var provId in realm.ProvinceIds)
            {
                if (!_mapData.ProvinceById.TryGetValue(provId, out var prov)) continue;

                var row = new VisualElement();
                row.AddToClassList("resource-item");

                var nameLabel = new Label(prov.Name ?? $"Province {provId}");
                row.Add(nameLabel);
                _resourcesList.Add(row);
            }
        }

        private void ShowSectionAsCountiesList(Province province)
        {
            if (_resourcesSection == null || _resourcesList == null) return;

            SetSectionVisible(_resourcesSection, true);

            // Change header to "Counties"
            var header = _resourcesSection.Q<Label>(className: "section-header");
            if (header != null)
                header.text = "Counties";

            _resourcesList.Clear();

            // Find all counties in this province
            var counties = _mapData.Counties?.Where(c => c.ProvinceId == province.Id).ToList()
                ?? new System.Collections.Generic.List<EconSim.Core.Data.County>();

            if (counties.Count == 0)
            {
                var noneLabel = new Label("None");
                noneLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                noneLabel.style.fontSize = 11;
                _resourcesList.Add(noneLabel);
                return;
            }

            // Show count if too many
            if (counties.Count > 10)
            {
                var countLabel = new Label($"{counties.Count} counties");
                countLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
                countLabel.style.fontSize = 11;
                _resourcesList.Add(countLabel);
                return;
            }

            foreach (var county in counties)
            {
                string name = county.Name ?? $"County {county.Id}";
                string cellInfo = county.CellCount == 1 ? "1 cell" : $"{county.CellCount} cells";

                var row = new VisualElement();
                row.AddToClassList("resource-item");

                var nameLabel = new Label($"{name} ({cellInfo})");
                row.Add(nameLabel);
                _resourcesList.Add(row);
            }
        }

        private void SetSectionVisible(VisualElement section, bool visible)
        {
            if (section == null) return;
            section.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
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

        private string GetCultureName(int realmId)
        {
            if (_mapData == null || _mapData.CultureById == null) return "-";
            if (realmId <= 0 || !_mapData.RealmById.TryGetValue(realmId, out var realm)) return "-";
            if (realm.CultureId <= 0 || !_mapData.CultureById.TryGetValue(realm.CultureId, out var culture)) return "-";
            return culture.Name ?? "-";
        }

        private string GetReligionName(int realmId)
        {
            if (_mapData == null || _mapData.CultureById == null || _mapData.ReligionById == null) return "-";
            if (realmId <= 0 || !_mapData.RealmById.TryGetValue(realmId, out var realm)) return "-";
            if (realm.CultureId <= 0 || !_mapData.CultureById.TryGetValue(realm.CultureId, out var culture)) return "-";
            if (culture.ReligionId <= 0 || !_mapData.ReligionById.TryGetValue(culture.ReligionId, out var religion)) return "-";
            return religion.Name ?? "-";
        }

        private string GetBurgNameById(int burgId)
        {
            if (_mapData == null || _mapData.Burgs == null || burgId <= 0)
                return "-";

            int index = burgId - 1; // IDs are 1-based; list indices are 0-based.
            if (index >= 0 && index < _mapData.Burgs.Count)
            {
                Burg burg = _mapData.Burgs[index];
                if (burg != null && burg.Id == burgId)
                    return burg.Name ?? "-";
            }

            for (int i = 0; i < _mapData.Burgs.Count; i++)
            {
                Burg burg = _mapData.Burgs[i];
                if (burg != null && burg.Id == burgId)
                    return burg.Name ?? "-";
            }

            return "-";
        }
    }
}
