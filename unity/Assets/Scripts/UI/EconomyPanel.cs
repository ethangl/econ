using UnityEngine;
using UnityEngine.UIElements;
using EconSim.Core.Economy;
using EconSim.Core.Simulation;
using System.Collections.Generic;

namespace EconSim.UI
{
    /// <summary>
    /// UI Toolkit controller for the global economy panel.
    /// Toggle with E key. Shows overview, production, and trade tabs.
    /// </summary>
    public class EconomyPanel : MonoBehaviour
    {
        private const float RefreshIntervalSeconds = 1.0f;

        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private KeyCode _toggleKey = KeyCode.E;

        private VisualElement _root;
        private VisualElement _panel;
        private Button _closeButton;

        // Tab buttons
        private Button _tabOverview;
        private Button _tabProduction;
        private Button _tabTrade;

        // Tab content (ScrollViews for overflow)
        private ScrollView _contentOverview;
        private ScrollView _contentProduction;
        private ScrollView _contentTrade;

        // Overview labels
        private Label _populationLabel;
        private Label _workersLabel;
        private Label _employedLabel;
        private Label _facilitiesLabel;
        private Label _marketsLabel;

        // Dynamic lists
        private VisualElement _productionList;
        private VisualElement _tradeList;

        private EconomyState _economy;
        private bool _isVisible;
        private int _activeTabIndex;
        private float _nextRefreshTime;

        private void Start()
        {
            if (EconSim.Core.GameManager.IsMapReady)
                StartCoroutine(Initialize());
            else
                EconSim.Core.GameManager.OnMapReady += OnMapReadyHandler;
        }

        private void OnDestroy()
        {
            EconSim.Core.GameManager.OnMapReady -= OnMapReadyHandler;
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
                var sim = gameManager.Simulation;
                if (sim != null)
                {
                    _economy = sim.GetState().Economy;
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
                Debug.LogError("EconomyPanel: No UIDocument found");
                return;
            }

            _root = _uiDocument.rootVisualElement;

            // Query panel and close button
            _panel = _root.Q<VisualElement>("economy-panel");
            _closeButton = _root.Q<Button>("economy-close-button");

            // Tab buttons
            _tabOverview = _root.Q<Button>("tab-overview");
            _tabProduction = _root.Q<Button>("tab-production");
            _tabTrade = _root.Q<Button>("tab-trade");

            // Tab content
            _contentOverview = _root.Q<ScrollView>("tab-content-overview");
            _contentProduction = _root.Q<ScrollView>("tab-content-production");
            _contentTrade = _root.Q<ScrollView>("tab-content-trade");

            // Overview labels
            _populationLabel = _root.Q<Label>("econ-population");
            _workersLabel = _root.Q<Label>("econ-workers");
            _employedLabel = _root.Q<Label>("econ-employed");
            _facilitiesLabel = _root.Q<Label>("econ-facilities");
            _marketsLabel = _root.Q<Label>("econ-markets");

            // Dynamic lists
            _productionList = _root.Q<VisualElement>("production-list");
            _tradeList = _root.Q<VisualElement>("trade-list");

            // Wire up events
            _closeButton?.RegisterCallback<ClickEvent>(evt => Hide());
            _tabOverview?.RegisterCallback<ClickEvent>(evt => SelectTab(0));
            _tabProduction?.RegisterCallback<ClickEvent>(evt => SelectTab(1));
            _tabTrade?.RegisterCallback<ClickEvent>(evt => SelectTab(2));

            // Start hidden
            _isVisible = false;
            _activeTabIndex = 0;
        }

        private void Update()
        {
            if (StartupScreenPanel.IsOpen) return;

            // Toggle with hotkey
            if (Input.GetKeyDown(_toggleKey))
            {
                if (_isVisible)
                    Hide();
                else
                    Show();
            }

            // Escape closes
            if (Input.GetKeyDown(KeyCode.Escape) && _isVisible)
            {
                Hide();
            }

            // Refresh data periodically while visible.
            if (_isVisible && Time.unscaledTime >= _nextRefreshTime)
            {
                _nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
                UpdateDisplay();
            }
        }

        public void Show()
        {
            _panel?.RemoveFromClassList("hidden");
            _isVisible = true;
            _nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
            UpdateDisplay();
        }

        public void Hide()
        {
            _panel?.AddToClassList("hidden");
            _isVisible = false;
        }

        private void SelectTab(int index)
        {
            _activeTabIndex = index;

            // Update button states
            SetTabActive(_tabOverview, index == 0);
            SetTabActive(_tabProduction, index == 1);
            SetTabActive(_tabTrade, index == 2);

            // Show/hide content
            SetContentVisible(_contentOverview, index == 0);
            SetContentVisible(_contentProduction, index == 1);
            SetContentVisible(_contentTrade, index == 2);

            UpdateDisplay();
        }

        private void SetTabActive(Button tab, bool active)
        {
            if (tab == null) return;
            if (active)
                tab.AddToClassList("tab-active");
            else
                tab.RemoveFromClassList("tab-active");
        }

        private void SetContentVisible(VisualElement content, bool visible)
        {
            if (content == null) return;
            if (visible)
                content.RemoveFromClassList("hidden");
            else
                content.AddToClassList("hidden");
        }

        private void UpdateDisplay()
        {
            if (_economy == null) return;

            switch (_activeTabIndex)
            {
                case 0:
                    UpdateOverview();
                    break;
                case 1:
                    UpdateProduction();
                    break;
                case 2:
                    UpdateTrade();
                    break;
                default:
                    UpdateOverview();
                    break;
            }
        }

        private void UpdateOverview()
        {
            long totalPop = 0;
            long totalWorkers = 0;
            long totalEmployed = 0;
            int totalFacilities = 0;

            foreach (var county in _economy.Counties.Values)
            {
                totalPop += county.Population.Total;
                totalWorkers += county.Population.LaborEligible;
                totalEmployed += county.Population.EmployedUnskilled + county.Population.EmployedSkilled;
                totalFacilities += county.FacilityIds?.Count ?? 0;
            }

            SetLabel(_populationLabel, FormatNumber(totalPop));
            SetLabel(_workersLabel, FormatNumber(totalWorkers));
            SetLabel(_employedLabel, FormatNumber(totalEmployed));
            SetLabel(_facilitiesLabel, totalFacilities.ToString());
            SetLabel(_marketsLabel, _economy.Markets.Count.ToString());
        }

        private void UpdateProduction()
        {
            if (_productionList == null) return;
            _productionList.Clear();

            // Aggregate production capacity by good type
            var production = new Dictionary<string, float>();

            foreach (var county in _economy.Counties.Values)
            {
                var facilityIds = county.FacilityIds;
                if (facilityIds == null)
                    continue;

                foreach (var facilityId in facilityIds)
                {
                    if (_economy.Facilities.TryGetValue(facilityId, out var facility))
                    {
                        var def = _economy.FacilityDefs.Get(facility.TypeId);
                        if (def?.OutputGoodId != null)
                        {
                            if (!production.ContainsKey(def.OutputGoodId))
                                production[def.OutputGoodId] = 0;
                            // Use current throughput (accounts for staffing)
                            production[def.OutputGoodId] += facility.GetThroughput(def);
                        }
                    }
                }
            }

            if (production.Count == 0)
            {
                AddEmptyLabel(_productionList, "No production data");
                return;
            }

            foreach (var kvp in production)
            {
                var row = CreateStatRow(kvp.Key, $"{kvp.Value:F1}/day");
                _productionList.Add(row);
            }
        }

        private void UpdateTrade()
        {
            if (_tradeList == null) return;
            _tradeList.Clear();

            if (_economy.Markets.Count == 0)
            {
                AddEmptyLabel(_tradeList, "No markets");
                return;
            }

            // Aggregate across legitimate markets only.
            // Off-map prices are intentionally fixed and should not mask local market movement.
            var aggregateGoods = new Dictionary<string, (float supply, float demand, float weightedPriceSum, float priceWeight)>();
            var blackMarket = _economy.BlackMarket;

            foreach (var market in _economy.Markets.Values)
            {
                // Skip non-legitimate markets in aggregate totals.
                if (market.Type != MarketType.Legitimate)
                    continue;

                foreach (var kvp in market.Goods)
                {
                    var state = kvp.Value;
                    if (!aggregateGoods.ContainsKey(kvp.Key))
                        aggregateGoods[kvp.Key] = (0f, 0f, 0f, 0f);

                    var current = aggregateGoods[kvp.Key];
                    float priceWeight = Mathf.Max(1f, state.SupplyOffered + state.Demand);
                    aggregateGoods[kvp.Key] = (
                        current.supply + state.SupplyOffered,
                        current.demand + state.Demand,
                        current.weightedPriceSum + state.Price * priceWeight,
                        current.priceWeight + priceWeight
                    );
                }
            }

            // Legitimate Markets Section
            AddSectionHeader(_tradeList, "Legitimate Markets");

            // Header
            var header = new VisualElement();
            header.AddToClassList("goods-header");
            header.Add(CreateLabel("Good", "goods-name"));
            header.Add(CreateLabel("Supply", "goods-value"));
            header.Add(CreateLabel("Demand", "goods-value"));
            header.Add(CreateLabel("Price", "goods-value"));
            _tradeList.Add(header);

            // Rows
            int legitRows = 0;
            foreach (var kvp in aggregateGoods)
            {
                var (supply, demand, weightedPriceSum, priceWeight) = kvp.Value;
                if (supply < 0.1f && demand < 0.1f) continue;
                float avgPrice = priceWeight > 0f ? weightedPriceSum / priceWeight : 0f;

                var row = new VisualElement();
                row.AddToClassList("goods-row");
                row.Add(CreateLabel(kvp.Key, "goods-name"));
                row.Add(CreateLabel(FormatQuantity(supply), "goods-value"));
                row.Add(CreateLabel(FormatQuantity(demand), "goods-value"));
                row.Add(CreateLabel($"{avgPrice:F2}", "goods-value"));
                _tradeList.Add(row);
                legitRows++;
            }

            if (legitRows == 0)
            {
                AddEmptyLabel(_tradeList, "No trade activity");
            }

            // Black Market Section
            if (blackMarket != null)
            {
                AddSectionHeader(_tradeList, "Black Market", new Color(0.8f, 0.3f, 0.3f));

                // Header
                var blackHeader = new VisualElement();
                blackHeader.AddToClassList("goods-header");
                blackHeader.Add(CreateLabel("Good", "goods-name"));
                blackHeader.Add(CreateLabel("Stock", "goods-value"));
                blackHeader.Add(CreateLabel("Sold", "goods-value"));
                blackHeader.Add(CreateLabel("Price", "goods-value"));
                _tradeList.Add(blackHeader);

                // Rows - show goods with any supply or recent activity
                int blackRows = 0;
                foreach (var kvp in blackMarket.Goods)
                {
                    var state = kvp.Value;
                    // Show if there's stock or recent sales
                    if (state.Supply < 0.1f && state.LastTradeVolume < 0.1f) continue;

                    var row = new VisualElement();
                    row.AddToClassList("goods-row");
                    row.Add(CreateLabel(kvp.Key, "goods-name"));
                    row.Add(CreateLabel(FormatQuantity(state.Supply), "goods-value"));
                    row.Add(CreateLabel(FormatQuantity(state.LastTradeVolume), "goods-value"));
                    row.Add(CreateLabel($"{state.Price:F2}", "goods-value"));
                    _tradeList.Add(row);
                    blackRows++;
                }

                if (blackRows == 0)
                {
                    AddEmptyLabel(_tradeList, "No stolen goods in circulation");
                }
            }
        }

        private void AddSectionHeader(VisualElement container, string text, Color? color = null)
        {
            var header = new Label(text);
            header.style.fontSize = 12;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color = color ?? new Color(0.7f, 0.7f, 0.8f);
            header.style.marginTop = 8;
            header.style.marginBottom = 4;
            container.Add(header);
        }

        private VisualElement CreateStatRow(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("stat-row");

            var labelEl = new Label(label);
            var valueEl = new Label(value);
            valueEl.style.color = new Color(0.86f, 0.86f, 0.9f);
            valueEl.style.unityFontStyleAndWeight = FontStyle.Bold;

            row.Add(labelEl);
            row.Add(valueEl);
            return row;
        }

        private Label CreateLabel(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }

        private void AddEmptyLabel(VisualElement container, string text)
        {
            var label = new Label(text);
            label.style.color = new Color(0.5f, 0.5f, 0.5f);
            label.style.fontSize = 11;
            container.Add(label);
        }

        private void SetLabel(Label label, string text)
        {
            if (label != null) label.text = text;
        }

        private string FormatNumber(long value)
        {
            if (value >= 1000000) return $"{value / 1000000f:F1}M";
            if (value >= 1000) return $"{value / 1000f:F1}k";
            return value.ToString();
        }

        private string FormatQuantity(float value)
        {
            if (value >= 1000) return $"{value / 1000:F1}k";
            if (value >= 100) return $"{value:F0}";
            return $"{value:F1}";
        }
    }
}
