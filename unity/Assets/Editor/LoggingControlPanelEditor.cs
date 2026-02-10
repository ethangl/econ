using EconSim.Core;
using EconSim.Core.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace EconSim.Editor
{
    [CustomEditor(typeof(LoggingControlPanel))]
    public class LoggingControlPanelEditor : UnityEditor.Editor
    {
        private static readonly LogDomain[] Domains =
        {
            LogDomain.MapGen,
            LogDomain.HeightmapDsl,
            LogDomain.Climate,
            LogDomain.Rivers,
            LogDomain.Biomes,
            LogDomain.Population,
            LogDomain.Political,
            LogDomain.Economy,
            LogDomain.Transport,
            LogDomain.Roads,
            LogDomain.Renderer,
            LogDomain.Shaders,
            LogDomain.Overlay,
            LogDomain.Selection,
            LogDomain.UI,
            LogDomain.Camera,
            LogDomain.Simulation,
            LogDomain.Bootstrap,
            LogDomain.IO
        };

        private SerializedProperty _ringBufferCapacity;
        private SerializedProperty _minimumLevel;
        private SerializedProperty _recentEntryLimit;

        private void OnEnable()
        {
            _ringBufferCapacity = serializedObject.FindProperty("ringBufferCapacity");
            _minimumLevel = serializedObject.FindProperty("minimumLevel");
            _recentEntryLimit = serializedObject.FindProperty("recentEntryLimit");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var panel = (LoggingControlPanel)target;

            EditorGUILayout.PropertyField(_ringBufferCapacity);
            EditorGUILayout.PropertyField(_minimumLevel);
            EditorGUILayout.PropertyField(_recentEntryLimit);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Quick Presets", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("MapGen only"))
                {
                    panel.UseMapGenOnlyPreset();
                }

                if (GUILayout.Button("Renderer only"))
                {
                    panel.UseRendererOnlyPreset();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Errors only"))
                {
                    panel.UseErrorsOnlyPreset();
                }

                if (GUILayout.Button("All on"))
                {
                    panel.UseAllOnPreset();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Domain Toggles", EditorStyles.boldLabel);

            var domains = panel.EnabledDomains;
            for (int i = 0; i < Domains.Length; i++)
            {
                LogDomain domain = Domains[i];
                bool enabled = (domains & domain) != 0;
                bool next = EditorGUILayout.ToggleLeft(domain.ToString(), enabled);
                if (next != enabled)
                {
                    panel.ToggleDomain(domain, next);
                }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Apply Filter"))
            {
                panel.ApplyRuntimeSettings();
            }

            if (GUILayout.Button("Clear Ring Buffer"))
            {
                panel.ClearRingBuffer();
            }

            var recentEntries = panel.GetRecentEntries();
            EditorGUILayout.LabelField($"Recent Entries ({recentEntries.Count})", EditorStyles.boldLabel);
            for (int i = 0; i < recentEntries.Count; i++)
            {
                var entry = recentEntries[i];
                EditorGUILayout.HelpBox(entry.FormatForConsole(), MessageType.None);
            }

            if (serializedObject.ApplyModifiedProperties())
            {
                panel.ApplyRuntimeSettings();
                EditorUtility.SetDirty(panel);
            }
        }
    }
}
