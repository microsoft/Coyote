# PowerShell v2

$PSScriptRoot = Split-Path -Parent -Path $MyInvocation.MyCommand.Definition

$CoyoteRoot = Split-Path $PSScriptRoot

$ToolPath = "$CoyoteRoot\packages"

if (-Not (Test-Path -Path "$CoyoteRoot\bin")) {
    throw "please build coyote project first"
}

function InstallToolVersion {
    Param ([string] $name, [string] $version)

    $list = dotnet tool list --tool-path $ToolPath
    $line = $list | Where-Object { $_ -Match "$name[ ]*([0-9.\-a-z]+).*" }
    $install = $false
    if ($null -eq $line) {
        Write-Host "$name is not installed."
        $install = $true
    }
    elseif (-not ($Matches[1] -eq $version)) {
        $old = $Matches[1]
        Write-Host "upgrading $name from version $old"
        dotnet tool uninstall $name --tool-path $ToolPath
        $install = $true
    }
    if ($install) {
        Write-Host "installing $name version $version."
        dotnet tool install $name --version "$version" --tool-path $ToolPath
    }
    return $installed
}

$inheritdoc = "$ToolPath\InheritDoc.exe"
$xmldoc = "$ToolPath\xmldocmd.exe"
$target = "$CoyoteRoot\docs\_learn\ref"

# install InheritDocTool
$installed = InstallToolVersion -name "InheritDocTool" -version "2.5.1"

# install xmldocmd
$installed = InstallToolVersion -name "xmldocmd" -version "2.3.0"

$frameworks = Get-ChildItem -Path "$CoyoteRoot/bin" | Where-Object Name -ne "nuget" | Select-Object -expand Name
foreach ($name in $frameworks) {
    $target = "$CoyoteRoot\bin\$name"
    Write-Host "processing inherit docs under $target ..." -ForegroundColor Yellow
    & $inheritdoc --base "$target" -o
}

$target = "$CoyoteRoot\docs\_learn\ref"

# Completely clean the ref folder so we start fresh
if (Test-Path -Path $target) {
    Remove-Item -Recurse -Force $target
}

Write-Host "Generating new markdown under $target"
& $xmldoc --namespace Microsoft.Coyote "$CoyoteRoot\bin\netcoreapp3.1\Microsoft.Coyote.dll" "$target" --front-matter "$CoyoteRoot\docs\assets\data\_front.md" --visibility protected --toc --toc-prefix /learn/ref --skip-unbrowsable --namespace-pages --permalink pretty
$coyotetoc = Get-Content -Path "$CoyoteRoot\docs\_learn\ref\toc.yml"

& $xmldoc --namespace Microsoft.Coyote.Test "$CoyoteRoot\bin\netcoreapp3.1\Microsoft.Coyote.Test.dll" "$target" --front-matter "$CoyoteRoot\docs\assets\data\_front.md" --visibility protected --toc --toc-prefix /learn/ref --skip-unbrowsable --namespace-pages --permalink pretty
$newtoc = Get-Content -Path "$CoyoteRoot\docs\_learn\ref\toc.yml"
$newtoc = [System.Collections.ArrayList]$newtoc
$newtoc.RemoveRange(0, 4); # remove -toc and assembly header
$newtoc.InsertRange(0, $coyotetoc)

$toc = "$CoyoteRoot\docs\_data\sidebar-learn.yml"

Write-Host "Merging $toc..."
# Now merge the new toc

$oldtoc = Get-Content -Path $toc

$found = $False
$start = "- title: API documentation"
$stop = "- title: Resources"
$merged = @()

for ($i = 0; $i -lt $oldtoc.Length; $i++) {
    $line = $oldtoc[$i]
    if ($line -eq $start) {
        $found = $True
        $merged += $line
        $merged += $oldtoc[$i + 1]
        $i = $i + 2  # skip to "- name: Microsoft.Coyote"
        for ($j = 4; $j -lt $newtoc.Count; $j++) {
            $line = $newtoc[$j]
            $merged += $line
        }

        # skip to the end of the api documentation

        for (; $i -lt $oldtoc.Length; $i++) {
            if ($oldtoc[$i] -eq $stop) {
                $i = $i - 1;
                break;
            }
        }
    }
    else {
        $merged += $line
    }
}

if (-Not $found) {
    throw "Did not find start item: $start"
}
else {
    Write-Host "Saving updated $toc  ..."
    Set-Content -Path "$toc" -Value $merged
}
