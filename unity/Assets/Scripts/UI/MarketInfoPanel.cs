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
    /// UI Toolkit controller for the market inspection panel (right rail).
    /// Appears when a market is selected in Market or MarketAccess mode.
    /// </summary>
    public class MarketInfoPanel : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private StyleSheet _styleSheet;
        [SerializeField] private MapView _mapView;

        private VisualElement _root;
        private VisualElement _panel;
        private VisualElement _marketRail;

        private Button _closeButton;
        private Label _titleLabel;
        private Label _hubValue;
        private Label _realmValue;
        private Label _countiesValue;
        private Label _popTotal;
        private VisualElement _goodsSection;
        private VisualElement _goodsList;

        private MapData _mapData;
        private ISimulation _simulation;
        private Coroutine _panelAnimationCoroutine;
        private float _currentRailWidth;

        private int _selectedMarketId = -1;

        private const float PanelOpenWidth = 360f;
        private const float DefaultPanelAnimationDuration = 0.35f;
        private const float MinPanelAnimationDuration = 0.08f;

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
            }

            if (_mapView == null)
                _mapView = FindAnyObjectByType<MapView>();

            if (_mapView != null)
                _mapView.OnSelectionChanged += OnSelectionChanged;

            SetupUI();
        }

        private void OnDestroy()
        {
            EconSim.Core.GameManager.OnMapReady -= OnMapReadyHandler;
            if (_mapView != null)
                _mapView.OnSelectionChanged -= OnSelectionChanged;

            if (_panelAnimationCoroutine != null)
            {
                StopCoroutine(_panelAnimationCoroutine);
                _panelAnimationCoroutine = null;
            }
        }

        private void SetupUI()
        {
            if (_uiDocument == null)
                _uiDocument = GetComponent<UIDocument>();

            if (_uiDocument == null)
            {
                Debug.LogError("MarketInfoPanel: No UIDocument found");
                return;
            }

            _root = _uiDocument.rootVisualElement;

            if (_styleSheet != null)
                _root.styleSheets.Add(_styleSheet);

            _marketRail = _root.Q<VisualElement>("market-rail");
            _panel = _root.Q<VisualElement>("market-panel");
            _closeButton = _root.Q<Button>("market-close-button");
            _titleLabel = _root.Q<Label>("market-title");
            _hubValue = _root.Q<Label>("market-hub-value");
            _realmValue = _root.Q<Label>("market-realm-value");
            _countiesValue = _root.Q<Label>("market-counties-value");
            _popTotal = _root.Q<Label>("market-pop-total");
            _goodsSection = _root.Q<VisualElement>("market-goods-section");
            _goodsList = _root.Q<VisualElement>("market-goods-list");

            _closeButton?.RegisterCallback<ClickEvent>(evt => Hide());

            SetPanelOpenImmediate(false);
        }

        private void OnSelectionChanged(MapView.SelectionScope scope)
        {
            if (_mapView == null) return;

            var mode = _mapView.CurrentMode;
            bool isMarketFamily = mode == MapView.MapMode.Market || mode == MapView.MapMode.MarketAccess;

            if (!isMarketFamily || scope != MapView.SelectionScope.Market)
            {
                Hide();
                return;
            }

            int marketId = _mapView.SelectedMarketId;
            if (marketId <= 0)
            {
                Hide();
                return;
            }

            _selectedMarketId = marketId;
            Show();
            UpdateDisplay();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && _selectedMarketId > 0)
            {
                Hide();
                return;
            }

            if (_selectedMarketId > 0 && Time.frameCount % 30 == 0)
                UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            if (_simulation == null || _mapData == null) return;

            var state = _simulation.GetState();
            var econ = state?.Economy;
            if (econ?.Markets == null || _selectedMarketId <= 0 || _selectedMarketId >= econ.Markets.Length)
                return;

            var market = econ.Markets[_selectedMarketId];

            SetLabel(_titleLabel, $"Market {market.Id}");

            // Hub county name
            string hubName = "-";
            if (_mapData.CountyById != null && _mapData.CountyById.TryGetValue(market.HubCountyId, out var hubCounty))
                hubName = hubCounty.Name ?? $"County {hubCounty.Id}";
            SetLabel(_hubValue, hubName);

            // Realm name
            string realmName = "-";
            if (_mapData.RealmById != null && market.HubRealmId > 0 && _mapData.RealmById.TryGetValue(market.HubRealmId, out var realm))
                realmName = realm.Name ?? $"Realm {realm.Id}";
            SetLabel(_realmValue, realmName);

            // County count in this market zone
            int countyCount = 0;
            if (econ.CountyToMarket != null)
            {
                for (int i = 0; i < econ.CountyToMarket.Length; i++)
                {
                    if (econ.CountyToMarket[i] == _selectedMarketId)
                        countyCount++;
                }
            }
            SetLabel(_countiesValue, countyCount.ToString("N0"));

            // Population - aggregate from counties in this market
            long totalPop = 0;
            if (econ.Counties != null && econ.CountyToMarket != null)
            {
                for (int i = 0; i < econ.Counties.Length; i++)
                {
                    if (econ.Counties[i] == null) continue;
                    if (i < econ.CountyToMarket.Length && econ.CountyToMarket[i] == _selectedMarketId)
                        totalPop += (long)econ.Counties[i].Population;
                }
            }
            SetLabel(_popTotal, totalPop.ToString("N0"));

            // Per-market prices
            if (_goodsList != null)
            {
                _goodsList.Clear();

                float[] localPrices = null;
                if (econ.PerMarketPrices != null && _selectedMarketId < econ.PerMarketPrices.Length)
                    localPrices = econ.PerMarketPrices[_selectedMarketId];

                if (localPrices != null)
                {
                    var goodDefs = EconSim.Core.Economy.Goods.Defs;
                    var basePrices = EconSim.Core.Economy.Goods.BasePrice;
                    for (int g = 0; g < goodDefs.Length; g++)
                    {
                        if (!goodDefs[g].IsTradeable || basePrices[g] <= 0f) continue;
                        float price = localPrices[g];
                        float ratio = price / basePrices[g];
                        string ratioStr = ratio >= 10f ? $"{ratio:F0}x" : $"{ratio:F2}x";

                        var row = new VisualElement();
                        row.style.flexDirection = FlexDirection.Row;
                        row.style.justifyContent = Justify.SpaceBetween;
                        row.style.paddingLeft = 4;
                        row.style.paddingRight = 4;

                        var nameLabel = new Label(goodDefs[g].Name);
                        nameLabel.style.fontSize = 12;
                        nameLabel.style.width = 100;
                        row.Add(nameLabel);

                        var priceLabel = new Label($"{price:F3}");
                        priceLabel.style.fontSize = 12;
                        priceLabel.style.width = 60;
                        priceLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                        row.Add(priceLabel);

                        var ratioLabel = new Label(ratioStr);
                        ratioLabel.style.fontSize = 12;
                        ratioLabel.style.width = 50;
                        ratioLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                        // Color code: green if above base, red if below
                        if (ratio > 1.05f)
                            ratioLabel.style.color = new Color(0.3f, 0.8f, 0.3f);
                        else if (ratio < 0.95f)
                            ratioLabel.style.color = new Color(0.8f, 0.3f, 0.3f);
                        else
                            ratioLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
                        row.Add(ratioLabel);

                        _goodsList.Add(row);
                    }
                }
            }
        }

        public void Show()
        {
            AnimatePanel(true, DefaultPanelAnimationDuration);
        }

        public void Hide()
        {
            AnimatePanel(false, DefaultPanelAnimationDuration);
            _selectedMarketId = -1;
        }

        private void AnimatePanel(bool open, float durationSeconds)
        {
            if (_marketRail == null) return;

            if (_panelAnimationCoroutine != null)
            {
                StopCoroutine(_panelAnimationCoroutine);
                _panelAnimationCoroutine = null;
            }

            float targetWidth = open ? PanelOpenWidth : 0f;
            if (Mathf.Abs(_currentRailWidth - targetWidth) < 0.01f)
            {
                SetRailWidth(targetWidth);
                _marketRail.pickingMode = open ? PickingMode.Position : PickingMode.Ignore;
                return;
            }

            _marketRail.pickingMode = open ? PickingMode.Position : PickingMode.Ignore;
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
            if (_marketRail == null) return;
            float targetWidth = open ? PanelOpenWidth : 0f;
            SetRailWidth(targetWidth);
            _marketRail.pickingMode = open ? PickingMode.Position : PickingMode.Ignore;
        }

        private void SetRailWidth(float width)
        {
            if (_marketRail == null) return;
            _currentRailWidth = Mathf.Clamp(width, 0f, PanelOpenWidth);
            _marketRail.style.width = _currentRailWidth;
            _marketRail.style.minWidth = _currentRailWidth;
            _marketRail.style.maxWidth = _currentRailWidth;
        }

        private void SetLabel(Label label, string text)
        {
            if (label != null) label.text = text;
        }
    }
}
