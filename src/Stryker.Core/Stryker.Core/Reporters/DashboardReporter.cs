using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.ProjectComponents;
using Stryker.Abstractions.Reporting;
using Stryker.Core.Clients;
using Stryker.Core.Reporters.Json;
using Stryker.Core.Reporters.Json.SourceFiles;
using Stryker.Core.Reporters.WebBrowserOpener;
using Stryker.Utilities.Logging;

namespace Stryker.Core.Reporters;

public class DashboardReporter : IReporter
{
    private readonly IStrykerOptions _options;
    private readonly IDashboardClient _dashboardClient;
    private readonly ILogger<DashboardReporter> _logger;
    private readonly IAnsiConsole _console;
    private readonly IWebbrowserOpener _browser;

    public DashboardReporter(IStrykerOptions options, IDashboardClient dashboardClient = null, ILogger<DashboardReporter> logger = null,
        IAnsiConsole console = null, IWebbrowserOpener browser = null)
    {
        _options = options;
        _dashboardClient = dashboardClient ?? new DashboardClient(options);
        _logger = logger ?? ApplicationLogging.LoggerFactory.CreateLogger<DashboardReporter>();
        _console = console ?? AnsiConsole.Console;
        _browser = browser ?? new CrossPlatformBrowserOpener();
    }

    public void OnAllMutantsTested(IReadOnlyProjectComponent reportComponent, ITestProjectsInfo testProjectsInfo)
    {
        var mutationReport = JsonReport.Build(_options, reportComponent, testProjectsInfo);
        _dashboardClient.PublishReport(mutationReport, _options.ProjectVersion).Wait();

        if (ShouldPublishInRealTime())
        {
            _dashboardClient.PublishFinished().Wait();
        }
    }

    private void OpenDashboardReport(string reportUri)
    {
        if (reportUri != null)
        {
            if (_options.ReportTypeToOpen == ReportType.Dashboard)
            {
                _browser.Open(reportUri);
            }
            else
            {
                var aqua = new Style(Color.Aqua);
                _console.WriteLine(
                    "Hint: by passing \"--open-report:dashboard or -o:dashboard\" the report will open automatically once Stryker is done.",
                    aqua);
            }

            var green = new Style(Color.Green);
            _console.WriteLine();
            _console.WriteLine("Your report has been uploaded at:", green);
            // We must print the report path as the link text because on some terminals links might be supported but not actually clickable: https://github.com/spectreconsole/spectre.console/issues/764
            _console.WriteLine(reportUri,
                _console.Profile.Capabilities.Links ? green.Combine(new Style(link: reportUri)) : green);
            _console.WriteLine("You can open it in your browser of choice.", green);
        }
        else
        {
            _logger.LogError("Uploading to stryker dashboard failed...");
        }

        _console.WriteLine();
        _console.WriteLine();
    }

    public void OnMutantsCreated(IReadOnlyProjectComponent reportComponent, ITestProjectsInfo testProjectsInfo)
    {
        if (!ShouldPublishInRealTime())
        {
            return;
        }

        var mutationReport = JsonReport.Build(_options, reportComponent, testProjectsInfo);
        var reportUri = _dashboardClient.PublishReport(mutationReport, _options.ProjectVersion, true).Result;

        OpenDashboardReport(reportUri);
    }

    public void OnMutantTested(IReadOnlyMutant result)
    {
        if (!ShouldPublishInRealTime())
        {
            return;
        }

        _dashboardClient.PublishMutantBatch(new JsonMutant(result)).Wait();
    }

    public void OnStartMutantTestRun(IEnumerable<IReadOnlyMutant> mutantsToBeTested)
    {
        // Method to implement the interface
    }

    private bool ShouldPublishInRealTime() =>
        _options.ReportTypeToOpen == ReportType.Dashboard ||
        _options.Reporters.Contains(Reporter.RealTimeDashboard);
}
