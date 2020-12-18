﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneDas.Buffers;
using OneDas.DataManagement.Database;
using OneDas.DataManagement.Explorer.Core;
using OneDas.DataManagement.Extensibility;
using OneDas.DataManagement.Infrastructure;
using OneDas.Extensibility;
using OneDas.Extension.Csv;
using OneDas.Extension.Famos;
using OneDas.Extension.Mat73;
using OneDas.Infrastructure;
using OneDas.Types;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OneDas.DataManagement.Explorer.Services
{
    public class DataService
    {
        #region Fields

        private ILogger _logger;
        private OneDasDatabaseManager _databaseManager;
        private OneDasExplorerOptions _options;
        private ulong _blockSizeLimit;

        #endregion

        #region Constructors

        public DataService(OneDasDatabaseManager databaseManager,
                           ILoggerFactory loggerFactory,
                           OneDasExplorerOptions options)
        {
            _databaseManager = databaseManager;
            _logger = loggerFactory.CreateLogger("OneDAS Explorer");
            _options = options;
            _blockSizeLimit = 5 * 1000 * 1000UL;

            this.Progress = new Progress<ProgressUpdatedEventArgs>();
        }

        #endregion

        #region Properties

        public Progress<ProgressUpdatedEventArgs> Progress { get; }

        #endregion

        #region Methods

        public Task<List<AvailabilityResult>> GetAvailabilityAsync(string projectId, DateTime begin, DateTime end)
        {
            return Task.Run(() =>
            {
                var dataReaders = _databaseManager.GetDataReaders(projectId);

                return dataReaders.Select(dataReaderForUsing =>
                {
                    using var dataReader = dataReaderForUsing;
                    return dataReader.GetAvailability(projectId, begin, end);
                }).ToList();
            });
        }

        public Task<string> ExportDataAsync(string username,
                                            ExportParameters exportParameters,
                                            List<DatasetInfo> datasets,
                                            CancellationToken cancellationToken)
        {
            if (!datasets.Any() || exportParameters.Begin == exportParameters.End)
                return Task.FromResult(string.Empty);

            // find sample rate
            var sampleRates = datasets.Select(dataset => dataset.GetSampleRate());

            if (sampleRates.Select(sampleRate => sampleRate.SamplesPerSecond).Distinct().Count() > 1)
                throw new ValidationException("Channels with different sample rates have been requested.");

            return Task.Run(() =>
            {
                var sampleRate = sampleRates.First();

                // log
                var message = $"User '{username}' exports data: {exportParameters.Begin.ToISO8601()} to {exportParameters.End.ToISO8601()} ... ";
                _logger.LogInformation(message);

                // zip file path
                var zipFilePath = Path.Combine(_options.ExportDirectoryPath, $"OneDAS_{exportParameters.Begin.ToString("yyyy-MM-ddTHH-mm")}_{sampleRate.ToUnitString(underscore: true)}_{Guid.NewGuid().ToString().Substring(0, 8)}.zip");

                try
                {
                    // convert datasets into projects
                    var projectIds = datasets.Select(dataset => dataset.Parent.Parent.Id).Distinct();
                    var projectContainers = _databaseManager.Database.ProjectContainers
                        .Where(projectContainer => projectIds.Contains(projectContainer.Id));

                    var sparseProjects = projectContainers.Select(projectContainer =>
                    {
                        var currentDatasets = datasets.Where(dataset => dataset.Parent.Parent.Id == projectContainer.Id).ToList();
                        return projectContainer.ToSparseProject(currentDatasets);
                    });

                    // start
                    using var zipArchive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create);

                    foreach (var sparseProject in sparseProjects)
                    {
                        this.WriteZipFileProjectEntry(zipArchive, exportParameters, sparseProject, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{message} Fail. Reason: {ex.GetFullMessage()}");
                    throw;
                }

                _logger.LogInformation($"{message} Done.");

                return $"export/{Path.GetFileName(zipFilePath)}";
            }, cancellationToken);
        }

        private void WriteZipFileProjectEntry(ZipArchive zipArchive,
                                              ExportParameters exportParameters,
                                              SparseProjectInfo sparseProject,
                                              CancellationToken cancellationToken)
        {
            var channelDescriptionSet = sparseProject.ToChannelDescriptions();
            var singleFile = exportParameters.FileGranularity == FileGranularity.SingleFile;

            TimeSpan filePeriod;

            if (singleFile)
                filePeriod = exportParameters.End - exportParameters.Begin;
            else
                filePeriod = TimeSpan.FromSeconds((int)exportParameters.FileGranularity);

            DataWriterExtensionSettingsBase settings;
            DataWriterExtensionLogicBase dataWriter;

            switch (exportParameters.FileFormat)
            {
                case FileFormat.CSV:

                    settings = new CsvSettings()
                    {
                        FilePeriod = filePeriod,
                        SingleFile = singleFile,
                        RowIndexFormat = exportParameters.CsvRowIndexFormat,
                        SignificantFigures = exportParameters.CsvSignificantFigures,
                    };

                    dataWriter = new CsvWriter((CsvSettings)settings, NullLogger.Instance);

                    break;

                case FileFormat.FAMOS:

                    settings = new FamosSettings()
                    {
                        FilePeriod = filePeriod,
                        SingleFile = singleFile,
                    };

                    dataWriter = new FamosWriter((FamosSettings)settings, NullLogger.Instance);

                    break;

                case FileFormat.MAT73:

                    settings = new Mat73Settings()
                    {
                        FilePeriod = filePeriod,
                        SingleFile = singleFile,
                    };

                    dataWriter = new Mat73Writer((Mat73Settings)settings, NullLogger.Instance);

                    break;

                default:
                    throw new NotImplementedException();
            }

            // create temp directory
            var directoryPath = Path.Combine(Path.GetTempPath(), "OneDas.DataManagement.Explorer", Guid.NewGuid().ToString());
            Directory.CreateDirectory(directoryPath);

            // create custom meta data
            var customMetadataEntrySet = new List<CustomMetadataEntry>();
            //customMetadataEntrySet.Add(new CustomMetadataEntry("system_name", "OneDAS Explorer", CustomMetadataEntryLevel.File));

            if (!string.IsNullOrWhiteSpace(sparseProject.License.FileMessage))
                customMetadataEntrySet.Add(new CustomMetadataEntry("license", sparseProject.License.FileMessage, CustomMetadataEntryLevel.Project));

            // initialize data writer
            var projectName_splitted = sparseProject.Id.Split('/');
            var dataWriterContext = new DataWriterContext("OneDAS Explorer", directoryPath, new OneDasProjectDescription(Guid.Empty, 0, projectName_splitted[1], projectName_splitted[2], projectName_splitted[3]), customMetadataEntrySet);
            dataWriter.Configure(dataWriterContext, channelDescriptionSet);

            try
            {
                // create temp files
                this.CreateFiles(dataWriter, exportParameters, sparseProject, cancellationToken);
                dataWriter.Dispose();

                // write zip archive entries
                var filePathSet = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
                var currentFile = 0;
                var fileCount = filePathSet.Count();

                foreach (string filePath in filePathSet)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var zipArchiveEntry = zipArchive.CreateEntry(Path.GetFileName(filePath), CompressionLevel.Optimal);

                    this.OnProgress(new ProgressUpdatedEventArgs(currentFile / (double)fileCount, $"Writing file {currentFile + 1} / {fileCount} to ZIP archive ..."));

                    using var fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read);
                    using var zipArchiveEntryStream = zipArchiveEntry.Open();

                    fileStream.CopyTo(zipArchiveEntryStream);
                    currentFile++;
                }
            }
            finally
            {
                this.CleanUp(directoryPath);
                dataWriter.Dispose();
            }
        }

        private void CreateFiles(DataWriterExtensionLogicBase dataWriter,
                                 ExportParameters exportParameters,
                                 SparseProjectInfo sparseProject,
                                 CancellationToken cancellationToken)
        {
            var datasets = sparseProject.Channels.SelectMany(channel => channel.Datasets);
            var registrationToDatasetsMap = new Dictionary<DataReaderRegistration, List<DatasetInfo>>();

            foreach (var dataset in datasets)
            {
                if (!registrationToDatasetsMap.ContainsKey(dataset.Registration))
                    registrationToDatasetsMap[dataset.Registration] = new List<DatasetInfo>();

                registrationToDatasetsMap[dataset.Registration].Add(dataset);
            }

            var progressHandler = (EventHandler<double>)((sender, e) =>
            {
                this.OnProgress(new ProgressUpdatedEventArgs(e, $"Loading data ..."));
            });

            foreach (var entry in registrationToDatasetsMap)
            {
                if (entry.Value.Any())
                {
                    var registration = entry.Key;
                    var dataReader = _databaseManager.GetDataReader(registration);
                    dataReader.Progress.ProgressChanged += progressHandler;

                    var applyStatus = dataReader.ApplyStatus;

                    try
                    {
                        foreach (var progressRecord in dataReader.Read(entry.Value, exportParameters.Begin, exportParameters.End, _blockSizeLimit, cancellationToken))
                        {
                            this.ProcessData(dataWriter, progressRecord, applyStatus);
                        }
                    }
                    finally
                    {
                        dataReader.Progress.ProgressChanged -= progressHandler;
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        private void ProcessData(DataWriterExtensionLogicBase dataWriter, DataReaderProgressRecord progressRecord, bool applyStatus)
        {
            var buffers = progressRecord.DatasetToRecordMap.Values.Select(dataRecord =>
            {
                double[] data;

                if (applyStatus)
                    data = BufferUtilities.ApplyDatasetStatus2(dataRecord.Dataset, dataRecord.Status);
                else
                    data = (double[])dataRecord.Dataset;

                return (IBuffer)BufferUtilities.CreateSimpleBuffer(data);
            }).ToList();

            var period = progressRecord.End - progressRecord.Begin;
            dataWriter.Write(progressRecord.Begin, period, buffers);

            // clean up
            buffers = null;
            GC.Collect();
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

        private void OnProgress(ProgressUpdatedEventArgs e)
        {
            ((IProgress<ProgressUpdatedEventArgs>)this.Progress).Report(e);
        }

        #endregion
    }
}