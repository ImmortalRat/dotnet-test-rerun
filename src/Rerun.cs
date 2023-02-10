﻿using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO.Abstractions;
using TrxFileParser;

namespace dotnet.test.rerun
{
    public class RerunCommand : RootCommand
    {
        private readonly Logger Log;
        private readonly RerunCommandConfiguration config;
        private readonly dotnet dotnet;
        private readonly IFileSystem fileSystem;

        public RerunCommand(Logger logger, RerunCommandConfiguration config, dotnet dotnet, IFileSystem fileSystem)
        {
            this.Log = logger;
            this.config = config;
            this.dotnet = dotnet;
            this.fileSystem = fileSystem;

            // Set Arguments and Options
            config.Set(this);

            this.SetHandler((context) =>
            {
                config.GetValues(context);
                Run();
            });
        }

        public void Run()
        {
            dotnet.Run(config.Path, config.Filter, config.Settings, config.Logger, config.ResultsDirectory);

            IDirectoryInfo resultsDirectory = fileSystem.DirectoryInfo.New(config.ResultsDirectory);
            IFileInfo dll = fileSystem.FileInfo.New(config.Path);

            var attempt = 1;
            while (attempt < config.RerunMaxAttempts)
            {
                var trxFile = resultsDirectory.EnumerateFiles("*.trx").OrderBy(f => f.Name).LastOrDefault();
                var testsToRun = GetFailedTestsFilter(trxFile);
                if (!string.IsNullOrEmpty(testsToRun))
                {
                    Log.Information($"Found Failed tests. Rerun filter: {testsToRun}");
                    dotnet.Run(dll.FullName, config.Filter, config.Settings, config.Logger, config.ResultsDirectory);
                    attempt++;
                }
                else
                {
                    Log.Information($"Rerun attempt {attempt} not needed. All testes Passed.");
                    attempt = config.RerunMaxAttempts;
                }
            }
        }

        private string GetFailedTestsFilter(IFileInfo trxFile)
        {
            var testFilter = string.Empty;
            var outcome = "Failed";

            if (trxFile != null)
            {
                var trx = TrxDeserializer.Deserialize(trxFile.FullName);

                var tests = trx.Results.UnitTestResults.Where(t => t.Outcome.Equals(outcome, StringComparison.InvariantCultureIgnoreCase)).ToList();

                if (tests != null && !tests.Any())
                {
                    Log.Warning($"No tests found with the Outcome {outcome}");
                }
                else
                {
                    for (var i = 0; i < tests.Count(); i++)
                    {
                        testFilter += $"FullyQualifiedName~{tests[0].TestName}" + (tests.Count() - 1 != i ? " | " : string.Empty);
                    }
                    Log.Verbose(testFilter);
                }
            }
            return testFilter;
        }
    }

    public class RerunCommandConfiguration
    {
        #region Properties

        public string Path { get; private set; }
        public string Filter { get; private set; }
        public string Settings { get; private set; }
        public string Logger { get; private set; }
        public string ResultsDirectory { get; private set; }
        public int RerunMaxAttempts { get; private set; }

        #endregion Properties

        #region Arguments

        private Argument<string> PathArgument = new("path")
        {
            Description = "Path to a test project .dll file."
        };

        #endregion Arguments

        #region Options

        private Option<string> FilterOption = new(new[] { "--filter" })
        {
            Description = "Run tests that match the given expression.",
            IsRequired = true
        };

        private Option<string> SettingsOption = new(new[] { "--settings", "-s" })
        {
            Description = "The run settings file to use when running tests.",
            IsRequired = true
        };

        private Option<string> loggerOption = new(new[] { "--logger", "-l" }, getDefaultValue: () => "trx")
        {
            Description = "Specifies a logger for test results.",
            IsRequired = false
        };

        private Option<string> ResultsDirectoryOption = new(new[] { "--results-directory", "-r" }, getDefaultValue: () => ".")
        {
            Description = "The directory where the test results will be placed.\nThe specified directory will be created if it does not exist.",
            IsRequired = false
        };

        private Option<int> RerunMaxAttemptsOption = new(new[] { "--rerunMaxAttempts" }, getDefaultValue: () => 3)
        {
            Description = "Maximum # of attempts. Default: 3.",
            IsRequired = false
        };

        #endregion Options

        public void Set(Command cmd)
        {
            cmd.Add(PathArgument);
            cmd.Add(FilterOption);
            cmd.Add(SettingsOption);
            cmd.Add(loggerOption);
            cmd.Add(ResultsDirectoryOption);
            cmd.Add(RerunMaxAttemptsOption);
        }

        public void GetValues(InvocationContext context)
        {
            Path = context.ParseResult.GetValueForArgument(PathArgument);
            Filter = context.ParseResult.GetValueForOption(FilterOption);
            Settings = context.ParseResult.GetValueForOption(SettingsOption);
            Logger = context.ParseResult.GetValueForOption(loggerOption);
            ResultsDirectory = context.ParseResult.GetValueForOption(ResultsDirectoryOption);
            RerunMaxAttempts = context.ParseResult.GetValueForOption(RerunMaxAttemptsOption);
        }
    }
}