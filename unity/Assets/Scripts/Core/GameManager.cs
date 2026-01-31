using UnityEngine;
using EconSim.Core.Import;
using EconSim.Core.Data;
using EconSim.Renderer;

namespace EconSim.Core
{
    /// <summary>
    /// Main entry point. Initializes map loading and wires together simulation and rendering.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("Map Settings")]
        [SerializeField] private string mapFileName = "1234_low-island_40k_1440x810.json";
        [SerializeField] private bool loadFromResources = false;

        [Header("References")]
        [SerializeField] private MapView mapView;

        public MapData MapData { get; private set; }

        public static GameManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            LoadMap();
        }

        private void LoadMap()
        {
            Debug.Log($"Loading map: {mapFileName}");

            AzgaarMap azgaarMap;

            if (loadFromResources)
            {
                // Load from Resources/Maps folder
                string resourcePath = $"Maps/{System.IO.Path.GetFileNameWithoutExtension(mapFileName)}";
                var textAsset = Resources.Load<TextAsset>(resourcePath);

                if (textAsset == null)
                {
                    Debug.LogError($"Failed to load map from Resources: {resourcePath}");
                    Debug.Log("Trying to load from file path...");
                    LoadMapFromFile();
                    return;
                }

                azgaarMap = AzgaarParser.Parse(textAsset.text);
            }
            else
            {
                LoadMapFromFile();
                return;
            }

            ConvertAndInitialize(azgaarMap);
        }

        private void LoadMapFromFile()
        {
            // Application.dataPath is unity/Assets, so go up two levels to reach project root
            string filePath = System.IO.Path.Combine(Application.dataPath, "..", "..", "reference", mapFileName);

            if (!System.IO.File.Exists(filePath))
            {
                Debug.LogError($"Map file not found: {filePath}");
                return;
            }

            var azgaarMap = AzgaarParser.ParseFile(filePath);
            ConvertAndInitialize(azgaarMap);
        }

        private void ConvertAndInitialize(AzgaarMap azgaarMap)
        {
            Debug.Log($"Parsed map: {azgaarMap.info.mapName}");
            Debug.Log($"  Dimensions: {azgaarMap.info.width}x{azgaarMap.info.height}");
            Debug.Log($"  Cells: {azgaarMap.pack.cells.Count}");
            Debug.Log($"  States: {azgaarMap.pack.states.Count}");
            Debug.Log($"  Provinces: {azgaarMap.pack.provinces.Count}");
            Debug.Log($"  Rivers: {azgaarMap.pack.rivers.Count}");
            Debug.Log($"  Burgs: {azgaarMap.pack.burgs.Count}");

            // Convert to simulation data
            MapData = MapConverter.Convert(azgaarMap);

            Debug.Log($"Converted map: {MapData.Info.Name}");
            Debug.Log($"  Land cells: {MapData.Info.LandCells} / {MapData.Info.TotalCells}");
            Debug.Log($"  States: {MapData.States.Count}");
            Debug.Log($"  Provinces: {MapData.Provinces.Count}");

            // Initialize renderer
            if (mapView != null)
            {
                mapView.Initialize(MapData);
            }
            else
            {
                Debug.LogWarning("MapView not assigned to GameManager");
            }
        }
    }
}
