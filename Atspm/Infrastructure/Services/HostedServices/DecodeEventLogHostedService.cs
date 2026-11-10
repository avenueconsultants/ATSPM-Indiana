#region license
// Copyright 2026 Utah Departement of Transportation
// for Infrastructure - Utah.Udot.Atspm.Infrastructure.Services.HostedServices/DecodeEventLogHostedService.cs
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using Utah.Udot.Atspm.Infrastructure.Extensions;
using Utah.Udot.ATSPM.Infrastructure.Workflows;

namespace Utah.Udot.Atspm.Infrastructure.Services.HostedServices
{
    public class DecodeEventLogHostedService(ILogger<DecodeEventLogHostedService> log, IServiceScopeFactory serviceProvider, IOptions<DecodeEventsConfiguration> options) : HostedServiceBase(log, serviceProvider)
    {
        private readonly IOptions<DecodeEventsConfiguration> _options = options;

        /// <inheritdoc/>
        public override async Task Process(IServiceScope scope, Stopwatch stopwatch, CancellationToken cancellationToken = default)
        {
            var dir = new DirectoryInfo(_options.Value.Path);
            if (!dir.Exists)
            {
                log.LogWarning("Source directory {Path} does not exist", dir.FullName);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var repo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
            var locationRepo = scope.ServiceProvider.GetRequiredService<ILocationRepository>();
            var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();
            var latestLocationIds = locationRepo.GetLatestVersionOfAllLocations().Select(l => l.Id).ToList();
            var devices = repo.GetList()
                .Where(d => latestLocationIds.Contains(d.LocationId))
                .ToLookup(d => d.Location.LocationIdentifier, StringComparer.Ordinal);
            var filesByLocation = new Dictionary<string, List<Tuple<Device, FileInfo>>>(StringComparer.Ordinal);
            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = 0
            };

            foreach (var folder in dir.EnumerateFiles("*", enumerationOptions).GroupBy(f => f.DirectoryName))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativeDirectory = Path.GetRelativePath(dir.FullName, folder.Key);
                if (relativeDirectory == ".")
                {
                    log.LogWarning("Skipping {FileCount} files directly in source directory {Path}; expected location or location/device folders", folder.Count(), dir.FullName);
                    continue;
                }

                var parts = relativeDirectory.Split(Path.DirectorySeparatorChar);
                if (parts.Length > 2)
                {
                    log.LogWarning("Skipping unsupported source folder {Folder}; expected location or location/device", folder.Key);
                    continue;
                }

                var matches = devices[parts[0]]
                    .Where(d => parts.Length == 1 || d.DeviceIdentifier == parts[1]).ToList();
                if (matches.Count != 1)
                {
                    log.LogWarning("Skipping unknown or ambiguous source folder {Folder}: matched {DeviceCount} configured devices", folder.Key, matches.Count);
                    continue;
                }

                var device = matches[0];
                if (!filesByLocation.TryGetValue(parts[0], out var files))
                    filesByLocation[parts[0]] = files = new();
                files.AddRange(folder.Select(file => Tuple.Create(device, file)));
            }

            Func<DecodeEventLogWorkflow> workflowFactory = () => new(scopeFactory, 50000, cancellationToken);

            foreach (var location in filesByLocation)
            {
                log.LogInformation("Decoding {FileCount} files for location {LocationIdentifier}", location.Value.Count, location.Key);
                await workflowFactory.BatchRunAsync(location.Value.ToAsyncEnumerable(), location.Value.Count, 1, cancellationToken);
            }
        }
    }
}
