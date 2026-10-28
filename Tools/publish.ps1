<#
.SYNOPSIS
  发布 db-chronicle：基础包 + 按需插件，输出到指定目录。

.DESCRIPTION
  CLI 自身只依赖核心库（基础能力：init/new/compile/check/status/verify/rollback、codegen）。
  数据库执行与源码扫描由插件提供，放在 <输出>/plugins 下，按需加载。
  不选任何插件时产出的是"离线编译包"，体积最小。

.EXAMPLE
  # 只要离线能力（最小包）
  ./Tools/publish.ps1 -Output dist

  # 带 MySQL/PG/SQL Server/SQLite 执行能力
  ./Tools/publish.ps1 -Output dist -Plugins adonet

  # 全量（执行器 + 各语言源码扫描）
  ./Tools/publish.ps1 -Output dist -Plugins all

  # 自包含（目标机无需安装 .NET 运行时）
  ./Tools/publish.ps1 -Output dist -Plugins adonet -SelfContained
#>
[CmdletBinding()]
param(
    [string]$Output = "dist",
    [string[]]$Plugins = @("adonet"),
    [switch]$SelfContained,
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$outDir = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }
$pluginDir = Join-Path $outDir "plugins"

# 插件名 -> 工程路径
$catalog = @{
    "adonet"    = "Src/DbChronicle.Executor.AdoNet/DbChronicle.Executor.AdoNet.csproj"
    "source"    = "Src/DbChronicle.Scanner.Source/DbChronicle.Scanner.Source.csproj"
    "languages" = "Src/DbChronicle.Scanner.Languages/DbChronicle.Scanner.Languages.csproj"
}

$selected = @()
foreach ($name in $Plugins) {
    if ($name -eq "all") {
        $selected = @($catalog.Keys | Sort-Object)
        break
    }
    if (-not $catalog.ContainsKey($name)) {
        throw "未知插件：$name（可用：$($catalog.Keys -join ' / ') / all）"
    }
    $selected += $name
}
$selected = $selected | Select-Object -Unique

Write-Host "== 发布 db-chronicle ==" -ForegroundColor Cyan
Write-Host "  输出目录 : $outDir"
Write-Host "  插件     : $(if ($selected.Count -eq 0) { '（无，仅离线能力）' } else { $selected -join ', ' })"
Write-Host "  自包含   : $([bool]$SelfContained)"
Write-Host ""

# 1) 发布 CLI（基础包）
$publishArgs = @("publish", (Join-Path $root "Src/DbChronicle.Cli/DbChronicle.Cli.csproj"),
    "-c", $Configuration, "-o", $outDir, "--nologo", "-v", "q")
if ($SelfContained) {
    $publishArgs += @("-r", $Runtime, "--self-contained", "true")
}
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "CLI 发布失败" }

# 2) 构建并复制插件（含第三方依赖与 runtimes 原生库）
if ($selected.Count -gt 0) {
    if (Test-Path $pluginDir) { Remove-Item -Recurse -Force $pluginDir }
    New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null

    foreach ($name in $selected) {
        $project = Join-Path $root $catalog[$name]
        $projectDir = Split-Path -Parent $project
        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)

        Write-Host "  构建插件：$name" -ForegroundColor DarkGray
        & dotnet build $project -c $Configuration --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "插件构建失败：$name" }

        $binDir = Join-Path $projectDir "bin/$Configuration/net8.0"
        if (-not (Test-Path $binDir)) { throw "未找到插件输出：$binDir" }

        Get-ChildItem $binDir -Recurse -File |
            Where-Object {
                $_.FullName -notlike "*\ref\*" -and
                $_.Extension -in @(".dll", ".json") -and
                $_.Name -notin @("DbChronicle.dll", "db-chronicle.dll")
            } |
            ForEach-Object {
                $relative = $_.FullName.Substring($binDir.Length).TrimStart('\', '/')
                $target = Join-Path $pluginDir $relative
                $targetDirectory = Split-Path -Parent $target
                if (-not (Test-Path $targetDirectory)) { New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null }
                Copy-Item $_.FullName $target -Force
            }
    }
}

# 3) 汇总
$cliSize = (Get-ChildItem $outDir -File -Recurse | Measure-Object Length -Sum).Sum
$pluginSize = if (Test-Path $pluginDir) { (Get-ChildItem $pluginDir -File -Recurse | Measure-Object Length -Sum).Sum } else { 0 }
$pluginCount = if (Test-Path $pluginDir) { (Get-ChildItem $pluginDir -File -Recurse | Measure-Object).Count } else { 0 }

Write-Host ""
Write-Host "== 完成 ==" -ForegroundColor Green
Write-Host ("  总计     : {0:N1} MB / {1} 个文件" -f ($cliSize / 1MB), (Get-ChildItem $outDir -File -Recurse | Measure-Object).Count)
Write-Host ("  plugins  : {0:N1} MB / {1} 个文件" -f ($pluginSize / 1MB), $pluginCount)
Write-Host ""
Write-Host "可用命令（无插件即可用）：init / new / compile / check / status / verify / rollback(dry-run) / codegen / scan --assembly"
if ($selected -contains "adonet") {
    Write-Host "需要 plugins/DbChronicle.Executor.AdoNet.dll：upgrade / scan-db / rollback --apply"
}
if ($selected -contains "source") {
    Write-Host "需要 plugins/DbChronicle.Scanner.Source.dll：scan --source --lang csharp"
}
if ($selected -contains "languages") {
    Write-Host "需要 plugins/DbChronicle.Scanner.Languages.dll：scan --source --lang java|go|typescript|auto"
}
