#region license
// Copyright 2026 Utah Departement of Transportation
// for InfrastructureTests - Utah.Udot.Atspm.InfrastructureTests.Services.HostedServices/DecodeEventLogHostedServiceTests.cs
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Utah.Udot.Atspm.Data.Models;
using Utah.Udot.Atspm.Data.Models.EventLogModels;
using Utah.Udot.Atspm.Infrastructure.Configuration;
using Utah.Udot.Atspm.Infrastructure.Services.HostedServices;
using Utah.Udot.Atspm.Repositories.ConfigurationRepositories;
using Utah.Udot.Atspm.Services;
using Xunit;

namespace Utah.Udot.Atspm.InfrastructureTests.Services.HostedServices;

public class DecodeEventLogHostedServiceTests : IDisposable
{
    private readonly DirectoryInfo _source = Directory.CreateTempSubdirectory("AtspmDecodeEventsTests-");

    [Fact]
    public async Task RootFiles_AreSkippedEvenWhenTheAddressMatchesADevice()
    {
        var device = CreateDevice(1, "01-001-001", "controller-a", "10.18.1.130");
        var rootFile = CreateFile("ECON_10.18.1.130_2026_10_24_0245.datZ");
        var folderFile = CreateFile("01-001-001/folder.datZ");

        var imported = await Run(new[] { device }, new[] { device.Location });

        var input = Assert.Single(imported);
        Assert.Same(device, input.Item1);
        Assert.Same(device.DeviceConfiguration, input.Item1.DeviceConfiguration);
        Assert.Equal(folderFile.FullName, input.Item2.FullName);
        Assert.True(File.Exists(rootFile.FullName));
    }

    [Fact]
    public async Task MultipleDevices_RequireADeviceFolder()
    {
        var first = CreateDevice(1, "01-001-001", "controller-a", null);
        var second = CreateDevice(1, "01-001-001", "controller-b", null);
        second.Id = 2;
        CreateFile("01-001-001/ambiguous.datZ");
        var firstFile = CreateFile("01-001-001/controller-a/first.datZ");
        var secondFile = CreateFile("01-001-001/controller-b/second.datZ");
        CreateFile("unknown-location/unknown.datZ");

        var imported = await Run(new[] { first, second }, new[] { first.Location });

        Assert.Equal(2, imported.Count);
        Assert.Same(first, Assert.Single(imported.Where(input => input.Item2.FullName == firstFile.FullName)).Item1);
        Assert.Same(second, Assert.Single(imported.Where(input => input.Item2.FullName == secondFile.FullName)).Item1);
        Assert.Equal(new[] { firstFile.FullName, secondFile.FullName }.OrderBy(path => path),
            imported.Select(input => input.Item2.FullName).OrderBy(path => path));
    }

    [Fact]
    public async Task FolderLayouts_StillUseExactIdentifiersWithoutAnAddress()
    {
        var device = CreateDevice(1, "01-001-001", "controller-a", null);
        var caseSensitiveDevice = CreateDevice(2, "01-001-002", "Controller-B", null);
        var locationFile = CreateFile("01-001-001/location.datZ");
        var deviceFile = CreateFile("01-001-001/controller-a/device.datZ");
        CreateFile("1-001-001/wrong-location.datZ");
        CreateFile("01-001-002/controller-b/wrong-device.datZ");

        var imported = await Run(new[] { device, caseSensitiveDevice }, new[] { device.Location, caseSensitiveDevice.Location });

        Assert.Equal(2, imported.Count);
        Assert.All(imported, input => Assert.Same(device, input.Item1));
        Assert.Equal(new[] { deviceFile.FullName, locationFile.FullName }.OrderBy(path => path),
            imported.Select(input => input.Item2.FullName).OrderBy(path => path));
    }

    private async Task<List<Tuple<Device, FileInfo>>> Run(Device[] devices, Location[] latestLocations)
    {
        var imported = new List<Tuple<Device, FileInfo>>();
        var deviceRepository = new Mock<IDeviceRepository>(MockBehavior.Strict);
        deviceRepository.Setup(repository => repository.GetList()).Returns(devices.AsQueryable());
        var locationRepository = new Mock<ILocationRepository>(MockBehavior.Strict);
        locationRepository.Setup(repository => repository.GetLatestVersionOfAllLocations()).Returns(latestLocations);
        var importer = new Mock<IEventLogImporter>(MockBehavior.Strict);
        importer.Setup(service => service.Execute(It.IsAny<Tuple<Device, FileInfo>>(), It.IsAny<CancellationToken>()))
            .Callback<Tuple<Device, FileInfo>, CancellationToken>((input, _) => imported.Add(input))
            .Returns(AsyncEnumerable.Empty<Tuple<Device, EventLogModelBase>>());

        var services = new ServiceCollection();
        services.AddSingleton(deviceRepository.Object);
        services.AddSingleton(locationRepository.Object);
        services.AddScoped(_ => importer.Object);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var hostedService = new DecodeEventLogHostedService(
            NullLogger<DecodeEventLogHostedService>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DecodeEventsConfiguration { Path = _source.FullName }));

        await hostedService.Process(scope, Stopwatch.StartNew()).WaitAsync(TimeSpan.FromSeconds(10));
        return imported;
    }

    private FileInfo CreateFile(string relativePath)
    {
        var file = new FileInfo(Path.Combine(_source.FullName, relativePath));
        Directory.CreateDirectory(file.DirectoryName!);
        File.WriteAllText(file.FullName, "copied test file");
        return file;
    }

    private static Device CreateDevice(int locationId, string locationIdentifier, string deviceIdentifier, string address) => new()
    {
        Id = locationId,
        DeviceIdentifier = deviceIdentifier,
        Ipaddress = address,
        LocationId = locationId,
        Location = new Location { Id = locationId, LocationIdentifier = locationIdentifier },
        DeviceConfiguration = new DeviceConfiguration { Decoders = new[] { "AscToIndianaDecoder" } }
    };

    public void Dispose()
    {
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (_source.Parent?.FullName == temporaryRoot && _source.Name.StartsWith("AtspmDecodeEventsTests-", StringComparison.Ordinal))
            Directory.Delete(_source.FullName, recursive: true);
    }
}
