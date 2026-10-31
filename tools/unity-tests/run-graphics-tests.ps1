param(
    [Parameter(Mandatory = $true)][string]$Editor,
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$Filter,
    [Parameter(Mandatory = $true)][string]$Report,
    [Parameter(Mandatory = $true)][string]$Log,
    [ValidateSet("EditMode", "PlayMode")][string]$Mode = "PlayMode"
)
$ErrorActionPreference = 'Stop'
# run-tests.sh checks the project, editor version, and absence of an open Editor first.
# Unity Test Framework exits this test process on completion; never terminate by PID.
$arguments = @('-automated', '-runTests', '-testPlatform', $Mode, '-testFilter', $Filter,
    '-testResults', $Report, '-projectPath', $Project, '-logFile', $Log)
$quoted = $arguments | ForEach-Object {
    if ($_.Contains('"')) { throw 'A graphics test argument contains an unsupported quote.' }
    '"' + $_ + '"'
}
$testProcess = Start-Process -FilePath $Editor -ArgumentList ($quoted -join ' ') -WorkingDirectory $Project -WindowStyle Hidden -PassThru
$testProcess.WaitForExit()
exit $testProcess.ExitCode
