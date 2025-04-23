@echo off
setlocal enabledelayedexpansion

:: --- Configuration ---
set "BENCHMARK_PROJECT_DIR=.\Mediator.Switch.Benchmark"
set "GENERATOR_PROJECT_DIR=.\Mediator.Switch.Benchmark.Generator"
set "GENERATED_CODE_DIR_NAME=Generated"
set "OUTPUT_ARTIFACTS_BASE_DIR=.\BenchmarkDotNet.Artifacts"

:: Define N and B values to test (space-separated)
set "N_VALUES=25 100 600"
set "B_VALUES=1 5"

:: Fixed values for cross-benchmarking
set "FIXED_N_FOR_PIPELINE_TEST=100"
set "FIXED_B_FOR_HANDLER_TEST=0"

set "BENCHMARK_PROJECT_FILE=%BENCHMARK_PROJECT_DIR%\Mediator.Switch.Benchmark.csproj"
set "GENERATOR_PROJECT_FILE=%GENERATOR_PROJECT_DIR%\Mediator.Switch.Benchmark.Generator.csproj"
set "GENERATED_CODE_FULL_PATH=%BENCHMARK_PROJECT_DIR%\%GENERATED_CODE_DIR_NAME%"
set "BUILD_CONFIG=Release"
:: --- End Configuration ---

echo Starting Benchmark Orchestration...
echo.
if not exist "%OUTPUT_ARTIFACTS_BASE_DIR%" (
    echo Creating artifacts directory: %OUTPUT_ARTIFACTS_BASE_DIR%
    md "%OUTPUT_ARTIFACTS_BASE_DIR%"
)

:: === Run Handler Scaling Benchmarks ===
echo =============================================
echo Running Handler Scaling Benchmarks (Fixed B=%FIXED_B_FOR_HANDLER_TEST%)
echo =============================================
echo.

for %%N in (%N_VALUES%) do (
    echo --- Processing N = %%N (Fixed B=%FIXED_B_FOR_HANDLER_TEST%) ---
    echo.

    :: 1. Clean
    echo Step 1: Cleaning...
    if exist "%GENERATED_CODE_FULL_PATH%" (
        echo Removing existing generated code directory: %GENERATED_CODE_FULL_PATH%
        rd /s /q "%GENERATED_CODE_FULL_PATH%"
    )
    md "%GENERATED_CODE_FULL_PATH%"
    call :run_command dotnet clean "%BENCHMARK_PROJECT_FILE%" -c "%BUILD_CONFIG%" -v q
    if errorlevel 1 goto :error

    :: 2. Generate code for current N and FIXED B
    echo Step 2: Generating code (N=%%N, B=%FIXED_B_FOR_HANDLER_TEST%)...
    call :run_command dotnet run --project "%GENERATOR_PROJECT_FILE%" -- -n %%N -b %FIXED_B_FOR_HANDLER_TEST% -o "%GENERATED_CODE_FULL_PATH%"
    if errorlevel 1 goto :error

    :: 3. Build the benchmark project
    echo Step 3: Building benchmark project...
    call :run_command dotnet build "%BENCHMARK_PROJECT_FILE%" -c "%BUILD_CONFIG%" --no-incremental
    if errorlevel 1 goto :error

    :: 4. Run BenchmarkDotNet, filtering for HandlerScalingBenchmarks class and current N
    echo Step 4: Running HandlerScalingBenchmarks for N=%%N...
    set "TARGET_FILTER=Mediator.Switch.Benchmark.HandlerScalingBenchmarks*(N: %%N)"
    set "ARTIFACT_PATH_N=%OUTPUT_ARTIFACTS_BASE_DIR%\HandlerScaling_N%%N"
    call :run_command dotnet run --project "%BENCHMARK_PROJECT_FILE%" -c "%BUILD_CONFIG%" --no-launch-profile -- --filter "!TARGET_FILTER!" --artifacts "!ARTIFACT_PATH_N!" --join
    if errorlevel 1 goto :error

    echo Completed HandlerScaling for N = %%N
    echo.
)

:: === Run Pipeline Scaling Benchmarks ===
echo =============================================
echo Running Pipeline Scaling Benchmarks (Fixed N=%FIXED_N_FOR_PIPELINE_TEST%)
echo =============================================
echo.

for %%B in (%B_VALUES%) do (
    echo --- Processing B = %%B (Fixed N=%FIXED_N_FOR_PIPELINE_TEST%) ---
    echo.

    :: 1. Clean
    echo Step 1: Cleaning...
    if exist "%GENERATED_CODE_FULL_PATH%" (
        rd /s /q "%GENERATED_CODE_FULL_PATH%"
    )
    md "%GENERATED_CODE_FULL_PATH%"
    call :run_command dotnet clean "%BENCHMARK_PROJECT_FILE%" -c "%BUILD_CONFIG%" -v q
    if errorlevel 1 goto :error

    :: 2. Generate code for FIXED N and current B
    echo Step 2: Generating code (N=%FIXED_N_FOR_PIPELINE_TEST%, B=%%B)...
    call :run_command dotnet run --project "%GENERATOR_PROJECT_FILE%" -- -n %FIXED_N_FOR_PIPELINE_TEST% -b %%B -o "%GENERATED_CODE_FULL_PATH%"
    if errorlevel 1 goto :error

    :: 3. Build the benchmark project
    echo Step 3: Building benchmark project...
    call :run_command dotnet build "%BENCHMARK_PROJECT_FILE%" -c "%BUILD_CONFIG%" --no-incremental
    if errorlevel 1 goto :error

    :: 4. Run BenchmarkDotNet, filtering for PipelineScalingBenchmarks class and current B
    echo Step 4: Running PipelineScalingBenchmarks for B=%%B...
    set "TARGET_FILTER=Mediator.Switch.Benchmark.PipelineScalingBenchmarks*(B: %%B)"
    set "ARTIFACT_PATH_B=%OUTPUT_ARTIFACTS_BASE_DIR%\PipelineScaling_B%%B"
    call :run_command dotnet run --project "%BENCHMARK_PROJECT_FILE%" -c "%BUILD_CONFIG%" --no-launch-profile -- --filter "!TARGET_FILTER!" --artifacts "!ARTIFACT_PATH_B!" --join
    if errorlevel 1 goto :error

    echo Completed PipelineScaling for B = %%B
    echo.
)


echo --------------------------------------------------
echo Benchmark Orchestration Finished Successfully!
echo Results saved in subdirectories under: %OUTPUT_ARTIFACTS_BASE_DIR%
echo --------------------------------------------------
echo.
goto :EOF

:: Subroutine to execute a command and check for errors
:run_command
    echo Executing: %*
    %*
    if errorlevel 1 (
        echo ERROR: Command failed with exit code %ERRORLEVEL%
        exit /b 1
    )
    echo Command executed successfully.
    echo.
    goto :EOF

:error
    echo.
    echo **************************************************
    echo An error occurred. Benchmark run aborted.
    echo **************************************************
    exit /b 1

:EOF
endlocal
exit /b 0