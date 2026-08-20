using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using SFS.Builds;
using SFS.Input;
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
			List<MenuElement> list = new List<MenuElement>
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
			List<MenuElement> list = new List<MenuElement>
			{
				new SizeSyncerBuilder(out sizeSync).HorizontalMode(SizeMode.MaxChildSize)
			};

			list.Add(ButtonBuilder.CreateButton(sizeSync, () => Field.Text("Export with Blue Background"), new Action(() => ExportBlueprintImage(false)), CloseMode.Current).MinSize(300f, 60f));
			list.Add(ButtonBuilder.CreateButton(sizeSync, () => Field.Text("Export with Transparent Background"), new Action(() => ExportBlueprintImage(true)), CloseMode.Current).MinSize(300f, 60f));
			list.Add(ButtonBuilder.CreateButton(sizeSync, () => Field.Text("Export with Green Background"), new Action(() => ExportBlueprintImage(false, true)), CloseMode.Current).MinSize(300f, 60f));
			list.Add(ButtonBuilder.CreateButton(sizeSync, () => Loc.main.Cancel, delegate { }, CloseMode.Current).MinSize(300f, 60f));

			ScreenManager.main.OpenScreen(MenuGenerator.CreateMenu(CancelButton.Cancel, CloseMode.Current, delegate { }, delegate { }, list.ToArray()));
		}

		static void ExportBlueprintImage(bool transparentBackground, bool greenBackground = false)
		{
			try
			{
				Blueprint blueprint = BuildState.main.GetBlueprint(false);
				if (blueprint == null || blueprint.parts == null || blueprint.parts.Length == 0)
				{
					MsgDrawer.main.Log("Cannot export empty blueprint");
					return;
				}

				OwnershipState[] ownershipStates;
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

				//按蓝图边界计算尺寸
				float paddedWidth = rect.width + 2f;
				float paddedHeight = rect.height + 2f;
				
				float pixelsPerUnit = 100f;
				int width = Mathf.Max(256, Mathf.RoundToInt(paddedWidth * pixelsPerUnit));
				int height = Mathf.Max(256, Mathf.RoundToInt(paddedHeight * pixelsPerUnit));
				
				//限制最大尺寸并保持宽高比
				const int maxDimension = 7500;
				if (width > maxDimension || height > maxDimension)
				{
					float scale = Mathf.Min((float)maxDimension / width, (float)maxDimension / height);
					width = Mathf.RoundToInt(width * scale);
					height = Mathf.RoundToInt(height * scale);
				}

				RenderTexture iconRT;
				if (transparentBackground)
					iconRT = CreatePartIconWithBackground(blueprint, width, height, Color.clear);
				else if (greenBackground)
					iconRT = CreatePartIconWithBackground(blueprint, width, height, Color.green);
				else
					iconRT = PartIconCreator.main.CreatePartIcon_Sharing(blueprint, width, height);
				
				Texture2D tex = ImageTools.RenderTextureTo2D(iconRT, width, height);

				//保存到Mod文件夹内
				string folderPath = Path.Combine(SettingsManager.GetModFolder(), "BPimages");
				Directory.CreateDirectory(folderPath);
				string fileName = DateTime.Now.ToString("yyyyMMdd_HHmmss") + 
					(transparentBackground ? "_transparent" : 
					 greenBackground ? "_green" : "_blue") + ".png";
				string filePath = Path.Combine(folderPath, fileName);
				File.WriteAllBytes(filePath, tex.EncodeToPNG());

				MsgDrawer.main.Log("Saved blueprint image: " + filePath);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				MsgDrawer.main.Log("Failed to export blueprint image: " + ex.Message);
			}
		}

		static RenderTexture CreatePartIconWithBackground(Blueprint blueprint, int width, int height, Color backgroundColor)
		{
			OwnershipState[] ownershipStates;
			Part[] tempParts = PartsLoader.CreateParts(blueprint.parts, null, null, OnPartNotOwned.Allow, out ownershipStates);
			Rect rect;
			Part_Utility.GetFramingBounds_WorldSpace(out rect, tempParts);
			Vector2 v = Vector2.one * 2f;
			rect = new Rect(rect.position - v / 2f, rect.size + v);
			var method = typeof(PartIconCreator).GetMethod("RenderAndDestroy", 
				BindingFlags.NonPublic | BindingFlags.Instance);
			return (RenderTexture)method.Invoke(PartIconCreator.main, new object[] { tempParts, rect, width, height, backgroundColor });
		}
		/*
		static string GetGameRoot()
		{
			var prop = typeof(FileLocations).GetProperty("BaseFolder");
			object folder = prop != null ? prop.GetValue(null) : typeof(FileLocations).GetMethod("GetBaseFolder").Invoke(null, null);
			return folder != null ? folder.ToString() : "";
		}
		*/
	}
}
