﻿using UnityEngine;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

[RequireComponent(typeof(KMService))]
public class SoundpackMaker : MonoBehaviour
{
	private string SoundsDirectory
	{
		get
		{
			return Path.Combine(Application.persistentDataPath, "Soundpacks");
		}
	}

	void Log(params object[] objects)
	{
		Debug.LogFormat("[SoundpackMaker] " + string.Join(", ", objects.Select(obj => Convert.ToString(obj)).ToArray()));
	}

	void Log(string text, params object[] formatting)
	{
		Debug.LogFormat("[SoundpackMaker] " + text, formatting);
	}

	Type SoundEffect = typeof(KMSoundOverride.SoundEffect);
	string[] audioExtensions = new string[] {
		".wav",
		".mp3",
		".ogg"
	};

	AudioClip MakeAudioClip(string path)
	{
		string ext = Path.GetExtension(path);
		if (audioExtensions.Contains(ext))
		{
			try
			{
				if (ext == ".mp3")
				{
					return NAudioPlayer.FromMp3Data(new WWW("file:///" + path).bytes);
				}
				else
				{
				    AudioClip clip = new WWW("file:///" + path).GetAudioClipCompressed();
                    while (clip.loadState != AudioDataLoadState.Loaded)
					{
					}
					return clip;
				}
			}
			catch (Exception ex)
			{
				Log("Failed to load sound sound file {0} due to Exception: {1}\nStack Trace {2}", path, ex.Source, ex.StackTrace);
			}
		}
		return null;
	}

	// Case insensitve version of Enum.IsDefined
	bool IsDefined(Type enumType, string name)
	{
		return Enum.GetNames(enumType).Any(enumName => enumName.ToLowerInvariant() == name.ToLowerInvariant());
	}

	List<KMSoundOverride> LoadSoundpack(string soundpackDirectory)
	{
		List<KMSoundOverride> Overrides = new List<KMSoundOverride>();

		foreach (string file in Directory.GetFiles(soundpackDirectory))
		{
			string fileName = Path.GetFileNameWithoutExtension(file);
			if (IsDefined(SoundEffect, fileName))
			{
				Log("Creating AudioClip for {0}.", fileName);
				AudioClip clip = MakeAudioClip(file);
				if (clip)
				{
					clip.name = Path.GetFileName(file);

					KMSoundOverride soundOverride = new GameObject().AddComponent<KMSoundOverride>();
					soundOverride.OverrideEffect = (KMSoundOverride.SoundEffect) Enum.Parse(SoundEffect, fileName, true);
					soundOverride.AudioClip = clip;
					Overrides.Add(soundOverride);
				}
				else
				{
					Log("Failed to create an AudioClip for {0}. Skipping.", fileName);
				}
			}
			else
			{
				Log("{0} isn't a valid sound effect. Skipping.", fileName);
			}
		}

		foreach (string directory in Directory.GetDirectories(soundpackDirectory))
		{
			string dirName = new DirectoryInfo(directory).Name;
			if (IsDefined(SoundEffect, dirName))
			{
				KMSoundOverride soundOverride = new GameObject().AddComponent<KMSoundOverride>();
				soundOverride.OverrideEffect = (KMSoundOverride.SoundEffect) Enum.Parse(SoundEffect, dirName, true);
				List<AudioClip> audioClips = new List<AudioClip>();

				foreach (string file in Directory.GetFiles(directory))
				{
					Log("Creating AudioClip for {0}.", Path.Combine(dirName, Path.GetFileName(file)));
					AudioClip clip = MakeAudioClip(file);
					if (clip)
					{
						clip.name = Path.GetFileName(file);

						if (!soundOverride.AudioClip)
						{
							soundOverride.AudioClip = clip;
						}
						else
						{
							audioClips.Add(clip);
						}
					}
					else
					{
						Log("Failed to create an AudioClip for {0}. Skipping.", Path.Combine(dirName, Path.GetFileName(file)));
					}
				}

				if (soundOverride.AudioClip)
				{
					if (audioClips.Count > 0)
					{
						soundOverride.AdditionalVariants = audioClips.ToArray();
					}

					Overrides.Add(soundOverride);
				}
			}
			else
			{
				Log("{0} isn't a valid sound effect. Skipping.", dirName);
			}
		}

		return Overrides;
	}

    private static object realMod = null;

	void Start()
	{
	    if (realMod != null)
	        return;

		ModSettings Soundpacks = new ModSettings("EnabledSoundpacks", typeof(List<string>));
		List<string> enabledSoundpacks = (List<string>) Soundpacks.Settings;

		if (!Directory.Exists(SoundsDirectory))
		{
			Log("Created the Soundpacks directory, not loading any soundpacks.");

			Directory.CreateDirectory(SoundsDirectory);
			return;
		}

	    Type ModManager = ReflectionHelper.FindType("ModManager");
	    FieldInfo ModManagerInstanceField = ModManager.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
	    FieldInfo ModManagerLoadedModsDictField = ModManager.GetField("loadedMods", BindingFlags.NonPublic | BindingFlags.Instance);
	    object ModManagerInstance = ModManagerInstanceField.GetValue(null);
	    IDictionary LoadedMods = (IDictionary) ModManagerLoadedModsDictField.GetValue(ModManagerInstance);
	    realMod = null;
	    foreach (DictionaryEntry kvp in LoadedMods)
	    {
	        string key = (string) kvp.Key;
	        var id = (string) kvp.Value.GetType().GetProperty("ModID", BindingFlags.Public | BindingFlags.Instance).GetValue(kvp.Value, null);
	        Log("Key = {0}, ModID = {1}", key, id);
	        if (id.Equals("SoundpackMaker"))
	        {
	            realMod = kvp.Value;
	            break;
	        }
	    }
	    if (realMod == null)
	        return;

		MethodInfo HandleSoundOverride = realMod.GetType().GetMethod("HandleSoundOverride", BindingFlags.NonPublic | BindingFlags.Instance);
		Dictionary<KMSoundOverride.SoundEffect, KMSoundOverride> soundOverrides = new Dictionary<KMSoundOverride.SoundEffect, KMSoundOverride>();
		
		// Add the new sound effects.
		foreach (string soundpackName in enabledSoundpacks)
		{
			string soundpackDirectory = Path.Combine(SoundsDirectory, soundpackName);
			if (Directory.Exists(soundpackDirectory))
			{
				Log("Adding soundpack: {0}", soundpackName);
				foreach (KMSoundOverride soundOverride in LoadSoundpack(soundpackDirectory))
				{
					if (soundOverrides.ContainsKey(soundOverride.OverrideEffect))
					{
						KMSoundOverride sOverride = soundOverrides[soundOverride.OverrideEffect];
						List<AudioClip> clips = new List<AudioClip>();
						clips.Add(soundOverride.AudioClip);
						if (soundOverride.AdditionalVariants != null) clips.AddRange(soundOverride.AdditionalVariants);
						if (sOverride.AdditionalVariants != null) clips.AddRange(sOverride.AdditionalVariants);
						sOverride.AdditionalVariants = clips.ToArray();
						soundOverride.AdditionalVariants = null;
						soundOverride.AudioClip = null;
						Destroy(soundOverride);
					}
					else
					{
						soundOverrides[soundOverride.OverrideEffect] = soundOverride;
					}
				}
			}
			else
			{
				Log("There is no soundpack called \"{0}\"", soundpackName);
			}
		}

		foreach (KMSoundOverride soundOverride in soundOverrides.Values)
		{
			HandleSoundOverride.Invoke(realMod, new object[] { soundOverride });
		}
	}
}
