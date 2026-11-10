#region license
// Copyright 2026 Utah Departement of Transportation
// for Infrastructure - Utah.Udot.ATSPM.Infrastructure.Workflows/DecodeEventLogWorkflow.cs
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
using System.Threading.Tasks.Dataflow;
using Utah.Udot.Atspm.Data.Models.EventLogModels;
using Utah.Udot.ATSPM.Infrastructure.WorkflowSteps;

namespace Utah.Udot.ATSPM.Infrastructure.Workflows
{
    public class DecodeEventLogWorkflow : WorkflowBase<Tuple<Device, FileInfo>, CompressedEventLogBase>
    {
        private readonly IServiceScopeFactory _services;
        private readonly int _batchSize;
        private readonly CancellationToken _cancellationToken;

        public DecodeEventLogWorkflow(IServiceScopeFactory services, int batchSize = 50000, CancellationToken cancellationToken = default)
        {
            _services = services;
            _batchSize = batchSize;
            _cancellationToken = cancellationToken;
        }

        ///<inheritdoc cref="DecodeDeviceData"/>
        public DecodeDeviceData DecodeDeviceData { get; private set; }

        /// <summary>
        /// Broadcasts decoded events to the archive and signal-timing branches.
        /// </summary>
        public BroadcastBlock<Tuple<Device, EventLogModelBase>> BroadcastEvents { get; private set; }

        /// <summary>
        /// Filters decoded events to the Indiana events used by signal-timing processing.
        /// </summary>
        public TransformManyBlock<Tuple<Device, EventLogModelBase>, IndianaEvent> TransformToIndianaEvent { get; private set; }

        /// <inheritdoc/>
        public ArchiveEventLogsWorkflow ArchiveEventLogsWorkflow { get; private set; }

        /// <inheritdoc/>
        public SignalTimingPlansWorkflow SignalTimingPlansWorkflow { get; private set; }

        /// <inheritdoc/>
        public override async Task Initialize()
        {
            Input = new(null, blockOptions);
            Output = new(blockOptions);

            InstantiateSteps();

            await ArchiveEventLogsWorkflow.Initialize();
            await SignalTimingPlansWorkflow.Initialize();

            Steps.Add(Input);
            AddStepsToTracker();
            LinkSteps();

            _ = TrackWorkflowCompletionAsync();
        }

        /// <inheritdoc/>
        protected override void AddStepsToTracker()
        {
            Steps.Add(DecodeDeviceData);
            Steps.Add(BroadcastEvents);
            Steps.Add(TransformToIndianaEvent);
            Steps.Add(ArchiveEventLogsWorkflow.Output);
            Steps.Add(SignalTimingPlansWorkflow.Output);
        }

        /// <inheritdoc/>
        protected override void InstantiateSteps()
        {
            DecodeDeviceData = new(_services, new ExecutionDataflowBlockOptions() { MaxDegreeOfParallelism = 1, CancellationToken = _cancellationToken });
            BroadcastEvents = new(null, new DataflowBlockOptions() { CancellationToken = _cancellationToken });
            TransformToIndianaEvent = new(t => new[] { t.Item2 }.OfType<IndianaEvent>(), new ExecutionDataflowBlockOptions() { CancellationToken = _cancellationToken });
            ArchiveEventLogsWorkflow = new(_services, _batchSize, 1, _cancellationToken);
            SignalTimingPlansWorkflow = new(_services, _batchSize, 1, _cancellationToken);
        }

        /// <inheritdoc/>
        protected override void LinkSteps()
        {
            Input.LinkTo(DecodeDeviceData, new DataflowLinkOptions() { PropagateCompletion = true });
            DecodeDeviceData.LinkTo(BroadcastEvents, new DataflowLinkOptions() { PropagateCompletion = true });

            BroadcastEvents.LinkTo(ArchiveEventLogsWorkflow.Input, new DataflowLinkOptions() { PropagateCompletion = true });
            BroadcastEvents.LinkTo(TransformToIndianaEvent, new DataflowLinkOptions() { PropagateCompletion = true });
            TransformToIndianaEvent.LinkTo(SignalTimingPlansWorkflow.Input, new DataflowLinkOptions() { PropagateCompletion = true });

            ArchiveEventLogsWorkflow.Output.LinkTo(Output);
            SignalTimingPlansWorkflow.Output.LinkTo(DataflowBlock.NullTarget<SignalTimingPlan>());
        }

        private async Task TrackWorkflowCompletionAsync()
        {
            try
            {
                await Task.WhenAll(
                    ArchiveEventLogsWorkflow.Output.Completion,
                    SignalTimingPlansWorkflow.Output.Completion
                );

                _cancellationToken.ThrowIfCancellationRequested();
                Output.Complete();
            }
            catch (Exception ex)
            {
                ((IDataflowBlock)Output).Fault(ex);
            }
        }
    }
}
