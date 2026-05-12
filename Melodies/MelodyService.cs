using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace JakeyTTS.Melodies
{
    public class MelodyPoint
    {
        public float TimePct { get; set; }
        public float Pitch { get; set; }
    }

    public class Melody
    {
        public string Name { get; set; } = "New Melody";
        public bool IsEnabled { get; set; } = true;
        public List<MelodyPoint> Points { get; set; } = new();
    }

    public class MelodyService
    {
        private static MelodyService? _instance;
        public static MelodyService Instance => _instance ??= new MelodyService();

        public List<Melody> Melodies { get; private set; } = new();
        private readonly string _folderPath = Path.Combine(AppConfig.BaseFolder, "melodies");

        public void Initialize()
        {
            // Ensure the directory exists in AppData
            if (!Directory.Exists(_folderPath)) Directory.CreateDirectory(_folderPath);
            LoadFromDisk();
        }

        public void LoadFromDisk()
        {
            Melodies.Clear();
            if (!Directory.Exists(_folderPath)) return;

            foreach (var file in Directory.GetFiles(_folderPath, "*.json"))
            {
                try
                {
                    var m = JsonSerializer.Deserialize<Melody>(File.ReadAllText(file));
                    if (m != null) Melodies.Add(m);
                }
                catch { }
            }
        }

        public void Save(Melody m)
        {
            string fileName = Path.Combine(_folderPath, $"{m.Name.Replace(" ", "_")}.json");
            File.WriteAllText(fileName, JsonSerializer.Serialize(m, new JsonSerializerOptions { WriteIndented = true }));
            if (!Melodies.Any(x => x.Name == m.Name)) Melodies.Add(m);
        }
        public void Delete(Melody m)
        {
            string fileName = Path.Combine(_folderPath, $"{m.Name.Replace(" ", "_")}.json");
            if (File.Exists(fileName)) File.Delete(fileName);
            Melodies.Remove(m);
        }

        public float GetPitchAt(Melody m, float progress)
        {
            if (m.Points == null || m.Points.Count == 0) return 1.0f;
            var points = m.Points.OrderBy(p => p.TimePct).ToList();

            if (progress <= points[0].TimePct) return points[0].Pitch;
            if (progress >= points.Last().TimePct) return points.Last().Pitch;

            for (int i = 0; i < points.Count - 1; i++)
            {
                if (progress >= points[i].TimePct && progress <= points[i + 1].TimePct)
                {
                    float t = (progress - points[i].TimePct) / (points[i + 1].TimePct - points[i].TimePct);
                    return points[i].Pitch + t * (points[i + 1].Pitch - points[i].Pitch);
                }
            }
            return 1.0f;
        }
    }
}