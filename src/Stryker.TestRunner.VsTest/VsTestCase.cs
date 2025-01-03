using System;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Stryker.Abstractions.Testing;
using ITestCase = Stryker.Abstractions.Testing.ITestCase;

namespace Stryker.TestRunner.VsTest;

public class VsTestCase : ITestCase
{
    public VsTestCase(TestCase testCase)
    {
        OriginalTestCase = testCase;
        Id = Identifier.Create(testCase.Id);
        Name = testCase.DisplayName;
        FullyQualifiedName = testCase.FullyQualifiedName;
        Uri = testCase.ExecutorUri;
        CodeFilePath = testCase.CodeFilePath ?? string.Empty;
        LineNumber = testCase.LineNumber;
        Source = testCase.Source;
    }

    public TestCase OriginalTestCase { get; }

    public Identifier Id { get; }

    public string Name { get; }

    public Uri Uri { get; }

    public string CodeFilePath { get; }

    public string FullyQualifiedName { get; }

    public int LineNumber { get; }

    public string Source { get; }
}
