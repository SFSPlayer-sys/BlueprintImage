using System;
using System.Reflection;
using System.IO;
using HarmonyLib;
using SFS.Builds;
using SFS.Input;
using SFS.IO;
using SFS.Parts;
using SFS.Parts.Modules;
using SFS.Translations;
using SFS.UI;
using UnityEngine;

namespace BlueprintImage
{
	[HarmonyPatch(typeof(DownloadMenu), "OpenSharingMenu_PC")]
	public static class ShareMenuPatch
	{
		static bool Prefix(DownloadMenu __instance)
		{
			SizeSyncerBuilder.Carrier sizeSync;
			System.Collections.Generic.List<MenuElement> list = new System.Collections.Generic.List<MenuElement>
			{
				new SizeSyncerBuilder(out sizeSync).HorizontalMode(SizeMode.MaxChildSize)
			};

			list.Add(ButtonBuilder.CreateButton(sizeSync, () => Loc.main.Upload_Blueprint_PC, new Action(__instance.UploadRocket), CloseMode.Current).MinSize(300f, 60f).CustomizeButton(a =>
			{
				a.SetEnabled(BuildManager.main.buildGrid.activeGrid.partsHolder.parts.Count > 0);
			}));

			var openDownloadMenu = (Action)typeof(DownloadMenu).GetMethod("OpenDownloadMenu", BindingFlags.NonPublic | BindingFlags.Instance).CreateDelegate(typeof(Action), __instance);
			list.Add(ButtonBuilder.CreateButton(sizeSync, () => Loc.main.Download_Blueprint_PC, openDownloadMenu, CloseMode.Current).MinSize(300f, 60f));

			list.Add(ButtonBuilder.CreateButton(sizeSync, () => Field.Text("Export Blueprint Image"), new Action(ShowExportOptions), CloseMode.Current).MinSize(300f, 60f).CustomizeButton(a =>
			{
				a.SetEnabled(BuildManager.main.buildGrid.activeGrid.partsHolder.parts.Count > 0);
			}));

			list.Add(ButtonBuilder.CreateButton(sizeSync, () => Loc.main.Cancel, delegate { }, CloseMode.Current).MinSize(300f, 60f));

			ScreenManager.main.OpenScreen(MenuGenerator.CreateMenu(CancelButton.Cancel, CloseMode.Current, delegate { }, delegate { }, list.ToArray()));
			return false;
		}

		static void ShowExportOptions()
		{
			SizeSyncerBuilder.Carrier sizeSync;
			System.Collections.Generic.List<MenuElement> list = new System.Collections.Generic.List<MenuElement>
			{
				new SizeSyncerBuilder(out sizeSync).HorizontalMode(SizeMode.MaxChildSize)
			};

			list.Add(ButtonBuilder.CreateButton(sizeSync, () => Field.Text("Export with Blue Background"), new Action(() => ExportBlueprintImage(false)), CloseMode.Current).MinSize(300f, 60f));
			list.Add(ButtonBuilder.CreateButton(sizeSync, () => Field.Text("Export with Transparent Background"), new Action(() => ExportBlueprintImage(true)), CloseMode.Current).MinSize(300f, 60f));
			list.Add(ButtonBuilder.CreateButton(sizeSync, () => Loc.main.Cancel, delegate { }, CloseMode.Current).MinSize(300f, 60f));

			ScreenManager.main.OpenScreen(MenuGenerator.CreateMenu(CancelButton.Cancel, CloseMode.Current, delegate { }, delegate { }, list.ToArray()));
		}

		static void ExportBlueprintImage(bool transparentBackground)
		{
			try
			{
				Blueprint blueprint = BuildState.main.GetBlueprint(false);
				if (blueprint == null || blueprint.parts == null || blueprint.parts.Length == 0)
				{
					MsgDrawer.main.Log("Cannot export empty blueprint");
					return;
				}

				// Compute adaptive width/height from blueprint bounds
				SFS.Parts.Modules.OwnershipState[] ownershipStates;
				Part[] tempParts = PartsLoader.CreateParts(blueprint.parts, null, null, OnPartNotOwned.Allow, out ownershipStates);
				Rect rect;
				Part_Utility.GetFramingBounds_WorldSpace(out rect, tempParts);
				for (int i = 0; i < tempParts.Length; i++)
				{
					if (tempParts[i] != null && tempParts[i].gameObject != null)
					{
						tempParts[i].gameObject.SetActive(false);
						UnityEngine.Object.Destroy(tempParts[i].gameObject);
					}
				}

				// Add small padding similar to sharing preview
				float paddedWidth = rect.width + 2f;
				float paddedHeight = rect.height + 2f;
				
				float pixelsPerUnit = 100f;
				int width = Mathf.Max(256, Mathf.RoundToInt(paddedWidth * pixelsPerUnit));
				int height = Mathf.Max(256, Mathf.RoundToInt(paddedHeight * pixelsPerUnit));

				// 根据选择使用不同的背景颜色
				RenderTexture iconRT;
				if (transparentBackground)
				{
					iconRT = CreatePartIconWithBackground(blueprint, width, height, Color.clear);
				}
				else
				{
					iconRT = PartIconCreator.main.CreatePartIcon_Sharing(blueprint, width, height);
				}
				
				Texture2D tex = ImageTools.RenderTextureTo2D(iconRT, width, height);

				// Save directly under the game root directory (parent of BaseFolder)
				string baseFolder = FileLocations.BaseFolder.ToString();
				string gameRoot = Path.GetDirectoryName(baseFolder);
				FolderPath imagesFolder = new FolderPath(gameRoot).Extend("BPimages").CreateFolder();
				string fileName = DateTime.Now.ToString("yyyyMMdd_HHmmss") + (transparentBackground ? "_transparent" : "_blue") + ".png";
				FilePath filePath = imagesFolder.ExtendToFile(FilePath.CleanupName(fileName));
				tex.SaveToFile(filePath);

				MsgDrawer.main.Log("Saved blueprint image: " + (string)filePath);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				MsgDrawer.main.Log("Failed to export blueprint image");
			}
		}

		// 创建带自定义背景的蓝图图标
		static RenderTexture CreatePartIconWithBackground(Blueprint blueprint, int width, int height, Color backgroundColor)
		{
			// 参考 PartIconCreator 的实现
			SFS.Parts.Modules.OwnershipState[] ownershipStates;
			Part[] tempParts = PartsLoader.CreateParts(blueprint.parts, null, null, OnPartNotOwned.Allow, out ownershipStates);
			Rect rect;
			Part_Utility.GetFramingBounds_WorldSpace(out rect, tempParts);
			
			// 添加边距
			Vector2 vector = Vector2.one * 2f;
			rect = new Rect(rect.position - vector / 2f, rect.size + vector);
			
			// 反射调用 RenderAndDestroy
			var method = typeof(PartIconCreator).GetMethod("RenderAndDestroy", 
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			return (RenderTexture)method.Invoke(PartIconCreator.main, new object[] { tempParts, rect, width, height, backgroundColor });
		}
	}
}
