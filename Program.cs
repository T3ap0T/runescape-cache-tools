﻿using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.IO.Compression;
using System.Threading;
using System.Diagnostics;
using ICSharpCode.SharpZipLib.BZip2;
using NDesk.Options;
using System.Text.RegularExpressions;

namespace RSCacheTool
{
	static class Program
	{
		static string _cacheDir = Environment.ExpandEnvironmentVariables(@"%USERPROFILE%/jagexcache/runescape/LIVE/");
		static string _outDir = "cache/";

		static int Main(string[] args)
		{
			bool error = false;

			bool help = false, extract = false, combine = false, overwrite = false, incomplete = false, nameMusic = false, pauseAfterDone = false, lossless = false;
			int extractArchive = -1, combineArchive = 40;
			string combineFile = "", nameFile = "";

			OptionSet argsParser = new OptionSet {
				{ "h", "show this message", val => { help = true; } },

				{ "o", "overwrite existing files, for all actions", val => { overwrite = true; } },

				{ "e:", "extract files from cache, supply a number to extract only a specific archive", val => { 
					extract = true;

					//set val only if it consists solely of numbers
					if (!String.IsNullOrWhiteSpace(val) && val.All(c => c >= '0' && c <= '9'))
						int.TryParse(val, out extractArchive); 
				}},

				{ "c:", "combine sound, supply a number to extract from a different archive (defaults to 40)", val => { 
					combine = true;
					if (!String.IsNullOrWhiteSpace(val) && val.All(c => c >= '0' && c <= '9'))
						int.TryParse(val, out combineArchive);
				}},
				{ "f=", "single index file (.jaga) to combine sounds of, if you want to fix just one sound", val => { combineFile = val; } },
				{ "i", "merge incomplete files (into special directory)", val => { incomplete = true; } },
				{ "l", "combine files losslessly (.flac format)", val => { lossless = true; } },

				{ "n:", "try to name music (archive 40, needs archive 17 file 5 too), renames incompletes too if i is set. If a number is suplied it will only name a single file.", val => { 
					nameMusic = true;

					if (!String.IsNullOrWhiteSpace(val) && val.All(c => c >= '0' && c <= '9'))
						nameFile = val; 
				}},

				{ "p", "pause after running (mainly for easier debugging in VS)", val => { pauseAfterDone = true; } }
			};

			List<string> otherArgs = argsParser.Parse(args);

			for (int i = 0; i < otherArgs.Count; i++)
			{
				string parsedPath = otherArgs[i];
				if (!parsedPath.EndsWith("/"))
					parsedPath += "/";

				parsedPath = Environment.ExpandEnvironmentVariables(parsedPath);

				if (Directory.Exists(parsedPath))
				{
					switch (i)
					{
						case 0:
							_outDir = parsedPath;
							break;
						case 1:
							_cacheDir = parsedPath;
							break;
					}
				}
				else
				{
					Console.WriteLine("The directory: " + parsedPath + " is not valid.");
					error = true;
				}
			}

			if (args.Length == 0 || help)
			{
				Console.WriteLine(
					"Usage: rscachetools [options] outDir cacheDir\n" + 
					"Provides various tools for extracting and manipulating RuneScape's cache files.\n" +
					"\n" +
					"Arguments:\n" +
					"outDir - The directory in which all files generated by this tool will be placed. Default: cache/\n" +
					"cacheDir - The directory that contains all cache files. Default: %USERPROFILE%/jagexcache/runescape/LIVE/.\n" +
					"\n" +
					"Options:"
				);

				argsParser.WriteOptionDescriptions(Console.Out);
			}
			else if (!error)
			{
				//create outdir
				if (!Directory.Exists(_outDir))
					Directory.CreateDirectory(_outDir);

				if (extract)
					ExtractFiles(extractArchive, overwrite);

				if (combine)
					CombineSounds(combineArchive, combineFile, overwrite, incomplete, lossless);

				if (nameMusic)
					NameMusic(nameFile, incomplete, overwrite);
			}

			if (pauseAfterDone)
				Console.ReadLine();

			return 0;
		}

		/// <summary>
		/// Rips all files from the cachefile and puts them (structured and given a fitting extension where possible) in the fileDir.
		/// </summary>
		static void ExtractFiles(int archive, bool overwriteExisting)
		{
			int startArchive = 0, endArchive = 255;

			if (archive != -1)
			{
				startArchive = archive;
				endArchive = archive;
			}

			using (FileStream cacheFile = File.Open(_cacheDir + "main_file_cache.dat2", FileMode.Open, FileAccess.Read))
			{
				for (int archiveIndex = startArchive; archiveIndex <= endArchive; archiveIndex++)
				{
					string indexFileString = _cacheDir + "main_file_cache.idx" + archiveIndex;

					if (!File.Exists(indexFileString)) 
						continue;

					FileStream indexFile = File.Open(indexFileString, FileMode.Open, FileAccess.Read);

					int fileCount = (int)indexFile.Length / 6;

					for (int fileIndex = 0; fileIndex < fileCount; fileIndex++)
					{
						bool fileError = false;

						indexFile.Position = fileIndex * 6L;

						uint fileSize = indexFile.ReadBytes(3);
						long startChunkOffset = indexFile.ReadBytes(3) * 520L;

						//Console.WriteLine("New file: archive: {0} file: {1} offset: {3} size: {2}", archiveIndex, fileIndex, fileSize, startChunkOffset);
 
						if (fileSize > 0 && startChunkOffset > 0 && startChunkOffset + fileSize <= cacheFile.Length)
						{
							byte[] buffer = new byte[fileSize];
							int writeOffset = 0;
							long currentChunkOffset = startChunkOffset;

							for (int chunkIndex = 0; writeOffset < fileSize && currentChunkOffset > 0; chunkIndex++)
							{
								cacheFile.Position = currentChunkOffset;

								int chunkSize;
								int checksumFileIndex = 0;

								if (fileIndex < 65536)
								{
									chunkSize = (int)Math.Min(512, fileSize - writeOffset);
								}
								else
								{
									//if file index exceeds 2 bytes, add 65536 and read 2(?) extra bytes
									chunkSize = (int)Math.Min(510, fileSize - writeOffset);

									cacheFile.ReadByte();
									checksumFileIndex = (cacheFile.ReadByte() << 16);
								}

								checksumFileIndex += (int)cacheFile.ReadBytes(2);
								int checksumChunkIndex = (int)cacheFile.ReadBytes(2);
								long nextChunkOffset = cacheFile.ReadBytes(3) * 520L;
								int checksumArchiveIndex = cacheFile.ReadByte();

								//Console.WriteLine("Chunk {2}: archive: {3} file: {1} size: {0} nextoffset: {4}", chunkSize, checksumFileIndex, checksumChunkIndex, checksumArchiveIndex, nextChunkOffset);

								if (checksumFileIndex == fileIndex && checksumChunkIndex == chunkIndex && checksumArchiveIndex == archiveIndex &&
								    nextChunkOffset >= 0 && nextChunkOffset < cacheFile.Length)
								{
									cacheFile.Read(buffer, writeOffset, chunkSize);
									writeOffset += chunkSize;
									currentChunkOffset = nextChunkOffset;
								}
								else
								{
									Console.WriteLine("Ignoring file because a chunk's checksum doesn't match, ideally should not happen.");

									fileError = true;
									break;
								}
							}

							if (fileError) 
								continue;

							//process file
							string outFileDir = _outDir + archiveIndex + "/";
							string outFileName = fileIndex.ToString(CultureInfo.InvariantCulture);

							//remove the first 5 bytes because they are not part of the file
							byte[] tempBuffer = new byte[fileSize - 5];
							Array.Copy(buffer, 5, tempBuffer, 0, fileSize - 5);
							buffer = tempBuffer;
							fileSize -= 5;

							//decompress gzip
							if (buffer.Length > 5 && (buffer[4] << 8) + buffer[5] == 0x1f8b) //gzip
							{
								//remove another 4 non-file bytes
								tempBuffer = new byte[fileSize - 4];
								Array.Copy(buffer, 4, tempBuffer, 0, fileSize - 4);
								buffer = tempBuffer;
								fileSize -= 4;

								GZipStream decompressionStream = new GZipStream(new MemoryStream(buffer), CompressionMode.Decompress);

								int readBytes;
								tempBuffer = new byte[0];

								do
								{
									byte[] readBuffer = new byte[100000];
									readBytes = decompressionStream.Read(readBuffer, 0, 100000);

									int storedBytes = tempBuffer.Length;
									Array.Resize(ref tempBuffer, tempBuffer.Length + readBytes);
									Array.Copy(readBuffer, 0, tempBuffer, storedBytes, readBytes);
								}
								while (readBytes == 100000);

								buffer = tempBuffer;

								Console.WriteLine("File decompressed as gzip.");
							}

							//decompress bzip2
							if (buffer.Length > 9 && buffer[4] == 0x31 && buffer[5] == 0x41 && buffer[6] == 0x59 && buffer[7] == 0x26 && buffer[8] == 0x53 && buffer[9] == 0x59) //bzip2
							{
								//remove another 4 non-file bytes
								tempBuffer = new byte[fileSize - 4];
								Array.Copy(buffer, 4, tempBuffer, 0, fileSize - 4);
								buffer = tempBuffer;
								//fileSize -= 4;

								//prepend file header
								byte[] magic = {
									0x42, 0x5a, //BZ (signature)
									0x68,		//h (version)
									0x31		//*100kB block-size
								};

								tempBuffer = new byte[magic.Length + buffer.Length];
								magic.CopyTo(tempBuffer, 0);
								buffer.CopyTo(tempBuffer, magic.Length);
								buffer = tempBuffer;

								BZip2InputStream decompressionStream = new BZip2InputStream(new MemoryStream(buffer));

								int readBytes;
								tempBuffer = new byte[0];

								do
								{
									byte[] readBuffer = new byte[100000];
									readBytes = decompressionStream.Read(readBuffer, 0, 100000);

									int storedBytes = tempBuffer.Length;
									Array.Resize(ref tempBuffer, tempBuffer.Length + readBytes);
									Array.Copy(readBuffer, 0, tempBuffer, storedBytes, readBytes);
								}
								while (readBytes == 100000);

								buffer = tempBuffer;

								Console.WriteLine("File decompressed as bzip2.");														
							}

							//detect ogg: OggS
							if (buffer.Length > 3 && (buffer[0] << 24) + (buffer[1] << 16) + (buffer[2] << 8) + buffer[3] == 0x4f676753)
								outFileName += ".ogg";

							//detect jaga: JAGA
							if (buffer.Length > 3 && (buffer[0] << 24) + (buffer[1] << 16) + (buffer[2] << 8) + buffer[3] == 0x4a414741)
								outFileName += ".jaga";

							//detect png: .PNG
							if (buffer.Length > 3 && (uint)(buffer[0] << 24) + (buffer[1] << 16) + (buffer[2] << 8) + buffer[3] == 0x89504e47)
								outFileName += ".png";

							//create and write file
							if (!Directory.Exists(outFileDir))
								Directory.CreateDirectory(outFileDir);

							//(over)write file
							if (!File.Exists(outFileDir + outFileName) || overwriteExisting)
							{
								using (FileStream outFile = File.Open(outFileDir + outFileName, FileMode.Create, FileAccess.Write))
								{
									outFile.Write(buffer, 0, buffer.Length);
									Console.WriteLine(outFileDir + outFileName);
								}
							}
							else
								Console.WriteLine("Skipping file because it already exists.");
						}
						else
						{
							Console.WriteLine("Ignoring file because of size or offset.");
						}
					}
				}
			}

			Console.WriteLine("Done extracting files.");
		}

		/// <summary>
		/// Combines the sound files (.jaga &amp; .ogg) in the specified archive (40 for the build it was made on), and puts them into the soundtracks directory.
		/// </summary>
		static void CombineSounds(int archive, string file, bool overwriteExisting, bool mergeIncomplete, bool lossless)
		{
			string archiveDir = _outDir + archive + "/";
			string soundDir = _outDir + "sound/";

			PlatformID platform = Environment.OSVersion.Platform;

			//gather all index files
			string[] indexFiles = Directory.GetFiles(archiveDir, "*.jaga", SearchOption.TopDirectoryOnly);

			//create directories
			if (!Directory.Exists(soundDir + "incomplete/"))
				Directory.CreateDirectory(soundDir + "incomplete/");

			foreach (string indexFileString in indexFiles)
			{
				string indexFileIdString = Path.GetFileNameWithoutExtension(indexFileString);

				//skip all others if file is set
				if (!String.IsNullOrWhiteSpace(file) && indexFileIdString != file)
					continue;

				bool incomplete = false;
				List<string> chunkFiles = new List<string>();

				using (FileStream indexFileStream = File.Open(indexFileString, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					indexFileStream.Position = 32L;

					while (indexFileStream.ReadBytes(4) != 0x4f676753)
					{
						uint fileId = indexFileStream.ReadBytes(4);

						//check if the file exists and add it to the buffer if it does
						if (File.Exists(archiveDir + fileId + ".ogg"))
							chunkFiles.Add(archiveDir + fileId + ".ogg");
						else
							incomplete = true;
					}

					//make sure ~index.ogg is not still being used by SoX
					while (true)
					{
						try
						{
							//copy the index's audio chunk to a temp file so SoX can handle the combining
							using (FileStream tempIndexFile = File.Open("~index.ogg", FileMode.Create, FileAccess.Write, FileShare.None))
							{
								indexFileStream.Position -= 4L; //include OggS
								indexFileStream.CopyTo(tempIndexFile);
								break;
							}
						}
						catch (IOException)
						{
							Thread.Sleep(100);
						}
					}
				}

				if (!incomplete || incomplete && mergeIncomplete)
				{
					string outFile = soundDir + (incomplete ? "incomplete/" : "") + indexFileIdString + "." + (lossless ? "flac" : "ogg");

					if (!overwriteExisting && File.Exists(outFile))
						Console.WriteLine("Skipping track because it already exists.");
					else
					{
						//combine the files with sox
						Console.WriteLine("Running SoX to concatenate ogg audio chunks.");

						Process soxProcess = new Process
						{
							StartInfo =
							{
								FileName = "sox",
								UseShellExecute = false,
							}
						};

						soxProcess.StartInfo.Arguments = "--combine concatenate ~index.ogg";
						chunkFiles.ForEach(str =>
						{
							soxProcess.StartInfo.Arguments += " " + str;
						});
						soxProcess.StartInfo.Arguments += " -C 6 --comment \"Created by RSCacheTool, combined by SoX.\" ~out." + (lossless ? "flac" : "ogg");
						soxProcess.StartInfo.UseShellExecute = false;

						soxProcess.Start();
						soxProcess.WaitForExit();

						if (soxProcess.ExitCode == 0)
						{
							//move to it's final destination
							if (File.Exists(outFile))
								File.Delete(outFile);

							//wait until unlocked (if locked)
							bool moved = false;
							do
							{
								try
								{
									File.Move("~out." + (lossless ? "flac" : "ogg"), outFile);
									moved = true;
								}
								catch (IOException)
								{
									Console.WriteLine("File was locked for moving, retrying in 100ms.");
									Thread.Sleep(100);
								}
							} while (!moved);				

							Console.WriteLine(outFile);
						}
						else
							Console.WriteLine("SoX encountered error code " + soxProcess.ExitCode + " and probably didn't finish processing the files.");
					}
				}
				else
					Console.WriteLine("Skipping track because it's incomplete.");
			}

			Console.WriteLine("Done combining sound.");
		}

		/// <summary>
		/// Tries to parse Archive 17 file 5 to obtain a list of music and their corresponding index file id in archive 40.
		/// Returns a dictionary that can resolve index file id to the name of the track as it appears in-game.
		/// </summary>
		public static void NameMusic(string file, bool incomplete, bool overwrite)
		{
			//the following is based on even more assumptions than normal made while comparing 2 extracted caches, it's therefore probably the first thing to break
			//4B magic number (0x00016902) - 2B a file id? - 2B amount of files (higher than actual entries sometimes) - 2B amount of files

			string resolveFileName = _outDir + "17/5";

			if (File.Exists(resolveFileName))
			{
				using (FileStream resolveFile = File.Open(resolveFileName, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					Dictionary<int, string> trackIdNames = new Dictionary<int, string>();
					Dictionary<uint, int> fileIdTracks = new Dictionary<uint, int>();

					//locate start of names and file ids
					byte[] namesMagicNumber = {
						0x00,
						0x66,
						0x24,
						0x07
					};

					byte[] filesMagicNumber = {
						0x00,
						0x66,
						0x0b,
						0x08
					};

					long namesStartPos = resolveFile.IndexOf(namesMagicNumber);
					long filesStartPos = resolveFile.IndexOf(filesMagicNumber);

					if (namesStartPos != -1 && filesStartPos != -1)
					{
						resolveFile.Position = namesStartPos + 6;
						uint musicCount = resolveFile.ReadBytes(2);
						
						//construct trackIdNames
						Regex regex = new Regex("[" + Regex.Escape(new string(Path.GetInvalidFileNameChars())) + "]");
						for(int i = 0; i < musicCount; i++)
						{
							int trackId = (int)resolveFile.ReadBytes(2);
							string trackName = resolveFile.ReadNullTerminatedString();

							//remove characters that can't be used in files from trackName
							trackName = regex.Replace(trackName, "");

							//add only if the string is of any use
							if (!String.IsNullOrWhiteSpace(trackName))
								trackIdNames.Add(trackId, trackName);
						}

						//construct fileIdTracks
						resolveFile.Position = filesStartPos + 6;
						uint fileCount = resolveFile.ReadBytes(2);
						for (int i = 0; i < fileCount; i++)
						{
							int trackId = (int)resolveFile.ReadBytes(2);
							uint fileId = resolveFile.ReadBytes(4);

							//only add if it doesn't exist already
							if (!fileIdTracks.ContainsKey(fileId))
								fileIdTracks.Add(fileId, trackId);
						}

						//let's do this!
						if (!Directory.Exists(_outDir + "sound/named/"))
							Directory.CreateDirectory(_outDir + "sound/named/");

						foreach (string soundFile in Directory.GetFiles(_outDir + "sound/"))
						{
							string fileIdString = Path.GetFileNameWithoutExtension(soundFile);
							string extension = Path.GetExtension(soundFile);

							if (!String.IsNullOrWhiteSpace(file) && fileIdString != file)
								continue;

							uint fileId;
							if (!uint.TryParse(fileIdString, out fileId)) 
								continue;

							if (!fileIdTracks.ContainsKey(fileId)) 
								continue;

							int trackId = fileIdTracks[fileId];
							if (!trackIdNames.ContainsKey(trackId))
								continue;

							string trackName = trackIdNames[trackId];
							string destFile = _outDir + "sound/named/" + trackName + extension;

							if (File.Exists(destFile) && !overwrite) 
								continue;

							File.Copy(soundFile, destFile, true);
							Console.WriteLine(destFile);
						}

						//redundancy, whatever
						if (incomplete)
						{
							if (!Directory.Exists(_outDir + "sound/named/incomplete/"))
								Directory.CreateDirectory(_outDir + "sound/named/incomplete");

							foreach (string soundFile in Directory.GetFiles(_outDir + "sound/incomplete/"))
							{
								string fileIdString = Path.GetFileNameWithoutExtension(soundFile);
								string extension = Path.GetExtension(soundFile);

								if (!String.IsNullOrWhiteSpace(file) && fileIdString != file)
									continue;

								uint fileId;
								if (!uint.TryParse(fileIdString, out fileId)) 
									continue;

								if (!fileIdTracks.ContainsKey(fileId)) 
									continue;

								int trackId = fileIdTracks[fileId];
								if (!trackIdNames.ContainsKey(trackId)) 
									continue;

								string trackName = trackIdNames[trackId];
								string destFile = _outDir + "sound/named/incomplete/" + trackName + extension;

								if (File.Exists(destFile) && !overwrite) 
									continue;

								File.Copy(soundFile, destFile, true);
								Console.WriteLine(destFile);
							}
						}
					}
					else
						Console.WriteLine("Entry points within resolving file could not be found.");
				}
			}
			else
				Console.WriteLine("File for resolving music names (" + resolveFileName + ") does not exist.");

			Console.WriteLine("Done naming music.");
		}

		/// <summary>
		/// Reads a given amount of unsigned bytes from the stream and combines them into one unsigned integer.
		/// </summary>
		public static uint ReadBytes(this Stream stream, int bytes)
		{
			if (bytes < 1 || bytes > 4)
				throw new ArgumentOutOfRangeException();

			uint result = 0;

			for (int i = 0; i < bytes; i++)
				result += (uint)stream.ReadByte() << (bytes - i - 1) * 8;

			return result;
		}

		/// <summary>
		/// Reads ANSI characters into a string until \0 or EOF occurs.
		/// </summary>
		public static string ReadNullTerminatedString(this Stream stream)
		{
			string result = "";
			int readByte = stream.ReadByte();

			while (readByte > 0)
			{
				result += Encoding.Default.GetString(new byte[] { (byte)readByte });
				readByte = stream.ReadByte();
			}

			return result;
		}

		/// <summary>
		/// Returns the stream location of the matchNumber-th occurence of needle, or -1 when there are no(t enough) matches.
		/// </summary>
		public static long IndexOf(this Stream stream, byte[] needle, int matchNumber = 1, int bufferSize = 10000)
		{
			//for resetting after method
			long startPosition = stream.Position;

			byte[] buffer = new byte[bufferSize];
			int offset = 0, readBytes, matches = 0;

			do
			{
				stream.Position = offset;
				readBytes = stream.Read(buffer, 0, bufferSize);

				for (int pos = 0; pos < readBytes - needle.Length + 1; pos++)
				{
					//try to find the rest of the match if the first byte matches
					int matchIndex = 0;
					while (buffer[pos + matchIndex] == needle[matchIndex])
					{
						//full match found
						if (matchIndex == needle.Length - 1)
						{
							//this is the chosen one, return the position
							if (++matches == matchNumber)
							{
								stream.Position = 0;
								return offset + pos;
							}

							break;
						}

						matchIndex++;
					}
				}

				//don't fully add readBytes, so the next string can find the full match if it started on the end of this buffer but couldn't complete
				offset += readBytes - needle.Length + 1;
			}
			while (readBytes == bufferSize);

			//no result
			stream.Position = startPosition;
			return -1;
		}

	}
}
