using System;
using System.IO;
using System.Text.Json;
using FreeMon.Models;

namespace FreeMon.Services
{
    /// <summary>
    /// Загрузка и сохранение настроек в %AppData%\FreeMon\config.json
    /// </summary>
    public static class ConfigService
    {
        private static string Dir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FreeMon");

        private static string FilePath => Path.Combine(Dir, "config.json");

        public static bool ConfigExists() => File.Exists(FilePath);

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    AppConfig? cfg = JsonSerializer.Deserialize<AppConfig>(json);
                    if (cfg != null)
                        return cfg;
                }
            }
            catch
            {
                // повреждённый конфиг — просто стартуем с настроек по умолчанию
            }
            return new AppConfig();
        }

        public static void Save(AppConfig config)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // не удалось сохранить — не критично
            }
        }
    }
}
