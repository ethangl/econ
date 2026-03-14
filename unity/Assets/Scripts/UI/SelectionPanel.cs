using UnityEngine;
using UnityEngine.UIElements;
using EconSim.Core.Actors;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Core.Religious;
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

        private Label _rulerValue;

        private VisualElement _resourcesSection;
        private VisualElement _resourcesList;
        private VisualElement _selectionRail;

        private MapData _mapData;
        private ISimulation _simulation;
        private ActorState _actorState;
        private ReligionState _religionState;
        private Coroutine _panelAnimationCoroutine;
        private float _currentRailWidth;
        private float _nextOpenDuration = DefaultPanelAnimationDuration;

        private const float PanelOpenWidth = 480f;
        private const float DefaultPanelAnimationDuration = 0.35f;
        private const float MinPanelAnimationDuration = 0.08f;

        // Selection state - what's currently selected based on mode
        private int _selectedCountyId = -1;
        private int _selectedProvinceId = -1;
        private int _selectedRealmId = -1;
        private int _selectedArchdioceseId = -1;
        private int _selectedDioceseId = -1;
        private int _selectedParishId = -1;

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
                _simulation = gameManager.Simulation;
                if (_simulation != null)
                {
                    _actorState = _simulation.GetState()?.Actors;
                    _religionState = _simulation.GetState()?.Religion;
                }
            }

            // Find MapView if not assigned
            if (_mapView == null)
            {
                _mapView = FindAnyObjectByType<MapView>();
            }

            // Subscribe to selection changes (fires after MapView updates its selection state)
            if (_mapView != null)
            {
                _mapView.OnSelectionChanged += OnSelectionChanged;
                _mapView.OnSelectionFocusStarted += OnSelectionFocusStarted;
            }

            SetupUI();
        }

        private void OnDestroy()
        {
            EconSim.Core.GameManager.OnMapReady -= OnMapReadyHandler;
            if (_mapView != null)
            {
                _mapView.OnSelectionChanged -= OnSelectionChanged;
                _mapView.OnSelectionFocusStarted -= OnSelectionFocusStarted;
            }

            if (_panelAnimationCoroutine != null)
            {
                StopCoroutine(_panelAnimationCoroutine);
                _panelAnimationCoroutine = null;
            }
        }

        private static bool IsPoliticalOrReligionMode(MapView.MapMode mode)
        {
            return mode == MapView.MapMode.Political ||
                   mode == MapView.MapMode.Province ||
                   mode == MapView.MapMode.County ||
                   mode == MapView.MapMode.Religion ||
                   mode == MapView.MapMode.ReligionDiocese ||
                   mode == MapView.MapMode.ReligionParish;
        }

        private void OnSelectionChanged(MapView.SelectionScope scope)
        {
            if (_mapView == null) return;

            if (!IsPoliticalOrReligionMode(_mapView.CurrentMode))
            {
                Hide();
                return;
            }

            switch (scope)
            {
                case MapView.SelectionScope.Realm:
                    SelectRealm(_mapView.SelectedRealmId);
                    break;
                case MapView.SelectionScope.Province:
                    SelectProvince(_mapView.SelectedProvinceId);
                    break;
                case MapView.SelectionScope.County:
                    SelectCounty(_mapView.SelectedCountyId);
                    break;
                case MapView.SelectionScope.Archdiocese:
                    SelectArchdiocese(_mapView.SelectedArchdioceseId);
                    break;
                case MapView.SelectionScope.Diocese:
                    SelectDiocese(_mapView.SelectedDioceseId);
                    break;
                case MapView.SelectionScope.Parish:
                    SelectParish(_mapView.SelectedParishId);
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
            _selectionRail = _root.Q<VisualElement>("selection-rail");
            _closeButton = _root.Q<Button>("close-button");
            _entityName = _root.Q<Label>("county-name");
            _rulerValue = _root.Q<Label>("ruler-value");
            _locationSection = _root.Q<VisualElement>("selection-panel")?.Q<VisualElement>(className: "section");
            _provinceValue = _root.Q<Label>("province-value");
            _stateValue = _root.Q<Label>("state-value");
            _terrainValue = _root.Q<Label>("terrain-value");
            _cultureValue = _root.Q<Label>("culture-value");
            _religionValue = _root.Q<Label>("religion-value");
            _popTotal = _root.Q<Label>("pop-total");

            _resourcesSection = _root.Q<VisualElement>("resources-section");
            _resourcesList = _root.Q<VisualElement>("resources-list");

            // Wire up close button
            _closeButton?.RegisterCallback<ClickEvent>(evt => Hide());

            // Start hidden
            SetPanelOpenImmediate(false);
        }

        public void SelectRealm(int realmId)
        {
            ClearSelection();
            _selectedRealmId = realmId;

            if (!TryGetRealm(realmId, out _))
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

            if (!TryGetProvince(provinceId, out _))
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

            if (!TryGetCounty(countyId, out _))
            {
                Hide();
                return;
            }

            Show();
            UpdateCountyDisplay();
        }

        public void SelectArchdiocese(int archdioceseId)
        {
            ClearSelection();
            _selectedArchdioceseId = archdioceseId;

            if (_religionState == null || archdioceseId <= 0 || archdioceseId >= _religionState.Archdioceses.Length
                || _religionState.Archdioceses[archdioceseId] == null)
            {
                Hide();
                return;
            }

            Show();
            UpdateArchdioceseDisplay();
        }

        public void SelectDiocese(int dioceseId)
        {
            ClearSelection();
            _selectedDioceseId = dioceseId;

            if (_religionState == null || dioceseId <= 0 || dioceseId >= _religionState.Dioceses.Length
                || _religionState.Dioceses[dioceseId] == null)
            {
                Hide();
                return;
            }

            Show();
            UpdateDioceseDisplay();
        }

        public void SelectParish(int parishId)
        {
            ClearSelection();
            _selectedParishId = parishId;

            if (_religionState == null || parishId <= 0 || parishId >= _religionState.Parishes.Length
                || _religionState.Parishes[parishId] == null)
            {
                Hide();
                return;
            }

            Show();
            UpdateParishDisplay();
        }

        private void ClearSelection()
        {
            _selectedCountyId = -1;
            _selectedProvinceId = -1;
            _selectedRealmId = -1;
            _selectedArchdioceseId = -1;
            _selectedDioceseId = -1;
            _selectedParishId = -1;
        }

        private bool HasSelection => _selectedCountyId > 0 || _selectedProvinceId > 0 || _selectedRealmId > 0
            || _selectedArchdioceseId > 0 || _selectedDioceseId > 0 || _selectedParishId > 0;

        public void Show()
        {
            AnimatePanel(true, _nextOpenDuration);
            _nextOpenDuration = DefaultPanelAnimationDuration;
        }

        public void Hide()
        {
            AnimatePanel(false, DefaultPanelAnimationDuration);
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
                else if (_selectedArchdioceseId > 0)
                    UpdateArchdioceseDisplay();
                else if (_selectedDioceseId > 0)
                    UpdateDioceseDisplay();
                else if (_selectedParishId > 0)
                    UpdateParishDisplay();
            }
        }

        private void UpdateRealmDisplay()
        {
            if (!TryGetRealm(_selectedRealmId, out var realm)) return;

            // Title - use full name (includes government form, e.g. "Kuningaskunta of Härvälä")
            SetLabel(_entityName, realm.FullName ?? realm.Name ?? $"Realm {realm.Id}");

            // Ruler
            var realmHolder = _actorState?.GetRealmHolder(realm.Id);
            SetLabel(_rulerValue, GetRulerDisplay(realmHolder, TitleRank.Realm));

            // Location section - show capital and culture
            SetLabel(_provinceValue, "-");
            SetLabel(_stateValue, "-");

            string capitalName = GetBurgNameById(realm.CapitalBurgId);
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

            // Population - aggregate from all cells in this realm
            long totalPop = 0;

            foreach (var cell in _mapData.Cells ?? Enumerable.Empty<Cell>())
            {
                if (cell.RealmId != _selectedRealmId || !cell.IsLand) continue;
                totalPop += (long)cell.Population;
            }

            SetLabel(_popTotal, totalPop.ToString("N0"));


            // Show provinces list in resources section
            ShowSectionAsProvincesList(realm);
        }

        private void UpdateProvinceDisplay()
        {
            if (!TryGetProvince(_selectedProvinceId, out var province)) return;

            // Title - use full name (e.g. "Province of Koskiniemi")
            SetLabel(_entityName, province.FullName ?? province.Name ?? $"Province {province.Id}");

            // Ruler
            var provHolder = _actorState?.GetProvinceHolder(province.Id);
            SetLabel(_rulerValue, GetRulerDisplay(provHolder, TitleRank.Province));

            // Location section
            SetLabel(_provinceValue, "-");

            string realmName = "-";
            if (TryGetRealm(province.RealmId, out var realm))
            {
                realmName = realm.Name;
            }
            SetLabel(_stateValue, realmName);

            string capitalName = GetBurgNameById(province.CapitalBurgId);
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

            // Population - aggregate from all cells in this province
            long totalPop = 0;

            foreach (var cell in _mapData.Cells ?? Enumerable.Empty<Cell>())
            {
                if (cell.ProvinceId != _selectedProvinceId || !cell.IsLand) continue;
                totalPop += (long)cell.Population;
            }

            SetLabel(_popTotal, totalPop.ToString("N0"));


            // Show counties list in resources section
            ShowSectionAsCountiesList(province);
        }

        private void UpdateCountyDisplay()
        {
            if (!TryGetCounty(_selectedCountyId, out var county)) return;

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

            // Ruler
            var countyHolder = _actorState?.GetCountyHolder(county.Id);
            SetLabel(_rulerValue, GetRulerDisplay(countyHolder, TitleRank.County));

            // Province
            string provinceName = "-";
            if (TryGetProvince(county.ProvinceId, out var province))
            {
                provinceName = province.Name;
            }
            SetLabel(_provinceValue, provinceName);

            // Realm
            string realmName = "-";
            if (TryGetRealm(county.RealmId, out var realm))
            {
                realmName = realm.Name;
            }
            SetLabel(_stateValue, realmName);

            // Cell count
            SetLabel(_terrainValue, $"{county.CellCount}");
            SetLabel(_cultureValue, GetCultureName(county.RealmId));
            SetLabel(_religionValue, GetCountyMajorityFaithName(county.Id));

            // Population — use live v4 economy data if available
            float pop = county.TotalPopulation;
            var econState = _simulation?.GetState()?.Economy;
            if (econState?.Counties != null && county.Id < econState.Counties.Length && econState.Counties[county.Id] != null)
                pop = econState.Counties[county.Id].TotalPopulation;
            SetLabel(_popTotal, ((int)pop).ToString("N0"));

            // Show adherence breakdown in religion mode, economy otherwise
            SetSectionVisible(_resourcesSection, true);
            var resourcesHeader = _resourcesSection?.Q<Label>(className: "section-header");

            if (_mapView != null && (_mapView.CurrentMode == MapView.MapMode.Religion ||
                _mapView.CurrentMode == MapView.MapMode.ReligionDiocese ||
                _mapView.CurrentMode == MapView.MapMode.ReligionParish))
            {
                if (resourcesHeader != null)
                    resourcesHeader.text = "Faith Adherence";
                ShowCountyAdherence(county.Id);
            }
            else
            {
                if (resourcesHeader != null)
                    resourcesHeader.text = "Economy";
                ShowCountyEconomy(county.Id);
            }
        }

        private void UpdateArchdioceseDisplay()
        {
            var arch = _religionState.Archdioceses[_selectedArchdioceseId];
            if (arch == null) return;

            int faithReligionId = (arch.FaithIndex >= 0 && arch.FaithIndex < _religionState.FaithIndexToReligion.Length)
                ? _religionState.FaithIndexToReligion[arch.FaithIndex] : 0;
            string faithName = faithReligionId > 0 ? GetFaithName(faithReligionId) : "Unknown";

            SetLabel(_entityName, $"Archdiocese {arch.Id}");
            SetLabel(_rulerValue, GetReligiousRulerDisplay(arch.ArchbishopActorId, TitleRank.Archdiocese));
            SetLabel(_provinceValue, "-");
            SetLabel(_stateValue, faithName);
            SetLabel(_terrainValue, "-");
            SetLabel(_cultureValue, "-");
            SetLabel(_religionValue, faithName);

            // Population: sum counties across all parishes in all dioceses
            long totalPop = 0;
            if (arch.DioceseIds != null)
            {
                var countedCounties = new System.Collections.Generic.HashSet<int>();
                foreach (int dioId in arch.DioceseIds)
                {
                    if (dioId <= 0 || dioId >= _religionState.Dioceses.Length) continue;
                    var dio = _religionState.Dioceses[dioId];
                    if (dio?.ParishIds == null) continue;
                    foreach (int pid in dio.ParishIds)
                    {
                        if (pid <= 0 || pid >= _religionState.Parishes.Length) continue;
                        var par = _religionState.Parishes[pid];
                        if (par?.CountyIds == null) continue;
                        foreach (int cid in par.CountyIds)
                        {
                            if (countedCounties.Add(cid) && TryGetCounty(cid, out var county))
                                totalPop += (long)county.TotalPopulation;
                        }
                    }
                }
            }
            SetLabel(_popTotal, totalPop.ToString("N0"));

            // Show dioceses list
            ShowSectionAsDiocesesList(arch);
        }

        private void UpdateDioceseDisplay()
        {
            var diocese = _religionState.Dioceses[_selectedDioceseId];
            if (diocese == null) return;

            int faithReligionId = (diocese.FaithIndex >= 0 && diocese.FaithIndex < _religionState.FaithIndexToReligion.Length)
                ? _religionState.FaithIndexToReligion[diocese.FaithIndex] : 0;
            string faithName = faithReligionId > 0 ? GetFaithName(faithReligionId) : "Unknown";

            SetLabel(_entityName, $"Diocese {diocese.Id}");
            SetLabel(_rulerValue, GetReligiousRulerDisplay(diocese.BishopActorId, TitleRank.Diocese));
            SetLabel(_provinceValue, "-");
            SetLabel(_stateValue, faithName);
            SetLabel(_terrainValue, "-");
            SetLabel(_cultureValue, "-");
            SetLabel(_religionValue, faithName);

            long totalPop = 0;
            var countedCounties = new System.Collections.Generic.HashSet<int>();
            if (diocese.ParishIds != null)
            {
                foreach (int pid in diocese.ParishIds)
                {
                    if (pid <= 0 || pid >= _religionState.Parishes.Length) continue;
                    var par = _religionState.Parishes[pid];
                    if (par?.CountyIds == null) continue;
                    foreach (int cid in par.CountyIds)
                    {
                        if (countedCounties.Add(cid) && TryGetCounty(cid, out var county))
                            totalPop += (long)county.TotalPopulation;
                    }
                }
            }
            SetLabel(_popTotal, totalPop.ToString("N0"));

            // Show parishes list
            ShowSectionAsParishesList(diocese);
        }

        private void UpdateParishDisplay()
        {
            var parish = _religionState.Parishes[_selectedParishId];
            if (parish == null) return;

            int faithReligionId = (parish.FaithIndex >= 0 && parish.FaithIndex < _religionState.FaithIndexToReligion.Length)
                ? _religionState.FaithIndexToReligion[parish.FaithIndex] : 0;
            string faithName = faithReligionId > 0 ? GetFaithName(faithReligionId) : "Unknown";

            SetLabel(_entityName, $"Parish {parish.Id}");
            SetLabel(_rulerValue, GetReligiousRulerDisplay(parish.PriestActorId, TitleRank.Parish));
            SetLabel(_provinceValue, "-");
            SetLabel(_stateValue, faithName);
            SetLabel(_terrainValue, $"{parish.CountyIds?.Count ?? 0} counties");
            SetLabel(_cultureValue, "-");
            SetLabel(_religionValue, faithName);

            long totalPop = 0;
            if (parish.CountyIds != null)
            {
                foreach (int cid in parish.CountyIds)
                {
                    if (TryGetCounty(cid, out var county))
                        totalPop += (long)county.TotalPopulation;
                }
            }
            SetLabel(_popTotal, totalPop.ToString("N0"));

            // Show counties list
            if (_resourcesSection != null && _resourcesList != null)
            {
                SetSectionVisible(_resourcesSection, true);
                var header = _resourcesSection.Q<Label>(className: "section-header");
                if (header != null) header.text = "Counties";
                _resourcesList.Clear();

                if (parish.CountyIds != null)
                {
                    foreach (int cid in parish.CountyIds)
                    {
                        if (!TryGetCounty(cid, out var county)) continue;
                        var row = new VisualElement();
                        row.AddToClassList("resource-item");
                        row.Add(new Label(county.Name ?? $"County {cid}"));
                        _resourcesList.Add(row);
                    }
                }
            }
        }

        private string GetReligiousRulerDisplay(int actorId, TitleRank rank)
        {
            if (_actorState == null || actorId <= 0 || actorId >= _actorState.Actors.Length)
                return "-";
            var actor = _actorState.Actors[actorId];
            if (actor == null) return "-";
            return GetRulerDisplay(actor, rank);
        }

        private void ShowSectionAsDiocesesList(Archdiocese arch)
        {
            if (_resourcesSection == null || _resourcesList == null) return;
            SetSectionVisible(_resourcesSection, true);
            var header = _resourcesSection.Q<Label>(className: "section-header");
            if (header != null) header.text = "Dioceses";
            _resourcesList.Clear();

            if (arch.DioceseIds == null || arch.DioceseIds.Count == 0)
            {
                _resourcesList.Add(new Label("None") { style = { color = new Color(0.5f, 0.5f, 0.5f), fontSize = 13 } });
                return;
            }

            foreach (int dioId in arch.DioceseIds)
            {
                if (dioId <= 0 || dioId >= _religionState.Dioceses.Length) continue;
                var dio = _religionState.Dioceses[dioId];
                if (dio == null) continue;

                var row = new VisualElement();
                row.AddToClassList("resource-item");
                row.Add(new Label($"Diocese {dioId} ({dio.ParishIds?.Count ?? 0} parishes)"));
                _resourcesList.Add(row);
            }
        }

        private void ShowSectionAsParishesList(Diocese diocese)
        {
            if (_resourcesSection == null || _resourcesList == null) return;
            SetSectionVisible(_resourcesSection, true);
            var header = _resourcesSection.Q<Label>(className: "section-header");
            if (header != null) header.text = "Parishes";
            _resourcesList.Clear();

            if (diocese.ParishIds == null || diocese.ParishIds.Count == 0)
            {
                _resourcesList.Add(new Label("None") { style = { color = new Color(0.5f, 0.5f, 0.5f), fontSize = 13 } });
                return;
            }

            if (diocese.ParishIds.Count > 15)
            {
                _resourcesList.Add(new Label($"{diocese.ParishIds.Count} parishes") { style = { color = new Color(0.7f, 0.7f, 0.7f), fontSize = 13 } });
                return;
            }

            foreach (int pid in diocese.ParishIds)
            {
                if (pid <= 0 || pid >= _religionState.Parishes.Length) continue;
                var par = _religionState.Parishes[pid];
                if (par == null) continue;

                var row = new VisualElement();
                row.AddToClassList("resource-item");
                row.Add(new Label($"Parish {pid} ({par.CountyIds?.Count ?? 0} counties)"));
                _resourcesList.Add(row);
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
                noneLabel.style.fontSize = 13;
                _resourcesList.Add(noneLabel);
                return;
            }

            foreach (var provId in realm.ProvinceIds)
            {
                if (!TryGetProvince(provId, out var prov)) continue;

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
                noneLabel.style.fontSize = 13;
                _resourcesList.Add(noneLabel);
                return;
            }

            // Show count if too many
            if (counties.Count > 10)
            {
                var countLabel = new Label($"{counties.Count} counties");
                countLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
                countLabel.style.fontSize = 13;
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

        private void ShowCountyAdherence(int countyId)
        {
            if (_resourcesList == null) return;
            _resourcesList.Clear();

            if (_religionState == null || _religionState.FaithCount == 0 ||
                countyId < 0 || countyId >= _religionState.Adherence.Length)
            {
                var noneLabel = new Label("No data");
                noneLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                noneLabel.style.fontSize = 13;
                _resourcesList.Add(noneLabel);
                return;
            }

            var adh = _religionState.Adherence[countyId];
            if (adh == null) return;

            // Build sorted list of faiths with non-zero adherence
            var entries = new System.Collections.Generic.List<(int faithIndex, float pct)>();
            for (int f = 0; f < _religionState.FaithCount; f++)
            {
                if (adh[f] > 0.001f)
                    entries.Add((f, adh[f]));
            }
            entries.Sort((a, b) => b.pct.CompareTo(a.pct));

            if (entries.Count == 0)
            {
                var noneLabel = new Label("Unaffiliated");
                noneLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                noneLabel.style.fontSize = 13;
                _resourcesList.Add(noneLabel);
                return;
            }

            foreach (var (fi, pct) in entries)
            {
                int religionId = _religionState.FaithIndexToReligion[fi];
                string faithName = GetFaithName(religionId);

                var row = new VisualElement();
                row.AddToClassList("resource-item");

                var nameLabel = new Label(faithName);
                nameLabel.style.fontSize = 13;
                row.Add(nameLabel);

                var valueLabel = new Label($"{pct * 100f:F1}%");
                valueLabel.AddToClassList("item-value");
                valueLabel.style.fontSize = 13;
                row.Add(valueLabel);

                _resourcesList.Add(row);
            }

            // Show unaffiliated remainder
            float total = 0f;
            for (int f = 0; f < _religionState.FaithCount; f++)
                total += adh[f];

            if (total < 0.999f)
            {
                var row = new VisualElement();
                row.AddToClassList("resource-item");

                var nameLabel = new Label("Unaffiliated");
                nameLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                nameLabel.style.fontSize = 13;
                row.Add(nameLabel);

                var valueLabel = new Label($"{(1f - total) * 100f:F1}%");
                valueLabel.AddToClassList("item-value");
                valueLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                valueLabel.style.fontSize = 13;
                row.Add(valueLabel);

                _resourcesList.Add(row);
            }
        }

        private void ShowCountyEconomy(int countyId)
        {
            if (_resourcesList == null) return;
            _resourcesList.Clear();

            var state = _simulation?.GetState();
            var econ = state?.Economy;
            if (econ?.Counties == null || countyId <= 0 || countyId >= econ.Counties.Length)
            {
                _resourcesList.Add(new Label("No data") { style = { color = new Color(0.5f, 0.5f, 0.5f), fontSize = 13 } });
                return;
            }

            var ce = econ.Counties[countyId];
            if (ce == null)
            {
                _resourcesList.Add(new Label("No data") { style = { color = new Color(0.5f, 0.5f, 0.5f), fontSize = 13 } });
                return;
            }

            // Population by class
            AddEconRow("Serfs (LC)", $"{ce.LowerCommonerPop:N0}");
            AddEconRow("Artisans (UC)", $"{ce.UpperCommonerPop:N0}");
            AddEconRow("Lower Nobility", $"{ce.LowerNobilityPop:N0}");
            AddEconRow("Upper Nobility", $"{ce.UpperNobilityPop:N0}");
            AddEconRow("Lower Clergy", $"{ce.LowerClergyPop:N0}");
            AddEconRow("Upper Clergy", $"{ce.UpperClergyPop:N0}");

            AddEconSeparator();

            // Satisfaction
            AddEconRow("LC Satisfaction", $"{ce.LowerCommonerSatisfaction:P0}");
            AddEconRow("UC Satisfaction", $"{ce.UpperCommonerSatisfaction:P0}");
            AddEconRow("Survival", $"{ce.SurvivalSatisfaction:P0}");
            AddEconRow("Religion", $"{ce.ReligionSatisfaction:P0}");
            AddEconRow("Economic", $"{ce.EconomicSatisfaction:P0}");

            AddEconSeparator();

            // Coin pools
            AddEconRow("UC Coin", $"{ce.UpperCommonerCoin:F1} Cr");
            AddEconRow("UN Treasury", $"{ce.UpperNobleTreasury:F1} Cr");
            AddEconRow("LN Treasury", $"{ce.LowerNobleTreasury:F1} Cr");
            AddEconRow("UC Treasury", $"{ce.UpperClergyTreasury:F1} Cr");

            // Market assignment
            if (econ.CountyToMarket != null && countyId < econ.CountyToMarket.Length)
            {
                int mId = econ.CountyToMarket[countyId];
                if (mId > 0)
                {
                    AddEconSeparator();
                    AddEconRow("Market", $"#{mId}");
                    if (mId < econ.Markets.Length && econ.Markets[mId] != null)
                        AddEconRow("Price Level", $"{econ.Markets[mId].PriceLevel:F2}");
                }
            }
        }

        private void AddEconRow(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("resource-item");

            var nameLabel = new Label(label);
            nameLabel.style.fontSize = 13;
            row.Add(nameLabel);

            var valueLabel = new Label(value);
            valueLabel.AddToClassList("item-value");
            valueLabel.style.fontSize = 13;
            row.Add(valueLabel);

            _resourcesList.Add(row);
        }

        private void AddEconSeparator()
        {
            var sep = new VisualElement();
            sep.style.height = 4;
            _resourcesList.Add(sep);
        }

        private string GetCountyMajorityFaithName(int countyId)
        {
            if (_religionState != null && countyId >= 0 && countyId < _religionState.MajorityFaith.Length)
            {
                int majorityReligionId = _religionState.MajorityFaith[countyId];
                if (majorityReligionId > 0)
                    return GetFaithName(majorityReligionId);
            }
            return GetReligionName(_mapData?.CountyById != null &&
                _mapData.CountyById.TryGetValue(countyId, out var c) ? c.RealmId : 0);
        }

        private string GetFaithName(int religionId)
        {
            if (_mapData?.ReligionById != null &&
                _mapData.ReligionById.TryGetValue(religionId, out var religion))
                return religion.Name ?? $"Faith {religionId}";
            return $"Faith {religionId}";
        }

        private void SetSectionVisible(VisualElement section, bool visible)
        {
            if (section == null) return;
            section.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetLabel(Label label, string text)
        {
            if (label != null) label.text = text;
        }

        private void ClearList(VisualElement list)
        {
            list?.Clear();
        }

        private void OnSelectionFocusStarted(float durationSeconds)
        {
            if (durationSeconds <= 0f)
            {
                _nextOpenDuration = DefaultPanelAnimationDuration;
                return;
            }

            _nextOpenDuration = Mathf.Max(MinPanelAnimationDuration, durationSeconds);
        }

        private void AnimatePanel(bool open, float durationSeconds)
        {
            if (_selectionRail == null)
            {
                return;
            }

            if (_panelAnimationCoroutine != null)
            {
                StopCoroutine(_panelAnimationCoroutine);
                _panelAnimationCoroutine = null;
            }

            float targetWidth = open ? PanelOpenWidth : 0f;
            if (Mathf.Abs(_currentRailWidth - targetWidth) < 0.01f)
            {
                SetRailWidth(targetWidth);
                _selectionRail.pickingMode = open ? PickingMode.Position : PickingMode.Ignore;
                return;
            }

            if (open)
                _selectionRail.pickingMode = PickingMode.Position;
            else
                _selectionRail.pickingMode = PickingMode.Ignore;

            _panelAnimationCoroutine = StartCoroutine(AnimatePanelCoroutine(targetWidth, durationSeconds));
        }

        private System.Collections.IEnumerator AnimatePanelCoroutine(float targetWidth, float durationSeconds)
        {
            float startWidth = _currentRailWidth;
            float duration = Mathf.Max(MinPanelAnimationDuration, durationSeconds);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                float width = Mathf.Lerp(startWidth, targetWidth, eased);
                SetRailWidth(width);
                yield return null;
            }

            SetRailWidth(targetWidth);
            _panelAnimationCoroutine = null;
        }

        private void SetPanelOpenImmediate(bool open)
        {
            if (_selectionRail == null) return;

            float targetWidth = open ? PanelOpenWidth : 0f;
            SetRailWidth(targetWidth);
            _selectionRail.pickingMode = open ? PickingMode.Position : PickingMode.Ignore;
        }

        private void SetRailWidth(float width)
        {
            if (_selectionRail == null) return;

            _currentRailWidth = Mathf.Clamp(width, 0f, PanelOpenWidth);
            _selectionRail.style.width = _currentRailWidth;
            _selectionRail.style.minWidth = _currentRailWidth;
            _selectionRail.style.maxWidth = _currentRailWidth;
        }

        private string GetRulerDisplay(Actor actor, TitleRank rank)
        {
            if (actor == null) return "-";
            string rankLabel;
            switch (rank)
            {
                case TitleRank.Realm: rankLabel = "King"; break;
                case TitleRank.Province: rankLabel = "Duke"; break;
                case TitleRank.County: rankLabel = "Count"; break;
                case TitleRank.Archdiocese: rankLabel = "Archbishop"; break;
                case TitleRank.Diocese: rankLabel = "Bishop"; break;
                case TitleRank.Parish: rankLabel = "Prior"; break;
                default: rankLabel = ""; break;
            }
            return $"{rankLabel} {actor.Name}";
        }

        private string GetCultureName(int realmId)
        {
            if (_mapData == null || _mapData.CultureById == null) return "-";
            if (!TryGetRealm(realmId, out var realm)) return "-";
            if (realm.CultureId <= 0 || !_mapData.CultureById.TryGetValue(realm.CultureId, out var culture)) return "-";
            return culture.Name ?? "-";
        }

        private string GetReligionName(int realmId)
        {
            if (_mapData == null || _mapData.CultureById == null || _mapData.ReligionById == null) return "-";
            if (!TryGetRealm(realmId, out var realm)) return "-";
            if (realm.CultureId <= 0 || !_mapData.CultureById.TryGetValue(realm.CultureId, out var culture)) return "-";
            if (culture.ReligionId <= 0 || !_mapData.ReligionById.TryGetValue(culture.ReligionId, out var religion)) return "-";
            return religion.Name ?? "-";
        }

        private bool TryGetRealm(int realmId, out Realm realm)
        {
            realm = null;
            return _mapData != null
                && _mapData.RealmById != null
                && realmId > 0
                && _mapData.RealmById.TryGetValue(realmId, out realm);
        }

        private bool TryGetProvince(int provinceId, out Province province)
        {
            province = null;
            return _mapData != null
                && _mapData.ProvinceById != null
                && provinceId > 0
                && _mapData.ProvinceById.TryGetValue(provinceId, out province);
        }

        private bool TryGetCounty(int countyId, out County county)
        {
            county = null;
            return _mapData != null
                && _mapData.CountyById != null
                && countyId > 0
                && _mapData.CountyById.TryGetValue(countyId, out county);
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
