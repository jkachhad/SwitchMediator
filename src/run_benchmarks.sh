#!/bin/bash

# Exit immediately if a command exits with a non-zero status.
set -e

# --- Configuration ---
# Adjust these paths relative to where you run the script (usually solution root)
BENCHMARK_PROJECT_DIR="./Mediator.Switch.Benchmark"
GENERATOR_PROJECT_DIR="./Mediator.Switch.Benchmark.Generator"
GENERATED_CODE_DIR_NAME="Generated"
OUTPUT_ARTIFACTS_BASE_DIR="./BenchmarkDotNet.Artifacts"

N_VALUES=(25 200 1000) # The N values to test
B_VALUES=(0 1 5) # The BehaviorCount values to test
BENCHMARK_PROJECT_FILE="$BENCHMARK_PROJECT_DIR/Mediator.Switch.Benchmark.csproj"
GENERATOR_PROJECT_FILE="$GENERATOR_PROJECT_DIR/Mediator.Switch.Benchmark.Generator.csproj"
GENERATED_CODE_FULL_PATH="$BENCHMARK_PROJECT_DIR/$GENERATED_CODE_DIR_NAME"
BUILD_CONFIG="Release" # Always use Release for benchmarks
# --- End Configuration ---

# Function to print colored messages
print_info() {
  printf "\e[36m%s\e[0m\n" "$1" # Cyan
}
print_success() {
  printf "\e[32m%s\e[0m\n" "$1" # Green
}
print_warning() {
  printf "\e[33m%s\e[0m\n" "$1" # Yellow
}
print_error() {
  printf "\e[31m%s\e[0m\n" "$1" >&2 # Red to stderr
}

# Function to execute a command and check for errors
run_command() {
  print_info "Executing: $*"
  "$@" # Execute the command and its arguments
  local status=$?
  if [ $status -ne 0 ]; then
    print_error "Command failed with exit code $status: $*"
    exit $status
  fi
  print_success "Command executed successfully."
}

print_warning "Starting Benchmark Orchestration..."
print_info "Benchmark Project: $BENCHMARK_PROJECT_FILE"
print_info "Generator Project: $GENERATOR_PROJECT_FILE"
print_info "Generated Code Path: $GENERATED_CODE_FULL_PATH"
print_info "N Values to test: ${N_VALUES[*]}"
print_info "B (BehaviorCount) Values to test: ${B_VALUES[*]}"
print_info "Output Artifacts Base: $OUTPUT_ARTIFACTS_BASE_DIR"

# Create base artifacts directory if it doesn't exist
mkdir -p "$OUTPUT_ARTIFACTS_BASE_DIR"

# --- Loop through each N and B value ---
for N in "${N_VALUES[@]}"; do
  for B in "${B_VALUES[@]}"; do
    print_warning "--------------------------------------------------"
    print_warning "Processing N = $N, B = $B"
    print_warning "--------------------------------------------------"

    # 1. Clean generated code and build artifacts
    print_info "Step 1: Cleaning..."
    if [ -d "$GENERATED_CODE_FULL_PATH" ]; then
      print_info "Removing existing generated code directory: $GENERATED_CODE_FULL_PATH"
      rm -rf "$GENERATED_CODE_FULL_PATH"
    else
      print_info "Generated code directory does not exist, skipping removal."
    fi
    # Clean the benchmark project thoroughly
    run_command dotnet clean "$BENCHMARK_PROJECT_FILE" -c "$BUILD_CONFIG" -v q

    # 2. Generate code for the current N and B
    print_info "Step 2: Generating code for N=$N, B=$B..."
    run_command dotnet run --project "$GENERATOR_PROJECT_FILE" -- \
        -n "$N" \
        -b "$B" \
        -o "$GENERATED_CODE_FULL_PATH"

    # 3. Build the benchmark project (Source Generator runs here)
    print_info "Step 3: Building benchmark project for N=$N, B=$B..."
    # Use --no-incremental to help ensure SG runs reliably after clean/generate
    run_command dotnet build "$BENCHMARK_PROJECT_FILE" -c "$BUILD_CONFIG" --no-incremental

    # 4. Run BenchmarkDotNet, filtering for the current N and B
    print_info "Step 4: Running benchmarks for N=$N, B=$B..."
    # Match Namespace.ClassName.*(N: Value, B: Value)
    FILTER_PATTERN="Mediator.Switch.Benchmark.MediatorBenchmarks.*(N: $N, B: $B)"
    ARTIFACT_PATH_N_B="$OUTPUT_ARTIFACTS_BASE_DIR/N${N}_B${B}"
    # Note the structure for passing args after -- to the benchmark app
    run_command dotnet run --project "$BENCHMARK_PROJECT_FILE" -c "$BUILD_CONFIG" --no-launch-profile -- \
        --filter "$FILTER_PATTERN" \
        --artifacts "$ARTIFACT_PATH_N_B" \
        --join # Optional: Join results if multiple benchmark classes match filter

    print_success "Completed processing for N = $N, B = $B"
  done
done

print_warning "--------------------------------------------------"
print_success "Benchmark Orchestration Finished Successfully!"
print_info "Results saved in subdirectories under: $OUTPUT_ARTIFACTS_BASE_DIR"
print_warning "--------------------------------------------------"

exit 0