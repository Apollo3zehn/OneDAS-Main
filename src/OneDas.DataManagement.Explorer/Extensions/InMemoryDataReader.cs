﻿using Microsoft.Extensions.Logging;
using OneDas.DataManagement.Database;
using OneDas.DataManagement.Extensibility;
using OneDas.Extensibility;
using OneDas.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OneDas.DataManagement.Extensions
{
    [ExtensionIdentification("OneDas.InMemory", "OneDAS in-memory", "Provides an in-memory database.", "", "")]
    public class InMemoryDataReader : DataReaderExtensionBase
    {
        #region Fields

        private Random _random;

        #endregion

        #region Constructors

        public InMemoryDataReader(DataReaderRegistration registration, ILogger logger) : base(registration, logger)
        {
            _random = new Random();
        }

        #endregion

        #region Methods

        public override (T[] Dataset, byte[] StatusSet) ReadSingle<T>(DatasetInfo dataset, DateTime begin, DateTime end)
        {
            double[] dataDouble;

            var beginTime = begin.ToUnixTimeStamp();
            var endTime = end.ToUnixTimeStamp();

            var length = (int)((end - begin).TotalSeconds * (double)dataset.GetSampleRate().SamplesPerSecond);
            var dt = (double)(1 / dataset.GetSampleRate().SamplesPerSecond);

            if (((ChannelInfo)dataset.Parent).Name.Contains("unix_time"))
            {
                dataDouble = Enumerable.Range(0, length).Select(i => i * dt + beginTime).ToArray();
            }
            else // temperature or wind speed
            {
                var kernelSize = 1000;
                var movingAverage = new double[kernelSize];
                var random = new Random();
                var mean = 15;

                dataDouble = new double[length];

                for (int i = 0; i < length; i++)
                {
                    movingAverage[i % kernelSize] = (random.NextDouble() - 0.5) * mean * 10 + mean;

                    if (movingAverage[kernelSize - 1] == 0)
                        dataDouble[i] = mean;
                    else
                        dataDouble[i] = movingAverage.Sum() / kernelSize;
                }
            }

            var data = dataDouble.Select(value => (T)Convert.ChangeType(value, typeof(T))).ToArray();
            var statusSet = Enumerable.Range(0, length).Select(value => (byte)1).ToArray();

            return (data, statusSet);
        }

        protected override List<ProjectInfo> LoadProjects()
        {
            var id11 = "7dec6d79-b92e-4af2-9358-21be1f3626c9";
            var id12 = "cf50190b-fd2a-477b-9655-48f4f41ba7bf";
            var id13 = "f01b6a96-1de6-4caa-9205-184d8a3eb2f8";
            var id14 = "d549a4dd-e003-4d24-98de-4d5bc8c72aca";
            var project_allowed = this.LoadProject("/IN_MEMORY/TEST/ACCESSIBLE", id11, id12, id13, id14);

            var id21 = "511d6e9c-9075-41ee-bac7-891d359f0dda";
            var id22 = "99b85689-5373-4a9a-8fd7-be04a89c9da8";
            var id23 = "50d38fe5-a7a8-49e8-8bd4-3e98a48a951f";
            var id24 = "d47d1adc6-7c38-4b75-9459-742fa570ef9d";
            var project_restricted = this.LoadProject("/IN_MEMORY/TEST/RESTRICTED", id21, id22, id23, id24);

            return new List<ProjectInfo>() { project_allowed, project_restricted };
        }

        protected override double GetAvailability(string projectId, DateTime Day)
        {
            if (!this.Projects.Any(project => project.Id == projectId))
                throw new Exception($"The requested project with name '{projectId}' could not be found.");

            return _random.NextDouble() / 10 + 0.9;
        }

        private ProjectInfo LoadProject(string projectId, string id1, string id2, string id3, string id4)
        {
            var project = new ProjectInfo(projectId);

            var channelA = new ChannelInfo(id1, project);
            var channelB = new ChannelInfo(id2, project);
            var channelC = new ChannelInfo(id3, project);
            var channelD = new ChannelInfo(id4, project);

            var dataset1 = new DatasetInfo("25 Hz", channelA) { DataType = OneDasDataType.INT32 };
            var dataset2 = new DatasetInfo("1 s_max", channelB) { DataType = OneDasDataType.FLOAT64 };
            var dataset3 = new DatasetInfo("1 s_mean", channelB) { DataType = OneDasDataType.FLOAT64 };
            var dataset4 = new DatasetInfo("1 s_mean", channelC) { DataType = OneDasDataType.FLOAT64 };
            var dataset5 = new DatasetInfo("1 s_mean", channelD) { DataType = OneDasDataType.FLOAT64 };

            // channel A
            channelA.Name = "unix_time1";
            channelA.Group = "Group 1";
            channelA.Unit = "";

            channelA.Datasets.Add(dataset1);

            // channel B
            channelB.Name = "unix_time2";
            channelB.Group = "Group 1";
            channelB.Unit = string.Empty;

            channelB.Datasets.Add(dataset2);
            channelB.Datasets.Add(dataset3);

            // channel C
            channelC.Name = "T1";
            channelC.Group = "Group 2";
            channelC.Unit = "°C";

            channelC.Datasets.Add(dataset4);

            // channel D
            channelD.Name = "V1";
            channelD.Group = "Group 2";
            channelD.Unit = "m/s";

            channelD.Datasets.Add(dataset5);

            // project
            project.Channels = new List<ChannelInfo>()
            {
                channelA,
                channelB,
                channelC,
                channelD
            };

            return project;
        }

        #endregion
    }
}