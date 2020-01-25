﻿using HDF.PInvoke;
using MathNet.Numerics.Statistics;
using Microsoft.Extensions.Logging;
using OneDas.Database;
using OneDas.DataManagement.Extensibility;
using OneDas.DataManagement.Hdf;
using OneDas.DataStorage;
using OneDas.Hdf.Explorer.Core;
using OneDas.Infrastructure;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OneDas.Hdf.Explorer.Commands
{
    public class AggregateCommand
    {
        #region Fields

        private HdfExplorerOptions _options;
        private AggregationMethod _method;
        private string _argument;
        private string _campaignName;
        private uint _days;
        private Dictionary<string, string> _filters;
        private ILogger _logger;

        #endregion

        #region Constructors

        public AggregateCommand(HdfExplorerOptions options, AggregationMethod method, string argument, string campaignName, uint days, Dictionary<string, string> filters, ILogger logger)
        {
            _options = options;
            _method = method;
            _argument = argument;
            _campaignName = campaignName;
            _days = days;
            _logger = logger;
            _filters = filters;
        }

        #endregion

        #region Methods

        public void Run()
        {
            var epochEnd = DateTime.UtcNow.Date;
            var epochStart = epochEnd.AddDays(-_days);

            for (int i = 0; i <= _days; i++)
            {
                this.CreateAggregatedFiles(epochStart.AddDays(i), _campaignName);
            }
        }

        private void CreateAggregatedFiles(DateTime dateTimeBegin, string campaignName)
        {
            var subfolderName = dateTimeBegin.ToString("yyyy-MM");
            var targetDirectoryPath = Path.Combine(Environment.CurrentDirectory, "DB_AGGREGATION", subfolderName);

            using var dataReader = Program.GetDatabase(campaignName).GetDataReader(campaignName, dateTimeBegin);

            // get files
            if (!dataReader.IsDataAvailable)
                return;

            // process data
            try
            {
                foreach (var version in dataReader.GetVersions())
                {
                    var targetFileId = -1L;

                    // campaignInfo
                    var campaignInfo = dataReader.GetCampaignInfo();

                    // targetFileId
                    var campaignFileName = campaignName.TrimStart('/').Replace("/", "_");
                    var dateTimeFileName = dateTimeBegin.ToString("yyyy-MM-ddTHH-mm-ssZ");
                    var targetFileName = $"{campaignFileName}_V{version}_{dateTimeFileName}.h5";
                    var targetFilePath = Path.Combine(targetDirectoryPath, targetFileName);

                    if (!Directory.Exists(targetDirectoryPath))
                        Directory.CreateDirectory(targetDirectoryPath);

                    if (File.Exists(targetFilePath))
                        targetFileId = H5F.open(targetFilePath, H5F.ACC_RDWR);

                    if (targetFileId == -1)
                        targetFileId = H5F.create(targetFilePath, H5F.ACC_TRUNC);

                    try
                    {
                        // create attribute if necessary
                        if (H5A.exists(targetFileId, "date_time") == 0)
                        {
                            var dateTimeString = dateTimeBegin.ToString("yyyy-MM-ddTHH-mm-ssZ");
                            IOHelper.PrepareAttribute(targetFileId, "date_time", new string[] { dateTimeString }, new ulong[] { 1 }, true);
                        }

                        // campaignInfo
                        this.AggregateCampaign(dataReader, campaignInfo, targetFileId);
                    }
                    finally
                    {
                        if (H5I.is_valid(targetFileId) > 0) { H5F.close(targetFileId); }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }
        }

        private void AggregateCampaign(DataReaderExtensionBase dataReader, CampaignInfo campaign, long targetFileId)
        {
            var datasetPath = $"{campaign.GetPath()}/is_chunk_completed_set";
            (var groupId, var isNew) = IOHelper.OpenOrCreateGroup(targetFileId, campaign.GetPath());

            try
            {
                if (isNew || !IOHelper.CheckLinkExists(targetFileId, datasetPath))
                {
                    var datasetId = IOHelper.OpenOrCreateDataset(groupId, datasetPath, TypeConversionHelper.GetHdfTypeIdFromType(typeof(byte)), 1440, 1).DatasetId;
                    (var start, var end) = dataReader.GetCompletedChunkBounds();

                    IOHelper.WriteDataset(datasetId, "is_chunk_completed_set", this.GetCompletedChunks(start, end));
                }

                var filteredVariables = campaign.VariableInfos.Where(variableInfo => this.ApplyAggregationFilter(variableInfo)).ToList();

                foreach (var filteredVariable in filteredVariables)
                {
                    var dataset = filteredVariable.DatasetInfos.First();

                    GeneralHelper.InvokeGenericMethod(typeof(AggregateCommand), this, nameof(this.OrchestrateAggregation),
                                                      BindingFlags.Instance | BindingFlags.NonPublic,
                                                      OneDasUtilities.GetTypeFromOneDasDataType(dataset.DataType),
                                                      new object[] { dataReader, dataset, targetFileId });
                }
            }
            finally
            {
                if (H5I.is_valid(groupId) > 0) { H5G.close(groupId); }
            }
        }

        private void OrchestrateAggregation<T>(DataReaderExtensionBase dataReader, DatasetInfo dataset, long targetFileId) where T : unmanaged
        {
            // value size
            var valueSize = OneDasUtilities.SizeOf(dataset.DataType);

            // check source sample rate
            var sampleRateContainer = new SampleRateContainer(dataset.Name);

            if (!sampleRateContainer.IsPositiveNonZeroIntegerHz)
                throw new NotSupportedException($"Only positive non-zero integer frequencies are supported, but '{dataset.Name}' data were provided.");

            // prepare period data
            var groupPath = dataset.Parent.GetPath();
            var periodToDataMap = new Dictionary<Period, PeriodData>();
            var actualPeriods = new List<Period>();

            try
            {
                foreach (Period period in Enum.GetValues(typeof(Period)))
                {
                    var targetDatasetPath = $"{groupPath}/{(int)period}s_{_method}";

                    if (!IOHelper.CheckLinkExists(targetFileId, targetDatasetPath))
                    {
                        var periodData = new PeriodData(period, sampleRateContainer, valueSize);
                        var bufferSize = (ulong)periodData.Buffer.Length;
                        var datasetId = IOHelper.OpenOrCreateDataset(targetFileId, targetDatasetPath, H5T.NATIVE_DOUBLE, bufferSize, 1).DatasetId;

                        if (!(H5I.is_valid(datasetId) > 0))
                            throw new FormatException($"Could not open dataset '{targetDatasetPath}'.");

                        periodData.DatasetId = datasetId;
                        periodToDataMap[period] = periodData;
                        actualPeriods.Add(period);
                    }
                    else
                    {
                        // skip period
                    }
                }
                    
                // read data
                dataReader.ReadFullDay<T>(dataset, TimeSpan.FromMinutes(10), (data, statusSet) =>
                {
                    // get aggregation data
                    var periodToPartialBufferMap = this.ApplyAggregationFunction(dataset, data, statusSet, periodToDataMap);

                    // copy aggregation data into buffer
                    foreach (Period period in actualPeriods)
                    {
                        var partialBuffer = periodToPartialBufferMap[period];
                        var periodData = periodToDataMap[period];

                        Array.Copy(partialBuffer, 0, periodData.Buffer, periodData.BufferPosition, partialBuffer.Length);

                        periodData.BufferPosition += partialBuffer.Length;
                    }
                });

                // write data to file
                foreach (Period period in actualPeriods)
                {
                    var periodData = periodToDataMap[period];

                    IOHelper.Write(periodData.DatasetId, periodData.Buffer, DataContainerType.Dataset);
                    H5F.flush(periodData.DatasetId, H5F.scope_t.LOCAL);
                }
            }
            finally
            {
                foreach (Period period in actualPeriods)
                {
                    var datasetId = periodToDataMap[period].DatasetId;
                    if (H5I.is_valid(datasetId) > 0) { H5D.close(datasetId); }
                }
            }
        }

        private Dictionary<Period, double[]> ApplyAggregationFunction<T>(DatasetInfo dataset, T[] data, byte[] statusSet, Dictionary<Period, PeriodData> periodToDataMap)
        {
            var dataset_double = default(double[]);
            var periodToPartialBufferMap = new Dictionary<Period, double[]>();

            foreach (var item in periodToDataMap)
            {
                var period = item.Key;
                var periodData = item.Value;

                switch (_method)
                {
                    case AggregationMethod.Mean:
                    case AggregationMethod.MeanPolar:
                    case AggregationMethod.Min:
                    case AggregationMethod.Max:
                    case AggregationMethod.Std:
                    case AggregationMethod.Rms:

                        if (dataset_double == null)
                            dataset_double = ExtendedDataStorageBase.ApplyDatasetStatus(data, statusSet);

                        periodToPartialBufferMap[period] = this.ApplyAggregationFunction(_method, _argument, chunkCount, dataset_double, _logger);

                        break;

                    case AggregationMethod.MinBitwise:
                    case AggregationMethod.MaxBitwise:

                        periodToPartialBufferMap[period] = this.ApplyAggregationFunction(_method, _argument, chunkCount, data, statusSet, _logger);

                        break;

                    default:

                        _logger.LogWarning($"The aggregation method '{_method}' is not known. Skipping period {period}.");

                        continue;
                }
            }

            return periodToPartialBufferMap;
        }

        private double[] ApplyAggregationFunction(AggregationMethod method, string argument, int targetDatasetLength, double[] valueSet, ILogger logger)
        {
            var chunkSize = valueSet.Count() / targetDatasetLength;
            var result = new double[targetDatasetLength];

            switch (method)
            {
                case AggregationMethod.Mean:

                    Parallel.For(0, targetDatasetLength, x =>
                    {
                        var baseIndex = x * chunkSize;
                        var chunkValueSet = new double[chunkSize];

                        Array.Copy(valueSet, baseIndex, chunkValueSet, 0, chunkSize);

                        result[x] = ArrayStatistics.Mean(chunkValueSet);
                    });

                    break;

                case AggregationMethod.MeanPolar:

                    double[] sin = new double[targetDatasetLength];
                    double[] cos = new double[targetDatasetLength];
                    double limit;

                    if (argument.Contains("*PI"))
                        limit = Double.Parse(argument.Replace("*PI", "")) * Math.PI;
                    else
                        limit = Double.Parse(argument);

                    var factor = 2 * Math.PI / limit;

                    Parallel.For(0, targetDatasetLength, x =>
                    {
                        var baseIndex = x * chunkSize;

                        for (int i = 0; i < chunkSize; i++)
                        {
                            sin[x] += Math.Sin(valueSet[baseIndex + i] * factor);
                            cos[x] += Math.Cos(valueSet[baseIndex + i] * factor);
                        }

                        result[x] = Math.Atan2(sin[x], cos[x]) / factor;

                        if (result[x] < 0)
                            result[x] += limit;
                    });

                    break;

                case AggregationMethod.Min:

                    Parallel.For(0, targetDatasetLength, x =>
                    {
                        var baseIndex = x * chunkSize;
                        var chunkValueSet = new double[chunkSize];

                        Array.Copy(valueSet, baseIndex, chunkValueSet, 0, chunkSize);

                        result[x] = ArrayStatistics.Minimum(chunkValueSet);
                    });

                    break;

                case AggregationMethod.Max:

                    Parallel.For(0, targetDatasetLength, x =>
                    {
                        var baseIndex = x * chunkSize;
                        var chunkValueSet = new double[chunkSize];

                        Array.Copy(valueSet, baseIndex, chunkValueSet, 0, chunkSize);

                        result[x] = ArrayStatistics.Maximum(chunkValueSet);
                    });

                    break;

                case AggregationMethod.Std:

                    Parallel.For(0, targetDatasetLength, x =>
                    {
                        var baseIndex = x * chunkSize;
                        var chunkValueSet = new double[chunkSize];

                        Array.Copy(valueSet, baseIndex, chunkValueSet, 0, chunkSize);

                        result[x] = ArrayStatistics.StandardDeviation(chunkValueSet);
                    });

                    break;

                case AggregationMethod.Rms:

                    Parallel.For(0, targetDatasetLength, x =>
                    {
                        var baseIndex = x * chunkSize;
                        var chunkValueSet = new double[chunkSize];

                        Array.Copy(valueSet, baseIndex, chunkValueSet, 0, chunkSize);

                        result[x] = ArrayStatistics.RootMeanSquare(chunkValueSet);
                    });

                    break;

                default:

                    logger.LogWarning($"The aggregation method '{method}' is not known. Skipping period.");

                    break;

            }

            return result;
        }

        private double[] ApplyAggregationFunction<T>(AggregationMethod method, string argument, int targetDatasetLength, T[] valueSet, byte[] valueSet_status, ILogger logger)
        {
            var chunkSize = valueSet.Count() / targetDatasetLength;
            var result = new double[targetDatasetLength];

            switch (method)
            {
                case AggregationMethod.MinBitwise:

                    T[] bitField_and = new T[targetDatasetLength];

                    Parallel.For(0, targetDatasetLength, x =>
                    {
                        var baseIndex = x * chunkSize;

                        for (int i = 0; i < chunkSize; i++)
                        {
                            if (valueSet_status[baseIndex + i] != 1)
                            {
                                result[x] = Double.NaN;
                                return;
                            }
                            else
                            {
                                if (i == 0)
                                    bitField_and[x] = GenericBitOr<T>.BitOr(bitField_and[x], valueSet[baseIndex + i]);
                                else
                                    bitField_and[x] = GenericBitAnd<T>.BitAnd(bitField_and[x], valueSet[baseIndex + i]);
                            }
                        }

                        // all OK
                        result[x] = Convert.ToDouble(bitField_and[x]);
                    });

                    break;

                case AggregationMethod.MaxBitwise:

                    T[] bitField_or = new T[targetDatasetLength];

                    Parallel.For(0, targetDatasetLength, x =>
                    {
                        var baseIndex = x * chunkSize;

                        for (int i = 0; i < chunkSize; i++)
                        {
                            if (valueSet_status[baseIndex + i] != 1)
                            {
                                result[x] = Double.NaN;
                                return;
                            }
                            else
                            {
                                bitField_or[x] = GenericBitOr<T>.BitOr(bitField_or[x], valueSet[baseIndex + i]);
                            }
                        }

                        // all OK
                        result[x] = Convert.ToDouble(bitField_or[x]);
                    });

                    break;

                default:
                    logger.LogWarning($"The aggregation method '{method}' is not known. Skipping period.");
                    break;

            }

            return result;
        }

        private bool ApplyAggregationFilter(VariableInfo variableInfo)
        {
            bool result = true;

            // channel
            if (_filters.ContainsKey("--include-channel"))
            {
                if (variableInfo.VariableNames.Any())
                    result &= Regex.IsMatch(variableInfo.VariableNames.Last(), _filters["--include-channel"]);
                else
                    result &= false;
            }

            if (_filters.ContainsKey("--exclude-channel"))
            {
                if (variableInfo.VariableNames.Any())
                    result &= !Regex.IsMatch(variableInfo.VariableNames.Last(), _filters["--exclude-channel"]);
                else
                    result &= true;
            }

            // group
            if (_filters.ContainsKey("--include-group"))
            {
                if (variableInfo.VariableGroups.Any())
                    result &= variableInfo.VariableGroups.Last().Split('\n').Any(groupName => Regex.IsMatch(groupName, _filters["--include-group"]));
                else
                    result &= false;
            }

            if (_filters.ContainsKey("--exclude-group"))
            {
                if (variableInfo.VariableGroups.Any())
                    result &= !variableInfo.VariableGroups.Last().Split('\n').Any(groupName => Regex.IsMatch(groupName, _filters["--exclude-group"]));
                else
                    result &= true;
            }

            // unit
            if (_filters.ContainsKey("--include-unit"))
            {
#warning Remove this special case check.
                if (variableInfo.Units.Last() == null)
                {
                    _logger.LogWarning("Unit 'null' value detected.");
                    result &= false;
                }
                else
                {
                    if (variableInfo.Units.Any())
                        result &= Regex.IsMatch(variableInfo.Units.Last(), _filters["--include-unit"]);
                    else
                        result &= false;
                }
            }

            if (_filters.ContainsKey("--exclude-unit"))
            {
#warning Remove this special case check.
                if (variableInfo.Units.Last() == null)
                {
                    _logger.LogWarning("Unit 'null' value detected.");
                    result &= true;

                }
                else
                {
                    if (variableInfo.Units.Any())
                        result &= !Regex.IsMatch(variableInfo.Units.Last(), _filters["--exclude-unit"]);
                    else
                        result &= true;
                }
            }

            return result;
        }

        private byte[] GetCompletedChunks(int start, int end)
        {
            if (!(0 <= start && start <= end && end < 1440))
                throw new ArgumentException(ErrorMessage.AggregateCommand_InvalidChunkBounds);

            var completedChunks = new byte[1440];

            for (int i = start; i < end + 1; i++)
            {
                completedChunks[i] = 1;
            }

            return completedChunks;
        }

        #endregion
    }
}