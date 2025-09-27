using HarmonyLib;
using ModLoader;

namespace BlueprintImage
{
    public class Main : Mod
    {
        public override string ModNameID => "BlueprintImage";
        public override string DisplayName => "BlueprintImage";
        public override string Author => "SFSGamer";
        public override string MinimumGameVersionNecessary => "1.5.10.2";
        public override string ModVersion => "v1.0.2";
        public override string Description => "Add a blueprint image to the blueprint screen.";

        static Harmony patcher;

        public override void Load()
        {
            patcher = new Harmony("BlueprintImage.Main");
            patcher.PatchAll();
        }
    }
} 