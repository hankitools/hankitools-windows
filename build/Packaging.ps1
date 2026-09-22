if (-not ('Hanki.Build.ArchiveBuilder' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'ArchiveBuilder.cs') -ReferencedAssemblies 'System.IO.Compression','System.IO.Compression.FileSystem'
}
function New-HankiArchive {
    param([Parameter(Mandatory=$true)][string]$SourceDirectory,[Parameter(Mandatory=$true)][string]$DestinationZip)
    [Hanki.Build.ArchiveBuilder]::Create($SourceDirectory,$DestinationZip)
}
