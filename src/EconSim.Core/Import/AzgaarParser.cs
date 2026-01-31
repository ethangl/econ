using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EconSim.Core.Import
{
    /// <summary>
    /// Parses Azgaar Fantasy Map Generator JSON exports into AzgaarMap data structures.
    /// Uses Newtonsoft.Json for robust parsing of complex nested structures.
    /// </summary>
    public static class AzgaarParser
    {
        /// <summary>
        /// Parse an Azgaar JSON file from a file path.
        /// </summary>
        public static AzgaarMap ParseFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Azgaar map file not found: {filePath}");
            }

            string json = File.ReadAllText(filePath);
            return Parse(json);
        }

        /// <summary>
        /// Parse Azgaar JSON string into AzgaarMap.
        /// </summary>
        public static AzgaarMap Parse(string json)
        {
            var root = JObject.Parse(json);
            var map = new AzgaarMap();

            // Parse info
            map.info = ParseInfo(root["info"]);

            // Parse settings
            map.settings = ParseSettings(root["settings"]);

            // Parse map coordinates
            map.mapCoordinates = ParseMapCoordinates(root["mapCoordinates"]);

            // Parse biomes data
            map.biomesData = ParseBiomesData(root["biomesData"]);

            // Parse grid (partial - we mainly need features)
            map.grid = ParseGrid(root["grid"]);

            // Parse pack (main map data)
            map.pack = ParsePack(root["pack"]);

            return map;
        }

        private static AzgaarInfo ParseInfo(JToken token)
        {
            if (token == null) return new AzgaarInfo();

            return new AzgaarInfo
            {
                version = token["version"]?.ToString(),
                description = token["description"]?.ToString(),
                exportedAt = token["exportedAt"]?.ToString(),
                mapName = token["mapName"]?.ToString(),
                width = token["width"]?.Value<int>() ?? 0,
                height = token["height"]?.Value<int>() ?? 0,
                seed = token["seed"]?.ToString(),
                mapId = token["mapId"]?.Value<long>() ?? 0
            };
        }

        private static AzgaarSettings ParseSettings(JToken token)
        {
            if (token == null) return new AzgaarSettings();

            return new AzgaarSettings
            {
                distanceUnit = token["distanceUnit"]?.ToString(),
                distanceScale = token["distanceScale"]?.Value<float>() ?? 1f,
                areaUnit = token["areaUnit"]?.ToString(),
                heightUnit = token["heightUnit"]?.ToString(),
                heightExponent = token["heightExponent"]?.ToString(),
                temperatureScale = token["temperatureScale"]?.ToString(),
                populationRate = token["populationRate"]?.Value<int>() ?? 1000,
                urbanization = token["urbanization"]?.Value<int>() ?? 1
            };
        }

        private static AzgaarMapCoordinates ParseMapCoordinates(JToken token)
        {
            if (token == null) return new AzgaarMapCoordinates();

            return new AzgaarMapCoordinates
            {
                latT = token["latT"]?.Value<float>() ?? 0,
                latN = token["latN"]?.Value<float>() ?? 0,
                latS = token["latS"]?.Value<float>() ?? 0,
                lonT = token["lonT"]?.Value<float>() ?? 0,
                lonW = token["lonW"]?.Value<float>() ?? 0,
                lonE = token["lonE"]?.Value<float>() ?? 0
            };
        }

        private static AzgaarBiomesData ParseBiomesData(JToken token)
        {
            if (token == null) return new AzgaarBiomesData();

            return new AzgaarBiomesData
            {
                i = token["i"]?.ToObject<List<int>>() ?? new List<int>(),
                name = token["name"]?.ToObject<List<string>>() ?? new List<string>(),
                color = token["color"]?.ToObject<List<string>>() ?? new List<string>(),
                habitability = token["habitability"]?.ToObject<List<int>>() ?? new List<int>(),
                iconsDensity = token["iconsDensity"]?.ToObject<List<int>>() ?? new List<int>(),
                cost = token["cost"]?.ToObject<List<int>>() ?? new List<int>()
            };
        }

        private static AzgaarGrid ParseGrid(JToken token)
        {
            if (token == null) return new AzgaarGrid();

            var grid = new AzgaarGrid
            {
                cellsDesired = token["cellsDesired"]?.Value<int>() ?? 0,
                spacing = token["spacing"]?.Value<float>() ?? 0,
                cellsY = token["cellsY"]?.Value<int>() ?? 0,
                cellsX = token["cellsX"]?.Value<int>() ?? 0,
                seed = token["seed"]?.ToString()
            };

            // Parse grid features (array may contain nulls or non-objects)
            var featuresToken = token["features"] as JArray;
            grid.features = new List<AzgaarGridFeature>();
            if (featuresToken != null)
            {
                foreach (var ft in featuresToken)
                {
                    if (ft.Type != JTokenType.Object) continue;
                    grid.features.Add(new AzgaarGridFeature
                    {
                        i = ft["i"]?.Value<int>() ?? 0,
                        land = ft["land"]?.Value<int>() ?? 0,
                        border = ft["border"]?.Value<int>() ?? 0,
                        type = ft["type"]?.ToString()
                    });
                }
            }

            return grid;
        }

        private static AzgaarPack ParsePack(JToken token)
        {
            if (token == null) return new AzgaarPack();

            var pack = new AzgaarPack();

            // Parse cells
            pack.cells = ParseCells(token["cells"] as JArray);

            // Parse vertices
            pack.vertices = ParseVertices(token["vertices"] as JArray);

            // Parse features
            pack.features = ParseFeatures(token["features"] as JArray);

            // Parse cultures
            pack.cultures = ParseCultures(token["cultures"] as JArray);

            // Parse burgs
            pack.burgs = ParseBurgs(token["burgs"] as JArray);

            // Parse states
            pack.states = ParseStates(token["states"] as JArray);

            // Parse provinces
            pack.provinces = ParseProvinces(token["provinces"] as JArray);

            // Parse religions
            pack.religions = ParseReligions(token["religions"] as JArray);

            // Parse rivers
            pack.rivers = ParseRivers(token["rivers"] as JArray);

            // Parse markers
            pack.markers = ParseMarkers(token["markers"] as JArray);

            // Parse routes
            pack.routes = ParseRoutes(token["routes"] as JArray);

            return pack;
        }

        private static List<AzgaarCell> ParseCells(JArray array)
        {
            var cells = new List<AzgaarCell>();
            if (array == null) return cells;

            foreach (var item in array)
            {
                cells.Add(new AzgaarCell
                {
                    i = item["i"]?.Value<int>() ?? 0,
                    v = item["v"]?.ToObject<List<int>>() ?? new List<int>(),
                    c = item["c"]?.ToObject<List<int>>() ?? new List<int>(),
                    p = item["p"]?.ToObject<List<float>>() ?? new List<float> { 0, 0 },
                    g = item["g"]?.Value<int>() ?? 0,
                    h = item["h"]?.Value<int>() ?? 0,
                    area = item["area"]?.Value<int>() ?? 0,
                    f = item["f"]?.Value<int>() ?? 0,
                    t = item["t"]?.Value<int>() ?? 0,
                    haven = item["haven"]?.Value<int>() ?? 0,
                    harbor = item["harbor"]?.Value<int>() ?? 0,
                    fl = item["fl"]?.Value<int>() ?? 0,
                    r = item["r"]?.Value<int>() ?? 0,
                    conf = item["conf"]?.Value<int>() ?? 0,
                    biome = item["biome"]?.Value<int>() ?? 0,
                    s = item["s"]?.Value<int>() ?? 0,
                    pop = item["pop"]?.Value<float>() ?? 0,
                    culture = item["culture"]?.Value<int>() ?? 0,
                    burg = item["burg"]?.Value<int>() ?? 0,
                    state = item["state"]?.Value<int>() ?? 0,
                    religion = item["religion"]?.Value<int>() ?? 0,
                    province = item["province"]?.Value<int>() ?? 0
                });
            }

            return cells;
        }

        private static List<AzgaarVertex> ParseVertices(JArray array)
        {
            var vertices = new List<AzgaarVertex>();
            if (array == null) return vertices;

            foreach (var item in array)
            {
                vertices.Add(new AzgaarVertex
                {
                    i = item["i"]?.Value<int>() ?? 0,
                    p = item["p"]?.ToObject<List<float>>() ?? new List<float> { 0, 0 },
                    v = item["v"]?.ToObject<List<int>>() ?? new List<int>(),
                    c = item["c"]?.ToObject<List<int>>() ?? new List<int>()
                });
            }

            return vertices;
        }

        private static List<AzgaarFeature> ParseFeatures(JArray array)
        {
            var features = new List<AzgaarFeature>();
            if (array == null) return features;

            foreach (var item in array)
            {
                if (item.Type != JTokenType.Object) continue;
                features.Add(new AzgaarFeature
                {
                    i = SafeGetInt(item["i"]),
                    type = item["type"]?.ToString(),
                    cells = ParseIntOrIntList(item["cells"]),
                    firstCell = SafeGetInt(item["firstCell"]),
                    border = SafeGetBool(item["border"]),
                    vertices = SafeGetInt(item["vertices"])
                });
            }

            return features;
        }

        /// <summary>
        /// Safely get an int from a token that might be an array or other type.
        /// </summary>
        private static int SafeGetInt(JToken token, int defaultValue = 0)
        {
            if (token == null) return defaultValue;
            if (token.Type == JTokenType.Integer) return token.Value<int>();
            if (token.Type == JTokenType.Float) return (int)token.Value<float>();
            return defaultValue;
        }

        /// <summary>
        /// Safely get a bool from a token.
        /// </summary>
        private static bool SafeGetBool(JToken token, bool defaultValue = false)
        {
            if (token == null) return defaultValue;
            if (token.Type == JTokenType.Boolean) return token.Value<bool>();
            if (token.Type == JTokenType.Integer) return token.Value<int>() != 0;
            return defaultValue;
        }

        /// <summary>
        /// Handle fields that can be either a single int or an array of ints.
        /// </summary>
        private static List<int> ParseIntOrIntList(JToken token)
        {
            if (token == null) return null;

            if (token.Type == JTokenType.Array)
            {
                return token.ToObject<List<int>>();
            }
            else if (token.Type == JTokenType.Integer)
            {
                return new List<int> { token.Value<int>() };
            }

            return null;
        }

        private static List<AzgaarCulture> ParseCultures(JArray array)
        {
            var cultures = new List<AzgaarCulture>();
            if (array == null) return cultures;

            foreach (var item in array)
            {
                if (item.Type != JTokenType.Object) continue;
                cultures.Add(new AzgaarCulture
                {
                    i = item["i"]?.Value<int>() ?? 0,
                    name = item["name"]?.ToString(),
                    @base = item["base"]?.ToString(),
                    origin = item["origin"]?.Value<int>() ?? 0,
                    shield = item["shield"]?.ToString(),
                    center = item["center"]?.Value<int>() ?? 0,
                    color = item["color"]?.ToString(),
                    type = item["type"]?.ToString(),
                    expansionism = item["expansionism"]?.Value<float>() ?? 1f,
                    code = item["code"]?.ToString()
                });
            }

            return cultures;
        }

        private static List<AzgaarBurg> ParseBurgs(JArray array)
        {
            var burgs = new List<AzgaarBurg>();
            if (array == null) return burgs;

            foreach (var item in array)
            {
                if (item.Type != JTokenType.Object) continue;
                burgs.Add(new AzgaarBurg
                {
                    i = item["i"]?.Value<int>() ?? 0,
                    cell = item["cell"]?.Value<int>() ?? 0,
                    x = item["x"]?.Value<float>() ?? 0,
                    y = item["y"]?.Value<float>() ?? 0,
                    state = item["state"]?.Value<int>() ?? 0,
                    culture = item["culture"]?.Value<int>() ?? 0,
                    name = item["name"]?.ToString(),
                    feature = item["feature"]?.Value<int>() ?? 0,
                    capital = item["capital"]?.Value<int>() ?? 0,
                    port = item["port"]?.Value<int>() ?? 0,
                    population = item["population"]?.Value<float>() ?? 0,
                    type = item["type"]?.ToString(),
                    citadel = item["citadel"]?.Value<int>() ?? 0,
                    plaza = item["plaza"]?.Value<int>() ?? 0,
                    walls = item["walls"]?.Value<int>() ?? 0,
                    shanty = item["shanty"]?.Value<int>() ?? 0,
                    temple = item["temple"]?.Value<int>() ?? 0,
                    group = item["group"]?.ToString()
                });
            }

            return burgs;
        }

        private static List<AzgaarState> ParseStates(JArray array)
        {
            var states = new List<AzgaarState>();
            if (array == null) return states;

            foreach (var item in array)
            {
                if (item.Type != JTokenType.Object) continue;
                states.Add(new AzgaarState
                {
                    i = item["i"]?.Value<int>() ?? 0,
                    name = item["name"]?.ToString(),
                    expansionism = item["expansionism"]?.Value<float>() ?? 1f,
                    capital = item["capital"]?.Value<int>() ?? 0,
                    type = item["type"]?.ToString(),
                    center = item["center"]?.Value<int>() ?? 0,
                    culture = item["culture"]?.Value<int>() ?? 0,
                    pole = item["pole"]?.ToObject<List<float>>(),
                    neighbors = item["neighbors"]?.ToObject<List<int>>() ?? new List<int>(),
                    color = item["color"]?.ToString(),
                    urban = item["urban"]?.Value<float>() ?? 0,
                    rural = item["rural"]?.Value<float>() ?? 0,
                    burgs = item["burgs"]?.Value<int>() ?? 0,
                    area = item["area"]?.Value<int>() ?? 0,
                    cells = item["cells"]?.Value<int>() ?? 0,
                    form = item["form"]?.ToString(),
                    formName = item["formName"]?.ToString(),
                    fullName = item["fullName"]?.ToString(),
                    provinces = item["provinces"]?.ToObject<List<int>>() ?? new List<int>()
                });
            }

            return states;
        }

        private static List<AzgaarProvince> ParseProvinces(JArray array)
        {
            var provinces = new List<AzgaarProvince>();
            if (array == null) return provinces;

            foreach (var item in array)
            {
                if (item.Type != JTokenType.Object) continue;
                provinces.Add(new AzgaarProvince
                {
                    i = item["i"]?.Value<int>() ?? 0,
                    state = item["state"]?.Value<int>() ?? 0,
                    center = item["center"]?.Value<int>() ?? 0,
                    burg = item["burg"]?.Value<int>() ?? 0,
                    name = item["name"]?.ToString(),
                    formName = item["formName"]?.ToString(),
                    fullName = item["fullName"]?.ToString(),
                    color = item["color"]?.ToString(),
                    pole = item["pole"]?.ToObject<List<float>>()
                });
            }

            return provinces;
        }

        private static List<AzgaarReligion> ParseReligions(JArray array)
        {
            var religions = new List<AzgaarReligion>();
            if (array == null) return religions;

            foreach (var item in array)
            {
                if (item.Type != JTokenType.Object) continue;
                religions.Add(new AzgaarReligion
                {
                    i = item["i"]?.Value<int>() ?? 0,
                    name = item["name"]?.ToString(),
                    color = item["color"]?.ToString(),
                    type = item["type"]?.ToString(),
                    form = item["form"]?.ToString(),
                    deity = item["deity"]?.ToString(),
                    culture = item["culture"]?.Value<int>() ?? 0,
                    center = item["center"]?.Value<int>() ?? 0,
                    origin = item["origin"]?.Value<int>() ?? 0,
                    code = item["code"]?.ToString()
                });
            }

            return religions;
        }

        private static List<AzgaarRiver> ParseRivers(JArray array)
        {
            var rivers = new List<AzgaarRiver>();
            if (array == null) return rivers;

            foreach (var item in array)
            {
                if (item.Type != JTokenType.Object) continue;
                rivers.Add(new AzgaarRiver
                {
                    i = item["i"]?.Value<int>() ?? 0,
                    source = item["source"]?.Value<int>() ?? 0,
                    mouth = item["mouth"]?.Value<int>() ?? 0,
                    discharge = item["discharge"]?.Value<int>() ?? 0,
                    length = item["length"]?.Value<float>() ?? 0,
                    width = item["width"]?.Value<float>() ?? 0,
                    widthFactor = item["widthFactor"]?.Value<float>() ?? 1f,
                    sourceWidth = item["sourceWidth"]?.Value<float>() ?? 0,
                    parent = item["parent"]?.Value<int>() ?? 0,
                    cells = item["cells"]?.ToObject<List<int>>() ?? new List<int>(),
                    basin = item["basin"]?.Value<int>() ?? 0,
                    name = item["name"]?.ToString(),
                    type = item["type"]?.ToString()
                });
            }

            return rivers;
        }

        private static List<AzgaarMarker> ParseMarkers(JArray array)
        {
            var markers = new List<AzgaarMarker>();
            if (array == null) return markers;

            foreach (var item in array)
            {
                if (item.Type != JTokenType.Object) continue;
                markers.Add(new AzgaarMarker
                {
                    i = item["i"]?.Value<int>() ?? 0,
                    icon = item["icon"]?.ToString(),
                    type = item["type"]?.ToString(),
                    x = item["x"]?.Value<float>() ?? 0,
                    y = item["y"]?.Value<float>() ?? 0,
                    cell = item["cell"]?.Value<int>() ?? 0,
                    note = item["note"]?.ToString()
                });
            }

            return markers;
        }

        private static List<AzgaarRoute> ParseRoutes(JArray array)
        {
            var routes = new List<AzgaarRoute>();
            if (array == null) return routes;

            foreach (var item in array)
            {
                if (item.Type != JTokenType.Object) continue;
                routes.Add(new AzgaarRoute
                {
                    i = SafeGetInt(item["i"]),
                    points = new List<int>(), // Skip parsing - points are nested arrays, not needed for map rendering
                    feature = SafeGetInt(item["feature"]),
                    group = item["group"]?.ToString()
                });
            }

            return routes;
        }
    }
}
