@echo off
setlocal EnableDelayedExpansion

:: --- Configuration ---
set "BENCHMARK_PROJECT_DIR=.\Mediator.Switch.Benchmark"
set "GENERATOR_PROJECT_DIR=.\Mediator.Switch.Benchmark.Generator"
set "GENERATED_CODE_DIR_NAME=Generated"
set "OUTPUT_ARTIFACTS_BASE_DIR=.\BenchmarkDotNet.Artifacts"
set "N_VALUES=25 200 1000"
set "B_VALUES=0 1 5"
set "BENCHMARK_PROJECT_FILE=%BENCHMARK_PROJECT_DIR%\Mediator.Switch.Benchmark.csproj"
set "GENERATOR_PROJECT_FILE=%GENERATOR_PROJECT_DIR%\Mediator.Switch.Benchmark.Generator.csproj"
set "GENERATED_CODE_FULL_PATH=%BENCHMARK_PROJECT_DIR%\%GENERATED_CODE_DIR_NAME%"
set "BUILD_CONFIG=Release"
:: --- End Configuration ---

:: Function to print messages
echo INFO: Starting Benchmark Orchestration...
echo INFO: Benchmark Project: %BENCHMARK_PROJECT_FILE%
echo INFO: Generator Project: %GENERATOR_PROJECT_FILE%
echo INFO: Generated Code Path: %GENERATED_CODE_FULL_PATH%
echo INFO: N Values to test: %N_VALUES%
echo INFO: B (BehaviorCount) Values to test: %B_VALUES%
echo INFO: Output Artifacts Base: %OUTPUT_ARTIFACTS_BASE_DIR%

:: Create base artifacts directory if it doesn't exist
if not exist "%OUTPUT_ARTIFACTS_BASE_DIR%" (
    echo INFO: Creating artifacts directory: %OUTPUT_ARTIFACTS_BASE_DIR%
    mkdir "%OUTPUT_ARTIFACTS_BASE_DIR%"
)

:: --- Loop through each N and B value ---
for %%N in (%N_VALUES%) do (
    for %%B in (%B_VALUES%) do (
        echo WARNING: --------------------------------------------------
        echo WARNING: Processing N = %%N, B = %%B
        echo WARNING: --------------------------------------------------

        :: 1. Clean generated code and build artifacts
        echo INFO: Step 1: Cleaning...
        if exist "%GENERATED_CODE_FULL_PATH%" (
            echo INFO: Removing existing generated code directory: %GENERATED_CODE_FULL_PATH%
            rmdir /s /q "%GENERATED_CODE_FULL_PATH%"
        ) else (
            echo INFO: Generated code directory does not exist, skipping removal.
        )
        :: Clean the benchmark project
        echo INFO: Executing: dotnet clean %BENCHMARK_PROJECT_FILE% -c %BUILD_CONFIG% -v q
        dotnet clean "%BENCHMARK_PROJECT_FILE%" -c "%BUILD_CONFIG%" -v q
        if !ERRORLEVEL! neq 0 (
            echo ERROR: Command failed: dotnet clean
            exit /b !ERRORLEVEL!
        )
        echo INFO: Command executed successfully.

        :: 2. Generate code for the current N and B
        echo INFO: Step 2: Generating code for N=%%N, B=%%B...
        echo INFO: Executing: dotnet run --project %GENERATOR_PROJECT_FILE% -- -n %%N -b %%B -o %GENERATED_CODE_FULL_PATH%
        dotnet run --project "%GENERATOR_PROJECT_FILE%" -- -n %%N -b %%B -o "%GENERATED_CODE_FULL_PATH%"
        if !ERRORLEVEL! neq 0 (
            echo ERROR: Command failed: dotnet run --project %GENERATOR_PROJECT_FILE%
            exit /b !ERRORLEVEL!
        )
        echo INFO: Command executed successfully.

        :: 3. Build the benchmark project
        echo INFO: Step 3: Building benchmark project for N=%%N, B=%%B...
        echo INFO: Executing: dotnet build %BENCHMARK_PROJECT_FILE% -c %BUILD_CONFIG% --no-incremental
        dotnet build "%BENCHMARK_PROJECT_FILE%" -c "%BUILD_CONFIG%" --no-incremental
        if !ERRORLEVEL! neq 0 (
            echo ERROR: Command failed: dotnet build
            exit /b !ERRORLEVEL!
        )
        echo INFO: Command executed successfully.

        :: 4. Run BenchmarkDotNet, filtering for the current N and B
        echo INFO: Step 4: Running benchmarks for N=%%N, B=%%B...
        set "FILTER_PATTERN=Mediator.Switch.Benchmark.MediatorBenchmarks.*(N: %%N, B: %%B)"
        set "ARTIFACT_PATH_N_B=%OUTPUT_ARTIFACTS_BASE_DIR%\N%%N_B%%B"
        echo INFO: Executing: dotnet run --project %BENCHMARK_PROJECT_FILE% -c %BUILD_CONFIG% --no-launch-profile -- --filter "!FILTER_PATTERN!" --artifacts "!ARTIFACT_PATH_N_B!" --join
        dotnet run --project "%BENCHMARK_PROJECT_FILE%" -c "%BUILD_CONFIG%" --no-launch-profile -- --filter "!FILTER_PATTERN!" --artifacts "!ARTIFACT_PATH_N_B!" --join
        if !ERRORLEVEL! neq 0 (
            echo ERROR: Command failed: dotnet run --project %BENCHMARK_PROJECT_FILE%
            exit /b !ERRORLEVEL!
        )
        echo INFO: Command executed successfully.

        echo INFO: Completed processing for N = %%N, B = %%B
    )
)

echo WARNING: --------------------------------------------------
echo INFO: Benchmark Orchestration Finished Successfully!
echo INFO: Results saved in subdirectories under: %OUTPUT_ARTIFACTS_BASE_DIR%
echo WARNING: --------------------------------------------------

exit /b 0