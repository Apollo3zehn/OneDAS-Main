﻿using HDF.PInvoke;
using Microsoft.Extensions.Logging.Abstractions;
using OneDas.DataStorage;
using OneDas.Extensibility;
using OneDas.Extension.Csv;
using OneDas.Extension.Famos;
using OneDas.Extension.Mat73;
using OneDas.Hdf.Core;
using OneDas.Hdf.IO;
using OneDas.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace OneDas.Hdf.Explorer.Core
{
    public class HdfDataLoader
    {
        public event EventHandler<ProgressUpdatedEventArgs> ProgressUpdated;

        private CancellationToken _cancellationToken;

        public HdfDataLoader(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        public bool WriteZipFileCampaignEntry(ZipArchive zipArchive, FileGranularity fileGranularity, FileFormat fileFormat, ZipSettings zipSettings)
        {
            // build variable descriptions
            var variableDescriptionSet = new List<VariableDescription>();

            zipSettings.CampaignInfo.Value.ToList().ForEach(variableInfo =>
            {
                variableInfo.Value.ForEach(datasetName =>
                {
                    long groupId = -1;
                    long typeId = -1;
                    long datasetId = -1;

                    try
                    {
                        groupId = H5G.open(zipSettings.SourceFileId, $"{ zipSettings.CampaignInfo.Key }/{ variableInfo.Key }");
                        datasetId = H5D.open(groupId, datasetName);
                        typeId = H5D.get_type(datasetId);

                        var displayName = IOHelper.ReadAttribute<string>(groupId, "name_set").Last();
                        var groupName = IOHelper.ReadAttribute<string>(groupId, "group_set").Last();
                        var unit = IOHelper.ReadAttribute<string>(groupId, "unit_set").LastOrDefault();
                        var hdf_transfer_function_t_set = IOHelper.ReadAttribute<hdf_transfer_function_t>(groupId, "transfer_function_set");
                        var transferFunctionSet = hdf_transfer_function_t_set.Select(tf => new TransferFunction(DateTime.ParseExact(tf.date_time, "yyyy-MM-ddTHH-mm-ssZ", CultureInfo.InvariantCulture), tf.type, tf.option, tf.argument)).ToList();

                        var oneDasDataType = OneDasUtilities.GetOneDasDataTypeFromType(TypeConversionHelper.GetTypeFromHdfTypeId(typeId));
                        var sampleRate = new SampleRateContainer(datasetName);

                        variableDescriptionSet.Add(new VariableDescription(new Guid(variableInfo.Key), displayName, datasetName, groupName, oneDasDataType, sampleRate, unit, transferFunctionSet, DataStorageType.Simple));
                    }
                    finally
                    {
                        if (H5I.is_valid(datasetId) > 0) { H5D.close(datasetId); }
                        if (H5I.is_valid(groupId) > 0) { H5G.close(groupId); }
                        if (H5I.is_valid(typeId) > 0) { H5T.close(typeId); }
                    }
                });
            });

            DataWriterExtensionSettingsBase settings;
            DataWriterExtensionLogicBase dataWriter = null;

            switch (fileFormat)
            {
                case FileFormat.CSV:

                    settings = new CsvSettings() { FileGranularity = fileGranularity };
                    dataWriter = new CsvWriter((CsvSettings)settings, NullLogger.Instance);

                    break;

                case FileFormat.FAMOS:

                    settings = new FamosSettings() { FileGranularity = fileGranularity };
                    dataWriter = new FamosWriter((FamosSettings)settings, NullLogger.Instance);

                    break;

                case FileFormat.MAT73:

                    settings = new Mat73Settings() { FileGranularity = fileGranularity };
                    dataWriter = new Mat73Writer((Mat73Settings)settings, NullLogger.Instance);

                    break;

                default:
                    throw new NotImplementedException();
            }

            // create temp directory
            var directoryPath = Path.Combine(Path.GetTempPath(), "OneDas.Hdf.Explorer", Guid.NewGuid().ToString());
            Directory.CreateDirectory(directoryPath);

            // create custom meta data
            var customMetadataEntrySet = new List<CustomMetadataEntry>();
            //customMetadataEntrySet.Add(new CustomMetadataEntry("system_name", "HDF Explorer", CustomMetadataEntryLevel.File));

            // initialize data writer
            var campaignName_splitted = zipSettings.CampaignInfo.Key.Split('/');
            var dataWriterContext = new DataWriterContext("HDF Explorer", directoryPath, new OneDasCampaignDescription(Guid.Empty, 0, campaignName_splitted[1], campaignName_splitted[2], campaignName_splitted[3]), customMetadataEntrySet);
            dataWriter.Configure(dataWriterContext, variableDescriptionSet);

            // create temp files
            try
            {
                if (!this.CreateFiles(dataWriter, zipSettings))
                {
                    this.CleanUp(directoryPath);

                    return false;
                }
            }
            finally
            {
                dataWriter.Dispose();
            }

            // write zip archive entries
            var filePathSet = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
            var currentFile = 0;
            var fileCount = filePathSet.Count();

            foreach (string filePath in filePathSet)
            {
                var zipArchiveEntry = zipArchive.CreateEntry(Path.GetFileName(filePath), CompressionLevel.Optimal);

                this.OnProgressUpdated(new ProgressUpdatedEventArgs(currentFile / (double)fileCount * 100, $"Writing file {currentFile + 1} / {fileCount} to ZIP archive ..."));

                using (FileStream fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read))
                {
                    using (Stream zipArchiveEntryStream = zipArchiveEntry.Open())
                    {
                        fileStream.CopyTo(zipArchiveEntryStream);
                    }
                }

                currentFile++;
            }

            this.CleanUp(directoryPath);

            return true;
        }

        private bool CreateFiles(DataWriterExtensionLogicBase dataWriter, ZipSettings zipSettings)
        {
            // START progress
            var datasetCount = (ulong)zipSettings.CampaignInfo.Value.SelectMany(variableInfo => variableInfo.Value).Count();
            var currentSegment = 0UL;
            var segmentCount = (ulong)Math.Ceiling(zipSettings.Block / (double)zipSettings.SegmentLength);
            // END progress

            var currentRowCount = zipSettings.SegmentLength;
            var lastRow = zipSettings.Start + zipSettings.Block;

            // for each segment
            for (ulong currentStart = zipSettings.Start; currentStart < lastRow; currentStart += currentRowCount)
            {
                var currentDataset = 0UL;
                var dataStorageSet = new List<IDataStorage>();

                if (currentStart + currentRowCount > zipSettings.Start + zipSettings.Block)
                    currentRowCount = zipSettings.Start + zipSettings.Block - currentStart;

                // load data
                foreach (var variableInfo in zipSettings.CampaignInfo.Value)
                {
                    variableInfo.Value.ForEach(datasetName =>
                    {
                        dataStorageSet.Add(this.LoadDataset(zipSettings.SourceFileId, $"{ zipSettings.CampaignInfo.Key }/{ variableInfo.Key }/{ datasetName }", currentStart, 1, currentRowCount, 1));
                        this.OnProgressUpdated(new ProgressUpdatedEventArgs((currentSegment * (double)datasetCount + currentDataset) / (segmentCount * datasetCount) * 100, $"Loading dataset segment { currentSegment * datasetCount + currentDataset + 1 } / { segmentCount * datasetCount } ..."));
                        currentDataset++;
                    });
                }

                this.OnProgressUpdated(new ProgressUpdatedEventArgs((currentSegment * (double)datasetCount + currentDataset) / (segmentCount * datasetCount) * 100, $"Writing data of segment { currentSegment + 1 } / { segmentCount } ..."));

                var dateTime = zipSettings.DateTimeBegin.AddSeconds((currentStart - zipSettings.Start) / zipSettings.SampleRate);
                var dataStoragePeriod = TimeSpan.FromSeconds(currentRowCount / zipSettings.SampleRate);

                dataWriter.Write(dateTime, dataStoragePeriod, dataStorageSet);

                // clean up
                dataStorageSet = null;
                GC.Collect();

                if (_cancellationToken.IsCancellationRequested)
                    return false;

                currentSegment++;
            }

            return true;
        }

        private ISimpleDataStorage LoadDataset(long sourceFileId, string datasetPath, ulong start, ulong stride, ulong block, ulong count)
        {
            long datasetId = -1;
            long typeId = -1;

            var dataset = IOHelper.ReadDataset(sourceFileId, datasetPath, start, stride, block, count);

            // apply status (only if native dataset)
            if (H5L.exists(sourceFileId, datasetPath + "_status") > 0)
            {
                try
                {
                    datasetId = H5D.open(sourceFileId, datasetPath);
                    typeId = H5D.get_type(datasetId);

                    var dataset_status = IOHelper.ReadDataset(sourceFileId, datasetPath + "_status", start, stride, block, count).Cast<byte>().ToArray();

                    var genericType = typeof(ExtendedDataStorage<>).MakeGenericType(TypeConversionHelper.GetTypeFromHdfTypeId(typeId));
                    var extendedDataStorage = (ExtendedDataStorageBase)Activator.CreateInstance(genericType, dataset, dataset_status);

                    dataset_status = null;

                    var simpleDataStorage = extendedDataStorage.ToSimpleDataStorage();
                    extendedDataStorage.Dispose();

                    return simpleDataStorage;
                }
                finally
                {
                    if (H5I.is_valid(datasetId) > 0) { H5D.close(datasetId); }
                    if (H5I.is_valid(typeId) > 0) { H5T.close(typeId); }
                }
            }
            else
            {
                return new SimpleDataStorage(dataset.Cast<double>().ToArray());
            }
        }

        private void CleanUp(string directoryPath)
        {
            try
            {
                Directory.Delete(directoryPath, true);
            }
            catch
            {
                //
            }
        }

        protected virtual void OnProgressUpdated(ProgressUpdatedEventArgs e)
        {
            this.ProgressUpdated?.Invoke(this, e);
        }
    }
}
