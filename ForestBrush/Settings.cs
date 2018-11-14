﻿using ColossalFramework.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using UnityEngine;

namespace ForestBrush
{
    [XmlRoot("ForestBrushSettings")]
    public class ForestBrushSettings
    {
        [XmlIgnore]
        private static readonly string configurationPath = Path.Combine(DataLocation.localApplicationData, "ForestBrushSettings.xml");

        public List<KeyValuePair<string, List<string>>> SavedBrushes { get; set; } = new List<KeyValuePair<string, List<string>>>() { new KeyValuePair<string, List<string>>(Constants.VanillaPack, new List<string>()) };

        public string SelectedBrush { get; set; } = Constants.VanillaPack;

        public float PanelX { get; set; } = 8f;

        public float PanelY { get; set; } = 65f;

        public bool ConfirmOverwrite { get; set; } = true;

        public bool UseTreeSize { get; set; } = false;

        public bool SquareBrush { get; set; } = true;

        public float Spacing { get; set; } = 4f;

        public OverlayColor OverlayColor { get; set; } = new Color32(133, 33, 33, 255);

        public static string ConfigurationPath
        {
            get
            {
                return configurationPath;
            }
        }


        public ForestBrushSettings() { }

        public void OnPreSerialize() { }

        public void OnPostDeserialize() { }

        public void Save()
        {
            SavedBrushes?.Clear();
            if (ForestBrushes.instance.Brushes != null)
            {
                foreach (var brush in ForestBrushes.instance.Brushes.ToList())
                    if (brush.Value != null)
                        SavedBrushes.Add(brush);
            }           
            
            var fileName = ConfigurationPath;
            var config = UserMod.Settings;
            var serializer = new XmlSerializer(typeof(ForestBrushSettings));
            using (var writer = new StreamWriter(fileName))
            {
                config.OnPreSerialize();
                serializer.Serialize(writer, config);
            }
        }


        public static ForestBrushSettings Load()
        {
            var fileName = ConfigurationPath;
            var serializer = new XmlSerializer(typeof(ForestBrushSettings));
            try
            {
                using (var reader = new StreamReader(fileName))
                {
                    var config = serializer.Deserialize(reader) as ForestBrushSettings;
                    ForestBrushes.instance.Brushes.Clear();
                    ForestBrushes.instance.Brushes = config.SavedBrushes?.ToDictionary(kv => kv.Key, kv => kv.Value);
                    return config;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log($"Error Parsing {fileName}: {ex}");
                return null;
            }
        }
    }
}
