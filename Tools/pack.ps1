<#
.SYNOPSIS
  产出 dotnet tool 的 NuGet 包（三个变体）。

.DESCRIPTION
  | 包 | 内含插件 | 用途 |
  | DbChronicle.Cli         | 无                       | 只要 compile / check / status / codegen |
  | DbChronicle.Cli.AdoNet  | Executor.AdoNet          | 追加 upgrade / scan-db / rollback --apply |
  | DbChronicle.Cli.All     | AdoNet + Source + Languages | 装完即用，可用 plugin disable 关掉不需要的 |

  插件在包内按子目录隔离（tools/net8.0/any/plugins/<插件名>/），
  用户可用 `db-chronicle plugin disable <name>` 启用/禁用，状态存用户目录，
  `dotnet tool update` 不会恢复被禁用的插件。

.EXAMPLE
  ./Tools/pack.ps1
  ./Tools/pack.ps1 -Output nupkg -Configuration Release
#>
[CmdletBinding()]
param(
    [string]$Output = "nupkg",
    [string]$Configuration = "Release",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$outDir = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

$cliProject = Join-Path $root "Src/DbChronicle.Cli/DbChronicle.Cli.csproj"

# 插件必须先生成，否则打包时 None Include 匹配不到文件
if (-not $SkipBuild) {
    Write-Host "== 构建解决方案（含插件）==" -ForegroundColor Cyan
    & dotnet build (Join-Path $root "Src/DbChronicle.sln") -c $Configuration --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "构建失败" }
}

# PackageId 由 csproj 里的条件 PropertyGroup 决定（不能用 -p:PackageId 覆盖，会与项目名冲突）
$variants = @(
    [pscustomobject]@{ Id = "DbChronicle.Cli";        Extra = @() },
    [pscustomobject]@{ Id = "DbChronicle.Cli.AdoNet"; Extra = @("-p:IncludeAdoNet=true") },
    [pscustomobject]@{ Id = "DbChronicle.Cli.All";    Extra = @("-p:IncludeAdoNet=true", "-p:IncludeScannerSource=true", "-p:IncludeScannerLanguages=true") }
)

Write-Host ""
foreach ($variant in $variants) {
    Write-Host ("== 打包 " + $variant.Id + " ==") -ForegroundColor Cyan
    $packArgs = @(
        "pack", $cliProject, "-c", $Configuration, "-o", $outDir,
        "--nologo", "-v", "q"
    )
    $packArgs += $variant.Extra
    & dotnet @packArgs
    if ($LASTEXITCODE -ne 0) { throw ("打包失败：" + $variant.Id) }
}

# 列出每个包的内容与体积
Add-Type -AssemblyName System.IO.Compression.FileSystem
Write-Host ""
Write-Host "== 产物 ==" -ForegroundColor Green

foreach ($pkg in (Get-ChildItem $outDir -Filter *.nupkg | Sort-Object Name)) {
    Write-Host ("")
    Write-Host ("  " + $pkg.Name + "   " + [math]::Round($pkg.Length / 1MB, 2) + " MB") -ForegroundColor Yellow

    $zip = [System.IO.Compression.ZipFile]::OpenRead($pkg.FullName)
    try {
        $plugins = $zip.Entries |
            Where-Object { $_.FullName -like "tools/*/any/plugins/*" } |
            ForEach-Object { ($_.FullName -split "plugins/")[1].Split("/")[0] } |
            Sort-Object -Unique
        $entryCount = $zip.Entries.Count
        $pluginCount = ($zip.Entries | Where-Object { $_.FullName -like "tools/*/any/plugins/*" }).Count

        if ($plugins.Count -eq 0) {
            Write-Host "     plugins: （无，仅离线能力）"
        } else {
            Write-Host ("     plugins: " + ($plugins -join ", ") + "  （" + $pluginCount + " 个文件）")
        }
        Write-Host ("     条目数 : " + $entryCount)
    }
    finally {
        $zip.Dispose()
    }
}

Write-Host ""
Write-Host "安装（本地源）：" -ForegroundColor Cyan
Write-Host ("  dotnet tool install -g " + $variants[2].Id + " --add-source " + $outDir)
Write-Host "然后："
Write-Host "  db-chronicle plugin list"
Write-Host "  db-chronicle plugin disable scanner-source    # 用不到源码扫描就关掉"
