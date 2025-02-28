using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Abstractions;
using System.Linq;
using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.TestPlatform.VsTestConsole.TranslationLayer;
using Microsoft.TestPlatform.VsTestConsole.TranslationLayer.Interfaces;
using Serilog.Events;
using Stryker.Abstractions.Exceptions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.Testing;
using Stryker.DataCollector;
using Stryker.TestRunner.VsTest.Helpers;
using Stryker.Utilities.Logging;

namespace Stryker.TestRunner.VsTest;

/// <summary>
///     Handles VsTest setup and configuration.
/// </summary>
public sealed class VsTestContextInformation : IDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly Func<string, IStrykerTestHostLauncher> _hostBuilder;
    private readonly ILogger _logger;
    private readonly bool _ownVsTestHelper;
    private readonly IVsTestHelper _vsTestHelper;
    private readonly Func<ConsoleParameters, IVsTestConsoleWrapper> _wrapperBuilder;
    private bool _disposed;
    private TestFrameworks _testFramework;

    /// <summary>
    /// Discovered tests (VsTest format)
    /// </summary>
    public IDictionary<Guid, VsTestDescription> VsTests { get; private set; }

    /// <summary>
    /// Tests in each source (assembly)
    /// </summary>
    public IDictionary<string, ISet<Guid>> TestsPerSource { get; } = new Dictionary<string, ISet<Guid>>();

    /// <summary>
    /// Tests (Stryker format)
    /// </summary>
    public TestSet Tests { get; } = new();

    public IStrykerOptions Options { get; }

    /// <summary>
    ///     Log folder path
    /// </summary>
    public string LogPath =>
        Options.OutputPath == null ? "logs" : _fileSystem.Path.Combine(Options.OutputPath, "logs");

    /// <param name="options">Configuration options</param>
    /// <param name="helper"></param>
    /// <param name="fileSystem"></param>
    /// <param name="builder"></param>
    /// <param name="hostBuilder"></param>
    /// <param name="logger"></param>
    public VsTestContextInformation(IStrykerOptions options,
        IVsTestHelper helper = null,
        IFileSystem fileSystem = null,
        Func<ConsoleParameters, IVsTestConsoleWrapper> builder = null,
        Func<string, IStrykerTestHostLauncher> hostBuilder = null,
        ILogger logger = null)
    {
        Options = options;
        _ownVsTestHelper = helper == null;
        _fileSystem = fileSystem ?? new FileSystem();
        _vsTestHelper = helper ?? new VsTestHelper(_fileSystem, logger);
        _wrapperBuilder = builder ?? BuildActualVsTestWrapper;
        var devMode = options.DevMode;
        _hostBuilder = hostBuilder ?? (name => new StrykerVsTestHostLauncher(name, devMode));
        _logger = logger ?? ApplicationLogging.LoggerFactory.CreateLogger<VsTestContextInformation>();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownVsTestHelper)
        {
            _vsTestHelper.Cleanup();
        }
    }

    /// <summary>
    /// Starts a new VsTest instance and returns a wrapper to control it.
    /// </summary>
    /// <param name="runnerId">Name of the instance to create (used in log files)</param>
    /// <param name="controlVariable">name of the env variable storing the active mutation id</param>
    /// <returns>a <see cref="IVsTestConsoleWrapper" /> controlling the created instance.</returns>
    public IVsTestConsoleWrapper BuildVsTestWrapper(string runnerId, string controlVariable)
    {
        var env = DetermineConsoleParameters(runnerId);
        // Set roll forward on no candidate fx so vstest console can start on incompatible dotnet core runtimes
        env.EnvironmentVariables["DOTNET_ROLL_FORWARD_ON_NO_CANDIDATE_FX"] = "2";
        // we define a per runner control variable to prevent conflict
        env.EnvironmentVariables["STRYKER_MUTANT_ID_CONTROL_VAR"] = controlVariable;
        var vsTestConsole = _wrapperBuilder(env);
        try
        {
            vsTestConsole.StartSession();
            vsTestConsole.InitializeExtensions([]);
        }
        catch (Exception e)
        {
            _logger.LogError("Stryker failed to connect to vstest.console with error: {error}", e.Message);
            throw new GeneralStrykerException("Stryker failed to connect to vstest.console", e);
        }

        return vsTestConsole;
    }

    /// <summary>
    ///     Builds a new process launcher used for a test session.
    /// </summary>
    /// <param name="runnerId">Name of the instance to create (used in log files)</param>
    /// <returns>a <see cref="IStrykerTestHostLauncher" /> </returns>
    public IStrykerTestHostLauncher BuildHostLauncher(string runnerId) => _hostBuilder(runnerId);

    private IVsTestConsoleWrapper BuildActualVsTestWrapper(ConsoleParameters parameters) =>
        new VsTestConsoleWrapper(_vsTestHelper.GetCurrentPlatformVsTestToolPath(),
            parameters);

    private ConsoleParameters DetermineConsoleParameters(string runnerId)
    {
        var determineConsoleParameters = new ConsoleParameters
        {
            TraceLevel = Options.LogOptions?.LogLevel switch
            {
                LogEventLevel.Debug => TraceLevel.Verbose,
                LogEventLevel.Verbose => TraceLevel.Verbose,
                LogEventLevel.Error => TraceLevel.Error,
                LogEventLevel.Fatal => TraceLevel.Error,
                LogEventLevel.Warning => TraceLevel.Warning,
                LogEventLevel.Information => TraceLevel.Info,
                _ => TraceLevel.Off
            }
        };

        if (Options.LogOptions?.LogToFile != true)
        {
            return determineConsoleParameters;
        }

        determineConsoleParameters.TraceLevel = Options.DevMode ? TraceLevel.Verbose : TraceLevel.Info;
        var vsTestLogPath = _fileSystem.Path.Combine(LogPath, $"{runnerId}-log.txt");
        _fileSystem.Directory.CreateDirectory(LogPath);
        determineConsoleParameters.LogFilePath = vsTestLogPath;
        return determineConsoleParameters;
    }

    public ITestSet GetTestsForSources(IEnumerable<string> sources)
    {
        var result = new TestSet();
        foreach (var source in sources)
        {
            result.RegisterTests(TestsPerSource[source].Select(id => Tests[id.ToString()]));
        }

        return result;
    }

    // keeps only test assemblies which have tests.
    public IEnumerable<string> GetValidSources(IEnumerable<string> sources) =>
        sources.Where(s => TestsPerSource.TryGetValue(s, out var result) && result.Count > 0);

    public bool AddTestSource(string source, string frameworkVersion = null, string platform = null)
    {
        if (!_fileSystem.File.Exists(source))
        {
            throw new GeneralStrykerException(
                $"The test project binaries could not be found at {source}, exiting...");
        }

        if (!TestsPerSource.ContainsKey(source))
        {
            DiscoverTestsInSources(source, frameworkVersion, platform);
        }

        return TestsPerSource[source].Count > 0;
    }

    private void DiscoverTestsInSources(string newSource, string frameworkVersion = null, string platform = null)
    {
        var wrapper = BuildVsTestWrapper("TestDiscoverer", "NOT_NEEDED");
        var messages = new List<string>();
        var handler = new DiscoveryEventHandler(messages);
        var settings = GenerateRunSettingsForDiscovery(frameworkVersion, platform);
        wrapper.DiscoverTests([newSource], settings, handler);

        handler.WaitEnd();
        if (handler.Aborted)
        {
            _logger.LogDebug("TestDiscoverer: Discovery settings: {discoverySettings}", settings);
            _logger.LogDebug("TestDiscoverer: {messages}", string.Join(Environment.NewLine, messages));
            _logger.LogError("TestDiscoverer: Test discovery has been aborted!");
        }

        wrapper.EndSession();

        TestsPerSource[newSource] = handler.DiscoveredTestCases.Select(c => c.Id).ToHashSet();
        VsTests ??= new Dictionary<Guid, VsTestDescription>(handler.DiscoveredTestCases.Count);
        foreach (var testCase in handler.DiscoveredTestCases)
        {
            if (!VsTests.ContainsKey(testCase.Id))
            {
                VsTests[testCase.Id] = new VsTestDescription(new VsTestCase(testCase));
            }

            VsTests[testCase.Id].AddSubCase();
            _logger.LogTrace(
                    "Test Case : name= {DisplayName} (id= {Id}, FQN= {FullyQualifiedName}).",
                    testCase.DisplayName, testCase.Id, testCase.FullyQualifiedName);
        }

        DetectTestFrameworks(VsTests.Values);
        Tests.RegisterTests(VsTests.Values.Select(t => t.Description));
    }

    internal void RegisterDiscoveredTest(VsTestDescription vsTestDescription)
    {
        var id = Guid.Parse(vsTestDescription.Id);
        VsTests[id] = vsTestDescription;
        Tests.RegisterTest(vsTestDescription.Description);
        TestsPerSource[vsTestDescription.Case.Source].Add(id);
    }

    private void DetectTestFrameworks(ICollection<VsTestDescription> tests)
    {
        if (tests == null)
        {
            _testFramework = 0;
            return;
        }

        if (tests.Any(testCase => testCase.Framework == TestFrameworks.NUnit))
        {
            _testFramework |= TestFrameworks.NUnit;
        }

        if (tests.Any(testCase => testCase.Framework == TestFrameworks.xUnit))
        {
            _testFramework |= TestFrameworks.xUnit;
        }

        if (tests.Any(testCase => testCase.Framework == TestFrameworks.MsTest))
        {
            _testFramework &= ~TestFrameworks.MsTest;
        }
    }

    private string GenerateCoreSettings(int maxCpu, string frameworkVersion, string platform)
    {
        var frameworkConfig = string.IsNullOrWhiteSpace(frameworkVersion)
            ? string.Empty
            : $"<TargetFrameworkVersion>{frameworkVersion}</TargetFrameworkVersion>" + Environment.NewLine;
        // cannot specify AnyCPU or default for VsTest
        var platformConfig = string.IsNullOrWhiteSpace(platform) || platform == "AnyCPU" || platform == "Default"
            ? string.Empty
            : $"<TargetPlatform>{SecurityElement.Escape(platform)}</TargetPlatform>" + Environment.NewLine;
        var testCaseFilter = string.IsNullOrWhiteSpace(Options.TestCaseFilter)
            ? string.Empty
            : $"<TestCaseFilter>{SecurityElement.Escape(Options.TestCaseFilter)}</TestCaseFilter>" + Environment.NewLine;
        return
            $@"
<MaxCpuCount>{Math.Max(0, maxCpu)}</MaxCpuCount>
{frameworkConfig}{platformConfig}{testCaseFilter} 
<DisableAppDomain>true</DisableAppDomain>";
    }

    private string GenerateRunSettingsForDiscovery(string frameworkVersion = null, string platform = null) =>
        $@"<RunSettings>
 <RunConfiguration>
{GenerateCoreSettings(Options.Concurrency, frameworkVersion, platform)}  <DesignMode>true</DesignMode>
 </RunConfiguration>
</RunSettings>";

    public string GenerateRunSettings(int? timeout, bool forCoverage, Dictionary<int, ITestIdentifiers> mutantTestsMap,
        string helperNameSpace, string frameworkVersion = null, string platform = null)
    {
        var settingsForCoverage = string.Empty;
        var needDataCollector = forCoverage || mutantTestsMap is not null;
        var dataCollectorSettings = needDataCollector
            ? CoverageCollector.GetVsTestSettings(
                forCoverage,
                mutantTestsMap?.Select(e => (e.Key, e.Value.GetIdentifiers().Select(x => Guid.Parse(x)))),
                helperNameSpace)
            : string.Empty;
        if (_testFramework.HasFlag(TestFrameworks.NUnit))
        {
            settingsForCoverage = "<CollectDataForEachTestSeparately>true</CollectDataForEachTestSeparately>";
        }
        if (_testFramework.HasFlag(TestFrameworks.xUnit) || _testFramework.HasFlag(TestFrameworks.MsTest))
        {
            settingsForCoverage += "<DisableParallelization>true</DisableParallelization>";
        }

        var timeoutSettings = timeout is > 0
            ? $"<TestSessionTimeout>{timeout}</TestSessionTimeout>" + Environment.NewLine
            : string.Empty;

        // we need to block parallel run to capture coverage and when testing multiple mutants in a single run
        var runSettings =
            $@"<RunSettings>
<RunConfiguration>
  <CollectSourceInformation>false</CollectSourceInformation>
{timeoutSettings}{settingsForCoverage}
<DesignMode>false</DesignMode>{GenerateCoreSettings(1, frameworkVersion, platform)}
</RunConfiguration>{dataCollectorSettings}
</RunSettings>";

        return runSettings;
    }

}
