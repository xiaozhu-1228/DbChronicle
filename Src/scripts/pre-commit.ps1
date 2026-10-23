# 安装（Git for Windows）：
#   Copy-Item Src\scripts\pre-commit.ps1 .git\hooks\pre-commit -Force
# 或在 .git\hooks\pre-commit 中调用本脚本。

$ErrorActionPreference = 'Stop'
$config = if ($args.Length -gt 0) { $args[0] } else { 'dbchronicle.config.yaml' }

if (-not (Test-Path $config)) {
    Write-Host "db-chronicle: 未找到 $config，跳过校验"
    exit 0
}

if (-not (Get-Command db-chronicle -ErrorAction SilentlyContinue)) {
    Write-Host "db-chronicle: 未安装 CLI（dotnet tool install -g DbChronicle.Cli），跳过硬校验"
    exit 0
}

Write-Host "db-chronicle: 校验变更单元..."
db-chronicle check --config $config
exit $LASTEXITCODE
