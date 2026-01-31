using System;
using System.Collections.Generic;
using EconSim.Core.Common;

namespace EconSim.Core.Import
{
    /// <summary>
    /// Data classes for deserializing Azgaar Fantasy Map Generator JSON exports.
    /// These are intermediate representations - convert to simulation data structures after parsing.
    /// </summary>

    [Serializable]
    public class AzgaarMap
    {
        public AzgaarInfo info;
        public AzgaarSettings settings;
        public AzgaarMapCoordinates mapCoordinates;
        public AzgaarPack pack;
        public AzgaarGrid grid;
        public AzgaarBiomesData biomesData;
    }

    [Serializable]
    public class AzgaarInfo
    {
        public string version;
        public string description;
        public string exportedAt;
        public string mapName;
        public int width;
        public int height;
        public string seed;
        public long mapId;
    }

    [Serializable]
    public class AzgaarSettings
    {
        public string distanceUnit;
        public float distanceScale;
        public string areaUnit;
        public string heightUnit;
        public string heightExponent;
        public string temperatureScale;
        public int populationRate;
        public int urbanization;
    }

    [Serializable]
    public class AzgaarMapCoordinates
    {
        public float latT;
        public float latN;
        public float latS;
        public float lonT;
        public float lonW;
        public float lonE;
    }

    [Serializable]
    public class AzgaarPack
    {
        public List<AzgaarCell> cells;
        public List<AzgaarVertex> vertices;
        public List<AzgaarFeature> features;
        public List<AzgaarCulture> cultures;
        public List<AzgaarBurg> burgs;
        public List<AzgaarState> states;
        public List<AzgaarProvince> provinces;
        public List<AzgaarReligion> religions;
        public List<AzgaarRiver> rivers;
        public List<AzgaarMarker> markers;
        public List<AzgaarRoute> routes;
    }

    [Serializable]
    public class AzgaarCell
    {
        public int i;              // Cell index
        public List<int> v;        // Vertex indices (polygon corners)
        public List<int> c;        // Neighbor cell indices
        public List<float> p;      // Position [x, y]
        public int g;              // Grid cell index
        public int h;              // Height (0-100, sea level = 20)
        public int area;           // Cell area
        public int f;              // Feature index (island/lake/continent)
        public int t;              // Distance to coast (+ = land, - = water)
        public int haven;          // Haven cell index
        public int harbor;         // Harbor quality
        public int fl;             // Flux (river flow)
        public int r;              // River index
        public int conf;           // Confluence
        public int biome;          // Biome index
        public int s;              // State (? different from state field)
        public float pop;          // Population
        public int culture;        // Culture index
        public int burg;           // Burg index (0 = none)
        public int state;          // State index
        public int religion;       // Religion index
        public int province;       // Province index

        public Vec2 Position => new Vec2(p[0], p[1]);
        public bool IsLand => h >= 20;
    }

    [Serializable]
    public class AzgaarVertex
    {
        public int i;              // Vertex index
        public List<float> p;      // Position [x, y]
        public List<int> v;        // Adjacent vertex indices
        public List<int> c;        // Adjacent cell indices

        public Vec2 Position => new Vec2(p[0], p[1]);
    }

    [Serializable]
    public class AzgaarFeature
    {
        public int i;
        public string type;        // "ocean", "island", "lake", etc.
        public List<int> cells;
        public int firstCell;
        public bool border;
        public int vertices;
    }

    [Serializable]
    public class AzgaarCulture
    {
        public int i;
        public string name;
        public string @base;       // Base culture type
        public int origin;
        public string shield;
        public int center;
        public string color;
        public string type;
        public float expansionism;
        public string code;
    }

    [Serializable]
    public class AzgaarBurg
    {
        public int i;
        public int cell;
        public float x;
        public float y;
        public int state;
        public int culture;
        public string name;
        public int feature;
        public int capital;        // 1 if capital
        public int port;           // 1 if port
        public float population;
        public string type;
        public int citadel;
        public int plaza;
        public int walls;
        public int shanty;
        public int temple;
        public string group;

        public Vec2 Position => new Vec2(x, y);
        public bool IsCapital => capital == 1;
        public bool IsPort => port == 1;
    }

    [Serializable]
    public class AzgaarState
    {
        public int i;
        public string name;
        public float expansionism;
        public int capital;        // Capital burg index
        public string type;
        public int center;         // Center cell index
        public int culture;
        public List<float> pole;   // Label position [x, y]
        public List<int> neighbors;
        public string color;
        public float urban;
        public float rural;
        public int burgs;
        public int area;
        public int cells;
        public string form;        // Government form
        public string formName;
        public string fullName;
        public List<int> provinces;

        public Vec2 PolePosition => pole != null && pole.Count >= 2
            ? new Vec2(pole[0], pole[1])
            : Vec2.Zero;
    }

    [Serializable]
    public class AzgaarProvince
    {
        public int i;
        public int state;
        public int center;         // Center cell index
        public int burg;           // Main burg index
        public string name;
        public string formName;
        public string fullName;
        public string color;
        public List<float> pole;   // Label position

        public Vec2 PolePosition => pole != null && pole.Count >= 2
            ? new Vec2(pole[0], pole[1])
            : Vec2.Zero;
    }

    [Serializable]
    public class AzgaarReligion
    {
        public int i;
        public string name;
        public string color;
        public string type;
        public string form;
        public string deity;
        public int culture;
        public int center;
        public int origin;
        public string code;
    }

    [Serializable]
    public class AzgaarRiver
    {
        public int i;
        public int source;         // Source cell index
        public int mouth;          // Mouth cell index
        public int discharge;
        public float length;
        public float width;
        public float widthFactor;
        public float sourceWidth;
        public int parent;         // Parent river (0 = none)
        public List<int> cells;    // Cell path from source to mouth
        public int basin;
        public string name;
        public string type;
    }

    [Serializable]
    public class AzgaarMarker
    {
        public string icon;
        public string type;
        public float x;
        public float y;
        public int cell;
        public int i;
        public string note;
    }

    [Serializable]
    public class AzgaarRoute
    {
        public int i;
        public List<int> points;   // Cell indices along route
        public int feature;
        public string group;       // "roads", "trails", "searoutes"
    }

    [Serializable]
    public class AzgaarGrid
    {
        public int cellsDesired;
        public float spacing;
        public int cellsY;
        public int cellsX;
        public string seed;
        public List<AzgaarGridFeature> features;
    }

    [Serializable]
    public class AzgaarGridFeature
    {
        public int i;
        public int land;
        public int border;
        public string type;
    }

    [Serializable]
    public class AzgaarBiomesData
    {
        public List<int> i;
        public List<string> name;
        public List<string> color;
        public List<int> habitability;
        public List<int> iconsDensity;
        public List<int> cost;
    }
}
