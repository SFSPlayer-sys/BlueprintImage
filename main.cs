using HarmonyLib;
using ModLoader;
using UnityEngine;

namespace BlueprintImage
{
    public class Main : Mod
    {
        public override string ModNameID => "BlueprintImage";
        public override string DisplayName => "BlueprintImage";
        public override string Author => "SFSGamer";
        public override string MinimumGameVersionNecessary => "1.5.10.2";
        public override string ModVersion => "v1.1";
        public override string Description => "Add a blueprint image to the blueprint screen with optional HTTP rendering service.";

        static Harmony patcher;
        private BlueprintServer serverComponent;

        public override void Load()
        {
            SettingsManager.Load();
            //启用则启动HTTP服务器
            if (SettingsManager.settings.enableServer)
            {
                var serverObject = new GameObject("BlueprintImage_Server");
                GameObject.DontDestroyOnLoad(serverObject);
                serverComponent = serverObject.AddComponent<BlueprintServer>();
                serverComponent.StartServer(SettingsManager.settings.port);
                Application.runInBackground = true;
                Debug.Log($"[BlueprintImage] HTTP server enabled on port {SettingsManager.settings.port}");
            }
            else
                Debug.Log("[BlueprintImage] HTTP server disabled");

            patcher = new Harmony("BlueprintImage.Main");
            patcher.PatchAll();
        }
    }
}
