﻿using RuneScapeCacheTools;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Villermen.RuneScapeCacheTools
{
	public abstract class Cache
	{
		/// <summary>
		/// The directory that is the default location for this type of cache.
		/// </summary>
		public abstract string DefaultCacheDirectory { get; }

		private string _cacheDirectory;
		public string CacheDirectory
		{
			get
			{
				return _cacheDirectory;
			}
			set
			{
				_cacheDirectory = DirectoryHelper.FormatDirectory(value);
			}
		}

		private string _outputDirectory;
		public string OutputDirectory
		{
			get
			{
				return _outputDirectory;
			}
			set
			{
				_outputDirectory = DirectoryHelper.FormatDirectory(value);
			}
		}

		private string _temporaryDirectory;
		public string TemporaryDirectory
		{
			get
			{
				return _temporaryDirectory;
			}
			set
			{
				_temporaryDirectory = DirectoryHelper.FormatDirectory(value);
			}
		}

		protected Cache()
		{
			CacheDirectory = DefaultCacheDirectory;
			TemporaryDirectory = Path.GetTempPath() + "rsct/";
		}

		public abstract IEnumerable<int> getArchiveIds();

		/// <summary>
		/// Extracts every file in every archive.
		/// </summary>
		/// <returns></returns>
		public abstract Task ExtractAllAsync();

		/// <summary>
		/// Extracts every file in the given archive.
		/// </summary>
		/// <param name="archiveId"></param>
		/// <returns></returns>
		public abstract Task ExtractArchiveAsync(int archiveId);

		/// <summary>
		/// Extracts the given file in the given archive.
		/// </summary>
		/// <param name="archiveId"></param>
		/// <param name="fileId"></param>
		/// <returns></returns>
		public abstract Task ExtractFileAsync(int archiveId, int fileId);

		public virtual bool IsArchiveExtracted(int archiveId)
		{
			return Directory.Exists(OutputDirectory + "cache/" + archiveId);
		}

		/// <summary>
		/// Finds the path for the given extracted file.
		/// </summary>
		/// <param name="archiveId"></param>
		/// <param name="fileId"></param>
		/// <param name="extractIfMissing">Try to extract the file if it hasn't been extracted yet.</param>
		/// <returns>Returns the path to the obtained file, or null if it does not exist.</returns>
		public virtual string GetFilePath(int archiveId, int fileId, bool extractIfMissing = false)
		{
			string path = Directory.EnumerateFiles($"{OutputDirectory}cache/{archiveId}/", $"{fileId}*")
				.Where(file => Regex.IsMatch(file, $@"(/|\\){fileId}(\..+)?$"))
				.FirstOrDefault();

			if (!string.IsNullOrWhiteSpace(path))
			{
				return path;
			}

			if (extractIfMissing)
			{
				ExtractFileAsync(archiveId, fileId).Wait();
				return GetFilePath(archiveId, fileId);
			}

			return null;
		}

		/// <summary>
		/// Write out the given data to the specified file.
		/// Deletes a previous version of the file (regardless of extension) if it exists.
		/// </summary>
		/// <param name="archiveId"></param>
		/// <param name="fileId"></param>
		/// <param name="data"></param>
		/// <param name="extension">File extension, without the dot.</param>
		protected void WriteFile(int archiveId, int fileId, byte[] data, string extension = null)
		{
			// Throw an exception if the output directory is not yet set or does not exist
			if (string.IsNullOrWhiteSpace(OutputDirectory) || !Directory.Exists(OutputDirectory))
			{
				throw new DirectoryNotFoundException("Output directory does not exist.");
			}

			// Delete existing file
			string existingFilePath = GetFilePath(archiveId, fileId);
			if (!string.IsNullOrWhiteSpace(existingFilePath))
			{
				File.Delete(existingFilePath);
			}

			// Construct new path
			string newFilePath = $"{OutputDirectory}cache/{archiveId}/{fileId}";
			if (!string.IsNullOrWhiteSpace(extension))
			{
				newFilePath += $".{extension}";
			}

			// Create directories where necessary, before writing to file
			Directory.CreateDirectory(newFilePath);
			File.WriteAllBytes(newFilePath, data);
		}
	}
}
