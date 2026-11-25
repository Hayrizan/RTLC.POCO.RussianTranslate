param(
    [string]$UnityEditor = 'E:/Programms/Unity2021.1.4f1/Editor/Unity.exe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$editorPath = (Resolve-Path -LiteralPath $UnityEditor).Path
$project = Join-Path ([IO.Path]::GetTempPath()) ('PocoBundle-' + [guid]::NewGuid())
$output = Join-Path $root 'build'
$log = Join-Path $output 'unity-bundle.log'
New-Item -ItemType Directory -Force "$project/Assets/Editor", "$project/Assets/Content/Textures", "$project/Packages", $output | Out-Null
try {
    '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0"}}' | Set-Content -LiteralPath "$project/Packages/manifest.json" -Encoding utf8
    Copy-Item -LiteralPath "$PSScriptRoot/PocoBundleBuilder.cs" -Destination "$project/Assets/Editor/"
    Copy-Item -LiteralPath "$root/content/Russian.json" -Destination "$project/Assets/Content/Russian.bytes"
    Copy-Item -LiteralPath "$root/content/Poco-Cyrillic-v1.004-Regular.ttf" -Destination "$project/Assets/Content/"
    Get-ChildItem -LiteralPath "$root/content/Textures" -Filter '*.png' -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination "$project/Assets/Content/Textures/$($_.Name).bytes"
    }
    $unityArguments = '-batchmode -nographics -quit -buildTarget Win64 -projectPath "{0}" -executeMethod PocoBundleBuilder.Build -logFile "{1}"' -f $project, $log
    $editor = Start-Process -FilePath $editorPath -ArgumentList $unityArguments -WindowStyle Hidden -Wait -PassThru
    if ($editor.ExitCode -ne 0 -or !(Test-Path -LiteralPath "$project/Output/rtlc.bundle")) {
        throw "AssetBundle build failed. See $log"
    }
    Copy-Item -LiteralPath "$project/Output/rtlc.bundle" -Destination "$output/rtlc.bundle"
    Write-Output "AssetBundle: $output/rtlc.bundle"
}
finally {
    $resolved = (Resolve-Path -LiteralPath $project).Path
    $temp = (Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).Path.TrimEnd('\')
    if ($resolved.StartsWith("$temp\PocoBundle-", [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
