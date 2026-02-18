using System.Reflection;
using EconSim.Renderer;
using NUnit.Framework;
using UnityEngine;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("SelectionScope")]
    public class MapViewSelectionScopeTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void SetMapMode_ZoomDrivenPoliticalTransition_PreservesCurrentSelectionScope()
        {
            var gameObject = new GameObject("MapViewSelectionScopeTests_Preserve");
            var mapView = gameObject.AddComponent<MapView>();

            try
            {
                SetSelectionState(mapView, MapView.SelectionScope.Province, realmId: 3, provinceId: 12, countyId: 48);

                mapView.SetMapMode(MapView.MapMode.County);

                Assert.That(GetSelectionScope(mapView), Is.EqualTo(MapView.SelectionScope.Province));
                Assert.That(mapView.SelectedRealmId, Is.EqualTo(3));
                Assert.That(mapView.SelectedProvinceId, Is.EqualTo(12));
                Assert.That(mapView.SelectedCountyId, Is.EqualTo(48));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SetMapMode_LeavingPoliticalFamily_ClearsSelectionScope()
        {
            var gameObject = new GameObject("MapViewSelectionScopeTests_Clear");
            var mapView = gameObject.AddComponent<MapView>();

            try
            {
                SetSelectionState(mapView, MapView.SelectionScope.Realm, realmId: 9, provinceId: 31, countyId: 82);

                mapView.SetMapMode(MapView.MapMode.Biomes);

                Assert.That(GetSelectionScope(mapView), Is.EqualTo(MapView.SelectionScope.None));
                Assert.That(mapView.SelectedRealmId, Is.EqualTo(-1));
                Assert.That(mapView.SelectedProvinceId, Is.EqualTo(-1));
                Assert.That(mapView.SelectedCountyId, Is.EqualTo(-1));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        private static MapView.SelectionScope GetSelectionScope(MapView mapView)
        {
            return GetFieldValue<MapView.SelectionScope>(mapView, "selectionScope");
        }

        private static void SetSelectionState(MapView mapView, MapView.SelectionScope scope, int realmId, int provinceId, int countyId)
        {
            SetFieldValue(mapView, "selectionScope", scope);
            SetFieldValue(mapView, "selectedRealmId", realmId);
            SetFieldValue(mapView, "selectedProvinceId", provinceId);
            SetFieldValue(mapView, "selectedCountyId", countyId);
        }

        private static T GetFieldValue<T>(MapView mapView, string fieldName)
        {
            var field = typeof(MapView).GetField(fieldName, NonPublicInstance);
            Assert.That(field, Is.Not.Null, $"Expected private field '{fieldName}' to exist.");
            return (T)field.GetValue(mapView);
        }

        private static void SetFieldValue<T>(MapView mapView, string fieldName, T value)
        {
            var field = typeof(MapView).GetField(fieldName, NonPublicInstance);
            Assert.That(field, Is.Not.Null, $"Expected private field '{fieldName}' to exist.");
            field.SetValue(mapView, value);
        }
    }
}
