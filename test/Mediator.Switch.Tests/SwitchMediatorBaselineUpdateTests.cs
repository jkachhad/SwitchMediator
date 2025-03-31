using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mediator.Switch.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Mediator.Switch.Tests;

public class SwitchMediatorBaselineUpdateTests
{
    private readonly ITestOutputHelper _output;

    private static ImmutableArray<MetadataReference>? _metadataReferences;
    private static readonly ReferenceAssemblies _referenceAssemblies =
        ReferenceAssemblies.Net.Net90.AddPackages([
            new PackageIdentity("FluentValidation", "11.11.0")
        ]);
    private static readonly Assembly _mediatorAssembly = typeof(IRequest<>).Assembly; // Assuming IRequest is in Mediator assembly

    public SwitchMediatorBaselineUpdateTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [TheoryRunnableInDebugOnly]
    [InlineData("Basic")]
    [InlineData("MultipleRequests")]
    [InlineData("PolymorphicRequests")]
    [InlineData("Notifications")]
    [InlineData("BasicPipeline")]
    [InlineData("BasicPipelineNestedType")]
    [InlineData("BasicPipelineAdapted")]
    [InlineData("ConstrainedPipeline")]
    [InlineData("OrderedPipeline")]
    [InlineData("FullPipeline")]
    [InlineData("NoMessages")]
    public async Task UpdateExpectedOutputFile(string testCase)
    {
        await InitializeReferencesAsync(_output); // Ensure references are ready

        var inputPath = Path.Combine("TestCases", testCase, "Input.cs");
        var expectedPath = Path.Combine(Path.GetDirectoryName(GetThisFilePath())!, "TestCases", testCase, "Expected.txt");
        const string expectedHintName = "SwitchMediator.g.cs";

        _output.WriteLine($"Processing test case: {testCase}");
        _output.WriteLine($"Input: {Path.GetFullPath(inputPath)}");
        _output.WriteLine($"Output (will be overwritten): {Path.GetFullPath(expectedPath)}");


        var inputCode = await File.ReadAllTextAsync(inputPath);

        // --- Manually run the generator ---
        var syntaxTree = CSharpSyntaxTree.ParseText(inputCode, new CSharpParseOptions(LanguageVersion.Latest));

        // Ensure we have references resolved
        if (_metadataReferences == null) {
            Assert.Fail("Metadata references were not initialized correctly.");
            return; // Keep compiler happy
        }

        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        var compilation = CSharpCompilation.Create(
            assemblyName: $"BaselineUpdate_{testCase}_{Guid.NewGuid()}", // Unique name
            syntaxTrees: [syntaxTree],
            references: _metadataReferences.Value, // Use resolved references
            options: compilationOptions);

        // Check for pre-compilation errors (syntax errors in input etc.)
        var preDiagnostics = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (preDiagnostics.Any())
        {
            var errorMessages = string.Join(Environment.NewLine, preDiagnostics.Select(d => d.ToString()));
            Assert.Fail($"Input code for '{testCase}' has compilation errors:{Environment.NewLine}{errorMessages}");
        }


        // Create an instance of the generator
        var generator = new SwitchMediatorSourceGenerator();
        var sourceGenerator = generator;

        // Create the driver
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [sourceGenerator],
            optionsProvider: null,
            parseOptions: (CSharpParseOptions)syntaxTree.Options,
            additionalTexts: ImmutableArray<AdditionalText>.Empty);

        // Run the generator
        var sw = Stopwatch.StartNew();
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        sw.Stop();
        _output.WriteLine($"Generator execution took {sw.ElapsedMilliseconds} ms");

        // --- Check for generator or post-generator compilation diagnostics ---
        var criticalDiagnostics = diagnostics
            .Concat(outputCompilation.GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (criticalDiagnostics.Any())
        {
            var errorMessages = string.Join(Environment.NewLine, criticalDiagnostics.Select(d => d.ToString()));
            // Write errors to output for easier debugging
            _output.WriteLine($"--- ERRORS Encountered for {testCase} ---");
            _output.WriteLine(errorMessages);
            _output.WriteLine($"--------------------------------------");
            // Fail the test so the baseline isn't updated with error output
            Assert.Fail($"Generator or compilation errors encountered during baseline update for '{testCase}'. See test output for details.");
        }
        var warnings = diagnostics
            .Concat(outputCompilation.GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Warning)
            .ToList();
        if (warnings.Any())
        {
            _output.WriteLine($"--- WARNINGS Encountered for {testCase} ---");
            foreach (var warn in warnings) _output.WriteLine(warn.ToString());
            _output.WriteLine($"---------------------------------------");
        }


        // --- Get the generated source text ---
        var runResult = driver.GetRunResult();
        var actualGeneratedCode = $"// ERROR: Generator {nameof(SwitchMediatorSourceGenerator)} did not produce expected output '{expectedHintName}' for test case '{testCase}'."; // Default Error Message

        var generatorResult = runResult.Results.Select(x => (GeneratorRunResult?) x).FirstOrDefault(r => r?.Generator.GetType() == typeof(SwitchMediatorSourceGenerator));
        if (generatorResult != null)
        {
            if (generatorResult.Value.Exception != null)
            {
                _output.WriteLine($"--- GENERATOR EXCEPTION for {testCase} ---");
                _output.WriteLine(generatorResult.Value.Exception.ToString());
                _output.WriteLine($"---------------------------------------");
                Assert.Fail($"Generator threw an exception during baseline update for '{testCase}': {generatorResult.Value.Exception.Message}");
            }

            var generatedFile = generatorResult.Value.GeneratedSources.Select(x => (GeneratedSourceResult?) x).FirstOrDefault(gs => gs?.HintName == expectedHintName);
            if (generatedFile != null)
            {
                actualGeneratedCode = generatedFile.Value.SourceText.ToString();
                _output.WriteLine($"Successfully generated source for hint '{expectedHintName}'. Length: {actualGeneratedCode.Length}");
            }
            else
            {
                // If the generator ran but didn't produce the specific file, that's also an error for baseline update
                _output.WriteLine($"--- ERROR: MISSING OUTPUT FILE for {testCase} ---");
                _output.WriteLine($"Generator ran, but did not produce output with hint name '{expectedHintName}'.");
                _output.WriteLine($"Generated files: [{string.Join(", ", generatorResult.Value.GeneratedSources.Select(gs => gs.HintName))}]");
                _output.WriteLine($"------------------------------------------------");
                Assert.Fail($"Generator ran but did not produce expected output '{expectedHintName}' for test case '{testCase}'.");
            }
        } else {
            _output.WriteLine($"--- ERROR: GENERATOR DID NOT RUN for {testCase} ---");
            // This case is less likely with CSharpGeneratorDriver.Create but good to check
            Assert.Fail($"Generator {nameof(SwitchMediatorSourceGenerator)} did not run or was not found in the results for test case '{testCase}'.");
        }


        // --- Normalize and Overwrite the Expected.txt file ---
        var normalizedActualOutput = Normalize(actualGeneratedCode);

        Directory.CreateDirectory(Path.GetDirectoryName(expectedPath)!); // Ensure directory exists
        await File.WriteAllTextAsync(expectedPath, normalizedActualOutput);
        _output.WriteLine($"Successfully updated baseline file: {expectedPath}");

        // --- No Assertions needed here beyond checking for errors ---
        // The goal of this test *is* to write the file.
        // We assert fail above if errors occur during generation.
    }

    private static string Normalize(string code) =>
        string.Join(Environment.NewLine,
            code.Replace("\r\n", "\n").TrimEnd().Split('\n')
                .Select(line => line.TrimEnd()));
    
    private static string GetThisFilePath([CallerFilePath] string? path = null) => path!;
    
    private static async Task InitializeReferencesAsync(ITestOutputHelper output)
    {
        if (_metadataReferences == null)
        {
            output.WriteLine("Resolving metadata references for baseline update...");
            var references = new List<MetadataReference>();
            var resolvedFrameworkReferences = await _referenceAssemblies.ResolveAsync(null, CancellationToken.None);
            references.AddRange(resolvedFrameworkReferences);

            // Add the Mediator assembly reference manually
            if (!_mediatorAssembly.IsDynamic && !string.IsNullOrEmpty(_mediatorAssembly.Location))
            {
                references.Add(MetadataReference.CreateFromFile(_mediatorAssembly.Location));
                output.WriteLine($"Added reference: {_mediatorAssembly.Location}");
            }
            else
            {
                output.WriteLine($"Warning: Could not resolve location for {_mediatorAssembly.FullName}. Baseline update might be incomplete.");
                // Consider failing or adding alternative ways to get the reference if needed
            }

            // Add any other essential AdditionalReferences here similarly
            // Example: references.Add(MetadataReference.CreateFromFile(typeof(SomeOtherType).Assembly.Location));

            _metadataReferences = references.ToImmutableArray();
            output.WriteLine($"Resolved {_metadataReferences.Value.Length} references.");
        }
    }
}