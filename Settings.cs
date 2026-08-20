using System;
using System.IO;
using Newtonsoft.Json;

namespace BlueprintImage
{
    [Serializable]
    public class ModSettingsConfig
    {
        public int port = 32000;
        public bool enableServer = false;
    }

    public static class SettingsManager
    {
        static string _modFolder = null;
        static string _configPath = null;
        public static ModSettingsConfig settings;

        //获取Mod文件夹路径
        public static string GetModFolder()
        {
            if (_modFolder != null) return _modFolder;

            if (ModLoader.Loader.main != null)
            {
                foreach (var mod in ModLoader.Loader.main.GetAllMods())
                {
                    if (mod.ModNameID == "BlueprintImage")
                    {
                        _modFolder = mod.ModFolder;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(_modFolder))
                _modFolder = "Mods/BlueprintImage";

            return _modFolder;
        }

        //获取配置文件路径
        public static string GetConfigPath()
        {
            if (_configPath == null)
                _configPath = Path.Combine(GetModFolder(), "Settings.txt");
            return _configPath;
        }

        //加载设置
        public static void Load()
        {
            string path = GetConfigPath();
            if (File.Exists(path))
            {
                try
                {
                    settings = JsonConvert.DeserializeObject<ModSettingsConfig>(File.ReadAllText(path));
                    return;
                }
                catch
                {
                    settings = new ModSettingsConfig();
                }
            }
            else
            {
                settings = new ModSettingsConfig();
            }
            Save();
        }

        //保存设置
        public static void Save()
        {
            File.WriteAllText(GetConfigPath(), JsonConvert.SerializeObject(settings, Formatting.Indented));
        }

        public static void SaveSettings(ModSettingsConfig config)
        {
            File.WriteAllText(GetConfigPath(), JsonConvert.SerializeObject(config, Formatting.Indented));
        }

        public static ModSettingsConfig LoadSettings()
        {
            string path = GetConfigPath();
            if (!File.Exists(path)) return new ModSettingsConfig();
            try
            {
                return JsonConvert.DeserializeObject<ModSettingsConfig>(File.ReadAllText(path));
            }
            catch
            {
                return new ModSettingsConfig();
            }
        }
    }
}
