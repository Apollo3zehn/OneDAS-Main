﻿using HDF.PInvoke;
using IwesOneDas;
using Microsoft.Extensions.Logging;
using OneDas.DataManagement.Database;
using OneDas.DataManagement.Extensibility;
using OneDas.DataManagement.Hdf;
using OneDas.Extensibility;
using OneDas.Infrastructure;
using OneDas.Types;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OneDas.DataManagement.Extensions
{
    [ExtensionIdentification("OneDas.HDF", "OneDAS HDF", "Provides access to databases with OneDAS HDF files.", "", "")]
    public class HdfDataReader : DataReaderExtensionBase
    {
        #region Constructors

        public HdfDataReader(string rootPath, ILogger logger) : base(rootPath, logger)
        {
            //
        }

        #endregion

        #region Methods

#warning Unify this with other readers
        public override (T[] Dataset, byte[] StatusSet) ReadSingle<T>(DatasetInfo dataset, DateTime begin, DateTime end)
        {
            long fileId = -1;

            var variable = (VariableInfo)dataset.Parent;
            var folderPath = Path.Combine(this.RootPath, "DATA");

            var samplesPerDay = new SampleRateContainer(dataset.Id).SamplesPerDay;
            var length = (long)((end - begin).TotalSeconds * 86400 * samplesPerDay);
            var data = new T[length];
            var statusSet = new byte[length];

            if (!Directory.Exists(folderPath))
                return (data, statusSet);

            var periodPerFile = TimeSpan.FromDays(1);

            // read data
            var currentBegin = begin.RoundDown(periodPerFile);
            var totalOffset = 0L;
            var remainingLength = length;
            var fileLength = (long)(periodPerFile.TotalSeconds * 86400 * samplesPerDay);

            while (remainingLength > 0)
            {
                // get data
                var fileName = $"{currentBegin.ToString("yyyy-MM-dd_HH-mm-ss")}.dat";
                var filePath = Path.Combine(folderPath, currentBegin.ToString("yyyy-MM"), fileName);

                if (File.Exists(filePath))
                {
                    fileId = H5F.open(filePath, H5F.ACC_RDONLY);

                    try
                    {
                        if (H5I.is_valid(fileId) > 0)
                        {
                            var datasetPath = dataset.GetPath();

                            if (IOHelper.CheckLinkExists(fileId, datasetPath))
                            {
                                // invoke generic 'ReadData' method
                                var methodName = nameof(HdfDataReader.ReadData);
                                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                                var genericType = OneDasUtilities.GetTypeFromOneDasDataType(dataset.DataType);
                                var parameters = new object[] { fileId, datasetPath, start, block };
                                var result = ((T[] Dataset, byte[] StatusSet))OneDasUtilities.InvokeGenericMethod(this, methodName, flags, genericType, parameters);

                                // write data
                                var currentLength = Math.Min(remainingLength, block);
                                result.Dataset[..currentLength].CopyTo(data.AsSpan(totalOffset));
                                result.StatusSet.CopyTo(statusSet.AsSpan(totalOffset));
                            }
                        }
                        else
                        {
                            this.Logger.LogError($"Could not open file '{filePath}'.");
                        }
                    }
                    finally
                    {
                        if (H5I.is_valid(fileId) > 0) { H5F.close(fileId); }
                    }
                }

                // update loop state
                totalOffset += fileLength;
                remainingLength -= fileLength;
                currentBegin += periodPerFile;
            }

            return (data, statusSet);
        }

        protected override List<CampaignInfo> LoadCampaigns()
        {
            // (0) load versioning file
            var versioningFilePath = Path.Combine(this.RootPath, "versioning.json");

            var versioning = File.Exists(versioningFilePath)
                ? HdfVersioning.Load(versioningFilePath)
                : new HdfVersioning();

            // (1) find beginning of database
            var dataFolderPath = Path.Combine(this.RootPath, "DATA");
            Directory.CreateDirectory(dataFolderPath);

            var firstMonthString = Path.GetFileName(Directory.EnumerateDirectories(dataFolderPath).FirstOrDefault());

            if (firstMonthString == null)
                return new List<CampaignInfo>();

            var firstMonth = DateTime.ParseExact(firstMonthString, "yyyy-MM", CultureInfo.InvariantCulture);

            // (2) for each month
            var now = DateTime.UtcNow;
            var months = ((now.Year - firstMonth.Year) * 12) + now.Month - firstMonth.Month + 1;
            var currentMonth = firstMonth;

            var cacheFolderPath = Path.Combine(this.RootPath, "CACHE");
            var mainCacheFilePath = Path.Combine(cacheFolderPath, "main.json");
            Directory.CreateDirectory(cacheFolderPath);

            bool cacheChanged = false;

            for (int i = 0; i < months; i++)
            {
                var currentDataFolderPath = Path.Combine(dataFolderPath, currentMonth.ToString("yyyy-MM"));

                // (3) find available campaign ids by scanning file contents (optimized)
                var campaignIds = this.FindCampaignIds(Path.Combine(dataFolderPath, currentMonth.ToString("yyyy-MM")));

                // (4) find corresponding cache file
                var cacheFilePath = Path.Combine(cacheFolderPath, $"{currentMonth.ToString("yyyy-MM")}.json");

                List<CampaignInfo> cache;

                // (5.a) cache file exists
                if (File.Exists(cacheFilePath))
                {
                    cache = JsonSerializerHelper.Deserialize<List<CampaignInfo>>(cacheFilePath);
                    cache.ForEach(campaign => campaign.Initialize());

                    foreach (var campaignId in campaignIds)
                    {
                        var campaign = cache.FirstOrDefault(campaign => campaign.Id == campaignId);

                        // campaign is in cache ...
                        if (campaign != null)
                        {
                            // ... but cache is outdated
                            if (this.IsCacheOutdated(campaignId, currentDataFolderPath, versioning))
                            {
                                campaign = this.ScanFiles(campaignId, currentDataFolderPath, versioning);
                                cacheChanged = true;
                            }
                        }
                        // campaign is not in cache
                        else
                        {
                            campaign = this.ScanFiles(campaignId, currentDataFolderPath, versioning);
                            cache.Add(campaign);
                            cacheChanged = true;
                        }
                    }
                }
                // (5.b) cache file does not exist
                else
                {
                    cache = campaignIds.Select(campaignId =>
                    {
                        var campaign = this.ScanFiles(campaignId, currentDataFolderPath, versioning);
                        cacheChanged = true;
                        return campaign;
                    }).ToList();
                }

                // (6) save cache and versioning files
                if (cacheChanged)
                {
                    JsonSerializerHelper.Serialize(cache, cacheFilePath);
                    JsonSerializerHelper.Serialize(versioning, versioningFilePath);
                }

                currentMonth = currentMonth.AddMonths(1);
            }

            // (7) update main cache
            List<CampaignInfo> mainCache;

            if (cacheChanged || !File.Exists(mainCacheFilePath))
            {
                var cacheFiles = Directory.EnumerateFiles(cacheFolderPath, "*-*.json");
                mainCache = new List<CampaignInfo>();

                var message = "Merging cache files to main cache ...";

                try
                {
                    this.Logger.LogInformation(message);

                    foreach (var cacheFile in cacheFiles)
                    {
                        var cache = JsonSerializerHelper.Deserialize<List<CampaignInfo>>(cacheFile);
                        cache.ForEach(campaign => campaign.Initialize());

                        foreach (var campaign in cache)
                        {
                            var reference = mainCache.FirstOrDefault(current => current.Id == campaign.Id);

                            if (reference != null)
                                reference.Merge(campaign, VariableMergeMode.NewWins);
                            else
                                mainCache.Add(campaign);
                        }
                    }

                    this.Logger.LogInformation($"{message} Done.");
                }
                catch (Exception ex)
                {
                    this.Logger.LogError($"{message} Error: {ex.GetFullMessage()}");
                    throw;
                }

                JsonSerializerHelper.Serialize(mainCache, mainCacheFilePath);
            }
            else
            {
                mainCache = JsonSerializerHelper.Deserialize<List<CampaignInfo>>(mainCacheFilePath);
            }

            // update campaign start and end
            foreach (var campaign in mainCache)
            {
                var searchPattern = $"{this.ToUnderscoredId(campaign.Id)}*.h5";
                var files = Directory.EnumerateFiles(dataFolderPath, searchPattern, SearchOption.AllDirectories);
                var firstDateTime = this.GetFirstDateTime(files);

                campaign.CampaignStart = firstDateTime;
                campaign.CampaignEnd = versioning.ScannedUntilMap[campaign.Id].AddDays(1);
            }

            return mainCache;
        }

        protected override double GetDataAvailability(string campaignId, DateTime day)
        {
            if (!this.Campaigns.Any(campaign => campaign.Id == campaignId))
                throw new Exception($"The campaign '{campaignId}' could not be found.");

            var fileNamePattern = $"{this.ToUnderscoredId(campaignId)}_*_{day.ToString("yyyy-MM-ddTHH-mm-ssZ")}.h5";
            var folderName = day.ToString("yyyy-MM");
            var folderPath = Path.Combine(this.RootPath, "DATA", folderName);
            var fileCount = Directory.EnumerateFiles(folderPath, fileNamePattern, SearchOption.AllDirectories).Count();

            return fileCount > 0 ? 1 : 0;
        }

        private List<string> FindCampaignIds(string dataFolderPath)
        {
            var distinctFiles = Directory
                .EnumerateFiles(dataFolderPath, "*.h5")
                .Select(filePath =>
                {
                    var fileName = Path.GetFileName(filePath);
                    return fileName.Substring(0, fileName.Length - 24);
                })
                .Distinct();

            return distinctFiles.Select(distinctFile =>
            {
                var filePath = Directory.EnumerateFiles(dataFolderPath, $"{distinctFile}*.h5").First();
                var campaignId = GeneralHelper.GetCampaignIdFromFile(filePath);
                return campaignId;
            }).Distinct().ToList();
        }

        private bool IsCacheOutdated(string campaignId, string dataFolderPath, HdfVersioning versioning)
        {
            var searchPattern = $"{this.ToUnderscoredId(campaignId)}*.h5";
            var files = Directory.EnumerateFiles(dataFolderPath, searchPattern);
            var lastDateTime = this.GetLastDateTime(files);
            return lastDateTime > versioning.ScannedUntilMap[campaignId];
        }

        private CampaignInfo ScanFiles(string campaignId, string dataFolderPath, HdfVersioning versioning)
        {
            var message = $"Scanning files for {Path.GetFileName(dataFolderPath)} ({campaignId}) ...";
            var searchPattern = $"{this.ToUnderscoredId(campaignId)}*.h5";
            var files = Directory.EnumerateFiles(dataFolderPath, searchPattern);
            var campaign = new CampaignInfo(campaignId);

            this.Logger.LogInformation(message);

            try
            {
                foreach (var file in files)
                {
                    var fileId = H5F.open(file, H5F.ACC_RDONLY);

                    try
                    {
                        if (H5I.is_valid(fileId) > 0)
                        {
                            var newCampaign = GeneralHelper.GetCampaign(fileId, campaignId);
                            campaign.Merge(newCampaign, VariableMergeMode.NewWins);
                        }
                    }
                    finally
                    {
                        if (H5I.is_valid(fileId) > 0) { H5F.close(fileId); }
                    }
                }

                // update scanned until
                var scannedUntil = this.GetLastDateTime(files);

                if (versioning.ScannedUntilMap.TryGetValue(campaignId, out var value))
                {
                    if (scannedUntil > value)
                        versioning.ScannedUntilMap[campaignId] = scannedUntil;
                }
                else
                {
                    versioning.ScannedUntilMap[campaignId] = scannedUntil;
                }

                this.Logger.LogInformation($"{message} Done.");
            }
            catch (Exception ex)
            {
                this.Logger.LogError($"{message} Error: {ex.GetFullMessage()}");
            }

            return campaign;
        }

        private DateTime GetFirstDateTime(IEnumerable<string> files)
        {
            if (files.Any())
            {
                var firstFile = files.First();
                var dateString = firstFile.Substring(firstFile.Length - 23, 20);
                var dateTime = DateTime.ParseExact(dateString, "yyyy-MM-ddTHH-mm-ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

                return dateTime.ToUniversalTime();
            }
            else
            {
                return DateTime.MinValue;
            }
        }

        private DateTime GetLastDateTime(IEnumerable<string> files)
        {
            if (files.Any())
            {
                var lastFile = files.Last();
                var dateString = lastFile.Substring(lastFile.Length - 23, 20);
                var dateTime = DateTime.ParseExact(dateString, "yyyy-MM-ddTHH-mm-ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

                return dateTime.ToUniversalTime();
            }
            else
            {
                return DateTime.MaxValue;
            }
        }

        private string ToUnderscoredId(string campaignId)
        {
            return campaignId.Replace('/', '_').TrimStart('_');
        }

        private (T[], byte[]) ReadData<T>(long fileId, string datasetPath, ulong start, ulong block) where T : unmanaged
        {
            var data = IOHelper.ReadDataset<T>(fileId, datasetPath, start, block);

            byte[] statusSet = null;

            if (IOHelper.CheckLinkExists(fileId, datasetPath + "_status"))
                statusSet = IOHelper.ReadDataset(fileId, datasetPath + "_status", start, block).Cast<byte>().ToArray();

            return (data, statusSet);
        }

        #endregion
    }
}
