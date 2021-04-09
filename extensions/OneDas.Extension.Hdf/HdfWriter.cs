﻿using HDF.PInvoke;
using Microsoft.Extensions.Logging;
using OneDas.Buffers;
using OneDas.Extensibility;
using OneDas.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace OneDas.Extension.Hdf
{
    // v1: initial version
    // v2: 2017-08-07 -> unit_set, transfer_function_set
    [DataWriterFormatVersion(2)]
    public class HdfWriter : DataWriterExtensionLogicBase
    {
        #region "Fields"

        private HdfSettings _settings;

        private string _dataFilePath;

        private long _lastCompletedChunk;
        private long _fileId = -1;

        private TimeSpan _chunkPeriod;
        private IntPtr _isChunkCompletedIntPtr;

        private IList<string> _systemTimeChangedSet;

        #endregion

        #region "Constructors"

        public HdfWriter(HdfSettings settings, ILogger logger) : base(settings, logger)
        {
            uint isLibraryThreadSafe;

            _settings = settings;

            _systemTimeChangedSet = new List<string>();
            _isChunkCompletedIntPtr = Marshal.AllocHGlobal(1);
            _chunkPeriod = TimeSpan.FromMinutes(1);

            Marshal.WriteByte(_isChunkCompletedIntPtr, 1);

            // check thread safety
            isLibraryThreadSafe = 0;
            H5.is_library_threadsafe(ref isLibraryThreadSafe);

            if (isLibraryThreadSafe <= 0)
            {
                throw new Exception(ErrorMessage.HdfWriter_HdfLibraryNotThreadSafe);
            }
        }

        #endregion

        #region "Methods"

        protected override void OnPrepareFile(DateTime startDateTime, List<ChannelContextGroup> channelContextGroupSet)
        {
            _dataFilePath = Path.Combine(this.DataWriterContext.DataDirectoryPath, $"{this.DataWriterContext.ProjectDescription.PrimaryGroupName}_{this.DataWriterContext.ProjectDescription.SecondaryGroupName}_{this.DataWriterContext.ProjectDescription.ProjectName}_V{this.DataWriterContext.ProjectDescription.Version}_{startDateTime.ToString("yyyy-MM-ddTHH-mm-ss")}Z.h5");

            if (_fileId > -1)
                this.CloseHdfFile(_fileId);

            this.OpenFile(_dataFilePath, startDateTime, channelContextGroupSet.SelectMany(contextGroup => contextGroup.ChannelContextSet).ToList());

            _systemTimeChangedSet.Clear();
        }

        protected override void OnWrite(ChannelContextGroup contextGroup, ulong fileOffset, ulong bufferOffset, ulong length)
        {
            long datasetId = -1;
            long dataspaceId = -1;
            long dataspaceId_buffer = -1;
            long groupId = -1;

            long result = -1;

            try
            {
                var firstChunk = (long)this.ToChunkIndex(fileOffset, contextGroup.SampleRate);
                var lastChunk = (long)this.ToChunkIndex(fileOffset + length, contextGroup.SampleRate) - 1;

                groupId = H5G.open(_fileId, $"/{this.DataWriterContext.ProjectDescription.PrimaryGroupName}/{this.DataWriterContext.ProjectDescription.SecondaryGroupName}/{this.DataWriterContext.ProjectDescription.ProjectName}");
                datasetId = H5D.open(groupId, "is_chunk_completed_set");
                dataspaceId = H5D.get_space(datasetId);
                dataspaceId_buffer = H5S.create_simple(1, new ulong[] { 1 }, new ulong[] { 1 });

                _lastCompletedChunk = IOHelper.ReadAttribute<long>(groupId, "last_completed_chunk").FirstOrDefault();

                // this does not work in conjunction with multiple context groups
                if (firstChunk <= _lastCompletedChunk)
                {
                    //throw new Exception(ErrorMessage.HdfLogic_ChunkAlreadyWritten);
                }

                // write data
                for (int i = 0; i < contextGroup.ChannelContextSet.Count(); i++)
                {
                    this.WriteData(fileOffset, bufferOffset, length, contextGroup.ChannelContextSet[i]);
                }

                // write is_chunk_completed_set
                for (long chunkIndex = firstChunk; chunkIndex <= lastChunk; chunkIndex++)
                {
                    result = H5S.select_hyperslab(dataspaceId, H5S.seloper_t.SET, new ulong[] { (ulong)chunkIndex }, new ulong[] { 1 }, new ulong[] { 1 }, new ulong[] { 1 });
                    result = H5D.write(datasetId, H5T.NATIVE_UINT8, dataspaceId_buffer, dataspaceId, H5P.DEFAULT, _isChunkCompletedIntPtr);
                }

                // write last_completed_chunk
                IOHelper.WriteAttribute(groupId, "last_completed_chunk", new long[] { lastChunk });
            }
            finally
            {
                if (H5I.is_valid(datasetId) > 0) { H5D.close(datasetId); }
                if (H5I.is_valid(dataspaceId) > 0) { H5S.close(dataspaceId); }
                if (H5I.is_valid(dataspaceId_buffer) > 0) { H5S.close(dataspaceId_buffer); }
                if (H5I.is_valid(groupId) > 0) { H5G.close(groupId); }

                H5F.flush(_fileId, H5F.scope_t.GLOBAL);
            }
        }

        protected override void FreeUnmanagedResources()
        {
            base.FreeUnmanagedResources();

            if (H5I.is_valid(_fileId) > 0)
                this.CloseHdfFile(_fileId);

            Marshal.FreeHGlobal(_isChunkCompletedIntPtr);
        }

        private void OpenFile(string filePath, DateTime startDateTime, IList<ChannelContext> channelContextSet)
        {
            long groupId = -1;
            long datasetId = -1;
            long typeId = -1;

            try
            {
                _fileId = -1;

                // open file
                if (File.Exists(filePath))
                {
                    _fileId = H5F.open(filePath, H5F.ACC_RDWR, H5P.DEFAULT);

                    if (_fileId < 0)
                    {
                        try
                        {
                            File.Copy(filePath, filePath + ".backup");
                        }
                        catch (Exception)
                        {
                            //
                        }
                    }
                }

                if (_fileId < 0)
                    _fileId = H5F.create(filePath, H5F.ACC_TRUNC, H5P.DEFAULT, H5P.DEFAULT);

                if (_fileId < 0)
                    throw new Exception($"{ ErrorMessage.HdfWriter_CouldNotOpenOrCreateFile } File: { filePath }.");

                // file
                IOHelper.PrepareAttribute(_fileId, "format_version", new int[] { typeof(HdfWriter).GetFirstAttribute<DataWriterFormatVersionAttribute>().FormatVersion }, new ulong[] { 1 }, true);
                IOHelper.PrepareAttribute(_fileId, "system_name", new string[] { $"{ this.DataWriterContext.SystemName }" }, new ulong[] { 1 }, true);
                IOHelper.PrepareAttribute(_fileId, "date_time", new string[] { $"{ startDateTime.ToString("yyyy-MM-ddTHH-mm-ss") }Z" }, new ulong[] { 1 }, true);
                IOHelper.PrepareAttribute(_fileId, "system_time_changed_set", new string[] { }, new ulong[] { H5S.UNLIMITED }, false);

                foreach (var customMetadataEntry in this.DataWriterContext.CustomMetadataEntrySet.Where(customMetadataEntry => customMetadataEntry.CustomMetadataEntryLevel == CustomMetadataEntryLevel.File))
                {
                    IOHelper.PrepareAttribute(_fileId, customMetadataEntry.Key, new string[] { customMetadataEntry.Value }, new ulong[] { 1 }, true);
                }

                // file -> project
                groupId = IOHelper.OpenOrCreateGroup(_fileId, $"/{this.DataWriterContext.ProjectDescription.PrimaryGroupName}/{this.DataWriterContext.ProjectDescription.SecondaryGroupName}/{this.DataWriterContext.ProjectDescription.ProjectName}").GroupId;

                IOHelper.PrepareAttribute(groupId, "project_version", new int[] { this.DataWriterContext.ProjectDescription.Version }, new ulong[] { 1 }, true);

                foreach (var customMetadataEntry in this.DataWriterContext.CustomMetadataEntrySet.Where(customMetadataEntry => customMetadataEntry.CustomMetadataEntryLevel == CustomMetadataEntryLevel.Project))
                {
                    IOHelper.PrepareAttribute(groupId, customMetadataEntry.Key, new string[] { customMetadataEntry.Value }, new ulong[] { 1 }, true);
                }

                // file -> project -> channels
                foreach (ChannelContext channelContext in channelContextSet)
                {
                    var periodInSeconds = (ulong)Math.Round(_settings.FilePeriod.TotalSeconds, MidpointRounding.AwayFromZero);
                    var samplesPerSecond = channelContext.ChannelDescription.SampleRate.SamplesPerSecond;
                    (var chunkLength, var chunkCount) = GeneralHelper.CalculateChunkParameters(periodInSeconds, samplesPerSecond);

                    this.PrepareChannel(groupId, channelContext.ChannelDescription, chunkLength, chunkCount);
                }

                // file -> chunk info
                IOHelper.PrepareAttribute(groupId, "last_completed_chunk", new long[] { -1 }, new ulong[] { 1 }, true);
                typeId = TypeConversionHelper.GetHdfTypeIdFromType(_fileId, typeof(byte));
                datasetId = IOHelper.OpenOrCreateDataset(groupId, "is_chunk_completed_set", typeId, 1440, 1).DatasetId;
            }
            catch
            {
                if (H5I.is_valid(_fileId) > 0) { H5F.close(_fileId); }

                throw;
            }
            finally
            {
                if (H5I.is_valid(typeId) > 0) { H5T.close(typeId); }
                if (H5I.is_valid(datasetId) > 0) { H5D.close(datasetId); }
                if (H5I.is_valid(groupId) > 0) { H5G.close(groupId); }

                H5F.flush(_fileId, H5F.scope_t.GLOBAL);
            }
        }

        private unsafe void WriteData(ulong fileOffset, ulong bufferOffset, ulong length, ChannelContext channelContext)
        {
            long groupId = -1;

            long datasetTypeId = -1;
            long datasetId = -1;
            long dataspaceId = -1;
            long dataspaceId_Buffer = -1;

            long datasetTypeId_Status = -1;
            long datasetId_Status = -1;
            long dataspaceId_Status = -1;
            long dataspaceId_Buffer_Status = -1;

            try
            {
                var buffer = channelContext.Buffer;
                var extendedBuffer = buffer as IExtendedBuffer;
                var elementType = OneDasUtilities.GetTypeFromOneDasDataType(channelContext.ChannelDescription.DataType);
                var projectDescription = this.DataWriterContext.ProjectDescription;

                groupId = H5G.open(_fileId, $"/{projectDescription.PrimaryGroupName}/{projectDescription.SecondaryGroupName}/{projectDescription.ProjectName}/{channelContext.ChannelDescription.Guid}");

                // dataset
                var datasetName = channelContext.ChannelDescription.DatasetName;
                datasetTypeId = TypeConversionHelper.GetHdfTypeIdFromType(elementType);
                datasetId = H5D.open(groupId, datasetName);
                dataspaceId = H5D.get_space(datasetId);
                dataspaceId_Buffer = H5S.create_simple(1, new ulong[] { length }, null);

                H5S.select_hyperslab(dataspaceId,
                                    H5S.seloper_t.SET,
                                    new ulong[] { fileOffset },
                                    new ulong[] { 1 },
                                    new ulong[] { 1 },
                                    new ulong[] { length });

                if (elementType.IsPrimitive)
                {
                    var offset = (int)bufferOffset * buffer.ElementSize;
                    var rawBuffer = buffer.RawBuffer[offset..];

                    fixed (byte* bufferPtr = rawBuffer)
                    {
                        if (H5D.write(datasetId, datasetTypeId, dataspaceId_Buffer, dataspaceId, H5P.DEFAULT, new IntPtr(bufferPtr)) < 0)
                            throw new Exception(ErrorMessage.HdfWriter_CouldNotWriteChunk_Dataset);
                    }
                }
                else
                {
                    throw new Exception(ErrorMessage.HdfWriter_ElementTypeNonPrimitive);
                }

                // dataset status
                if (extendedBuffer != null)
                {
                    var datasetName_Status = $"{datasetName}_status";
                    datasetTypeId_Status = TypeConversionHelper.GetHdfTypeIdFromType(typeof(byte));
                    datasetId_Status = H5D.open(groupId, datasetName_Status);
                    dataspaceId_Status = H5D.get_space(datasetId_Status);
                    dataspaceId_Buffer_Status = H5S.create_simple(1, new ulong[] { length }, null);

                    H5S.select_hyperslab(dataspaceId_Status, 
                                         H5S.seloper_t.SET, 
                                         new ulong[] { fileOffset }, 
                                         new ulong[] { 1 }, 
                                         new ulong[] { 1 }, 
                                         new ulong[] { length });

                    var offset = (int)bufferOffset;
                    var statusBuffer = extendedBuffer.StatusBuffer[offset..];

                    fixed (byte* bufferPtr = statusBuffer)
                    {
                        if (H5D.write(datasetId_Status, datasetTypeId_Status, dataspaceId_Buffer_Status, dataspaceId_Status, H5P.DEFAULT, new IntPtr(bufferPtr)) < 0)
                            throw new Exception(ErrorMessage.HdfWriter_CouldNotWriteChunk_Status);
                    }
                }
            }
            finally
            {
                if (H5I.is_valid(groupId) > 0) { H5G.close(groupId); }

                // dataset status
                if (H5I.is_valid(datasetId_Status) > 0) { H5D.close(datasetId_Status); }
                if (H5I.is_valid(dataspaceId_Status) > 0) { H5S.close(dataspaceId_Status); }
                if (H5I.is_valid(dataspaceId_Buffer_Status) > 0) { H5S.close(dataspaceId_Buffer_Status); }
                if (H5I.is_valid(datasetTypeId_Status) > 0) { H5T.close(datasetTypeId_Status); }

                // dataset
                if (H5I.is_valid(datasetId) > 0) { H5D.close(datasetId); }
                if (H5I.is_valid(dataspaceId) > 0) { H5S.close(dataspaceId); }
                if (H5I.is_valid(dataspaceId_Buffer) > 0) { H5S.close(dataspaceId_Buffer); }
                if (H5I.is_valid(datasetTypeId) > 0) { H5T.close(datasetTypeId); }
            }
        }

        private void PrepareChannel(long locationId, ChannelDescription channelDescription, ulong chunkLength, ulong chunkCount)
        {
            long groupId = -1;
            long datasetId = -1;
            long datasetTypeId = -1;
            long datasetId_status = -1;

            string datasetName = null;

            try
            {
                // group (GUID)
                groupId = IOHelper.OpenOrCreateGroup(locationId, channelDescription.Guid.ToString()).GroupId;

                // attributes
                IOHelper.PrepareAttribute(groupId, "name_set", new string[] { channelDescription.ChannelName }, new ulong[] { H5S.UNLIMITED }, true);
                IOHelper.PrepareAttribute(groupId, "group_set", new string[] { channelDescription.Group }, new ulong[] { H5S.UNLIMITED }, true);
                IOHelper.PrepareAttribute(groupId, "comment_set", new string[] { "yyyy-MM-ddTHH-mm-ssZ: Comment1" }, new ulong[] { H5S.UNLIMITED }, true);
                IOHelper.PrepareAttribute(groupId, "unit_set", new string[] { channelDescription.Unit }, new ulong[] { H5S.UNLIMITED }, true);

                // dataset (native)
                datasetName = channelDescription.DatasetName;
                datasetTypeId = TypeConversionHelper.GetHdfTypeIdFromType(OneDasUtilities.GetTypeFromOneDasDataType(channelDescription.DataType));
                datasetId = IOHelper.OpenOrCreateDataset(groupId, datasetName, datasetTypeId, chunkLength, chunkCount).DatasetId;

                // dataset (status)
                if (channelDescription.BufferType == BufferType.Extended)
                {
                    datasetName = $"{datasetName}_status";
                    datasetId_status = IOHelper.OpenOrCreateDataset(groupId, datasetName, H5T.NATIVE_UINT8, chunkLength, chunkCount).DatasetId;
                }
            }
            finally
            {
                if (H5I.is_valid(datasetId_status) > 0) { H5D.close(datasetId_status); }
                if (H5I.is_valid(datasetId) > 0) { H5D.close(datasetId); }
                if (H5I.is_valid(datasetTypeId) > 0) { H5T.close(datasetTypeId); }

                if (H5I.is_valid(groupId) > 0) { H5G.close(groupId); }
            }          
        }

        private ulong ToChunkIndex(ulong offset, SampleRateContainer sampleRate)
        {
            var length = _chunkPeriod.TotalSeconds * (double)sampleRate.SamplesPerSecond;
            return (ulong)(offset / length);
        }

        private void CloseHdfFile(long fileId)
        {
            if (H5I.is_valid(fileId) > 0) { H5F.close(fileId); }
        }

        #endregion
    }
}
