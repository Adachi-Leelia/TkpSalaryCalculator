param(
    [string]$KeyStorePath = $env:TKP_ANDROID_KEYSTORE_PATH,

    [string]$KeyStorePassword = $env:TKP_ANDROID_KEYSTORE_PASSWORD,

    [string]$KeyAlias = $env:TKP_ANDROID_KEY_ALIAS,

    [string]$KeyPassword = $env:TKP_ANDROID_KEY_PASSWORD,

    [string]$AndroidSdkDirectory = $(if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { $env:ANDROID_HOME })
)

$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param([bool]$Condition, [string]$Message)

    if (-not $Condition) {
        throw $Message
    }
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repositoryRoot 'src\TkpSalaryCalculator.App\TkpSalaryCalculator.App.csproj'
$publishDirectory = Join-Path $repositoryRoot 'src\TkpSalaryCalculator.App\bin\Release\net10.0-android\publish'

Assert-Condition (Test-Path -LiteralPath $KeyStorePath) "Android signing keystore was not found: $KeyStorePath"
Assert-Condition (-not [string]::IsNullOrWhiteSpace($KeyStorePassword)) 'Android signing keystore password is required.'
Assert-Condition (-not [string]::IsNullOrWhiteSpace($KeyAlias)) 'Android signing key alias is required.'
Assert-Condition (-not [string]::IsNullOrWhiteSpace($KeyPassword)) 'Android signing key password is required.'
Assert-Condition (-not [string]::IsNullOrWhiteSpace($AndroidSdkDirectory)) 'ANDROID_SDK_ROOT (or -AndroidSdkDirectory) is required.'
Assert-Condition (Test-Path -LiteralPath $AndroidSdkDirectory) "Android SDK was not found: $AndroidSdkDirectory"

# Do not allow an APK left by an older build to be selected as the release
# candidate. The project also invalidates its signing output before _Sign.
if (Test-Path -LiteralPath $publishDirectory) {
    Get-ChildItem -LiteralPath $publishDirectory -Filter '*-Signed.apk' -File |
        Remove-Item -Force
}

& dotnet publish $projectPath --configuration Release --framework net10.0-android `
    "/p:AndroidSigningKeyStore=$KeyStorePath" `
    "/p:AndroidSigningStorePass=$KeyStorePassword" `
    "/p:AndroidSigningKeyAlias=$KeyAlias" `
    "/p:AndroidSigningKeyPass=$KeyPassword"
if ($LASTEXITCODE -ne 0) {
    throw 'Release APK build failed.'
}

$signedApks = @(Get-ChildItem -LiteralPath $publishDirectory -Filter '*-Signed.apk' -File)
Assert-Condition ($signedApks.Count -eq 1) "Expected exactly one signed APK in $publishDirectory, found $($signedApks.Count)."

$signedApk = $signedApks[0].FullName
& (Join-Path $repositoryRoot 'tests\TkpSalaryCalculator.App.Tests\verify-release-apk.ps1') `
    -ApkPath $signedApk `
    -AndroidSdkDirectory $AndroidSdkDirectory

[xml]$project = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
$version = $project.Project.PropertyGroup.ApplicationDisplayVersion | Select-Object -First 1
Assert-Condition (-not [string]::IsNullOrWhiteSpace($version)) 'ApplicationDisplayVersion is missing from the app project.'

$artifactDirectory = Join-Path $repositoryRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
$distributionApk = Join-Path $artifactDirectory "TkpSalaryCalculator-$version.apk"
Copy-Item -LiteralPath $signedApk -Destination $distributionApk -Force

Write-Output "Release APK created and verified: $distributionApk"
