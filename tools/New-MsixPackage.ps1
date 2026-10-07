<#
.SYNOPSIS
    Packages a published ShellDeck (dotnet publish --self-contained) as an MSIX for the Microsoft Store.
.DESCRIPTION
    Fills packaging/AppxManifest.xml with the Store identity, adds the logos of packaging/Assets and runs makeappx
    from the Windows SDK. The Store signs the package itself on submission: the result is left unsigned, except with
    -TestCertificate, which signs it with a self-signed certificate to install it on this machine before submitting.
.EXAMPLE
    .\tools\New-MsixPackage.ps1 -PublishDirectory publish\win-x64 -Version 0.2.0 -Architecture x64 `
        -IdentityName 12345Me.ShellDeck -Publisher 'CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX' -PublisherDisplayName 'Me' `
        -DisplayName 'ShellDeck'
#>
param(
    [Parameter(Mandatory)] [string]$PublishDirectory,
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [ValidateSet('x64', 'arm64')] [string]$Architecture,
    # Partner Center › Product management › Product identity: Package/Identity/Name.
    [Parameter(Mandatory)] [string]$IdentityName,
    # Same page: Package/Identity/Publisher, e.g. CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX.
    [Parameter(Mandatory)] [string]$Publisher,
    # Same page: Package/Properties/PublisherDisplayName.
    [Parameter(Mandatory)] [string]$PublisherDisplayName,
    # The name reserved in Partner Center, shown in the Store and the Start menu.
    [Parameter(Mandatory)] [string]$DisplayName,
    [string]$OutputPath = "ShellDeck-$Version-win-$Architecture.msix",
    [switch]$TestCertificate
)

$ErrorActionPreference = 'Stop'
$packagingDirectory = Join-Path $PSScriptRoot '..\packaging'

function Find-SdkTool([string]$name) {
    $tool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$name" -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
        Select-Object -First 1
    if (-not $tool) { throw "$name not found: install the Windows SDK (winget install Microsoft.WindowsSDK.10.0.26100)." }
    return $tool.FullName
}

# The Store wants four parts with a zero revision: 0.2.0 becomes 0.2.0.0.
$parts = @($Version.Split('.') | ForEach-Object { [int]$_ })
while ($parts.Count -lt 4) { $parts += 0 }
if ($parts.Count -gt 4 -or $parts[3] -ne 0) { throw "Version ${Version}: the Store needs at most three parts (the fourth, revision, must be 0)." }
$packageVersion = $parts -join '.'

$staging = Join-Path ([System.IO.Path]::GetTempPath()) "shelldeck-msix-$([guid]::NewGuid())"
try {
    Copy-Item -Path $PublishDirectory -Destination $staging -Recurse
    Copy-Item -Path (Join-Path $packagingDirectory 'Assets') -Destination (Join-Path $staging 'Assets') -Recurse

    $escape = { param($value) [System.Security.SecurityElement]::Escape($value) }
    $manifest = (Get-Content -Raw -Encoding UTF8 (Join-Path $packagingDirectory 'AppxManifest.xml')).
        Replace('{{IdentityName}}', (& $escape $IdentityName)).
        Replace('{{Publisher}}', (& $escape $Publisher)).
        Replace('{{PublisherDisplayName}}', (& $escape $PublisherDisplayName)).
        Replace('{{DisplayName}}', (& $escape $DisplayName)).
        Replace('{{Version}}', $packageVersion).
        Replace('{{Architecture}}', $Architecture)
    [System.IO.File]::WriteAllText((Join-Path $staging 'AppxManifest.xml'), $manifest, (New-Object System.Text.UTF8Encoding $false))

    & (Find-SdkTool 'makeappx.exe') pack /d $staging /p $OutputPath /o /nv | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makeappx failed with exit code $LASTEXITCODE." }
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}

if ($TestCertificate) {
    # Reused across runs: trusting it once is enough. Its subject must equal the manifest's Publisher.
    $certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Publisher -and $_.FriendlyName -eq 'ShellDeck test' } | Select-Object -First 1
    if (-not $certificate) {
        $certificate = New-SelfSignedCertificate -Type Custom -Subject $Publisher -KeyUsage DigitalSignature -FriendlyName 'ShellDeck test' `
            -CertStoreLocation Cert:\CurrentUser\My -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    }

    & (Find-SdkTool 'signtool.exe') sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $OutputPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE." }

    $certificatePath = [System.IO.Path]::ChangeExtension($OutputPath, '.cer')
    Export-Certificate -Cert $certificate -FilePath $certificatePath | Out-Null
    Write-Output "Signed with a test certificate. To install, once, from an administrator PowerShell:"
    Write-Output "  Import-Certificate -FilePath '$((Resolve-Path $certificatePath).ProviderPath)' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
    Write-Output "then: Add-AppxPackage '$((Resolve-Path $OutputPath).ProviderPath)'"
}

Write-Output "Package written: $OutputPath (version $packageVersion, $Architecture)"
