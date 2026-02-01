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
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private KeyCode _toggleKey = KeyCode.E;

        private VisualElement _root;
        private VisualElement _panel;
        private Button _closeButton;

        // Tab buttons
        private Button _tabOverview;
        private Button _tabProduction;
        private Button _tabTrade;

        // Tab content
        private VisualElement _contentOverview;
        private VisualElement _contentProduction;
        private VisualElement _contentTrade;

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
            _contentOverview = _root.Q<VisualElement>("tab-content-overview");
            _contentProduction = _root.Q<VisualElement>("tab-content-production");
            _contentTrade = _root.Q<VisualElement>("tab-content-trade");

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
        }

        private void Update()
        {
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

            // Refresh data periodically
            if (_isVisible && Time.frameCount % 30 == 0)
            {
                UpdateDisplay();
            }
        }

        public void Show()
        {
            _panel?.RemoveFromClassList("hidden");
            _isVisible = true;
            UpdateDisplay();
        }

        public void Hide()
        {
            _panel?.AddToClassList("hidden");
            _isVisible = false;
        }

        private void SelectTab(int index)
        {
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

            UpdateOverview();
            UpdateProduction();
            UpdateTrade();
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
                totalWorkers += county.Population.WorkingAge;
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
                foreach (var facilityId in county.FacilityIds ?? new List<int>())
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

            // Aggregate across all markets
            var aggregateGoods = new Dictionary<string, (float supply, float demand, float price)>();

            foreach (var market in _economy.Markets.Values)
            {
                foreach (var kvp in market.Goods)
                {
                    var state = kvp.Value;
                    if (!aggregateGoods.ContainsKey(kvp.Key))
                        aggregateGoods[kvp.Key] = (0, 0, state.Price);

                    var current = aggregateGoods[kvp.Key];
                    aggregateGoods[kvp.Key] = (
                        current.supply + state.SupplyOffered,
                        current.demand + state.Demand,
                        state.Price  // Use last market's price for now
                    );
                }
            }

            // Header
            var header = new VisualElement();
            header.AddToClassList("goods-header");
            header.Add(CreateLabel("Good", "goods-name"));
            header.Add(CreateLabel("Supply", "goods-value"));
            header.Add(CreateLabel("Demand", "goods-value"));
            header.Add(CreateLabel("Price", "goods-value"));
            _tradeList.Add(header);

            // Rows
            foreach (var kvp in aggregateGoods)
            {
                var (supply, demand, price) = kvp.Value;
                if (supply < 0.1f && demand < 0.1f) continue;

                var row = new VisualElement();
                row.AddToClassList("goods-row");
                row.Add(CreateLabel(kvp.Key, "goods-name"));
                row.Add(CreateLabel(FormatQuantity(supply), "goods-value"));
                row.Add(CreateLabel(FormatQuantity(demand), "goods-value"));
                row.Add(CreateLabel($"{price:F2}", "goods-value"));
                _tradeList.Add(row);
            }

            if (_tradeList.childCount <= 1)
            {
                AddEmptyLabel(_tradeList, "No trade activity");
            }
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
