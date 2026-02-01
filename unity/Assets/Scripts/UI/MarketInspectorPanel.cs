using UnityEngine;
using UnityEngine.UIElements;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Core.Simulation;
using EconSim.Renderer;

namespace EconSim.UI
{
    /// <summary>
    /// UI Toolkit controller for the market inspection panel.
    /// Shows detailed info about a market when clicking on a market hub in market mode.
    /// </summary>
    public class MarketInspectorPanel : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private StyleSheet _styleSheet;
        [SerializeField] private MapView _mapView;

        private VisualElement _root;
        private VisualElement _panel;

        // Cached element references
        private Button _closeButton;
        private Label _marketName;
        private Label _hubValue;
        private Label _zoneValue;
        private VisualElement _goodsList;

        private MapData _mapData;
        private EconomyState _economy;
        private Market _selectedMarket;

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
            // Only respond in market mode
            if (_mapView == null || _mapView.CurrentMode != MapView.MapMode.Market)
            {
                Hide();
                return;
            }

            // Check if clicked cell is a market hub
            var market = _mapView.GetMarketAtCell(cellId);
            if (market != null)
            {
                SelectMarket(market);
            }
            else
            {
                Hide();
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
                Debug.LogError("MarketInspectorPanel: No UIDocument found");
                return;
            }

            _root = _uiDocument.rootVisualElement;

            if (_styleSheet != null)
            {
                _root.styleSheets.Add(_styleSheet);
            }

            // Query elements
            _panel = _root.Q<VisualElement>("market-panel");
            _closeButton = _root.Q<Button>("market-close-button");
            _marketName = _root.Q<Label>("market-name");
            _hubValue = _root.Q<Label>("market-hub-value");
            _zoneValue = _root.Q<Label>("market-zone-value");
            _goodsList = _root.Q<VisualElement>("market-goods-list");

            // Wire up close button
            _closeButton?.RegisterCallback<ClickEvent>(evt => Hide());

            // Start hidden
            if (_panel != null)
            {
                _panel.AddToClassList("hidden");
            }
        }

        public void SelectMarket(Market market)
        {
            _selectedMarket = market;

            if (market == null)
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
            _selectedMarket = null;
        }

        private void Update()
        {
            // Escape key closes the panel
            if (Input.GetKeyDown(KeyCode.Escape) && _selectedMarket != null)
            {
                Hide();
                return;
            }

            // Hide if map mode changes away from market
            if (_mapView != null && _mapView.CurrentMode != MapView.MapMode.Market && _selectedMarket != null)
            {
                Hide();
                return;
            }

            // Refresh display periodically if something is selected
            if (_selectedMarket != null && Time.frameCount % 30 == 0)
            {
                UpdateDisplay();
            }
        }

        private void UpdateDisplay()
        {
            if (_selectedMarket == null || _mapData == null) return;

            // Market name
            SetLabel(_marketName, _selectedMarket.Name ?? $"Market {_selectedMarket.Id}");

            // Hub location (burg name or cell ID)
            string hubName = $"Cell {_selectedMarket.LocationCellId}";
            if (_mapData.CellById != null && _mapData.CellById.TryGetValue(_selectedMarket.LocationCellId, out var cell))
            {
                if (cell.BurgId > 0 && cell.BurgId < _mapData.Burgs.Count)
                {
                    var burg = _mapData.Burgs[cell.BurgId];
                    if (!string.IsNullOrEmpty(burg.Name))
                    {
                        hubName = burg.Name;
                    }
                }
            }
            SetLabel(_hubValue, hubName);

            // Zone size
            int zoneSize = _selectedMarket.ZoneCellIds?.Count ?? 0;
            SetLabel(_zoneValue, $"{zoneSize} counties");

            // Goods
            UpdateGoodsList();
        }

        private void UpdateGoodsList()
        {
            if (_goodsList == null) return;

            _goodsList.Clear();

            if (_selectedMarket.Goods == null || _selectedMarket.Goods.Count == 0)
            {
                var noneLabel = new Label("No goods traded");
                noneLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                noneLabel.style.fontSize = 11;
                _goodsList.Add(noneLabel);
                return;
            }

            // Add header row
            var header = new VisualElement();
            header.AddToClassList("goods-header");
            header.Add(CreateLabel("Good", "goods-name"));
            header.Add(CreateLabel("Supply", "goods-value"));
            header.Add(CreateLabel("Demand", "goods-value"));
            header.Add(CreateLabel("Price", "goods-value"));
            _goodsList.Add(header);

            // Add each good
            foreach (var kvp in _selectedMarket.Goods)
            {
                var state = kvp.Value;
                if (state.SupplyOffered < 0.1f && state.Demand < 0.1f && state.LastTradeVolume < 0.1f)
                    continue;  // Skip goods with no activity

                var row = new VisualElement();
                row.AddToClassList("goods-row");

                row.Add(CreateLabel(kvp.Key, "goods-name"));
                row.Add(CreateLabel(FormatQuantity(state.SupplyOffered), "goods-value"));
                row.Add(CreateLabel(FormatQuantity(state.Demand), "goods-value"));
                row.Add(CreateLabel($"{state.Price:F2}", "goods-value"));

                _goodsList.Add(row);
            }

            if (_goodsList.childCount <= 1)  // Only header
            {
                var noneLabel = new Label("No goods traded");
                noneLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                noneLabel.style.fontSize = 11;
                _goodsList.Add(noneLabel);
            }
        }

        private Label CreateLabel(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }

        private string FormatQuantity(float value)
        {
            if (value >= 1000) return $"{value / 1000:F1}k";
            if (value >= 100) return $"{value:F0}";
            return $"{value:F1}";
        }

        private void SetLabel(Label label, string text)
        {
            if (label != null) label.text = text;
        }
    }
}
