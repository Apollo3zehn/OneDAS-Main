using Microsoft.Extensions.Logging;
using OneDas.DataManagement.Core.Tests;
using OneDas.DataManagement.Extensions;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace OneDas.Core.Tests
{
    public class HdfDataReaderTests : IClassFixture<HdfDataReaderFixture>
    {
        private ILogger _logger;
        private HdfDataReaderFixture _fixture;

        public HdfDataReaderTests(HdfDataReaderFixture fixture, ITestOutputHelper xunitLogger)
        {
            _fixture = fixture;
            _logger = new XunitLoggerProvider(xunitLogger).CreateLogger(nameof(HdfDataReaderTests));
        }

        [Fact]
        public void ProvidesCampaignNames()
        {
            // arrange
            var dataReader = new HdfDataReader(_fixture.Root, _logger);

            // act
            var actual = dataReader.GetCampaignNames();

            // assert
            var expected = new List<string>() { "/A2/B/C", "/A/B/C" };
            Assert.True(expected.SequenceEqual(actual));
        }

        [Fact]
        public void ProvidesCampaign()
        {
            // arrange
            var dataReader = new HdfDataReader(_fixture.Root, _logger);

            // act
            var actual = dataReader.GetCampaign("/A/B/C");
            var actualNames = actual.Variables.Select(variable => variable.Name).ToList();
            var actualGroups = actual.Variables.Select(variable => variable.Group).ToList();
            var actualUnits = actual.Variables.Select(variable => variable.Unit).ToList();

            // assert
            var expectedNames = new List<string>() { "A" };
            var expectedGroups = new List<string>() { "Group A" };
            var expectedUnits = new List<string>() { "Hz" };
            var expectedStartDate = new DateTime(2020, 07, 08, 00, 00, 00);
            var expectedEndDate = new DateTime(2020, 07, 10, 00, 00, 00);

            Assert.True(expectedNames.SequenceEqual(actualNames));
            Assert.True(expectedGroups.SequenceEqual(actualGroups));
            Assert.True(expectedUnits.SequenceEqual(actualUnits));
            Assert.Equal(expectedStartDate, actual.CampaignStart);
            Assert.Equal(expectedEndDate, actual.CampaignEnd);
        }

        [Fact]
        public void ProvidesDataAvailabilityStatistics()
        {
            // arrange
            var dataReader = new HdfDataReader(_fixture.Root, _logger);

            // act
            var actual = dataReader.GetDataAvailabilityStatistics("/A/B/C", new DateTime(2020, 07, 1), new DateTime(2020, 08, 01));

            // assert
            var expected = Enumerable.Range(0, 31).Select(number => 0).ToArray();
            expected[7] = 100;
            expected[8] = 100;

            Assert.True(actual.Data.SequenceEqual(expected));
        }

        [Fact]
        public void CanReadTwoDaysShifted()
        {
            // arrange
            var dataReader = new HdfDataReader(_fixture.Root, _logger);

            // act
            var campaign = dataReader.GetCampaign("/A/B/C");
            var dataset = campaign.Variables.First().Datasets.First();

            var begin = new DateTime(2020, 07, 07, 23, 00, 00);
            var end = new DateTime(2020, 07, 10, 00, 00, 00);

            var result = dataReader.ReadSingle<double>(dataset, begin, end);

            // assert
            var samplesPerDay = 86400 * 100;

            var baseOffset = samplesPerDay / 24 * 1;
            var dayOffset = 86400 * 100;
            var hourOffset = 360000;
            var halfHourOffset = hourOffset / 2;

            // day 1 V1
            Assert.True(result.StatusSet[baseOffset + 0] == 1);
            Assert.True(result.StatusSet[baseOffset + 1] == 0);
            Assert.True(result.StatusSet[baseOffset + 2] == 1);
            Assert.True(result.StatusSet[baseOffset + 3] == 0);
            Assert.True(result.Dataset[baseOffset + 0] == 99.27636);
            Assert.True(result.Dataset[baseOffset + 2] == 99.27626);
            Assert.True(result.Dataset[baseOffset + 86400 * 100 - 1] == 2323e-3);

            // day 2 V1
            Assert.True(result.StatusSet[baseOffset + dayOffset - 1] == 1);
            Assert.True(result.StatusSet[baseOffset + dayOffset + 0] == 1);
            Assert.True(result.StatusSet[baseOffset + dayOffset + 2] == 1);
            Assert.True(result.StatusSet[baseOffset + dayOffset + dayOffset - hourOffset - 1] == 1);
            Assert.True(result.Dataset[baseOffset + dayOffset + 0] == 98.27636);
            Assert.True(result.Dataset[baseOffset + dayOffset + 2] == 97.27626);
            Assert.True(result.Dataset[baseOffset + dayOffset + dayOffset - hourOffset - 1] == 2323e-6);

            // day 2 V2
            Assert.True(result.StatusSet[baseOffset + dayOffset + dayOffset - halfHourOffset + 0] == 1);
            Assert.True(result.StatusSet[baseOffset + dayOffset + dayOffset - halfHourOffset + 2] == 1);
            Assert.True(result.StatusSet[baseOffset + dayOffset + dayOffset - 1] == 1);
            Assert.True(result.Dataset[baseOffset + dayOffset + dayOffset - halfHourOffset + 0] == 90.27636);
            Assert.True(result.Dataset[baseOffset + dayOffset + dayOffset - halfHourOffset + 2] == 90.27626);
            Assert.True(result.Dataset[baseOffset + dayOffset + dayOffset - 1] == 2323e-9);
        }
    }
}