param(
    [Parameter(Mandatory = $true)]
    [string]$ApkPath,

    [Parameter(Mandatory = $true)]
    [string]$AndroidSdkDirectory
)

$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Condition,
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$resolvedApk = (Resolve-Path -LiteralPath $ApkPath).Path
$buildToolsDirectory = Join-Path $AndroidSdkDirectory 'build-tools'
$aapt2 = Get-ChildItem -LiteralPath $buildToolsDirectory -Directory |
    Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName 'aapt2.exe' } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
Assert-Condition ($null -ne $aapt2) 'aapt2.exe was not found in the Android SDK.'

function Invoke-Aapt2Dump {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = & $aapt2 @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "aapt2 failed to inspect the APK: $($output -join [Environment]::NewLine)"
    }
    return $output -join [Environment]::NewLine
}

$permissions = Invoke-Aapt2Dump -Arguments @('dump', 'permissions', $resolvedApk)
$manifest = Invoke-Aapt2Dump -Arguments @('dump', 'xmltree', $resolvedApk, '--file', 'AndroidManifest.xml')
$backupRules = Invoke-Aapt2Dump -Arguments @('dump', 'xmltree', $resolvedApk, '--file', 'res/xml/backup_rules.xml')
$extractionRules = Invoke-Aapt2Dump -Arguments @('dump', 'xmltree', $resolvedApk, '--file', 'res/xml/data_extraction_rules.xml')

$packageMatch = [regex]::Match($permissions, 'package:\s+(?<name>\S+)')
Assert-Condition $packageMatch.Success 'Could not read the APK package name.'
$packageName = $packageMatch.Groups['name'].Value
$allowedPermission = "$packageName.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION"
$declaredPermissions = [regex]::Matches($permissions, "uses-permission:\s+name='(?<name>[^']+)'") |
    ForEach-Object { $_.Groups['name'].Value }
$unexpectedPermissions = @($declaredPermissions | Where-Object { $_ -ne $allowedPermission })
Assert-Condition ($unexpectedPermissions.Count -eq 0) `
    "Release APK contains unexpected permissions: $($unexpectedPermissions -join ', ')"

Assert-Condition ($manifest -match 'minSdkVersion[^\r\n]*=29') 'Release APK minSdkVersion is not 29.'
Assert-Condition ($manifest -match 'allowBackup[^\r\n]*=false') 'Release APK does not set allowBackup=false.'
Assert-Condition ($manifest -match 'usesCleartextTraffic[^\r\n]*=false') 'Release APK does not set usesCleartextTraffic=false.'
Assert-Condition ($manifest -match 'fullBackupContent[^\r\n]*=@') 'Release APK has no fullBackupContent reference.'
Assert-Condition ($manifest -match 'dataExtractionRules[^\r\n]*=@') 'Release APK has no dataExtractionRules reference.'
Assert-Condition ($manifest -notmatch 'screenOrientation') 'Release APK locks the screen orientation.'

$domains = @('root', 'file', 'database', 'sharedpref', 'external')
foreach ($domain in $domains) {
    $backupPattern = "(?s)A: domain=`"$domain`".*?A: path=`"\.`""
    Assert-Condition ([regex]::IsMatch($backupRules, $backupPattern)) `
        "fullBackupContent does not exclude all $domain data."

    $extractionCount = [regex]::Matches($extractionRules, "A: domain=`"$domain`"").Count
    Assert-Condition ($extractionCount -eq 2) `
        "dataExtractionRules does not exclude all $domain data from both transfer modes."
}
Assert-Condition ($extractionRules -match 'E: cloud-backup') 'APK has no cloud-backup rule.'
Assert-Condition ($extractionRules -match 'E: device-transfer') 'APK has no device-transfer rule.'

Write-Output "Release APK verification passed: $resolvedApk"
Write-Output "Declared uses-permission: $($declaredPermissions -join ', ')"
