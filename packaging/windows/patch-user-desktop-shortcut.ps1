param(
    [Parameter(Mandatory = $true)]
    [string]$MsiDirectory,

    [Parameter(Mandatory = $true)]
    [string]$AppName
)

$ErrorActionPreference = "Stop"

function SqlQuote([string]$Value) {
    return "'" + $Value.Replace("'", "''") + "'"
}

function Open-MsiView([object]$Database, [string]$Sql) {
    return $Database.GetType().InvokeMember(
        "OpenView",
        "InvokeMethod",
        $null,
        $Database,
        @($Sql)
    )
}

function Invoke-MsiSql([object]$Database, [string]$Sql) {
    $view = Open-MsiView $Database $Sql
    try {
        $view.GetType().InvokeMember("Execute", "InvokeMethod", $null, $view, $null) | Out-Null
    } finally {
        $view.GetType().InvokeMember("Close", "InvokeMethod", $null, $view, $null) | Out-Null
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($view) | Out-Null
    }
}

function Test-MsiRow([object]$Database, [string]$Sql) {
    $view = Open-MsiView $Database $Sql
    try {
        $view.GetType().InvokeMember("Execute", "InvokeMethod", $null, $view, $null) | Out-Null
        $record = $view.GetType().InvokeMember("Fetch", "InvokeMethod", $null, $view, $null)
        if ($null -ne $record) {
            [System.Runtime.InteropServices.Marshal]::ReleaseComObject($record) | Out-Null
            return $true
        }
        return $false
    } finally {
        $view.GetType().InvokeMember("Close", "InvokeMethod", $null, $view, $null) | Out-Null
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($view) | Out-Null
    }
}

function Get-MsiValue([object]$Database, [string]$Sql) {
    $view = Open-MsiView $Database $Sql
    try {
        $view.GetType().InvokeMember("Execute", "InvokeMethod", $null, $view, $null) | Out-Null
        $record = $view.GetType().InvokeMember("Fetch", "InvokeMethod", $null, $view, $null)
        if ($null -eq $record) {
            return $null
        }
        try {
            return $record.GetType().InvokeMember("StringData", "GetProperty", $null, $record, @(1))
        } finally {
            [System.Runtime.InteropServices.Marshal]::ReleaseComObject($record) | Out-Null
        }
    } finally {
        $view.GetType().InvokeMember("Close", "InvokeMethod", $null, $view, $null) | Out-Null
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($view) | Out-Null
    }
}

function Get-AppExeFileId([object]$Database, [string]$AppName) {
    $view = Open-MsiView $Database 'SELECT `File`, `FileName` FROM `File`'
    try {
        $view.GetType().InvokeMember("Execute", "InvokeMethod", $null, $view, $null) | Out-Null
        while ($record = $view.GetType().InvokeMember("Fetch", "InvokeMethod", $null, $view, $null)) {
            try {
                $fileId = $record.GetType().InvokeMember("StringData", "GetProperty", $null, $record, @(1))
                $fileName = $record.GetType().InvokeMember("StringData", "GetProperty", $null, $record, @(2))
                $longName = ($fileName -split "\|")[-1]
                if ($longName -ieq "$AppName.exe") {
                    return $fileId
                }
            } finally {
                [System.Runtime.InteropServices.Marshal]::ReleaseComObject($record) | Out-Null
            }
        }
    } finally {
        $view.GetType().InvokeMember("Close", "InvokeMethod", $null, $view, $null) | Out-Null
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($view) | Out-Null
    }

    throw "Could not find packaged executable for $AppName.exe"
}

$msi = Get-ChildItem -LiteralPath $MsiDirectory -Filter "*.msi" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $msi) {
    throw "No MSI file found in $MsiDirectory"
}

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $null

try {
    $database = $installer.GetType().InvokeMember(
        "OpenDatabase",
        "InvokeMethod",
        $null,
        $installer,
        @($msi.FullName, 1)
    )

    $productVersion = Get-MsiValue $database "SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = 'ProductVersion'"
    if ([string]::IsNullOrWhiteSpace($productVersion)) {
        $productVersion = "current"
    }

    $exeFileId = Get-AppExeFileId $database $AppName
    $desktopDirectoryId = "DesktopFolder"
    $componentId = "UserDesktopShortcut"
    $registryId = "UserDesktopShortcutRegistry"
    $shortcutId = "UserDesktopShortcut"
    $shortcutName = "WAFFLE~1|$AppName"
    $registryKey = "Software\Unknown\$AppName\$productVersion"

    if (-not (Test-MsiRow $database "SELECT ``Directory`` FROM ``Directory`` WHERE ``Directory`` = '$desktopDirectoryId'")) {
        Invoke-MsiSql $database "INSERT INTO ``Directory`` (``Directory``, ``Directory_Parent``, ``DefaultDir``) VALUES ('$desktopDirectoryId', 'TARGETDIR', 'Desktop')"
    }

    if (-not (Test-MsiRow $database "SELECT ``Component`` FROM ``Component`` WHERE ``Component`` = '$componentId'")) {
        Invoke-MsiSql $database "INSERT INTO ``Component`` (``Component``, ``ComponentId``, ``Directory_``, ``Attributes``, ``Condition``, ``KeyPath``) VALUES ('$componentId', '{D107CF65-E062-4534-AC22-1C8273EEC181}', '$desktopDirectoryId', 260, '', '$registryId')"
    }

    if (-not (Test-MsiRow $database "SELECT ``Registry`` FROM ``Registry`` WHERE ``Registry`` = '$registryId'")) {
        Invoke-MsiSql $database "INSERT INTO ``Registry`` (``Registry``, ``Root``, ``Key``, ``Name``, ``Value``, ``Component_``) VALUES ('$registryId', 1, $(SqlQuote $registryKey), 'ProductCode', '[ProductCode]', '$componentId')"
    }

    if (-not (Test-MsiRow $database "SELECT ``Shortcut`` FROM ``Shortcut`` WHERE ``Shortcut`` = '$shortcutId'")) {
        Invoke-MsiSql $database "INSERT INTO ``Shortcut`` (``Shortcut``, ``Directory_``, ``Name``, ``Component_``, ``Target``, ``Arguments``, ``Description``, ``Hotkey``, ``Icon_``, ``IconIndex``, ``ShowCmd``, ``WkDir``) VALUES ('$shortcutId', '$desktopDirectoryId', $(SqlQuote $shortcutName), '$componentId', '[#$exeFileId]', '', $(SqlQuote $AppName), 0, '', 0, 1, 'INSTALLDIR')"
    }

    if (-not (Test-MsiRow $database "SELECT ``Feature_`` FROM ``FeatureComponents`` WHERE ``Feature_`` = 'DefaultFeature' AND ``Component_`` = '$componentId'")) {
        Invoke-MsiSql $database "INSERT INTO ``FeatureComponents`` (``Feature_``, ``Component_``) VALUES ('DefaultFeature', '$componentId')"
    }

    $database.GetType().InvokeMember("Commit", "InvokeMethod", $null, $database, $null) | Out-Null
    Write-Host "Patched current-user desktop shortcut into $($msi.FullName)"
} finally {
    if ($null -ne $database) {
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($database) | Out-Null
    }
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($installer) | Out-Null
}
