﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using Villermen.RuneScapeCacheTools.Cache.RuneTek5.Enums;

namespace Villermen.RuneScapeCacheTools.Cache.RuneTek5.Audio
{
	/// <summary>
	///   Contains tools for obtaining and combining soundtracks from the cache.
	/// </summary>
	public class Soundtrack
	{
		/// <summary>
		/// Used in the generation of temporary filenames.
		/// </summary>
		private Random _random = new Random();

		public Soundtrack(CacheBase cache)
		{
			Cache = cache;
		}

		public CacheBase Cache { get; set; }

		/// <summary>
		///   Returns the track names and their corresponding jaga file id in index 40.
		///   Track names are made filename-safe, and empty ones are filtered out.
		/// </summary>
		/// <returns></returns>
		public IDictionary<int, string> GetTrackNames()
		{
			var trackNames = new EnumFile(Cache.GetArchiveFileData(17, 5, 65));
			var jagaFileIds = new EnumFile(Cache.GetArchiveFileData(17, 5, 71));

			var result = new Dictionary<int, string>();
			foreach (var trackNamePair in trackNames)
			{
				var trackName = (string) trackNamePair.Value;

				if (!jagaFileIds.ContainsKey(trackNamePair.Key))
				{
					continue;
				}

				var trackFileId = (int) jagaFileIds[trackNamePair.Key];

				// Make trackName filename-safe
				foreach (var invalidChar in Path.GetInvalidFileNameChars())
				{
					trackName = trackName.Replace(invalidChar.ToString(), "");
				}

				// Don't add empty filenames to the array
				if (string.IsNullOrWhiteSpace(trackName))
				{
					continue;
				}

				if (!result.ContainsKey(trackFileId))
				{
					result.Add(trackFileId, trackName);
				}
				else
				{
					result[trackFileId] = trackName;
				}
			}

			return result;
		}

		public async Task ExportTracksAsync(bool overwriteExisting = false)
		{
			var trackNames = GetTrackNames();
			var outputDirectory = Cache.OutputDirectory + "soundtrack/";

			Directory.CreateDirectory(outputDirectory);
			Directory.CreateDirectory(Cache.TemporaryDirectory);

			// Remove existing tracks from the dictionary if we should not export them anyway
			if (!overwriteExisting)
			{
				var existingTrackNames = Directory.EnumerateFiles(outputDirectory, "*.ogg").Select(Path.GetFileNameWithoutExtension);

				trackNames = trackNames.Where(pair => !existingTrackNames.Contains(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value);
			}

			Parallel.ForEach(trackNames, trackNamePair =>
			{
				var jagaFile = new JagaFile(Cache.GetFileData(40, trackNamePair.Key));

				// Obtain names for the temporary files. We can't use the id as filename, because we are going full parallel.
				var randomTemporaryFilenames = GetRandomTemporaryFilenames(jagaFile.ChunkCount);

				// Write out the files
				File.WriteAllBytes(randomTemporaryFilenames[0], jagaFile.ContainedChunkData);

				for (var chunkIndex = 1; chunkIndex < jagaFile.ChunkCount; chunkIndex++)
				{
					File.WriteAllBytes(randomTemporaryFilenames[chunkIndex],
						Cache.GetFileData(40, jagaFile.ChunkDescriptors[chunkIndex].FileId));
				}

				// Combine the files using oggCat
				var combineProcess = new Process
				{
					StartInfo =
					{
						FileName = "oggCat",
						UseShellExecute = false,
						CreateNoWindow = true,
						Arguments = $"\"{outputDirectory}{trackNamePair.Value}.ogg\" " + string.Join(" ", randomTemporaryFilenames)
					}
				};

				combineProcess.Start();
				combineProcess.WaitForExit();

				// Remove temporary files
				foreach (var randomTemporaryFilename in randomTemporaryFilenames)
				{
					File.Delete(randomTemporaryFilename);
				}

				if (combineProcess.ExitCode != 0)
				{
					throw new SoundtrackException($"oggCat returned with error code {combineProcess.ExitCode}.");
				}
			});
		}

		private string[] GetRandomTemporaryFilenames(int amountOfNames)
		{
			const string validChars = "abcdefghijklmnopqrstuvwxyz0123456789-_()&^%$#@![]{}',`~=+";
			const int nameLength = 16;
			var result = new string[amountOfNames];

			for (var i = 0; i < amountOfNames; i++)
			{
				string newPath;
				do
				{
					newPath = new string(Enumerable.Repeat(validChars, nameLength).Select(s => s[_random.Next(s.Length)]).ToArray());
					newPath = Cache.TemporaryDirectory + newPath + ".ogg";
				}
				while (File.Exists(newPath) || result.Contains(newPath));

				result[i] = newPath;
			}

			return result;
		}
	}
}