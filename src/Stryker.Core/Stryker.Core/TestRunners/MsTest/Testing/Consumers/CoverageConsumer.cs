using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Extensions.TestHost;
using Stryker.Core.TestRunners.MsTest.Testing.Tests;

namespace Stryker.Core.TestRunners.MsTest.Testing.Consumers;

internal class CoverageConsumer : IDataConsumer
{
    private readonly CoverageCollector _coverageCollector;

    private CoverageConsumer(CoverageCollector coverageCollector)
    {
        _coverageCollector = coverageCollector;
    }

    public static CoverageConsumer Create(CoverageCollector coverageCollector) => new(coverageCollector);

    public Type[] DataTypesConsumed => [typeof(TestNodeUpdateMessage)];

    public string Uid => nameof(CoverageConsumer);

    public string Version => "1.0.0";

    public string DisplayName => "Stryker.CoverageConsumer";

    public string Description => "Used to gather coverage";

    public Task<bool> IsEnabledAsync() => Task.FromResult(true);

    public Task ConsumeAsync(IDataProducer dataProducer, IData value, CancellationToken cancellationToken)
    {
        var update = value as TestNodeUpdateMessage;
        var state = update!.TestNode.Properties.Single<TestNodeStateProperty>();

        if (state is InProgressTestNodeStateProperty)
        {
            _coverageCollector.CaptureCoverageOutsideTests();
            return Task.CompletedTask;
        }

        _coverageCollector.PublishCoverageData(update.TestNode);

        return Task.CompletedTask;
    }
}
