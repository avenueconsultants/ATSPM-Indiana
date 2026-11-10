#region license
// Copyright 2026 Utah Departement of Transportation
// for EventLogUtility - Utah.Udot.Atspm.EventLogUtility.Commands/DecodeEventsCommand.cs
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
using Microsoft.Extensions.Hosting;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.NamingConventionBinder;
using Utah.Udot.Atspm.Infrastructure.Services.HostedServices;

namespace Utah.Udot.Atspm.EventLogUtility.Commands
{
    public class DecodeEventsCommand : Command, ICommandOption
    {
        public DecodeEventsCommand() : base("decode-events", "Import and decode event logs from files")
        {
            AddGlobalOption(PathCommandOption);
        }

        public PathCommandOption PathCommandOption { get; set; } = new();

        public void BindCommandOptions(HostBuilderContext host, IServiceCollection services)
        {
            services.Configure<DecodeEventsConfiguration>(host.Configuration.GetSection(nameof(DecodeEventsConfiguration)));

            var decodeEventsConfiguration = new ModelBinder<DecodeEventsConfiguration>();

            decodeEventsConfiguration.BindMemberFromValue(b => b.Path, PathCommandOption);

            services.AddOptions<DecodeEventsConfiguration>()
                .Configure<BindingContext>((a, b) =>
                {
                    // The implicit CLI default must not replace the configured path.
                    if (b.ParseResult.FindResultFor(PathCommandOption)?.IsImplicit == false)
                        decodeEventsConfiguration.UpdateInstance(a, b);
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddHostedService<DecodeEventLogHostedService>();
        }
    }
}
