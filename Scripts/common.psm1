# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

function CheckPSVersion() {
     $ver = $PSVersionTable["PSVersion"]
     if ($ver -lt [version]"7.0.0") {
         Write-Error "Please use latest version of 'pwsh' to run this script"
         Write-Error "You can install it using: dotnet tool install -f powershell" 
         Exit 1
     }
}

# Invokes the specified coyote tool command on the specified target.
function Invoke-CoyoteTool([String]$cmd, [String]$dotnet, [String]$framework, [String]$target, [String]$key) {
    Write-Comment -prefix "..." -text "Rewriting '$target' ($framework)"

    $tool = Join-Path -Path "." -ChildPath "bin" -AdditionalChildPath @($framework, "coyote.exe")
    $command = "$cmd $target"

    if (-not (Test-Path $tool)) {
        $tool = $dotnet
        $coyote = Join-Path -Path "." -ChildPath "bin" -AdditionalChildPath @($framework, "coyote.dll")
        $command = "$coyote $cmd $target"
    }

    if ($command -eq "rewrite" -and $framework -ne "net6.0" -and $framework -ne "net8.0" -and $IsWindows) {
        # NOTE: Mono.Cecil cannot sign assemblies on unix platforms.
        $command = "$command -snk $key"
    }

    Write-Comment -prefix "..." -text "$tool"
    $error_msg = "Failed to $cmd '$target'"
    Invoke-ToolCommand -tool $tool -cmd $command -error_msg $error_msg
}

# Builds the specified .NET project
function Invoke-DotnetBuild([String]$dotnet, [String]$solution, [String]$config, [bool]$local, [bool]$nuget) {
    Write-Comment -prefix "..." -text "Building $solution"

    $platform = "/p:Platform=`"Any CPU`""
    $restore_command = "restore $solution $platform"
    $build_command = "build -c $config $solution --no-restore  $platform"
    if ($local -and $nuget) {
        $nuget_config_file = "$PSScriptRoot/../NuGet.config"
        $restore_command = "$restore_command --configfile $nuget_config_file /p:UseLocalNugetPackages=true"
        $build_command = "$build_command /p:UseLocalNugetPackages=true"
    } elseif ($local) {
        $nuget_config_file = "$PSScriptRoot/../Samples/NuGet.config"
        $restore_command = "$restore_command --configfile $nuget_config_file /p:UseLocalCoyote=true"
        $build_command = "$build_command /p:UseLocalCoyote=true"
    }

    Write-Host $restore_command
    Invoke-ToolCommand -tool $dotnet -cmd $restore_command -error_msg "Failed to restore $solution"
    Write-Host $build_command
    Invoke-ToolCommand -tool $dotnet -cmd $build_command -error_msg "Failed to build $solution"
}

# Runs the specified .NET test using the specified framework.
function Invoke-DotnetTest([String]$dotnet, [String]$project, [String]$target, [string]$filter, [string]$framework, [string]$logger, [string]$verbosity) {
    Write-Comment -prefix "..." -text "Testing '$project' ($framework)"
    if (-not (Test-Path $target)) {
        Write-Error "tests for '$project' ($framework) not found."
        exit
    }

    $command = "test $target -f $framework --no-build -v $verbosity --logger 'trx' --blame --blame-crash"
    if (!($filter -eq "")) {
        $command = "$command --filter $filter"
    }

    if (!($logger -eq "")) {
        $command = "$command --logger $logger"
    }

    $error_msg = "Failed to test '$project'"
    Invoke-ToolCommand -tool $dotnet -cmd $command -error_msg $error_msg
}

# Runs the specified tool command.
function Invoke-ToolCommand([String]$tool, [String]$cmd, [String]$error_msg) {
    Write-Host "Invoking $tool $cmd"
    Invoke-Expression "$tool $cmd"
    if (-not ($LASTEXITCODE -eq 0)) {
        Write-Error $error_msg
        exit $LASTEXITCODE
    }
}

function FindProgram([String]$name) {
    $result = $null
    $path = $ENV:PATH.split([System.IO.Path]::PathSeparator) | ForEach-Object {
        $test = Join-Path -Path $_ -ChildPath $name
        if ($IsWindows) {
            $test = $test + ".exe"
        }
        if (Test-Path -Path $test) {
            $result = $test
        }
    }
    return $result
}

function GetAssemblyName([String]$path){
    $AssemblyName = $null;
    $doc = [System.Xml.Linq.XDocument]::Load($path);
    $name = [System.Xml.Linq.XName]::Get("AssemblyName", $r.Name.Namespace);
    $doc.Root.Descendants($name) | ForEach-Object { $AssemblyName = $_.Value };
    return $AssemblyName
}

# Finds the path of the .NET SDK.
function FindDotNetSdkPath([String]$dotnet) {
    $dotnet_sdks = Invoke-Expression "$dotnet --list-sdks"
    $dotnet_sdk_path = $dotnet_sdks | ForEach-Object {
        $sdk_path = ($_ -split {$_ -eq '[' -or $_ -eq ']'})[1]
        return $sdk_path
    }

    if ($dotnet_sdk_path -is [array]) {
        $dotnet_sdk_path = $dotnet_sdk_path[0]
    }

    return $dotnet_sdk_path
}

# Finds the path of the .NET runtime.
function FindDotNetRuntimePath([String]$dotnet, [String]$runtime) {
    $dotnet_runtimes = Invoke-Expression "$dotnet --list-runtimes"
    $dotnet_runtime_path = $dotnet_runtimes | ForEach-Object {
        $runtime_path = ($_ -split {$_ -eq '[' -or $_ -eq ']'})[1]
        if ($runtime_path.Contains($runtime)) {
            return $runtime_path
        }
    }

    if ($dotnet_runtime_path -is [array]) {
        $dotnet_runtime_path = $dotnet_runtime_path[0]
    }

    return $dotnet_runtime_path
}

# Finds the dotnet SDK version.
function FindDotNetSdkVersion([String]$dotnet_sdk_path) {
    $globalJson = Join-Path -Path $PSScriptRoot -ChildPath ".." -AdditionalChildPath @("global.json")
    $json = Get-Content $globalJson | Out-String | ConvertFrom-Json
    $global_version = $json.sdk.version
    Write-Comment -prefix "..." -text "Searching for .NET SDK version '$global_version' in '$dotnet_sdk_path'"
    $matching_version = FindMatchingVersion -path $dotnet_sdk_path -version $global_version
    if ($null -ne $matching_version) {
        if ($global_version -eq $matching_version) {
            Write-Comment -prefix "....." -text "Found expected .NET SDK version '$matching_version'"
        }
    }

    return $matching_version
}

# Finds the dotnet runtime version.
function FindDotNetRuntimeVersion([String]$dotnet_runtime_path) {
    $globalJson = Join-Path -Path $PSScriptRoot -ChildPath ".." -AdditionalChildPath @("global.json")
    $json = Get-Content $globalJson | Out-String | ConvertFrom-Json
    $global_version = $json.sdk.version
    return FindMatchingVersion -path $dotnet_runtime_path -version $global_version
}

# Searches the specified directory for the closest match for the given version.
function FindMatchingVersion([String]$path, [version]$version) {
    $matching_version = $null
    $best_match = $null
    $exact_match = $false
    if ("" -ne $path) {
        foreach ($item in Get-ChildItem $path  -directory) {
            $name = $item.Name
            $global_version = $name
            if ($name.Contains("-preview")) {
                # For the string to be legal in global.json it must
                # be major.minor.patch or major.minor.patch-preview.
                # So we have to remove any preview version like you see
                # in "5.0.100-preview.7.20366.6"
                $name = $name.Split("-preview")[0]
                $global_version = "$name-preview"
            }

            try {
              $v = [version] $name
              if ($v.Major -eq $version.Major -and $v.Minor -eq $version.Minor ) {
                if ($null -eq $best_match) {
                    $best_match = $v
                    $matching_version = $global_version
                }
                elseif ($v.Build -eq $version.Build) {
                    $exact_match = $true
                    $best_match = $v
                    $matching_version = $global_version
                }
                elseif ($v -gt $best_match -and $exact_match -eq $false) {
                    # Use the newest version then.
                    $best_match = $v
                    $matching_version = $global_version
                }
              }
            } catch {
               # Ignore 'NuGetFallbackFolder' and other none version numbered folders.
            }
        }

        return [string] $matching_version
    }
}

function Write-Comment([String]$prefix, [String]$text, [String]$color = "white") {
    if ($prefix.Length -gt 0) {
        $prefix = "$prefix "
    }

    Write-Host $prefix -b "black" -nonewline; Write-Host $text -b "black" -f $color
}

function Write-Error([String]$text) {
    Write-Host "Error: $text" -b "black" -f "red"
}
